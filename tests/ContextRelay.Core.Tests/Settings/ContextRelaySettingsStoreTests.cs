using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ContextRelay.Core.Settings;
using Xunit;

namespace ContextRelay.Core.Tests.Settings;

public sealed class ContextRelaySettingsStoreTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(Path.GetTempPath(), "ContextRelayVS.SettingsTests", Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(tempDirectory, "settings.json");

    [Fact]
    public void UpdateSettings_KeepsFieldsOwnedByAnotherWriter()
    {
        ContextRelaySettingsStore.UpdateSettings(SettingsPath, settings => settings.MaxResults = 17);
        ContextRelaySettingsStore.UpdateSettings(SettingsPath, settings =>
        {
            settings.EmbeddedEnabled = true;
            settings.EmbeddedSources = new List<EmbeddedSourceSettings>
            {
                new() { Name = "Contoso Legal", ContainerTypeId = "4a9c1d2e-0b3f-4c5d-8e6f-7a8b9c0d1e2f" }
            };
        });

        Assert.True(ContextRelaySettingsStore.TryLoadSettings(SettingsPath, out var loaded));
        Assert.Equal(17, loaded.MaxResults);
        Assert.True(loaded.EmbeddedEnabled);
        Assert.Equal("Contoso Legal", Assert.Single(loaded.EmbeddedSources).Name);
    }

    [Fact]
    public async Task UpdateSettings_ConcurrentWritersOfDifferentFields_LoseNoUpdates()
    {
        ContextRelaySettingsStore.UpdateSettings(SettingsPath, _ => { });
        const int iterations = 25;

        // Each writer appends to its own field; a lost update would drop entries from the other list.
        var general = Task.Run(() =>
        {
            for (var index = 0; index < iterations; index++)
            {
                var value = index;
                ContextRelaySettingsStore.UpdateSettings(SettingsPath, settings => settings.MaxResults = value + 1);
            }
        });
        var embedded = Task.Run(() =>
        {
            for (var index = 0; index < iterations; index++)
            {
                var value = index;
                ContextRelaySettingsStore.UpdateSettings(SettingsPath, settings =>
                    settings.EmbeddedSources.Add(new EmbeddedSourceSettings
                    {
                        Name = $"Source {value}",
                        ContainerTypeId = Guid.NewGuid().ToString("D")
                    }));
            }
        });
        await Task.WhenAll(general, embedded);

        Assert.True(ContextRelaySettingsStore.TryLoadSettings(SettingsPath, out var loaded));
        Assert.Equal(iterations, loaded.MaxResults);
        Assert.Equal(
            Enumerable.Range(0, iterations).Select(index => $"Source {index}"),
            loaded.EmbeddedSources.Select(source => source.Name));
    }

    [Fact]
    public void UpdateSettings_WhenFileIsUnreadable_ThrowsAndLeavesTheFileUnchanged()
    {
        Directory.CreateDirectory(tempDirectory);
        File.WriteAllText(SettingsPath, "{ not json");

        Assert.Throws<IOException>(() => ContextRelaySettingsStore.UpdateSettings(SettingsPath, settings => settings.MaxResults = 3));
        Assert.Equal("{ not json", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void TryLoadSettings_MissingFile_ReturnsDefaultsWithEmbeddedDisabled()
    {
        Assert.True(ContextRelaySettingsStore.TryLoadSettings(SettingsPath, out var settings));
        Assert.False(settings.EmbeddedEnabled);
        Assert.Empty(settings.EmbeddedSources);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
