using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.Adapters;
using Xunit;

namespace ContextRelay.Core.Tests.Adapters;

public sealed class WorkIqAdapterTests
{
    [Fact]
    public async Task SendMessageAsync_SendsA2ARequestAndParsesResponse()
    {
        var handler = new RecordingHandler(_ => CreateJsonResponse(
            """
            {
              "result": {
                "task": {
                  "id": "task-1",
                  "contextId": "ctx-next",
                  "status": { "state": "TASK_STATE_COMPLETED" },
                  "artifacts": [
                    { "parts": [ { "text": "Work IQ summary" } ] }
                  ]
                }
              }
            }
            """));
        using var adapter = new WorkIqAdapter(new HttpClient(handler), ownsHttpClient: true);
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await adapter.SendMessageAsync("token-1", "Summarize my day", "ctx-current", cancellationToken: cancellationToken);

        Assert.Equal("Work IQ summary", response.Text);
        Assert.Equal("ctx-next", response.ContextId);
        Assert.Equal("task-1", response.TaskId);

        Assert.Single(handler.Requests);
        var request = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal("token-1", request.AuthorizationParameter);
        Assert.Equal(WorkIqAdapter.A2aVersion, request.Headers["A2A-Version"]);
        Assert.Equal("application/json", request.ContentType);

        using var document = JsonDocument.Parse(request.Body);
        var root = document.RootElement;
        Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.Equal("SendMessage", root.GetProperty("method").GetString());

        var message = root.GetProperty("params").GetProperty("message");
        Assert.Equal("ROLE_USER", message.GetProperty("role").GetString());
        Assert.Equal("ctx-current", message.GetProperty("contextId").GetString());
        Assert.Equal("Summarize my day", message.GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.True(message.GetProperty("metadata").TryGetProperty("Location", out _));
    }

    [Fact]
    public async Task SendMessageAsync_RetriesOn429ThenSucceeds()
    {
        var attempt = 0;
        var handler = new RecordingHandler(_ =>
        {
            attempt++;
            if (attempt == 1)
            {
                var throttled = new HttpResponseMessage((HttpStatusCode)429);
                throttled.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
                return throttled;
            }

            return CreateJsonResponse(
                """
                {
                  "result": {
                    "task": {
                      "status": { "state": "TASK_STATE_COMPLETED" },
                      "artifacts": [ { "parts": [ { "text": "Recovered" } ] } ]
                    }
                  }
                }
                """);
        });
        using var adapter = new WorkIqAdapter(new HttpClient(handler), ownsHttpClient: true);
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await adapter.SendMessageAsync("token-1", "Retry me", cancellationToken: cancellationToken);

        Assert.Equal("Recovered", response.Text);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task SendMessageAsync_ThrowsHelpfulForbiddenError()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        using var adapter = new WorkIqAdapter(new HttpClient(handler), ownsHttpClient: true);
        var cancellationToken = TestContext.Current.CancellationToken;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            adapter.SendMessageAsync("token-1", "Why is this forbidden?", cancellationToken: cancellationToken));

        Assert.Contains("WorkIQAgent.Ask", exception.Message);
    }

    [Fact]
    public async Task SendMessageAsync_PrefersArtifactContentOverStatusMessage()
    {
        var handler = new RecordingHandler(_ => CreateJsonResponse(
            """
            {
              "result": {
                "task": {
                  "status": {
                    "state": "TASK_STATE_COMPLETED",
                    "message": { "parts": [ { "text": "Working..." } ] }
                  },
                  "artifacts": [
                    { "parts": [ { "text": "## Summary\n- item 1" } ] }
                  ]
                }
              }
            }
            """));
        using var adapter = new WorkIqAdapter(new HttpClient(handler), ownsHttpClient: true);
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await adapter.SendMessageAsync("token-1", "Prefer artifact", cancellationToken: cancellationToken);

        Assert.Equal("## Summary\n- item 1", response.Text);
    }

    private static HttpResponseMessage CreateJsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> responder;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            this.responder = responder;
        }

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in request.Headers)
            {
                headers[header.Key] = string.Join(",", header.Value);
            }

            Requests.Add(new CapturedRequest
            {
                Method = request.Method,
                Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken),
                Headers = headers,
                ContentType = request.Content?.Headers.ContentType?.MediaType,
                AuthorizationScheme = request.Headers.Authorization?.Scheme,
                AuthorizationParameter = request.Headers.Authorization?.Parameter
            });

            return responder(request);
        }
    }

    private sealed class CapturedRequest
    {
        public HttpMethod Method { get; set; } = HttpMethod.Get;

        public string Body { get; set; } = string.Empty;

        public Dictionary<string, string> Headers { get; set; } = [];

        public string? ContentType { get; set; }

        public string? AuthorizationScheme { get; set; }

        public string? AuthorizationParameter { get; set; }
    }
}
