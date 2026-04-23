using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.Models;
using ContextRelay.Core.Router;

namespace ContextRelay.Core.Adapters;

public sealed class OneNoteSearchAdapter : IContextSearchAdapter
{
    private readonly GraphHttpClient graphClient;

    public OneNoteSearchAdapter(GraphHttpClient? graphClient = null)
    {
        this.graphClient = graphClient ?? new GraphHttpClient();
    }

    public ContextSource Source => ContextSource.OneNote;

    public async Task<IReadOnlyList<ContextItem>> SearchAsync(
        string accessToken,
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        var scanLimit = Math.Min(Math.Max(maxResults * 3, maxResults), 30);
        var intent = QueryIntentParser.Parse(query);
        var pages = await ListRecentPagesAsync(accessToken, scanLimit, cancellationToken).ConfigureAwait(false);
        var previews = await FetchPagePreviewsAsync(accessToken, pages, cancellationToken).ConfigureAwait(false);

        var candidates = new List<PageCandidate>(pages.Count);
        for (var index = 0; index < pages.Count; index++)
        {
            var page = pages[index];
            var previewText = index < previews.Count ? previews[index] : string.Empty;
            var score = ComputePageScore(page, previewText, intent.SearchTerms, intent.IncludeOneNoteHierarchy);
            candidates.Add(new PageCandidate(page, previewText, score));
        }

        return candidates
            .Where(candidate => intent.SearchTerms.Count == 0 || candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => ParseTimestamp(candidate.Page.LastModifiedDateTime ?? candidate.Page.CreatedDateTime))
            .Take(maxResults)
            .Select(candidate => MapCandidate(candidate, intent.IncludeOneNoteHierarchy))
            .ToArray();
    }

    private async Task<IReadOnlyList<OneNotePage>> ListRecentPagesAsync(
        string accessToken,
        int scanLimit,
        CancellationToken cancellationToken)
    {
        var url = $"{graphClient.BaseUrl}/v1.0/me/onenote/pages?$top={scanLimit}&$select=id,title,createdDateTime,lastModifiedDateTime,contentUrl,links&$expand=parentSection($select=id,displayName),parentNotebook($select=id,displayName)";
        using var response = await graphClient
            .SendWithRetryAsync(url, accessToken, HttpMethod.Get, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var data = await graphClient.ReadJsonAsync<OneNotePageResponse>(response, cancellationToken).ConfigureAwait(false);
        return (data.Value ?? Array.Empty<OneNotePage>())
            .Where(page => !string.IsNullOrWhiteSpace(page.Id))
            .ToArray();
    }

    private async Task<IReadOnlyList<string>> FetchPagePreviewsAsync(
        string accessToken,
        IReadOnlyList<OneNotePage> pages,
        CancellationToken cancellationToken)
    {
        var tasks = pages.Select(page => FetchPagePreviewAsync(accessToken, page.Id!, cancellationToken)).ToArray();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }

    private async Task<string> FetchPagePreviewAsync(
        string accessToken,
        string pageId,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{graphClient.BaseUrl}/v1.0/me/onenote/pages/{Uri.EscapeDataString(pageId)}/preview";
            using var response = await graphClient
                .SendWithRetryAsync(url, accessToken, HttpMethod.Get, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var data = await graphClient.ReadJsonAsync<OneNotePagePreview>(response, cancellationToken).ConfigureAwait(false);
            return data.PreviewText?.Trim() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static int ComputePageScore(
        OneNotePage page,
        string previewText,
        IReadOnlyList<string> searchTerms,
        bool includeHierarchy)
    {
        if (searchTerms.Count == 0)
        {
            return 0;
        }

        var titleScore = QueryIntentParser.ScoreMatches(page.Title ?? string.Empty, searchTerms) * 4;
        var previewScore = QueryIntentParser.ScoreMatches(previewText, searchTerms) * 3;
        var hierarchyScore = includeHierarchy
            ? (QueryIntentParser.ScoreMatches(page.ParentSection?.DisplayName ?? string.Empty, searchTerms) +
               QueryIntentParser.ScoreMatches(page.ParentNotebook?.DisplayName ?? string.Empty, searchTerms)) * 2
            : 0;

        return titleScore + previewScore + hierarchyScore;
    }

    private static ContextItem MapCandidate(PageCandidate candidate, bool includeHierarchy)
    {
        var page = candidate.Page;
        var sectionName = page.ParentSection?.DisplayName?.Trim();
        var notebookName = page.ParentNotebook?.DisplayName?.Trim();
        var hierarchyParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(sectionName))
        {
            hierarchyParts.Add(sectionName!);
        }

        if (!string.IsNullOrWhiteSpace(notebookName))
        {
            hierarchyParts.Add(notebookName!);
        }

        var hierarchy = string.Join(" · ", hierarchyParts);
        var body = string.IsNullOrWhiteSpace(candidate.PreviewText)
            ? "No preview text is available for this page yet."
            : candidate.PreviewText;

        var snippet = includeHierarchy && hierarchy.Length > 0
            ? $"{hierarchy}\n{body}"
            : body;

        return new ContextItem
        {
            Source = ContextSource.OneNote,
            Title = string.IsNullOrWhiteSpace(page.Title) ? "Untitled page" : page.Title!.Trim(),
            Snippet = snippet,
            Url = page.Links?.OneNoteWebUrl?.Href,
            Timestamp = page.LastModifiedDateTime ?? page.CreatedDateTime,
            Cache = new ContextItemCacheInfo { Hit = false },
            Metadata = new Dictionary<string, string>
            {
                ["pageId"] = page.Id ?? string.Empty,
                ["contentUrl"] = page.ContentUrl ?? string.Empty,
                ["sectionName"] = sectionName ?? string.Empty,
                ["notebookName"] = notebookName ?? string.Empty,
                ["previewText"] = candidate.PreviewText
            }
        };
    }

    private static DateTimeOffset ParseTimestamp(string? value)
    {
        return DateTimeOffset.TryParse(value, out var result) ? result : DateTimeOffset.MinValue;
    }

    private sealed class PageCandidate
    {
        public PageCandidate(OneNotePage page, string previewText, int score)
        {
            Page = page;
            PreviewText = previewText;
            Score = score;
        }

        public OneNotePage Page { get; }
        public string PreviewText { get; }
        public int Score { get; }
    }

    private sealed class OneNotePageResponse
    {
        [JsonPropertyName("value")]
        public OneNotePage[]? Value { get; set; }
    }

    private sealed class OneNotePage
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("createdDateTime")]
        public string? CreatedDateTime { get; set; }

        [JsonPropertyName("lastModifiedDateTime")]
        public string? LastModifiedDateTime { get; set; }

        [JsonPropertyName("contentUrl")]
        public string? ContentUrl { get; set; }

        [JsonPropertyName("links")]
        public OneNotePageLinks? Links { get; set; }

        [JsonPropertyName("parentSection")]
        public OneNotePageParent? ParentSection { get; set; }

        [JsonPropertyName("parentNotebook")]
        public OneNotePageParent? ParentNotebook { get; set; }
    }

    private sealed class OneNotePageLinks
    {
        [JsonPropertyName("oneNoteWebUrl")]
        public OneNoteWebUrl? OneNoteWebUrl { get; set; }
    }

    private sealed class OneNoteWebUrl
    {
        [JsonPropertyName("href")]
        public string? Href { get; set; }
    }

    private sealed class OneNotePageParent
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }
    }

    private sealed class OneNotePagePreview
    {
        [JsonPropertyName("previewText")]
        public string? PreviewText { get; set; }
    }
}
