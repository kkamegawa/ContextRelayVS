using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.FileContext;
using ContextRelay.Core.Models;
using ContextRelay.Core.Embedded;
using ContextRelay.Core.Router;
using ContextRelay.Core.Settings;
using ContextRelay.Core.SharedStore;
using ContextRelay.VSExtension.Services;
using ContextRelay.VSExtension.ToolWindows.Markdown;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.UI;

namespace ContextRelay.VSExtension.ToolWindows;

[DataContract]
internal sealed class ContextRelayWindowViewModel : NotifyPropertyChangedObject, IDisposable
{
    // The preview cannot scroll itself to the newest output, so it shows the last few parsed blocks.
    private const int StreamingPreviewMaxBlocks = 8;
    private static readonly TimeSpan StreamingParseInterval = TimeSpan.FromMilliseconds(120);
    private readonly ObservableList<MarkdownBlockViewModel> streamingBlocks = new();
    private readonly System.Diagnostics.Stopwatch streamingParseClock = new();
    private bool hasEarlierStreamingBlocks;

    private const int MaxVisibleCommandSuggestions = 4;
    private const int ChatTabIndex = 0;
    private const int ResultsTabIndex = 1;
    private const int PinsTabIndex = 2;
    private readonly ContextRelayHost host;
    private int selectedTabIndex = ChatTabIndex;
    private int unseenChatCount;
    private int unseenResultCount;
    private int unseenPinCount;
    private bool isStatusError;
    private const string ContextItemKeyMetadataKey = "contextItemKey";
    private static readonly Regex EmbeddedQueryPattern = new(@"^\s*/embedded(\s|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Display state kept here because the tab control discards the visual tree of unselected tabs.
    private readonly HashSet<string> expandedResultKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> expandedSnippetIds = new(StringComparer.Ordinal);
    private HashSet<string> pinnedKeys = new(StringComparer.Ordinal);
    private IReadOnlyList<EmbeddedSourceSettings> embeddedSources = Array.Empty<EmbeddedSourceSettings>();
    private string? selectedEmbeddedContainerTypeId;
    private string? lastSelectedEmbeddedContainerTypeId;
    private int embeddedInvalidSourceCount;
    private bool isEmbeddedSourceSelectorOpen;
    private bool embeddedSelectorShownForEntry;
    private int embeddedHighlightIndex;
    private IReadOnlyList<EmbeddedSourceOptionViewModel> embeddedSourceOptions = Array.Empty<EmbeddedSourceOptionViewModel>();
    private bool isBusy;
    private bool isChatRequestActive;
    private bool isCommandPopupOpen;
    private int commandSuggestionWindowStart;
    private string queryText = string.Empty;
    private bool? collectionsUseJapanese;
    private string helpText = ContextRelayLocalizedStrings.GenericHelpText;
    private string statusMessage = ContextRelayLocalizedStrings.ReadyStatus;
    private string signedInUserText = ContextRelayLocalizedStrings.SignedOutText;
    private string searchSummary = string.Empty;
    private IReadOnlyList<ContextItemViewModel> searchResults = Array.Empty<ContextItemViewModel>();
    private IReadOnlyList<SnippetItemViewModel> snippets = Array.Empty<SnippetItemViewModel>();
    private IReadOnlyList<ChatHistoryItemViewModel> chatHistory = Array.Empty<ChatHistoryItemViewModel>();
    private IReadOnlyList<SlashCommandSuggestion> commandSuggestions = Array.Empty<SlashCommandSuggestion>();
    private IReadOnlyList<SlashCommandSuggestion> visibleCommandSuggestions = Array.Empty<SlashCommandSuggestion>();
    private SlashCommandSuggestion? selectedCommandSuggestion;
    private IReadOnlyList<string> workspaceFiles = Array.Empty<string>();
    private bool isEmbeddedActive;
    private bool isApplyingState;
    private string windowTitleText = ContextRelayLocalizedStrings.WindowTitleText;
    private bool isStreaming;
    private string streamingResponseText = string.Empty;
    private IReadOnlyList<PendingAttachmentViewModel> pendingAttachments = Array.Empty<PendingAttachmentViewModel>();

    public ContextRelayWindowViewModel(ContextRelayHost host)
    {
        this.host = host;
        host.StateChanged += OnHostStateChanged;

        RefreshLocalizedUiTexts();

        SearchCommand = new AsyncCommand(async (_, context, ct) =>
        {
            // The composer keeps one primary button so keyboard focus survives the switch
            // between sending and stopping.
            if (IsStreaming)
            {
                host.StopGeneration();
                return;
            }

            await SubmitAsync(context, ct).ConfigureAwait(false);
        });
        GenerateHandoffCommand = new AsyncCommand(async (_, ct) => await RunBusyAsync(() => host.GenerateHandoffAsync(ct)).ConfigureAwait(false));
        CopyPromptCommand = new AsyncCommand(async (_, ct) => await RunBusyAsync(() => host.CopyHandoffPromptAsync(ct)).ConfigureAwait(false));
        OpenHandoffCommand = new AsyncCommand(async (_, ct) => await RunBusyAsync(() => host.OpenHandoffDocumentAsync(ct)).ConfigureAwait(false));
        OpenCopilotCommand = new AsyncCommand(async (_, ct) => await RunBusyAsync(() => host.OpenCopilotChatWithPromptAsync(ct)).ConfigureAwait(false));
        ClearChatCommand = new AsyncCommand(async (_, ct) => await RunBusyAsync(() => host.ClearChatAsync(ct)).ConfigureAwait(false));
        ClearSnippetsCommand = new AsyncCommand(async (_, ct) => await RunBusyAsync(() => host.ClearSnippetsAsync(ct)).ConfigureAwait(false));
        ClearCacheCommand = new AsyncCommand(async (_, ct) => await RunBusyAsync(() => host.ClearCacheAsync(ct)).ConfigureAwait(false));
        AddFilesCommand = new AsyncCommand(async (_, ct) => await host.AddFilesToQueryAsync(ct).ConfigureAwait(false));
        StopGenerationCommand = new AsyncCommand((_, _) =>
        {
            host.StopGeneration();
            return Task.CompletedTask;
        });
        // Up/Down/Tab/Esc serve the command popup first, then the Embedded source selector.
        MoveSelectionDownCommand = new AsyncCommand((_, _) => { MoveSelection(1); return Task.CompletedTask; });
        MoveSelectionUpCommand = new AsyncCommand((_, _) => { MoveSelection(-1); return Task.CompletedTask; });
        ApplyCommandSelectionCommand = new AsyncCommand(async (_, ct) =>
        {
            if (!ApplySelectedCommandSuggestion() && isEmbeddedSourceSelectorOpen)
            {
                await ConfirmEmbeddedSourceAsync(embeddedHighlightIndex, ct).ConfigureAwait(false);
            }
        });
        ConfirmQueryInputCommand = new AsyncCommand(async (_, context, ct) => await ConfirmQueryInputAsync(context, ct).ConfigureAwait(false));
        CloseCommandPopupCommand = new AsyncCommand(async (_, ct) =>
        {
            if (IsCommandPopupOpen)
            {
                CloseCommandPopup();
            }
            else if (isEmbeddedSourceSelectorOpen)
            {
                await ClearEmbeddedSourceAsync(ct).ConfigureAwait(false);
            }
        });
        ChangeEmbeddedSourceCommand = new AsyncCommand((_, _) => { OpenEmbeddedSourceSelector(); return Task.CompletedTask; });
        UpdateSuggestionKeyBindingAvailability();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var state = await host.GetStateAsync().ConfigureAwait(false);
        ApplyState(state);
    }

    [DataMember] public string GenerateHandoffButtonText { get; private set; } = string.Empty;
    [DataMember] public string CopyPromptButtonText { get; private set; } = string.Empty;
    [DataMember] public string OpenHandoffButtonText { get; private set; } = string.Empty;
    [DataMember] public string OpenCopilotButtonText { get; private set; } = string.Empty;
    [DataMember] public string ClearChatButtonText { get; private set; } = string.Empty;
    [DataMember] public string ClearSnippetsButtonText { get; private set; } = string.Empty;
    [DataMember] public string ClearCacheButtonText { get; private set; } = string.Empty;
    [DataMember] public string SearchButtonText { get; private set; } = string.Empty;
    [DataMember] public string SearchResultsHeaderText { get; private set; } = string.Empty;
    [DataMember] public string SearchSummaryHeaderText { get; private set; } = string.Empty;
    [DataMember] public string SnippetsHeaderText { get; private set; } = string.Empty;
    [DataMember] public string ChatHistoryHeaderText { get; private set; } = string.Empty;
    [DataMember] public string SearchToolTipText { get; private set; } = string.Empty;
    [DataMember] public string CommandPopupHeaderText { get; private set; } = string.Empty;
    [DataMember] public string AddFilesButtonText { get; private set; } = string.Empty;
    [DataMember] public string AddFilesToolTipText { get; private set; } = string.Empty;
    [DataMember] public string StopGenerationButtonText { get; private set; } = string.Empty;
    [DataMember] public IReadOnlyList<PendingAttachmentViewModel> PendingAttachments { get => pendingAttachments; private set { pendingAttachments = value; RaiseNotifyPropertyChangedEvent(nameof(PendingAttachments)); } }

    [DataMember]
    public bool IsStreaming
    {
        get => isStreaming;
        private set
        {
            if (isStreaming == value)
            {
                return;
            }

            isStreaming = value;
            RaiseNotifyPropertyChangedEvent(nameof(IsStreaming));
            RaiseNotifyPropertyChangedEvent(nameof(PrimaryActionButtonText));
            RaiseNotifyPropertyChangedEvent(nameof(IsPrimaryActionEnabled));
        }
    }

    /// <summary>
    /// Gets the label of the composer's primary button, which sends a query and becomes the
    /// stop action while a response is being generated.
    /// </summary>
    [DataMember]
    public string PrimaryActionButtonText => isStreaming ? StopGenerationButtonText : SearchButtonText;

    /// <summary>
    /// Gets a value indicating whether the composer's primary button is enabled. Stopping stays
    /// available while the request that made the view model busy is still running.
    /// </summary>
    [DataMember]
    public bool IsPrimaryActionEnabled => isChatRequestActive || isStreaming || !isBusy;

    /// <summary>
    /// Gets the raw text streamed so far. It is not sent to the UI; the preview renders
    /// <see cref="StreamingBlocks"/> parsed from it.
    /// </summary>
    public string StreamingResponseText
    {
        get => streamingResponseText;
        private set
        {
            if (streamingResponseText != value)
            {
                streamingResponseText = value;
                RaiseNotifyPropertyChangedEvent(nameof(StreamingResponseText));
                UpdateStreamingBlocks();
            }
        }
    }

    /// <summary>Gets the newest Markdown blocks of the streaming answer, updated in place as text arrives.</summary>
    [DataMember]
    public ObservableList<MarkdownBlockViewModel> StreamingBlocks => streamingBlocks;

    [DataMember]
    public bool HasEarlierStreamingBlocks
    {
        get => hasEarlierStreamingBlocks;
        private set
        {
            if (hasEarlierStreamingBlocks != value)
            {
                hasEarlierStreamingBlocks = value;
                RaiseNotifyPropertyChangedEvent(nameof(HasEarlierStreamingBlocks));
            }
        }
    }

    [DataMember]
    public string WindowTitleText
    {
        get => windowTitleText;
        private set
        {
            if (windowTitleText != value)
            {
                windowTitleText = value;
                RaiseNotifyPropertyChangedEvent(nameof(WindowTitleText));
            }
        }
    }

    [DataMember] public AsyncCommand SearchCommand { get; }
    [DataMember] public AsyncCommand GenerateHandoffCommand { get; }
    [DataMember] public AsyncCommand CopyPromptCommand { get; }
    [DataMember] public AsyncCommand OpenHandoffCommand { get; }
    [DataMember] public AsyncCommand OpenCopilotCommand { get; }
    [DataMember] public AsyncCommand ClearChatCommand { get; }
    [DataMember] public AsyncCommand ClearSnippetsCommand { get; }
    [DataMember] public AsyncCommand ClearCacheCommand { get; }
    [DataMember] public AsyncCommand AddFilesCommand { get; }
    [DataMember] public AsyncCommand StopGenerationCommand { get; }
    [DataMember] public AsyncCommand MoveSelectionDownCommand { get; }
    [DataMember] public AsyncCommand MoveSelectionUpCommand { get; }
    [DataMember] public AsyncCommand ApplyCommandSelectionCommand { get; }
    [DataMember] public AsyncCommand ConfirmQueryInputCommand { get; }
    [DataMember] public AsyncCommand CloseCommandPopupCommand { get; }
    [DataMember] public AsyncCommand ChangeEmbeddedSourceCommand { get; }

    /// <summary>Gets a value indicating whether the composer shows the Embedded source chip (an active /embedded draft).</summary>
    [DataMember] public bool ShowEmbeddedSourceBar => isEmbeddedActive && EmbeddedQueryPattern.IsMatch(queryText);

    [DataMember]
    public string EmbeddedSourceChipText => ContextRelayLocalizedStrings.GetEmbeddedSourceChipText(
        embeddedSources.FirstOrDefault(source => string.Equals(source.ContainerTypeId, selectedEmbeddedContainerTypeId, StringComparison.OrdinalIgnoreCase))?.Name);

    [DataMember] public bool CanChangeEmbeddedSource => embeddedSources.Count > 1;
    [DataMember] public string ChangeEmbeddedSourceButtonText => ContextRelayLocalizedStrings.ChangeEmbeddedSourceButtonText;
    [DataMember] public string EmbeddedSourceSelectorHeaderText => ContextRelayLocalizedStrings.EmbeddedSourceSelectorHeaderText;
    [DataMember] public string EmbeddedPreviewNoticeText => ContextRelayLocalizedStrings.EmbeddedPreviewNoticeText;
    [DataMember] public bool HasEmbeddedInvalidSources => ShowEmbeddedSourceBar && embeddedInvalidSourceCount > 0;
    [DataMember] public string EmbeddedInvalidSourcesText => ContextRelayLocalizedStrings.GetEmbeddedInvalidSourcesStatus(embeddedInvalidSourceCount);
    [DataMember] public bool HasEmbeddedResults => searchResults.Any(result => result.IsEmbedded);

    [DataMember]
    public bool IsEmbeddedSourceSelectorOpen
    {
        get => isEmbeddedSourceSelectorOpen;
        private set
        {
            if (isEmbeddedSourceSelectorOpen != value)
            {
                isEmbeddedSourceSelectorOpen = value;
                RaiseNotifyPropertyChangedEvent(nameof(IsEmbeddedSourceSelectorOpen));
                UpdateSuggestionKeyBindingAvailability();
            }
        }
    }

    [DataMember]
    public IReadOnlyList<EmbeddedSourceOptionViewModel> EmbeddedSourceOptions
    {
        get => embeddedSourceOptions;
        private set
        {
            embeddedSourceOptions = value;
            RaiseNotifyPropertyChangedEvent(nameof(EmbeddedSourceOptions));
        }
    }

    [DataMember]
    public string QueryText
    {
        get => queryText;
        set
        {
            if (queryText == value)
            {
                return;
            }

            var previousQuery = queryText;
            queryText = value;
            host.UpdateDraftQueryText(queryText);
            RaiseNotifyPropertyChangedEvent(nameof(QueryText));
            if (!isApplyingState)
            {
                if (ShouldLogComposerTransition(previousQuery, queryText))
                {
                    host.LogUiDiagnostic(
                        $"QueryText changed previousLength={previousQuery.Length} currentLength={queryText.Length} startsWithSlash={queryText.StartsWith("/", StringComparison.Ordinal)}");
                }

                UpdateCommandSuggestions();
                UpdateTransientHelpText();
                RefreshEmbeddedSourceBar(allowOpen: true);
            }
        }
    }

    /// <summary>
    /// Gets or sets the selected tab (chat, results, pins). The tab control binds this two-way; the
    /// composer sits outside the tabs, so switching never loses the draft.
    /// </summary>
    [DataMember]
    public int SelectedTabIndex
    {
        get => selectedTabIndex;
        set
        {
            if (value < ChatTabIndex || value > PinsTabIndex || selectedTabIndex == value)
            {
                return;
            }

            selectedTabIndex = value;
            RaiseNotifyPropertyChangedEvent(nameof(SelectedTabIndex));
            ClearUnseenCount(value);
        }
    }

    [DataMember]
    public string ChatTabHeaderText => ContextRelayLocalizedStrings.GetTabHeaderText(ContextRelayLocalizedStrings.ChatTabHeaderText, unseenChatCount);

    [DataMember]
    public string ResultsTabHeaderText => ContextRelayLocalizedStrings.GetTabHeaderText(ContextRelayLocalizedStrings.ResultsTabHeaderText, unseenResultCount);

    [DataMember]
    public string PinsTabHeaderText => ContextRelayLocalizedStrings.GetTabHeaderText(ContextRelayLocalizedStrings.PinsTabHeaderText, unseenPinCount);

    /// <summary>
    /// Gets a value indicating whether the usage help is shown: only before anything was asked, while
    /// the suggestion popup explains the highlighted command, or after a failure.
    /// </summary>
    [DataMember]
    public bool ShowHelpText => (chatHistory.Count == 0 && searchResults.Count == 0) || isCommandPopupOpen || isStatusError;

    [DataMember]
    public string HelpText
    {
        get => helpText;
        private set
        {
            if (helpText != value)
            {
                helpText = value;
                RaiseNotifyPropertyChangedEvent(nameof(HelpText));
            }
        }
    }

    [DataMember]
    public string StatusMessage
    {
        get => statusMessage;
        private set
        {
            if (statusMessage != value)
            {
                statusMessage = value;
                RaiseNotifyPropertyChangedEvent(nameof(StatusMessage));
            }
        }
    }

    [DataMember]
    public string SignedInUserText
    {
        get => signedInUserText;
        private set
        {
            if (signedInUserText != value)
            {
                signedInUserText = value;
                RaiseNotifyPropertyChangedEvent(nameof(SignedInUserText));
            }
        }
    }

    [DataMember]
    public string SearchSummary
    {
        get => searchSummary;
        private set
        {
            if (searchSummary != value)
            {
                searchSummary = value;
                RaiseNotifyPropertyChangedEvent(nameof(SearchSummary));
                RaiseNotifyPropertyChangedEvent(nameof(HasSearchSummary));
            }
        }
    }

    [DataMember]
    public bool HasSearchSummary => !string.IsNullOrWhiteSpace(SearchSummary);

    [DataMember]
    public bool IsCommandPopupOpen
    {
        get => isCommandPopupOpen;
        private set
        {
            if (isCommandPopupOpen != value)
            {
                isCommandPopupOpen = value;
                RaiseNotifyPropertyChangedEvent(nameof(IsCommandPopupOpen));
                RaiseNotifyPropertyChangedEvent(nameof(ShowHelpText));

                // The query box binds Tab, Up, Down, and Escape to these commands. While the
                // popup is closed the key bindings must not handle the key, so Tab keeps moving
                // focus to the next control instead of being swallowed by the input.
                UpdateSuggestionKeyBindingAvailability();
            }
        }
    }

    [DataMember]
    public bool IsNotBusy => !isBusy;

    [DataMember]
    public IReadOnlyList<ContextItemViewModel> SearchResults
    {
        get => searchResults;
        private set
        {
            searchResults = value;
            RaiseNotifyPropertyChangedEvent(nameof(SearchResults));
            RaiseNotifyPropertyChangedEvent(nameof(ShowHelpText));
            RaiseNotifyPropertyChangedEvent(nameof(HasEmbeddedResults));
        }
    }

    [DataMember]
    public IReadOnlyList<SnippetItemViewModel> Snippets
    {
        get => snippets;
        private set
        {
            snippets = value;
            RaiseNotifyPropertyChangedEvent(nameof(Snippets));
        }
    }

    [DataMember]
    public IReadOnlyList<ChatHistoryItemViewModel> ChatHistory
    {
        get => chatHistory;
        private set
        {
            chatHistory = value;
            RaiseNotifyPropertyChangedEvent(nameof(ChatHistory));
            RaiseNotifyPropertyChangedEvent(nameof(ShowHelpText));
        }
    }

    [DataMember]
    public IReadOnlyList<SlashCommandSuggestion> CommandSuggestions
    {
        get => commandSuggestions;
        private set
        {
            commandSuggestions = value;
            RaiseNotifyPropertyChangedEvent(nameof(CommandSuggestions));
        }
    }

    [DataMember]
    public IReadOnlyList<SlashCommandSuggestion> VisibleCommandSuggestions
    {
        get => visibleCommandSuggestions;
        private set
        {
            visibleCommandSuggestions = value;
            RaiseNotifyPropertyChangedEvent(nameof(VisibleCommandSuggestions));
        }
    }

    [DataMember]
    public SlashCommandSuggestion? SelectedCommandSuggestion
    {
        get => selectedCommandSuggestion;
        set
        {
            if (ReferenceEquals(selectedCommandSuggestion, value))
            {
                return;
            }

            selectedCommandSuggestion = value;

            // IsSelected is baked into fresh display clones by BuildVisibleWindow, not toggled here —
            // every caller of this setter is immediately followed by a call that rebuilds
            // VisibleCommandSuggestions from new instances (Remote UI does not re-serialize in-place
            // mutations on objects it already knows by identity).
            RaiseNotifyPropertyChangedEvent(nameof(SelectedCommandSuggestion));
            if (!isApplyingState && IsCommandPopupOpen)
            {
                UpdateTransientHelpText();
            }
        }
    }

    internal async Task PinResultAsync(ContextItemViewModel item, CancellationToken ct)
    {
        await RunBusyAsync(() => host.PinSnippetAsync(item.Item, ct)).ConfigureAwait(false);
    }

    internal async Task CopyResultAsync(ContextItemViewModel item, CancellationToken ct)
    {
        await RunBusyAsync(() => host.CopyResultToClipboardAsync(item.Item, ct)).ConfigureAwait(false);
    }

    internal async Task AppendResultAsync(ContextItemViewModel item, CancellationToken ct)
    {
        await RunBusyAsync(() => host.AppendResultToHandoffAsync(item.Item, ct)).ConfigureAwait(false);
    }

    internal void OpenUrl(string? url)
    {
        if (SafeLinks.TryGetOpenableUrl(url, out var openableUrl))
        {
            host.OpenExternalUrl(openableUrl);
        }
    }

    internal async Task CopyPreviewTextAsync(string text, CancellationToken ct)
    {
        await RunBusyAsync(() => host.CopyPreviewTextAsync(text, ct)).ConfigureAwait(false);
    }

    internal void SetResultPreviewExpanded(ContextItemViewModel item, bool expanded)
    {
        if (expanded)
        {
            expandedResultKeys.Add(item.Key);
        }
        else
        {
            expandedResultKeys.Remove(item.Key);
        }

        item.SetPreviewExpanded(expanded);
    }

    internal void SetSnippetPreviewExpanded(SnippetItemViewModel item, bool expanded)
    {
        if (expanded)
        {
            expandedSnippetIds.Add(item.Id);
        }
        else
        {
            expandedSnippetIds.Remove(item.Id);
        }

        item.SetPreviewExpanded(expanded);
    }

    internal async Task DeleteSnippetAsync(string id, CancellationToken ct)
    {
        await RunBusyAsync(() => host.DeleteSnippetAsync(id, ct)).ConfigureAwait(false);
    }

    internal async Task CopyAssistantTextAsync(string text, CancellationToken ct)
    {
        await RunBusyAsync(() => host.CopyAssistantTextAsync(text, ct)).ConfigureAwait(false);
    }

    internal async Task AppendAssistantTextAsync(string text, CancellationToken ct)
    {
        await RunBusyAsync(() => host.AppendAssistantTextToEditorAsync(text, ct)).ConfigureAwait(false);
    }

    internal async Task ReplaceEditorWithAssistantTextAsync(string text, CancellationToken ct)
    {
        await RunBusyAsync(() => host.ReplaceEditorWithAssistantTextAsync(text, ct)).ConfigureAwait(false);
    }

    internal async Task ContinueAssistantResponseAsync(string itemId, string text, CancellationToken ct)
    {
        await RunBusyAsync(() => host.ContinueAssistantResponseAsync(itemId, text, ct)).ConfigureAwait(false);
    }

    internal async Task RemovePendingAttachmentAsync(string attachmentId, CancellationToken ct)
    {
        await host.RemovePendingAttachmentAsync(attachmentId, ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        host.StateChanged -= OnHostStateChanged;
    }

    private void OnHostStateChanged(object? sender, ContextRelayStateChangedEventArgs e)
    {
        if (e.StreamingOnly)
        {
            ApplyStreamingState(e.State);
            return;
        }

        ApplyState(e.State);
    }

    /// <summary>
    /// Applies a streaming progress snapshot. One arrives per response frame, so this notifies only the
    /// streaming properties instead of rescanning every collection and refreshing all localized labels.
    /// </summary>
    /// <param name="state">The host state carrying the current streaming snapshot.</param>
    private void ApplyStreamingState(ContextRelayHostState state)
    {
        isApplyingState = true;
        try
        {
            IsStreaming = state.IsStreaming;
            StreamingResponseText = state.StreamingResponseText;
        }
        finally
        {
            isApplyingState = false;
        }
    }

    /// <summary>
    /// Re-parses the whole streamed text (at most every <see cref="StreamingParseInterval"/>, except for
    /// the first text and a reset) and updates the preview blocks in place. An unclosed code fence parses
    /// as a code block that is still being written.
    /// </summary>
    private void UpdateStreamingBlocks()
    {
        var text = streamingResponseText;
        var isReset = string.IsNullOrEmpty(text) || streamingBlocks.Count == 0;
        if (!isReset && streamingParseClock.IsRunning && streamingParseClock.Elapsed < StreamingParseInterval)
        {
            return;
        }

        streamingParseClock.Restart();
        var blocks = SafeMarkdownParser.Parse(text).Blocks;
        var visible = blocks.Count > StreamingPreviewMaxBlocks
            ? blocks.Skip(blocks.Count - StreamingPreviewMaxBlocks).ToArray()
            : blocks;
        MarkdownBlockList.Update(streamingBlocks, visible);
        HasEarlierStreamingBlocks = blocks.Count > visible.Count;
    }

    private void ApplyState(ContextRelayHostState state)
    {
        var queryChanged = !string.Equals(queryText, state.QueryText, StringComparison.Ordinal);
        isApplyingState = true;
        try
        {
            QueryText = state.QueryText;
            HelpText = state.HelpText;
            StatusMessage = state.StatusMessage;
            RefreshLocalizedUiTexts();
            var collectionLanguageChanged = collectionsUseJapanese != ContextRelayLocalizedStrings.UseJapanese;
            collectionsUseJapanese = ContextRelayLocalizedStrings.UseJapanese;
            SignedInUserText = string.IsNullOrWhiteSpace(state.SignedInUser)
                ? ContextRelayLocalizedStrings.SignedOutText
                : ContextRelayLocalizedStrings.GetSignedInUserText(state.SignedInUser!);
            SearchSummary = state.SearchSummary;
            IsStreaming = state.IsStreaming;
            StreamingResponseText = state.StreamingResponseText;
            if (collectionLanguageChanged || !PendingAttachmentsEqual(state.PendingAttachments))
            {
                PendingAttachments = state.PendingAttachments.Select(item => new PendingAttachmentViewModel(item, this)).ToArray();
            }
            if (isStatusError != state.IsStatusError)
            {
                isStatusError = state.IsStatusError;
                RaiseNotifyPropertyChangedEvent(nameof(ShowHelpText));
            }

            pinnedKeys = new HashSet<string>(
                state.Snippets
                    .Select(snippet => EmbeddedSnippetMetadata.TryGetString(snippet, ContextItemKeyMetadataKey))
                    .Where(key => !string.IsNullOrEmpty(key))
                    .Select(key => key!),
                StringComparer.Ordinal);
            var resultsChanged = !SearchResultsEqual(state.SearchResults);
            if (collectionLanguageChanged || resultsChanged)
            {
                if (resultsChanged)
                {
                    // A new result set starts collapsed; a language-only rebuild keeps what was expanded.
                    expandedResultKeys.Clear();
                }

                SearchResults = state.SearchResults
                    .Select(item =>
                    {
                        var key = ContextItemKeys.Build(item);
                        return new ContextItemViewModel(item, this, pinnedKeys.Contains(key), expandedResultKeys.Contains(key));
                    })
                    .ToArray();
            }
            else
            {
                foreach (var result in SearchResults)
                {
                    result.SetPinned(pinnedKeys.Contains(result.Key));
                }
            }

            var previousPinCount = Snippets.Count;
            if (collectionLanguageChanged || !SnippetsEqual(state.Snippets))
            {
                expandedSnippetIds.IntersectWith(state.Snippets.Select(item => item.Id));
                Snippets = state.Snippets.Select(item => new SnippetItemViewModel(item, this, expandedSnippetIds.Contains(item.Id))).ToArray();
            }
            var previousChatCount = ChatHistory.Count;
            if (collectionLanguageChanged || !ChatHistoryEqual(state.ChatHistory, state.ContinuableCopilotAssistantItemId))
            {
                ChatHistory = state.ChatHistory
                    .Select(item => new ChatHistoryItemViewModel(item, this, string.Equals(item.Id, state.ContinuableCopilotAssistantItemId, StringComparison.Ordinal)))
                    .ToArray();
            }

            UpdateUnseenCounts(resultsChanged, previousChatCount, previousPinCount, collectionLanguageChanged);
            workspaceFiles = state.WorkspaceFiles;
            isEmbeddedActive = state.IsEmbeddedActive;
            embeddedSources = state.EmbeddedSources;
            selectedEmbeddedContainerTypeId = state.SelectedEmbeddedContainerTypeId;
            lastSelectedEmbeddedContainerTypeId = state.LastSelectedEmbeddedContainerTypeId;
            embeddedInvalidSourceCount = state.EmbeddedInvalidSourceCount;
            RefreshEmbeddedSourceBar(allowOpen: false);
            if (queryChanged)
            {
                CommandSuggestions = Array.Empty<SlashCommandSuggestion>();
                VisibleCommandSuggestions = Array.Empty<SlashCommandSuggestion>();
                SelectedCommandSuggestion = null;
                commandSuggestionWindowStart = 0;
                IsCommandPopupOpen = false;
            }
            else if (collectionLanguageChanged && IsCommandPopupOpen)
            {
                // The query is unchanged, so the popup would otherwise keep showing suggestion
                // descriptions and help text built in the previous UI language.
                RebuildCommandSuggestionsForLanguageChange();
            }
        }
        finally
        {
            isApplyingState = false;
        }

        if (!queryChanged && IsCommandPopupOpen)
        {
            UpdateTransientHelpText();
        }
    }

    private async Task SubmitAsync(IClientContext clientContext, CancellationToken ct)
    {
        var query = QueryText;
        host.LogUiDiagnostic($"SubmitAsync invoked queryLength={query.Length} startsWithSlash={query.StartsWith("/", StringComparison.Ordinal)}");

        // Both the primary button and Enter in the query box submit through here, so the enabled
        // state has to be maintained here rather than in either caller. Checking busy first keeps
        // a second submission from clearing the flag of the request that is already running.
        if (isBusy)
        {
            host.LogUiDiagnostic("Submit ignored because the view model is already busy.");
            return;
        }

        CloseCommandPopup();
        IsEmbeddedSourceSelectorOpen = false;
        var targetTab = GetTabForQuery(query);
        if (targetTab is int tab)
        {
            SelectedTabIndex = tab;
        }

        // Authentication and conversation setup run before the first streaming update. Keep the
        // button enabled across that gap for routes that can stream and be stopped; disabling it
        // would move keyboard focus away, and WPF does not restore it when the control is enabled
        // again. Other routes keep the ordinary disabled-while-busy state.
        var stoppable = IsStoppableRoute(query);
        if (stoppable)
        {
            isChatRequestActive = true;
            RaiseNotifyPropertyChangedEvent(nameof(IsPrimaryActionEnabled));
        }

        try
        {
            await RunBusyAsync(async () => { await host.SubmitQueryAsync(query, clientContext, ct).ConfigureAwait(false); }).ConfigureAwait(false);
        }
        finally
        {
            if (stoppable)
            {
                isChatRequestActive = false;
                RaiseNotifyPropertyChangedEvent(nameof(IsPrimaryActionEnabled));
            }
        }
    }

    private bool PendingAttachmentsEqual(IReadOnlyList<ResolvedAttachment> items) =>
        PendingAttachments.Count == items.Count && PendingAttachments.Zip(items, (current, next) =>
            string.Equals(current.Id, next.Id, StringComparison.Ordinal) &&
            string.Equals(current.Label, next.Label, StringComparison.Ordinal)).All(value => value);

    private bool SearchResultsEqual(IReadOnlyList<ContextItem> items) =>
        SearchResults.Count == items.Count && SearchResults.Zip(items, (current, next) =>
            string.Equals(current.Title, next.Title, StringComparison.Ordinal) &&
            string.Equals(current.Snippet, next.Snippet, StringComparison.Ordinal) &&
            string.Equals(current.Source, ContextSourceSchema.ToSchemaValue(next.Source), StringComparison.Ordinal) &&
            ReferenceEquals(current.Item, next) &&
            string.Equals(current.Timestamp, next.Timestamp ?? string.Empty, StringComparison.Ordinal)).All(value => value);

    private bool SnippetsEqual(IReadOnlyList<SharedSnippetItem> items) =>
        Snippets.Count == items.Count && Snippets.Zip(items, (current, next) =>
            string.Equals(current.Id, next.Id, StringComparison.Ordinal) &&
            string.Equals(current.Name, next.Name, StringComparison.Ordinal) &&
            string.Equals(current.Snippet, next.Snippet, StringComparison.Ordinal) &&
            string.Equals(current.Source, next.Source, StringComparison.Ordinal) &&
            string.Equals(current.SourceUrl, SafeLinks.TryGetOpenableUrl(next.SourceUrl, out var url) ? url : string.Empty, StringComparison.Ordinal)).All(value => value);

    private bool ChatHistoryEqual(IReadOnlyList<SharedChatHistoryItem> items, string? continuableId) =>
        ChatHistory.Count == items.Count && ChatHistory.Zip(items, (current, next) =>
            string.Equals(current.Id, next.Id, StringComparison.Ordinal) &&
            string.Equals(current.Role, next.Role, StringComparison.Ordinal) &&
            string.Equals(current.Text, next.Text, StringComparison.Ordinal) &&
            string.Equals(current.Timestamp, next.Timestamp, StringComparison.Ordinal) &&
            current.IsActionableAssistant == next.IsActionableAssistant &&
            current.IsCopilotAssistant == next.IsCopilotAssistant &&
            current.IsLatestCopilotAssistant == (next.IsCopilotAssistant && string.Equals(next.Id, continuableId, StringComparison.Ordinal)) &&
            current.HasContextLabels == next.HasContextLabels &&
            string.Equals(current.ContextLabelsJoinedDisplay, next.ContextLabelsJoinedDisplay, StringComparison.Ordinal)).All(value => value);

    private async Task ConfirmQueryInputAsync(IClientContext clientContext, CancellationToken ct)
    {
        host.LogUiDiagnostic($"ConfirmQueryInput invoked popupOpen={IsCommandPopupOpen} suggestionCount={commandSuggestions.Count}");
        if (ApplySelectedCommandSuggestion())
        {
            return;
        }

        if (isEmbeddedSourceSelectorOpen)
        {
            await ConfirmEmbeddedSourceAsync(embeddedHighlightIndex, ct).ConfigureAwait(false);
            return;
        }

        await SubmitAsync(clientContext, ct).ConfigureAwait(false);
    }

    private void MoveSelection(int delta)
    {
        if (IsCommandPopupOpen)
        {
            MoveCommandSelection(delta);
        }
        else if (isEmbeddedSourceSelectorOpen && embeddedSources.Count > 0)
        {
            embeddedHighlightIndex = (embeddedHighlightIndex + delta + embeddedSources.Count) % embeddedSources.Count;
            RebuildEmbeddedSourceOptions();
        }
    }

    /// <summary>
    /// Shows or hides the Embedded source chip for the current draft. With several sources, the selector
    /// opens once each time the draft enters the /embedded route (and the command popup is not showing).
    /// </summary>
    private void RefreshEmbeddedSourceBar(bool allowOpen)
    {
        var showBar = ShowEmbeddedSourceBar;
        if (!showBar)
        {
            embeddedSelectorShownForEntry = false;
            IsEmbeddedSourceSelectorOpen = false;
        }
        else if (allowOpen && !embeddedSelectorShownForEntry && !IsCommandPopupOpen && embeddedSources.Count > 1)
        {
            OpenEmbeddedSourceSelector();
        }
        else if (isEmbeddedSourceSelectorOpen)
        {
            RebuildEmbeddedSourceOptions();
        }

        RaiseNotifyPropertyChangedEvent(nameof(ShowEmbeddedSourceBar));
        RaiseNotifyPropertyChangedEvent(nameof(EmbeddedSourceChipText));
        RaiseNotifyPropertyChangedEvent(nameof(CanChangeEmbeddedSource));
        RaiseNotifyPropertyChangedEvent(nameof(HasEmbeddedInvalidSources));
        RaiseNotifyPropertyChangedEvent(nameof(EmbeddedInvalidSourcesText));
    }

    private void OpenEmbeddedSourceSelector()
    {
        if (embeddedSources.Count < 2)
        {
            return;
        }

        embeddedSelectorShownForEntry = true;
        var initial = selectedEmbeddedContainerTypeId ?? lastSelectedEmbeddedContainerTypeId;
        embeddedHighlightIndex = Math.Max(0, embeddedSources
            .Select((source, index) => (source, index))
            .Where(pair => string.Equals(pair.source.ContainerTypeId, initial, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.index)
            .DefaultIfEmpty(0)
            .First());
        RebuildEmbeddedSourceOptions();
        IsEmbeddedSourceSelectorOpen = true;
        host.LogUiDiagnostic($"Embedded source selector opened sourceCount={embeddedSources.Count}");
    }

    private void RebuildEmbeddedSourceOptions()
    {
        if (embeddedHighlightIndex >= embeddedSources.Count)
        {
            embeddedHighlightIndex = 0;
        }

        EmbeddedSourceOptions = embeddedSources
            .Select((source, index) => new EmbeddedSourceOptionViewModel(
                source.Name,
                source.ContainerTypeId,
                index == embeddedHighlightIndex,
                new AsyncCommand(async (_, ct) => await ConfirmEmbeddedSourceAsync(index, ct).ConfigureAwait(false))))
            .ToArray();
    }

    private async Task ConfirmEmbeddedSourceAsync(int index, CancellationToken ct)
    {
        if (index < 0 || index >= embeddedSources.Count)
        {
            return;
        }

        IsEmbeddedSourceSelectorOpen = false;
        await host.SelectEmbeddedSourceAsync(embeddedSources[index].ContainerTypeId, ct).ConfigureAwait(false);
    }

    /// <summary>Esc in the selector: clears the choice, so a submit asks to choose instead of sending.</summary>
    private async Task ClearEmbeddedSourceAsync(CancellationToken ct)
    {
        IsEmbeddedSourceSelectorOpen = false;
        await host.SelectEmbeddedSourceAsync(null, ct).ConfigureAwait(false);
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (isBusy)
        {
            host.LogUiDiagnostic("RunBusyAsync ignored because the view model is already busy.");
            return;
        }

        isBusy = true;
        RaiseNotifyPropertyChangedEvent(nameof(IsNotBusy));
        RaiseNotifyPropertyChangedEvent(nameof(IsPrimaryActionEnabled));
        try
        {
            await action().ConfigureAwait(false);
        }
        finally
        {
            isBusy = false;
            RaiseNotifyPropertyChangedEvent(nameof(IsNotBusy));
            RaiseNotifyPropertyChangedEvent(nameof(IsPrimaryActionEnabled));
        }
    }

    private void MoveCommandSelection(int delta)
    {
        if (commandSuggestions.Count == 0)
        {
            host.LogUiDiagnostic($"MoveCommandSelection ignored delta={delta} because no suggestions are available.");
            return;
        }

        var currentIndex = selectedCommandSuggestion is null ? -1 : IndexOf(commandSuggestions, selectedCommandSuggestion);
        var nextIndex = currentIndex + delta;
        if (nextIndex < 0)
        {
            nextIndex = commandSuggestions.Count - 1;
        }
        else if (nextIndex >= commandSuggestions.Count)
        {
            nextIndex = 0;
        }

        SelectCommandSuggestion(nextIndex);
        host.LogUiDiagnostic($"MoveCommandSelection applied delta={delta} selectedIndex={nextIndex}");
    }

    private bool ApplySelectedCommandSuggestion()
    {
        return ApplyCommandSuggestion(selectedCommandSuggestion);
    }

    private bool ApplyCommandSuggestion(SlashCommandSuggestion? suggestion)
    {
        if (!SlashCommandSuggestion.TryBuildCommittedQuery(IsCommandPopupOpen, suggestion, out var committedQuery))
        {
            host.LogUiDiagnostic("ApplyCommandSuggestion skipped because popup was closed or no suggestion was selected.");
            return false;
        }

        SelectedCommandSuggestion = suggestion;
        QueryText = committedQuery;
        host.LogUiDiagnostic($"ApplyCommandSuggestion committed command={suggestion?.Name ?? "(unknown)"}");
        CloseCommandPopup();
        return true;
    }

    private void UpdateCommandSuggestions()
    {
        var suggestions = BuildComposerSuggestions(QueryText, workspaceFiles, isEmbeddedActive);
        CommandSuggestions = suggestions
            .Select(CreateInteractiveSuggestion)
            .ToArray();
        commandSuggestionWindowStart = 0;
        SelectedCommandSuggestion = commandSuggestions.Count > 0 ? commandSuggestions[0] : null;
        UpdateVisibleCommandSuggestions();
        IsCommandPopupOpen = commandSuggestions.Count > 0;
        CommandPopupHeaderText = commandSuggestions.Count > 0 && IsFileMentionSuggestion(commandSuggestions[0])
            ? ContextRelayLocalizedStrings.FileMentionPopupHeaderText
            : ContextRelayLocalizedStrings.CommandPopupHeaderText;
        RaiseNotifyPropertyChangedEvent(nameof(CommandPopupHeaderText));
        host.LogUiDiagnostic($"UpdateCommandSuggestions updated count={commandSuggestions.Count} popupOpen={IsCommandPopupOpen}");
    }

    /// <summary>
    /// Rebuilds an already-open popup's suggestions after a UI language change, so their descriptions and
    /// header text follow the new language instead of staying in the language they were typed in.
    /// </summary>
    private void RebuildCommandSuggestionsForLanguageChange()
    {
        // A suggestion's Name (the literal command or file token) is language-invariant, unlike its
        // localized Description, so it identifies the same row across a rebuild.
        var selectedName = selectedCommandSuggestion?.Name;

        var suggestions = BuildComposerSuggestions(QueryText, workspaceFiles, isEmbeddedActive);
        CommandSuggestions = suggestions
            .Select(CreateInteractiveSuggestion)
            .ToArray();

        if (commandSuggestions.Count == 0)
        {
            VisibleCommandSuggestions = Array.Empty<SlashCommandSuggestion>();
            SelectedCommandSuggestion = null;
            commandSuggestionWindowStart = 0;
            IsCommandPopupOpen = false;
            return;
        }

        var reselected = selectedName is null
            ? null
            : commandSuggestions.FirstOrDefault(suggestion => string.Equals(suggestion.Name, selectedName, StringComparison.Ordinal));
        SelectedCommandSuggestion = reselected ?? commandSuggestions[0];
        commandSuggestionWindowStart = Math.Min(
            commandSuggestionWindowStart,
            Math.Max(0, commandSuggestions.Count - MaxVisibleCommandSuggestions));
        UpdateVisibleCommandSuggestions();
        CommandPopupHeaderText = IsFileMentionSuggestion(commandSuggestions[0])
            ? ContextRelayLocalizedStrings.FileMentionPopupHeaderText
            : ContextRelayLocalizedStrings.CommandPopupHeaderText;
        RaiseNotifyPropertyChangedEvent(nameof(CommandPopupHeaderText));
        host.LogUiDiagnostic($"RebuildCommandSuggestionsForLanguageChange updated count={commandSuggestions.Count}");
    }

    private void UpdateTransientHelpText()
    {
        HelpText = IsCommandPopupOpen && selectedCommandSuggestion is not null
            ? selectedCommandSuggestion.Description
            : ContextRelayLocalizedStrings.GetHelpTextForQuery(QueryText);
    }

    private void RefreshLocalizedUiTexts()
    {
        GenerateHandoffButtonText = ContextRelayLocalizedStrings.GenerateHandoffButtonText;
        RaiseNotifyPropertyChangedEvent(nameof(GenerateHandoffButtonText));
        CopyPromptButtonText = ContextRelayLocalizedStrings.CopyPromptButtonText;
        RaiseNotifyPropertyChangedEvent(nameof(CopyPromptButtonText));
        OpenHandoffButtonText = ContextRelayLocalizedStrings.OpenHandoffButtonText;
        RaiseNotifyPropertyChangedEvent(nameof(OpenHandoffButtonText));
        OpenCopilotButtonText = ContextRelayLocalizedStrings.OpenCopilotButtonText;
        RaiseNotifyPropertyChangedEvent(nameof(OpenCopilotButtonText));
        ClearChatButtonText = ContextRelayLocalizedStrings.ClearChatButtonText;
        RaiseNotifyPropertyChangedEvent(nameof(ClearChatButtonText));
        ClearSnippetsButtonText = ContextRelayLocalizedStrings.ClearSnippetsButtonText;
        RaiseNotifyPropertyChangedEvent(nameof(ClearSnippetsButtonText));
        ClearCacheButtonText = ContextRelayLocalizedStrings.ClearCacheButtonText;
        RaiseNotifyPropertyChangedEvent(nameof(ClearCacheButtonText));
        SearchButtonText = ContextRelayLocalizedStrings.SearchButtonText;
        RaiseNotifyPropertyChangedEvent(nameof(SearchButtonText));
        SearchResultsHeaderText = ContextRelayLocalizedStrings.SearchResultsHeaderText;
        RaiseNotifyPropertyChangedEvent(nameof(SearchResultsHeaderText));
        SearchSummaryHeaderText = ContextRelayLocalizedStrings.SearchSummaryHeaderText;
        RaiseNotifyPropertyChangedEvent(nameof(SearchSummaryHeaderText));
        SnippetsHeaderText = ContextRelayLocalizedStrings.SnippetsHeaderText;
        RaiseNotifyPropertyChangedEvent(nameof(SnippetsHeaderText));
        ChatHistoryHeaderText = ContextRelayLocalizedStrings.ChatHistoryHeaderText;
        RaiseNotifyPropertyChangedEvent(nameof(ChatHistoryHeaderText));
        SearchToolTipText = ContextRelayLocalizedStrings.SearchToolTip;
        RaiseNotifyPropertyChangedEvent(nameof(SearchToolTipText));
        CommandPopupHeaderText = ContextRelayLocalizedStrings.CommandPopupHeaderText;
        RaiseNotifyPropertyChangedEvent(nameof(CommandPopupHeaderText));
        AddFilesButtonText = ContextRelayLocalizedStrings.AddFilesButtonText;
        RaiseNotifyPropertyChangedEvent(nameof(AddFilesButtonText));
        AddFilesToolTipText = ContextRelayLocalizedStrings.AddFilesToolTip;
        RaiseNotifyPropertyChangedEvent(nameof(AddFilesToolTipText));
        StopGenerationButtonText = ContextRelayLocalizedStrings.StopGenerationButtonText;
        RaiseNotifyPropertyChangedEvent(nameof(StopGenerationButtonText));
        WindowTitleText = ContextRelayLocalizedStrings.WindowTitleText;
        RaiseTabHeaders();
        RaiseNotifyPropertyChangedEvent(nameof(ChangeEmbeddedSourceButtonText));
        RaiseNotifyPropertyChangedEvent(nameof(EmbeddedSourceSelectorHeaderText));
        RaiseNotifyPropertyChangedEvent(nameof(EmbeddedPreviewNoticeText));

        // Notify the derived caption last. It reads whichever label matches the current state,
        // so notifying before both labels are assigned would publish the previous language.
        RaiseNotifyPropertyChangedEvent(nameof(PrimaryActionButtonText));
    }

    /// <summary>
    /// Returns whether a query routes to a Copilot chat turn, which is the only kind of request
    /// that streams and can be stopped from the composer.
    /// </summary>
    /// <param name="query">The query text about to be submitted.</param>
    /// <returns><see langword="true"/> for non-empty plain chat, <c>/ask</c>, and <c>/embedded</c> submissions.</returns>
    private static bool IsStoppableRoute(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        var target = SlashCommandRouter.Parse(query.Trim()).Target;
        return target is RouteTarget.Chat or RouteTarget.Ask or RouteTarget.SharePointEmbedded;
    }

    /// <summary>
    /// Returns the tab a submission's output lands in: chat turns (plain text, /ask, /workiq) show in
    /// the chat tab and searches (including /embedded) in the results tab.
    /// </summary>
    /// <param name="query">The query text about to be submitted.</param>
    /// <returns>The tab index, or <see langword="null"/> to keep the current tab.</returns>
    internal static int? GetTabForQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        return SlashCommandRouter.Parse(query.Trim()).Target switch
        {
            RouteTarget.Clear => null,
            RouteTarget.Chat or RouteTarget.Ask or RouteTarget.WorkIq => ChatTabIndex,
            _ => ResultsTabIndex
        };
    }

    /// <summary>
    /// Counts what arrived in a tab the user is not looking at, so its header can show "(n)".
    /// </summary>
    private void UpdateUnseenCounts(bool resultsChanged, int previousChatCount, int previousPinCount, bool languageChanged)
    {
        if (resultsChanged && !languageChanged && selectedTabIndex != ResultsTabIndex)
        {
            // A new search replaces the list, so every result in it is unseen.
            unseenResultCount = searchResults.Count;
        }

        if (selectedTabIndex != ChatTabIndex && chatHistory.Count > previousChatCount)
        {
            unseenChatCount += chatHistory.Count - previousChatCount;
        }

        if (selectedTabIndex != PinsTabIndex && snippets.Count > previousPinCount)
        {
            unseenPinCount += snippets.Count - previousPinCount;
        }

        unseenResultCount = Math.Min(unseenResultCount, searchResults.Count);
        unseenChatCount = Math.Min(unseenChatCount, chatHistory.Count);
        unseenPinCount = Math.Min(unseenPinCount, snippets.Count);
        RaiseTabHeaders();
    }

    private void ClearUnseenCount(int tabIndex)
    {
        switch (tabIndex)
        {
            case ChatTabIndex:
                unseenChatCount = 0;
                break;
            case ResultsTabIndex:
                unseenResultCount = 0;
                break;
            case PinsTabIndex:
                unseenPinCount = 0;
                break;
        }

        RaiseTabHeaders();
    }

    private void RaiseTabHeaders()
    {
        RaiseNotifyPropertyChangedEvent(nameof(ChatTabHeaderText));
        RaiseNotifyPropertyChangedEvent(nameof(ResultsTabHeaderText));
        RaiseNotifyPropertyChangedEvent(nameof(PinsTabHeaderText));
    }

    /// <summary>
    /// Enables the suggestion key bindings only while the slash-command popup is open, so the
    /// query box does not consume Tab, Up, Down, or Escape when there is nothing to navigate.
    /// </summary>
    private void UpdateSuggestionKeyBindingAvailability()
    {
        var popupOpen = isCommandPopupOpen || isEmbeddedSourceSelectorOpen;
        ApplyCommandSelectionCommand.CanExecute = popupOpen;
        MoveSelectionDownCommand.CanExecute = popupOpen;
        MoveSelectionUpCommand.CanExecute = popupOpen;
        CloseCommandPopupCommand.CanExecute = popupOpen;
    }

    private void CloseCommandPopup()
    {
        var hadSuggestions = commandSuggestions.Count > 0;
        CommandSuggestions = Array.Empty<SlashCommandSuggestion>();
        VisibleCommandSuggestions = Array.Empty<SlashCommandSuggestion>();
        SelectedCommandSuggestion = null;
        commandSuggestionWindowStart = 0;
        IsCommandPopupOpen = false;
        if (hadSuggestions)
        {
            host.LogUiDiagnostic("CloseCommandPopup cleared command suggestions.");
        }
    }

    private void SelectCommandSuggestion(int index)
    {
        if (index < 0 || index >= commandSuggestions.Count)
        {
            return;
        }

        commandSuggestionWindowStart = CalculateVisibleWindowStart(
            totalCount: commandSuggestions.Count,
            selectedIndex: index,
            currentWindowStart: commandSuggestionWindowStart,
            maxVisibleCount: MaxVisibleCommandSuggestions);

        SelectedCommandSuggestion = commandSuggestions[index];

        // Always rebuild VisibleCommandSuggestions from fresh display clones, even when the scroll
        // window didn't move: Remote UI de-duplicates already-transmitted item objects by identity,
        // so reassigning the collection with the same instances would not repaint the highlight.
        UpdateVisibleCommandSuggestions();
    }

    private void UpdateVisibleCommandSuggestions()
    {
        if (commandSuggestions.Count == 0)
        {
            VisibleCommandSuggestions = Array.Empty<SlashCommandSuggestion>();
            commandSuggestionWindowStart = 0;
            return;
        }

        var maxStartIndex = Math.Max(0, commandSuggestions.Count - MaxVisibleCommandSuggestions);
        commandSuggestionWindowStart = Math.Clamp(commandSuggestionWindowStart, 0, maxStartIndex);
        var window = BuildVisibleWindow(
            commandSuggestions,
            commandSuggestionWindowStart,
            MaxVisibleCommandSuggestions,
            selectedCommandSuggestion);
        for (var i = 0; i < window.Length; i++)
        {
            // Target the master suggestion so selection bookkeeping (IndexOf over
            // commandSuggestions) keeps working on the canonical instances.
            var master = commandSuggestions[commandSuggestionWindowStart + i];
            window[i].ApplyCommand = new AsyncCommand((_, _) =>
            {
                ApplyCommandSuggestion(master);
                return Task.CompletedTask;
            });
        }

        VisibleCommandSuggestions = window;
    }

    /// <summary>
    /// Builds the visible popup window as brand-new <see cref="SlashCommandSuggestion"/> display
    /// clones with <see cref="SlashCommandSuggestion.IsSelected"/> baked in at construction.
    /// Fresh instances are required on every selection change because Remote UI tracks
    /// already-transmitted objects by identity and does not re-serialize in-place property
    /// mutations on them; the master items in <paramref name="suggestions"/> are never mutated.
    /// </summary>
    internal static SlashCommandSuggestion[] BuildVisibleWindow(
        IReadOnlyList<SlashCommandSuggestion> suggestions,
        int windowStart,
        int maxVisibleCount,
        SlashCommandSuggestion? selected)
    {
        return suggestions
            .Skip(windowStart)
            .Take(maxVisibleCount)
            .Select(master => SlashCommandSuggestion.CreateDisplayClone(master, ReferenceEquals(master, selected)))
            .ToArray();
    }

    private SlashCommandSuggestion CreateInteractiveSuggestion(SlashCommandSuggestion suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);

        var interactiveSuggestion = new SlashCommandSuggestion
        {
            Icon = suggestion.Icon,
            Name = suggestion.Name,
            Description = suggestion.Description,
            CommittedQuery = suggestion.CommittedQuery
        };

        interactiveSuggestion.ApplyCommand = new AsyncCommand((_, _) =>
        {
            ApplyCommandSuggestion(interactiveSuggestion);
            return Task.CompletedTask;
        });

        return interactiveSuggestion;
    }

    internal static int CalculateVisibleWindowStart(int totalCount, int selectedIndex, int currentWindowStart, int maxVisibleCount)
    {
        if (totalCount <= 0 || maxVisibleCount <= 0)
        {
            return 0;
        }

        if (selectedIndex < 0)
        {
            selectedIndex = 0;
        }
        else if (selectedIndex >= totalCount)
        {
            selectedIndex = totalCount - 1;
        }

        var nextWindowStart = currentWindowStart;
        if (selectedIndex < nextWindowStart)
        {
            nextWindowStart = selectedIndex;
        }
        else if (selectedIndex >= nextWindowStart + maxVisibleCount)
        {
            nextWindowStart = selectedIndex - maxVisibleCount + 1;
        }

        var maxWindowStart = Math.Max(0, totalCount - maxVisibleCount);
        return Math.Clamp(nextWindowStart, 0, maxWindowStart);
    }

    private static int IndexOf<T>(IReadOnlyList<T> list, T item)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], item))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool ShouldLogComposerTransition(string previousQuery, string currentQuery)
    {
        return currentQuery.Length == 0 ||
            currentQuery.StartsWith("/", StringComparison.Ordinal) ||
            previousQuery.StartsWith("/", StringComparison.Ordinal) ||
            currentQuery.IndexOf('#') >= 0 ||
            previousQuery.IndexOf('#') >= 0;
    }

    private static IReadOnlyList<SlashCommandSuggestion> BuildComposerSuggestions(string queryText, IReadOnlyList<string> workspaceFiles, bool includeEmbedded)
    {
        var fileSuggestions = BuildFileMentionSuggestions(queryText, workspaceFiles);
        return fileSuggestions.Count > 0
            ? fileSuggestions
            : ContextRelayLocalizedStrings.GetCommandSuggestions(queryText, includeEmbedded);
    }

    private static IReadOnlyList<SlashCommandSuggestion> BuildFileMentionSuggestions(string queryText, IReadOnlyList<string> workspaceFiles)
    {
        if (string.IsNullOrWhiteSpace(queryText) ||
            char.IsWhiteSpace(queryText[^1]))
        {
            return Array.Empty<SlashCommandSuggestion>();
        }

        var match = Regex.Match(queryText, "(^|\\s)(#(?:\"[^\"]*\"|'[^']*'|[^\\s#]*))$", RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return Array.Empty<SlashCommandSuggestion>();
        }

        var token = match.Groups[2].Value;
        if (!token.StartsWith("#", StringComparison.Ordinal))
        {
            return Array.Empty<SlashCommandSuggestion>();
        }

        var tokenStart = match.Index + match.Groups[1].Length;
        var tokenEnd = match.Index + match.Length;
        var partialPath = token.Substring(1).Trim('"', '\'');
        var filteredFiles = workspaceFiles
            .Where(path => path.Contains(partialPath, StringComparison.OrdinalIgnoreCase))
            .Take(50)
            .ToArray();
        if (filteredFiles.Length == 0)
        {
            return Array.Empty<SlashCommandSuggestion>();
        }

        var suggestions = new List<SlashCommandSuggestion>(filteredFiles.Length);
        foreach (var file in filteredFiles)
        {
            var fileToken = file.IndexOf(' ') >= 0 ? $"#\"{file}\"" : $"#{file}";
            var prefix = queryText[..tokenStart];
            var suffix = queryText[tokenEnd..];
            var separator = suffix.StartsWith(" ", StringComparison.Ordinal) || suffix.Length == 0 ? string.Empty : " ";
            suggestions.Add(new SlashCommandSuggestion
            {
                Icon = "#",
                Name = file,
                Description = ContextRelayLocalizedStrings.GetLocalFileContextLabel(file),
                CommittedQuery = $"{prefix}{fileToken}{separator}{suffix}"
            });
        }

        return suggestions;
    }

    private static bool IsFileMentionSuggestion(SlashCommandSuggestion suggestion)
    {
        return string.Equals(suggestion.Icon, "#", StringComparison.Ordinal);
    }
}
