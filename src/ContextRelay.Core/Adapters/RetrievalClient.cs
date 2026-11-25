using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.Models;

namespace ContextRelay.Core.Adapters;

/// <summary>
/// Sends Microsoft 365 Copilot Retrieval API requests and maps <c>retrievalHits</c> to a neutral shape.
/// Shared by Copilot connectors (<c>externalItem</c>) and SharePoint Embedded (<c>sharePointEmbedded</c>).
/// </summary>
public sealed class RetrievalClient
{
    public const string ExternalItemDataSource = "externalItem";
    public const string SharePointEmbeddedDataSource = "sharePointEmbedded";
    public const int MaxQueryLength = 1500;
    public const int MaxResultsLimit = 25;

    private readonly GraphHttpClient graphClient;

    public RetrievalClient(GraphHttpClient? graphClient = null)
    {
        this.graphClient = graphClient ?? new GraphHttpClient();
    }

    public string BuildRequestUrl()
    {
        return $"{graphClient.BaseUrl}/v1.0/copilot/retrieval";
    }

    public static string BuildRequestBody(RetrievalRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var maximumNumberOfResults = Math.Min(Math.Max(request.MaxResults, 1), MaxResultsLimit);
        if (string.IsNullOrWhiteSpace(request.ContainerTypeId))
        {
            return JsonSerializer.Serialize(new
            {
                queryString = request.QueryString,
                dataSource = request.DataSource,
                resourceMetadata = new[] { "title" },
                maximumNumberOfResults
            });
        }

        return JsonSerializer.Serialize(new
        {
            queryString = request.QueryString,
            dataSource = request.DataSource,
            dataSourceConfiguration = new
            {
                sharePointEmbedded = new { containerTypeId = request.ContainerTypeId }
            },
            resourceMetadata = new[] { "title" },
            maximumNumberOfResults
        });
    }

    /// <summary>
    /// Sends the request. Non-success responses throw <see cref="GraphApiException"/> after the
    /// shared 429/503 retry policy in <see cref="GraphHttpClient.SendWithRetryAsync"/>.
    /// </summary>
    public async Task<IReadOnlyList<RetrievalHitResult>> RetrieveAsync(
        string accessToken,
        RetrievalRequest request,
        CancellationToken cancellationToken = default)
    {
        var body = BuildRequestBody(request);
        using var response = await graphClient
            .SendWithRetryAsync(BuildRequestUrl(), accessToken, HttpMethod.Post, body, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var data = await graphClient.ReadJsonAsync<RetrievalResponse>(response, cancellationToken).ConfigureAwait(false);

        return (data.RetrievalHits ?? Array.Empty<RetrievalHit>())
            .Select(MapHit)
            .ToArray();
    }

    private static RetrievalHitResult MapHit(RetrievalHit hit)
    {
        var extracts = (hit.Extracts ?? Array.Empty<RetrievalExtract>())
            .Where(extract => !string.IsNullOrWhiteSpace(extract.Text))
            .Select(extract => new ContextItemExtract
            {
                Text = extract.Text!,
                RelevanceScore = extract.RelevanceScore
            })
            .ToList();

        return new RetrievalHitResult
        {
            WebUrl = hit.WebUrl,
            Title = string.IsNullOrWhiteSpace(hit.ResourceMetadata?.Title) ? null : hit.ResourceMetadata!.Title,
            Name = string.IsNullOrWhiteSpace(hit.ResourceMetadata?.Name) ? null : hit.ResourceMetadata!.Name,
            ResourceType = hit.ResourceType,
            Extracts = extracts,
            SensitivityLabelId = hit.SensitivityLabel?.SensitivityLabelId,
            SensitivityLabelDisplayName = hit.SensitivityLabel?.DisplayName
        };
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

        [JsonPropertyName("sensitivityLabel")]
        public RetrievalSensitivityLabel? SensitivityLabel { get; set; }
    }

    private sealed class RetrievalExtract
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("relevanceScore")]
        public double? RelevanceScore { get; set; }
    }

    private sealed class RetrievalResourceMetadata
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private sealed class RetrievalSensitivityLabel
    {
        [JsonPropertyName("sensitivityLabelId")]
        public string? SensitivityLabelId { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }
    }
}

public sealed class RetrievalRequest
{
    public string QueryString { get; set; } = string.Empty;

    public string DataSource { get; set; } = RetrievalClient.ExternalItemDataSource;

    /// <summary>Set only for SharePoint Embedded; adds <c>dataSourceConfiguration</c>.</summary>
    public string? ContainerTypeId { get; set; }

    public int MaxResults { get; set; } = 10;
}

public sealed class RetrievalHitResult
{
    public string? WebUrl { get; set; }

    public string? Title { get; set; }

    public string? Name { get; set; }

    public string? ResourceType { get; set; }

    public List<ContextItemExtract> Extracts { get; set; } = new();

    public string? SensitivityLabelId { get; set; }

    public string? SensitivityLabelDisplayName { get; set; }

    /// <summary>The highest extract relevance score, or null when no extract carries one.</summary>
    public double? MaxRelevanceScore => Extracts.Select(extract => extract.RelevanceScore).Max();
}
