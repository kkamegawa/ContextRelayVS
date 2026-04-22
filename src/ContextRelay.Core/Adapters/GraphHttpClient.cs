using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ContextRelay.Core.Adapters;

public sealed class GraphHttpClient
{
    public const string DefaultGraphBase = "https://graph.microsoft.com";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;
    private readonly IGraphLogger? logger;

    public GraphHttpClient(HttpClient? httpClient = null, IGraphLogger? logger = null, string? baseUrl = null)
    {
        this.httpClient = httpClient ?? new HttpClient();
        this.logger = logger;
        BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultGraphBase : baseUrl!.TrimEnd('/');
    }

    public string BaseUrl { get; set; }

    public async Task<HttpResponseMessage> SendAsync(
        string url,
        string accessToken,
        HttpMethod method,
        string? jsonBody = null,
        CancellationToken cancellationToken = default)
    {
        logger?.Log($"-> {method.Method} {url}");

        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        if (jsonBody is not null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        logger?.Log($"<- {(int)response.StatusCode} {response.ReasonPhrase} {url}");
        return response;
    }

    public async Task<HttpResponseMessage> SendWithRetryAsync(
        string url,
        string accessToken,
        HttpMethod method,
        string? jsonBody = null,
        int maxRetries = 3,
        CancellationToken cancellationToken = default)
    {
        var delay = TimeSpan.FromSeconds(1);

        for (var attempt = 0; ; attempt++)
        {
            var response = await SendAsync(url, accessToken, method, jsonBody, cancellationToken).ConfigureAwait(false);

            if ((response.StatusCode == (HttpStatusCode)429 || response.StatusCode == HttpStatusCode.ServiceUnavailable) &&
                attempt < maxRetries)
            {
                var retryAfter = GetRetryDelay(response) ?? delay;
                logger?.Log($"! throttled {(int)response.StatusCode}, retry {attempt + 1}/{maxRetries} after {retryAfter.TotalMilliseconds:0}ms");
                response.Dispose();
                await Task.Delay(retryAfter > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : retryAfter, cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 30000));
                continue;
            }

            return response;
        }
    }

    public async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        if (response.IsSuccessStatusCode)
        {
            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var result = await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
            if (result is null)
            {
                throw new InvalidOperationException("Graph API response body was empty.");
            }

            return result;
        }

        var body = await SafeReadBodyAsync(response).ConfigureAwait(false);
        var errorCode = TryGetErrorCode(body);
        var requestId = response.Headers.Contains("request-id")
            ? string.Join(",", response.Headers.GetValues("request-id"))
            : response.Headers.Contains("client-request-id")
                ? string.Join(",", response.Headers.GetValues("client-request-id"))
                : null;

        logger?.Log(BuildGraphErrorLog((int)response.StatusCode, errorCode, requestId));

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new GraphApiException(response.StatusCode, "Unauthorized: session expired, please re-authenticate.", errorCode, requestId);
        }

        if (response.StatusCode == HttpStatusCode.Forbidden && (body.IndexOf("CopilotLicenseRequired", StringComparison.OrdinalIgnoreCase) >= 0 ||
            body.IndexOf("licenseRequired", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            throw new GraphApiException(response.StatusCode, "Microsoft 365 Copilot license is required for this feature.", errorCode, requestId);
        }

        throw new GraphApiException(
            response.StatusCode,
            $"Graph API error {(int)response.StatusCode}: {body}",
            errorCode,
            requestId);
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

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string? TryGetErrorCode(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("code", out var code) &&
                code.ValueKind == JsonValueKind.String)
            {
                return code.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static string BuildGraphErrorLog(int status, string? errorCode, string? requestId)
    {
        var builder = new StringBuilder($"x Graph API error {status}");
        if (!string.IsNullOrWhiteSpace(errorCode))
        {
            builder.Append(" code=").Append(errorCode);
        }

        if (!string.IsNullOrWhiteSpace(requestId))
        {
            builder.Append(" requestId=").Append(requestId);
        }

        return builder.ToString();
    }
}
