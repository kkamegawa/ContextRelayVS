using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace ContextRelay.Core.Tests.ToolWindows;

/// <summary>
/// Guards against regressing the generated command and tool window contributions.
/// </summary>
public sealed class ExtensionHostConfigurationTests
{
    private const string ToolsMenuGuid = "d309f791-903f-11d0-9efc-00a0c911004f";
    private const int ToolsMenuGroupId = 133;
    private const string ContextRelayMenuGroupName = "ContextRelay.VSExtension.ContextRelayMenuDefinitions.ContextRelayMenuGroup";
    private const string ContextRelayMenuName = "ContextRelay.VSExtension.ContextRelayMenuDefinitions.ContextRelayMenu";

    [Fact]
    public void BuiltExtensionManifest_ContainsWindowAndCommandContributions()
    {
        var extensionAssemblyPath = BuiltExtensionArtifactLocator.ResolveExtensionArtifactPath("ContextRelay.VSExtension.dll");
        var extensionOutputDirectory = Path.GetDirectoryName(extensionAssemblyPath);
        Assert.NotNull(extensionOutputDirectory);

        var extensionManifestPath = Path.Combine(extensionOutputDirectory!, ".vsextension", "extension.json");
        Assert.True(File.Exists(extensionManifestPath), "Built extension manifest was not found.");

        var manifestText = File.ReadAllText(extensionManifestPath);
        using var document = JsonDocument.Parse(manifestText);
        var services = document.RootElement.GetProperty("services").EnumerateArray().ToArray();
        var toolWindows = document.RootElement.GetProperty("toolWindows").EnumerateArray().ToArray();
        var controlPlacements = document.RootElement.GetProperty("controlPlacements").EnumerateArray().ToArray();

        // Display names stay resource tokens so Visual Studio picks the string-resources.json that
        // matches its UI language. Baking English text into the manifest would defeat that, and a
        // baked-in name would simply vanish from the token set, so compare against every resource key.
        var manifestTokens = ContextRelayResourceTokens.FindTokens(manifestText);
        var resourceKeys = ContextRelayResourceTokens.ReadResourceMap(
            Path.Combine(Path.GetDirectoryName(extensionAssemblyPath)!, ".vsextension", "string-resources.json")).Keys;
        Assert.Equal(resourceKeys.OrderBy(key => key, StringComparer.Ordinal), manifestTokens);
        Assert.NotEmpty(services);
        Assert.Contains(
            services,
            service => service.GetProperty("name").GetString() == "ContextRelay.VSExtension.ContextRelayExtensionCommandSet");
        Assert.Contains(
            services,
            service => service.GetProperty("name").GetString() == "ContextRelay.VSExtension.GeneratedToolWindowProvider");
        Assert.All(
            services,
            service => Assert.False(
                service.GetProperty("allowHostingInProcess").GetBoolean(),
                $"Service '{service.GetProperty("name").GetString()}' must remain out-of-process so devenv.exe does not try to load the net8 extension assembly in-process."));
        Assert.Contains(
            toolWindows,
            toolWindow => toolWindow.GetProperty("identifier").GetString() == "ContextRelay.VSExtension.ToolWindows.ContextRelayToolWindowDef");
        Assert.Contains(
            controlPlacements,
            placement => placement.GetProperty("controlName").GetString() == "ContextRelay.VSExtension.Commands.OpenWindowCommand");
        Assert.Contains(
            controlPlacements,
            placement => placement.GetProperty("controlName").GetString() == "ContextRelay.VSExtension.Commands.SetLanguageEnglishCommand");
        Assert.Contains(
            controlPlacements,
            placement => placement.GetProperty("controlName").GetString() == "ContextRelay.VSExtension.Commands.SetLanguageJapaneseCommand");
    }

    [Fact]
    public void BuiltExtensionManifest_RegistersOneContextRelayMenuUnderTools()
    {
        var extensionAssemblyPath = BuiltExtensionArtifactLocator.ResolveExtensionArtifactPath("ContextRelay.VSExtension.dll");
        var extensionOutputDirectory = Path.GetDirectoryName(extensionAssemblyPath);
        Assert.NotNull(extensionOutputDirectory);

        var extensionManifestPath = Path.Combine(extensionOutputDirectory!, ".vsextension", "extension.json");
        Assert.True(File.Exists(extensionManifestPath), "Built extension manifest was not found.");

        using var document = JsonDocument.Parse(File.ReadAllText(extensionManifestPath));
        var placements = document.RootElement.GetProperty("controlPlacements").EnumerateArray().ToArray();
        var toolsPlacements = placements
            .Where(placement => IsToolsMenuPlacement(placement))
            .ToArray();

        var contextRelayToolsPlacements = toolsPlacements
            .Where(placement => (placement.GetProperty("controlName").GetString() ?? string.Empty).StartsWith(
                "ContextRelay.VSExtension.",
                StringComparison.Ordinal))
            .ToArray();

        var contextRelayMenuGroupPlacements = contextRelayToolsPlacements
            .Where(placement => placement.GetProperty("controlName").GetString() == ContextRelayMenuGroupName)
            .ToArray();

        Assert.Single(contextRelayMenuGroupPlacements);
        Assert.DoesNotContain(
            contextRelayToolsPlacements,
            placement => placement.GetProperty("controlName").GetString() == ContextRelayMenuName);
    }

    [Fact]
    public void BuiltExtensionManifest_EveryDisplayTokenIsDefinedInEnglishAndJapaneseResources()
    {
        var extensionAssemblyPath = BuiltExtensionArtifactLocator.ResolveExtensionArtifactPath("ContextRelay.VSExtension.dll");
        var extensionOutputDirectory = Path.GetDirectoryName(extensionAssemblyPath);
        Assert.NotNull(extensionOutputDirectory);

        var rootResource = Path.Combine(extensionOutputDirectory!, ".vsextension", "string-resources.json");
        var japaneseResource = Path.Combine(extensionOutputDirectory!, ".vsextension", "ja", "string-resources.json");
        Assert.True(File.Exists(rootResource), "Default string-resources.json was not deployed.");
        Assert.True(File.Exists(japaneseResource), "Japanese string-resources.json was not deployed.");

        var english = ContextRelayResourceTokens.ReadResourceMap(rootResource);
        var japanese = ContextRelayResourceTokens.ReadResourceMap(japaneseResource);
        var manifestText = File.ReadAllText(Path.Combine(extensionOutputDirectory!, ".vsextension", "extension.json"));

        // The manifest tokens, the default resources, and the Japanese resources must be the same
        // set. A token missing from a language file would surface as raw %...% text or silently fall
        // back, and a key that no token references is a name that was baked into the manifest.
        var manifestTokens = ContextRelayResourceTokens.FindTokens(manifestText);
        Assert.Equal(manifestTokens, english.Keys.OrderBy(key => key, StringComparer.Ordinal));
        Assert.Equal(manifestTokens, japanese.Keys.OrderBy(key => key, StringComparer.Ordinal));
    }

    [Fact]
    public void JapaneseResources_TranslateEveryDisplayNameThatIsNotAProperNoun()
    {
        var extensionAssemblyPath = BuiltExtensionArtifactLocator.ResolveExtensionArtifactPath("ContextRelay.VSExtension.dll");
        var extensionOutputDirectory = Path.GetDirectoryName(extensionAssemblyPath);
        Assert.NotNull(extensionOutputDirectory);

        var english = ContextRelayResourceTokens.ReadResourceMap(Path.Combine(extensionOutputDirectory!, ".vsextension", "string-resources.json"));
        var japanese = ContextRelayResourceTokens.ReadResourceMap(Path.Combine(extensionOutputDirectory!, ".vsextension", "ja", "string-resources.json"));

        // Names that are the same in both languages on purpose: the product name and the language
        // picker entries, which are written in their own language.
        var sameInBothLanguages = new[]
        {
            "ContextRelay.Menu.DisplayName",
            "ContextRelay.Command.OpenWindow.DisplayName",
            "ContextRelay.Command.SetLanguageEnglish.DisplayName",
            "ContextRelay.Command.SetLanguageJapanese.DisplayName",
        };

        foreach (var (key, englishText) in english.Where(pair => !sameInBothLanguages.Contains(pair.Key)))
        {
            Assert.NotEqual(englishText, japanese[key]);
        }

        Assert.Equal("Attach File to Chat", english["ContextRelay.Command.AttachFileToChat.DisplayName"]);
        Assert.Equal("チャットにファイルを添付", japanese["ContextRelay.Command.AttachFileToChat.DisplayName"]);
    }

    private static bool IsToolsMenuPlacement(JsonElement placement)
    {
        if (!placement.TryGetProperty("parent", out var parent) ||
            !parent.TryGetProperty("legacyParentId", out var legacyParentId))
        {
            return false;
        }

        return legacyParentId.TryGetProperty("guid", out var guid) &&
            string.Equals(guid.GetString(), ToolsMenuGuid, StringComparison.OrdinalIgnoreCase) &&
            legacyParentId.TryGetProperty("id", out var id) &&
            id.GetInt32() == ToolsMenuGroupId;
    }
}
