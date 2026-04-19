using System;

namespace ContextRelay.Core.Cache;

public sealed class TtlLruCacheSnapshotEntry<TKey, TValue>
{
    public TKey Key { get; set; } = default!;

    public TValue Value { get; set; } = default!;

    public DateTimeOffset StoredAt { get; set; }
}
