namespace ContextRelay.Core.Auth;

public sealed class ContextRelayAccountInfo
{
    public string HomeAccountId { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string? Environment { get; set; }
}
