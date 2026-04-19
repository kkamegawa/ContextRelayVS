using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ContextRelay.Core.Models;
using ContextRelay.Core.SharedStore;
using ContextRelay.VSExtension.Services;
using Microsoft.VisualStudio.Shell;

namespace ContextRelay.VSExtension.ToolWindows;

public sealed partial class ContextRelayToolWindowControl : UserControl, INotifyPropertyChanged
{
    private readonly ContextRelayHost host;
    private bool isApplyingState;
    private bool isBusy;
    private bool isCommandPopupOpen;
    private string queryText = string.Empty;
    private string helpText = ContextRelayLocalizedStrings.GenericHelpText;
    private string statusMessage = ContextRelayLocalizedStrings.ReadyStatus;
    private string signedInUserText = ContextRelayLocalizedStrings.SignedOutText;
    private SlashCommandSuggestion? selectedCommandSuggestion;

    internal ContextRelayToolWindowControl(ContextRelayHost host)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        InitializeComponent();
        DataContext = this;
        host.StateChanged += Host_StateChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ContextItem> SearchResults { get; } = new();

    public ObservableCollection<SharedSnippetItem> Snippets { get; } = new();

    public ObservableCollection<SharedChatHistoryItem> ChatHistory { get; } = new();

    public ObservableCollection<SlashCommandSuggestion> CommandSuggestions { get; } = new();

    public string GenerateHandoffButtonText => ContextRelayLocalizedStrings.GenerateHandoffButtonText;

    public string CopyPromptButtonText => ContextRelayLocalizedStrings.CopyPromptButtonText;

    public string OpenHandoffButtonText => ContextRelayLocalizedStrings.OpenHandoffButtonText;

    public string OpenCopilotButtonText => ContextRelayLocalizedStrings.OpenCopilotButtonText;

    public string ClearChatButtonText => ContextRelayLocalizedStrings.ClearChatButtonText;

    public string ClearSnippetsButtonText => ContextRelayLocalizedStrings.ClearSnippetsButtonText;

    public string ClearCacheButtonText => ContextRelayLocalizedStrings.ClearCacheButtonText;

    public string SettingsButtonText => ContextRelayLocalizedStrings.SettingsButtonText;

    public string DebugLogButtonText => ContextRelayLocalizedStrings.DebugLogButtonText;

    public string SearchButtonText => ContextRelayLocalizedStrings.SearchButtonText;

    public string SearchResultsHeaderText => ContextRelayLocalizedStrings.SearchResultsHeaderText;

    public string SnippetsHeaderText => ContextRelayLocalizedStrings.SnippetsHeaderText;

    public string ChatHistoryHeaderText => ContextRelayLocalizedStrings.ChatHistoryHeaderText;

    public string SearchToolTipText => ContextRelayLocalizedStrings.SearchToolTip;

    public string PinButtonText => ContextRelayLocalizedStrings.PinButtonText;

    public string OpenButtonText => ContextRelayLocalizedStrings.OpenButtonText;

    public string DeleteButtonText => ContextRelayLocalizedStrings.DeleteButtonText;

    public string CopyMenuText => ContextRelayLocalizedStrings.CopyMenuText;

    public string AppendToHandoffMenuText => ContextRelayLocalizedStrings.AppendToHandoffMenuText;

    public string OpenInBrowserMenuText => ContextRelayLocalizedStrings.OpenInBrowserMenuText;

    public string CommandPopupHeaderText => ContextRelayLocalizedStrings.CommandPopupHeaderText;

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
            OnPropertyChanged(nameof(QueryText));
            if (!isApplyingState)
            {
                UpdateCommandSuggestions();
                UpdateTransientHelpText();
            }
        }
    }

    public string HelpText
    {
        get => helpText;
        private set
        {
            if (helpText == value)
            {
                return;
            }

            helpText = value;
            OnPropertyChanged(nameof(HelpText));
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set
        {
            if (statusMessage == value)
            {
                return;
            }

            statusMessage = value;
            OnPropertyChanged(nameof(StatusMessage));
        }
    }

    public string SignedInUserText
    {
        get => signedInUserText;
        private set
        {
            if (signedInUserText == value)
            {
                return;
            }

            signedInUserText = value;
            OnPropertyChanged(nameof(SignedInUserText));
        }
    }

    public bool IsCommandPopupOpen
    {
        get => isCommandPopupOpen;
        private set
        {
            if (isCommandPopupOpen == value)
            {
                return;
            }

            isCommandPopupOpen = value;
            OnPropertyChanged(nameof(IsCommandPopupOpen));
        }
    }

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
            OnPropertyChanged(nameof(SelectedCommandSuggestion));
            if (!isApplyingState && IsCommandPopupOpen)
            {
                UpdateTransientHelpText();
            }
        }
    }

    public bool IsNotBusy => !isBusy;

    public void FocusSearchBox()
    {
        SearchTextBox.Focus();
        SearchTextBox.SelectAll();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ThreadHelper.JoinableTaskFactory.Run(RefreshAsync);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        host.StateChanged -= Host_StateChanged;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
    }

    private void Host_StateChanged(object? sender, ContextRelayStateChangedEventArgs e)
    {
        ThreadHelper.JoinableTaskFactory.Run(async delegate
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            ApplyState(e.State);
        });
    }

    private async Task RefreshAsync()
    {
        await RunBusyAsync(async delegate
        {
            var state = await host.GetStateAsync().ConfigureAwait(false);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            ApplyState(state);
        }).ConfigureAwait(false);
    }

    private async Task SubmitAsync()
    {
        var query = QueryText;
        CloseCommandPopup();
        await RunBusyAsync(async delegate
        {
            var state = await host.SubmitQueryAsync(query).ConfigureAwait(false);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            ApplyState(state);
        }).ConfigureAwait(false);
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (isBusy)
        {
            return;
        }

        isBusy = true;
        OnPropertyChanged(nameof(IsNotBusy));
        try
        {
            await action().ConfigureAwait(false);
        }
        finally
        {
            isBusy = false;
            OnPropertyChanged(nameof(IsNotBusy));
        }
    }

    private void ApplyState(ContextRelayHostState state)
    {
        isApplyingState = true;
        try
        {
            QueryText = state.QueryText;
            HelpText = state.HelpText;
            StatusMessage = state.StatusMessage;
            var signedInUser = state.SignedInUser;
            if (string.IsNullOrWhiteSpace(signedInUser))
            {
                SignedInUserText = ContextRelayLocalizedStrings.SignedOutText;
            }
            else
            {
                SignedInUserText = ContextRelayLocalizedStrings.GetSignedInUserText(signedInUser!);
            }

            ReplaceItems(SearchResults, state.SearchResults);
            ReplaceItems(Snippets, state.Snippets);
            ReplaceItems(ChatHistory, state.ChatHistory);
            ReplaceItems(CommandSuggestions, Array.Empty<SlashCommandSuggestion>());
            SelectedCommandSuggestion = null;
            IsCommandPopupOpen = false;
        }
        finally
        {
            isApplyingState = false;
        }
    }

    private static void ReplaceItems<T>(ObservableCollection<T> target, System.Collections.Generic.IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (IsCommandPopupOpen)
        {
            switch (e.Key)
            {
                case Key.Down:
                    MoveCommandSelection(1);
                    e.Handled = true;
                    return;
                case Key.Up:
                    MoveCommandSelection(-1);
                    e.Handled = true;
                    return;
                case Key.Tab:
                case Key.Enter:
                    if (ApplySelectedCommandSuggestion())
                    {
                        e.Handled = true;
                        return;
                    }

                    break;
                case Key.Escape:
                    CloseCommandPopup();
                    e.Handled = true;
                    return;
            }
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = SubmitAsync();
        }
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        _ = SubmitAsync();
    }

    private void CommandSuggestionsListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ApplySelectedCommandSuggestion())
        {
            e.Handled = true;
        }
    }

    private void GenerateHandoffButton_Click(object sender, RoutedEventArgs e)
    {
        _ = RunBusyAsync(async () => await host.GenerateHandoffAsync().ConfigureAwait(false));
    }

    private void CopyPromptButton_Click(object sender, RoutedEventArgs e)
    {
        _ = RunBusyAsync(async () => await host.CopyHandoffPromptAsync().ConfigureAwait(false));
    }

    private void OpenHandoffButton_Click(object sender, RoutedEventArgs e)
    {
        _ = RunBusyAsync(async () => await host.OpenHandoffDocumentAsync().ConfigureAwait(false));
    }

    private void OpenCopilotButton_Click(object sender, RoutedEventArgs e)
    {
        _ = RunBusyAsync(async () => await host.OpenCopilotChatWithPromptAsync().ConfigureAwait(false));
    }

    private void ClearChatButton_Click(object sender, RoutedEventArgs e)
    {
        _ = RunBusyAsync(async () => await host.ClearChatAsync().ConfigureAwait(false));
    }

    private void ClearSnippetsButton_Click(object sender, RoutedEventArgs e)
    {
        _ = RunBusyAsync(async () => await host.ClearSnippetsAsync().ConfigureAwait(false));
    }

    private void ClearCacheButton_Click(object sender, RoutedEventArgs e)
    {
        _ = RunBusyAsync(async () => await host.ClearCacheAsync().ConfigureAwait(false));
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ThreadHelper.JoinableTaskFactory.Run(async delegate
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            host.OpenSettings();
        });
    }

    private void DebugLogButton_Click(object sender, RoutedEventArgs e)
    {
        host.ShowDebugLog();
    }

    private void PinResultButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetTag(sender, out ContextItem? item) && item is not null)
        {
            _ = RunBusyAsync(async () => await host.PinSnippetAsync(item).ConfigureAwait(false));
        }
    }

    private void CopyResultMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetTag(sender, out ContextItem? item) && item is not null)
        {
            _ = RunBusyAsync(async () => await host.CopyResultToClipboardAsync(item).ConfigureAwait(false));
        }
    }

    private void AppendResultMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetTag(sender, out ContextItem? item) && item is not null)
        {
            _ = RunBusyAsync(async () => await host.AppendResultToHandoffAsync(item).ConfigureAwait(false));
        }
    }

    private void OpenResultButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetTag(sender, out ContextItem? item) && item is not null)
        {
            host.OpenExternalUrl(item.Url);
        }
    }

    private void OpenSnippetButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetTag(sender, out SharedSnippetItem? item) && item is not null)
        {
            host.OpenExternalUrl(item.SourceUrl);
        }
    }

    private void DeleteSnippetButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetTag(sender, out SharedSnippetItem? item) && item is not null)
        {
            _ = RunBusyAsync(async () => await host.DeleteSnippetAsync(item.Id).ConfigureAwait(false));
        }
    }

    private void MoveCommandSelection(int delta)
    {
        if (CommandSuggestions.Count == 0)
        {
            return;
        }

        var currentIndex = SelectedCommandSuggestion is null
            ? -1
            : CommandSuggestions.IndexOf(SelectedCommandSuggestion);
        var nextIndex = currentIndex + delta;
        if (nextIndex < 0)
        {
            nextIndex = CommandSuggestions.Count - 1;
        }
        else if (nextIndex >= CommandSuggestions.Count)
        {
            nextIndex = 0;
        }

        SelectedCommandSuggestion = CommandSuggestions[nextIndex];
        CommandSuggestionsListBox.ScrollIntoView(SelectedCommandSuggestion);
    }

    private bool ApplySelectedCommandSuggestion()
    {
        if (SelectedCommandSuggestion is null)
        {
            return false;
        }

        QueryText = $"{SelectedCommandSuggestion.Name} ";
        CloseCommandPopup();
        SearchTextBox.Focus();
        SearchTextBox.CaretIndex = SearchTextBox.Text.Length;
        return true;
    }

    private void UpdateCommandSuggestions()
    {
        var suggestions = ContextRelayLocalizedStrings.GetCommandSuggestions(QueryText);
        ReplaceItems(CommandSuggestions, suggestions);
        SelectedCommandSuggestion = CommandSuggestions.Count > 0 ? CommandSuggestions[0] : null;
        IsCommandPopupOpen = CommandSuggestions.Count > 0;
    }

    private void UpdateTransientHelpText()
    {
        HelpText = IsCommandPopupOpen && SelectedCommandSuggestion is not null
            ? SelectedCommandSuggestion.Description
            : ContextRelayLocalizedStrings.GetHelpTextForQuery(QueryText);
    }

    private void CloseCommandPopup()
    {
        ReplaceItems(CommandSuggestions, Array.Empty<SlashCommandSuggestion>());
        SelectedCommandSuggestion = null;
        IsCommandPopupOpen = false;
    }

    private static bool TryGetTag<T>(object sender, out T? item)
        where T : class
    {
        item = (sender as FrameworkElement)?.Tag as T;
        return item is not null;
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
