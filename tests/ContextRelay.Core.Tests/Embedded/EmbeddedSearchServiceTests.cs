using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.Adapters;
using ContextRelay.Core.Embedded;
using ContextRelay.Core.Settings;
using ContextRelay.Core.Utilities;
using Xunit;

namespace ContextRelay.Core.Tests.Embedded;

public sealed class EmbeddedSearchServiceTests
{
    private const string ResponseJson = """
        {
          "retrievalHits": [
            {
              "webUrl": "https://contoso.example/app/doc/1",
              "extracts": [ { "text": "Renewal clause.", "relevanceScore": 0.7 } ],
              "resourceMetadata": { "title": "Renewal terms" }
            }
          ]
        }
        """;

    private readonly FakeClock clock = new(new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task SearchAsync_SecondIdenticalRequest_IsServedFromCache()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new CountingHttpMessageHandler();
        var service = CreateService(handler);

        var first = await service.SearchAsync(CreateRequest(), cancellationToken);
        var second = await service.SearchAsync(CreateRequest(), cancellationToken);

        Assert.False(first.FromCache);
        Assert.True(second.FromCache);
        Assert.True(second.Items[0].Cache.Hit);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(1, service.CachedEntryCount);
    }

    [Theory]
    [InlineData("other-account", "global|https://graph.microsoft.com", "4a9c1d2e-0b3f-4c5d-8e6f-7a8b9c0d1e2f", 10, "renewal")]
    [InlineData("account-1", "global|https://graph.example", "4a9c1d2e-0b3f-4c5d-8e6f-7a8b9c0d1e2f", 10, "renewal")]
    [InlineData("account-1", "global|https://graph.microsoft.com", "11111111-2222-3333-4444-555555555555", 10, "renewal")]
    [InlineData("account-1", "global|https://graph.microsoft.com", "4a9c1d2e-0b3f-4c5d-8e6f-7a8b9c0d1e2f", 5, "renewal")]
    [InlineData("account-1", "global|https://graph.microsoft.com", "4a9c1d2e-0b3f-4c5d-8e6f-7a8b9c0d1e2f", 10, "termination")]
    public async Task SearchAsync_DifferentKeyPart_DoesNotShareCacheEntries(
        string accountKey,
        string endpointKey,
        string containerTypeId,
        int maxResults,
        string query)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new CountingHttpMessageHandler();
        var service = CreateService(handler);
        await service.SearchAsync(CreateRequest(), cancellationToken);

        var outcome = await service.SearchAsync(
            new EmbeddedSearchRequest
            {
                AccessToken = "token",
                AccountKey = accountKey,
                EndpointKey = endpointKey,
                Source = new EmbeddedSourceSettings { Name = "Contoso", ContainerTypeId = containerTypeId },
                Query = query,
                MaxResults = maxResults
            },
            cancellationToken);

        Assert.False(outcome.FromCache);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public void BuildCacheKey_NormalizesContainerTypeIdCase()
    {
        Assert.Equal(
            EmbeddedSearchService.BuildCacheKey("a", "e", "ABCDEF00-0000-0000-0000-000000000000", 10, "q"),
            EmbeddedSearchService.BuildCacheKey("a", "e", "abcdef00-0000-0000-0000-000000000000", 10, "q"));
    }

    [Fact]
    public async Task Invalidate_ClearsCacheAndBumpsGeneration()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new CountingHttpMessageHandler();
        var service = CreateService(handler);
        await service.SearchAsync(CreateRequest(), cancellationToken);
        var generation = service.Generation;

        service.Invalidate();
        var outcome = await service.SearchAsync(CreateRequest(), cancellationToken);

        Assert.Equal(generation + 1, service.Generation);
        Assert.False(service.IsCurrent(generation));
        Assert.False(outcome.FromCache);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task SearchAsync_InvalidatedWhileInFlight_ReturnsStaleOutcomeAndDoesNotCache()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new CountingHttpMessageHandler(release.Task);
        var service = CreateService(handler);

        var pending = service.SearchAsync(CreateRequest(), cancellationToken);
        await handler.FirstRequestStarted.WaitAsync(cancellationToken);
        service.Invalidate();
        release.SetResult(true);
        var outcome = await pending;

        Assert.True(outcome.IsStale);
        Assert.Empty(outcome.Items);
        Assert.Equal(0, service.CachedEntryCount);
    }

    [Fact]
    public async Task SearchAsync_CancelledWhileInFlight_ThrowsAndDoesNotCache()
    {
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new CountingHttpMessageHandler(release.Task);
        var service = CreateService(handler);
        using var cancellation = new CancellationTokenSource();

        var pending = service.SearchAsync(CreateRequest(), cancellation.Token);
        await handler.FirstRequestStarted.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        release.SetResult(true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(0, service.CachedEntryCount);
    }

    [Fact]
    public async Task ObserveAccount_FirstCallRecordsAndChangeInvalidates()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = CreateService(new CountingHttpMessageHandler());

        Assert.False(service.ObserveAccount("account-1"));
        await service.SearchAsync(CreateRequest(), cancellationToken);
        Assert.False(service.ObserveAccount("account-1"));
        Assert.Equal(1, service.CachedEntryCount);

        Assert.True(service.ObserveAccount("account-2"));
        Assert.Equal(0, service.CachedEntryCount);
        Assert.True(service.ObserveAccount(null));
    }

    [Fact]
    public async Task SearchAsync_ReturnsCopiesSoCallersCannotMutateTheCache()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = CreateService(new CountingHttpMessageHandler());

        var first = await service.SearchAsync(CreateRequest(), cancellationToken);
        first.Items[0].Title = "mutated";
        first.Items[0].Extracts[0].Text = "mutated";
        var second = await service.SearchAsync(CreateRequest(), cancellationToken);

        Assert.Equal("Renewal terms", second.Items[0].Title);
        Assert.Equal("Renewal clause.", second.Items[0].Extracts[0].Text);
    }

    [Fact]
    public async Task ConfigureCache_AppliesTtl()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new CountingHttpMessageHandler();
        var service = CreateService(handler);
        service.ConfigureCache(ttlSeconds: 60, maxEntries: 10);

        await service.SearchAsync(CreateRequest(), cancellationToken);
        clock.UtcNow = clock.UtcNow.AddSeconds(61);
        var second = await service.SearchAsync(CreateRequest(), cancellationToken);

        Assert.False(second.FromCache);
        Assert.Equal(2, handler.CallCount);
    }

    private EmbeddedSearchService CreateService(CountingHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        return new EmbeddedSearchService(
            new EmbeddedRetrievalAdapter(new RetrievalClient(new GraphHttpClient(httpClient))),
            clock);
    }

    private static EmbeddedSearchRequest CreateRequest()
    {
        return new EmbeddedSearchRequest
        {
            AccessToken = "token",
            AccountKey = "account-1",
            EndpointKey = "global|https://graph.microsoft.com",
            Source = new EmbeddedSourceSettings { Name = "Contoso", ContainerTypeId = "4a9c1d2e-0b3f-4c5d-8e6f-7a8b9c0d1e2f" },
            Query = "renewal",
            MaxResults = 10
        };
    }

    private sealed class CountingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Task? gate;
        private readonly TaskCompletionSource<bool> firstRequestStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int callCount;

        public CountingHttpMessageHandler(Task? gate = null)
        {
            this.gate = gate;
        }

        public int CallCount => Volatile.Read(ref callCount);

        public Task FirstRequestStarted => firstRequestStarted.Task;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref callCount);
            firstRequestStarted.TrySetResult(true);
            if (gate is not null)
            {
                await gate.ConfigureAwait(false);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ResponseJson, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; set; }
    }
}
