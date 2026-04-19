using System;
using System.Collections.Generic;
using System.Linq;
using ContextRelay.Core.Utilities;

namespace ContextRelay.Core.Cache;

public sealed class TtlLruCache<TKey, TValue> where TKey : notnull
{
    private readonly IClock clock;
    private readonly Dictionary<TKey, CacheEntry> entries;
    private readonly LinkedList<TKey> accessOrder = new();
    private readonly TimeSpan timeToLive;
    private readonly int maxEntries;

    public TtlLruCache(
        TtlLruCacheOptions? options = null,
        IClock? clock = null,
        IEnumerable<TtlLruCacheSnapshotEntry<TKey, TValue>>? snapshot = null,
        IEqualityComparer<TKey>? comparer = null)
    {
        options ??= new TtlLruCacheOptions();
        if (options.TimeToLive < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Cache TTL must not be negative.");
        }

        if (options.MaxEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Cache max entries must be greater than zero.");
        }

        this.clock = clock ?? SystemClock.Instance;
        timeToLive = options.TimeToLive;
        maxEntries = options.MaxEntries;
        entries = new Dictionary<TKey, CacheEntry>(comparer ?? EqualityComparer<TKey>.Default);

        if (snapshot is not null)
        {
            RestoreSnapshot(snapshot);
        }
    }

    public int Count => entries.Count;

    public void Set(TKey key, TValue value)
    {
        if (entries.TryGetValue(key, out var existing))
        {
            existing.Value = value;
            existing.StoredAt = clock.UtcNow;
            MoveToMostRecent(existing.Node);
            return;
        }

        EvictExpiredEntries();
        if (entries.Count >= maxEntries)
        {
            EvictLeastRecentlyUsed();
        }

        var node = accessOrder.AddLast(key);
        entries[key] = new CacheEntry(value, clock.UtcNow, node);
    }

    public bool TryGetValue(TKey key, out TValue value)
    {
        if (!entries.TryGetValue(key, out var entry))
        {
            value = default!;
            return false;
        }

        if (IsExpired(entry.StoredAt))
        {
            Remove(key, entry);
            value = default!;
            return false;
        }

        MoveToMostRecent(entry.Node);
        value = entry.Value;
        return true;
    }

    public DateTimeOffset? GetStoredAt(TKey key)
    {
        return entries.TryGetValue(key, out var entry) ? entry.StoredAt : null;
    }

    public bool IsStale(TKey key)
    {
        return !entries.TryGetValue(key, out var entry) || IsExpired(entry.StoredAt);
    }

    public void Clear()
    {
        entries.Clear();
        accessOrder.Clear();
    }

    public IReadOnlyList<TtlLruCacheSnapshotEntry<TKey, TValue>> ExportSnapshot()
    {
        EvictExpiredEntries();

        return accessOrder
            .Select(key => new TtlLruCacheSnapshotEntry<TKey, TValue>
            {
                Key = key,
                Value = entries[key].Value,
                StoredAt = entries[key].StoredAt
            })
            .ToArray();
    }

    private void RestoreSnapshot(IEnumerable<TtlLruCacheSnapshotEntry<TKey, TValue>> snapshot)
    {
        foreach (var item in snapshot)
        {
            if (item is null || IsExpired(item.StoredAt))
            {
                continue;
            }

            if (entries.ContainsKey(item.Key))
            {
                continue;
            }

            if (entries.Count >= maxEntries)
            {
                EvictLeastRecentlyUsed();
            }

            var node = accessOrder.AddLast(item.Key);
            entries[item.Key] = new CacheEntry(item.Value, item.StoredAt, node);
        }
    }

    private void EvictExpiredEntries()
    {
        var expiredKeys = entries
            .Where(pair => IsExpired(pair.Value.StoredAt))
            .Select(pair => pair.Key)
            .ToArray();

        foreach (var key in expiredKeys)
        {
            Remove(key, entries[key]);
        }
    }

    private void EvictLeastRecentlyUsed()
    {
        if (accessOrder.First is null)
        {
            return;
        }

        var key = accessOrder.First.Value;
        Remove(key, entries[key]);
    }

    private void Remove(TKey key, CacheEntry entry)
    {
        accessOrder.Remove(entry.Node);
        entries.Remove(key);
    }

    private void MoveToMostRecent(LinkedListNode<TKey> node)
    {
        if (node.List is null || ReferenceEquals(accessOrder.Last, node))
        {
            return;
        }

        accessOrder.Remove(node);
        accessOrder.AddLast(node);
    }

    private bool IsExpired(DateTimeOffset storedAt)
    {
        return clock.UtcNow - storedAt > timeToLive;
    }

    private sealed class CacheEntry
    {
        public CacheEntry(TValue value, DateTimeOffset storedAt, LinkedListNode<TKey> node)
        {
            Value = value;
            StoredAt = storedAt;
            Node = node;
        }

        public TValue Value { get; set; }

        public DateTimeOffset StoredAt { get; set; }

        public LinkedListNode<TKey> Node { get; }
    }
}
