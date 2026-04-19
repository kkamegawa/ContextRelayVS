using System;
using System.IO;

namespace ContextRelay.Core.SharedStore;

public sealed class SharedStoreOptions
{
    public const int CurrentSchemaVersion = 1;

    public string RootDirectory { get; set; } = string.Empty;

    public string ProducerId { get; set; } = "vs";

    public string ProducerVersion { get; set; } = "0.1.0";

    public int ChatHistoryRetentionCount { get; set; } = 200;

    public TimeSpan TombstoneRetention { get; set; } = TimeSpan.FromDays(7);

    public int LockRetryCount { get; set; } = 10;

    public TimeSpan LockRetryDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    public int WatcherDebounceMilliseconds { get; set; } = 200;

    public static SharedStoreOptions CreateDefault(string producerId, string producerVersion)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new SharedStoreOptions
        {
            RootDirectory = Path.Combine(localAppData, "ContextRelay", "shared"),
            ProducerId = producerId,
            ProducerVersion = producerVersion
        };
    }
}
