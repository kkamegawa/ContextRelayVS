using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ContextRelay.Core.Chat;

/// <summary>
/// Coordinates one active chat request, pending attachment ownership, and history persistence.
/// </summary>
public sealed class ChatRequestStateMachine : IDisposable
{
    private readonly object sync = new();
    private readonly HashSet<string> pendingAttachmentIds = new(StringComparer.Ordinal);
    private CancellationTokenSource? activeCancellation;
    private Request? activeRequest;
    private long clearGeneration;

    public bool IsRunning
    {
        get { lock (sync) return activeRequest is not null; }
    }

    public void AddPendingAttachment(string attachmentId)
    {
        if (string.IsNullOrWhiteSpace(attachmentId)) return;
        lock (sync) pendingAttachmentIds.Add(attachmentId);
    }

    public bool RemovePendingAttachment(string attachmentId)
    {
        lock (sync) return pendingAttachmentIds.Remove(attachmentId);
    }

    public IReadOnlyList<string> PendingAttachmentIds
    {
        get { lock (sync) return pendingAttachmentIds.ToArray(); }
    }

    public bool TryBegin(IEnumerable<string> submittedPendingIds, CancellationToken cancellationToken, out Request request)
    {
        lock (sync)
        {
            if (activeRequest is not null)
            {
                request = null!;
                return false;
            }

            activeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            request = new Request(
                this,
                activeCancellation.Token,
                submittedPendingIds?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray()
                    ?? Array.Empty<string>(),
                clearGeneration);
            activeRequest = request;
            return true;
        }
    }

    public void Stop()
    {
        lock (sync) activeCancellation?.Cancel();
    }

    public void Clear()
    {
        lock (sync)
        {
            clearGeneration++;
        }
    }

    public bool TryRecordHistory(Request request)
    {
        lock (sync)
        {
            if (request.historyRecorded || request.clearGeneration != clearGeneration)
                return false;
            request.historyRecorded = true;
            return true;
        }
    }

    public IReadOnlyList<string> Complete(Request request)
    {
        lock (sync)
        {
            if (!ReferenceEquals(activeRequest, request)) return Array.Empty<string>();
            var consumed = request.submittedPendingIds.Where(pendingAttachmentIds.Contains).ToArray();
            foreach (var id in consumed) pendingAttachmentIds.Remove(id);
            activeRequest = null;
            activeCancellation?.Dispose();
            activeCancellation = null;
            return consumed;
        }
    }

    public void Dispose()
    {
        lock (sync) activeCancellation?.Dispose();
    }

    public sealed class Request
    {
        private readonly ChatRequestStateMachine owner;
        internal readonly IReadOnlyList<string> submittedPendingIds;
        internal readonly long clearGeneration;
        internal bool historyRecorded;

        internal Request(ChatRequestStateMachine owner, CancellationToken cancellationToken, IReadOnlyList<string> submittedPendingIds, long clearGeneration)
        {
            this.owner = owner;
            this.submittedPendingIds = submittedPendingIds;
            this.clearGeneration = clearGeneration;
            CancellationToken = cancellationToken;
        }

        public CancellationToken CancellationToken { get; }

        public bool TryRecordHistory() => owner.TryRecordHistory(this);
    }
}
