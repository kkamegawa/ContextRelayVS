using System;
using System.Collections.Generic;
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
    private const int MaxContinuationRounds = 3;
    private const string ContinuePrompt = "Continue exactly from where your previous message stopped. Do not repeat earlier content.";

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
        var firstTurn = await SendSingleMessageAsync(accessToken, conversationId, message, options, cancellationToken).ConfigureAwait(false);
        var fullReply = firstTurn.Text;
        var messageCount = firstTurn.MessageCount;
        var partLengths = new List<int>(firstTurn.PartLengths);
        var integrity = CopilotResponseIntegrityChecker.Evaluate(fullReply);
        var truncationDetected = integrity.IsLikelyTruncated;
        var continuationRounds = 0;

        while (integrity.IsLikelyTruncated && continuationRounds < MaxContinuationRounds)
        {
            continuationRounds++;
            graphClient.LogDiagnostic(
                $"! Copilot response may be incomplete ({integrity.Reason}); requesting continuation {continuationRounds}/{MaxContinuationRounds}");

            var continuation = await SendSingleMessageAsync(accessToken, conversationId, ContinuePrompt, options: null, cancellationToken).ConfigureAwait(false);
            messageCount += continuation.MessageCount;
            partLengths.AddRange(continuation.PartLengths);

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
            integrity.Reason);
        graphClient.LogDiagnostic(BuildResponseDiagnosticsLog(LastResponseDiagnostics));

        return fullReply;
    }

    private async Task<CopilotChatTurnResult> SendSingleMessageAsync(
        string accessToken,
        string conversationId,
        string message,
        CopilotChatSendOptions? options,
        CancellationToken cancellationToken)
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

        var body = JsonSerializer.Serialize(request, SerializerOptions);

        using var response = await graphClient
            .SendWithRetryAsync($"{graphClient.BaseUrl}/beta/copilot/conversations/{conversationId}/chat", accessToken, HttpMethod.Post, body, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var data = await graphClient.ReadJsonAsync<ChatResponse>(response, cancellationToken).ConfigureAwait(false);

        return ExtractAssistantReply(data, message);
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
            partLengths);
    }

    internal static string StitchAssistantResponses(string existing, string continuation)
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

    private static string JoinResponseParts(IReadOnlyList<string> parts)
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
            $"reason={diagnostics.TruncationReason ?? "none"}";
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

    private sealed class CopilotChatTurnResult
    {
        public CopilotChatTurnResult(string text, int messageCount, IReadOnlyList<int> partLengths)
        {
            Text = text;
            MessageCount = messageCount;
            PartLengths = partLengths;
        }

        public string Text { get; }

        public int MessageCount { get; }

        public IReadOnlyList<int> PartLengths { get; }
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
