using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.Adapters;
using ContextRelay.Core.Models;
using ContextRelay.Core.Router;
using ContextRelay.Core.Settings;
using Xunit;

namespace ContextRelay.Core.Tests.Adapters;

public sealed class EmbeddedRetrievalAdapterTests
{
    private const string ContainerTypeId = "4a9c1d2e-0b3f-4c5d-8e6f-7a8b9c0d1e2f";

    private static readonly EmbeddedSourceSettings Source = new()
    {
        Name = "Contoso Legal",
        ContainerTypeId = ContainerTypeId,
        Enabled = true
    };

    [Fact]
    public void BuildRequestBody_ForEmbedded_IncludesDataSourceConfiguration()
    {
        var body = RetrievalClient.BuildRequestBody(new RetrievalRequest
        {
            QueryString = "contract renewal",
            DataSource = RetrievalClient.SharePointEmbeddedDataSource,
            ContainerTypeId = ContainerTypeId,
            MaxResults = 10
        });

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal("contract renewal", root.GetProperty("queryString").GetString());
        Assert.Equal("sharePointEmbedded", root.GetProperty("dataSource").GetString());
        Assert.Equal(
            ContainerTypeId,
            root.GetProperty("dataSourceConfiguration").GetProperty("sharePointEmbedded").GetProperty("containerTypeId").GetString());
        Assert.Equal("title", root.GetProperty("resourceMetadata")[0].GetString());
        Assert.Equal(10, root.GetProperty("maximumNumberOfResults").GetInt32());
    }

    [Fact]
    public void BuildRequestBody_ForConnectors_OmitsDataSourceConfiguration()
    {
        var body = RetrievalClient.BuildRequestBody(new RetrievalRequest
        {
            QueryString = "incident",
            DataSource = RetrievalClient.ExternalItemDataSource,
            MaxResults = 5
        });

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal("externalItem", root.GetProperty("dataSource").GetString());
        Assert.False(root.TryGetProperty("dataSourceConfiguration", out _));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(25, 25)]
    [InlineData(100, 25)]
    public void BuildRequestBody_ClampsMaximumNumberOfResults(int requested, int expected)
    {
        var body = RetrievalClient.BuildRequestBody(new RetrievalRequest { QueryString = "q", MaxResults = requested });

        using var document = JsonDocument.Parse(body);
        Assert.Equal(expected, document.RootElement.GetProperty("maximumNumberOfResults").GetInt32());
    }

    [Fact]
    public async Task SearchAsync_PostsEmbeddedRequestAndMapsHits()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new RecordingHttpMessageHandler(CreateResponse(HttpStatusCode.OK, """
            {
              "retrievalHits": [
                {
                  "webUrl": "https://contoso.example/app/doc/1",
                  "resourceType": "driveItem",
                  "extracts": [
                    { "text": "First extract.", "relevanceScore": 0.4 },
                    { "text": "Second extract.", "relevanceScore": 0.9 }
                  ],
                  "resourceMetadata": { "title": "Renewal terms" },
                  "sensitivityLabel": { "sensitivityLabelId": "label-1", "displayName": "Confidential" }
                }
              ]
            }
            """));
        using var httpClient = new HttpClient(handler);
        var adapter = new EmbeddedRetrievalAdapter(new RetrievalClient(new GraphHttpClient(httpClient)));

        var results = await adapter.SearchAsync("token", "renewal", Source, 10, cancellationToken);

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.EndsWith("/v1.0/copilot/retrieval", handler.LastUri!.AbsolutePath, StringComparison.Ordinal);
        using (var document = JsonDocument.Parse(handler.LastRequestBody))
        {
            Assert.Equal("sharePointEmbedded", document.RootElement.GetProperty("dataSource").GetString());
        }

        var item = Assert.Single(results);
        Assert.Equal(ContextSource.SharePointEmbedded, item.Source);
        Assert.Equal("Renewal terms", item.Title);
        Assert.Equal("First extract.", item.Snippet);
        Assert.Equal(0.9, item.Relevance);
        Assert.Equal("https://contoso.example/app/doc/1", item.Url);
        Assert.Equal(new[] { "First extract.", "Second extract." }, item.Extracts.Select(extract => extract.Text));
        Assert.Equal("Contoso Legal", item.Metadata[EmbeddedMetadataKeys.SourceName]);
        Assert.Equal(ContainerTypeId, item.Metadata[EmbeddedMetadataKeys.ContainerTypeId]);
        Assert.Equal("Confidential", item.Metadata[EmbeddedMetadataKeys.SensitivityLabelDisplayName]);
        Assert.Equal("label-1", item.Metadata[EmbeddedMetadataKeys.SensitivityLabelId]);
    }

    [Fact]
    public async Task SearchAsync_EmptyRetrievalHits_ReturnsNoItems()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var httpClient = new HttpClient(new RecordingHttpMessageHandler(CreateResponse(HttpStatusCode.OK, """{ "retrievalHits": [] }""")));
        var adapter = new EmbeddedRetrievalAdapter(new RetrievalClient(new GraphHttpClient(httpClient)));

        var results = await adapter.SearchAsync("token", "nothing", Source, 10, cancellationToken);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_Forbidden_ThrowsClassifiedException()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var httpClient = new HttpClient(new RecordingHttpMessageHandler(
            CreateResponse(HttpStatusCode.Forbidden, """{ "error": { "code": "accessDenied", "message": "Denied." } }""")));
        var adapter = new EmbeddedRetrievalAdapter(new RetrievalClient(new GraphHttpClient(httpClient)));

        var exception = await Assert.ThrowsAsync<EmbeddedRetrievalException>(() =>
            adapter.SearchAsync("token", "renewal", Source, 10, cancellationToken));

        Assert.Equal(EmbeddedRetrievalErrorKind.Forbidden, exception.Kind);
        Assert.IsType<GraphApiException>(exception.InnerException);
    }

    [Theory]
    [InlineData(400, EmbeddedRetrievalErrorKind.BadRequest)]
    [InlineData(401, EmbeddedRetrievalErrorKind.Unauthorized)]
    [InlineData(403, EmbeddedRetrievalErrorKind.Forbidden)]
    [InlineData(404, EmbeddedRetrievalErrorKind.NotFound)]
    [InlineData(429, EmbeddedRetrievalErrorKind.Throttled)]
    [InlineData(500, EmbeddedRetrievalErrorKind.Other)]
    public void Classify_MapsStatusCodes(int statusCode, EmbeddedRetrievalErrorKind expected)
    {
        Assert.Equal(expected, EmbeddedRetrievalException.Classify((HttpStatusCode)statusCode));
    }

    [Fact]
    public void MapHits_SortsByMaxScoreAndPutsUnscoredHitsLastInResponseOrder()
    {
        var hits = new[]
        {
            CreateHit("Unscored A", null),
            CreateHit("Low", 0.2),
            CreateHit("Unscored B", null),
            CreateHit("High", 0.8),
            CreateHit("Tie first", 0.5),
            CreateHit("Tie second", 0.5)
        };

        var items = EmbeddedRetrievalAdapter.MapHits(hits, Source);

        Assert.Equal(
            new[] { "High", "Tie first", "Tie second", "Low", "Unscored A", "Unscored B" },
            items.Select(item => item.Title));
        Assert.Null(items[4].Relevance);
    }

    [Fact]
    public void MapHits_UsesUntitledWhenTitleIsMissingAndNeverParsesWebUrl()
    {
        var hit = new RetrievalHitResult
        {
            WebUrl = "https://contoso.example/app/Quarterly%20Plan.docx",
            Title = "  ",
            Extracts = new List<ContextItemExtract>()
        };

        var item = Assert.Single(EmbeddedRetrievalAdapter.MapHits(new[] { hit }, Source));

        Assert.Equal(EmbeddedRetrievalAdapter.UntitledTitle, item.Title);
        Assert.Equal(string.Empty, item.Snippet);
        Assert.Empty(item.Extracts);
        Assert.False(item.Metadata.ContainsKey(EmbeddedMetadataKeys.SensitivityLabelDisplayName));
    }

    [Fact]
    public void MapHits_ClonesExtractsSoCallersCannotMutateTheSource()
    {
        var hit = CreateHit("Doc", 0.3);

        var item = Assert.Single(EmbeddedRetrievalAdapter.MapHits(new[] { hit }, Source));
        item.Extracts[0].Text = "changed";

        Assert.Equal("Doc extract", hit.Extracts[0].Text);
    }

    private static RetrievalHitResult CreateHit(string title, double? score)
    {
        return new RetrievalHitResult
        {
            Title = title,
            WebUrl = $"https://contoso.example/app/{Uri.EscapeDataString(title)}",
            Extracts = new List<ContextItemExtract>
            {
                new() { Text = $"{title} extract", RelevanceScore = score }
            }
        };
    }

    private static HttpResponseMessage CreateResponse(HttpStatusCode statusCode, string body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage response;

        public RecordingHttpMessageHandler(HttpResponseMessage response)
        {
            this.response = response;
        }

        public HttpMethod? LastMethod { get; private set; }

        public Uri? LastUri { get; private set; }

        public string LastRequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            LastUri = request.RequestUri;
            LastRequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }
}
