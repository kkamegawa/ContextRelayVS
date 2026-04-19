using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ContextRelay.Core.Models;
using ContextRelay.Core.SharedStore;
using Microsoft.VisualStudio.Shell;
using ContextRelay.VSExtension.Services;

namespace ContextRelay.VSExtension.ToolWindows;

public sealed partial class ContextRelayToolWindowControl : UserControl, INotifyPropertyChanged
{
    private readonly ContextRelayHost host;
    private bool isBusy;
    private string queryText = string.Empty;
    private string helpText = "Type a query to search Microsoft 365 content.";
    private string statusMessage = "ContextRelay is ready.";
    private string signedInUserText = "Not signed in.";

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
        QueryText = state.QueryText;
        HelpText = state.HelpText;
        StatusMessage = state.StatusMessage;
        SignedInUserText = string.IsNullOrWhiteSpace(state.SignedInUser)
            ? "Not signed in. Configure Client ID under Tools > Options > ContextRelay."
            : $"Signed in as {state.SignedInUser}";

        ReplaceItems(SearchResults, state.SearchResults);
        ReplaceItems(Snippets, state.Snippets);
        ReplaceItems(ChatHistory, state.ChatHistory);
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
        if (((FrameworkElement)sender).Tag is ContextItem item)
        {
            _ = RunBusyAsync(async () => await host.PinSnippetAsync(item).ConfigureAwait(false));
        }
    }

    private void OpenResultButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is ContextItem item)
        {
            host.OpenExternalUrl(item.Url);
        }
    }

    private void OpenSnippetButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is SharedSnippetItem item)
        {
            host.OpenExternalUrl(item.SourceUrl);
        }
    }

    private void DeleteSnippetButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is SharedSnippetItem item)
        {
            _ = RunBusyAsync(async () => await host.DeleteSnippetAsync(item.Id).ConfigureAwait(false));
        }
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
