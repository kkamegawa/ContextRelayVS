using ContextRelay.Core.Auth;
using Xunit;

namespace ContextRelay.Core.Tests.Auth;

public sealed class ContextRelayAuthSettingsTests
{
    [Fact]
    public void GraphEndpoint_DefaultsToGlobalCloud()
    {
        var settings = new ContextRelayAuthSettings();
        Assert.Equal("https://graph.microsoft.com", settings.GraphEndpoint);
    }

    [Fact]
    public void AuthEndpoint_DefaultsToGlobalCloud()
    {
        var settings = new ContextRelayAuthSettings();
        Assert.Equal("https://login.microsoftonline.com", settings.AuthEndpoint);
    }

    [Fact]
    public void GraphEndpoint_ReflectsCloudEnvironment()
    {
        var settings = new ContextRelayAuthSettings { CloudEnvironment = CloudEnvironment.USGovGCCHigh };
        Assert.Equal("https://graph.microsoft.us", settings.GraphEndpoint);
    }

    [Fact]
    public void AuthEndpoint_ReflectsCloudEnvironment()
    {
        var settings = new ContextRelayAuthSettings { CloudEnvironment = CloudEnvironment.China };
        Assert.Equal("https://login.chinacloudapi.cn", settings.AuthEndpoint);
    }

    [Fact]
    public void Custom_GraphEndpoint_UsesCustomValue()
    {
        var settings = new ContextRelayAuthSettings
        {
            CloudEnvironment = CloudEnvironment.Custom,
            CustomGraphEndpoint = "https://my.graph.api"
        };
        Assert.Equal("https://my.graph.api", settings.GraphEndpoint);
    }

    [Fact]
    public void Custom_AuthEndpoint_UsesCustomValue()
    {
        var settings = new ContextRelayAuthSettings
        {
            CloudEnvironment = CloudEnvironment.Custom,
            CustomAuthEndpoint = "https://my.auth.endpoint"
        };
        Assert.Equal("https://my.auth.endpoint", settings.AuthEndpoint);
    }

    [Fact]
    public void TenantId_DefaultsToOrganizations()
    {
        var settings = new ContextRelayAuthSettings();
        Assert.Equal("organizations", settings.TenantId);
    }
}
