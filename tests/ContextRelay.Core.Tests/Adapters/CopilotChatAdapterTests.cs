using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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
            CreateStreamResponse("""
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
            CreateStreamResponse("""
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
            CreateStreamResponse("""
                {
                  "messages": [
                    { "text": "Create a fenced block." },
                    { "text": "```text\nFirst line" }
                  ]
                }
                """),
            CreateStreamResponse("""
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
            CreatePlainPartResponse("part 3"),
            CreatePlainPartResponse("part 4"),
            CreatePlainPartResponse("part 5"));
        using var httpClient = new HttpClient(handler);
        var adapter = new CopilotChatAdapter(new GraphHttpClient(httpClient));

        var reply = await adapter.SendMessageAsync("token", "c", "Create a fenced block.", cancellationToken: cancellationToken);

        Assert.Contains("part 5", reply, StringComparison.Ordinal);
        Assert.Equal(6, handler.RequestBodies.Count);
        Assert.True(adapter.LastResponseDiagnostics.MayBeIncomplete);
        Assert.Equal(5, adapter.LastResponseDiagnostics.ContinuationRounds);
    }

    [Fact]
    public async Task SendMessageAsync_FallsBackToSynchronousChatWhenStreamingIsUnavailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new RecordingQueueHttpMessageHandler(
            CreateResponse(HttpStatusCode.NotFound, """{ "error": { "code": "notFound" } }"""),
            CreateResponse(HttpStatusCode.OK, """
                {
                  "messages": [
                    { "text": "Summarize." },
                    { "text": "Fallback reply." }
                  ]
                }
                """));
        using var httpClient = new HttpClient(handler);
        var adapter = new CopilotChatAdapter(new GraphHttpClient(httpClient));

        var reply = await adapter.SendMessageAsync("token", "c", "Summarize.", cancellationToken: cancellationToken);

        Assert.Equal("Fallback reply.", reply);
        Assert.Equal(2, handler.RequestBodies.Count);
        Assert.EndsWith("/chatOverStream", handler.RequestUris[0], StringComparison.Ordinal);
        Assert.EndsWith("/chat", handler.RequestUris[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendMessageAsync_FallsBackToSynchronousChatWhenStreamingTimesOut()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new RecordingQueueHttpMessageHandler(
            new TaskCanceledException("Simulated stream timeout."),
            CreateResponse(HttpStatusCode.OK, """
                {
                  "messages": [
                    { "text": "Summarize." },
                    { "text": "Fallback after timeout." }
                  ]
                }
                """));
        using var httpClient = new HttpClient(handler);
        var adapter = new CopilotChatAdapter(new GraphHttpClient(httpClient));

        var reply = await adapter.SendMessageAsync("token", "c", "Summarize.", cancellationToken: cancellationToken);

        Assert.Equal("Fallback after timeout.", reply);
        Assert.Equal(2, handler.RequestBodies.Count);
        Assert.EndsWith("/chatOverStream", handler.RequestUris[0], StringComparison.Ordinal);
        Assert.EndsWith("/chat", handler.RequestUris[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendMessageAsync_DoesNotRepostWhenAcceptedStreamingBodyStalls()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new RecordingQueueHttpMessageHandler(
            CreateStallingStreamResponse(),
            CreateResponse(HttpStatusCode.OK, """
                {
                  "messages": [
                    { "text": "Summarize." },
                    { "text": "Fallback after stalled body." }
                  ]
                }
                """));
        using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(100) };
        var adapter = new CopilotChatAdapter(new GraphHttpClient(httpClient));

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => adapter.SendMessageAsync("token", "c", "Summarize.", cancellationToken: cancellationToken));

        Assert.Contains("accepted", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(handler.RequestBodies);
        Assert.EndsWith("/chatOverStream", handler.RequestUris[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendMessageAsync_FallsBackToSynchronousChatWhenStreamingErrorBodyStalls()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new RecordingQueueHttpMessageHandler(
            CreateStallingErrorResponse(),
            CreateResponse(HttpStatusCode.OK, """
                {
                  "messages": [
                    { "text": "Summarize." },
                    { "text": "Fallback after stalled error body." }
                  ]
                }
                """));
        using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(100) };
        var adapter = new CopilotChatAdapter(new GraphHttpClient(httpClient));

        var reply = await adapter.SendMessageAsync("token", "c", "Summarize.", cancellationToken: cancellationToken);

        Assert.Equal("Fallback after stalled error body.", reply);
        Assert.Equal(2, handler.RequestBodies.Count);
        Assert.EndsWith("/chatOverStream", handler.RequestUris[0], StringComparison.Ordinal);
        Assert.EndsWith("/chat", handler.RequestUris[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendMessageAsync_FallsBackToSynchronousChatWhenStreamingTransportFails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new RecordingQueueHttpMessageHandler(
            new HttpRequestException("stream reset"),
            CreateResponse(HttpStatusCode.OK, """
                {
                  "messages": [
                    { "text": "Summarize." },
                    { "text": "Fallback after transport failure." }
                  ]
                }
                """));
        using var httpClient = new HttpClient(handler);
        var adapter = new CopilotChatAdapter(new GraphHttpClient(httpClient));

        var reply = await adapter.SendMessageAsync("token", "c", "Summarize.", cancellationToken: cancellationToken);

        Assert.Equal("Fallback after transport failure.", reply);
        Assert.Equal(2, handler.RequestBodies.Count);
        Assert.EndsWith("/chatOverStream", handler.RequestUris[0], StringComparison.Ordinal);
        Assert.EndsWith("/chat", handler.RequestUris[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendMessageAsync_FallsBackToSynchronousChatWhenStreamingIsThrottled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new RecordingQueueHttpMessageHandler(
            CreateResponse((HttpStatusCode)429, """{ "error": { "code": "tooManyRequests" } }"""),
            CreateResponse(HttpStatusCode.OK, """
                {
                  "messages": [
                    { "text": "Summarize." },
                    { "text": "Fallback after throttle." }
                  ]
                }
                """));
        using var httpClient = new HttpClient(handler);
        var adapter = new CopilotChatAdapter(new GraphHttpClient(httpClient));

        var reply = await adapter.SendMessageAsync("token", "c", "Summarize.", cancellationToken: cancellationToken);

        Assert.Equal("Fallback after throttle.", reply);
        Assert.Equal(2, handler.RequestBodies.Count);
        Assert.EndsWith("/chatOverStream", handler.RequestUris[0], StringComparison.Ordinal);
        Assert.EndsWith("/chat", handler.RequestUris[1], StringComparison.Ordinal);
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
        return CreateStreamResponse($$"""
            {
              "messages": [
                { "text": "```text\n{{text}}" }
              ]
            }
            """);
    }

    private static HttpResponseMessage CreatePlainPartResponse(string text)
    {
        return CreateStreamResponse($$"""
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

    private static HttpResponseMessage CreateStreamResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"data: {CompressJson(json)}\n\n", Encoding.UTF8, "text/event-stream")
        };
    }

    private static HttpResponseMessage CreateStallingStreamResponse()
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StallingReadStream())
        };
    }

    private static HttpResponseMessage CreateStallingErrorResponse()
    {
        return new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StreamContent(new StallingReadStream())
        };
    }

    private static string CompressJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
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
        private readonly object[] responses;
        private int index;

        public RecordingQueueHttpMessageHandler(params object[] responses)
        {
            this.responses = responses;
        }

        public List<string> RequestBodies { get; } = new();

        public List<string> RequestUris { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri?.AbsolutePath ?? string.Empty);
            RequestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));

            var current = responses[Math.Min(index, responses.Length - 1)];
            index++;
            if (current is Exception exception)
            {
                throw exception;
            }

            return (HttpResponseMessage)current;
        }
    }

    private sealed class StallingReadStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
