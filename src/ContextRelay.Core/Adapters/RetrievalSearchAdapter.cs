using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.Models;
using ContextRelay.Core.Router;

namespace ContextRelay.Core.Adapters;

public sealed class RetrievalSearchAdapter : IContextSearchAdapter
{
    private readonly GraphHttpClient graphClient;
    private readonly RetrievalDataSource dataSource;

    public RetrievalSearchAdapter(RetrievalDataSource dataSource, GraphHttpClient? graphClient = null)
    {
        this.dataSource = dataSource;
        this.graphClient = graphClient ?? new GraphHttpClient();
    }

    public ContextSource Source => dataSource switch
    {
        RetrievalDataSource.SharePoint => ContextSource.SharePoint,
        RetrievalDataSource.OneDriveBusiness => ContextSource.OneDrive,
        RetrievalDataSource.ExternalItem => ContextSource.Connectors,
        _ => throw new ArgumentOutOfRangeException()
    };

    public Task<IReadOnlyList<ContextItem>> SearchAsync(
        string accessToken,
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        return dataSource switch
        {
            RetrievalDataSource.ExternalItem => SearchExternalItemsAsync(accessToken, query, maxResults, cancellationToken),
            RetrievalDataSource.SharePoint => SearchFilesAsync(accessToken, query, maxResults, ContextSource.SharePoint, cancellationToken),
            RetrievalDataSource.OneDriveBusiness => SearchFilesAsync(accessToken, query, maxResults, ContextSource.OneDrive, cancellationToken),
            _ => Task.FromResult<IReadOnlyList<ContextItem>>(Array.Empty<ContextItem>())
        };
    }

    private async Task<IReadOnlyList<ContextItem>> SearchFilesAsync(
        string accessToken,
        string query,
        int maxResults,
        ContextSource target,
        CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(new
        {
            requests = new[]
            {
                new
                {
                    entityTypes = new[] { "driveItem", "listItem", "list", "site" },
                    query = new { queryString = query },
                    size = Math.Min(Math.Max(maxResults * 3, maxResults), 50)
                }
            }
        });

        using var response = await graphClient
            .SendWithRetryAsync($"{graphClient.BaseUrl}/v1.0/search/query", accessToken, HttpMethod.Post, body, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var data = await graphClient.ReadJsonAsync<SearchResponse>(response, cancellationToken).ConfigureAwait(false);

        var items = new List<ContextItem>();
        foreach (var result in data.Value ?? Array.Empty<SearchResult>())
        {
            foreach (var container in result.HitsContainers ?? Array.Empty<SearchHitsContainer>())
            {
                foreach (var hit in container.Hits ?? Array.Empty<SearchHit>())
                {
                    var resource = hit.Resource;
                    var webUrl = resource?.WebUrl;
                    if (string.IsNullOrWhiteSpace(webUrl))
                    {
                        continue;
                    }

                    var currentResource = resource!;

                    var matchesTarget = target == ContextSource.OneDrive
                        ? RetrievalSearchUtils.IsOneDriveUrl(webUrl!)
                        : !RetrievalSearchUtils.IsOneDriveUrl(webUrl!);

                    if (!matchesTarget)
                    {
                        continue;
                    }

                    var summaryText = RetrievalSearchUtils.StripSearchMarkup(hit.Summary ?? string.Empty);
                    items.Add(new ContextItem
                    {
                        Source = target,
                        Title = string.IsNullOrWhiteSpace(currentResource.Name)
                            ? RetrievalSearchUtils.GetTitleFromUrl(webUrl!) ?? "Untitled"
                            : currentResource.Name!,
                        Snippet = RetrievalSearchUtils.BuildSearchSnippet(hit.Summary, currentResource.Description, webUrl!),
                        Url = webUrl,
                        Timestamp = currentResource.LastModifiedDateTime ?? currentResource.FileSystemInfo?.LastModifiedDateTime ?? currentResource.CreatedDateTime,
                        Cache = new ContextItemCacheInfo { Hit = false },
                        Metadata = new Dictionary<string, string>
                        {
                            ["id"] = currentResource.Id ?? string.Empty,
                            ["driveId"] = currentResource.ParentReference?.DriveId ?? string.Empty,
                            ["siteId"] = currentResource.ParentReference?.SiteId ?? string.Empty,
                            ["path"] = currentResource.ParentReference?.Path ?? string.Empty,
                            ["mimeType"] = currentResource.File?.MimeType ?? string.Empty,
                            ["extracts"] = summaryText
                        }
                    });

                    if (items.Count >= maxResults)
                    {
                        return items;
                    }
                }
            }
        }

        return items;
    }

    private async Task<IReadOnlyList<ContextItem>> SearchExternalItemsAsync(
        string accessToken,
        string query,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var queryString = query.Length > 1500 ? query.Substring(0, 1500) : query;
        var body = JsonSerializer.Serialize(new
        {
            queryString,
            dataSource = "externalItem",
            resourceMetadata = new[] { "title" },
            maximumNumberOfResults = Math.Min(Math.Max(maxResults, 1), 25)
        });

        using var response = await graphClient
            .SendWithRetryAsync($"{graphClient.BaseUrl}/v1.0/copilot/retrieval", accessToken, HttpMethod.Post, body, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var data = await graphClient.ReadJsonAsync<RetrievalResponse>(response, cancellationToken).ConfigureAwait(false);

        return (data.RetrievalHits ?? Array.Empty<RetrievalHit>())
            .Select(hit =>
            {
                var extracts = (hit.Extracts ?? Array.Empty<RetrievalExtract>())
                    .Select(extract => extract.Text ?? string.Empty)
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .ToArray();
                var title = hit.ResourceMetadata?.Title ??
                    hit.ResourceMetadata?.Name ??
                    RetrievalSearchUtils.GetTitleFromUrl(hit.WebUrl ?? string.Empty) ??
                    "Untitled";

                return new ContextItem
                {
                    Source = ContextSource.Connectors,
                    Title = title,
                    Snippet = string.Join(" ", extracts),
                    Url = hit.WebUrl,
                    Cache = new ContextItemCacheInfo { Hit = false },
                    Metadata = new Dictionary<string, string>
                    {
                        ["resourceType"] = hit.ResourceType ?? string.Empty,
                        ["extracts"] = string.Join("\n", extracts)
                    }
                };
            })
            .ToArray();
    }

    private sealed class SearchResponse
    {
        [JsonPropertyName("value")]
        public SearchResult[]? Value { get; set; }
    }

    private sealed class SearchResult
    {
        [JsonPropertyName("hitsContainers")]
        public SearchHitsContainer[]? HitsContainers { get; set; }
    }

    private sealed class SearchHitsContainer
    {
        [JsonPropertyName("hits")]
        public SearchHit[]? Hits { get; set; }
    }

    private sealed class SearchHit
    {
        [JsonPropertyName("summary")]
        public string? Summary { get; set; }

        [JsonPropertyName("resource")]
        public SearchHitResource? Resource { get; set; }
    }

    private sealed class SearchHitResource
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("webUrl")]
        public string? WebUrl { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("createdDateTime")]
        public string? CreatedDateTime { get; set; }

        [JsonPropertyName("lastModifiedDateTime")]
        public string? LastModifiedDateTime { get; set; }

        [JsonPropertyName("file")]
        public SearchHitFile? File { get; set; }

        [JsonPropertyName("fileSystemInfo")]
        public SearchHitFileSystemInfo? FileSystemInfo { get; set; }

        [JsonPropertyName("parentReference")]
        public SearchHitParentReference? ParentReference { get; set; }
    }

    private sealed class SearchHitFile
    {
        [JsonPropertyName("mimeType")]
        public string? MimeType { get; set; }
    }

    private sealed class SearchHitFileSystemInfo
    {
        [JsonPropertyName("createdDateTime")]
        public string? CreatedDateTime { get; set; }

        [JsonPropertyName("lastModifiedDateTime")]
        public string? LastModifiedDateTime { get; set; }
    }

    private sealed class SearchHitParentReference
    {
        [JsonPropertyName("driveId")]
        public string? DriveId { get; set; }

        [JsonPropertyName("siteId")]
        public string? SiteId { get; set; }

        [JsonPropertyName("path")]
        public string? Path { get; set; }
    }

    private sealed class RetrievalResponse
    {
        [JsonPropertyName("retrievalHits")]
        public RetrievalHit[]? RetrievalHits { get; set; }
    }

    private sealed class RetrievalHit
    {
        [JsonPropertyName("webUrl")]
        public string? WebUrl { get; set; }

        [JsonPropertyName("extracts")]
        public RetrievalExtract[]? Extracts { get; set; }

        [JsonPropertyName("resourceMetadata")]
        public RetrievalResourceMetadata? ResourceMetadata { get; set; }

        [JsonPropertyName("resourceType")]
        public string? ResourceType { get; set; }
    }

    private sealed class RetrievalExtract
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    private sealed class RetrievalResourceMetadata
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
