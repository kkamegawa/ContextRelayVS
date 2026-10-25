using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace ContextRelay.Core.Tests.ToolWindows;

public sealed class ToolWindowLayoutTests
{
    private const string XamlResourceName = "ContextRelay.VSExtension.ToolWindows.ContextRelayWindowContent.xaml";

    [Theory]
    [InlineData("explain the build", 0)]
    [InlineData("/ask summarize the pins", 0)]
    [InlineData("/workiq what changed today", 0)]
    [InlineData("/mail budget", 1)]
    [InlineData("/all release notes", 1)]
    [InlineData("/embedded renewal terms", 1)]
    [InlineData("/mail /teams budget", 1)]
    public void GetTabForQuery_SendsChatToTheChatTabAndSearchesToTheResultsTab(string query, int expectedTab)
    {
        Assert.Equal(expectedTab, InvokeGetTabForQuery(query));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/clear")]
    public void GetTabForQuery_KeepsTheCurrentTabForEmptyInputAndClear(string query)
    {
        Assert.Null(InvokeGetTabForQuery(query));
    }

    [Fact]
    public void EmbeddedXaml_UsesOneColumnOfTabsBoundToTheViewModel()
    {
        var xaml = ReadXaml();

        Assert.Single(Regex.Matches(xaml, "<TabControl "));
        Assert.Contains("SelectedIndex=\"{Binding SelectedTabIndex, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"{Binding ChatTabHeaderText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"{Binding ResultsTabHeaderText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"{Binding PinsTabHeaderText}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ColumnDefinition Width=\"2.2*\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding ShowHelpText, Converter={StaticResource BoolToVisConverter}}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void EmbeddedXaml_ComposerIsMultiLineWithASingleEnterBinding()
    {
        var xaml = ReadXaml();

        Assert.Contains("AcceptsReturn=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=\"120\"", xaml, StringComparison.Ordinal);

        // Shift+Enter must reach the text box's own line-break command, so Enter has exactly one plain binding.
        Assert.Single(Regex.Matches(xaml, "<KeyBinding Key=\"Enter\""));
        Assert.DoesNotContain("Modifiers=\"Shift\"", xaml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://contoso.sharepoint.com/doc", true)]
    [InlineData("HTTPS://contoso.example/a?b=c", true)]
    [InlineData("http://contoso.example/doc", false)]
    [InlineData("mailto:someone@contoso.example", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///C:/secret.txt", false)]
    [InlineData("/relative/path", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void SafeLinks_OpensOnlyHttpsUrlsThatPassTheSafetyCheck(string? candidate, bool expected)
    {
        var type = LoadBuiltExtensionAssembly().GetType("ContextRelay.VSExtension.ToolWindows.SafeLinks", throwOnError: true);
        var method = type!.GetMethod("TryGetOpenableUrl", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var arguments = new object?[] { candidate, null };

        var result = (bool)method!.Invoke(obj: null, arguments)!;

        Assert.Equal(expected, result);
        Assert.Equal(expected, !string.IsNullOrEmpty((string?)arguments[1]));
    }

    [Fact]
    public void EmbeddedXaml_CardsOfferPreviewPinStateAndSafeOpen()
    {
        var xaml = ReadXaml();

        Assert.Contains("<Decorator x:Name=\"PART_ContentHost\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource SelectableTextStyle}\" Text=\"{Binding PreviewText, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(xaml, "Command=\"{Binding TogglePreviewCommand}\"").Count);
        Assert.Equal(2, Regex.Matches(xaml, "Command=\"{Binding CopyPreviewCommand}\"").Count);
        Assert.Contains("Content=\"{Binding PinButtonText}\" Command=\"{Binding PinCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(xaml, "Visibility=\"{Binding CanOpen, Converter={StaticResource BoolToVisConverter}}\"").Count);
        Assert.Contains("Text=\"{Binding SensitivityLabelText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding EmbeddedPreviewBadgeText}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void EmbeddedXaml_ComposerShowsTheEmbeddedSourceChipAndSelector()
    {
        var xaml = ReadXaml();

        Assert.Contains("Visibility=\"{Binding ShowEmbeddedSourceBar, Converter={StaticResource BoolToVisConverter}}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding EmbeddedSourceChipText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding EmbeddedSourceOptions}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DataTrigger Binding=\"{Binding IsHighlighted}\" Value=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding HasEmbeddedInvalidSources, Converter={StaticResource BoolToVisConverter}}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ContainerTypeId}", xaml, StringComparison.Ordinal);
    }

    private static int? InvokeGetTabForQuery(string query)
    {
        var assembly = LoadBuiltExtensionAssembly();
        var viewModelType = assembly.GetType("ContextRelay.VSExtension.ToolWindows.ContextRelayWindowViewModel", throwOnError: true);
        var method = viewModelType!.GetMethod("GetTabForQuery", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (int?)method!.Invoke(obj: null, new object[] { query });
    }

    private static string ReadXaml()
    {
        using var stream = LoadBuiltExtensionAssembly().GetManifestResourceStream(XamlResourceName);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        return reader.ReadToEnd();
    }

    private static Assembly LoadBuiltExtensionAssembly()
    {
        return Assembly.LoadFrom(BuiltExtensionArtifactLocator.ResolveExtensionArtifactPath("ContextRelay.VSExtension.dll"));
    }
}
