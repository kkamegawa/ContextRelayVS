using System.ComponentModel;
using Microsoft.VisualStudio.Shell;

namespace ContextRelay.VSExtension.Options;

public sealed class ContextRelayAuthenticationOptionsPage : DialogPage
{
    [Category("Authentication")]
    [DisplayName("Client ID")]
    [Description("Microsoft Entra public-client application ID used for MSAL.NET sign-in.")]
    public string ClientId { get; set; } = string.Empty;

    [Category("Authentication")]
    [DisplayName("Tenant ID")]
    [Description("Tenant ID or organizations/common authority segment.")]
    public string TenantId { get; set; } = "organizations";
}
