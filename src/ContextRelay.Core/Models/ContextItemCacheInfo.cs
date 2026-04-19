namespace ContextRelay.Core.Models;

public sealed class ContextItemCacheInfo
{
    public bool Hit { get; set; }

    public string? StoredAt { get; set; }

    public int? TtlSeconds { get; set; }
}
