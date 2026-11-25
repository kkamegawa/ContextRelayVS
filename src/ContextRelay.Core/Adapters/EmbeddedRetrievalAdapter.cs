using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.Models;
using ContextRelay.Core.Router;
using ContextRelay.Core.Settings;

namespace ContextRelay.Core.Adapters;

/// <summary>
/// Searches one SharePoint Embedded container type through the Retrieval API (preview, Global cloud only).
/// </summary>
public sealed class EmbeddedRetrievalAdapter
{
    public const string UntitledTitle = "Untitled";

    private readonly RetrievalClient retrievalClient;

    public EmbeddedRetrievalAdapter(RetrievalClient retrievalClient)
    {
        this.retrievalClient = retrievalClient ?? throw new ArgumentNullException(nameof(retrievalClient));
    }

    /// <summary>
    /// Runs the request. The caller has already pre-validated the query and source.
    /// Graph failures surface as <see cref="EmbeddedRetrievalException"/> with a classified kind.
    /// </summary>
    public async Task<IReadOnlyList<ContextItem>> SearchAsync(
        string accessToken,
        string query,
        EmbeddedSourceSettings source,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        IReadOnlyList<RetrievalHitResult> hits;
        try
        {
            hits = await retrievalClient
                .RetrieveAsync(
                    accessToken,
                    new RetrievalRequest
                    {
                        QueryString = query,
                        DataSource = RetrievalClient.SharePointEmbeddedDataSource,
                        ContainerTypeId = source.ContainerTypeId,
                        MaxResults = maxResults
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GraphApiException ex)
        {
            throw new EmbeddedRetrievalException(EmbeddedRetrievalException.Classify(ex.StatusCode), ex.Message, ex);
        }

        return MapHits(hits, source);
    }

    /// <summary>
    /// Orders hits by their highest extract relevance score (descending). Hits without any score go
    /// last and keep their response order, and so do ties.
    /// </summary>
    public static IReadOnlyList<ContextItem> MapHits(IReadOnlyList<RetrievalHitResult> hits, EmbeddedSourceSettings source)
    {
        return hits
            .Select((hit, index) => (Hit: hit, Index: index, Score: hit.MaxRelevanceScore))
            .OrderBy(entry => entry.Score.HasValue ? 0 : 1)
            .ThenByDescending(entry => entry.Score ?? 0d)
            .ThenBy(entry => entry.Index)
            .Select(entry => MapHit(entry.Hit, source))
            .ToArray();
    }

    private static ContextItem MapHit(RetrievalHitResult hit, EmbeddedSourceSettings source)
    {
        // webUrl depends on the owning app's urlTemplate and is opaque, so it is never parsed for a title.
        var title = string.IsNullOrWhiteSpace(hit.Title) ? UntitledTitle : hit.Title!.Trim();
        var extracts = hit.Extracts.Select(extract => extract.Clone()).ToList();
        var metadata = new Dictionary<string, string>
        {
            [EmbeddedMetadataKeys.SourceName] = source.Name,
            [EmbeddedMetadataKeys.ContainerTypeId] = source.ContainerTypeId
        };

        if (!string.IsNullOrWhiteSpace(hit.SensitivityLabelDisplayName))
        {
            metadata[EmbeddedMetadataKeys.SensitivityLabelDisplayName] = hit.SensitivityLabelDisplayName!;
        }

        if (!string.IsNullOrWhiteSpace(hit.SensitivityLabelId))
        {
            metadata[EmbeddedMetadataKeys.SensitivityLabelId] = hit.SensitivityLabelId!;
        }

        return new ContextItem
        {
            Source = ContextSource.SharePointEmbedded,
            Title = title,
            Snippet = extracts.Count > 0 ? extracts[0].Text : string.Empty,
            Url = string.IsNullOrWhiteSpace(hit.WebUrl) ? null : hit.WebUrl,
            Relevance = hit.MaxRelevanceScore,
            Cache = new ContextItemCacheInfo { Hit = false },
            Metadata = metadata,
            Extracts = extracts
        };
    }
}

public enum EmbeddedRetrievalErrorKind
{
    BadRequest,
    Unauthorized,
    Forbidden,
    NotFound,
    Throttled,
    Other
}

public sealed class EmbeddedRetrievalException : Exception
{
    public EmbeddedRetrievalException(EmbeddedRetrievalErrorKind kind, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public EmbeddedRetrievalErrorKind Kind { get; }

    public static EmbeddedRetrievalErrorKind Classify(HttpStatusCode statusCode)
    {
        return (int)statusCode switch
        {
            400 => EmbeddedRetrievalErrorKind.BadRequest,
            401 => EmbeddedRetrievalErrorKind.Unauthorized,
            403 => EmbeddedRetrievalErrorKind.Forbidden,
            404 => EmbeddedRetrievalErrorKind.NotFound,
            429 => EmbeddedRetrievalErrorKind.Throttled,
            _ => EmbeddedRetrievalErrorKind.Other
        };
    }
}
