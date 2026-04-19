namespace ContextRelay.Core.Auth;

public sealed class ContextRelayAuthSettings
{
    public string ClientId { get; set; } = string.Empty;

    public string TenantId { get; set; } = "organizations";
}
