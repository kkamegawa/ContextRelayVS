using System;

namespace ContextRelay.Core.Utilities;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
