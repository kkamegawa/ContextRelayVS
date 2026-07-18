using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ContextRelay.Core.Adapters;

public sealed class CopilotChatAdapter : ICopilotChatAdapter
{
    private const string DefaultIanaTimeZone = "Etc/UTC";
    private const int MaxContinuationRounds = 5;
    public const string ContinuationPrompt = "Continue exactly from where your previous message stopped. Do not repeat earlier content.";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly GraphHttpClient graphClient;

    public CopilotChatAdapter(GraphHttpClient? graphClient = null)
    {
        this.graphClient = graphClient ?? new GraphHttpClient();
    }

    public CopilotChatResponseDiagnostics LastResponseDiagnostics { get; private set; } = CopilotChatResponseDiagnostics.Empty;

    public void SetLastResponseDiagnostics(CopilotChatResponseDiagnostics diagnostics)
    {
        if (diagnostics is null)
        {
            throw new ArgumentNullException(nameof(diagnostics));
        }

        LastResponseDiagnostics = diagnostics;
        graphClient.LogDiagnostic(BuildResponseDiagnosticsLog(LastResponseDiagnostics));
    }

    public async Task<string> AskAsync(string accessToken, string prompt, CancellationToken cancellationToken = default)
    {
        var conversationId = await CreateConversationAsync(accessToken, cancellationToken).ConfigureAwait(false);
        var reply = await SendMessageAsync(accessToken, conversationId, prompt, options: null, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(reply))
        {
            throw new InvalidOperationException("Microsoft 365 Copilot returned an empty response.");
        }

        return reply;
    }

    public async Task<string> CreateConversationAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        using var response = await graphClient
            .SendWithRetryAsync($"{graphClient.BaseUrl}/beta/copilot/conversations", accessToken, HttpMethod.Post, "{}", cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var data = await graphClient.ReadJsonAsync<CreateConversationResponse>(response, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(data.Id))
        {
            throw new InvalidOperationException("Failed to create Copilot conversation: no ID returned.");
        }

        return data.Id;
    }

    public async Task<string> SendMessageAsync(
        string accessToken,
        string conversationId,
        string message,
        CopilotChatSendOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var firstTurn = await SendMessageWithStreamingFallbackAsync(accessToken, conversationId, message, options, cancellationToken).ConfigureAwait(false);
        var fullReply = firstTurn.Text;
        var messageCount = firstTurn.MessageCount;
        var partLengths = new List<int>(firstTurn.PartLengths);
        var streamEventCount = firstTurn.StreamEventCount;
        var integrity = CopilotResponseIntegrityChecker.Evaluate(fullReply);
        var truncationDetected = integrity.IsLikelyTruncated;
        var continuationRounds = 0;

        while (integrity.IsLikelyTruncated && continuationRounds < MaxContinuationRounds)
        {
            continuationRounds++;
            graphClient.LogDiagnostic(
                $"! Copilot response may be incomplete ({integrity.Reason}); requesting continuation {continuationRounds}/{MaxContinuationRounds}");

            CopilotChatTurnResult continuation;
            try
            {
                continuation = await SendMessageWithStreamingFallbackAsync(accessToken, conversationId, ContinuationPrompt, options: null, cancellationToken).ConfigureAwait(false);
            }
            catch (AcceptedStreamingResponseException)
            {
                // The continuation stream was accepted but then stalled or contained malformed SSE.
                // fullReply still holds the usable initial response; surface it as incomplete so
                // the user can read it and trigger manual continuation if needed.
                graphClient.LogDiagnostic("! Continuation stream accepted but failed mid-transfer; returning partial response.");
                break;
            }

            messageCount += continuation.MessageCount;
            partLengths.AddRange(continuation.PartLengths);
            streamEventCount += continuation.StreamEventCount;

            if (string.IsNullOrWhiteSpace(continuation.Text))
            {
                break;
            }

            fullReply = StitchAssistantResponses(fullReply, continuation.Text);
            integrity = CopilotResponseIntegrityChecker.Evaluate(fullReply);
        }

        LastResponseDiagnostics = new CopilotChatResponseDiagnostics(
            messageCount,
            partLengths,
            fullReply.Length,
            continuationRounds,
            truncationDetected,
            integrity.IsLikelyTruncated,
            integrity.Reason,
            streamEventCount);
        graphClient.LogDiagnostic(BuildResponseDiagnosticsLog(LastResponseDiagnostics));

        return fullReply;
    }

    public async Task<string> ContinueAsync(
        string accessToken,
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        var continuation = await SendMessageWithStreamingFallbackAsync(accessToken, conversationId, ContinuationPrompt, options: null, cancellationToken).ConfigureAwait(false);
        var integrity = CopilotResponseIntegrityChecker.Evaluate(continuation.Text);
        SetLastResponseDiagnostics(new CopilotChatResponseDiagnostics(
            continuation.MessageCount,
            continuation.PartLengths,
            continuation.Text.Length,
            continuationRounds: 0,
            truncationDetected: integrity.IsLikelyTruncated,
            mayBeIncomplete: integrity.IsLikelyTruncated,
            truncationReason: integrity.Reason,
            continuation.StreamEventCount));
        return continuation.Text;
    }

    private async Task<CopilotChatTurnResult> SendMessageWithStreamingFallbackAsync(
        string accessToken,
        string conversationId,
        string message,
        CopilotChatSendOptions? options,
        CancellationToken cancellationToken)
    {
        try
        {
            var streamed = await SendSingleMessageOverStreamAsync(accessToken, conversationId, message, options, cancellationToken).ConfigureAwait(false);
            if (streamed is not null && !string.IsNullOrWhiteSpace(streamed.Text))
            {
                return streamed;
            }

            if (streamed is not null)
            {
                graphClient.LogDiagnostic("! Copilot chat stream returned no assistant text; falling back to synchronous chat.");
            }
        }
        catch (Exception ex) when (ex is not AcceptedStreamingResponseException &&
            ex is JsonException or IOException or InvalidOperationException or TimeoutException or HttpRequestException)
        {
            graphClient.LogDiagnostic($"! Copilot chat stream failed; falling back to synchronous chat. {ex.GetType().Name}: {ex.Message}");
        }

        return await SendSingleMessageAsync(accessToken, conversationId, message, options, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CopilotChatTurnResult?> SendSingleMessageOverStreamAsync(
        string accessToken,
        string conversationId,
        string message,
        CopilotChatSendOptions? options,
        CancellationToken cancellationToken)
    {
        var body = BuildRequestBody(message, options);
        using var response = await graphClient
            .SendStreamingAsync($"{graphClient.BaseUrl}/beta/copilot/conversations/{conversationId}/chatOverStream", accessToken, HttpMethod.Post, body, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            if (ShouldFallbackFromStreaming(response.StatusCode))
            {
                graphClient.LogDiagnostic($"! Copilot chatOverStream returned {(int)response.StatusCode}; falling back to synchronous chat.");
                return null;
            }

            await graphClient.ReadJsonAsync<ChatResponse>(response, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("Unexpected successful Graph error parsing path.");
        }

        try
        {
            var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var result = await ParseStreamWithTimeoutAsync(stream, message, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(result.Text))
            {
                throw new AcceptedStreamingResponseException("Copilot chat stream returned no assistant text after the request was accepted.");
            }

            graphClient.LogDiagnostic(
                $"Copilot chat stream diagnostics: events={result.StreamEventCount}, messageCount={result.MessageCount}, totalLength={result.Text.Length}");
            return result;
        }
        catch (Exception ex) when (ex is not AcceptedStreamingResponseException &&
            ex is JsonException or IOException or InvalidOperationException or TimeoutException)
        {
            throw new AcceptedStreamingResponseException("Copilot chat stream failed after the request was accepted.", ex);
        }
    }

    private async Task<CopilotChatTurnResult> ParseStreamWithTimeoutAsync(
        Stream stream,
        string message,
        CancellationToken cancellationToken)
    {
        var timeout = graphClient.Timeout;
        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return await CopilotChatStreamParser.ParseAsync(stream, message, cancellationToken).ConfigureAwait(false);
        }

        using var streamTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        streamTimeout.CancelAfter(timeout);
        try
        {
            return await CopilotChatStreamParser.ParseAsync(stream, message, streamTimeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && streamTimeout.IsCancellationRequested)
        {
            throw new TimeoutException("Copilot chat stream timed out before the response body completed.", ex);
        }
    }

    private async Task<CopilotChatTurnResult> SendSingleMessageAsync(
        string accessToken,
        string conversationId,
        string message,
        CopilotChatSendOptions? options,
        CancellationToken cancellationToken)
    {
        var body = BuildRequestBody(message, options);

        using var response = await graphClient
            .SendWithRetryAsync($"{graphClient.BaseUrl}/beta/copilot/conversations/{conversationId}/chat", accessToken, HttpMethod.Post, body, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var data = await graphClient.ReadJsonAsync<ChatResponse>(response, cancellationToken).ConfigureAwait(false);

        return ExtractAssistantReply(data, message);
    }

    private static string BuildRequestBody(string message, CopilotChatSendOptions? options)
    {
        var request = new CopilotChatRequest
        {
            Message = new CopilotChatRequestMessage { Text = message },
            LocationHint = new CopilotLocationHint { TimeZone = ResolveTimeZone() }
        };

        if (options?.AdditionalContext is { Count: > 0 })
        {
            request.AdditionalContext = options.AdditionalContext;
        }

        if (options?.ContextualResources is { } contextualResources &&
            contextualResources.Files is { Count: > 0 })
        {
            request.ContextualResources = contextualResources;
        }

        return JsonSerializer.Serialize(request, SerializerOptions);
    }

    private static bool ShouldFallbackFromStreaming(System.Net.HttpStatusCode statusCode)
    {
        return statusCode == (System.Net.HttpStatusCode)429 ||
            statusCode is System.Net.HttpStatusCode.NotFound or
            System.Net.HttpStatusCode.MethodNotAllowed or
            System.Net.HttpStatusCode.NotImplemented or
            System.Net.HttpStatusCode.InternalServerError or
            System.Net.HttpStatusCode.BadGateway or
            System.Net.HttpStatusCode.ServiceUnavailable or
            System.Net.HttpStatusCode.GatewayTimeout;
    }

    private static CopilotChatTurnResult ExtractAssistantReply(ChatResponse data, string requestMessage)
    {
        var messages = data.Messages ?? Array.Empty<CopilotChatMessage>();
        var hasCreatedDate = messages.Any(message => message.CreatedDateTime.HasValue);
        var orderedMessages = messages
            .Select((message, index) => new IndexedCopilotChatMessage(message, index))
            .OrderBy(item => hasCreatedDate ? item.Message.CreatedDateTime ?? DateTimeOffset.MaxValue : DateTimeOffset.MinValue)
            .ThenBy(item => item.Index);

        var parts = new List<string>();
        var partLengths = new List<int>();
        var seenParts = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in orderedMessages)
        {
            var text = item.Message.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text) ||
                string.Equals(text, requestMessage, StringComparison.Ordinal) ||
                !seenParts.Add(text!))
            {
                continue;
            }

            parts.Add(text!);
            partLengths.Add(text!.Length);
        }

        return new CopilotChatTurnResult(
            JoinResponseParts(parts),
            messages.Length,
            partLengths,
            streamEventCount: 0);
    }

    public static string StitchAssistantResponses(string existing, string continuation)
    {
        if (string.IsNullOrWhiteSpace(existing))
        {
            return continuation.Trim();
        }

        if (string.IsNullOrWhiteSpace(continuation))
        {
            return existing.Trim();
        }

        var left = existing.TrimEnd();
        var right = continuation.TrimStart();
        var overlapLength = FindOverlapLength(left, right);
        if (overlapLength > 0)
        {
            return left + right.Substring(overlapLength);
        }

        return AppendWithNaturalBoundary(left, right);
    }

    internal static string JoinResponseParts(IReadOnlyList<string> parts)
    {
        if (parts.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(parts[0]);
        for (var index = 1; index < parts.Count; index++)
        {
            var combined = AppendWithNaturalBoundary(builder.ToString(), parts[index]);
            builder.Clear();
            builder.Append(combined);
        }

        return builder.ToString();
    }

    private static string AppendWithNaturalBoundary(string left, string right)
    {
        if (left.Length == 0)
        {
            return right;
        }

        if (right.Length == 0)
        {
            return left;
        }

        var separator = char.IsWhiteSpace(left[left.Length - 1]) ||
            char.IsWhiteSpace(right[0]) ||
            IsContinuationPunctuation(right[0])
                ? string.Empty
                : Environment.NewLine;
        return left + separator + right;
    }

    private static bool IsContinuationPunctuation(char value)
    {
        return value == '.' || value == ',' || value == ';' || value == ':' || value == ')' || value == ']' || value == '}';
    }

    private static int FindOverlapLength(string left, string right)
    {
        var max = Math.Min(Math.Min(left.Length, right.Length), 1000);
        for (var length = max; length >= 20; length--)
        {
            if (left.EndsWith(right.Substring(0, length), StringComparison.Ordinal))
            {
                return length;
            }
        }

        return 0;
    }

    private static string BuildResponseDiagnosticsLog(CopilotChatResponseDiagnostics diagnostics)
    {
        return "Copilot chat response diagnostics: " +
            $"messageCount={diagnostics.MessageCount}, " +
            $"partLengths=[{string.Join(",", diagnostics.PartLengths)}], " +
            $"totalLength={diagnostics.TotalLength}, " +
            $"continuationRounds={diagnostics.ContinuationRounds}, " +
            $"truncationDetected={diagnostics.TruncationDetected}, " +
            $"mayBeIncomplete={diagnostics.MayBeIncomplete}, " +
            $"reason={diagnostics.TruncationReason ?? "none"}, " +
            $"streamEventCount={diagnostics.StreamEventCount}";
    }

    private static string ResolveTimeZone()
    {
        try
        {
            var localTimeZoneId = TimeZoneInfo.Local.Id;
            // Graph requires IANA zone names (for example "Asia/Tokyo").
            if (!string.IsNullOrWhiteSpace(localTimeZoneId) && localTimeZoneId.IndexOf('/') >= 0)
            {
                return localTimeZoneId;
            }
        }
        catch
        {
        }

        return DefaultIanaTimeZone;
    }

    private sealed class CreateConversationResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
    }

    private sealed class ChatResponse
    {
        [JsonPropertyName("messages")]
        public CopilotChatMessage[]? Messages { get; set; }
    }

    private sealed class CopilotChatMessage
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("createdDateTime")]
        public DateTimeOffset? CreatedDateTime { get; set; }
    }

    private sealed class CopilotChatRequest
    {
        [JsonPropertyName("message")]
        public CopilotChatRequestMessage Message { get; set; } = new();

        [JsonPropertyName("locationHint")]
        public CopilotLocationHint LocationHint { get; set; } = new();

        [JsonPropertyName("additionalContext")]
        public IReadOnlyList<CopilotContextMessage>? AdditionalContext { get; set; }

        [JsonPropertyName("contextualResources")]
        public CopilotContextualResources? ContextualResources { get; set; }
    }

    private sealed class CopilotChatRequestMessage
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    private sealed class CopilotLocationHint
    {
        [JsonPropertyName("timeZone")]
        public string TimeZone { get; set; } = string.Empty;
    }

    private sealed class IndexedCopilotChatMessage
    {
        public IndexedCopilotChatMessage(CopilotChatMessage message, int index)
        {
            Message = message;
            Index = index;
        }

        public CopilotChatMessage Message { get; }

        public int Index { get; }
    }

    internal sealed class CopilotChatTurnResult
    {
        public CopilotChatTurnResult(string text, int messageCount, IReadOnlyList<int> partLengths, int streamEventCount)
        {
            Text = text;
            MessageCount = messageCount;
            PartLengths = partLengths;
            StreamEventCount = streamEventCount;
        }

        public string Text { get; }

        public int MessageCount { get; }

        public IReadOnlyList<int> PartLengths { get; }

        public int StreamEventCount { get; }
    }

    private sealed class AcceptedStreamingResponseException : InvalidOperationException
    {
        public AcceptedStreamingResponseException(string message)
            : base(message)
        {
        }

        public AcceptedStreamingResponseException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}

public sealed class CopilotChatSendOptions
{
    public IReadOnlyList<CopilotContextMessage> AdditionalContext { get; set; } = Array.Empty<CopilotContextMessage>();

    public CopilotContextualResources? ContextualResources { get; set; }
}

public sealed class CopilotContextMessage
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public sealed class CopilotContextualResources
{
    [JsonPropertyName("files")]
    public IReadOnlyList<CopilotContextualFileResource> Files { get; set; } = Array.Empty<CopilotContextualFileResource>();
}

public sealed class CopilotContextualFileResource
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;
}
