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
using ContextRelay.Core.Chat;
using ContextRelay.Core.FileContext;
using ContextRelay.Core.Handoff;
using ContextRelay.Core.Models;
using ContextRelay.Core.Router;
using ContextRelay.Core.Settings;
using ContextRelay.Core.SharedStore;
using ContextRelay.Core.Snippets;
using ContextRelay.Core.Utilities;
using ContextRelay.VSExtension.ToolWindows;

namespace ContextRelay.VSExtension.Services;

internal sealed class ContextRelayHost : IDisposable
{
    private readonly record struct CreatedFileTargetContext(string RootDirectory, bool ShouldAddToSolutionExplorer, bool FolderWasSelected);
    private readonly record struct HandoffDocumentWriteContext(HandoffGenerationResult GenerationResult, CreatedFileTargetContext TargetContext);

    private readonly IContextRelayPackageServices packageServices;
    private readonly ContextRelayOutputLogger logger;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource disposeCancellation = new();
    private readonly object draftQuerySync = new();
    private readonly Lazy<IContextRelayAuthProvider> authProvider;
    private readonly FileSystemSharedSessionStore sharedStore;
    private readonly SharedStoreWatcher watcher;
    private readonly SharedSnippetRepository snippetRepository;
    private readonly GraphHttpClient graphClient;
    private readonly Dictionary<ContextSource, IContextSearchAdapter> adapters;
    private readonly ICopilotChatAdapter copilotChatAdapter;
    private readonly WorkIqAdapter workIqAdapter;
    private readonly HandoffDocumentGenerator handoffDocumentGenerator;
    private TtlLruCache<string, ContextItem[]> searchCache = new();
    private ContextRelayHostState state = new();
    private string? lastSearchSummary;
    private string? copilotConversationId;
    private string? workIqContextId;
    private string draftQueryText = string.Empty;
    private string? cacheWorkspaceRoot;
    private int cacheTtlSeconds = 300;
    private int cacheMaxEntries = 200;
    private ContextItem[] currentSearchResults = Array.Empty<ContextItem>();
    private bool initialized;
    private bool shouldResolveSignedInUser;
    private int deferredSignedInUserResolutionScheduled;

    public ContextRelayHost(IContextRelayPackageServices packageServices, ContextRelayOutputLogger logger)
    {
        this.packageServices = packageServices;
        this.logger = logger;
        authProvider = new Lazy<IContextRelayAuthProvider>(
            static () => new MsalAuthProvider(),
            LazyThreadSafetyMode.ExecutionAndPublication);

        var sharedStoreOptions = SharedStoreOptions.CreateDefault("vs", typeof(ContextRelayHost).Assembly.GetName().Version?.ToString() ?? "0.1.0");
        watcher = new SharedStoreWatcher(sharedStoreOptions.RootDirectory, sharedStoreOptions.WatcherDebounceMilliseconds);
        watcher.Changed += OnSharedStoreChanged;
        sharedStore = new FileSystemSharedSessionStore(sharedStoreOptions, watcher: watcher);
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
        workIqAdapter = new WorkIqAdapter(new System.Net.Http.HttpClient(), logger, ownsHttpClient: true);
        handoffDocumentGenerator = new HandoffDocumentGenerator(sharedStore);
    }

    public event EventHandler<ContextRelayStateChangedEventArgs>? StateChanged;

    public void UpdateDraftQueryText(string queryText)
    {
        lock (draftQuerySync)
        {
            draftQueryText = queryText ?? string.Empty;
        }
    }

    public void LogUiDiagnostic(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        logger.LogDiagnostic($"[ui] {message}");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (initialized)
            {
                return;
            }

            await logger.InitializeAsync(cancellationToken).ConfigureAwait(false);
            initialized = true;
            await RefreshStateCoreAsync(ContextRelayLocalizedStrings.ReadyStatus, GetDraftQueryText(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ReportToolWindowInitializationFailureAsync(Exception exception, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exception);

        logger.LogError("ContextRelay tool window initialization failed.", exception);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            state = new ContextRelayHostState
            {
                QueryText = GetDraftQueryText(),
                HelpText = ContextRelayLocalizedStrings.GenericHelpText,
                StatusMessage = ContextRelayLocalizedStrings.GetToolWindowInitializationFailedStatus(exception.Message),
                SignedInUser = state.SignedInUser,
                LastHandoffPath = state.LastHandoffPath,
                SearchResults = state.SearchResults,
                Snippets = state.Snippets,
                ChatHistory = state.ChatHistory
            };
        }
        finally
        {
            gate.Release();
        }

        StateChanged?.Invoke(this, new ContextRelayStateChangedEventArgs(state));
    }

    public async Task<ContextRelayHostState> GetStateAsync()
    {
        var settings = await packageServices.GetSettingsSnapshotAsync().ConfigureAwait(false);
        ContextRelayLocalizedStrings.SetUiLanguage(settings.UiLanguage);
        logger.SetDebugLoggingEnabled(settings.EnableGraphDebugLogging, settings.EnableWorkIqDebugLogging);

        var statusMessage = ContextRelayLocalizedStrings.IsReadyStatus(state.StatusMessage)
            ? ContextRelayLocalizedStrings.ReadyStatus
            : state.StatusMessage;

        await RefreshStateAsync(statusMessage).ConfigureAwait(false);
        return state;
    }

    public async Task<ContextRelayHostState> UpdateUiLanguageAsync(string uiLanguage, CancellationToken cancellationToken = default)
    {
        await packageServices.UpdateUiLanguageAsync(uiLanguage, cancellationToken).ConfigureAwait(false);
        await RefreshStateAsync(ContextRelayLocalizedStrings.ReadyStatus).ConfigureAwait(false);
        return state;
    }

    public void StartDeferredSignedInUserResolution()
    {
        if (Interlocked.Exchange(ref deferredSignedInUserResolutionScheduled, 1) == 1)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), disposeCancellation.Token).ConfigureAwait(false);

                // Guard against the host being disposed right after the delay completes.
                if (disposeCancellation.IsCancellationRequested)
                {
                    return;
                }

                shouldResolveSignedInUser = true;
                await RefreshStateAsync(state.StatusMessage).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Deferred signed-in-user refresh failed: {ex.Message}");
            }
        }, disposeCancellation.Token);
    }

    public async Task<ContextRelayHostState> SubmitQueryAsync(string input, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var trimmed = input?.Trim() ?? string.Empty;
            var route = SlashCommandRouter.Parse(trimmed);
            state.QueryText = trimmed;
            UpdateDraftQueryText(trimmed);

            if (route.Target == RouteTarget.Clear)
            {
                await sharedStore.ClearAsync(SharedStoreFileKind.ChatHistory, cancellationToken).ConfigureAwait(false);
                await snippetRepository.ClearAsync(cancellationToken).ConfigureAwait(false);
                currentSearchResults = Array.Empty<ContextItem>();
                lastSearchSummary = null;
                copilotConversationId = null;
                workIqContextId = null;
                return await RefreshStateCoreAsync(ContextRelayLocalizedStrings.ChatAndSnippetsClearedStatus, trimmed, cancellationToken).ConfigureAwait(false);
            }

            if (route.IsEmpty)
            {
                return await RefreshStateCoreAsync(ContextRelayLocalizedStrings.TypeQueryStatus, trimmed, cancellationToken).ConfigureAwait(false);
            }

            var settings = await packageServices.GetSettingsSnapshotAsync(cancellationToken).ConfigureAwait(false);
            logger.SetDebugLoggingEnabled(settings.EnableGraphDebugLogging, settings.EnableWorkIqDebugLogging);
            graphClient.BaseUrl = settings.ToAuthSettings().GraphEndpoint;
            logger.LogDiagnostic(
                $"SubmitQuery target={route.Target} command={(string.IsNullOrWhiteSpace(route.SlashCommandName) ? "(chat)" : route.SlashCommandName)} " +
                $"inputLength={trimmed.Length} queryLength={route.Query.Length}");
            await EnsureCacheReadyAsync(settings, cancellationToken).ConfigureAwait(false);
            var enabledSources = GetEnabledSources(route, settings);
            if (enabledSources.Count == 0 &&
                route.Target != RouteTarget.Ask &&
                route.Target != RouteTarget.Chat &&
                route.Target != RouteTarget.WorkIq)
            {
                return await RefreshStateCoreAsync(ContextRelayLocalizedStrings.RequestedSourceDisabledStatus, trimmed, cancellationToken).ConfigureAwait(false);
            }

            var authSettings = settings.ToAuthSettings();
            shouldResolveSignedInUser = true;
            var filePrompt = route.Target switch
            {
                RouteTarget.Chat => await ResolveFilePromptAsync(trimmed, route.Query, trimmed, cancellationToken).ConfigureAwait(false),
                RouteTarget.Ask => await ResolveFilePromptAsync(route.Query, route.Query, trimmed, cancellationToken).ConfigureAwait(false),
                RouteTarget.WorkIq => await ResolveFilePromptAsync(route.Query, route.Query, trimmed, cancellationToken).ConfigureAwait(false),
                _ => new FilePromptContext(route.Query, Array.Empty<ResolvedFileMention>())
            };
            if (filePrompt is null)
            {
                return state;
            }

            if (route.Target == RouteTarget.WorkIq)
            {
                return await HandleWorkIqCommandAsync(trimmed, filePrompt.Prompt, authSettings, settings, filePrompt.Files, cancellationToken).ConfigureAwait(false);
            }

            var featureOptions = settings.ToFeatureOptions();
            ContextRelayAccessToken token;
            try
            {
                token = await authProvider.Value.GetAccessTokenAsync(authSettings, featureOptions, cancellationToken).ConfigureAwait(false);
            }
            catch (ContextRelayAuthenticationException ex)
            {
                logger.LogError("Authentication failed.", ex);
                return await RefreshStateCoreAsync(ex.Message, trimmed, cancellationToken).ConfigureAwait(false);
            }

            if (route.Target == RouteTarget.Chat)
            {
                if (!settings.EnableChatPreview)
                {
                    return await RefreshStateCoreAsync(ContextRelayLocalizedStrings.ChatPreviewDisabledStatus, trimmed, cancellationToken).ConfigureAwait(false);
                }

                return await HandleChatCommandAsync(trimmed, filePrompt.Prompt, token.AccessToken, filePrompt.Files, cancellationToken).ConfigureAwait(false);
            }

            if (route.Target == RouteTarget.Ask)
            {
                if (!settings.EnableChatPreview)
                {
                    return await RefreshStateCoreAsync(ContextRelayLocalizedStrings.AskDisabledStatus, trimmed, cancellationToken).ConfigureAwait(false);
                }

                var snippets = await snippetRepository.GetAllAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                if (snippets.Count == 0 && filePrompt.Files.Count == 0)
                {
                    return await RefreshStateCoreAsync(ContextRelayLocalizedStrings.AskRequiresPinnedContextStatus, trimmed, cancellationToken).ConfigureAwait(false);
                }

                logger.LogDiagnostic($"/ask context sources: pinnedSnippets={snippets.Count}, localFiles={filePrompt.Files.Count}");
                var snippetsWithLocalFileContext = await BuildSnippetsWithLocalFileContextAsync(snippets, filePrompt.Files, cancellationToken).ConfigureAwait(false);
                var contextPayload = ChatContextPayloadBuilder.Build(snippetsWithLocalFileContext);
                LogChatPayloadDiagnostics("ask", filePrompt.Prompt, contextPayload);
                var conversationId = await EnsureCopilotConversationAsync(token.AccessToken, cancellationToken).ConfigureAwait(false);
                var reply = await copilotChatAdapter
                    .SendMessageAsync(token.AccessToken, conversationId, filePrompt.Prompt, contextPayload.SendOptions, cancellationToken)
                    .ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(reply))
                {
                    throw new InvalidOperationException("Microsoft 365 Copilot returned an empty response.");
                }

                await AppendChatHistoryAsync(filePrompt.Prompt, reply, "ask", contextPayload.Labels, cancellationToken).ConfigureAwait(false);
                logger.LogInformation("Handled /ask with Microsoft 365 Copilot.");
                return await RefreshStateCoreAsync(
                    filePrompt.Files.Count == 0
                        ? ContextRelayLocalizedStrings.GetAskReplyShownStatus(snippets.Count)
                        : ContextRelayLocalizedStrings.GetAskReplyShownWithContextBreakdownStatus(snippets.Count, filePrompt.Files.Count),
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
                return await RefreshStateCoreAsync(ContextRelayLocalizedStrings.ResultUnpinnedStatus, GetDraftQueryText(), cancellationToken).ConfigureAwait(false);
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
                GetDraftQueryText(),
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
            var handoffContext = await TryEnsureHandoffDocPathAsync(cancellationToken).ConfigureAwait(false);
            if (handoffContext is null)
            {
                await RefreshStateCoreAsync(ContextRelayLocalizedStrings.CreatedFilesFolderSelectionCanceledStatus, GetDraftQueryText(), cancellationToken).ConfigureAwait(false);
                return;
            }

            var handoffPath = handoffContext.Value.GenerationResult.HandoffPath ?? handoffContext.Value.GenerationResult.PlanPath;
            var hydrated = await TryHydrateContextItemForHandoffAsync(item, cancellationToken).ConfigureAwait(false);
            var excerpt = BuildHandoffExcerpt(hydrated.Item);
            await AppendMarkdownToFileAsync(handoffPath, excerpt, cancellationToken).ConfigureAwait(false);
            await RefreshStateCoreAsync(ContextRelayLocalizedStrings.AppendedToHandoffStatus, GetDraftQueryText(), cancellationToken).ConfigureAwait(false);
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
            return await RefreshStateCoreAsync(ContextRelayLocalizedStrings.SnippetRemovedStatus, GetDraftQueryText(), cancellationToken).ConfigureAwait(false);
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
            return await RefreshStateCoreAsync(ContextRelayLocalizedStrings.SnippetsClearedStatus, GetDraftQueryText(), cancellationToken).ConfigureAwait(false);
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
            currentSearchResults = Array.Empty<ContextItem>();
            lastSearchSummary = null;
            copilotConversationId = null;
            workIqContextId = null;
            return await RefreshStateCoreAsync(ContextRelayLocalizedStrings.ChatHistoryClearedStatus, GetDraftQueryText(), cancellationToken).ConfigureAwait(false);
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
            var settings = await packageServices.GetSettingsSnapshotAsync(cancellationToken).ConfigureAwait(false);
            logger.SetDebugLoggingEnabled(settings.EnableGraphDebugLogging, settings.EnableWorkIqDebugLogging);
            await PersistCacheIfNeededAsync(settings, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Search cache cleared.");
            return await RefreshStateCoreAsync(ContextRelayLocalizedStrings.SearchCacheClearedStatus, GetDraftQueryText(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ContextRelayHostState> AddFilesToQueryAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SyncDebugLoggingOptionsAsync(cancellationToken).ConfigureAwait(false);
            var currentQuery = GetDraftQueryText();
            logger.LogDiagnostic($"[ui] AddFilesToQuery invoked with currentQueryLength={currentQuery.Length}");
            var workspaceRoots = await packageServices.GetWorkspaceRootsAsync(cancellationToken).ConfigureAwait(false);

            var selectedFiles = await packageServices
                .PickWorkspaceFilesAsync(workspaceRoots.Count > 0 ? workspaceRoots[0] : null, cancellationToken)
                .ConfigureAwait(false);
            logger.LogDiagnostic($"[ui] AddFilesToQuery picker returned selectedFileCount={selectedFiles.Count}");
            if (selectedFiles.Count == 0)
            {
                logger.LogDiagnostic("[ui] AddFilesToQuery canceled because no files were selected.");
                return await RefreshStateCoreAsync(ContextRelayLocalizedStrings.FilePickerNoFilesSelectedStatus, currentQuery, cancellationToken).ConfigureAwait(false);
            }

            var mergeResult = MergeSelectedFilesIntoQuery(currentQuery, selectedFiles, workspaceRoots);
            logger.LogDiagnostic(
                $"[ui] AddFilesToQuery merged files with mergedQueryLength={mergeResult.QueryText.Length} status=\"{mergeResult.StatusMessage}\"");
            return await RefreshStateCoreAsync(mergeResult.StatusMessage, mergeResult.QueryText, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError("Adding local files to the query failed.", ex);
            return await RefreshStateCoreAsync(ContextRelayLocalizedStrings.FilePickerAddFilesFailedStatus, GetDraftQueryText(), cancellationToken).ConfigureAwait(false);
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
            var handoffContext = await TryEnsureHandoffDocsAsync(cancellationToken).ConfigureAwait(false);
            if (handoffContext is null)
            {
                return await RefreshStateCoreAsync(ContextRelayLocalizedStrings.CreatedFilesFolderSelectionCanceledStatus, GetDraftQueryText(), cancellationToken).ConfigureAwait(false);
            }

            var result = handoffContext.Value.GenerationResult;
            logger.LogInformation($"Generated handoff docs in '{result.OutputDirectory}'.");
            return await RefreshStateCoreAsync(ContextRelayLocalizedStrings.GetHandoffUpdatedStatus(result.WrittenFiles.Count), GetDraftQueryText(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task CopyHandoffPromptAsync(CancellationToken cancellationToken = default)
    {
        var handoffContext = await TryEnsureHandoffDocPathAsync(cancellationToken).ConfigureAwait(false);
        if (handoffContext is null)
        {
            await RefreshStateAsync(ContextRelayLocalizedStrings.CreatedFilesFolderSelectionCanceledStatus).ConfigureAwait(false);
            return;
        }

        var handoffPath = handoffContext.Value.GenerationResult.HandoffPath ?? handoffContext.Value.GenerationResult.PlanPath;
        var prompt = BuildHandoffPrompt(handoffPath);
        await packageServices.CopyTextToClipboardAsync(prompt, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Handoff prompt copied to clipboard.");
        await RefreshStateAsync(ContextRelayLocalizedStrings.HandoffPromptCopiedStatus).ConfigureAwait(false);
    }

    public async Task CopyAssistantTextAsync(string text, CancellationToken cancellationToken = default)
    {
        await CopyTextToClipboardAsync(text, ContextRelayLocalizedStrings.AssistantResponseCopiedStatus, cancellationToken).ConfigureAwait(false);
    }

    public async Task AppendAssistantTextToEditorAsync(string text, CancellationToken cancellationToken = default)
    {
        var applied = await packageServices.AppendToActiveDocumentAsync(text, cancellationToken).ConfigureAwait(false);
        await RefreshStateAsync(applied
            ? ContextRelayLocalizedStrings.AssistantResponseAppendedStatus
            : ContextRelayLocalizedStrings.NoActiveEditorStatus).ConfigureAwait(false);
    }

    public async Task ReplaceEditorWithAssistantTextAsync(string text, CancellationToken cancellationToken = default)
    {
        var applied = await packageServices.ReplaceActiveDocumentAsync(text, cancellationToken).ConfigureAwait(false);
        await RefreshStateAsync(applied
            ? ContextRelayLocalizedStrings.AssistantResponseReplacedStatus
            : ContextRelayLocalizedStrings.NoActiveEditorStatus).ConfigureAwait(false);
    }

    public async Task OpenHandoffDocumentAsync(CancellationToken cancellationToken = default)
    {
        var handoffContext = await TryEnsureHandoffDocPathAsync(cancellationToken).ConfigureAwait(false);
        if (handoffContext is null)
        {
            await RefreshStateAsync(ContextRelayLocalizedStrings.CreatedFilesFolderSelectionCanceledStatus).ConfigureAwait(false);
            return;
        }

        var handoffPath = handoffContext.Value.GenerationResult.HandoffPath ?? handoffContext.Value.GenerationResult.PlanPath;
        await packageServices.OpenDocumentAsync(handoffPath, cancellationToken).ConfigureAwait(false);
        await RefreshStateAsync(ContextRelayLocalizedStrings.OpenedHandoffStatus).ConfigureAwait(false);
    }

    public async Task OpenCopilotChatWithPromptAsync(CancellationToken cancellationToken = default)
    {
        await CopyHandoffPromptAsync(cancellationToken).ConfigureAwait(false);
        var opened = await packageServices.TryOpenCopilotChatAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Soft handoff prepared for Copilot. Prompt copied to clipboard.");
        await RefreshStateAsync(
            opened
                ? ContextRelayLocalizedStrings.OpenCopilotPromptAndPaneReadyStatus
                : ContextRelayLocalizedStrings.OpenCopilotPromptReadyStatus).ConfigureAwait(false);
    }

    public void ShowDebugLog()
    {
        _ = ShowDebugLogAsync();
    }

    public async Task ShowDebugLogAsync(CancellationToken cancellationToken = default)
    {
        await SyncDebugLoggingOptionsAsync(cancellationToken).ConfigureAwait(false);
        logger.ShowDebugPane();
        logger.LogDiagnostic("[ui] Debug log pane opened.");
        await RefreshStateAsync(ContextRelayLocalizedStrings.DebugLogOpenedStatus).ConfigureAwait(false);
    }

    public void OpenExternalUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (!ExternalUrlSafety.TryNormalizeExternalUrl(url, out var safeUrl))
        {
            logger.LogWarning("Blocked unsafe external URL open attempt.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = safeUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            logger.LogError($"Failed to open external URL '{safeUrl}'.", ex);
        }
    }

    public void Dispose()
    {
        disposeCancellation.Cancel();
        watcher.Changed -= OnSharedStoreChanged;
        workIqAdapter.Dispose();
        snippetRepository.Dispose();
        watcher.Dispose();
        gate.Dispose();
        disposeCancellation.Dispose();
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
        var workspaceRoot = settings.PersistWorkspaceState ? await packageServices.GetSolutionRootAsync(cancellationToken).ConfigureAwait(false) : null;
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

    private async Task<HandoffDocumentWriteContext?> TryEnsureHandoffDocPathAsync(CancellationToken cancellationToken)
    {
        return await TryEnsureHandoffDocsAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<HandoffDocumentWriteContext?> TryEnsureHandoffDocsAsync(CancellationToken cancellationToken)
    {
        var settings = await packageServices.GetSettingsSnapshotAsync(cancellationToken).ConfigureAwait(false);
        logger.SetDebugLoggingEnabled(settings.EnableGraphDebugLogging, settings.EnableWorkIqDebugLogging);
        var targetContext = await TryResolveCreatedFileTargetContextAsync(cancellationToken).ConfigureAwait(false);
        if (targetContext is null)
        {
            logger.LogInformation("Skipped handoff generation because no target folder was selected.");
            return null;
        }

        var snippets = await snippetRepository.GetAllAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var result = await handoffDocumentGenerator.GenerateAsync(
            new HandoffContext
            {
                Snippets = snippets,
                SearchSummary = lastSearchSummary
            },
            new HandoffGenerationOptions
            {
                OutputDirectory = string.IsNullOrWhiteSpace(settings.OutputDirectory) ? ".contextrelay" : settings.OutputDirectory,
                WorkspaceRoot = targetContext.Value.RootDirectory,
                FallbackRootDirectory = targetContext.Value.RootDirectory,
                IncludeHandoffDocument = true
            },
            cancellationToken).ConfigureAwait(false);
        await RegisterCreatedFilesAsync(result.WrittenFiles, targetContext.Value, cancellationToken).ConfigureAwait(false);
        return new HandoffDocumentWriteContext(result, targetContext.Value);
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

    private async Task<IReadOnlyList<SharedSnippetItem>> BuildSnippetsWithLocalFileContextAsync(
        IReadOnlyList<SharedSnippetItem> snippets,
        IReadOnlyList<ResolvedFileMention> localFiles,
        CancellationToken cancellationToken)
    {
        if (localFiles.Count == 0)
        {
            return snippets;
        }

        var mergedSnippets = new List<SharedSnippetItem>(snippets.Count + localFiles.Count);
        mergedSnippets.AddRange(snippets);
        logger.LogDiagnostic($"Injecting local file context into snippets: pinnedSnippetCount={snippets.Count}, localFileCount={localFiles.Count}");
        mergedSnippets.Add(new SharedSnippetItem
        {
            Name = "Local file grounding",
            Source = "local-file",
            Snippet = BuildLocalFileGroundingSnippet(localFiles)
        });
        foreach (var localFile in localFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rawContent = await ReadBoundedLocalFileContentAsync(localFile.AbsolutePath, cancellationToken).ConfigureAwait(false);
            var normalizedContent = FileContextPromptBuilder.NormalizeExtractedText(rawContent);
            var boundedContent = FileContextPromptBuilder.TruncateForBudget(
                string.IsNullOrWhiteSpace(normalizedContent) ? "(empty file)" : normalizedContent,
                FileContextPromptBuilder.MaxWorkIqFileChars);
            var snippetContent = $"[File: {localFile.RelativePath}]\n{boundedContent}";
            logger.LogDiagnostic(
                $"Local file context injected: path={localFile.RelativePath}, rawChars={rawContent.Length}, normalizedChars={normalizedContent.Length}, " +
                $"boundedChars={boundedContent.Length}");

            mergedSnippets.Add(new SharedSnippetItem
            {
                Name = ContextRelayLocalizedStrings.GetLocalFileContextLabel(localFile.RelativePath),
                Source = "local-file",
                SourceUrl = localFile.Uri,
                Snippet = snippetContent
            });
        }

        logger.LogDiagnostic($"Snippet merge completed: mergedSnippetCount={mergedSnippets.Count}");
        return mergedSnippets;
    }

    private static async Task<string> ReadBoundedLocalFileContentAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var buffer = new char[FileContextPromptBuilder.MaxWorkIqFileChars * 4];
        var read = await reader.ReadBlockAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
        var text = new string(buffer, 0, read);
        if (!reader.EndOfStream)
        {
            text += "\n[additional file content omitted]";
        }

        cancellationToken.ThrowIfCancellationRequested();
        return text;
    }

    private static string BuildLocalFileGroundingSnippet(IReadOnlyList<ResolvedFileMention> localFiles)
    {
        var builder = new StringBuilder();
        builder.AppendLine("ContextRelay local workspace file grounding:");
        builder.AppendLine("- Prioritize the local files listed below for this request.");
        builder.AppendLine("- Do not infer or cite SharePoint/OneDrive files unless the user explicitly asks for cloud sources.");
        builder.AppendLine("- Treat local file paths as authoritative references for this turn.");
        builder.AppendLine();
        builder.AppendLine("Local files:");
        foreach (var localFile in localFiles)
        {
            builder.Append("- ");
            builder.AppendLine(localFile.RelativePath);
        }

        return builder.ToString().TrimEnd();
    }

    private void LogChatPayloadDiagnostics(string route, string prompt, ChatContextPayload payload)
    {
        var additionalContext = payload.SendOptions.AdditionalContext;
        var contextualFiles = payload.SendOptions.ContextualResources?.Files ?? Array.Empty<CopilotContextualFileResource>();
        logger.LogDiagnostic(
            $"[{route}] Outbound payload summary: promptLength={prompt.Length}, labels={payload.Labels.Count}, " +
            $"additionalContextCount={additionalContext.Count}, contextualFileCount={contextualFiles.Count}");
        for (var index = 0; index < additionalContext.Count; index++)
        {
            var context = additionalContext[index];
            logger.LogDiagnostic(
                $"[{route}] additionalContext[{index}] description={context.Description ?? "(none)"} textChars={context.Text.Length}");
        }

        for (var index = 0; index < contextualFiles.Count; index++)
        {
            logger.LogDiagnostic($"[{route}] contextualResources.files[{index}] uri={contextualFiles[index].Uri}");
        }
    }

    private async Task AppendChatHistoryAsync(string userPrompt, string assistantReply, CancellationToken cancellationToken)
    {
        await AppendChatHistoryAsync(userPrompt, assistantReply, kind: null, contextLabels: Array.Empty<string>(), cancellationToken).ConfigureAwait(false);
    }

    private async Task AppendChatHistoryAsync(
        string userPrompt,
        string assistantReply,
        string? kind,
        IReadOnlyList<string> contextLabels,
        CancellationToken cancellationToken)
    {
        await sharedStore.AppendChatHistoryAsync(new[]
        {
            CreateChatItem("user", userPrompt),
            CreateChatItem("assistant", assistantReply, kind, contextLabels)
        }, cancellationToken).ConfigureAwait(false);
    }

    private SharedChatHistoryItem CreateChatItem(string role, string text, string? kind = null, IReadOnlyList<string>? contextLabels = null)
    {
        var metadata = new Dictionary<string, JsonElement>();
        if (!string.IsNullOrWhiteSpace(kind))
        {
            metadata["kind"] = JsonSerializer.SerializeToElement(kind);
        }

        if (contextLabels is { Count: > 0 })
        {
            metadata["contextLabels"] = JsonSerializer.SerializeToElement(contextLabels);
        }

        return new SharedChatHistoryItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Role = role,
            Text = text,
            Timestamp = DateTimeOffset.UtcNow.ToString("O"),
            Metadata = metadata
        };
    }

    private async Task<ContextRelayHostState> HandleChatCommandAsync(
        string originalInput,
        string message,
        string accessToken,
        IReadOnlyList<ResolvedFileMention> localFiles,
        CancellationToken cancellationToken)
    {
        var snippets = await snippetRepository.GetAllAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        logger.LogDiagnostic($"chat context sources: pinnedSnippets={snippets.Count}, localFiles={localFiles.Count}, hasSearchSummary={!string.IsNullOrWhiteSpace(lastSearchSummary)}");
        var snippetsWithLocalFileContext = await BuildSnippetsWithLocalFileContextAsync(snippets, localFiles, cancellationToken).ConfigureAwait(false);
        var contextPayload = ChatContextPayloadBuilder.Build(
            snippetsWithLocalFileContext,
            lastSearchSummary);
        LogChatPayloadDiagnostics("chat", message, contextPayload);
        var conversationId = await EnsureCopilotConversationAsync(accessToken, cancellationToken).ConfigureAwait(false);
        var reply = await copilotChatAdapter
            .SendMessageAsync(accessToken, conversationId, message, contextPayload.SendOptions, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(reply))
        {
            throw new InvalidOperationException("Microsoft 365 Copilot returned an empty response.");
        }

        await AppendChatHistoryAsync(message, reply, "chat", contextPayload.Labels, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Handled plain chat with Microsoft 365 Copilot.");
        return await RefreshStateCoreAsync(
            contextPayload.Labels.Count == 0
                ? ContextRelayLocalizedStrings.ChatReplyShownStatus
                : ContextRelayLocalizedStrings.GetChatReplyShownWithContextStatus(contextPayload.Labels.Count),
            originalInput,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ContextRelayHostState> HandleWorkIqCommandAsync(
        string originalInput,
        string query,
        ContextRelayAuthSettings authSettings,
        ContextRelaySettingsSnapshot settings,
        IReadOnlyList<ResolvedFileMention> localFiles,
        CancellationToken cancellationToken)
    {
        if (localFiles.Count > 0 && !settings.AllowLocalFileContextForWorkIq)
        {
            return await RefreshStateCoreAsync(ContextRelayLocalizedStrings.WorkIqLocalFileContextDisabledStatus, originalInput, cancellationToken).ConfigureAwait(false);
        }

        ContextRelayAccessToken token;
        try
        {
            token = await authProvider.Value.GetWorkIqAccessTokenAsync(authSettings, cancellationToken).ConfigureAwait(false);
        }
        catch (ContextRelayAuthenticationException ex)
        {
            logger.LogError("Work IQ authentication failed.", ex);
            return await RefreshStateCoreAsync(ex.Message, originalInput, cancellationToken).ConfigureAwait(false);
        }

        var workIqQuery = localFiles.Count == 0
            ? query
            : await FileContextPromptBuilder.BuildWorkIqPromptAsync(query, localFiles, cancellationToken).ConfigureAwait(false);
        logger.LogDiagnostic(
            $"/workiq outbound summary: localFileCount={localFiles.Count}, queryLength={query.Length}, composedQueryLength={workIqQuery.Length}");
        var reply = await workIqAdapter
            .SendMessageAsync(token.AccessToken, workIqQuery, workIqContextId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(reply.Text))
        {
            throw new InvalidOperationException("Work IQ returned an empty response.");
        }

        if (!string.IsNullOrWhiteSpace(reply.ContextId))
        {
            workIqContextId = reply.ContextId;
        }

        var contextLabels = localFiles.Select(file => ContextRelayLocalizedStrings.GetLocalFileContextLabel(file.RelativePath)).ToArray();
        await AppendChatHistoryAsync(query, reply.Text.Trim(), "workiq", contextLabels, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Handled /workiq with Work IQ.");
        return await RefreshStateCoreAsync(ContextRelayLocalizedStrings.WorkIqReplyShownStatus, originalInput, cancellationToken).ConfigureAwait(false);
    }

    private async Task<FilePromptContext?> ResolveFilePromptAsync(
        string rawPrompt,
        string fallbackPrompt,
        string originalInput,
        CancellationToken cancellationToken)
    {
        var candidates = FileMentionResolver.ExtractCandidates(rawPrompt);
        if (candidates.Count == 0)
        {
            logger.LogDiagnostic("No #file mention candidates detected in prompt.");
            return new FilePromptContext(fallbackPrompt, Array.Empty<ResolvedFileMention>());
        }

        logger.LogDiagnostic($"Detected {candidates.Count} #file mention candidate(s): {string.Join(", ", candidates.Select(candidate => candidate.RawPath))}");
        var workspaceRoots = await packageServices.GetWorkspaceRootsAsync(cancellationToken).ConfigureAwait(false);
        logger.LogDiagnostic(
            $"Workspace roots for mention resolution: count={workspaceRoots.Count}, values={string.Join(" | ", workspaceRoots.Take(5))}" +
            (workspaceRoots.Count > 5 ? " | ..." : string.Empty));
        var resolution = FileMentionResolver.Resolve(rawPrompt, workspaceRoots);
        logger.LogDiagnostic(
            $"Mention resolution result: localFiles={resolution.Files.Count}, errors={resolution.Errors.Count}, cleanedPromptLength={resolution.CleanedPrompt.Length}");
        if (resolution.Errors.Count > 0)
        {
            foreach (var error in resolution.Errors)
            {
                logger.LogDiagnostic($"Mention resolution error: code={error.Code}, detail={error.Detail}");
            }

            return await RefreshFilePromptErrorAsync(GetFileMentionResolutionErrorMessage(resolution.Errors[0]), originalInput, cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(resolution.CleanedPrompt))
        {
            logger.LogDiagnostic("Mention resolution produced an empty prompt after removing #file mentions.");
            return await RefreshFilePromptErrorAsync(ContextRelayLocalizedStrings.FileMentionPromptEmptyStatus, originalInput, cancellationToken).ConfigureAwait(false);
        }

        if (resolution.Files.Count > 0)
        {
            logger.LogDiagnostic($"Resolved local files: {string.Join(", ", resolution.Files.Select(file => file.RelativePath))}");
        }

        return new FilePromptContext(resolution.CleanedPrompt, resolution.Files);
    }

    private async Task<FilePromptContext?> RefreshFilePromptErrorAsync(string message, string originalInput, CancellationToken cancellationToken)
    {
        await RefreshStateCoreAsync(message, originalInput, cancellationToken).ConfigureAwait(false);
        return null;
    }

    private static string GetFileMentionResolutionErrorMessage(FileMentionResolutionError error)
    {
        return error.Code switch
        {
            FileMentionErrorCode.WorkspaceUnavailable => ContextRelayLocalizedStrings.FileMentionWorkspaceUnavailableStatus,
            FileMentionErrorCode.MentionLimitReached => ContextRelayLocalizedStrings.GetFilePickerMentionLimitReachedStatus(FileMentionResolver.MaxFileMentions),
            FileMentionErrorCode.NotFound => ContextRelayLocalizedStrings.GetFileMentionNotFoundStatus(error.Detail ?? string.Empty),
            FileMentionErrorCode.OutsideWorkspace => ContextRelayLocalizedStrings.GetFileMentionOutsideWorkspaceStatus(error.Detail ?? string.Empty),
            FileMentionErrorCode.AmbiguousPath => ContextRelayLocalizedStrings.GetFileMentionAmbiguousStatus(error.Detail ?? string.Empty),
            FileMentionErrorCode.UnsupportedFileType => ContextRelayLocalizedStrings.GetFileMentionUnsupportedFileTypeStatus(error.Detail ?? string.Empty),
            _ => ContextRelayLocalizedStrings.FileMentionWorkspaceUnavailableStatus
        };
    }

    private async Task<string> EnsureCopilotConversationAsync(string accessToken, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(copilotConversationId))
        {
            return copilotConversationId!;
        }

        copilotConversationId = await copilotChatAdapter.CreateConversationAsync(accessToken, cancellationToken).ConfigureAwait(false);
        return copilotConversationId;
    }

    private async Task OpenAskPreviewAsync(
        string query,
        AskPreviewDocument previewDocument,
        ContextRelaySettingsSnapshot settings,
        CancellationToken cancellationToken)
    {
        var targetContext = await TryResolveCreatedFileTargetContextAsync(cancellationToken).ConfigureAwait(false);
        if (targetContext is null)
        {
            await RefreshStateAsync(ContextRelayLocalizedStrings.CreatedFilesFolderSelectionCanceledStatus).ConfigureAwait(false);
            return;
        }

        var outputDirectory = ResolveOutputDirectory(settings, targetContext.Value.RootDirectory);
        Directory.CreateDirectory(outputDirectory);
        var fileExtension = AskPreviewLanguageDetector.GetFileExtension(previewDocument.LanguageId);
        var fileName = ContextRelayLocalizedStrings.GetAskPreviewDocumentTitle(query, fileExtension);
        var filePath = Path.Combine(outputDirectory, fileName);
        await WriteAllTextAsync(filePath, previewDocument.Content, cancellationToken).ConfigureAwait(false);
        await RegisterCreatedFilesAsync(new[] { filePath }, targetContext.Value, cancellationToken).ConfigureAwait(false);
        await packageServices.OpenDocumentAsync(filePath, cancellationToken).ConfigureAwait(false);
    }

    private async Task RefreshStateAsync(string statusMessage)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await RefreshStateCoreAsync(statusMessage, GetDraftQueryText(), CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ContextRelayHostState> RefreshStateCoreAsync(string statusMessage, string queryText, CancellationToken cancellationToken)
    {
        UpdateDraftQueryText(queryText);
        var chatHistory = await sharedStore.GetChatHistoryAsync(cancellationToken).ConfigureAwait(false);
        var snippets = await snippetRepository.GetAllAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var workspaceRoot = await packageServices.GetSolutionRootAsync(cancellationToken).ConfigureAwait(false);
        var handoffIndex = await sharedStore.GetHandoffIndexAsync(cancellationToken).ConfigureAwait(false);
        var effectiveQueryText = GetDraftQueryText();

        string? signedInUser = state.SignedInUser;
        if (shouldResolveSignedInUser)
        {
            shouldResolveSignedInUser = false;
            signedInUser = await TryGetSignedInUserAsync(cancellationToken).ConfigureAwait(false);
        }

        state = new ContextRelayHostState
        {
            QueryText = effectiveQueryText,
            HelpText = ContextRelayLocalizedStrings.GetHelpTextForQuery(effectiveQueryText),
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

    private string GetDraftQueryText()
    {
        lock (draftQuerySync)
        {
            return draftQueryText;
        }
    }

    private async Task<string?> TryGetSignedInUserAsync(CancellationToken cancellationToken)
    {
        var settings = await packageServices.GetSettingsSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(settings.ClientId))
        {
            return null;
        }

        try
        {
            var account = await authProvider.Value.GetAccountAsync(settings.ToAuthSettings(), cancellationToken).ConfigureAwait(false);
            return account?.Username;
        }
        catch (Exception ex)
        {
            logger.LogWarning($"Unable to read cached account: {ex.Message}");
            return null;
        }
    }

    private async Task SyncDebugLoggingOptionsAsync(CancellationToken cancellationToken)
    {
        var settings = await packageServices.GetSettingsSnapshotAsync(cancellationToken).ConfigureAwait(false);
        logger.SetDebugLoggingEnabled(settings.EnableGraphDebugLogging, settings.EnableWorkIqDebugLogging);
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
        await packageServices.CopyTextToClipboardAsync(text, cancellationToken).ConfigureAwait(false);
        await RefreshStateAsync(statusMessage).ConfigureAwait(false);
    }

    private static string ResolveOutputDirectory(ContextRelaySettingsSnapshot settings, string rootDirectory)
    {
        var outputDirectory = string.IsNullOrWhiteSpace(settings.OutputDirectory) ? ".contextrelay" : settings.OutputDirectory;
        if (Path.IsPathRooted(outputDirectory))
        {
            return Path.GetFullPath(outputDirectory);
        }

        return Path.GetFullPath(Path.Combine(rootDirectory, outputDirectory));
    }

    private async Task RegisterCreatedFilesAsync(
        IReadOnlyList<string> filePaths,
        CreatedFileTargetContext targetContext,
        CancellationToken cancellationToken)
    {
        if (filePaths.Count == 0)
        {
            return;
        }

        if (!targetContext.ShouldAddToSolutionExplorer)
        {
            return;
        }

        var addedCount = await packageServices.TryAddFilesToSolutionAsync(filePaths, cancellationToken).ConfigureAwait(false);
        if (addedCount > 0)
        {
            logger.LogInformation($"Added {addedCount} ContextRelay-created file(s) to Solution Explorer.");
            return;
        }

        logger.LogInformation(
            "ContextRelay created files on disk, but automatic Solution Explorer registration is not available in the current workspace.");
    }

    private async Task<CreatedFileTargetContext?> TryResolveCreatedFileTargetContextAsync(CancellationToken cancellationToken)
    {
        var initialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var targetContext = await ResolveCreatedFileTargetContextAsync(
            packageServices.GetSolutionRootAsync,
            packageServices.PickWorkspaceFolderAsync,
            initialDirectory,
            cancellationToken).ConfigureAwait(false);
        if (targetContext is { FolderWasSelected: true } selectedTarget)
        {
            logger.LogInformation($"Selected '{selectedTarget.RootDirectory}' as the target folder for ContextRelay-created files.");
        }

        return targetContext;
    }

    private static async Task<CreatedFileTargetContext?> ResolveCreatedFileTargetContextAsync(
        Func<CancellationToken, Task<string?>> getSolutionRootAsync,
        Func<string?, CancellationToken, Task<string?>> pickFolderAsync,
        string? initialDirectory,
        CancellationToken cancellationToken)
    {
        var workspaceRoot = await getSolutionRootAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return new CreatedFileTargetContext(Path.GetFullPath(workspaceRoot), ShouldAddToSolutionExplorer: true, FolderWasSelected: false);
        }

        var selectedFolder = await pickFolderAsync(initialDirectory, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(selectedFolder))
        {
            return null;
        }

        return new CreatedFileTargetContext(Path.GetFullPath(selectedFolder), ShouldAddToSolutionExplorer: false, FolderWasSelected: true);
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
            var settings = await packageServices.GetSettingsSnapshotAsync(cancellationToken).ConfigureAwait(false);
            var token = await authProvider.Value
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

    private static FileSelectionQueryUpdate MergeSelectedFilesIntoQuery(
        string currentQuery,
        IReadOnlyList<string> selectedFiles,
        IReadOnlyList<string> workspaceRoots)
    {
        var existingCandidates = FileMentionResolver.ExtractCandidates(currentQuery);
        var remainingCapacity = FileMentionResolver.MaxFileMentions - existingCandidates.Count;
        if (remainingCapacity <= 0)
        {
            return new FileSelectionQueryUpdate(
                currentQuery,
                ContextRelayLocalizedStrings.GetFilePickerMentionLimitReachedStatus(FileMentionResolver.MaxFileMentions));
        }

        var normalizedSelections = selectedFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var normalizedWorkspaceRoots = workspaceRoots
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedWorkspaceRoots.Length == 0 && normalizedSelections.Length > 0)
        {
            normalizedWorkspaceRoots = normalizedSelections
                .Select(path => WorkspaceRootInference.InferWorkspaceRootFromPath(path))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => Path.GetFullPath(path!))
                .Where(Directory.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        if (normalizedWorkspaceRoots.Length == 0)
        {
            return new FileSelectionQueryUpdate(currentQuery, ContextRelayLocalizedStrings.FilePickerWorkspaceUnavailableStatus);
        }

        var existingMentionPaths = new HashSet<string>(
            existingCandidates.Select(candidate => NormalizeComparablePath(candidate.RawPath)),
            StringComparer.OrdinalIgnoreCase);

        var mentionTokens = new List<string>();
        var skippedCount = 0;
        foreach (var selectedPath in normalizedSelections)
        {
            if (mentionTokens.Count >= remainingCapacity)
            {
                skippedCount++;
                continue;
            }

            if (!CopilotSupportedFilePolicy.IsSupported(selectedPath))
            {
                skippedCount++;
                continue;
            }

            var workspaceRoot = normalizedWorkspaceRoots
                .Where(root => IsPathWithinRoot(selectedPath, root))
                .OrderByDescending(root => root.Length)
                .FirstOrDefault();
            if (workspaceRoot is null)
            {
                skippedCount++;
                continue;
            }

            var relativePath = GetRelativeWorkspacePath(workspaceRoot, selectedPath);
            var comparablePath = NormalizeComparablePath(relativePath);
            if (!existingMentionPaths.Add(comparablePath))
            {
                skippedCount++;
                continue;
            }

            mentionTokens.Add(BuildMentionToken(relativePath));
        }

        if (mentionTokens.Count == 0)
        {
            if (normalizedSelections.Length > 0 && existingCandidates.Count >= FileMentionResolver.MaxFileMentions)
            {
                return new FileSelectionQueryUpdate(
                    currentQuery,
                    ContextRelayLocalizedStrings.GetFilePickerMentionLimitReachedStatus(FileMentionResolver.MaxFileMentions));
            }

            return new FileSelectionQueryUpdate(currentQuery, ContextRelayLocalizedStrings.FilePickerNoWorkspaceFilesSelectedStatus);
        }

        var updatedQuery = AppendMentionTokens(currentQuery, mentionTokens);
        var status = skippedCount > 0
            ? ContextRelayLocalizedStrings.GetFilePickerFilesAddedPartialStatus(mentionTokens.Count, skippedCount)
            : ContextRelayLocalizedStrings.GetFilePickerFilesAddedStatus(mentionTokens.Count);
        return new FileSelectionQueryUpdate(updatedQuery, status);
    }

    private static bool IsPathWithinRoot(string path, string root)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedPath = Path.GetFullPath(path);
        if (string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetRelativeWorkspacePath(string workspaceRoot, string absolutePath)
    {
        var normalizedRoot = workspaceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;
        var relative = absolutePath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            ? absolutePath.Substring(rootWithSeparator.Length)
            : Path.GetFileName(absolutePath);
        return relative.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static string BuildMentionToken(string relativePath)
    {
        var normalizedPath = relativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        var requiresQuotes = normalizedPath.IndexOfAny(new[] { ' ', '\t', '\r', '\n', '#' }) >= 0;
        return requiresQuotes
            ? $"#\"{normalizedPath}\""
            : $"#{normalizedPath}";
    }

    private static string AppendMentionTokens(string currentQuery, IReadOnlyList<string> mentionTokens)
    {
        var separator = string.IsNullOrWhiteSpace(currentQuery) || char.IsWhiteSpace(currentQuery[currentQuery.Length - 1])
            ? string.Empty
            : " ";
        return string.IsNullOrWhiteSpace(currentQuery)
            ? string.Join(" ", mentionTokens)
            : currentQuery + separator + string.Join(" ", mentionTokens);
    }

    private static string NormalizeComparablePath(string path)
    {
        return (path ?? string.Empty)
            .Trim()
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private void OnSharedStoreChanged(object? sender, SharedStoreChangedEventArgs e)
    {
        if (e.FileKind != SharedStoreFileKind.ChatHistory &&
            e.FileKind != SharedStoreFileKind.Snippets &&
            e.FileKind != SharedStoreFileKind.HandoffIndex)
        {
            return;
        }

        _ = Task.Run(async () =>
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

    private sealed class FilePromptContext
    {
        public FilePromptContext(string prompt, IReadOnlyList<ResolvedFileMention> files)
        {
            Prompt = prompt;
            Files = files;
        }

        public string Prompt { get; }

        public IReadOnlyList<ResolvedFileMention> Files { get; }
    }

    private sealed class FileSelectionQueryUpdate
    {
        public FileSelectionQueryUpdate(string queryText, string statusMessage)
        {
            QueryText = queryText;
            StatusMessage = statusMessage;
        }

        public string QueryText { get; }

        public string StatusMessage { get; }
    }

    private enum DriveContentMode
    {
        PlainText,
        Docx,
        HtmlConvertible,
        Unsupported
    }
}
