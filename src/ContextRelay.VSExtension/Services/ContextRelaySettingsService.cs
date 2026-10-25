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

    public async Task UpdateUiLanguageAsync(string uiLanguage, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = NormalizeUiLanguage(uiLanguage);

        // Update only the language so a concurrent Options save of other fields is not overwritten.
        await Task.Run(
            () => ContextRelaySettingsStore.UpdateSettings(settings => settings.UiLanguage = normalized),
            cancellationToken).ConfigureAwait(false);
    }

    public static string NormalizeUiLanguage(string? uiLanguage)
    {
        return ContextRelaySettingsStore.NormalizeUiLanguage(uiLanguage);
    }

    public string GetSettingsFilePath() => ContextRelaySettingsStore.SettingsFilePath;
}
