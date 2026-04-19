using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.Adapters;
using ContextRelay.Core.Auth;
using ContextRelay.Core.Auth.Msal;
using ContextRelay.Core.Cache;
using ContextRelay.Core.Handoff;
using ContextRelay.Core.Models;
using ContextRelay.Core.Router;
using ContextRelay.Core.SharedStore;
using ContextRelay.Core.Snippets;
using ContextRelay.VSExtension.Options;
using Microsoft.VisualStudio.Shell;

namespace ContextRelay.VSExtension.Services;

internal sealed class ContextRelayHost : IDisposable
{
    private readonly ContextRelayPackage package;
    private readonly ContextRelayOutputLogger logger;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly IContextRelayAuthProvider authProvider;
    private readonly FileSystemSharedSessionStore sharedStore;
    private readonly SharedStoreWatcher watcher;
    private readonly SharedSnippetRepository snippetRepository;
    private readonly GraphHttpClient graphClient;
    private readonly Dictionary<ContextSource, IContextSearchAdapter> adapters;
    private readonly ICopilotChatAdapter copilotChatAdapter;
    private readonly HandoffDocumentGenerator handoffDocumentGenerator;
    private TtlLruCache<string, ContextItem[]> searchCache = new();
    private ContextRelayHostState state = new();
    private string? lastSearchSummary;
    private string? cacheWorkspaceRoot;
    private int cacheTtlSeconds = 300;
    private int cacheMaxEntries = 200;
    private ContextItem[] currentSearchResults = Array.Empty<ContextItem>();

    public ContextRelayHost(ContextRelayPackage package, ContextRelayOutputLogger logger)
    {
        this.package = package;
        this.logger = logger;
        authProvider = new MsalAuthProvider();

        var sharedStoreOptions = SharedStoreOptions.CreateDefault("vs", package.ExtensionVersion);
        sharedStore = new FileSystemSharedSessionStore(sharedStoreOptions);
        watcher = new SharedStoreWatcher(sharedStoreOptions.RootDirectory, sharedStoreOptions.WatcherDebounceMilliseconds);
        watcher.Changed += OnSharedStoreChanged;
        snippetRepository = new SharedSnippetRepository(sharedStore, watcher, ownsWatcher: false);

        graphClient = new GraphHttpClient(new System.Net.Http.HttpClient(), logger);
        adapters = new Dictionary<ContextSource, IContextSearchAdapter>
        {
            [ContextSource.Mail] = new MailSearchAdapter(graphClient),
            [ContextSource.Teams] = new TeamsSearchAdapter(graphClient),
            [ContextSource.SharePoint] = new RetrievalSearchAdapter(RetrievalDataSource.SharePoint, graphClient),
            [ContextSource.OneDrive] = new RetrievalSearchAdapter(RetrievalDataSource.OneDriveBusiness, graphClient),
            [ContextSource.Connectors] = new RetrievalSearchAdapter(RetrievalDataSource.ExternalItem, graphClient)
        };
        copilotChatAdapter = new CopilotChatAdapter(graphClient);
        handoffDocumentGenerator = new HandoffDocumentGenerator(sharedStore);
    }

    public event EventHandler<ContextRelayStateChangedEventArgs>? StateChanged;

    public async Task InitializeAsync()
    {
        await RefreshStateAsync("ContextRelay is ready.").ConfigureAwait(false);
    }

    public async Task<ContextRelayHostState> GetStateAsync()
    {
        await RefreshStateAsync(state.StatusMessage).ConfigureAwait(false);
        return state;
    }

    public async Task<ContextRelayHostState> SubmitQueryAsync(string input, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var trimmed = input?.Trim() ?? string.Empty;
            var route = SlashCommandRouter.Parse(trimmed);
            state.QueryText = trimmed;

            if (route.Target == RouteTarget.Clear)
            {
                await sharedStore.ClearAsync(SharedStoreFileKind.ChatHistory, cancellationToken).ConfigureAwait(false);
                await snippetRepository.ClearAsync(cancellationToken).ConfigureAwait(false);
                currentSearchResults = Array.Empty<ContextItem>();
                lastSearchSummary = "Chat and snippets were cleared.";
                return await RefreshStateCoreAsync("Chat and snippets cleared.", trimmed, cancellationToken).ConfigureAwait(false);
            }

            if (route.IsEmpty)
            {
                return await RefreshStateCoreAsync("Type a query to search Microsoft 365 content.", trimmed, cancellationToken).ConfigureAwait(false);
            }

            var settings = await package.GetSettingsSnapshotAsync(cancellationToken).ConfigureAwait(false);
            logger.SetDebugLoggingEnabled(settings.EnableGraphDebugLogging);
            await EnsureCacheReadyAsync(settings, cancellationToken).ConfigureAwait(false);
            var enabledSources = GetEnabledSources(route, settings);
            if (enabledSources.Count == 0 && route.Target != RouteTarget.Ask)
            {
                return await RefreshStateCoreAsync("The requested source is disabled in ContextRelay options.", trimmed, cancellationToken).ConfigureAwait(false);
            }

            var authSettings = settings.ToAuthSettings();
            var featureOptions = settings.ToFeatureOptions();
            ContextRelayAccessToken token;
            try
            {
                token = await authProvider.GetAccessTokenAsync(authSettings, featureOptions, cancellationToken).ConfigureAwait(false);
            }
            catch (ContextRelayAuthenticationException ex)
            {
                logger.LogError("Authentication failed.", ex);
                return await RefreshStateCoreAsync(ex.Message, trimmed, cancellationToken).ConfigureAwait(false);
            }

            if (route.Target == RouteTarget.Ask)
            {
                if (!settings.EnableChatPreview)
                {
                    return await RefreshStateCoreAsync("/ask is disabled. Enable chat preview in ContextRelay options.", trimmed, cancellationToken).ConfigureAwait(false);
                }

                var prompt = await BuildAskPromptAsync(route.Query, cancellationToken).ConfigureAwait(false);
                var reply = await copilotChatAdapter.AskAsync(token.AccessToken, prompt, cancellationToken).ConfigureAwait(false);
                await AppendChatHistoryAsync(route.Query, reply, cancellationToken).ConfigureAwait(false);
                currentSearchResults = Array.Empty<ContextItem>();
                lastSearchSummary = reply;
                logger.LogInformation("Handled /ask with Microsoft 365 Copilot.");
                return await RefreshStateCoreAsync("Microsoft 365 Copilot response added to chat.", trimmed, cancellationToken).ConfigureAwait(false);
            }

            var tasks = enabledSources
                .Select(source => SearchSourceAsync(source, token.AccessToken, route.Query, settings.MaxResults, settings.CacheTtlSeconds, cancellationToken))
                .ToArray();
            var results = (await Task.WhenAll(tasks).ConfigureAwait(false))
                .SelectMany(items => items)
                .OrderByDescending(item => ParseTimestamp(item.Timestamp))
                .ThenBy(item => item.Source)
                .Take(settings.MaxResults * Math.Max(1, enabledSources.Count))
                .ToArray();

            currentSearchResults = results;
            lastSearchSummary = BuildSearchSummary(route, results);
            await AppendSearchHistoryAsync(trimmed, results, cancellationToken).ConfigureAwait(false);
            await PersistCacheIfNeededAsync(settings, cancellationToken).ConfigureAwait(false);
            logger.LogInformation($"Search completed with {results.Length} result(s).");
            return await RefreshStateCoreAsync(
                results.Length == 0 ? "No results found." : $"Found {results.Length} result(s).",
                trimmed,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError("Search execution failed.", ex);
            return await RefreshStateCoreAsync(ex.Message, input?.Trim() ?? string.Empty, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ContextRelayHostState> PinSnippetAsync(ContextItem item, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await snippetRepository.GetAllAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            var itemKey = ContextItemKeys.Build(item);
            if (existing.Any(snippet => snippet.Metadata.TryGetValue("contextItemKey", out var _) &&
                snippet.Metadata.ContainsKey("contextItemKey") &&
                snippet.Metadata["contextItemKey"].GetString() == itemKey))
            {
                return await RefreshStateCoreAsync("This result is already pinned.", state.QueryText, cancellationToken).ConfigureAwait(false);
            }

            var metadata = new Dictionary<string, JsonElement>
            {
                ["contextItemKey"] = JsonDocument.Parse($"\"{itemKey}\"").RootElement.Clone()
            };
            await snippetRepository.SaveAsync(new SaveSnippetRequest
            {
                Name = item.Title,
                Source = ToSnippetSource(item.Source),
                SourceUrl = item.Url,
                Snippet = item.Snippet,
                Metadata = metadata
            }, cancellationToken).ConfigureAwait(false);

            logger.LogInformation($"Pinned snippet '{item.Title}'.");
            return await RefreshStateCoreAsync("Result pinned to snippets.", state.QueryText, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ContextRelayHostState> DeleteSnippetAsync(string id, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await snippetRepository.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
            return await RefreshStateCoreAsync("Snippet removed.", state.QueryText, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ContextRelayHostState> ClearSnippetsAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await snippetRepository.ClearAsync(cancellationToken).ConfigureAwait(false);
            return await RefreshStateCoreAsync("All snippets cleared.", state.QueryText, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ContextRelayHostState> ClearChatAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await sharedStore.ClearAsync(SharedStoreFileKind.ChatHistory, cancellationToken).ConfigureAwait(false);
            return await RefreshStateCoreAsync("Chat history cleared.", state.QueryText, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ContextRelayHostState> ClearCacheAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            searchCache.Clear();
            var settings = await package.GetSettingsSnapshotAsync(cancellationToken).ConfigureAwait(false);
            logger.SetDebugLoggingEnabled(settings.EnableGraphDebugLogging);
            await PersistCacheIfNeededAsync(settings, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Search cache cleared.");
            return await RefreshStateCoreAsync("Search cache cleared.", state.QueryText, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ContextRelayHostState> GenerateHandoffAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await EnsureHandoffDocsAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation($"Generated handoff docs in '{result.OutputDirectory}'.");
            return await RefreshStateCoreAsync($"Handoff docs updated ({result.WrittenFiles.Count} files).", state.QueryText, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task CopyHandoffPromptAsync(CancellationToken cancellationToken = default)
    {
        var handoffPath = await EnsureHandoffDocPathAsync(cancellationToken).ConfigureAwait(false);
        var prompt = BuildHandoffPrompt(handoffPath);
        await package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        System.Windows.Clipboard.SetText(prompt);
        logger.LogInformation("Handoff prompt copied to clipboard.");
        await RefreshStateAsync("Handoff prompt copied to clipboard.").ConfigureAwait(false);
    }

    public async Task OpenHandoffDocumentAsync(CancellationToken cancellationToken = default)
    {
        var handoffPath = await EnsureHandoffDocPathAsync(cancellationToken).ConfigureAwait(false);
        await package.OpenDocumentAsync(handoffPath, cancellationToken).ConfigureAwait(false);
        await RefreshStateAsync("Opened HANDOFF.md.").ConfigureAwait(false);
    }

    public async Task OpenCopilotChatWithPromptAsync(CancellationToken cancellationToken = default)
    {
        await CopyHandoffPromptAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Soft handoff prepared for Copilot. Prompt copied to clipboard.");
        await RefreshStateAsync("Prompt copied. Paste it into GitHub Copilot Chat in Visual Studio.").ConfigureAwait(false);
    }

    public void ShowDebugLog()
    {
        logger.ShowDebugPane();
    }

    public void OpenSettings()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        package.OpenSettings();
    }

    public void OpenExternalUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    public void Dispose()
    {
        watcher.Changed -= OnSharedStoreChanged;
        snippetRepository.Dispose();
        watcher.Dispose();
        gate.Dispose();
    }

    private async Task<IReadOnlyList<ContextItem>> SearchSourceAsync(
        ContextSource source,
        string accessToken,
        string query,
        int maxResults,
        int ttlSeconds,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"{source}|{maxResults}|{query}";
        if (searchCache.TryGetValue(cacheKey, out var cached))
        {
            var storedAt = searchCache.GetStoredAt(cacheKey);
            return CloneItems(cached, true, storedAt, ttlSeconds);
        }

        var results = await adapters[source].SearchAsync(accessToken, query, maxResults, cancellationToken).ConfigureAwait(false);
        var stored = results.Select(CloneItem).ToArray();
        searchCache.Set(cacheKey, stored);
        return CloneItems(stored, false, searchCache.GetStoredAt(cacheKey), ttlSeconds);
    }

    private async Task EnsureCacheReadyAsync(ContextRelaySettingsSnapshot settings, CancellationToken cancellationToken)
    {
        var workspaceRoot = settings.PersistWorkspaceState ? await package.GetSolutionRootAsync(cancellationToken).ConfigureAwait(false) : null;
        if (searchCache.Count > 0 &&
            workspaceRoot == cacheWorkspaceRoot &&
            cacheTtlSeconds == settings.CacheTtlSeconds &&
            cacheMaxEntries == settings.CacheMaxEntries)
        {
            return;
        }

        cacheTtlSeconds = settings.CacheTtlSeconds;
        cacheMaxEntries = settings.CacheMaxEntries;
        cacheWorkspaceRoot = workspaceRoot;
        searchCache = new TtlLruCache<string, ContextItem[]>(
            new TtlLruCacheOptions
            {
                TimeToLive = TimeSpan.FromSeconds(Math.Max(0, settings.CacheTtlSeconds)),
                MaxEntries = Math.Max(1, settings.CacheMaxEntries)
            },
            snapshot: await LoadCacheSnapshotAsync(workspaceRoot, settings, cancellationToken).ConfigureAwait(false));
    }

    private async Task<IReadOnlyList<TtlLruCacheSnapshotEntry<string, ContextItem[]>>> LoadCacheSnapshotAsync(
        string? workspaceRoot,
        ContextRelaySettingsSnapshot settings,
        CancellationToken cancellationToken)
    {
        if (!settings.PersistWorkspaceState || string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return Array.Empty<TtlLruCacheSnapshotEntry<string, ContextItem[]>>();
        }

        var cachePath = GetCacheFilePath(workspaceRoot!);
        if (!File.Exists(cachePath))
        {
            return Array.Empty<TtlLruCacheSnapshotEntry<string, ContextItem[]>>();
        }

        try
        {
            using var stream = File.OpenRead(cachePath);
            var file = await JsonSerializer.DeserializeAsync<CacheSnapshotFile>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return file?.Entries ?? Array.Empty<TtlLruCacheSnapshotEntry<string, ContextItem[]>>();
        }
        catch (JsonException ex)
        {
            logger.LogWarning($"Ignoring corrupt cache snapshot '{cachePath}': {ex.Message}");
            return Array.Empty<TtlLruCacheSnapshotEntry<string, ContextItem[]>>();
        }
    }

    private async Task PersistCacheIfNeededAsync(ContextRelaySettingsSnapshot settings, CancellationToken cancellationToken)
    {
        if (!settings.PersistWorkspaceState || string.IsNullOrWhiteSpace(cacheWorkspaceRoot))
        {
            return;
        }

        var cachePath = GetCacheFilePath(cacheWorkspaceRoot!);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        using var stream = new FileStream(cachePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(
            stream,
            new CacheSnapshotFile { Entries = searchCache.ExportSnapshot().ToArray() },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> EnsureHandoffDocPathAsync(CancellationToken cancellationToken)
    {
        var result = await EnsureHandoffDocsAsync(cancellationToken).ConfigureAwait(false);
        return result.HandoffPath ?? result.PlanPath;
    }

    private async Task<HandoffGenerationResult> EnsureHandoffDocsAsync(CancellationToken cancellationToken)
    {
        var workspaceRoot = await package.GetSolutionRootAsync(cancellationToken).ConfigureAwait(false);
        var settings = await package.GetSettingsSnapshotAsync(cancellationToken).ConfigureAwait(false);
        logger.SetDebugLoggingEnabled(settings.EnableGraphDebugLogging);
        var snippets = await snippetRepository.GetAllAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var fallbackRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return await handoffDocumentGenerator.GenerateAsync(
            new HandoffContext
            {
                Snippets = snippets,
                SearchSummary = lastSearchSummary
            },
            new HandoffGenerationOptions
            {
                OutputDirectory = string.IsNullOrWhiteSpace(settings.OutputDirectory) ? ".contextrelay" : settings.OutputDirectory,
                WorkspaceRoot = workspaceRoot,
                FallbackRootDirectory = fallbackRoot,
                IncludeHandoffDocument = true
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task AppendSearchHistoryAsync(string query, IReadOnlyList<ContextItem> results, CancellationToken cancellationToken)
    {
        var response = results.Count == 0
            ? "No results found."
            : $"Found {results.Count} result(s): {string.Join(", ", results.Take(3).Select(item => item.Title))}";

        await sharedStore.AppendChatHistoryAsync(new[]
        {
            CreateChatItem("user", query),
            CreateChatItem("assistant", response)
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task AppendChatHistoryAsync(string userPrompt, string assistantReply, CancellationToken cancellationToken)
    {
        await sharedStore.AppendChatHistoryAsync(new[]
        {
            CreateChatItem("user", userPrompt),
            CreateChatItem("assistant", assistantReply)
        }, cancellationToken).ConfigureAwait(false);
    }

    private SharedChatHistoryItem CreateChatItem(string role, string text)
    {
        return new SharedChatHistoryItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Role = role,
            Text = text,
            Timestamp = DateTimeOffset.UtcNow.ToString("O")
        };
    }

    private async Task<string> BuildAskPromptAsync(string instruction, CancellationToken cancellationToken)
    {
        var snippets = await snippetRepository.GetAllAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (snippets.Count == 0)
        {
            return instruction;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Use the following pinned snippets as context.");
        builder.AppendLine();

        foreach (var snippet in snippets)
        {
            builder.AppendLine($"### {snippet.Name}");
            builder.AppendLine($"- Source: {snippet.Source}");
            if (!string.IsNullOrWhiteSpace(snippet.SourceUrl))
            {
                builder.AppendLine($"- Link: {snippet.SourceUrl}");
            }

            builder.AppendLine();
            builder.AppendLine(snippet.Snippet);
            builder.AppendLine();
        }

        builder.AppendLine("User request:");
        builder.AppendLine(instruction);
        return builder.ToString().TrimEnd();
    }

    private async Task RefreshStateAsync(string statusMessage)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await RefreshStateCoreAsync(statusMessage, state.QueryText, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ContextRelayHostState> RefreshStateCoreAsync(string statusMessage, string queryText, CancellationToken cancellationToken)
    {
        var chatHistory = await sharedStore.GetChatHistoryAsync(cancellationToken).ConfigureAwait(false);
        var snippets = await snippetRepository.GetAllAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var workspaceRoot = await package.GetSolutionRootAsync(cancellationToken).ConfigureAwait(false);
        var handoffIndex = await sharedStore.GetHandoffIndexAsync(cancellationToken).ConfigureAwait(false);
        var signedInUser = await TryGetSignedInUserAsync(cancellationToken).ConfigureAwait(false);

        state = new ContextRelayHostState
        {
            QueryText = queryText,
            HelpText = SlashCommandRouter.GetHelpText(ExtractCommand(queryText) ?? string.Empty),
            StatusMessage = statusMessage,
            SignedInUser = signedInUser,
            LastHandoffPath = ResolveHandoffPath(workspaceRoot, handoffIndex),
            SearchResults = currentSearchResults,
            Snippets = snippets,
            ChatHistory = chatHistory
        };

        StateChanged?.Invoke(this, new ContextRelayStateChangedEventArgs(state));
        return state;
    }

    private async Task<string?> TryGetSignedInUserAsync(CancellationToken cancellationToken)
    {
        var settings = await package.GetSettingsSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(settings.ClientId))
        {
            return null;
        }

        try
        {
            var account = await authProvider.GetAccountAsync(settings.ToAuthSettings(), cancellationToken).ConfigureAwait(false);
            return account?.Username;
        }
        catch (Exception ex)
        {
            logger.LogWarning($"Unable to read cached account: {ex.Message}");
            return null;
        }
    }

    private static IReadOnlyList<ContextSource> GetEnabledSources(SlashCommandParseResult route, ContextRelaySettingsSnapshot settings)
    {
        var sources = new List<ContextSource>();
        foreach (var source in route.TargetSources)
        {
            if (IsEnabled(source, settings))
            {
                sources.Add(source);
            }
        }

        if (route.Target == RouteTarget.All && settings.ConnectorsEnabled)
        {
            sources.Add(ContextSource.Connectors);
        }

        return sources;
    }

    private static bool IsEnabled(ContextSource source, ContextRelaySettingsSnapshot settings)
    {
        return source switch
        {
            ContextSource.Mail => settings.MailEnabled,
            ContextSource.Teams => settings.TeamsEnabled,
            ContextSource.SharePoint => settings.SharePointEnabled,
            ContextSource.OneDrive => settings.OneDriveEnabled,
            ContextSource.Connectors => settings.ConnectorsEnabled,
            _ => false
        };
    }

    private static string BuildSearchSummary(SlashCommandParseResult route, IReadOnlyList<ContextItem> results)
    {
        if (results.Count == 0)
        {
            return $"No results found for '{route.Query}'.";
        }

        return $"Query '{route.Query}' returned {results.Count} result(s): {string.Join(", ", results.Take(5).Select(item => item.Title))}.";
    }

    private static string BuildHandoffPrompt(string handoffPath)
    {
        return string.Join(
            Environment.NewLine,
            "Please review the handoff document at the path below and help me continue development.",
            $"Handoff document: {handoffPath}",
            string.Empty,
            "Use the context in HANDOFF.md to understand current decisions, open questions,",
            "next tasks, and relevant snippets from Microsoft 365. Then help me plan next steps.");
    }

    private static string? ExtractCommand(string queryText)
    {
        if (!queryText.StartsWith("/", StringComparison.Ordinal))
        {
            return null;
        }

        var whitespaceIndex = queryText.IndexOf(' ');
        return whitespaceIndex >= 0 ? queryText.Substring(0, whitespaceIndex) : queryText;
    }

    private static string? ResolveHandoffPath(string? workspaceRoot, IReadOnlyList<SharedHandoffIndexItem> handoffIndex)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return null;
        }

        var match = handoffIndex.FirstOrDefault(item => string.Equals(item.WorkspaceRoot, workspaceRoot, StringComparison.OrdinalIgnoreCase));
        if (match?.Docs?.Handoff is { Length: > 0 } relativeHandoff)
        {
            return Path.Combine(workspaceRoot, relativeHandoff.Replace('/', Path.DirectorySeparatorChar));
        }

        return null;
    }

    private static SnippetSource ToSnippetSource(ContextSource source)
    {
        return source switch
        {
            ContextSource.Mail => SnippetSource.Mail,
            ContextSource.Teams => SnippetSource.Teams,
            ContextSource.SharePoint => SnippetSource.SharePoint,
            ContextSource.OneDrive => SnippetSource.OneDrive,
            ContextSource.Connectors => SnippetSource.Connectors,
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unsupported snippet source.")
        };
    }

    private static ContextItem[] CloneItems(IEnumerable<ContextItem> items, bool cacheHit, DateTimeOffset? storedAt, int ttlSeconds)
    {
        return items.Select(item =>
        {
            var clone = CloneItem(item);
            clone.Cache = new ContextItemCacheInfo
            {
                Hit = cacheHit,
                StoredAt = storedAt?.ToString("O"),
                TtlSeconds = ttlSeconds
            };
            return clone;
        }).ToArray();
    }

    private static ContextItem CloneItem(ContextItem item)
    {
        return new ContextItem
        {
            Source = item.Source,
            Title = item.Title,
            Snippet = item.Snippet,
            Url = item.Url,
            Timestamp = item.Timestamp,
            Relevance = item.Relevance,
            Cache = new ContextItemCacheInfo
            {
                Hit = item.Cache.Hit,
                StoredAt = item.Cache.StoredAt,
                TtlSeconds = item.Cache.TtlSeconds
            },
            Metadata = new Dictionary<string, string>(item.Metadata)
        };
    }

    private static DateTimeOffset ParseTimestamp(string? value)
    {
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : DateTimeOffset.MinValue;
    }

    private static string GetCacheFilePath(string workspaceRoot)
    {
        return Path.Combine(workspaceRoot, ".vs", "ContextRelay", "cache.json");
    }

    private void OnSharedStoreChanged(object? sender, SharedStoreChangedEventArgs e)
    {
        if (e.FileKind != SharedStoreFileKind.ChatHistory &&
            e.FileKind != SharedStoreFileKind.Snippets &&
            e.FileKind != SharedStoreFileKind.HandoffIndex)
        {
            return;
        }

        _ = package.JoinableTaskFactory.RunAsync(async delegate
        {
            try
            {
                await RefreshStateAsync(state.StatusMessage).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Failed to refresh state after shared-store update: {ex.Message}");
            }
        });
    }

    private sealed class CacheSnapshotFile
    {
        public TtlLruCacheSnapshotEntry<string, ContextItem[]>[] Entries { get; set; } = Array.Empty<TtlLruCacheSnapshotEntry<string, ContextItem[]>>();
    }
}
