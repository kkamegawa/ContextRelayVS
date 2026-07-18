using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.Adapters;
using Xunit;

namespace ContextRelay.Core.Tests.Adapters;

public sealed class CopilotChatStreamParserTests
{
    [Fact]
    public async Task ParseAsync_UsesLatestNonEmptyAssistantSnapshot()
    {
        var stream = ToStream("""
            data: {"messages":[{"text":"Write a plan."},{"text":"First part. Second part."}]}
            id:1

            data: {"messages":[]}
            id:2

            data: {"messages":[{"text":"Write a plan."},{"text":"Final."}]}
            id:3

            """);

        var result = await CopilotChatStreamParser.ParseAsync(stream, "Write a plan.", TestContext.Current.CancellationToken);

        Assert.Equal("Final.", result.Text);
        Assert.Equal(3, result.StreamEventCount);
        Assert.Equal(2, result.MessageCount);
    }

    [Fact]
    public async Task ParseAsync_AcceptsDocumentedMultilineDataEvent()
    {
        var stream = ToStream("""
            data: {
            data:   "messages": [
            data:     { "text": "Prompt" },
            data:     { "text": "Assistant reply." }
            data:   ]
            data: }
            id:1

            """);

        var result = await CopilotChatStreamParser.ParseAsync(stream, "Prompt", TestContext.Current.CancellationToken);

        Assert.Equal("Assistant reply.", result.Text);
        Assert.Equal(1, result.StreamEventCount);
    }

    [Fact]
    public async Task ParseAsync_IgnoresCommentsAndUnknownFieldsInsideEvent()
    {
        var stream = ToStream("""
            data: {
            : keep-alive
            data:   "messages": [
            x-extra: ignored
            data:     { "text": "Prompt" },
            data:     { "text": "Assistant reply." }
            data:   ]
            data: }
            id:1

            """);

        var result = await CopilotChatStreamParser.ParseAsync(stream, "Prompt", TestContext.Current.CancellationToken);

        Assert.Equal("Assistant reply.", result.Text);
        Assert.Equal(1, result.StreamEventCount);
    }

    [Fact]
    public async Task ParseAsync_IgnoresMalformedJsonEvent()
    {
        // A single malformed event no longer throws; the result is empty since no valid snapshot exists.
        var stream = ToStream("""
            data: {"messages":[

            """);

        var result = await CopilotChatStreamParser.ParseAsync(stream, "Prompt", TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, result.Text);
    }

    [Fact]
    public async Task ParseAsync_PreservesValidSnapshotAfterMalformedJsonEvent()
    {
        // A malformed event that arrives after a valid snapshot must not discard the valid snapshot.
        var stream = ToStream("""
            data: {"messages":[{"text":"Prompt"},{"text":"Valid answer."}]}

            data: {"messages":[

            """);

        var result = await CopilotChatStreamParser.ParseAsync(stream, "Prompt", TestContext.Current.CancellationToken);

        Assert.Equal("Valid answer.", result.Text);
    }

    [Fact]
    public async Task ParseAsync_ObservesCancellationWhileWaitingForLine()
    {
        using var stream = new StallingReadStream();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CopilotChatStreamParser.ParseAsync(stream, "Prompt", cancellation.Token));
    }

    [Fact]
    public async Task ParseAsync_TreatsDoneEventAsTerminal()
    {
        // After [DONE], the parser must stop reading and return the last valid snapshot
        // without waiting for the server to close the connection.
        var stream = ToStream("""
            data: {"messages":[{"text":"Prompt"},{"text":"Complete answer."}]}

            data: [DONE]

            """);

        var result = await CopilotChatStreamParser.ParseAsync(stream, "Prompt", TestContext.Current.CancellationToken);

        Assert.Equal("Complete answer.", result.Text);
    }

    [Fact]
    public async Task ParseAsync_PreservesSnapshotOnTransportFailureAfterValidEvent()
    {
        // An IOException raised after a valid snapshot has been captured must not
        // discard that snapshot; the parser must return the last valid result.
        var stream = new PartialThenThrowStream(
            "data: {\"messages\":[{\"text\":\"Prompt\"},{\"text\":\"Valid answer.\"}]}\n\n");

        var result = await CopilotChatStreamParser.ParseAsync(stream, "Prompt", TestContext.Current.CancellationToken);

        Assert.Equal("Valid answer.", result.Text);
    }

    [Fact]
    public async Task ParseAsync_PreservesSnapshotWhenInternalTimeoutFiresAfterValidEvent()
    {
        // Mirrors CopilotChatAdapter.ParseStreamWithTimeoutAsync's linked timeout token: the
        // read token is cancelled (an internal body-read timeout) while the caller's own token
        // stays uncancelled, so the already-captured snapshot must be preserved and flagged.
        using var stream = new PartialThenStallStream(
            "data: {\"messages\":[{\"text\":\"Prompt\"},{\"text\":\"Valid answer.\"}]}\n\n");
        using var readTimeout = new CancellationTokenSource();
        readTimeout.CancelAfter(TimeSpan.FromMilliseconds(50));

        var result = await CopilotChatStreamParser.ParseAsync(
            stream,
            "Prompt",
            readTimeout.Token,
            TestContext.Current.CancellationToken);

        Assert.Equal("Valid answer.", result.Text);
        Assert.True(result.Interrupted);
    }

    [Fact]
    public async Task ParseAsync_PropagatesRealCallerCancellationEvenAfterValidEvent()
    {
        // When the same token represents genuine caller cancellation (not an internal timeout),
        // it must still propagate rather than being swallowed as an interrupted snapshot.
        using var stream = new PartialThenStallStream(
            "data: {\"messages\":[{\"text\":\"Prompt\"},{\"text\":\"Valid answer.\"}]}\n\n");
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CopilotChatStreamParser.ParseAsync(stream, "Prompt", cancellation.Token, cancellation.Token));
    }

    [Fact]
    public async Task ParseAsync_SupportsDocumentedChatOverStreamResponseFormat()
    {
        // The Microsoft Graph chatOverStream endpoint only prefixes the first physical line of
        // the JSON body with "data:"; the remaining pretty-printed body lines (including the
        // closing brace) stream unprefixed until the object closes, followed by an "id:" field.
        var stream = ToStream("""
            data: {
              "id": "conversation-1",
              "messages": [
                {
                  "text": "Prompt"
                },
                {
                  "text": "Assistant reply from the documented endpoint format."
                }
              ]
            }
            id:137

            """);

        var result = await CopilotChatStreamParser.ParseAsync(stream, "Prompt", TestContext.Current.CancellationToken);

        Assert.Equal("Assistant reply from the documented endpoint format.", result.Text);
        Assert.Equal(1, result.StreamEventCount);
    }

    private static Stream ToStream(string value)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n")));
    }

    /// <summary>
    /// Streams initial bytes then throws <see cref="IOException"/> to simulate a mid-stream transport failure.
    /// </summary>
    private sealed class PartialThenThrowStream : Stream
    {
        private readonly byte[] initialBytes;
        private int position;

        public PartialThenThrowStream(string initialContent)
        {
            initialBytes = Encoding.UTF8.GetBytes(initialContent);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (position < initialBytes.Length)
            {
                var toCopy = Math.Min(count, initialBytes.Length - position);
                Array.Copy(initialBytes, position, buffer, offset, toCopy);
                position += toCopy;
                return Task.FromResult(toCopy);
            }

            throw new IOException("Simulated transport failure after valid SSE event.");
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// Streams initial bytes then blocks forever, unblocking only when the read is cancelled.
    /// Simulates a stream that stalls mid-transfer rather than failing outright.
    /// </summary>
    private sealed class PartialThenStallStream : Stream
    {
        private readonly byte[] initialBytes;
        private int position;

        public PartialThenStallStream(string initialContent)
        {
            initialBytes = Encoding.UTF8.GetBytes(initialContent);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (position < initialBytes.Length)
            {
                var toCopy = Math.Min(count, initialBytes.Length - position);
                Array.Copy(initialBytes, position, buffer, offset, toCopy);
                position += toCopy;
                return toCopy;
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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
