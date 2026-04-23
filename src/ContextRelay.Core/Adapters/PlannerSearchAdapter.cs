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

public sealed class PlannerSearchAdapter : IContextSearchAdapter
{
    private readonly GraphHttpClient graphClient;

    public PlannerSearchAdapter(GraphHttpClient? graphClient = null)
    {
        this.graphClient = graphClient ?? new GraphHttpClient();
    }

    public ContextSource Source => ContextSource.Planner;

    public async Task<IReadOnlyList<ContextItem>> SearchAsync(
        string accessToken,
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        var scanLimit = Math.Min(Math.Max(maxResults * 4, maxResults), 40);
        var intent = QueryIntentParser.Parse(query);
        var tasks = await ListAssignedTasksAsync(accessToken, scanLimit, cancellationToken).ConfigureAwait(false);
        var detailsMap = await ResolveTaskDetailsAsync(accessToken, tasks, intent.IncludePlannerMetadata || intent.IncludePlannerComments, cancellationToken).ConfigureAwait(false);
        var planTitles = intent.IncludePlannerMetadata
            ? await ResolvePlanTitlesAsync(accessToken, tasks, cancellationToken).ConfigureAwait(false)
            : new Dictionary<string, string>();
        var bucketNames = intent.IncludePlannerMetadata
            ? await ResolveBucketNamesAsync(accessToken, tasks, cancellationToken).ConfigureAwait(false)
            : new Dictionary<string, string>();

        var candidates = tasks.Select(task =>
        {
            var details = task.Id is not null && detailsMap.TryGetValue(task.Id, out var d) ? d : null;
            var description = details?.Description?.Trim() ?? string.Empty;
            var checklistTitles = ExtractChecklistTitles(details);
            var planTitle = task.PlanId is not null && planTitles.TryGetValue(task.PlanId, out var pt) ? pt : null;
            var bucketName = task.BucketId is not null && bucketNames.TryGetValue(task.BucketId, out var bn) ? bn : null;
            var score = ComputePlannerScore(task, description, checklistTitles, planTitle, bucketName, intent.SearchTerms, intent.IncludePlannerMetadata);
            return new PlannerCandidate(task, description, checklistTitles, planTitle, bucketName, score);
        }).ToList();

        return candidates
            .Where(candidate => intent.SearchTerms.Count == 0 || candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => ParseTimestamp(candidate.Task.DueDateTime ?? candidate.Task.CreatedDateTime))
            .Take(maxResults)
            .Select(candidate => MapPlannerCandidate(candidate, intent.IncludePlannerMetadata, intent.IncludePlannerComments))
            .ToArray();
    }

    private async Task<IReadOnlyList<PlannerTask>> ListAssignedTasksAsync(
        string accessToken,
        int scanLimit,
        CancellationToken cancellationToken)
    {
        var url = $"{graphClient.BaseUrl}/v1.0/me/planner/tasks?$top={scanLimit}";
        using var response = await graphClient
            .SendWithRetryAsync(url, accessToken, HttpMethod.Get, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var data = await graphClient.ReadJsonAsync<PlannerTaskResponse>(response, cancellationToken).ConfigureAwait(false);
        return (data.Value ?? Array.Empty<PlannerTask>())
            .Where(task => !string.IsNullOrWhiteSpace(task.Id))
            .ToArray();
    }

    private async Task<Dictionary<string, PlannerTaskDetails>> ResolveTaskDetailsAsync(
        string accessToken,
        IReadOnlyList<PlannerTask> tasks,
        bool includeChecklist,
        CancellationToken cancellationToken)
    {
        var detailTaskIds = tasks
            .Where(task => task.Id is not null && (task.HasDescription == true || includeChecklist))
            .Select(task => task.Id!)
            .ToArray();

        if (detailTaskIds.Length == 0)
        {
            return new Dictionary<string, PlannerTaskDetails>();
        }

        const int maxConcurrentDetailRequests = 8;
        var map = new Dictionary<string, PlannerTaskDetails>();

        for (var offset = 0; offset < detailTaskIds.Length; offset += maxConcurrentDetailRequests)
        {
            var batchTaskIds = detailTaskIds
                .Skip(offset)
                .Take(maxConcurrentDetailRequests)
                .ToArray();

            var batchTasks = batchTaskIds
                .Select(taskId => FetchTaskDetailsAsync(accessToken, taskId, cancellationToken))
                .ToArray();

            var batchResults = await Task.WhenAll(batchTasks).ConfigureAwait(false);
            for (var index = 0; index < batchTaskIds.Length; index++)
            {
                if (batchResults[index] is not null)
                {
                    map[batchTaskIds[index]] = batchResults[index]!;
                }
            }
        }

        return map;
    }

    private async Task<PlannerTaskDetails?> FetchTaskDetailsAsync(
        string accessToken,
        string taskId,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{graphClient.BaseUrl}/v1.0/planner/tasks/{Uri.EscapeDataString(taskId)}/details";
            using var response = await graphClient
                .SendWithRetryAsync(url, accessToken, HttpMethod.Get, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return await graphClient.ReadJsonAsync<PlannerTaskDetails>(response, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<Dictionary<string, string>> ResolvePlanTitlesAsync(
        string accessToken,
        IReadOnlyList<PlannerTask> tasks,
        CancellationToken cancellationToken)
    {
        const int maxConcurrentPlanFetches = 10;

        var planIds = tasks
            .Select(task => task.PlanId)
            .Where(planId => !string.IsNullOrWhiteSpace(planId))
            .Distinct()
            .ToArray();

        var map = new Dictionary<string, string>();
        for (var offset = 0; offset < planIds.Length; offset += maxConcurrentPlanFetches)
        {
            var batchPlanIds = planIds
                .Skip(offset)
                .Take(maxConcurrentPlanFetches)
                .ToArray();

            var planTasks = batchPlanIds
                .Select(planId => FetchPlanAsync(accessToken, planId!, cancellationToken))
                .ToArray();
            var results = await Task.WhenAll(planTasks).ConfigureAwait(false);

            for (var index = 0; index < batchPlanIds.Length; index++)
            {
                if (!string.IsNullOrWhiteSpace(results[index]))
                {
                    map[batchPlanIds[index]!] = results[index]!;
                }
            }
        }

        return map;
    }

    private async Task<string?> FetchPlanAsync(
        string accessToken,
        string planId,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{graphClient.BaseUrl}/v1.0/planner/plans/{Uri.EscapeDataString(planId)}";
            using var response = await graphClient
                .SendWithRetryAsync(url, accessToken, HttpMethod.Get, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var plan = await graphClient.ReadJsonAsync<PlannerPlan>(response, cancellationToken).ConfigureAwait(false);
            return plan.Title?.Trim();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<Dictionary<string, string>> ResolveBucketNamesAsync(
        string accessToken,
        IReadOnlyList<PlannerTask> tasks,
        CancellationToken cancellationToken)
    {
        const int maxConcurrentBucketFetches = 10;

        var bucketIds = tasks
            .Select(task => task.BucketId)
            .Where(bucketId => !string.IsNullOrWhiteSpace(bucketId))
            .Distinct()
            .ToArray();

        var map = new Dictionary<string, string>();
        for (var offset = 0; offset < bucketIds.Length; offset += maxConcurrentBucketFetches)
        {
            var batchBucketIds = bucketIds
                .Skip(offset)
                .Take(maxConcurrentBucketFetches)
                .ToArray();

            var bucketTasks = batchBucketIds
                .Select(bucketId => FetchBucketAsync(accessToken, bucketId!, cancellationToken))
                .ToArray();
            var results = await Task.WhenAll(bucketTasks).ConfigureAwait(false);

            for (var index = 0; index < batchBucketIds.Length; index++)
            {
                if (!string.IsNullOrWhiteSpace(results[index]))
                {
                    map[batchBucketIds[index]!] = results[index]!;
                }
            }
        }

        return map;
    }

    private async Task<string?> FetchBucketAsync(
        string accessToken,
        string bucketId,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{graphClient.BaseUrl}/v1.0/planner/buckets/{Uri.EscapeDataString(bucketId)}";
            using var response = await graphClient
                .SendWithRetryAsync(url, accessToken, HttpMethod.Get, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var bucket = await graphClient.ReadJsonAsync<PlannerBucket>(response, cancellationToken).ConfigureAwait(false);
            return bucket.Name?.Trim();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static string[] ExtractChecklistTitles(PlannerTaskDetails? details)
    {
        if (details?.Checklist is null || details.Checklist.Count == 0)
        {
            return Array.Empty<string>();
        }

        return details.Checklist.Values
            .Select(item => item.Title?.Trim() ?? string.Empty)
            .Where(title => title.Length > 0)
            .ToArray();
    }

    private static int ComputePlannerScore(
        PlannerTask task,
        string description,
        string[] checklistTitles,
        string? planTitle,
        string? bucketName,
        IReadOnlyList<string> searchTerms,
        bool includeMetadata)
    {
        if (searchTerms.Count == 0)
        {
            return 0;
        }

        var titleScore = QueryIntentParser.ScoreMatches(task.Title ?? string.Empty, searchTerms) * 4;
        var descriptionScore = QueryIntentParser.ScoreMatches(description, searchTerms) * 3;
        var metadataScore = includeMetadata
            ? (QueryIntentParser.ScoreMatches(planTitle ?? string.Empty, searchTerms) +
               QueryIntentParser.ScoreMatches(bucketName ?? string.Empty, searchTerms) +
               QueryIntentParser.ScoreMatches(string.Join(" ", checklistTitles), searchTerms)) * 2
            : 0;

        return titleScore + descriptionScore + metadataScore;
    }

    private static ContextItem MapPlannerCandidate(
        PlannerCandidate candidate,
        bool includeMetadata,
        bool includeComments)
    {
        var task = candidate.Task;
        var snippetParts = new List<string>
        {
            string.IsNullOrWhiteSpace(candidate.Description) ? "No task description available." : candidate.Description
        };

        if (includeMetadata)
        {
            var metadataParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(candidate.PlanTitle))
            {
                metadataParts.Add($"Plan: {candidate.PlanTitle}");
            }

            if (!string.IsNullOrWhiteSpace(candidate.BucketName))
            {
                metadataParts.Add($"Bucket: {candidate.BucketName}");
            }

            if (task.PercentComplete.HasValue)
            {
                metadataParts.Add($"Progress: {task.PercentComplete.Value}%");
            }

            if (!string.IsNullOrWhiteSpace(task.DueDateTime))
            {
                metadataParts.Add($"Due: {FormatDate(task.DueDateTime)}");
            }

            if (metadataParts.Count > 0)
            {
                snippetParts.Add(string.Join(" · ", metadataParts));
            }

            if (candidate.ChecklistTitles.Length > 0)
            {
                snippetParts.Add($"Checklist: {string.Join("; ", candidate.ChecklistTitles)}");
            }
        }

        if (includeComments)
        {
            snippetParts.Add(
                !string.IsNullOrWhiteSpace(task.ConversationThreadId)
                    ? "Comments were requested, but Planner task comments need additional Microsoft 365 group conversation permissions beyond the current Tasks.Read search path."
                    : "Comments were requested, but this task does not expose a conversation thread in the current Planner response.");
        }

        return new ContextItem
        {
            Source = ContextSource.Planner,
            Title = string.IsNullOrWhiteSpace(task.Title) ? "Untitled task" : task.Title!.Trim(),
            Snippet = string.Join("\n", snippetParts.Where(part => part.Length > 0)),
            Timestamp = task.DueDateTime ?? task.CreatedDateTime,
            Cache = new ContextItemCacheInfo { Hit = false },
            Metadata = new Dictionary<string, string>
            {
                ["description"] = candidate.Description,
                ["planId"] = task.PlanId ?? string.Empty,
                ["planTitle"] = candidate.PlanTitle ?? string.Empty,
                ["bucketId"] = task.BucketId ?? string.Empty,
                ["bucketName"] = candidate.BucketName ?? string.Empty,
                ["percentComplete"] = task.PercentComplete?.ToString() ?? string.Empty,
                ["conversationThreadId"] = task.ConversationThreadId ?? string.Empty
            }
        };
    }

    private static string FormatDate(string? isoDate)
    {
        return DateTimeOffset.TryParse(isoDate, out var date)
            ? date.ToString("d")
            : isoDate ?? string.Empty;
    }

    private static DateTimeOffset ParseTimestamp(string? value)
    {
        return DateTimeOffset.TryParse(value, out var result) ? result : DateTimeOffset.MinValue;
    }

    private sealed class PlannerCandidate
    {
        public PlannerCandidate(PlannerTask task, string description, string[] checklistTitles, string? planTitle, string? bucketName, int score)
        {
            Task = task;
            Description = description;
            ChecklistTitles = checklistTitles;
            PlanTitle = planTitle;
            BucketName = bucketName;
            Score = score;
        }

        public PlannerTask Task { get; }
        public string Description { get; }
        public string[] ChecklistTitles { get; }
        public string? PlanTitle { get; }
        public string? BucketName { get; }
        public int Score { get; }
    }

    private sealed class PlannerTaskResponse
    {
        [JsonPropertyName("value")]
        public PlannerTask[]? Value { get; set; }
    }

    private sealed class PlannerTask
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("planId")]
        public string? PlanId { get; set; }

        [JsonPropertyName("bucketId")]
        public string? BucketId { get; set; }

        [JsonPropertyName("conversationThreadId")]
        public string? ConversationThreadId { get; set; }

        [JsonPropertyName("percentComplete")]
        public int? PercentComplete { get; set; }

        [JsonPropertyName("hasDescription")]
        public bool? HasDescription { get; set; }

        [JsonPropertyName("createdDateTime")]
        public string? CreatedDateTime { get; set; }

        [JsonPropertyName("dueDateTime")]
        public string? DueDateTime { get; set; }
    }

    private sealed class PlannerTaskDetails
    {
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("checklist")]
        public Dictionary<string, PlannerChecklistItem>? Checklist { get; set; }
    }

    private sealed class PlannerChecklistItem
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }
    }

    private sealed class PlannerPlan
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }
    }

    private sealed class PlannerBucket
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
