using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
using ContextRelay.VSExtension.ToolWindows;
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
            [ContextSource.Connectors] = new RetrievalSearchAdapter(RetrievalDataSource.ExternalItem, graphClient),
            [ContextSource.OneNote] = new OneNoteSearchAdapter(graphClient),
            [ContextSource.Planner] = new PlannerSearchAdapter(graphClient),
            [ContextSource.Todo] = new TodoSearchAdapter(graphClient)
        };
        copilotChatAdapter = new CopilotChatAdapter(graphClient);
        handoffDocumentGenerator = new HandoffDocumentGenerator(sharedStore);
    }

    public event EventHandler<ContextRelayStateChangedEventArgs>? StateChanged;

    public async Task InitializeAsync()
    {
        await RefreshStateAsync(ContextRelayLocalizedStrings.ReadyStatus).ConfigureAwait(false);
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
                lastSearchSummary = ContextRelayLocalizedStrings.ChatAndSnippetsClearedStatus;
                return await RefreshStateCoreAsync(ContextRelayLocalizedStrings.ChatAndSnippetsClearedStatus, trimmed, cancellationToken).ConfigureAwait(false);
            }

            if (route.IsEmpty)
            {
                return await RefreshStateCoreAsync(ContextRelayLocalizedStrings.TypeQueryStatus, trimmed, cancellationToken).ConfigureAwait(false);
            }

            var settings = await package.GetSettingsSnapshotAsync(cancellationToken).ConfigureAwait(false);
            logger.SetDebugLoggingEnabled(settings.EnableGraphDebugLogging);
            graphClient.BaseUrl = settings.ToAuthSettings().GraphEndpoint;
            await EnsureCacheReadyAsync(settings, cancellationToken).ConfigureAwait(false);
            var enabledSources = GetEnabledSources(route, settings);
            if (enabledSources.Count == 0 && route.Target != RouteTarget.Ask)
            {
                return await RefreshStateCoreAsync(ContextRelayLocalizedStrings.RequestedSourceDisabledStatus, trimmed, cancellationToken).ConfigureAwait(false);
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
                    return await RefreshStateCoreAsync(ContextRelayLocalizedStrings.AskDisabledStatus, trimmed, cancellationToken).ConfigureAwait(false);
                }

                var snippets = await snippetRepository.GetAllAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                if (snippets.Count == 0)
                {
                    return await RefreshStateCoreAsync(ContextRelayLocalizedStrings.AskRequiresPinnedContextStatus, trimmed, cancellationToken).ConfigureAwait(false);
                }

                var prompt = AskPromptBuilder.Build(route.Query, snippets);
                var reply = await copilotChatAdapter.AskAsync(token.AccessToken, prompt, cancellationToken).ConfigureAwait(false);
                var previewDocument = AskPreviewLanguageDetector.Detect(route.Query, reply);
                await AppendChatHistoryAsync(route.Query, reply, cancellationToken).ConfigureAwait(false);
                await OpenAskPreviewAsync(route.Query, previewDocument, settings, cancellationToken).ConfigureAwait(false);
                currentSearchResults = Array.Empty<ContextItem>();
                lastSearchSummary = reply;
                logger.LogInformation("Handled /ask with Microsoft 365 Copilot.");
                return await RefreshStateCoreAsync(
                    ContextRelayLocalizedStrings.GetAskPreviewOpenedStatus(previewDocument.LanguageId, snippets.Count),
                    trimmed,
                    cancellationToken).ConfigureAwait(false);
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
                results.Length == 0
                    ? ContextRelayLocalizedStrings.NoResultsFoundStatus
                    : ContextRelayLocalizedStrings.GetFoundResultsStatus(results.Length),
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
            var matchingSnippetIds = existing
                .Where(snippet => snippet.Metadata.ContainsKey("contextItemKey") &&
                    snippet.Metadata["contextItemKey"].GetString() == itemKey)
                .Select(snippet => snippet.Id)
                .ToArray();

            if (matchingSnippetIds.Length > 0)
            {
                foreach (var snippetId in matchingSnippetIds)
                {
                    await snippetRepository.DeleteAsync(snippetId, cancellationToken).ConfigureAwait(false);
                }

                logger.LogInformation($"Unpinned snippet '{item.Title}'.");
                return await RefreshStateCoreAsync(ContextRelayLocalizedStrings.ResultUnpinnedStatus, state.QueryText, cancellationToken).ConfigureAwait(false);
            }

            var hydrationResult = await TryHydrateContextItemForHandoffAsync(item, cancellationToken).ConfigureAwait(false);
            var metadata = new Dictionary<string, JsonElement>
            {
                ["contextItemKey"] = JsonSerializer.SerializeToElement(itemKey)
            };
            await snippetRepository.SaveAsync(new SaveSnippetRequest
            {
                Name = hydrationResult.Item.Title,
                Source = ToSnippetSource(hydrationResult.Item.Source),
                SourceUrl = hydrationResult.Item.Url,
                Snippet = hydrationResult.Item.Snippet,
                Metadata = metadata
            }, cancellationToken).ConfigureAwait(false);

            logger.LogInformation($"Pinned snippet '{item.Title}'.");
            return await RefreshStateCoreAsync(
                hydrationResult.FellBackToExcerpt
                    ? ContextRelayLocalizedStrings.ResultPinnedWithExcerptFallbackStatus
                    : ContextRelayLocalizedStrings.ResultPinnedStatus,
                state.QueryText,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task CopyResultToClipboardAsync(ContextItem item, CancellationToken cancellationToken = default)
    {
        var text = FormatContextItemForClipboard(item);
        await CopyTextToClipboardAsync(text, ContextRelayLocalizedStrings.ResultCopiedStatus, cancellationToken).ConfigureAwait(false);
    }

    public async Task AppendResultToHandoffAsync(ContextItem item, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var handoffPath = await EnsureHandoffDocPathAsync(cancellationToken).ConfigureAwait(false);
            var hydrated = await TryHydrateContextItemForHandoffAsync(item, cancellationToken).ConfigureAwait(false);
            var excerpt = BuildHandoffExcerpt(hydrated.Item);
            await AppendMarkdownToFileAsync(handoffPath, excerpt, cancellationToken).ConfigureAwait(false);
            await RefreshStateCoreAsync(ContextRelayLocalizedStrings.AppendedToHandoffStatus, state.QueryText, cancellationToken).ConfigureAwait(false);
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
            return await RefreshStateCoreAsync(ContextRelayLocalizedStrings.SnippetRemovedStatus, state.QueryText, cancellationToken).ConfigureAwait(false);
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
            return await RefreshStateCoreAsync(ContextRelayLocalizedStrings.SnippetsClearedStatus, state.QueryText, cancellationToken).ConfigureAwait(false);
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
            return await RefreshStateCoreAsync(ContextRelayLocalizedStrings.ChatHistoryClearedStatus, state.QueryText, cancellationToken).ConfigureAwait(false);
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
            return await RefreshStateCoreAsync(ContextRelayLocalizedStrings.SearchCacheClearedStatus, state.QueryText, cancellationToken).ConfigureAwait(false);
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
            return await RefreshStateCoreAsync(ContextRelayLocalizedStrings.GetHandoffUpdatedStatus(result.WrittenFiles.Count), state.QueryText, cancellationToken).ConfigureAwait(false);
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
        await RefreshStateAsync(ContextRelayLocalizedStrings.HandoffPromptCopiedStatus).ConfigureAwait(false);
    }

    public async Task OpenHandoffDocumentAsync(CancellationToken cancellationToken = default)
    {
        var handoffPath = await EnsureHandoffDocPathAsync(cancellationToken).ConfigureAwait(false);
        await package.OpenDocumentAsync(handoffPath, cancellationToken).ConfigureAwait(false);
        await RefreshStateAsync(ContextRelayLocalizedStrings.OpenedHandoffStatus).ConfigureAwait(false);
    }

    public async Task OpenCopilotChatWithPromptAsync(CancellationToken cancellationToken = default)
    {
        await CopyHandoffPromptAsync(cancellationToken).ConfigureAwait(false);
        var opened = await package.TryOpenCopilotChatAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Soft handoff prepared for Copilot. Prompt copied to clipboard.");
        await RefreshStateAsync(
            opened
                ? ContextRelayLocalizedStrings.OpenCopilotPromptAndPaneReadyStatus
                : ContextRelayLocalizedStrings.OpenCopilotPromptReadyStatus).ConfigureAwait(false);
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

    private async Task OpenAskPreviewAsync(
        string query,
        AskPreviewDocument previewDocument,
        ContextRelaySettingsSnapshot settings,
        CancellationToken cancellationToken)
    {
        var outputDirectory = await ResolveOutputDirectoryAsync(settings, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(outputDirectory);
        var fileExtension = AskPreviewLanguageDetector.GetFileExtension(previewDocument.LanguageId);
        var fileName = ContextRelayLocalizedStrings.GetAskPreviewDocumentTitle(query, fileExtension);
        var filePath = Path.Combine(outputDirectory, fileName);
        await WriteAllTextAsync(filePath, previewDocument.Content, cancellationToken).ConfigureAwait(false);
        await package.OpenDocumentAsync(filePath, cancellationToken).ConfigureAwait(false);
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
            HelpText = ContextRelayLocalizedStrings.GetHelpTextForQuery(queryText),
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
            ContextSource.OneNote => settings.OneNoteEnabled,
            ContextSource.Planner => settings.PlannerEnabled,
            ContextSource.Todo => settings.TodoEnabled,
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
            ContextSource.OneNote => SnippetSource.OneNote,
            ContextSource.Planner => SnippetSource.Planner,
            ContextSource.Todo => SnippetSource.Todo,
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

    private async Task CopyTextToClipboardAsync(string text, string statusMessage, CancellationToken cancellationToken)
    {
        await package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        System.Windows.Clipboard.SetText(text);
        await RefreshStateAsync(statusMessage).ConfigureAwait(false);
    }

    private async Task<string> ResolveOutputDirectoryAsync(ContextRelaySettingsSnapshot settings, CancellationToken cancellationToken)
    {
        var workspaceRoot = await package.GetSolutionRootAsync(cancellationToken).ConfigureAwait(false);
        var outputDirectory = string.IsNullOrWhiteSpace(settings.OutputDirectory) ? ".contextrelay" : settings.OutputDirectory;
        if (Path.IsPathRooted(outputDirectory))
        {
            return Path.GetFullPath(outputDirectory);
        }

        var baseDirectory = !string.IsNullOrWhiteSpace(workspaceRoot)
            ? workspaceRoot
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.GetFullPath(Path.Combine(baseDirectory!, outputDirectory));
    }

    private static string FormatContextItemForClipboard(ContextItem item)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"### {item.Title}");
        builder.AppendLine($"- Source: {item.Source}");
        if (!string.IsNullOrWhiteSpace(item.Timestamp))
        {
            builder.AppendLine($"- Timestamp: {item.Timestamp}");
        }

        if (!string.IsNullOrWhiteSpace(item.Url))
        {
            builder.AppendLine($"- Link: {item.Url}");
        }

        builder.AppendLine();
        builder.AppendLine(item.Snippet);
        return builder.ToString().TrimEnd();
    }

    private static string BuildHandoffExcerpt(ContextItem item)
    {
        return string.Join(
            "\n",
            $"## Added excerpt ({DateTimeOffset.UtcNow:yyyy-MM-dd'T'HH:mm:ss'Z'})",
            string.Empty,
            $"### {item.Title}",
            $"- **Source**: {item.Source}",
            string.IsNullOrWhiteSpace(item.Timestamp) ? string.Empty : $"- **Timestamp**: {item.Timestamp}",
            string.IsNullOrWhiteSpace(item.Url) ? string.Empty : $"- **Link**: {item.Url}",
            string.Empty,
            item.Snippet.Trim()).TrimEnd();
    }

    private static async Task AppendMarkdownToFileAsync(string filePath, string content, CancellationToken cancellationToken)
    {
        var prefix = File.Exists(filePath) && new FileInfo(filePath).Length > 0 ? "\n\n" : string.Empty;
        using var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync($"{prefix}{content.TrimEnd()}\n").ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static async Task WriteAllTextAsync(string filePath, string content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync(content).ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task<HydrationResult> TryHydrateContextItemForHandoffAsync(ContextItem item, CancellationToken cancellationToken)
    {
        if (!CanHydrateForHandoff(item.Source))
        {
            return new HydrationResult(item, false);
        }

        try
        {
            var settings = await package.GetSettingsSnapshotAsync(cancellationToken).ConfigureAwait(false);
            var token = await authProvider
                .GetAccessTokenAsync(settings.ToAuthSettings(), settings.ToFeatureOptions(), cancellationToken)
                .ConfigureAwait(false);
            var hydrated = await HydrateContextItemForHandoffAsync(token.AccessToken, item, cancellationToken).ConfigureAwait(false);
            return new HydrationResult(hydrated, false);
        }
        catch (GraphApiException ex)
        {
            logger.LogWarning($"Unable to hydrate '{item.Title}' for handoff content: {ex.Message}");
            return new HydrationResult(item, true);
        }
        catch (ContextRelayAuthenticationException ex)
        {
            logger.LogWarning($"Unable to hydrate '{item.Title}' because authentication is unavailable: {ex.Message}");
            return new HydrationResult(item, true);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning($"Unable to hydrate '{item.Title}' because the Graph download failed: {ex.Message}");
            return new HydrationResult(item, true);
        }
        catch (InvalidDataException ex)
        {
            logger.LogWarning($"Unable to hydrate '{item.Title}' because the downloaded content was invalid: {ex.Message}");
            return new HydrationResult(item, true);
        }
        catch (JsonException ex)
        {
            logger.LogWarning($"Unable to hydrate '{item.Title}' because the Graph response could not be parsed: {ex.Message}");
            return new HydrationResult(item, true);
        }
        catch (IOException ex)
        {
            logger.LogWarning($"Unable to hydrate '{item.Title}' because the content stream could not be read: {ex.Message}");
            return new HydrationResult(item, true);
        }
    }

    private async Task<ContextItem> HydrateContextItemForHandoffAsync(string accessToken, ContextItem item, CancellationToken cancellationToken)
    {
        return item.Source switch
        {
            ContextSource.Mail => await HydrateMailItemAsync(accessToken, item, cancellationToken).ConfigureAwait(false),
            ContextSource.SharePoint => await HydrateDriveItemAsync(accessToken, item, cancellationToken).ConfigureAwait(false),
            ContextSource.OneDrive => await HydrateDriveItemAsync(accessToken, item, cancellationToken).ConfigureAwait(false),
            ContextSource.OneNote => await HydrateOneNoteItemAsync(accessToken, item, cancellationToken).ConfigureAwait(false),
            _ => item
        };
    }

    private async Task<ContextItem> HydrateMailItemAsync(string accessToken, ContextItem item, CancellationToken cancellationToken)
    {
        if (!item.Metadata.TryGetValue("messageId", out var messageId) || string.IsNullOrWhiteSpace(messageId))
        {
            return item;
        }

        using var response = await graphClient
            .SendWithRetryAsync(
                $"{graphClient.BaseUrl}/v1.0/me/messages/{Uri.EscapeDataString(messageId)}?$select=body",
                accessToken,
                HttpMethod.Get,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var bodyEnvelope = await graphClient.ReadJsonAsync<MailBodyEnvelope>(response, cancellationToken).ConfigureAwait(false);
        var hydratedBody = NormalizeMailBody(bodyEnvelope.Body?.Content, bodyEnvelope.Body?.ContentType);
        return string.IsNullOrWhiteSpace(hydratedBody)
            ? item
            : CloneItemWithSnippet(item, hydratedBody);
    }

    private async Task<ContextItem> HydrateDriveItemAsync(string accessToken, ContextItem item, CancellationToken cancellationToken)
    {
        if (!item.Metadata.TryGetValue("driveId", out var driveId) ||
            string.IsNullOrWhiteSpace(driveId) ||
            !item.Metadata.TryGetValue("id", out var itemId) ||
            string.IsNullOrWhiteSpace(itemId))
        {
            return item;
        }

        item.Metadata.TryGetValue("mimeType", out var mimeType);
        var content = await DownloadDriveItemContentAsync(
            accessToken,
            driveId,
            itemId,
            InferDriveContentMode(item.Title, mimeType),
            cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(content)
            ? item
            : CloneItemWithSnippet(item, content);
    }

    private async Task<ContextItem> HydrateOneNoteItemAsync(string accessToken, ContextItem item, CancellationToken cancellationToken)
    {
        if (!item.Metadata.TryGetValue("pageId", out var pageId) || string.IsNullOrWhiteSpace(pageId))
        {
            return item;
        }

        var url = $"{graphClient.BaseUrl}/v1.0/me/onenote/pages/{Uri.EscapeDataString(pageId)}/content";
        using var response = await graphClient
            .SendWithRetryAsync(url, accessToken, HttpMethod.Get, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return item;
        }

        var html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var content = NormalizeHtmlToText(html);
        return string.IsNullOrWhiteSpace(content)
            ? item
            : CloneItemWithSnippet(item, content);
    }

    private async Task<string> DownloadDriveItemContentAsync(
        string accessToken,
        string driveId,
        string itemId,
        DriveContentMode mode,
        CancellationToken cancellationToken)
    {
        switch (mode)
        {
            case DriveContentMode.PlainText:
            {
                using var response = await SendDriveItemContentRequestAsync(accessToken, driveId, itemId, format: null, cancellationToken).ConfigureAwait(false);
                return NormalizeDownloadedText(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            }

            case DriveContentMode.HtmlConvertible:
            {
                using var response = await SendDriveItemContentRequestAsync(accessToken, driveId, itemId, format: "html", cancellationToken).ConfigureAwait(false);
                return NormalizeHtmlToText(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            }

            case DriveContentMode.Docx:
            {
                using var response = await SendDriveItemContentRequestAsync(accessToken, driveId, itemId, format: null, cancellationToken).ConfigureAwait(false);
                using var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var buffer = new MemoryStream();
                await responseStream.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
                buffer.Position = 0;
                return ExtractDocxText(buffer);
            }

            default:
                return string.Empty;
        }
    }

    private async Task<HttpResponseMessage> SendDriveItemContentRequestAsync(
        string accessToken,
        string driveId,
        string itemId,
        string? format,
        CancellationToken cancellationToken)
    {
        var suffix = string.IsNullOrWhiteSpace(format) ? string.Empty : $"?format={format}";
        var url = $"{graphClient.BaseUrl}/v1.0/drives/{Uri.EscapeDataString(driveId)}/items/{Uri.EscapeDataString(itemId)}/content{suffix}";
        var response = await graphClient
            .SendWithRetryAsync(url, accessToken, HttpMethod.Get, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            using (response)
            {
                JsonDocument? errorDocument = null;
                try
                {
                    var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(responseBody))
                    {
                        errorDocument = JsonDocument.Parse(responseBody);
                    }
                }
                catch (JsonException)
                {
                }

                using (errorDocument)
                {
                    string? graphErrorCode = null;
                    string? graphErrorMessage = null;
                    if (errorDocument is not null &&
                        errorDocument.RootElement.ValueKind == JsonValueKind.Object &&
                        errorDocument.RootElement.TryGetProperty("error", out var errorElement) &&
                        errorElement.ValueKind == JsonValueKind.Object)
                    {
                        if (errorElement.TryGetProperty("code", out var codeElement) &&
                            codeElement.ValueKind == JsonValueKind.String)
                        {
                            graphErrorCode = codeElement.GetString();
                        }

                        if (errorElement.TryGetProperty("message", out var messageElement) &&
                            messageElement.ValueKind == JsonValueKind.String)
                        {
                            graphErrorMessage = messageElement.GetString();
                        }
                    }

                    var statusDescription = $"{(int)response.StatusCode} {response.ReasonPhrase}".Trim();
                    var message = $"The drive item content request did not complete successfully. URL: {url}. HTTP status: {statusDescription}.";
                    if (!string.IsNullOrWhiteSpace(graphErrorCode))
                    {
                        message += $" Graph error code: {graphErrorCode}.";
                    }

                    if (!string.IsNullOrWhiteSpace(graphErrorMessage))
                    {
                        message += $" Graph error message: {graphErrorMessage}.";
                    }

                    throw new InvalidOperationException(message);
                }
            }
        }

        return response;
    }

    private static bool CanHydrateForHandoff(ContextSource source)
    {
        return source == ContextSource.Mail ||
            source == ContextSource.SharePoint ||
            source == ContextSource.OneDrive ||
            source == ContextSource.OneNote;
    }

    private static ContextItem CloneItemWithSnippet(ContextItem item, string snippet)
    {
        var clone = CloneItem(item);
        clone.Snippet = snippet;
        return clone;
    }

    private static string NormalizeMailBody(string? content, string? contentType)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var body = content!;
        return string.Equals(contentType, "html", StringComparison.OrdinalIgnoreCase)
            ? NormalizeHtmlToText(body)
            : NormalizeDownloadedText(body);
    }

    private static string NormalizeDownloadedText(string value)
    {
        return value
            .Replace("\uFEFF", string.Empty)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Trim();
    }

    private static string NormalizeHtmlToText(string html)
    {
        var text = html
            .Replace("<br>", "\n")
            .Replace("<br/>", "\n")
            .Replace("<br />", "\n");
        text = Regex.Replace(text, "</p\\s*>", "\n\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", string.Empty);
        text = WebUtility.HtmlDecode(text);
        text = NormalizeDownloadedText(text);
        return Regex.Replace(text, "\n{3,}", "\n\n");
    }

    private static string ExtractDocxText(Stream docxStream)
    {
        using var archive = new ZipArchive(docxStream, ZipArchiveMode.Read, leaveOpen: false);
        var entries = archive.Entries
            .Where(entry => entry.FullName.StartsWith("word/", StringComparison.OrdinalIgnoreCase) &&
                (entry.FullName.Equals("word/document.xml", StringComparison.OrdinalIgnoreCase) ||
                    Regex.IsMatch(entry.FullName, "^word/(header\\d+|footer\\d+|footnotes|endnotes)\\.xml$", RegexOptions.IgnoreCase)))
            .ToArray();
        var parts = new List<string>();
        foreach (var entry in entries)
        {
            using var entryStream = entry.Open();
            using var reader = new StreamReader(entryStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var xml = reader.ReadToEnd();
            var text = ExtractWordprocessingText(xml);
            if (!string.IsNullOrWhiteSpace(text))
            {
                parts.Add(text);
            }
        }

        return string.Join("\n\n", parts).Trim();
    }

    private static string ExtractWordprocessingText(string xml)
    {
        var text = xml
            .Replace("<w:tab/>", "\t")
            .Replace("<w:tab />", "\t");
        text = Regex.Replace(text, "<w:(?:br|cr)\\s*/>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "</w:p>", "\n\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", string.Empty);
        text = WebUtility.HtmlDecode(text);
        text = NormalizeDownloadedText(text);
        text = Regex.Replace(text, "[ \\t]+\n", "\n");
        text = Regex.Replace(text, "\n[ \\t]+", "\n");
        text = Regex.Replace(text, "\n{3,}", "\n\n");
        text = Regex.Replace(text, "[ \\t]{2,}", " ");
        return text.Trim();
    }

    private static DriveContentMode InferDriveContentMode(string name, string? mimeType)
    {
        var extension = Path.GetExtension(name ?? string.Empty).TrimStart('.').ToLowerInvariant();
        var normalizedMimeType = mimeType ?? string.Empty;
        if (extension == "docx" || extension == "dotx" || extension == "docm" || extension == "dotm")
        {
            return DriveContentMode.Docx;
        }

        if (extension == "eml" || extension == "msg")
        {
            return DriveContentMode.HtmlConvertible;
        }

        if (PlainTextExtensions.Contains(extension) ||
            (!string.IsNullOrWhiteSpace(normalizedMimeType) && normalizedMimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(normalizedMimeType, "application/json", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedMimeType, "application/xml", StringComparison.OrdinalIgnoreCase))
        {
            return DriveContentMode.PlainText;
        }

        return DriveContentMode.Unsupported;
    }

    private static readonly HashSet<string> PlainTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "txt", "md", "markdown", "json", "jsonl", "csv", "tsv", "log", "xml", "yaml", "yml",
        "html", "htm", "css", "js", "ts", "tsx", "jsx", "py", "java", "cs", "go", "rs", "sql"
    };

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

    private sealed class MailBodyEnvelope
    {
        public MailBodyContent? Body { get; set; }
    }

    private sealed class MailBodyContent
    {
        public string? ContentType { get; set; }

        public string? Content { get; set; }
    }

    private sealed class HydrationResult
    {
        public HydrationResult(ContextItem item, bool fellBackToExcerpt)
        {
            Item = item;
            FellBackToExcerpt = fellBackToExcerpt;
        }

        public ContextItem Item { get; }

        public bool FellBackToExcerpt { get; }
    }

    private enum DriveContentMode
    {
        PlainText,
        Docx,
        HtmlConvertible,
        Unsupported
    }
}
