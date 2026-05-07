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
    [Fact]
    public void BuiltExtensionManifest_ContainsWindowAndCommandContributions()
    {
        var extensionAssemblyPath = BuiltExtensionArtifactLocator.ResolveExtensionArtifactPath("ContextRelay.VSExtension.dll");
        var extensionOutputDirectory = Path.GetDirectoryName(extensionAssemblyPath);
        Assert.NotNull(extensionOutputDirectory);

        var extensionManifestPath = Path.Combine(extensionOutputDirectory!, ".vsextension", "extension.json");
        Assert.True(File.Exists(extensionManifestPath), "Built extension manifest was not found.");

        using var document = JsonDocument.Parse(File.ReadAllText(extensionManifestPath));
        var services = document.RootElement.GetProperty("services").EnumerateArray().ToArray();
        var toolWindows = document.RootElement.GetProperty("toolWindows").EnumerateArray().ToArray();
        var controlPlacements = document.RootElement.GetProperty("controlPlacements").EnumerateArray().ToArray();

        Assert.NotEmpty(services);
        Assert.Contains(
            services,
            service => service.GetProperty("name").GetString() == "ContextRelay.VSExtension.ContextRelayExtensionCommandSet");
        Assert.Contains(
            services,
            service => service.GetProperty("name").GetString() == "ContextRelay.VSExtension.GeneratedToolWindowProvider");
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
}
