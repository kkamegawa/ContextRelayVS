using System;

namespace ContextRelay.Core.SharedStore;

public sealed class SharedStoreChangedEventArgs : EventArgs
{
    public SharedStoreChangedEventArgs(SharedStoreFileKind fileKind, string? contentHash)
    {
        FileKind = fileKind;
        ContentHash = contentHash;
    }

    public SharedStoreFileKind FileKind { get; }

    public string? ContentHash { get; }
}
