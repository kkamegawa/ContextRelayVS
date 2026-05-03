using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.VSExtension.Options;

namespace ContextRelay.VSExtension.Services;

internal sealed class ContextRelaySettingsService
{
    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ContextRelay",
        "settings.json");

    public async Task<ContextRelaySettingsSnapshot> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsFilePath))
        {
            return new ContextRelaySettingsSnapshot();
        }

        try
        {
            using var stream = File.OpenRead(SettingsFilePath);
            return await JsonSerializer.DeserializeAsync<ContextRelaySettingsSnapshot>(stream, cancellationToken: cancellationToken).ConfigureAwait(false)
                   ?? new ContextRelaySettingsSnapshot();
        }
        catch
        {
            return new ContextRelaySettingsSnapshot();
        }
    }

    public string GetSettingsFilePath() => SettingsFilePath;
}
