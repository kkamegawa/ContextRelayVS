namespace ContextRelay.Core.Auth;

public sealed class ContextRelayAuthSettings
{
    public string ClientId { get; set; } = string.Empty;

    public string TenantId { get; set; } = "organizations";

    public CloudEnvironment CloudEnvironment { get; set; } = CloudEnvironment.Global;

    public string CustomGraphEndpoint { get; set; } = string.Empty;

    public string CustomAuthEndpoint { get; set; } = string.Empty;

    public string GraphEndpoint =>
        CloudEndpoints.GetGraphEndpoint(CloudEnvironment, CustomGraphEndpoint);

    public string AuthEndpoint =>
        CloudEndpoints.GetAuthEndpoint(CloudEnvironment, CustomAuthEndpoint);
}
