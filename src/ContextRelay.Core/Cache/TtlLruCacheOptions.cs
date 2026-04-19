using System;

namespace ContextRelay.Core.Cache;

public sealed class TtlLruCacheOptions
{
    public TimeSpan TimeToLive { get; set; } = TimeSpan.FromMinutes(5);

    public int MaxEntries { get; set; } = 200;
}
