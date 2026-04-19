using System;
using System.Linq;
using ContextRelay.Core.Cache;
using ContextRelay.Core.Utilities;
using Xunit;

namespace ContextRelay.Core.Tests.Cache;

public sealed class TtlLruCacheTests
{
    [Fact]
    public void TryGetValue_ReturnsFalseForMissingKey()
    {
        var cache = CreateCache();

        var found = cache.TryGetValue("missing", out var value);

        Assert.False(found);
        Assert.Null(value);
    }

    [Fact]
    public void Set_StoresAndRetrievesValue()
    {
        var cache = CreateCache();
        cache.Set("key1", "value1");

        var found = cache.TryGetValue("key1", out var value);

        Assert.True(found);
        Assert.Equal("value1", value);
    }

    [Fact]
    public void TryGetValue_ReturnsFalseAfterTtlExpiry()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 4, 19, 15, 0, 0, TimeSpan.Zero));
        var cache = CreateCache(clock: clock, ttl: TimeSpan.Zero);
        cache.Set("key1", "value1");

        clock.Advance(TimeSpan.FromMilliseconds(1));

        var found = cache.TryGetValue("key1", out _);

        Assert.False(found);
    }

    [Fact]
    public void Set_EvictsLeastRecentlyUsedEntryWhenCapacityIsExceeded()
    {
        var cache = CreateCache(maxEntries: 3);
        cache.Set("a", "1");
        cache.Set("b", "2");
        cache.Set("c", "3");
        cache.TryGetValue("a", out _);

        cache.Set("d", "4");

        Assert.Equal(3, cache.Count);
        Assert.False(cache.TryGetValue("b", out _));
        Assert.True(cache.TryGetValue("a", out _));
        Assert.True(cache.TryGetValue("c", out _));
        Assert.True(cache.TryGetValue("d", out _));
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var cache = CreateCache();
        cache.Set("k1", "v1");
        cache.Set("k2", "v2");

        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryGetValue("k1", out _));
    }

    [Fact]
    public void IsStale_ReturnsTrueForExpiredOrMissingEntry()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 4, 19, 15, 0, 0, TimeSpan.Zero));
        var cache = CreateCache(clock: clock, ttl: TimeSpan.Zero);
        cache.Set("key", "value");
        clock.Advance(TimeSpan.FromMilliseconds(1));

        Assert.True(cache.IsStale("key"));
        Assert.True(cache.IsStale("nonexistent"));
    }

    [Fact]
    public void Set_OverwritesExistingKeyWithoutGrowingCache()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 4, 19, 15, 0, 0, TimeSpan.Zero));
        var cache = CreateCache(clock: clock);
        cache.Set("key", "original");
        clock.Advance(TimeSpan.FromMinutes(1));

        cache.Set("key", "updated");

        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryGetValue("key", out var value));
        Assert.Equal("updated", value);
        Assert.Equal(clock.UtcNow, cache.GetStoredAt("key"));
    }

    [Fact]
    public void ExportSnapshot_RetainsLruOrderAndSkipsExpiredEntries()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 4, 19, 15, 0, 0, TimeSpan.Zero));
        var cache = CreateCache(clock: clock, ttl: TimeSpan.FromMinutes(5));
        cache.Set("a", "1");
        clock.Advance(TimeSpan.FromMinutes(1));
        cache.Set("b", "2");
        cache.TryGetValue("a", out _);
        clock.Advance(TimeSpan.FromMinutes(10));

        var snapshot = cache.ExportSnapshot();

        Assert.Empty(snapshot);
    }

    [Fact]
    public void Constructor_RestoresSnapshotUpToCapacity()
    {
        var now = new DateTimeOffset(2026, 4, 19, 15, 0, 0, TimeSpan.Zero);
        var cache = new TtlLruCache<string, string>(
            new TtlLruCacheOptions
            {
                TimeToLive = TimeSpan.FromMinutes(5),
                MaxEntries = 2
            },
            new MutableClock(now),
            new[]
            {
                new TtlLruCacheSnapshotEntry<string, string> { Key = "a", Value = "1", StoredAt = now.AddMinutes(-1) },
                new TtlLruCacheSnapshotEntry<string, string> { Key = "b", Value = "2", StoredAt = now.AddMinutes(-2) },
                new TtlLruCacheSnapshotEntry<string, string> { Key = "c", Value = "3", StoredAt = now.AddMinutes(-3) }
            });

        var keys = cache.ExportSnapshot().Select(item => item.Key).ToArray();

        Assert.Equal(new[] { "b", "c" }, keys);
    }

    private static TtlLruCache<string, string> CreateCache(
        MutableClock? clock = null,
        TimeSpan? ttl = null,
        int maxEntries = 200)
    {
        return new TtlLruCache<string, string>(
            new TtlLruCacheOptions
            {
                TimeToLive = ttl ?? TimeSpan.FromMinutes(5),
                MaxEntries = maxEntries
            },
            clock ?? new MutableClock(new DateTimeOffset(2026, 4, 19, 15, 0, 0, TimeSpan.Zero)));
    }

    private sealed class MutableClock : IClock
    {
        public MutableClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; private set; }

        public void Advance(TimeSpan delta)
        {
            UtcNow = UtcNow.Add(delta);
        }
    }
}
