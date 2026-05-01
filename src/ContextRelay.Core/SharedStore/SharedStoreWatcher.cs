using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace ContextRelay.Core.SharedStore;

public sealed class SharedStoreWatcher : IDisposable
{
    private readonly ConcurrentDictionary<SharedStoreFileKind, string> lastWrittenHashes = new();
    private readonly ConcurrentDictionary<SharedStoreFileKind, Timer> debounceTimers = new();
    private readonly int debounceMilliseconds;
    private readonly FileSystemWatcher watcher;
    private bool disposed;

    public SharedStoreWatcher(string rootDirectory, int debounceMilliseconds = 200)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("Shared store root directory must be provided.", nameof(rootDirectory));
        }

        Directory.CreateDirectory(rootDirectory);
        this.debounceMilliseconds = debounceMilliseconds;
        watcher = new FileSystemWatcher(rootDirectory, "*.json")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
        };

        watcher.Changed += OnFileSystemEvent;
        watcher.Created += OnFileSystemEvent;
        watcher.Renamed += OnRenamed;
    }

    public event EventHandler<SharedStoreChangedEventArgs>? Changed;

    public void Start() => watcher.EnableRaisingEvents = true;

    public void RegisterLastWriteHash(SharedStoreFileKind fileKind, string contentHash)
    {
        lastWrittenHashes[fileKind] = contentHash;
    }

    internal string? TryGetLastWrittenHash(SharedStoreFileKind fileKind)
    {
        return lastWrittenHashes.TryGetValue(fileKind, out var hash) ? hash : null;
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        if (!TryGetFileKind(e.FullPath, out var fileKind))
        {
            return;
        }

        ScheduleNotification(fileKind, e.FullPath);
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (!TryGetFileKind(e.FullPath, out var fileKind))
        {
            return;
        }

        ScheduleNotification(fileKind, e.FullPath);
    }

    private void ScheduleNotification(SharedStoreFileKind fileKind, string path)
    {
        var timer = debounceTimers.AddOrUpdate(
            fileKind,
            _ => new Timer(_ => RaiseChanged(fileKind, path), null, debounceMilliseconds, Timeout.Infinite),
            (_, existing) =>
            {
                existing.Change(debounceMilliseconds, Timeout.Infinite);
                return existing;
            });

        timer.Change(debounceMilliseconds, Timeout.Infinite);
    }

    private void RaiseChanged(SharedStoreFileKind fileKind, string path)
    {
        try
        {
            var hash = FileSystemSharedSessionStore.TryReadContentHash(path);
            if (hash is not null &&
                lastWrittenHashes.TryGetValue(fileKind, out var lastHash) &&
                StringComparer.Ordinal.Equals(hash, lastHash))
            {
                return;
            }

            Changed?.Invoke(this, new SharedStoreChangedEventArgs(fileKind, hash));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool TryGetFileKind(string path, out SharedStoreFileKind fileKind)
    {
        var fileName = Path.GetFileName(path);
        switch (fileName)
        {
            case "snippets.json":
                fileKind = SharedStoreFileKind.Snippets;
                return true;
            case "chat-history.json":
                fileKind = SharedStoreFileKind.ChatHistory;
                return true;
            case "handoff-index.json":
                fileKind = SharedStoreFileKind.HandoffIndex;
                return true;
            default:
                fileKind = default;
                return false;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        watcher.Dispose();
        foreach (var timer in debounceTimers.Values)
        {
            timer.Dispose();
        }
    }
}
