using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ContextRelay.Core.Adapters;

public sealed class WorkIqAdapter : IDisposable
{
    public const string DefaultEndpoint = "https://workiq.svc.cloud.microsoft/a2a/";
    public const string A2aVersion = "1.0";

    private readonly HttpClient httpClient;
    private readonly IWorkIqLogger? logger;
    private readonly string endpoint;
    private readonly bool ownsHttpClient;
    private bool disposed;

    public WorkIqAdapter(
        HttpClient? httpClient = null,
        IWorkIqLogger? logger = null,
        string? endpoint = null,
        bool ownsHttpClient = false)
    {
        this.httpClient = httpClient ?? new HttpClient();
        this.logger = logger;
        this.endpoint = string.IsNullOrWhiteSpace(endpoint) ? DefaultEndpoint : endpoint!.Trim();
        this.ownsHttpClient = httpClient is null || ownsHttpClient;
    }

    public async Task<WorkIqResponse> SendMessageAsync(
        string accessToken,
        string text,
        string? contextId = null,
        int maxRetries = 2,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new ArgumentException("Access token must not be empty.", nameof(accessToken));
        }

        if (disposed)
        {
            throw new ObjectDisposedException(nameof(WorkIqAdapter));
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Message text must not be empty.", nameof(text));
        }

        var body = BuildSendMessageRequest(text, contextId);
        var delay = TimeSpan.FromSeconds(1);

        for (var attempt = 0; ; attempt++)
        {
            using var response = await SendAsync(accessToken, body, cancellationToken).ConfigureAwait(false);
            if ((response.StatusCode == (HttpStatusCode)429 || response.StatusCode == HttpStatusCode.ServiceUnavailable) &&
                attempt < maxRetries)
            {
                var retryDelay = GetRetryDelay(response) ?? delay;
                logger?.LogWorkIq($"! Work IQ throttled {(int)response.StatusCode}, retry {attempt + 1}/{maxRetries} after {retryDelay.TotalMilliseconds:0}ms");
                await Task.Delay(retryDelay > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : retryDelay, cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 30000));
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw BuildHttpException(response);
            }

            var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(responseText))
            {
                throw new InvalidOperationException("Work IQ returned an empty response body.");
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(responseText);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("Work IQ returned an invalid JSON response.", ex);
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.TryGetProperty("error", out var error))
                {
                    throw BuildJsonRpcException(error);
                }

                if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidOperationException("Work IQ response missing result field.");
                }

                var taskState = ExtractTaskState(result);
                if (string.Equals(taskState, "TASK_STATE_FAILED", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Work IQ task failed. The agent could not process the request.");
                }

                var reply = ExtractResponseText(result);
                if (string.IsNullOrWhiteSpace(reply) && !string.IsNullOrWhiteSpace(taskState) &&
                    !string.Equals(taskState, "TASK_STATE_COMPLETED", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Work IQ task did not complete (state: {taskState}).");
                }

                var nextContextId = ExtractContextId(result);
                var taskId = ExtractTaskId(result);
                logger?.LogWorkIq(BuildResponseMetadataLog(taskState, taskId, nextContextId));
                return new WorkIqResponse
                {
                    Text = reply,
                    ContextId = nextContextId,
                    TaskId = taskId,
                    State = taskState
                };
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }

    public static string BuildSendMessageRequest(string text, string? contextId = null)
    {
        var location = ResolveLocationMetadata();
        var message = new Dictionary<string, object?>
        {
            ["role"] = "ROLE_USER",
            ["messageId"] = Guid.NewGuid().ToString(),
            ["parts"] = new[] { new Dictionary<string, string> { ["text"] = text } },
            ["metadata"] = new Dictionary<string, object?>
            {
                ["Location"] = new Dictionary<string, object?>
                {
                    ["timeZoneOffset"] = location.TimeZoneOffsetMinutes,
                    ["timeZone"] = location.TimeZone
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(contextId))
        {
            message["contextId"] = contextId;
        }

        var envelope = new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = Guid.NewGuid().ToString(),
            ["method"] = "SendMessage",
            ["params"] = new Dictionary<string, object?>
            {
                ["message"] = message
            }
        };

        return JsonSerializer.Serialize(envelope);
    }

    private async Task<HttpResponseMessage> SendAsync(string accessToken, string body, CancellationToken cancellationToken)
    {
        logger?.LogWorkIq($"-> POST {endpoint}");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("A2A-Version", A2aVersion);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        logger?.LogWorkIq($"<- {(int)response.StatusCode} {response.ReasonPhrase} {endpoint}");
        return response;
    }

    private static WorkIqLocationMetadata ResolveLocationMetadata()
    {
        try
        {
            return new WorkIqLocationMetadata
            {
                TimeZone = string.IsNullOrWhiteSpace(TimeZoneInfo.Local.Id) ? "UTC" : TimeZoneInfo.Local.Id,
                TimeZoneOffsetMinutes = (int)TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.Now).TotalMinutes
            };
        }
        catch
        {
            return new WorkIqLocationMetadata
            {
                TimeZone = "UTC",
                TimeZoneOffsetMinutes = 0
            };
        }
    }

    private static Exception BuildHttpException(HttpResponseMessage response)
    {
        var requestId = GetRequestId(response);
        var requestIdSuffix = string.IsNullOrWhiteSpace(requestId) ? string.Empty : $" (Request ID: {requestId})";
        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new InvalidOperationException("Unauthorized: Work IQ session expired, please re-authenticate."),
            HttpStatusCode.Forbidden => new InvalidOperationException(
                "Forbidden (403): Missing WorkIQAgent.Ask permission or Microsoft 365 Copilot license. Ensure admin consent has been granted and the user has a Copilot license."),
            _ => new InvalidOperationException($"Work IQ API error {(int)response.StatusCode}{requestIdSuffix}")
        };
    }

    private static string? GetRequestId(HttpResponseMessage response)
    {
        foreach (var headerName in new[] { "x-ms-request-id", "request-id", "x-request-id", "traceparent" })
        {
            if (response.Headers.TryGetValues(headerName, out var values))
            {
                return values.FirstOrDefault();
            }
        }

        return null;
    }

    private static Exception BuildJsonRpcException(JsonElement error)
    {
        var code = error.TryGetProperty("code", out var codeElement) ? codeElement.ToString() : "unknown";
        var message = error.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String
            ? messageElement.GetString()
            : error.ToString();
        return new InvalidOperationException($"Work IQ JSON-RPC error ({code}): {message}");
    }

    private static TimeSpan? GetRetryDelay(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is TimeSpan delta)
        {
            return delta;
        }

        if (response.Headers.RetryAfter?.Date is DateTimeOffset date)
        {
            var remaining = date - DateTimeOffset.UtcNow;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        return null;
    }

    private static string ExtractResponseText(JsonElement result)
    {
        var candidates = new List<string>();
        if (result.TryGetProperty("task", out var task) && task.ValueKind == JsonValueKind.Object)
        {
            var statusMessage = ExtractTaskStatusMessageText(task);
            if (!string.IsNullOrWhiteSpace(statusMessage))
            {
                candidates.Add(statusMessage);
            }

            if (task.TryGetProperty("artifacts", out var artifacts) && artifacts.ValueKind == JsonValueKind.Array)
            {
                var artifactTexts = new List<string>();
                foreach (var artifact in artifacts.EnumerateArray())
                {
                    var text = ExtractPartsText(artifact);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        artifactTexts.Add(text);
                    }
                }

                if (artifactTexts.Count > 0)
                {
                    candidates.Add(string.Join("\n\n", artifactTexts));
                }
            }
        }

        if (result.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object)
        {
            var text = ExtractPartsText(message);
            if (!string.IsNullOrWhiteSpace(text))
            {
                candidates.Add(text);
            }
        }

        return SelectBestResponseText(candidates);
    }

    private static string ExtractTaskStatusMessageText(JsonElement task)
    {
        if (!task.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.Object ||
            !status.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        return ExtractPartsText(message);
    }

    private static string ExtractPartsText(JsonElement container)
    {
        if (!container.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                builder.Append(text.GetString());
            }
        }

        return builder.ToString();
    }

    private static string SelectBestResponseText(IEnumerable<string> candidates)
    {
        return candidates
            .Select(candidate => candidate?.Trim() ?? string.Empty)
            .Where(candidate => candidate.Length > 0)
            .OrderByDescending(ScoreResponseText)
            .FirstOrDefault() ?? string.Empty;
    }

    private static int ScoreResponseText(string text)
    {
        var compact = new string(text.Where(character => !char.IsWhiteSpace(character)).ToArray());
        if (compact.Length == 0)
        {
            return 0;
        }

        var score = text.Length;
        if (compact.All(character => character is '?' or '？' or '!' or '！' or '.' or '。'))
        {
            score -= 10000;
        }

        if (text.IndexOf('\n') >= 0)
        {
            score += 100;
        }

        if (text.Contains("](", StringComparison.Ordinal) || text.Contains("http", StringComparison.OrdinalIgnoreCase))
        {
            score += 250;
        }

        return score;
    }

    private static string? ExtractContextId(JsonElement result)
    {
        foreach (var propertyName in new[] { "task", "message" })
        {
            if (result.TryGetProperty(propertyName, out var child) &&
                child.ValueKind == JsonValueKind.Object &&
                child.TryGetProperty("contextId", out var contextId) &&
                contextId.ValueKind == JsonValueKind.String)
            {
                return contextId.GetString();
            }
        }

        if (result.TryGetProperty("contextId", out var directContextId) && directContextId.ValueKind == JsonValueKind.String)
        {
            return directContextId.GetString();
        }

        return null;
    }

    private static string? ExtractTaskState(JsonElement result)
    {
        if (!result.TryGetProperty("task", out var task) || task.ValueKind != JsonValueKind.Object ||
            !task.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.Object ||
            !status.TryGetProperty("state", out var state) || state.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return state.GetString();
    }

    private static string? ExtractTaskId(JsonElement result)
    {
        if (!result.TryGetProperty("task", out var task) || task.ValueKind != JsonValueKind.Object ||
            !task.TryGetProperty("id", out var taskId) || taskId.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return taskId.GetString();
    }

    private static string BuildResponseMetadataLog(string? taskState, string? taskId, string? contextId)
    {
        var parts = new List<string> { "-> Work IQ response:" };
        if (!string.IsNullOrWhiteSpace(taskState))
        {
            parts.Add($"state={taskState}");
        }

        if (!string.IsNullOrWhiteSpace(taskId))
        {
            parts.Add($"taskId={taskId}");
        }

        if (!string.IsNullOrWhiteSpace(contextId))
        {
            parts.Add($"contextId={contextId}");
        }

        return string.Join(" ", parts);
    }

    private sealed class WorkIqLocationMetadata
    {
        public string TimeZone { get; set; } = "UTC";

        public int TimeZoneOffsetMinutes { get; set; }
    }
}

public sealed class WorkIqResponse
{
    public string Text { get; set; } = string.Empty;

    public string? ContextId { get; set; }

    public string? TaskId { get; set; }

    public string? State { get; set; }
}
