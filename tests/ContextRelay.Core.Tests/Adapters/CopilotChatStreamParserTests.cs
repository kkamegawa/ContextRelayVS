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

    private static Stream ToStream(string value)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n")));
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
