using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ContextRelay.Core.Adapters;

public sealed class CopilotChatAdapter : ICopilotChatAdapter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly GraphHttpClient graphClient;

    public CopilotChatAdapter(GraphHttpClient? graphClient = null)
    {
        this.graphClient = graphClient ?? new GraphHttpClient();
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

        var messages = data.Messages ?? Array.Empty<CopilotChatMessage>();
        for (var index = messages.Length - 1; index >= 0; index--)
        {
            var chatMessage = messages[index];
            var text = chatMessage.Text;
            if (!string.IsNullOrWhiteSpace(text) && !string.Equals(text, message, StringComparison.Ordinal))
            {
                return text!;
            }
        }

        return string.Empty;
    }

    private static string ResolveTimeZone()
    {
        try
        {
            return TimeZoneInfo.Local.Id;
        }
        catch
        {
            return "UTC";
        }
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
