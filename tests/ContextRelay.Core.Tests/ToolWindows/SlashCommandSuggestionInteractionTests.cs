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
        suggestionType.GetProperty("Name")!.SetValue(suggestion, "/onedrive");
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
        suggestionType.GetProperty("Name")!.SetValue(suggestion, "/onenote");
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
    }

    private static Assembly LoadBuiltExtensionAssembly()
    {
        var assemblyPath = BuiltExtensionArtifactLocator.ResolveExtensionArtifactPath("ContextRelay.VSExtension.dll");
        return Assembly.LoadFrom(assemblyPath);
    }
}
