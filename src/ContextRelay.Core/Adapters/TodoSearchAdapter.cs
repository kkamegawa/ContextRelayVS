using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.Models;
using ContextRelay.Core.Router;

namespace ContextRelay.Core.Adapters;

public sealed class TodoSearchAdapter : IContextSearchAdapter
{
    private const int MaxConcurrentListRequests = 4;
    private const int MaxListsPerQuery = 8;
    private const int MaxTotalTasksPerQuery = 80;

    private readonly GraphHttpClient graphClient;

    public TodoSearchAdapter(GraphHttpClient? graphClient = null)
    {
        this.graphClient = graphClient ?? new GraphHttpClient();
    }

    public ContextSource Source => ContextSource.Todo;

    public async Task<IReadOnlyList<ContextItem>> SearchAsync(
        string accessToken,
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        var scanLimit = Math.Min(Math.Max(maxResults * 4, maxResults), 40);
        var intent = QueryIntentParser.Parse(query);
        var taskLists = await ListTaskListsAsync(accessToken, cancellationToken).ConfigureAwait(false);
        var taskListsWithTasks = await ListTasksByListAsync(accessToken, taskLists, scanLimit, cancellationToken).ConfigureAwait(false);

        var candidates = taskListsWithTasks
            .SelectMany(pair => pair.Tasks.Select(task =>
            {
                var body = NormalizeTodoBody(task.Body);
                var listName = string.IsNullOrWhiteSpace(pair.List.DisplayName)
                    ? GetFallbackListName(pair.List)
                    : pair.List.DisplayName!.Trim();
                var wellknownListName = pair.List.WellknownListName?.Trim();
                var score = ComputeTodoScore(task, body, listName, intent.IncludePlannerMetadata, intent.SearchTerms);
                return new TodoCandidate(task, body, listName, wellknownListName, score);
            }))
            .ToList();

        return candidates
            .Where(candidate => intent.SearchTerms.Count == 0 || candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => ParseTimestamp(
                candidate.Task.DueDateTime?.DateTime ??
                candidate.Task.LastModifiedDateTime ??
                candidate.Task.CreatedDateTime))
            .Take(maxResults)
            .Select(candidate => MapTodoCandidate(candidate, intent.IncludePlannerMetadata))
            .ToArray();
    }

    private async Task<IReadOnlyList<TodoTaskList>> ListTaskListsAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        var url = $"{graphClient.BaseUrl}/v1.0/me/todo/lists";
        using var response = await graphClient
            .SendWithRetryAsync(url, accessToken, HttpMethod.Get, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var data = await graphClient.ReadJsonAsync<TodoTaskListResponse>(response, cancellationToken).ConfigureAwait(false);
        return (data.Value ?? Array.Empty<TodoTaskList>())
            .Where(list => !string.IsNullOrWhiteSpace(list.Id))
            .ToArray();
    }

    private async Task<IReadOnlyList<TaskListWithTasks>> ListTasksByListAsync(
        string accessToken,
        IReadOnlyList<TodoTaskList> lists,
        int scanLimit,
        CancellationToken cancellationToken)
    {
        var listsToScan = lists.Take(MaxListsPerQuery).ToArray();
        if (listsToScan.Length == 0)
        {
            return Array.Empty<TaskListWithTasks>();
        }

        var perListScanLimit = Math.Max(1, Math.Min(scanLimit, (int)Math.Ceiling((double)MaxTotalTasksPerQuery / listsToScan.Length)));
        var results = new List<TaskListWithTasks>();

        for (var index = 0; index < listsToScan.Length; index += MaxConcurrentListRequests)
        {
            var chunk = listsToScan.Skip(index).Take(MaxConcurrentListRequests).ToArray();
            var chunkTasks = chunk
                .Select(async list => new TaskListWithTasks(list, await ListTasksAsync(accessToken, list.Id!, perListScanLimit, cancellationToken).ConfigureAwait(false)))
                .ToArray();
            var chunkResults = await Task.WhenAll(chunkTasks).ConfigureAwait(false);
            results.AddRange(chunkResults);
        }

        return results;
    }

    private async Task<TodoTask[]> ListTasksAsync(
        string accessToken,
        string listId,
        int scanLimit,
        CancellationToken cancellationToken)
    {
        var url = $"{graphClient.BaseUrl}/v1.0/me/todo/lists/{Uri.EscapeDataString(listId)}/tasks?$top={scanLimit}";
        using var response = await graphClient
            .SendWithRetryAsync(url, accessToken, HttpMethod.Get, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var data = await graphClient.ReadJsonAsync<TodoTaskResponse>(response, cancellationToken).ConfigureAwait(false);
        return (data.Value ?? Array.Empty<TodoTask>())
            .Where(task => !string.IsNullOrWhiteSpace(task.Id))
            .ToArray();
    }

    private static int ComputeTodoScore(
        TodoTask task,
        string body,
        string listName,
        bool includeMetadata,
        IReadOnlyList<string> searchTerms)
    {
        if (searchTerms.Count == 0)
        {
            return 0;
        }

        var titleScore = QueryIntentParser.ScoreMatches(task.Title ?? string.Empty, searchTerms) * 4;
        var bodyScore = QueryIntentParser.ScoreMatches(body, searchTerms) * 3;
        var metadataScore = includeMetadata
            ? (QueryIntentParser.ScoreMatches(listName, searchTerms) +
               QueryIntentParser.ScoreMatches(task.Status ?? string.Empty, searchTerms) +
               QueryIntentParser.ScoreMatches(task.Importance ?? string.Empty, searchTerms) +
               QueryIntentParser.ScoreMatches(string.Join(" ", task.Categories ?? Array.Empty<string>()), searchTerms)) * 2
            : 0;

        return titleScore + bodyScore + metadataScore;
    }

    private static ContextItem MapTodoCandidate(TodoCandidate candidate, bool includeMetadata)
    {
        var task = candidate.Task;
        var snippetParts = new List<string>
        {
            string.IsNullOrWhiteSpace(candidate.Body) ? "No task notes available." : candidate.Body
        };

        if (includeMetadata)
        {
            var metadataParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(candidate.ListName))
            {
                metadataParts.Add($"List: {candidate.ListName}");
            }

            if (!string.IsNullOrWhiteSpace(task.Status))
            {
                metadataParts.Add($"Status: {task.Status}");
            }

            if (!string.IsNullOrWhiteSpace(task.Importance))
            {
                metadataParts.Add($"Importance: {task.Importance}");
            }

            var dueDate = FormatDueDate(task.DueDateTime);
            if (!string.IsNullOrWhiteSpace(dueDate))
            {
                metadataParts.Add($"Due: {dueDate}");
            }

            if (metadataParts.Count > 0)
            {
                snippetParts.Add(string.Join(" · ", metadataParts));
            }

            var categories = task.Categories ?? Array.Empty<string>();
            if (categories.Length > 0)
            {
                snippetParts.Add($"Categories: {string.Join("; ", categories)}");
            }
        }

        var metadata = new Dictionary<string, string>
        {
            ["body"] = candidate.Body,
            ["listName"] = candidate.ListName,
            ["status"] = task.Status ?? string.Empty,
            ["importance"] = task.Importance ?? string.Empty,
            ["categories"] = string.Join(";", task.Categories ?? Array.Empty<string>())
        };

        if (!string.IsNullOrWhiteSpace(candidate.WellknownListName))
        {
            metadata["wellknownListName"] = candidate.WellknownListName!;
        }

        return new ContextItem
        {
            Source = ContextSource.Todo,
            Title = string.IsNullOrWhiteSpace(task.Title) ? "Untitled task" : task.Title!.Trim(),
            Snippet = string.Join("\n", snippetParts),
            Timestamp = task.DueDateTime?.DateTime ?? task.LastModifiedDateTime ?? task.CreatedDateTime,
            Cache = new ContextItemCacheInfo { Hit = false },
            Metadata = metadata
        };
    }

    private static string GetFallbackListName(TodoTaskList list)
    {
        return string.IsNullOrWhiteSpace(list.WellknownListName) ? "Task list" : list.WellknownListName!.Trim();
    }

    private static string NormalizeTodoBody(TodoTaskBody? body)
    {
        var content = body?.Content?.Trim() ?? string.Empty;
        if (content.Length == 0)
        {
            return string.Empty;
        }

        if (string.Equals(body?.ContentType, "html", StringComparison.OrdinalIgnoreCase))
        {
            content = Regex.Replace(content, "<[^>]+>", " ");
            content = System.Net.WebUtility.HtmlDecode(content);
        }

        content = Regex.Replace(content, @"\s+", " ");
        return content.Trim();
    }

    private static string? FormatDueDate(TodoDateTimeTimeZone? value)
    {
        var iso = value?.DateTime?.Trim();
        if (string.IsNullOrWhiteSpace(iso))
        {
            return null;
        }

        return DateTimeOffset.TryParse(iso, out var parsed)
            ? parsed.ToString("d")
            : iso;
    }

    private static DateTimeOffset ParseTimestamp(string? value)
    {
        return DateTimeOffset.TryParse(value, out var result) ? result : DateTimeOffset.MinValue;
    }

    private sealed class TodoCandidate
    {
        public TodoCandidate(TodoTask task, string body, string listName, string? wellknownListName, int score)
        {
            Task = task;
            Body = body;
            ListName = listName;
            WellknownListName = wellknownListName;
            Score = score;
        }

        public TodoTask Task { get; }
        public string Body { get; }
        public string ListName { get; }
        public string? WellknownListName { get; }
        public int Score { get; }
    }

    private sealed class TaskListWithTasks
    {
        public TaskListWithTasks(TodoTaskList list, TodoTask[] tasks)
        {
            List = list;
            Tasks = tasks;
        }

        public TodoTaskList List { get; }
        public TodoTask[] Tasks { get; }
    }

    private sealed class TodoTaskListResponse
    {
        [JsonPropertyName("value")]
        public TodoTaskList[]? Value { get; set; }
    }

    private sealed class TodoTaskList
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("wellknownListName")]
        public string? WellknownListName { get; set; }
    }

    private sealed class TodoTaskResponse
    {
        [JsonPropertyName("value")]
        public TodoTask[]? Value { get; set; }
    }

    private sealed class TodoTask
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("importance")]
        public string? Importance { get; set; }

        [JsonPropertyName("categories")]
        public string[]? Categories { get; set; }

        [JsonPropertyName("body")]
        public TodoTaskBody? Body { get; set; }

        [JsonPropertyName("createdDateTime")]
        public string? CreatedDateTime { get; set; }

        [JsonPropertyName("lastModifiedDateTime")]
        public string? LastModifiedDateTime { get; set; }

        [JsonPropertyName("dueDateTime")]
        public TodoDateTimeTimeZone? DueDateTime { get; set; }
    }

    private sealed class TodoTaskBody
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("contentType")]
        public string? ContentType { get; set; }
    }

    private sealed class TodoDateTimeTimeZone
    {
        [JsonPropertyName("dateTime")]
        public string? DateTime { get; set; }

        [JsonPropertyName("timeZone")]
        public string? TimeZone { get; set; }
    }
}
