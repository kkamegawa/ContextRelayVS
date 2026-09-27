using System;

namespace ContextRelay.VSExtension.Services;

internal sealed class ContextRelayStateChangedEventArgs : EventArgs
{
    public ContextRelayStateChangedEventArgs(ContextRelayHostState state, bool streamingOnly = false)
    {
        State = state;
        StreamingOnly = streamingOnly;
    }

    public ContextRelayHostState State { get; }

    /// <summary>
    /// Gets a value indicating whether only the streaming properties changed. Streaming progress
    /// arrives per response frame, so listeners can skip a full state refresh for these updates.
    /// </summary>
    public bool StreamingOnly { get; }
}
