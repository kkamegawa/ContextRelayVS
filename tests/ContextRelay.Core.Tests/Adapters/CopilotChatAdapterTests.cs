using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.Adapters;
using Xunit;

namespace ContextRelay.Core.Tests.Adapters;

public sealed class CopilotChatAdapterTests
{
    [Fact]
    public async Task SendMessageAsync_ConcatenatesAssistantPartsInCreatedOrder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var httpClient = new HttpClient(new QueueHttpMessageHandler(
            CreateResponse(HttpStatusCode.OK, """
                {
                  "messages": [
                    { "text": "Second section.", "createdDateTime": "2026-07-06T00:02:00Z" },
                    { "text": "Write a report.", "createdDateTime": "2026-07-06T00:00:00Z" },
                    { "text": "First section.", "createdDateTime": "2026-07-06T00:01:00Z" }
                  ]
                }
                """)));
        var adapter = new CopilotChatAdapter(new GraphHttpClient(httpClient));

        var reply = await adapter.SendMessageAsync("token", "c", "Write a report.", cancellationToken: cancellationToken);

        Assert.Equal($"First section.{Environment.NewLine}Second section.", reply);
        Assert.Equal(3, adapter.LastResponseDiagnostics.MessageCount);
        Assert.Equal(new[] { "First section.".Length, "Second section.".Length }, adapter.LastResponseDiagnostics.PartLengths);
    }

    [Fact]
    public async Task SendMessageAsync_RemovesEchoAndDuplicateParts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var httpClient = new HttpClient(new QueueHttpMessageHandler(
            CreateResponse(HttpStatusCode.OK, """
                {
                  "messages": [
                    { "text": "Summarize." },
                    { "text": "Unique reply." },
                    { "text": "Unique reply." }
                  ]
                }
                """)));
        var adapter = new CopilotChatAdapter(new GraphHttpClient(httpClient));

        var reply = await adapter.SendMessageAsync("token", "c", "Summarize.", cancellationToken: cancellationToken);

        Assert.Equal("Unique reply.", reply);
    }

    [Fact]
    public async Task SendMessageAsync_ContinuesWhenResponseLooksTruncated()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new RecordingQueueHttpMessageHandler(
            CreateResponse(HttpStatusCode.OK, """
                {
                  "messages": [
                    { "text": "Create a fenced block." },
                    { "text": "```text\nFirst line" }
                  ]
                }
                """),
            CreateResponse(HttpStatusCode.OK, """
                {
                  "messages": [
                    { "text": "Continue exactly from where your previous message stopped. Do not repeat earlier content." },
                    { "text": "Second line.\n```" }
                  ]
                }
                """));
        using var httpClient = new HttpClient(handler);
        var adapter = new CopilotChatAdapter(new GraphHttpClient(httpClient));

        var reply = await adapter.SendMessageAsync("token", "c", "Create a fenced block.", cancellationToken: cancellationToken);

        Assert.Equal($"```text\nFirst line{Environment.NewLine}Second line.\n```", reply);
        Assert.Equal(2, handler.RequestBodies.Count);
        Assert.Contains("Continue exactly from where", handler.RequestBodies[1], StringComparison.Ordinal);
        Assert.True(adapter.LastResponseDiagnostics.TruncationDetected);
        Assert.False(adapter.LastResponseDiagnostics.MayBeIncomplete);
        Assert.Equal(1, adapter.LastResponseDiagnostics.ContinuationRounds);
    }

    [Fact]
    public async Task SendMessageAsync_StopsContinuationAtLimit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new RecordingQueueHttpMessageHandler(
            CreateUnclosedFenceResponse("part 0"),
            CreatePlainPartResponse("part 1"),
            CreatePlainPartResponse("part 2"),
            CreatePlainPartResponse("part 3"));
        using var httpClient = new HttpClient(handler);
        var adapter = new CopilotChatAdapter(new GraphHttpClient(httpClient));

        var reply = await adapter.SendMessageAsync("token", "c", "Create a fenced block.", cancellationToken: cancellationToken);

        Assert.Contains("part 3", reply, StringComparison.Ordinal);
        Assert.Equal(4, handler.RequestBodies.Count);
        Assert.True(adapter.LastResponseDiagnostics.MayBeIncomplete);
        Assert.Equal(3, adapter.LastResponseDiagnostics.ContinuationRounds);
    }

    [Fact]
    public void StitchAssistantResponses_RemovesOverlappingContinuation()
    {
        var existing = "The architecture has API, storage, and authentication layers.";
        var continuation = "authentication layers. Add observability and deployment guidance.";

        var stitched = CopilotChatAdapter.StitchAssistantResponses(existing, continuation);

        Assert.Equal("The architecture has API, storage, and authentication layers. Add observability and deployment guidance.", stitched);
    }

    private static HttpResponseMessage CreateUnclosedFenceResponse(string text)
    {
        return CreateResponse(HttpStatusCode.OK, $$"""
            {
              "messages": [
                { "text": "```text\n{{text}}" }
              ]
            }
            """);
    }

    private static HttpResponseMessage CreatePlainPartResponse(string text)
    {
        return CreateResponse(HttpStatusCode.OK, $$"""
            {
              "messages": [
                { "text": "{{text}}" }
              ]
            }
            """);
    }

    private static HttpResponseMessage CreateResponse(HttpStatusCode statusCode, string body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private sealed class QueueHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage[] responses;
        private int index;

        public QueueHttpMessageHandler(params HttpResponseMessage[] responses)
        {
            this.responses = responses;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var current = responses[Math.Min(index, responses.Length - 1)];
            index++;
            return Task.FromResult(current);
        }
    }

    private sealed class RecordingQueueHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage[] responses;
        private int index;

        public RecordingQueueHttpMessageHandler(params HttpResponseMessage[] responses)
        {
            this.responses = responses;
        }

        public List<string> RequestBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));

            var current = responses[Math.Min(index, responses.Length - 1)];
            index++;
            return current;
        }
    }
}
