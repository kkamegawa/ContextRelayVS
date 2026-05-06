using System;
using System.IO;
using System.Text.Json;

namespace ContextRelay.Core.Settings;

/// <summary>
/// Provides shared JSON-based settings persistence for ContextRelay hosts.
/// </summary>
public static class ContextRelaySettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Gets the path to the shared settings file.
    /// </summary>
    public static string SettingsFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ContextRelay",
        "settings.json");

    /// <summary>
    /// Loads the persisted ContextRelay settings.
    /// </summary>
    /// <returns>The persisted settings, or defaults if the file is absent or invalid.</returns>
    public static ContextRelaySettingsSnapshot LoadSettings()
    {
        if (!File.Exists(SettingsFilePath))
        {
            return new ContextRelaySettingsSnapshot();
        }

        try
        {
            using var stream = File.OpenRead(SettingsFilePath);
            return JsonSerializer.Deserialize<ContextRelaySettingsSnapshot>(stream, JsonOptions)
                   ?? new ContextRelaySettingsSnapshot();
        }
        catch
        {
            return new ContextRelaySettingsSnapshot();
        }
    }

    /// <summary>
    /// Saves the specified settings snapshot.
    /// </summary>
    /// <param name="settings">The settings to persist.</param>
    public static void SaveSettings(ContextRelaySettingsSnapshot settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        var directory = Path.GetDirectoryName(SettingsFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = SettingsFilePath + ".tmp";
        try
        {
            using (var stream = File.Create(tempPath))
            {
                JsonSerializer.Serialize(stream, settings, JsonOptions);
            }

            if (File.Exists(SettingsFilePath))
            {
                // Use atomic replace to avoid losing settings if the process crashes between delete and move.
                File.Replace(tempPath, SettingsFilePath, null);
            }
            else
            {
                File.Move(tempPath, SettingsFilePath);
            }
        }
        catch
        {
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
            }

            throw;
        }
    }

    /// <summary>
    /// Normalizes a UI language identifier to a supported value.
    /// </summary>
    /// <param name="uiLanguage">The input UI language identifier.</param>
    /// <returns>The normalized identifier.</returns>
    public static string NormalizeUiLanguage(string? uiLanguage)
    {
        var normalized = (uiLanguage ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "en" => "en",
            "ja" => "ja",
            "auto" => "auto",
            "en-us" => "en",
            "en-gb" => "en",
            "ja-jp" => "ja",
            _ => "auto",
        };
    }
}