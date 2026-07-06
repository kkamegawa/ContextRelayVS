using System;
using System.Collections.Generic;

namespace ContextRelay.Core.Adapters;

public sealed class CopilotChatResponseDiagnostics
{
    public static CopilotChatResponseDiagnostics Empty { get; } = new(
        messageCount: 0,
        partLengths: Array.Empty<int>(),
        totalLength: 0,
        continuationRounds: 0,
        truncationDetected: false,
        mayBeIncomplete: false,
        truncationReason: null);

    public CopilotChatResponseDiagnostics(
        int messageCount,
        IReadOnlyList<int> partLengths,
        int totalLength,
        int continuationRounds,
        bool truncationDetected,
        bool mayBeIncomplete,
        string? truncationReason)
    {
        MessageCount = messageCount;
        PartLengths = partLengths ?? throw new ArgumentNullException(nameof(partLengths));
        TotalLength = totalLength;
        ContinuationRounds = continuationRounds;
        TruncationDetected = truncationDetected;
        MayBeIncomplete = mayBeIncomplete;
        TruncationReason = truncationReason;
    }

    public int MessageCount { get; }

    public IReadOnlyList<int> PartLengths { get; }

    public int TotalLength { get; }

    public int ContinuationRounds { get; }

    public bool TruncationDetected { get; }

    public bool MayBeIncomplete { get; }

    public string? TruncationReason { get; }
}
