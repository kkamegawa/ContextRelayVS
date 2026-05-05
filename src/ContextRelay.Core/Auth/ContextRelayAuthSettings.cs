namespace ContextRelay.Core.Auth;

public sealed class ContextRelayAuthSettings
{
    public string ClientId { get; set; } = string.Empty;

    public string TenantId { get; set; } = "organizations";

    public CloudEnvironment CloudEnvironment { get; set; } = CloudEnvironment.Global;

    public string CustomGraphEndpoint { get; set; } = string.Empty;

    public string CustomAuthEndpoint { get; set; } = string.Empty;

    // Defaults to false to opt out of the OS authentication broker by default.
    // ContextRelaySettingsSnapshot.UseBroker also defaults to false; the two must stay aligned.
    public bool UseBroker { get; set; } = false;

    public string GraphEndpoint =>
        CloudEndpoints.GetGraphEndpoint(CloudEnvironment, CustomGraphEndpoint);

    public string AuthEndpoint =>
        CloudEndpoints.GetAuthEndpoint(CloudEnvironment, CustomAuthEndpoint);
}
