using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.Chat;
using ContextRelay.Core.FileContext;
using ContextRelay.Core.Models;
using ContextRelay.Core.Router;
using ContextRelay.Core.SharedStore;
using Xunit;

namespace ContextRelay.Core.Tests.ToolWindows;

public sealed class SlashCommandSuggestionInteractionTests
{
    [Fact]
    public void StreamingStateUpdate_PreservesUnchangedCollectionsAndCommands()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var hostType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHost", throwOnError: true)!;
        var viewModelType = assembly.GetType("ContextRelay.VSExtension.ToolWindows.ContextRelayWindowViewModel", throwOnError: true)!;
        var stateType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHostState", throwOnError: true)!;
        var host = RuntimeHelpers.GetUninitializedObject(hostType);
        var viewModel = Activator.CreateInstance(viewModelType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { host }, null)!;
        var applyState = viewModelType.GetMethod("ApplyState", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var state = Activator.CreateInstance(stateType, nonPublic: true)!;
        var search = new[] { new ContextItem { Title = "title", Snippet = "snippet", Source = ContextSource.Mail, Timestamp = "time", Url = "url" } };
        var snippets = new[] { new SharedSnippetItem { Id = "snippet-id", Name = "name", Snippet = "text", Source = "source" } };
        var history = new[] { new SharedChatHistoryItem { Id = "history-id", Role = "assistant", Text = "reply", Timestamp = "time" } };
        var pending = new[] { new ResolvedAttachment { Id = "pending-id", RelativePath = "file.md" } };
        SetState(stateType, state, "SearchResults", search);
        SetState(stateType, state, "Snippets", snippets);
        SetState(stateType, state, "ChatHistory", history);
        SetState(stateType, state, "PendingAttachments", pending);
        applyState.Invoke(viewModel, new[] { state });

        var searchProperty = viewModelType.GetProperty("SearchResults")!;
        var snippetsProperty = viewModelType.GetProperty("Snippets")!;
        var historyProperty = viewModelType.GetProperty("ChatHistory")!;
        var pendingProperty = viewModelType.GetProperty("PendingAttachments")!;
        var commandProperty = viewModelType.GetProperty("SearchCommand")!;
        var firstSearch = searchProperty.GetValue(viewModel);
        var firstSnippets = snippetsProperty.GetValue(viewModel);
        var firstHistory = historyProperty.GetValue(viewModel);
        var firstPending = pendingProperty.GetValue(viewModel);
        var command = commandProperty.GetValue(viewModel);

        SetState(stateType, state, "IsStreaming", true);
        SetState(stateType, state, "StreamingResponseText", "partial response");
        applyState.Invoke(viewModel, new[] { state });

        Assert.Same(firstSearch, searchProperty.GetValue(viewModel));
        Assert.Same(firstSnippets, snippetsProperty.GetValue(viewModel));
        Assert.Same(firstHistory, historyProperty.GetValue(viewModel));
        Assert.Same(firstPending, pendingProperty.GetValue(viewModel));
        Assert.Same(command, commandProperty.GetValue(viewModel));
        Assert.Equal("partial response", viewModelType.GetProperty("StreamingResponseText")!.GetValue(viewModel));

        SetState(stateType, state, "PendingAttachments", new[] { new ResolvedAttachment { Id = "replacement-id", RelativePath = "file.md" } });
        applyState.Invoke(viewModel, new[] { state });
        Assert.NotSame(firstPending, pendingProperty.GetValue(viewModel));
    }

    [Fact]
    public void OnHostStateChanged_StreamingOnlyUpdate_NotifiesOnlyStreamingProperties()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var hostType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHost", throwOnError: true)!;
        var viewModelType = assembly.GetType("ContextRelay.VSExtension.ToolWindows.ContextRelayWindowViewModel", throwOnError: true)!;
        var stateType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHostState", throwOnError: true)!;
        var eventArgsType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayStateChangedEventArgs", throwOnError: true)!;
        var host = RuntimeHelpers.GetUninitializedObject(hostType);
        var viewModel = Activator.CreateInstance(viewModelType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { host }, null)!;
        var applyState = viewModelType.GetMethod("ApplyState", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var onHostStateChanged = viewModelType.GetMethod("OnHostStateChanged", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var state = Activator.CreateInstance(stateType, nonPublic: true)!;
        SetState(stateType, state, "SearchResults", new[] { new ContextItem { Title = "title", Snippet = "snippet", Source = ContextSource.Mail, Timestamp = "time", Url = "url" } });
        SetState(stateType, state, "PendingAttachments", new[] { new ResolvedAttachment { Id = "pending-id", RelativePath = "file.md" } });
        applyState.Invoke(viewModel, new[] { state });

        var notifyPropertyChanged = Assert.IsAssignableFrom<INotifyPropertyChanged>(viewModel);
        var raisedProperties = new System.Collections.Generic.List<string?>();
        notifyPropertyChanged.PropertyChanged += (_, e) => raisedProperties.Add(e.PropertyName);

        SetState(stateType, state, "IsStreaming", true);
        SetState(stateType, state, "StreamingResponseText", "partial response");
        var streamingArgs = Activator.CreateInstance(eventArgsType, new object[] { state, true })!;
        onHostStateChanged.Invoke(viewModel, new object?[] { null, streamingArgs });

        Assert.Equal("partial response", viewModelType.GetProperty("StreamingResponseText")!.GetValue(viewModel));
        Assert.True((bool)viewModelType.GetProperty("IsStreaming")!.GetValue(viewModel)!);
        Assert.Equal(
            new[] { "IsStreaming", "PrimaryActionButtonText", "IsPrimaryActionEnabled", "StreamingResponseText" },
            raisedProperties);
    }

    [Fact]
    public void SlashCommandSuggestion_IsSelected_RaisesPropertyChangedOnlyWhenValueChanges()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var suggestionType = assembly.GetType("ContextRelay.VSExtension.ToolWindows.SlashCommandSuggestion", throwOnError: true);
        var suggestion = Activator.CreateInstance(suggestionType!);
        var isSelectedProperty = suggestionType!.GetProperty("IsSelected");
        Assert.NotNull(isSelectedProperty);

        var notifyPropertyChanged = Assert.IsAssignableFrom<INotifyPropertyChanged>(suggestion);
        var raisedProperties = new System.Collections.Generic.List<string?>();
        notifyPropertyChanged.PropertyChanged += (_, e) => raisedProperties.Add(e.PropertyName);

        Assert.False((bool)isSelectedProperty!.GetValue(suggestion)!);

        isSelectedProperty.SetValue(suggestion, true);
        isSelectedProperty.SetValue(suggestion, true);
        isSelectedProperty.SetValue(suggestion, false);

        Assert.True((bool)isSelectedProperty.GetValue(suggestion)! == false);
        Assert.Equal(new[] { "IsSelected", "IsSelected" }, raisedProperties);
    }

    [Fact]
    public void TryBuildCommittedQuery_WhenPopupIsOpen_AppendsTrailingSpace()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var suggestionType = assembly.GetType("ContextRelay.VSExtension.ToolWindows.SlashCommandSuggestion", throwOnError: true);
        var method = suggestionType!.GetMethod("TryBuildCommittedQuery", BindingFlags.Static | BindingFlags.NonPublic);
        var suggestion = Activator.CreateInstance(suggestionType);
        var nameProperty = suggestionType.GetProperty("Name");
        Assert.NotNull(method);
        Assert.NotNull(nameProperty);
        nameProperty!.SetValue(suggestion, "/onedrive");
        var parameters = new object?[] { true, suggestion, null };

        var result = (bool)method!.Invoke(obj: null, parameters)!;

        Assert.True(result);
        Assert.Equal("/onedrive ", Assert.IsType<string>(parameters[2]));
    }

    [Fact]
    public void TryBuildCommittedQuery_WhenCommittedQueryIsProvided_PreservesFullSelectionContext()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var suggestionType = assembly.GetType("ContextRelay.VSExtension.ToolWindows.SlashCommandSuggestion", throwOnError: true);
        var method = suggestionType!.GetMethod("TryBuildCommittedQuery", BindingFlags.Static | BindingFlags.NonPublic);
        var suggestion = Activator.CreateInstance(suggestionType);
        var nameProperty = suggestionType.GetProperty("Name");
        var committedQueryProperty = suggestionType.GetProperty("CommittedQuery");
        Assert.NotNull(method);
        Assert.NotNull(nameProperty);
        Assert.NotNull(committedQueryProperty);
        nameProperty!.SetValue(suggestion, "/onedrive");
        committedQueryProperty!.SetValue(suggestion, "/mail /onedrive ");
        var parameters = new object?[] { true, suggestion, null };

        var result = (bool)method!.Invoke(obj: null, parameters)!;

        Assert.True(result);
        Assert.Equal("/mail /onedrive ", Assert.IsType<string>(parameters[2]));
    }

    [Fact]
    public void TryBuildCommittedQuery_WhenPopupIsClosed_DoesNotCommitSuggestion()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var suggestionType = assembly.GetType("ContextRelay.VSExtension.ToolWindows.SlashCommandSuggestion", throwOnError: true);
        var method = suggestionType!.GetMethod("TryBuildCommittedQuery", BindingFlags.Static | BindingFlags.NonPublic);
        var suggestion = Activator.CreateInstance(suggestionType);
        var nameProperty = suggestionType.GetProperty("Name");
        Assert.NotNull(method);
        Assert.NotNull(nameProperty);
        nameProperty!.SetValue(suggestion, "/onenote");
        var parameters = new object?[] { false, suggestion, null };

        var result = (bool)method!.Invoke(obj: null, parameters)!;

        Assert.False(result);
        Assert.Equal(string.Empty, Assert.IsType<string>(parameters[2]));
    }

    [Fact]
    public void EmbeddedXaml_TogglesSendAndStopAndThemesStreamingText()
    {
        var assembly = LoadBuiltExtensionAssembly();
        using var stream = assembly.GetManifestResourceStream("ContextRelay.VSExtension.ToolWindows.ContextRelayWindowContent.xaml");

        Assert.NotNull(stream);

        using var reader = new StreamReader(stream!);
        var xaml = reader.ReadToEnd();

        // One primary button switches label and behavior, so keyboard focus survives the change.
        Assert.Contains("Content=\"{Binding PrimaryActionButtonText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding IsPrimaryActionEnabled}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("{Binding IsNotStreaming", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Command=\"{Binding StopGenerationCommand}\"", xaml, StringComparison.Ordinal);

        // The streaming preview is outside the chat list, so its text needs an explicit themed brush.
        Assert.Contains("Text=\"{Binding StreamingResponseText}\" Style=\"{StaticResource BodyTextStyle}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding StreamingResponseText}\" Style=\"{StaticResource CardBodyTextStyle}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void PrimaryAction_SwitchesToStopWhileStreamingAndStaysEnabled()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var hostType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHost", throwOnError: true)!;
        var viewModelType = assembly.GetType("ContextRelay.VSExtension.ToolWindows.ContextRelayWindowViewModel", throwOnError: true)!;
        var stateType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHostState", throwOnError: true)!;
        var host = RuntimeHelpers.GetUninitializedObject(hostType);
        var viewModel = Activator.CreateInstance(viewModelType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { host }, null)!;
        var applyState = viewModelType.GetMethod("ApplyState", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var state = Activator.CreateInstance(stateType, nonPublic: true)!;
        applyState.Invoke(viewModel, new[] { state });

        var primaryText = viewModelType.GetProperty("PrimaryActionButtonText")!;
        var primaryEnabled = viewModelType.GetProperty("IsPrimaryActionEnabled")!;
        var searchText = viewModelType.GetProperty("SearchButtonText")!.GetValue(viewModel);
        var stopText = viewModelType.GetProperty("StopGenerationButtonText")!.GetValue(viewModel);

        Assert.Equal(searchText, primaryText.GetValue(viewModel));
        Assert.True((bool)primaryEnabled.GetValue(viewModel)!);

        SetState(stateType, state, "IsStreaming", true);
        applyState.Invoke(viewModel, new[] { state });

        Assert.Equal(stopText, primaryText.GetValue(viewModel));
        Assert.True((bool)primaryEnabled.GetValue(viewModel)!);

        // The enabled state across the submit-to-streaming gap is covered by
        // PrimaryAction_WhileSubmitting_IsEnabledOnlyForChatRoutesOnEitherSubmitPath, which drives
        // the real command instead of setting the fields it maintains.
    }

    [Fact]
    public void PrimaryActionCaption_IsNotifiedAfterBothLabelsAreRefreshed()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var hostType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHost", throwOnError: true)!;
        var viewModelType = assembly.GetType("ContextRelay.VSExtension.ToolWindows.ContextRelayWindowViewModel", throwOnError: true)!;
        var stateType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHostState", throwOnError: true)!;
        var host = RuntimeHelpers.GetUninitializedObject(hostType);
        var viewModel = Activator.CreateInstance(viewModelType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { host }, null)!;
        var applyState = viewModelType.GetMethod("ApplyState", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var state = Activator.CreateInstance(stateType, nonPublic: true)!;
        SetState(stateType, state, "IsStreaming", true);
        applyState.Invoke(viewModel, new[] { state });

        var notifyPropertyChanged = Assert.IsAssignableFrom<INotifyPropertyChanged>(viewModel);
        var raised = new System.Collections.Generic.List<string?>();
        notifyPropertyChanged.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        applyState.Invoke(viewModel, new[] { state });

        // The derived caption reads whichever label matches the current state, so it must be
        // notified after both labels have been reassigned for the current UI language.
        var caption = raised.LastIndexOf("PrimaryActionButtonText");
        var stopLabel = raised.LastIndexOf("StopGenerationButtonText");
        var sendLabel = raised.LastIndexOf("SearchButtonText");
        Assert.True(caption >= 0);
        Assert.True(caption > stopLabel);
        Assert.True(caption > sendLabel);
    }

    [Fact]
    public void SuggestionKeyBindings_AreDisabledWhileTheCommandPopupIsClosed()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var hostType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHost", throwOnError: true)!;
        var viewModelType = assembly.GetType("ContextRelay.VSExtension.ToolWindows.ContextRelayWindowViewModel", throwOnError: true)!;
        var host = RuntimeHelpers.GetUninitializedObject(hostType);
        var viewModel = Activator.CreateInstance(viewModelType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { host }, null)!;

        // The query box binds Tab to the apply command. While the popup is closed the binding must
        // not handle the key, otherwise Tab cannot move focus to the composer's primary button.
        var applyCommand = viewModelType.GetProperty("ApplyCommandSelectionCommand")!.GetValue(viewModel)!;
        var canExecute = applyCommand.GetType().GetProperty("CanExecute")!;
        var popupOpen = viewModelType.GetProperty("IsCommandPopupOpen")!;

        Assert.False((bool)popupOpen.GetValue(viewModel)!);
        Assert.False((bool)canExecute.GetValue(applyCommand)!);

        var popupField = viewModelType.GetField("isCommandPopupOpen", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var refresh = viewModelType.GetMethod("UpdateSuggestionKeyBindingAvailability", BindingFlags.Instance | BindingFlags.NonPublic)!;
        popupField.SetValue(viewModel, true);
        refresh.Invoke(viewModel, null);

        Assert.True((bool)canExecute.GetValue(applyCommand)!);
    }

    // Both the primary button (SearchCommand) and Enter in the query box (ConfirmQueryInputCommand)
    // submit, and they must leave the primary button in the same state. Enter used to bypass the
    // enabled-state bookkeeping, so the button was disabled and Tab could not reach Stop.
    [Theory]
    [InlineData("SearchCommand", "explain this", true)]
    [InlineData("ConfirmQueryInputCommand", "explain this", true)]
    [InlineData("SearchCommand", "/ask summarize", true)]
    [InlineData("ConfirmQueryInputCommand", "/ask summarize", true)]
    [InlineData("SearchCommand", "/mail budget", false)]
    [InlineData("ConfirmQueryInputCommand", "/mail budget", false)]
    public async Task PrimaryAction_WhileSubmitting_IsEnabledOnlyForChatRoutesOnEitherSubmitPath(
        string commandName,
        string query,
        bool expectedEnabled)
    {
        var assembly = LoadBuiltExtensionAssembly();
        var hostType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHost", throwOnError: true)!;
        var loggerType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayOutputLogger", throwOnError: true)!;
        var viewModelType = assembly.GetType("ContextRelay.VSExtension.ToolWindows.ContextRelayWindowViewModel", throwOnError: true)!;
        var host = RuntimeHelpers.GetUninitializedObject(hostType);

        // Give the bare host what the submit path touches, and hold its gate closed so the request
        // stays in flight in exactly the window between submit and the first streaming update.
        using var submitGate = new SemaphoreSlim(0, 1);
        hostType.GetField("logger", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(host, RuntimeHelpers.GetUninitializedObject(loggerType));
        hostType.GetField("gate", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, submitGate);
        hostType.GetField("draftQuerySync", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, new object());

        var viewModel = Activator.CreateInstance(viewModelType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { host }, null)!;
        viewModelType.GetField("queryText", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(viewModel, query);
        var primaryEnabled = viewModelType.GetProperty("IsPrimaryActionEnabled")!;
        var busyField = viewModelType.GetField("isBusy", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var command = viewModelType.GetProperty(commandName)!.GetValue(viewModel)!;
        var execute = command.GetType().GetMethods()
            .Single(method => method.Name == "ExecuteAsync" && method.GetParameters().Length == 3);
        using var cancellation = new CancellationTokenSource();
        var execution = (Task)execute.Invoke(command, new object?[] { null, null, cancellation.Token })!;

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!(bool)busyField.GetValue(viewModel)! && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True((bool)busyField.GetValue(viewModel)!);

        // Busy but not streaming yet. A chat request keeps the button enabled, because disabling a
        // focused control moves keyboard focus away and WPF does not restore it. Other routes keep
        // the ordinary disabled-while-busy state.
        Assert.False((bool)viewModelType.GetProperty("IsStreaming")!.GetValue(viewModel)!);
        Assert.Equal(expectedEnabled, (bool)primaryEnabled.GetValue(viewModel)!);

        cancellation.Cancel();
        try
        {
            await execution;
        }
        catch (OperationCanceledException)
        {
            // Expected: the held gate is released only by cancelling the request.
        }

        // Finished requests hand the button back to its ordinary state.
        Assert.True((bool)primaryEnabled.GetValue(viewModel)!);
    }

    [Fact]
    public async Task PrimaryAction_WhenNotStreaming_SubmitsInsteadOfCancelling()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var hostType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHost", throwOnError: true)!;
        var loggerType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayOutputLogger", throwOnError: true)!;
        var viewModelType = assembly.GetType("ContextRelay.VSExtension.ToolWindows.ContextRelayWindowViewModel", throwOnError: true)!;
        var host = RuntimeHelpers.GetUninitializedObject(hostType);

        // An active request that only the stop branch may cancel.
        var stateMachine = new ChatRequestStateMachine();
        hostType.GetField("chatRequestStateMachine", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, stateMachine);
        hostType.GetField("activeChatRequestSync", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, new object());
        using var requestCancellation = new CancellationTokenSource();
        Assert.True(stateMachine.TryBegin(Array.Empty<string>(), requestCancellation.Token, out var request));

        // Hold the host's gate closed so SubmitQueryAsync blocks at a boundary the test can observe.
        // Reaching the busy state proves the send branch entered the submission path; there is no
        // catch-all, so an unexpected failure surfaces instead of being read as success.
        using var submitGate = new SemaphoreSlim(0, 1);
        hostType.GetField("logger", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(host, RuntimeHelpers.GetUninitializedObject(loggerType));
        hostType.GetField("gate", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, submitGate);
        hostType.GetField("draftQuerySync", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, new object());

        var viewModel = Activator.CreateInstance(viewModelType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { host }, null)!;
        viewModelType.GetField("queryText", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(viewModel, "explain this");
        Assert.False((bool)viewModelType.GetProperty("IsStreaming")!.GetValue(viewModel)!);
        var busyField = viewModelType.GetField("isBusy", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var command = viewModelType.GetProperty("SearchCommand")!.GetValue(viewModel)!;
        var execute = command.GetType().GetMethods()
            .Single(method => method.Name == "ExecuteAsync" && method.GetParameters().Length == 3);
        using var cancellation = new CancellationTokenSource();
        var execution = (Task)execute.Invoke(command, new object?[] { null, null, cancellation.Token })!;

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!(bool)busyField.GetValue(viewModel)! && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        // The command entered the submission path and is waiting on the host, so it sent.
        Assert.True((bool)busyField.GetValue(viewModel)!);
        Assert.False(execution.IsCompleted);

        // ...and it did not take the stop branch, which is the only thing that cancels the request.
        Assert.False(request!.CancellationToken.IsCancellationRequested);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
    }

    [Fact]
    public async Task PrimaryAction_WhileStreaming_RequestsCancellationInsteadOfSubmitting()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var hostType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHost", throwOnError: true)!;
        var viewModelType = assembly.GetType("ContextRelay.VSExtension.ToolWindows.ContextRelayWindowViewModel", throwOnError: true)!;
        var stateType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHostState", throwOnError: true)!;
        var host = RuntimeHelpers.GetUninitializedObject(hostType);

        // StopGeneration cancels through the request state machine, so give the bare host the two
        // fields it touches and start a request that the command is expected to cancel.
        var stateMachine = new ChatRequestStateMachine();
        hostType.GetField("chatRequestStateMachine", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, stateMachine);
        hostType.GetField("activeChatRequestSync", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, new object());
        using var requestCancellation = new CancellationTokenSource();
        Assert.True(stateMachine.TryBegin(Array.Empty<string>(), requestCancellation.Token, out var request));

        var viewModel = Activator.CreateInstance(viewModelType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { host }, null)!;
        var applyState = viewModelType.GetMethod("ApplyState", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var state = Activator.CreateInstance(stateType, nonPublic: true)!;
        SetState(stateType, state, "IsStreaming", true);
        applyState.Invoke(viewModel, new[] { state });

        var command = viewModelType.GetProperty("SearchCommand")!.GetValue(viewModel)!;
        // The client context type lives in the extensibility SDK, which this project does not
        // reference, so resolve the overload by shape and pass no context; the stop path ignores it.
        var execute = command.GetType().GetMethods()
            .Single(method => method.Name == "ExecuteAsync" && method.GetParameters().Length == 3);
        await (Task)execute.Invoke(command, new object?[] { null, null, TestContext.Current.CancellationToken })!;

        Assert.True(request!.CancellationToken.IsCancellationRequested);

        // A second submission must not start: the submit path would flag an active chat request.
        var chatRequestField = viewModelType.GetField("isChatRequestActive", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.False((bool)chatRequestField.GetValue(viewModel)!);
    }

    [Fact]
    public void EmbeddedXaml_WiresSuggestionApplyAndConfirmBindings()
    {
        var assembly = LoadBuiltExtensionAssembly();
        using var stream = assembly.GetManifestResourceStream("ContextRelay.VSExtension.ToolWindows.ContextRelayWindowContent.xaml");

        Assert.NotNull(stream);

        using var reader = new StreamReader(stream!);
        var xaml = reader.ReadToEnd();

        Assert.Contains("Command=\"{Binding ApplyCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ConfirmQueryInputCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding VisibleCommandSuggestions}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedIndex=\"{Binding SelectedVisibleCommandSuggestionIndex", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedItem=\"{Binding SelectedCommandSuggestion", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SuggestionRow\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DataTrigger Binding=\"{Binding IsSelected}\" Value=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter TargetName=\"SuggestionRow\" Property=\"Background\" Value=\"{DynamicResource {x:Static colors:EnvironmentColors.CommandBarSelectedBrushKey}}\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter TargetName=\"SuggestionRow\" Property=\"Foreground\" Value=\"{DynamicResource {x:Static colors:EnvironmentColors.CommandBarTextSelectedBrushKey}}\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource SuggestionPopupListBoxStyle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemContainerStyle=\"{StaticResource SuggestionListBoxItemStyle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FocusManager.IsFocusScope=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FocusManager.FocusedElement=\"{Binding ElementName=QueryTextBox}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"QueryTextBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding AddFilesCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding AddFilesButtonText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{Binding AddFilesToolTipText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolTip=\"{Binding AddFilesToolTipText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolWindowTextBrushKey", xaml, StringComparison.Ordinal);
        Assert.Contains("Focusable\" Value=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsTabStop\" Value=\"False\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"{Binding DebugLogButtonText}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Command=\"{Binding ShowDebugLogCommand}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("EventTrigger EventName=\"Loaded\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CallMethodAction", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("xmlns:i=\"http://schemas.microsoft.com/xaml/behaviors\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("mc:Ignorable=\"i\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void EmbeddedXaml_DefinesFluentVisualAndThemeContract()
    {
        var assembly = LoadBuiltExtensionAssembly();
        using var stream = assembly.GetManifestResourceStream("ContextRelay.VSExtension.ToolWindows.ContextRelayWindowContent.xaml");

        Assert.NotNull(stream);

        using var reader = new StreamReader(stream!);
        var xaml = reader.ReadToEnd();

        Assert.Contains("x:Key=\"PanelSurfaceBorderStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"CardBorderStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ThemedButtonStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"PrimaryButtonStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"FluentListBoxItemStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ThemedTextBoxStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Focusable\" Value=\"True\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"IsTabStop\" Value=\"True\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Background\" Value=\"{DynamicResource {x:Static colors:EnvironmentColors.CommandBarSelectedBrushKey}}\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Foreground\" Value=\"{DynamicResource {x:Static colors:EnvironmentColors.CommandBarTextSelectedBrushKey}}\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("Background\" Value=\"{Binding Background, RelativeSource={RelativeSource AncestorType=ListBoxItem}}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Foreground\" Value=\"{Binding Foreground, RelativeSource={RelativeSource AncestorType=ListBoxItem}}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ContentPresenter Foreground=", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectionBrush\" Value=\"{DynamicResource {x:Static colors:EnvironmentColors.SystemHighlightBrushKey}}", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectionTextBrush\" Value=\"{DynamicResource {x:Static colors:EnvironmentColors.SystemHighlightTextBrushKey}}", xaml, StringComparison.Ordinal);
        Assert.Contains("CaretBrush\" Value=\"{DynamicResource {x:Static colors:EnvironmentColors.ToolWindowTextBrushKey}}", xaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource PrimaryButtonStyle}\" Grid.Row=\"2\" Grid.Column=\"2\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding PrimaryActionButtonText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding PendingAttachments}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalContentAlignment=\"Stretch\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"Auto\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding SearchCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding StreamingResponseText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=\"180\"", xaml, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{Binding RemoveAutomationName}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"ScrollViewer.CanContentScroll\" Value=\"False\" />", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("StretchListBoxItemStyle", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Background=\"#", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ViewModel_DoesNotExposePanelOnlyDebugLogMembers()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var viewModelType = assembly.GetType("ContextRelay.VSExtension.ToolWindows.ContextRelayWindowViewModel", throwOnError: true);

        Assert.NotNull(viewModelType);
        Assert.Null(viewModelType!.GetProperty("DebugLogButtonText", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        Assert.Null(viewModelType.GetProperty("ShowDebugLogCommand", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
    }

    [Theory]
    [InlineData(6, 4, 0, 4, 1)]
    [InlineData(6, 5, 1, 4, 2)]
    [InlineData(6, 1, 2, 4, 1)]
    [InlineData(3, 2, 0, 4, 0)]
    public void CalculateVisibleWindowStart_TracksKeyboardSelection(int totalCount, int selectedIndex, int currentWindowStart, int maxVisibleCount, int expectedStart)
    {
        var assembly = LoadBuiltExtensionAssembly();
        var viewModelType = assembly.GetType("ContextRelay.VSExtension.ToolWindows.ContextRelayWindowViewModel", throwOnError: true);
        var method = viewModelType!.GetMethod("CalculateVisibleWindowStart", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var result = (int)method!.Invoke(obj: null, new object[] { totalCount, selectedIndex, currentWindowStart, maxVisibleCount })!;

        Assert.Equal(expectedStart, result);
    }

    [Fact]
    public void BuildVisibleWindow_ReturnsFreshInstancesWithSelectionStamped()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var suggestionType = assembly.GetType("ContextRelay.VSExtension.ToolWindows.SlashCommandSuggestion", throwOnError: true);
        var viewModelType = assembly.GetType("ContextRelay.VSExtension.ToolWindows.ContextRelayWindowViewModel", throwOnError: true);
        var method = viewModelType!.GetMethod("BuildVisibleWindow", BindingFlags.Static | BindingFlags.NonPublic);
        var isSelectedProperty = suggestionType!.GetProperty("IsSelected");
        var nameProperty = suggestionType.GetProperty("Name");
        var iconProperty = suggestionType.GetProperty("Icon");
        var descriptionProperty = suggestionType.GetProperty("Description");
        var committedQueryProperty = suggestionType.GetProperty("CommittedQuery");
        Assert.NotNull(method);
        Assert.NotNull(isSelectedProperty);

        var masters = Array.CreateInstance(suggestionType, 6);
        for (var i = 0; i < masters.Length; i++)
        {
            var master = Activator.CreateInstance(suggestionType);
            nameProperty!.SetValue(master, $"/command{i}");
            iconProperty!.SetValue(master, $"icon{i}");
            descriptionProperty!.SetValue(master, $"description{i}");
            committedQueryProperty!.SetValue(master, $"/command{i} ");
            masters.SetValue(master, i);
        }

        var selected = masters.GetValue(2);
        var window = (Array)method!.Invoke(obj: null, parameters: new object?[] { masters, 1, 4, selected })!;

        Assert.Equal(4, window.Length);
        for (var i = 0; i < window.Length; i++)
        {
            var clone = window.GetValue(i)!;
            var master = masters.GetValue(1 + i)!;

            // Remote UI de-duplicates transmitted objects by identity, so every visible item
            // must be a brand-new instance — never a reused master.
            foreach (var m in masters)
            {
                Assert.False(ReferenceEquals(clone, m));
            }

            Assert.Equal(nameProperty!.GetValue(master), nameProperty.GetValue(clone));
            Assert.Equal(iconProperty!.GetValue(master), iconProperty.GetValue(clone));
            Assert.Equal(descriptionProperty!.GetValue(master), descriptionProperty.GetValue(clone));
            Assert.Equal(committedQueryProperty!.GetValue(master), committedQueryProperty.GetValue(clone));
            Assert.Equal(ReferenceEquals(master, selected), (bool)isSelectedProperty!.GetValue(clone)!);
        }

        // Masters are never mutated: selection state lives only on the display clones.
        foreach (var master in masters)
        {
            Assert.False((bool)isSelectedProperty!.GetValue(master)!);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BuildVisibleWindow_WhenSelectionIsNullOrOutsideWindow_MarksNothing(bool useOutsideSelection)
    {
        var assembly = LoadBuiltExtensionAssembly();
        var suggestionType = assembly.GetType("ContextRelay.VSExtension.ToolWindows.SlashCommandSuggestion", throwOnError: true);
        var viewModelType = assembly.GetType("ContextRelay.VSExtension.ToolWindows.ContextRelayWindowViewModel", throwOnError: true);
        var method = viewModelType!.GetMethod("BuildVisibleWindow", BindingFlags.Static | BindingFlags.NonPublic);
        var isSelectedProperty = suggestionType!.GetProperty("IsSelected");
        Assert.NotNull(method);
        Assert.NotNull(isSelectedProperty);

        var masters = Array.CreateInstance(suggestionType, 6);
        for (var i = 0; i < masters.Length; i++)
        {
            masters.SetValue(Activator.CreateInstance(suggestionType), i);
        }

        // masters[0] sits before windowStart 1, so it is outside the visible window.
        var selected = useOutsideSelection ? masters.GetValue(0) : null;
        var window = (Array)method!.Invoke(obj: null, parameters: new object?[] { masters, 1, 4, selected })!;

        Assert.Equal(4, window.Length);
        foreach (var clone in window)
        {
            Assert.False((bool)isSelectedProperty!.GetValue(clone)!);
        }
    }

    [Fact]
    public void LocalizedStrings_GetCommandSuggestions_SupportsCombinableCommands()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var stringsType = assembly.GetType("ContextRelay.VSExtension.ToolWindows.ContextRelayLocalizedStrings", throwOnError: true);
        var method = stringsType!.GetMethod("GetCommandSuggestions", BindingFlags.Static | BindingFlags.Public);
        stringsType.GetMethod("SetUiLanguage", BindingFlags.Static | BindingFlags.Public)?.Invoke(obj: null, parameters: new object?[] { "en" });
        Assert.NotNull(method);

        var suggestions = Assert.IsAssignableFrom<System.Collections.IEnumerable>(method!.Invoke(obj: null, parameters: new object?[] { "/mail /on" }));
        var enumerator = suggestions.GetEnumerator();
        Assert.True(enumerator.MoveNext());
        var firstSuggestion = enumerator.Current!;

        Assert.Equal("/onedrive", Assert.IsType<string>(firstSuggestion.GetType().GetProperty("Name")!.GetValue(firstSuggestion)));
        Assert.Equal("/mail /onedrive ", Assert.IsType<string>(firstSuggestion.GetType().GetProperty("CommittedQuery")!.GetValue(firstSuggestion)));
    }

    [Fact]
    public void LocalizedStrings_GetChatResponseFailedStatus_FormatsConfiguredLanguage()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var stringsType = assembly.GetType("ContextRelay.VSExtension.ToolWindows.ContextRelayLocalizedStrings", throwOnError: true)!;
        var setLanguage = stringsType.GetMethod("SetUiLanguage", BindingFlags.Static | BindingFlags.Public)!;
        var formatFailure = stringsType.GetMethod("GetChatResponseFailedStatus", BindingFlags.Static | BindingFlags.Public)!;

        setLanguage.Invoke(null, new object?[] { "ja" });
        var japanese = Assert.IsType<string>(formatFailure.Invoke(null, new object?[] { "detail" }));
        setLanguage.Invoke(null, new object?[] { "en" });

        Assert.Contains("応答の生成に失敗しました", japanese, StringComparison.Ordinal);
        Assert.Contains("detail", japanese, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalizedStrings_AskRequiresContextStatus_MentionsAllSupportedContextSources()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var stringsType = assembly.GetType("ContextRelay.VSExtension.ToolWindows.ContextRelayLocalizedStrings", throwOnError: true)!;
        var setLanguage = stringsType.GetMethod("SetUiLanguage", BindingFlags.Static | BindingFlags.Public)!;
        var status = stringsType.GetProperty("AskRequiresContextStatus", BindingFlags.Static | BindingFlags.Public)!;

        setLanguage.Invoke(null, new object?[] { "en" });
        var english = Assert.IsType<string>(status.GetValue(null));
        Assert.Contains("pending file attachment", english, StringComparison.Ordinal);
        Assert.Contains("#path", english, StringComparison.Ordinal);
        Assert.Contains("active-editor attachment", english, StringComparison.Ordinal);

        setLanguage.Invoke(null, new object?[] { "ja" });
        var japanese = Assert.IsType<string>(status.GetValue(null));
        Assert.Contains("保留中のファイル添付", japanese, StringComparison.Ordinal);
        Assert.Contains("#path", japanese, StringComparison.Ordinal);
        Assert.Contains("アクティブ エディターの添付", japanese, StringComparison.Ordinal);

        setLanguage.Invoke(null, new object?[] { "en" });
    }

    [Fact]
    public void ContextRelayVsServices_SelectionEndingAtNextLineStart_UsesLastSelectedCharacter()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var servicesType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayVsServices", throwOnError: true);
        var method = servicesType!.GetMethod("GetInclusiveSelectionEndOffset", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var result = method!.Invoke(null, new object[] { 2, 10, (Func<int, int>)(offset => offset / 10) });

        Assert.Equal(9, Assert.IsType<int>(result));
    }

    [Fact]
    public void BuildComposerSuggestions_WhenHashMentionIsTyped_ReturnsFileSuggestions()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var viewModelType = assembly.GetType("ContextRelay.VSExtension.ToolWindows.ContextRelayWindowViewModel", throwOnError: true);
        var method = viewModelType!.GetMethod("BuildComposerSuggestions", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var suggestions = Assert.IsAssignableFrom<System.Collections.IEnumerable>(method!.Invoke(obj: null, new object[]
        {
            "/ask #docs/pl",
            new[] { "docs/plan.md", "docs/summary.md" }
        }));
        var enumerator = suggestions.GetEnumerator();
        Assert.True(enumerator.MoveNext());
        var firstSuggestion = enumerator.Current!;

        Assert.Equal("docs/plan.md", Assert.IsType<string>(firstSuggestion.GetType().GetProperty("Name")!.GetValue(firstSuggestion)));
        Assert.Equal("/ask #docs/plan.md", Assert.IsType<string>(firstSuggestion.GetType().GetProperty("CommittedQuery")!.GetValue(firstSuggestion)));
    }

    [Fact]
    public void BuildComposerSuggestions_WhenQuotedMentionIsClosed_ReturnsMatchingFileSuggestions()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var viewModelType = assembly.GetType("ContextRelay.VSExtension.ToolWindows.ContextRelayWindowViewModel", throwOnError: true);
        var method = viewModelType!.GetMethod("BuildComposerSuggestions", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var suggestions = Assert.IsAssignableFrom<System.Collections.IEnumerable>(method!.Invoke(obj: null, new object[]
        {
            "/ask #\"docs/release notes.md\"",
            new[] { "docs/release notes.md", "docs/summary.md" }
        }));
        var enumerator = suggestions.GetEnumerator();
        Assert.True(enumerator.MoveNext());
        var firstSuggestion = enumerator.Current!;

        Assert.Equal("docs/release notes.md", Assert.IsType<string>(firstSuggestion.GetType().GetProperty("Name")!.GetValue(firstSuggestion)));
        Assert.Equal("/ask #\"docs/release notes.md\"", Assert.IsType<string>(firstSuggestion.GetType().GetProperty("CommittedQuery")!.GetValue(firstSuggestion)));
    }

    [Fact]
    public void EmbeddedXaml_ShowsSearchSummaryPanel()
    {
        var assembly = LoadBuiltExtensionAssembly();
        using var stream = assembly.GetManifestResourceStream("ContextRelay.VSExtension.ToolWindows.ContextRelayWindowContent.xaml");

        Assert.NotNull(stream);

        using var reader = new StreamReader(stream!);
        var xaml = reader.ReadToEnd();

        Assert.Contains("Text=\"{Binding SearchSummaryHeaderText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding SearchSummary}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding HasSearchSummary, Converter={StaticResource BoolToVisConverter}}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ContextRelayHost_NormalizeAssistantReplyForDisplay_StripsSingleWrappingFenceForChat()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var hostType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHost", throwOnError: true);
        var method = hostType!.GetMethod("NormalizeAssistantReplyForDisplay", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var result = Assert.IsType<string>(method!.Invoke(obj: null, parameters: new object[]
        {
            RouteTarget.Chat,
            "convert to json",
            "```json\n{\"ok\":true}\n```"
        }));

        Assert.Equal("{\"ok\":true}", result);
    }

    [Fact]
    public void ContextRelayHost_BuildSearchSummary_IncludesRequestedSourcesAndTopItems()
    {
        var assembly = LoadBuiltExtensionAssembly();
        assembly.GetType("ContextRelay.VSExtension.ToolWindows.ContextRelayLocalizedStrings", throwOnError: true)!
            .GetMethod("SetUiLanguage", BindingFlags.Static | BindingFlags.Public)!
            .Invoke(obj: null, parameters: new object?[] { "en" });
        var hostType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHost", throwOnError: true);
        var method = hostType!.GetMethod("BuildSearchSummary", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var route = new SlashCommandParseResult
        {
            Target = RouteTarget.All,
            Query = "architecture decisions",
            SourceCommandNames = new[] { "/mail", "/onedrive" },
            SearchScope = SearchScope.Scoped
        };
        var items = new[]
        {
            new ContextItem
            {
                Source = ContextSource.Mail,
                Title = "Architecture review",
                Cache = new ContextItemCacheInfo { Hit = true }
            },
            new ContextItem
            {
                Source = ContextSource.OneDrive,
                Title = "Decision log"
            }
        };

        var summary = Assert.IsType<string>(method!.Invoke(obj: null, parameters: new object[] { route, items }));

        Assert.Contains("Latest search query: `architecture decisions`", summary, StringComparison.Ordinal);
        Assert.Contains("Requested sources: Exchange Mail, OneDrive", summary, StringComparison.Ordinal);
        Assert.Contains("- Exchange Mail: 1 item(s) (cached). Top items: Architecture review.", summary, StringComparison.Ordinal);
        Assert.Contains("- OneDrive: 1 item(s). Top items: Decision log.", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void ContextRelayHost_BuildSearchSummary_LocalizesBodyWhenUiLanguageIsJapanese()
    {
        var assembly = LoadBuiltExtensionAssembly();
        assembly.GetType("ContextRelay.VSExtension.ToolWindows.ContextRelayLocalizedStrings", throwOnError: true)!
            .GetMethod("SetUiLanguage", BindingFlags.Static | BindingFlags.Public)!
            .Invoke(obj: null, parameters: new object?[] { "ja" });
        var hostType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHost", throwOnError: true);
        var method = hostType!.GetMethod("BuildSearchSummary", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var route = new SlashCommandParseResult
        {
            Target = RouteTarget.All,
            Query = "設計判断",
            SearchScope = SearchScope.All
        };

        var summary = Assert.IsType<string>(method!.Invoke(obj: null, parameters: new object[] { route, System.Array.Empty<ContextItem>() }));

        Assert.Contains("最新の検索クエリ: `設計判断`", summary, StringComparison.Ordinal);
        Assert.Contains("- いずれのソースからも結果は返されませんでした。", summary, StringComparison.Ordinal);
    }

    private static Assembly LoadBuiltExtensionAssembly()
    {
        var assemblyPath = BuiltExtensionArtifactLocator.ResolveExtensionArtifactPath("ContextRelay.VSExtension.dll");
        return Assembly.LoadFrom(assemblyPath);
    }

    private static void SetState(Type stateType, object state, string propertyName, object value)
    {
        stateType.GetProperty(propertyName)!.SetValue(state, value);
    }
}
