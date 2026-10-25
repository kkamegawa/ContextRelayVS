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
