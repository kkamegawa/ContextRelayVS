using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.Chat;
using Xunit;

namespace ContextRelay.Core.Tests.Chat;

public sealed class ChatRequestStateMachineTests
{
    [Fact]
    public void DoubleSubmission_IsRejectedUntilTheFirstRequestCompletes()
    {
        using var machine = new ChatRequestStateMachine();
        Assert.True(machine.TryBegin(Array.Empty<string>(), CancellationToken.None, out var first));
        Assert.False(machine.TryBegin(Array.Empty<string>(), CancellationToken.None, out _));
        machine.Complete(first);
        Assert.True(machine.TryBegin(Array.Empty<string>(), CancellationToken.None, out var second));
        machine.Complete(second);
    }

    [Fact]
    public void AttachmentAddedDuringGeneration_RemainsPending()
    {
        using var machine = new ChatRequestStateMachine();
        machine.AddPendingAttachment("before");
        Assert.True(machine.TryBegin(new[] { "before" }, CancellationToken.None, out var request));
        machine.AddPendingAttachment("during");
        Assert.Equal(new[] { "before" }, machine.Complete(request).OrderBy(id => id));
        Assert.Equal(new[] { "during" }, machine.PendingAttachmentIds);
    }

    [Fact]
    public async Task StopThenNewSend_CancelsFirstRequestAndAllowsSecond()
    {
        using var machine = new ChatRequestStateMachine();
        Assert.True(machine.TryBegin(Array.Empty<string>(), CancellationToken.None, out var first));
        machine.Stop();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await Task.Delay(Timeout.Infinite, first.CancellationToken));
        machine.Complete(first);
        Assert.True(machine.TryBegin(Array.Empty<string>(), CancellationToken.None, out var second));
        machine.Complete(second);
    }

    [Fact]
    public void ClearDuringGeneration_PreventsStaleHistoryAndPreservesPendingAttachment()
    {
        using var machine = new ChatRequestStateMachine();
        machine.AddPendingAttachment("pending");
        Assert.True(machine.TryBegin(new[] { "pending" }, CancellationToken.None, out var request));
        machine.Clear();
        Assert.False(request.TryRecordHistory());
        Assert.Equal(new[] { "pending" }, machine.PendingAttachmentIds);
        machine.Complete(request);
    }

    [Fact]
    public void HistoryPersistence_IsAllowedOnlyOncePerRequest()
    {
        using var machine = new ChatRequestStateMachine();
        Assert.True(machine.TryBegin(Array.Empty<string>(), CancellationToken.None, out var request));
        machine.Complete(request);
        Assert.True(request.TryRecordHistory());
        Assert.False(request.TryRecordHistory());
    }
}
