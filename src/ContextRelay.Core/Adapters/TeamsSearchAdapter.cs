using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.Models;
using ContextRelay.Core.Router;

namespace ContextRelay.Core.Adapters;

public sealed class TeamsSearchAdapter : IContextSearchAdapter
{
    private readonly GraphHttpClient graphClient;

    public TeamsSearchAdapter(GraphHttpClient? graphClient = null)
    {
        this.graphClient = graphClient ?? new GraphHttpClient();
    }

    public ContextSource Source => ContextSource.Teams;

    public async Task<IReadOnlyList<ContextItem>> SearchAsync(
        string accessToken,
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        var body = System.Text.Json.JsonSerializer.Serialize(new
        {
            requests = new[]
            {
                new
                {
                    entityTypes = new[] { "chatMessage" },
                    query = new { queryString = query },
                    size = maxResults,
                    enableTopResults = true
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
                    var resource = hit.Resource ?? new SearchHitResource();
                    var title = BuildTitle(resource);
                    items.Add(new ContextItem
                    {
                        Source = ContextSource.Teams,
                        Title = title,
                        Snippet = hit.Summary ?? string.Empty,
                        Url = resource.WebUrl,
                        Timestamp = resource.CreatedDateTime ?? resource.LastModifiedDateTime,
                        Cache = new ContextItemCacheInfo { Hit = false },
                        Metadata = new Dictionary<string, string>
                        {
                            ["senderName"] = resource.From?.EmailAddress?.Name ?? string.Empty,
                            ["channelName"] = resource.ChannelIdentity?.ChannelDisplayName ?? string.Empty
                        }
                    });
                }
            }
        }

        return items;
    }

    private static string BuildTitle(SearchHitResource resource)
    {
        var senderName = resource.From?.EmailAddress?.Name;
        var channelName = resource.ChannelIdentity?.ChannelDisplayName;
        if (!string.IsNullOrWhiteSpace(senderName) && !string.IsNullOrWhiteSpace(channelName))
        {
            return $"{senderName} — {channelName}";
        }

        if (!string.IsNullOrWhiteSpace(senderName))
        {
            return senderName!;
        }

        if (!string.IsNullOrWhiteSpace(channelName))
        {
            return channelName!;
        }

        return "Teams message";
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
        [JsonPropertyName("webUrl")]
        public string? WebUrl { get; set; }

        [JsonPropertyName("createdDateTime")]
        public string? CreatedDateTime { get; set; }

        [JsonPropertyName("lastModifiedDateTime")]
        public string? LastModifiedDateTime { get; set; }

        [JsonPropertyName("from")]
        public SearchHitSender? From { get; set; }

        [JsonPropertyName("channelIdentity")]
        public ChannelIdentity? ChannelIdentity { get; set; }
    }

    private sealed class SearchHitSender
    {
        [JsonPropertyName("emailAddress")]
        public SearchHitEmailAddress? EmailAddress { get; set; }
    }

    private sealed class SearchHitEmailAddress
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private sealed class ChannelIdentity
    {
        [JsonPropertyName("channelDisplayName")]
        public string? ChannelDisplayName { get; set; }
    }
}
