using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ContextRelay.Core.Adapters;

public sealed class CopilotChatAdapter : ICopilotChatAdapter
{
    private readonly GraphHttpClient graphClient;

    public CopilotChatAdapter(GraphHttpClient? graphClient = null)
    {
        this.graphClient = graphClient ?? new GraphHttpClient();
    }

    public async Task<string> AskAsync(string accessToken, string prompt, CancellationToken cancellationToken = default)
    {
        var conversationId = await CreateConversationAsync(accessToken, cancellationToken).ConfigureAwait(false);
        var reply = await SendMessageAsync(accessToken, conversationId, prompt, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(reply))
        {
            throw new InvalidOperationException("Microsoft 365 Copilot returned an empty response.");
        }

        return reply;
    }

    private async Task<string> CreateConversationAsync(string accessToken, CancellationToken cancellationToken)
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

    private async Task<string> SendMessageAsync(string accessToken, string conversationId, string message, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(new
        {
            message = new { text = message },
            locationHint = new { timeZone = ResolveTimeZone() }
        });

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
}
