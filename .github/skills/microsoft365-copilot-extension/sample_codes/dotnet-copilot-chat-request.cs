using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

internal static class CopilotChatSample
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task<string> SendAsync(
        HttpClient httpClient,
        string accessToken,
        string prompt,
        string? conversationId = null,
        CancellationToken cancellationToken = default)
    {
        var activeConversationId = string.IsNullOrWhiteSpace(conversationId)
            ? await CreateConversationAsync(httpClient, accessToken, cancellationToken).ConfigureAwait(false)
            : conversationId!;

        var requestBody = new
        {
            message = new { text = prompt },
            locationHint = new { timeZone = ResolveCopilotTimeZone() },
            additionalContext = new[]
            {
                new
                {
                    description = "Pinned context",
                    text = "Summarize the latest architecture notes before answering."
                }
            },
            contextualResources = new
            {
                files = new[]
                {
                    new
                    {
                        uri = "https://contoso.sharepoint.com/sites/engineering/Shared%20Documents/Architecture.md"
                    }
                }
            }
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://graph.microsoft.com/beta/copilot/conversations/{activeConversationId}/chat");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody, SerializerOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> CreateConversationAsync(
        HttpClient httpClient,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://graph.microsoft.com/beta/copilot/conversations");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        if (!document.RootElement.TryGetProperty("id", out var idProperty) ||
            string.IsNullOrWhiteSpace(idProperty.GetString()))
        {
            throw new InvalidOperationException("Copilot conversation response did not contain an id.");
        }

        return idProperty.GetString()!;
    }

    private static string ResolveCopilotTimeZone()
    {
        try
        {
            // On Windows, .NET commonly returns a Windows ID such as "Tokyo Standard Time".
            // Copilot Chat expects an IANA identifier, so fall back to Etc/UTC unless you have a trusted mapper.
            var localId = TimeZoneInfo.Local.Id;
            return localId.Contains("/", StringComparison.Ordinal) ? localId : "Etc/UTC";
        }
        catch
        {
            return "Etc/UTC";
        }
    }
}
