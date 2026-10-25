using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.Adapters;
using ContextRelay.Core.Cache;
using ContextRelay.Core.Models;
using ContextRelay.Core.Settings;
using ContextRelay.Core.Utilities;

namespace ContextRelay.Core.Embedded;

/// <summary>
/// The inputs for one validated /embedded request.
/// </summary>
public sealed class EmbeddedSearchRequest
{
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>The MSAL home account identifier (<c>oid.tid</c>) that isolates cache entries per account and tenant.</summary>
    public string AccountKey { get; set; } = string.Empty;

    /// <summary>The cloud and Graph endpoint, so entries never cross endpoints.</summary>
    public string EndpointKey { get; set; } = string.Empty;

    public EmbeddedSourceSettings Source { get; set; } = new();

    public string Query { get; set; } = string.Empty;

    public int MaxResults { get; set; }
}

public sealed class EmbeddedSearchOutcome
{
    internal EmbeddedSearchOutcome(IReadOnlyList<ContextItem> items, long generation, bool fromCache, bool isStale)
    {
        Items = items;
        Generation = generation;
        FromCache = fromCache;
        IsStale = isStale;
    }

    public IReadOnlyList<ContextItem> Items { get; }

    /// <summary>The generation the request started in; compare with <see cref="EmbeddedSearchService.IsCurrent"/> before publishing.</summary>
    public long Generation { get; }

    public bool FromCache { get; }

    /// <summary>True when the results were invalidated while the request was in flight; they must not be shown.</summary>
    public bool IsStale { get; }
}

/// <summary>
/// Runs /embedded with a memory-only cache. The cache is never written to the workspace snapshot.
/// Every invalidation bumps a generation number; responses from an older generation are neither cached nor shown.
/// </summary>
public sealed class EmbeddedSearchService
{
    private readonly EmbeddedRetrievalAdapter adapter;
    private readonly IClock clock;
    private readonly object cacheLock = new();
    private TtlLruCache<string, ContextItem[]> cache;
    private int cacheTtlSeconds;
    private int cacheMaxEntries;
    private long generation;
    private string? lastAccountKey;

    public EmbeddedSearchService(EmbeddedRetrievalAdapter adapter, IClock? clock = null, int cacheTtlSeconds = 300, int cacheMaxEntries = 100)
    {
        this.adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        this.clock = clock ?? SystemClock.Instance;
        cache = CreateCache(cacheTtlSeconds, cacheMaxEntries);
    }

    public long Generation => Interlocked.Read(ref generation);

    public int CachedEntryCount
    {
        get
        {
            lock (cacheLock)
            {
                return cache.Count;
            }
        }
    }

    public bool IsCurrent(long requestGeneration) => Generation == requestGeneration;

    /// <summary>Drops cached results and makes every in-flight request stale.</summary>
    public void Invalidate()
    {
        Interlocked.Increment(ref generation);
        lock (cacheLock)
        {
            cache.Clear();
        }
    }

    /// <summary>
    /// Records the signed-in account. Returns true and invalidates when it differs from the last one,
    /// which covers sign-in, sign-out, and account switches.
    /// </summary>
    public bool ObserveAccount(string? accountKey)
    {
        var normalized = accountKey ?? string.Empty;
        lock (cacheLock)
        {
            if (lastAccountKey is null)
            {
                lastAccountKey = normalized;
                return false;
            }

            if (string.Equals(lastAccountKey, normalized, StringComparison.Ordinal))
            {
                return false;
            }

            lastAccountKey = normalized;
        }

        Invalidate();
        return true;
    }

    /// <summary>Applies the shared cache TTL and size settings, recreating the cache only when they change.</summary>
    public void ConfigureCache(int ttlSeconds, int maxEntries)
    {
        lock (cacheLock)
        {
            if (ttlSeconds == cacheTtlSeconds && maxEntries == cacheMaxEntries)
            {
                return;
            }

            cache = CreateCache(ttlSeconds, maxEntries);
        }
    }

    public static string BuildCacheKey(string accountKey, string endpointKey, string containerTypeId, int maxResults, string query)
    {
        return string.Join(
            "|",
            accountKey ?? string.Empty,
            endpointKey ?? string.Empty,
            (containerTypeId ?? string.Empty).ToLowerInvariant(),
            maxResults.ToString(System.Globalization.CultureInfo.InvariantCulture),
            query ?? string.Empty);
    }

    public async Task<EmbeddedSearchOutcome> SearchAsync(EmbeddedSearchRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var requestGeneration = Generation;
        var key = BuildCacheKey(request.AccountKey, request.EndpointKey, request.Source.ContainerTypeId, request.MaxResults, request.Query);
        lock (cacheLock)
        {
            if (cache.TryGetValue(key, out var cached))
            {
                return new EmbeddedSearchOutcome(CloneItems(cached, cacheHit: true), requestGeneration, fromCache: true, isStale: false);
            }
        }

        var items = await adapter
            .SearchAsync(request.AccessToken, request.Query, request.Source, request.MaxResults, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        lock (cacheLock)
        {
            // Checked under the lock so an invalidation cannot slip between the check and the write.
            if (!IsCurrent(requestGeneration))
            {
                return new EmbeddedSearchOutcome(Array.Empty<ContextItem>(), requestGeneration, fromCache: false, isStale: true);
            }

            cache.Set(key, CloneItems(items, cacheHit: false).ToArray());
        }

        return new EmbeddedSearchOutcome(items, requestGeneration, fromCache: false, isStale: false);
    }

    private TtlLruCache<string, ContextItem[]> CreateCache(int ttlSeconds, int maxEntries)
    {
        cacheTtlSeconds = ttlSeconds;
        cacheMaxEntries = maxEntries;
        return new TtlLruCache<string, ContextItem[]>(
            new TtlLruCacheOptions
            {
                TimeToLive = TimeSpan.FromSeconds(Math.Max(0, ttlSeconds)),
                MaxEntries = Math.Max(1, maxEntries)
            },
            clock);
    }

    private ContextItem[] CloneItems(IEnumerable<ContextItem> items, bool cacheHit)
    {
        return items
            .Select(item => new ContextItem
            {
                Source = item.Source,
                Title = item.Title,
                Snippet = item.Snippet,
                Url = item.Url,
                Timestamp = item.Timestamp,
                Relevance = item.Relevance,
                Cache = new ContextItemCacheInfo
                {
                    Hit = cacheHit,
                    StoredAt = cacheHit ? item.Cache.StoredAt : clock.UtcNow.ToString("O")
                },
                Metadata = new Dictionary<string, string>(item.Metadata),
                Extracts = item.Extracts.Select(extract => extract.Clone()).ToList()
            })
            .ToArray();
    }
}
