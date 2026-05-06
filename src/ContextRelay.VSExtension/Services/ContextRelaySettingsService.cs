using System;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.Settings;

namespace ContextRelay.VSExtension.Services;

internal sealed class ContextRelaySettingsService
{
    public async Task<ContextRelaySettingsSnapshot> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Task.Run(LoadSettings, cancellationToken).ConfigureAwait(false);
    }

    public static ContextRelaySettingsSnapshot LoadSettings()
    {
        return ContextRelaySettingsStore.LoadSettings();
    }

    public async Task SaveSettingsAsync(ContextRelaySettingsSnapshot settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Run(() => SaveSettings(settings), cancellationToken).ConfigureAwait(false);
    }

    public static void SaveSettings(ContextRelaySettingsSnapshot settings)
    {
        ContextRelaySettingsStore.SaveSettings(settings);
    }

    public async Task UpdateUiLanguageAsync(string uiLanguage, CancellationToken cancellationToken = default)
    {
        var settings = await LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        settings.UiLanguage = NormalizeUiLanguage(uiLanguage);
        await SaveSettingsAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    public static string NormalizeUiLanguage(string? uiLanguage)
    {
        return ContextRelaySettingsStore.NormalizeUiLanguage(uiLanguage);
    }

    public string GetSettingsFilePath() => ContextRelaySettingsStore.SettingsFilePath;
}
