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

public sealed class MailSearchAdapter : IContextSearchAdapter
{
    private readonly GraphHttpClient graphClient;

    public MailSearchAdapter(GraphHttpClient? graphClient = null)
    {
        this.graphClient = graphClient ?? new GraphHttpClient();
    }

    public ContextSource Source => ContextSource.Mail;

    public async Task<IReadOnlyList<ContextItem>> SearchAsync(
        string accessToken,
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        var encoded = Uri.EscapeDataString($"\"{query}\"");
        var url = $"{graphClient.BaseUrl}/v1.0/me/messages?$search={encoded}&$top={maxResults}";

        using var response = await graphClient
            .SendWithRetryAsync(url, accessToken, HttpMethod.Get, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var data = await graphClient.ReadJsonAsync<MailResponse>(response, cancellationToken).ConfigureAwait(false);

        return (data.Value ?? Array.Empty<MailMessage>())
            .Select(msg => new ContextItem
            {
                Source = ContextSource.Mail,
                Title = string.IsNullOrWhiteSpace(msg.Subject) ? "(No subject)" : msg.Subject!,
                Snippet = msg.BodyPreview ?? string.Empty,
                Url = msg.WebLink,
                Timestamp = msg.ReceivedDateTime,
                Cache = new ContextItemCacheInfo { Hit = false },
                Metadata = new Dictionary<string, string>
                {
                    ["messageId"] = msg.Id ?? string.Empty,
                    ["senderName"] = msg.From?.EmailAddress?.Name ?? string.Empty,
                    ["senderAddress"] = msg.From?.EmailAddress?.Address ?? string.Empty
                }
            })
            .ToArray();
    }

    private sealed class MailResponse
    {
        [JsonPropertyName("value")]
        public MailMessage[]? Value { get; set; }
    }

    private sealed class MailMessage
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("subject")]
        public string? Subject { get; set; }

        [JsonPropertyName("bodyPreview")]
        public string? BodyPreview { get; set; }

        [JsonPropertyName("receivedDateTime")]
        public string? ReceivedDateTime { get; set; }

        [JsonPropertyName("webLink")]
        public string? WebLink { get; set; }

        [JsonPropertyName("from")]
        public MailSender? From { get; set; }
    }

    private sealed class MailSender
    {
        [JsonPropertyName("emailAddress")]
        public MailEmailAddress? EmailAddress { get; set; }
    }

    private sealed class MailEmailAddress
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("address")]
        public string? Address { get; set; }
    }
}
