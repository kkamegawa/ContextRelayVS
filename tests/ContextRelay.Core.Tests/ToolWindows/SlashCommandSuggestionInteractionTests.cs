using System;
using System.IO;
using System.Reflection;
using ContextRelay.Core.Models;
using ContextRelay.Core.Router;
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
        Assert.Contains("x:Name=\"QueryTextBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding AddFilesCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding AddFilesButtonText}\"", xaml, StringComparison.Ordinal);
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
}
