using System;

namespace ContextRelay.VSExtension.Services;

internal sealed class ContextRelayStateChangedEventArgs : EventArgs
{
    public ContextRelayStateChangedEventArgs(ContextRelayHostState state)
    {
        State = state;
    }

    public ContextRelayHostState State { get; }
}
