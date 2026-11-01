using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace ContextRelay.Core.Settings;

/// <summary>
/// Provides shared JSON-based settings persistence for ContextRelay hosts.
/// </summary>
public static class ContextRelaySettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly TimeSpan UpdateLockTimeout = TimeSpan.FromSeconds(10);

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
        TryLoadSettings(out var settings);
        return settings;
    }

    /// <summary>
    /// Attempts to load the persisted ContextRelay settings, distinguishing a legitimate default snapshot
    /// (the file does not exist yet) from a failed read of a file that does exist — for example because a
    /// concurrent save is still in progress, or the content is corrupt. A caller that must not mistake a
    /// transient read failure for "no change" (such as reacting to a file-change notification) should use
    /// this overload instead of <see cref="LoadSettings"/>, which intentionally cannot make that
    /// distinction.
    /// </summary>
    /// <param name="settings">
    /// The persisted settings on success, or a default snapshot (not to be trusted as the persisted state)
    /// when this method returns <see langword="false"/>.
    /// </param>
    /// <returns><see langword="true"/> if the file was absent or was read and parsed successfully.</returns>
    public static bool TryLoadSettings(out ContextRelaySettingsSnapshot settings)
    {
        return TryLoadSettings(SettingsFilePath, out settings);
    }

    /// <summary>
    /// Reads the latest persisted settings, applies only the caller's changes, and saves the result
    /// atomically. The whole read-modify-write runs under a cross-process named mutex, so a page or
    /// process that updates its own fields never discards fields another writer saved meanwhile.
    /// </summary>
    /// <param name="update">Applies the caller's changes to the latest persisted settings.</param>
    /// <returns>The settings that were saved.</returns>
    /// <exception cref="IOException">The existing settings file could not be read, so nothing was saved.</exception>
    /// <exception cref="TimeoutException">Another writer held the settings lock for too long.</exception>
    public static ContextRelaySettingsSnapshot UpdateSettings(Action<ContextRelaySettingsSnapshot> update)
    {
        return UpdateSettings(SettingsFilePath, update);
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

    internal static bool TryLoadSettings(string path, out ContextRelaySettingsSnapshot settings)
    {
        if (!File.Exists(path))
        {
            settings = new ContextRelaySettingsSnapshot();
            return true;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var deserialized = JsonSerializer.Deserialize<ContextRelaySettingsSnapshot>(stream, JsonOptions);
            if (deserialized is null)
            {
                // The file exists but its content is JSON null (or otherwise deserializes to null) rather
                // than an object — not a legitimate "no settings yet" case, since that is File.Exists being
                // false above. Report failure instead of silently substituting a default snapshot.
                settings = new ContextRelaySettingsSnapshot();
                return false;
            }

            settings = deserialized;
            return true;
        }
        catch
        {
            settings = new ContextRelaySettingsSnapshot();
            return false;
        }
    }

    internal static ContextRelaySettingsSnapshot UpdateSettings(string path, Action<ContextRelaySettingsSnapshot> update)
    {
        if (update is null)
        {
            throw new ArgumentNullException(nameof(update));
        }

        using var mutex = new Mutex(initiallyOwned: false, GetUpdateMutexName(path));
        var acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(UpdateLockTimeout);
            }
            catch (AbandonedMutexException)
            {
                // A writer exited while holding the lock. The file is still whole because saves replace it
                // atomically, and this thread now owns the mutex.
                acquired = true;
            }

            if (!acquired)
            {
                throw new TimeoutException("Timed out waiting for another ContextRelay process to finish saving settings.");
            }

            // Merging onto defaults after a failed read would silently reset every field this caller does not
            // own, so an unreadable file fails the update instead.
            if (!TryLoadSettings(path, out var settings))
            {
                throw new IOException($"The ContextRelay settings file '{path}' could not be read, so the change was not saved. Fix or delete the file and try again.");
            }

            update(settings);
            Save(path, settings);
            return settings;
        }
        finally
        {
            if (acquired)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    private static void Save(string path, ContextRelaySettingsSnapshot settings)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        try
        {
            using (var stream = File.Create(tempPath))
            {
                JsonSerializer.Serialize(stream, settings, JsonOptions);
            }

            if (File.Exists(path))
            {
                // Use atomic replace to avoid losing settings if the process crashes between delete and move.
                ReplaceWithRetry(tempPath, path);
            }
            else
            {
                File.Move(tempPath, path);
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

    private static void ReplaceWithRetry(string sourcePath, string destinationPath)
    {
        // Readers load the file without the update lock, and antivirus or the indexer can open it briefly
        // after a save; either makes File.Replace fail with a transient sharing violation.
        const int maxAttempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Replace(sourcePath, destinationPath, null);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(20 * attempt);
            }
        }
    }

    private static string GetUpdateMutexName(string path)
    {
        // Devenv (the Options pages) and the out-of-process extension host run in the same user session, so a
        // session-local name is enough. Hashing the full path keeps test files from sharing the real lock.
        var normalized = Path.GetFullPath(path).ToUpperInvariant();
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(normalized));
        var builder = new StringBuilder(@"Local\ContextRelay.Settings.");
        for (var index = 0; index < 16; index++)
        {
            builder.Append(hash[index].ToString("x2"));
        }

        return builder.ToString();
    }
}
