using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.VSExtension.Options;

namespace ContextRelay.VSExtension.Services;

internal sealed class ContextRelaySettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

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

    public async Task SaveSettingsAsync(ContextRelaySettingsSnapshot settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(SettingsFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(SettingsFilePath);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateUiLanguageAsync(string uiLanguage, CancellationToken cancellationToken = default)
    {
        var settings = await LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        settings.UiLanguage = uiLanguage;
        await SaveSettingsAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    public string GetSettingsFilePath() => SettingsFilePath;
}
