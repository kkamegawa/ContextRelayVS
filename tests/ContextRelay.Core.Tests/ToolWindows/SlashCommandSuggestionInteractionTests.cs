using System;
using System.IO;
using System.Reflection;
using Xunit;

namespace ContextRelay.Core.Tests.ToolWindows;

public sealed class SlashCommandSuggestionInteractionTests
{
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
        Assert.Contains("Style=\"{StaticResource SuggestionPopupListBoxStyle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemContainerStyle=\"{StaticResource SuggestionListBoxItemStyle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FocusManager.IsFocusScope=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FocusManager.FocusedElement=\"{Binding ElementName=QueryTextBox}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("EventName=\"Loaded\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MethodName=\"Focus\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"QueryTextBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding AddFilesCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding AddFilesButtonText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolTip=\"{Binding AddFilesToolTipText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolWindowTextBrushKey", xaml, StringComparison.Ordinal);
        Assert.Contains("Focusable\" Value=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsTabStop\" Value=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("mc:Ignorable=\"i\"", xaml, StringComparison.Ordinal);
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

    private static Assembly LoadBuiltExtensionAssembly()
    {
        var assemblyPath = BuiltExtensionArtifactLocator.ResolveExtensionArtifactPath("ContextRelay.VSExtension.dll");
        return Assembly.LoadFrom(assemblyPath);
    }
}
