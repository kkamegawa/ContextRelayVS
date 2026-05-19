using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

internal static class WorkIqSample
{
    public static async Task<string> SendMessageAsync(
        HttpClient httpClient,
        string accessToken,
        string text,
        string? contextId = null,
        CancellationToken cancellationToken = default)
    {
        var location = ResolveLocation();
        var payload = new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = Guid.NewGuid().ToString(),
            ["method"] = "SendMessage",
            ["params"] = new Dictionary<string, object?>
            {
                ["message"] = new Dictionary<string, object?>
                {
                    ["role"] = "ROLE_USER",
                    ["messageId"] = Guid.NewGuid().ToString(),
                    ["parts"] = new[]
                    {
                        new Dictionary<string, string>
                        {
                            ["text"] = text
                        }
                    },
                    ["metadata"] = new Dictionary<string, object?>
                    {
                        ["Location"] = new Dictionary<string, object?>
                        {
                            ["timeZone"] = location.TimeZone,
                            ["timeZoneOffset"] = location.TimeZoneOffsetMinutes
                        }
                    },
                    ["contextId"] = contextId
                }
            }
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://workiq.svc.cloud.microsoft/a2a/");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("A2A-Version", "1.0");
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private static (string TimeZone, int TimeZoneOffsetMinutes) ResolveLocation()
    {
        try
        {
            var localId = TimeZoneInfo.Local.Id;
            var normalizedTimeZone = localId.Contains("/", StringComparison.Ordinal) ? localId : "Etc/UTC";
            return (normalizedTimeZone, (int)TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.Now).TotalMinutes);
        }
        catch
        {
            return ("Etc/UTC", 0);
        }
    }
}
