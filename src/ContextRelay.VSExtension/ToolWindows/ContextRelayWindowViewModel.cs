using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.Models;
using ContextRelay.Core.Router;
using ContextRelay.VSExtension.Services;
using Microsoft.VisualStudio.Extensibility.UI;

namespace ContextRelay.VSExtension.ToolWindows;

[DataContract]
internal sealed class ContextRelayWindowViewModel : NotifyPropertyChangedObject, IDisposable
{
    private const int MaxVisibleCommandSuggestions = 4;
    private readonly ContextRelayHost host;
    private bool isBusy;
    private bool isCommandPopupOpen;
    private int commandSuggestionWindowStart;
    private string queryText = string.Empty;
    private string helpText = ContextRelayLocalizedStrings.GenericHelpText;
    private string statusMessage = ContextRelayLocalizedStrings.ReadyStatus;
    private string signedInUserText = ContextRelayLocalizedStrings.SignedOutText;
    private IReadOnlyList<ContextItemViewModel> searchResults = Array.Empty<ContextItemViewModel>();
    private IReadOnlyList<SnippetItemViewModel> snippets = Array.Empty<SnippetItemViewModel>();
    private IReadOnlyList<ChatHistoryItemViewModel> chatHistory = Array.Empty<ChatHistoryItemViewModel>();
    private IReadOnlyList<SlashCommandSuggestion> commandSuggestions = Array.Empty<SlashCommandSuggestion>();
    private IReadOnlyList<SlashCommandSuggestion> visibleCommandSuggestions = Array.Empty<SlashCommandSuggestion>();
    private SlashCommandSuggestion? selectedCommandSuggestion;
    private bool isApplyingState;
    private string windowTitleText = ContextRelayLocalizedStrings.WindowTitleText;

    public ContextRelayWindowViewModel(ContextRelayHost host)
    {
        this.host = host;
        host.StateChanged += OnHostStateChanged;

        RefreshLocalizedUiTexts();

        SearchCommand = new AsyncCommand(async (_, ct) => await SubmitAsync(ct).ConfigureAwait(false));
        GenerateHandoffCommand = new AsyncCommand(async (_, ct) => await RunBusyAsync(() => host.GenerateHandoffAsync(ct)).ConfigureAwait(false));
        CopyPromptCommand = new AsyncCommand(async (_, ct) => await RunBusyAsync(() => host.CopyHandoffPromptAsync(ct)).ConfigureAwait(false));
        OpenHandoffCommand = new AsyncCommand(async (_, ct) => await RunBusyAsync(() => host.OpenHandoffDocumentAsync(ct)).ConfigureAwait(false));
        OpenCopilotCommand = new AsyncCommand(async (_, ct) => await RunBusyAsync(() => host.OpenCopilotChatWithPromptAsync(ct)).ConfigureAwait(false));
        ClearChatCommand = new AsyncCommand(async (_, ct) => await RunBusyAsync(() => host.ClearChatAsync(ct)).ConfigureAwait(false));
        ClearSnippetsCommand = new AsyncCommand(async (_, ct) => await RunBusyAsync(() => host.ClearSnippetsAsync(ct)).ConfigureAwait(false));
        ClearCacheCommand = new AsyncCommand(async (_, ct) => await RunBusyAsync(() => host.ClearCacheAsync(ct)).ConfigureAwait(false));
        ShowDebugLogCommand = new AsyncCommand((_, _) => { host.ShowDebugLog(); return Task.CompletedTask; });
        MoveSelectionDownCommand = new AsyncCommand((_, _) => { MoveCommandSelection(1); return Task.CompletedTask; });
        MoveSelectionUpCommand = new AsyncCommand((_, _) => { MoveCommandSelection(-1); return Task.CompletedTask; });
        ApplyCommandSelectionCommand = new AsyncCommand((_, _) => { ApplySelectedCommandSuggestion(); return Task.CompletedTask; });
        ConfirmQueryInputCommand = new AsyncCommand(async (_, ct) => await ConfirmQueryInputAsync(ct).ConfigureAwait(false));
        CloseCommandPopupCommand = new AsyncCommand((_, _) => { CloseCommandPopup(); return Task.CompletedTask; });
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
    [DataMember] public string DebugLogButtonText { get; private set; } = string.Empty;
    [DataMember] public string SearchButtonText { get; private set; } = string.Empty;
    [DataMember] public string SearchResultsHeaderText { get; private set; } = string.Empty;
    [DataMember] public string SnippetsHeaderText { get; private set; } = string.Empty;
    [DataMember] public string ChatHistoryHeaderText { get; private set; } = string.Empty;
    [DataMember] public string SearchToolTipText { get; private set; } = string.Empty;
    [DataMember] public string CommandPopupHeaderText { get; private set; } = string.Empty;

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
    [DataMember] public AsyncCommand ShowDebugLogCommand { get; }
    [DataMember] public AsyncCommand MoveSelectionDownCommand { get; }
    [DataMember] public AsyncCommand MoveSelectionUpCommand { get; }
    [DataMember] public AsyncCommand ApplyCommandSelectionCommand { get; }
    [DataMember] public AsyncCommand ConfirmQueryInputCommand { get; }
    [DataMember] public AsyncCommand CloseCommandPopupCommand { get; }

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

            queryText = value;
            RaiseNotifyPropertyChangedEvent(nameof(QueryText));
            if (!isApplyingState)
            {
                UpdateCommandSuggestions();
                UpdateTransientHelpText();
            }
        }
    }

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
    public bool IsCommandPopupOpen
    {
        get => isCommandPopupOpen;
        private set
        {
            if (isCommandPopupOpen != value)
            {
                isCommandPopupOpen = value;
                RaiseNotifyPropertyChangedEvent(nameof(IsCommandPopupOpen));
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
            RaiseNotifyPropertyChangedEvent(nameof(SelectedCommandSuggestion));
            if (!isApplyingState && IsCommandPopupOpen)
            {
                UpdateTransientHelpText();
            }
        }
    }

    internal async Task PinResultAsync(ContextItemViewModel item, CancellationToken ct)
    {
        var contextItem = ReconstructContextItem(item);
        await RunBusyAsync(() => host.PinSnippetAsync(contextItem, ct)).ConfigureAwait(false);
    }

    internal async Task CopyResultAsync(ContextItemViewModel item, CancellationToken ct)
    {
        var contextItem = ReconstructContextItem(item);
        await RunBusyAsync(() => host.CopyResultToClipboardAsync(contextItem, ct)).ConfigureAwait(false);
    }

    internal async Task AppendResultAsync(ContextItemViewModel item, CancellationToken ct)
    {
        var contextItem = ReconstructContextItem(item);
        await RunBusyAsync(() => host.AppendResultToHandoffAsync(contextItem, ct)).ConfigureAwait(false);
    }

    internal void OpenUrl(string? url)
    {
        host.OpenExternalUrl(url);
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

    public void Dispose()
    {
        host.StateChanged -= OnHostStateChanged;
    }

    private static ContextItem ReconstructContextItem(ContextItemViewModel item)
    {
        Enum.TryParse<ContextSource>(item.Source, out var source);
        return new ContextItem
        {
            Title = item.Title,
            Snippet = item.Snippet,
            Source = source,
            Url = item.Url,
            Timestamp = item.Timestamp
        };
    }

    private void OnHostStateChanged(object? sender, ContextRelayStateChangedEventArgs e)
    {
        ApplyState(e.State);
    }

    private void ApplyState(ContextRelayHostState state)
    {
        isApplyingState = true;
        try
        {
            QueryText = state.QueryText;
            HelpText = state.HelpText;
            StatusMessage = state.StatusMessage;
            RefreshLocalizedUiTexts();
            SignedInUserText = string.IsNullOrWhiteSpace(state.SignedInUser)
                ? ContextRelayLocalizedStrings.SignedOutText
                : ContextRelayLocalizedStrings.GetSignedInUserText(state.SignedInUser!);
            SearchResults = state.SearchResults.Select(item => new ContextItemViewModel(item, this)).ToArray();
            Snippets = state.Snippets.Select(item => new SnippetItemViewModel(item, this)).ToArray();
            ChatHistory = state.ChatHistory.Select(item => new ChatHistoryItemViewModel(item, this)).ToArray();
            CommandSuggestions = Array.Empty<SlashCommandSuggestion>();
            VisibleCommandSuggestions = Array.Empty<SlashCommandSuggestion>();
            SelectedCommandSuggestion = null;
            commandSuggestionWindowStart = 0;
            IsCommandPopupOpen = false;
        }
        finally
        {
            isApplyingState = false;
        }
    }

    private async Task SubmitAsync(CancellationToken ct)
    {
        var query = QueryText;
        CloseCommandPopup();
        await RunBusyAsync(async () => { await host.SubmitQueryAsync(query, ct).ConfigureAwait(false); }).ConfigureAwait(false);
    }

    private async Task ConfirmQueryInputAsync(CancellationToken ct)
    {
        if (ApplySelectedCommandSuggestion())
        {
            return;
        }

        await SubmitAsync(ct).ConfigureAwait(false);
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (isBusy)
        {
            return;
        }

        isBusy = true;
        RaiseNotifyPropertyChangedEvent(nameof(IsNotBusy));
        try
        {
            await action().ConfigureAwait(false);
        }
        finally
        {
            isBusy = false;
            RaiseNotifyPropertyChangedEvent(nameof(IsNotBusy));
        }
    }

    private void MoveCommandSelection(int delta)
    {
        if (commandSuggestions.Count == 0)
        {
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
    }

    private bool ApplySelectedCommandSuggestion()
    {
        return ApplyCommandSuggestion(selectedCommandSuggestion);
    }

    private bool ApplyCommandSuggestion(SlashCommandSuggestion? suggestion)
    {
        if (!SlashCommandSuggestion.TryBuildCommittedQuery(IsCommandPopupOpen, suggestion, out var committedQuery))
        {
            return false;
        }

        SelectedCommandSuggestion = suggestion;
        QueryText = committedQuery;
        CloseCommandPopup();
        return true;
    }

    private void UpdateCommandSuggestions()
    {
        var suggestions = ContextRelayLocalizedStrings.GetCommandSuggestions(QueryText);
        CommandSuggestions = suggestions
            .Select(CreateInteractiveSuggestion)
            .ToArray();
        commandSuggestionWindowStart = 0;
        SelectedCommandSuggestion = commandSuggestions.Count > 0 ? commandSuggestions[0] : null;
        UpdateVisibleCommandSuggestions();
        IsCommandPopupOpen = commandSuggestions.Count > 0;
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
        DebugLogButtonText = ContextRelayLocalizedStrings.DebugLogButtonText;
        RaiseNotifyPropertyChangedEvent(nameof(DebugLogButtonText));
        SearchButtonText = ContextRelayLocalizedStrings.SearchButtonText;
        RaiseNotifyPropertyChangedEvent(nameof(SearchButtonText));
        SearchResultsHeaderText = ContextRelayLocalizedStrings.SearchResultsHeaderText;
        RaiseNotifyPropertyChangedEvent(nameof(SearchResultsHeaderText));
        SnippetsHeaderText = ContextRelayLocalizedStrings.SnippetsHeaderText;
        RaiseNotifyPropertyChangedEvent(nameof(SnippetsHeaderText));
        ChatHistoryHeaderText = ContextRelayLocalizedStrings.ChatHistoryHeaderText;
        RaiseNotifyPropertyChangedEvent(nameof(ChatHistoryHeaderText));
        SearchToolTipText = ContextRelayLocalizedStrings.SearchToolTip;
        RaiseNotifyPropertyChangedEvent(nameof(SearchToolTipText));
        CommandPopupHeaderText = ContextRelayLocalizedStrings.CommandPopupHeaderText;
        RaiseNotifyPropertyChangedEvent(nameof(CommandPopupHeaderText));
        WindowTitleText = ContextRelayLocalizedStrings.WindowTitleText;
    }

    private void CloseCommandPopup()
    {
        CommandSuggestions = Array.Empty<SlashCommandSuggestion>();
        VisibleCommandSuggestions = Array.Empty<SlashCommandSuggestion>();
        SelectedCommandSuggestion = null;
        commandSuggestionWindowStart = 0;
        IsCommandPopupOpen = false;
    }

    private void SelectCommandSuggestion(int index)
    {
        if (index < 0 || index >= commandSuggestions.Count)
        {
            return;
        }

        var nextWindowStart = CalculateVisibleWindowStart(
            totalCount: commandSuggestions.Count,
            selectedIndex: index,
            currentWindowStart: commandSuggestionWindowStart,
            maxVisibleCount: MaxVisibleCommandSuggestions);
        if (nextWindowStart != commandSuggestionWindowStart)
        {
            commandSuggestionWindowStart = nextWindowStart;
            UpdateVisibleCommandSuggestions();
        }

        SelectedCommandSuggestion = commandSuggestions[index];
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
        VisibleCommandSuggestions = commandSuggestions
            .Skip(commandSuggestionWindowStart)
            .Take(MaxVisibleCommandSuggestions)
            .ToArray();
    }

    private SlashCommandSuggestion CreateInteractiveSuggestion(SlashCommandSuggestion suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);

        var interactiveSuggestion = new SlashCommandSuggestion
        {
            Icon = suggestion.Icon,
            Name = suggestion.Name,
            Description = suggestion.Description
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
}
