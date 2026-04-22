using ContextRelay.Core.Auth;
using Xunit;

namespace ContextRelay.Core.Tests.Auth;

public sealed class CloudEndpointsTests
{
    [Fact]
    public void GetGraphEndpoint_ReturnsCorrectEndpoints()
    {
        Assert.Equal("https://graph.microsoft.com", CloudEndpoints.GetGraphEndpoint(CloudEnvironment.Global));
        Assert.Equal("https://graph.microsoft.us", CloudEndpoints.GetGraphEndpoint(CloudEnvironment.USGovGCCHigh));
        Assert.Equal("https://dod-graph.microsoft.us", CloudEndpoints.GetGraphEndpoint(CloudEnvironment.USGovDoD));
        Assert.Equal("https://graph.microsoft.de", CloudEndpoints.GetGraphEndpoint(CloudEnvironment.Germany));
        Assert.Equal("https://microsoftgraph.chinacloudapi.cn", CloudEndpoints.GetGraphEndpoint(CloudEnvironment.China));
    }

    [Fact]
    public void GetAuthEndpoint_ReturnsCorrectEndpoints()
    {
        Assert.Equal("https://login.microsoftonline.com", CloudEndpoints.GetAuthEndpoint(CloudEnvironment.Global));
        Assert.Equal("https://login.microsoftonline.us", CloudEndpoints.GetAuthEndpoint(CloudEnvironment.USGovGCCHigh));
        Assert.Equal("https://login.microsoftonline.us", CloudEndpoints.GetAuthEndpoint(CloudEnvironment.USGovDoD));
        Assert.Equal("https://login.microsoftonline.de", CloudEndpoints.GetAuthEndpoint(CloudEnvironment.Germany));
        Assert.Equal("https://login.chinacloudapi.cn", CloudEndpoints.GetAuthEndpoint(CloudEnvironment.China));
    }

    [Fact]
    public void Custom_GraphEndpoint_UsesProvidedValue()
    {
        var endpoint = CloudEndpoints.GetGraphEndpoint(CloudEnvironment.Custom, "https://my.custom.graph");
        Assert.Equal("https://my.custom.graph", endpoint);
    }

    [Fact]
    public void Custom_GraphEndpoint_FallsBackWhenEmpty()
    {
        var endpoint = CloudEndpoints.GetGraphEndpoint(CloudEnvironment.Custom, "");
        Assert.Equal("https://graph.microsoft.com", endpoint);
    }

    [Fact]
    public void Custom_GraphEndpoint_FallsBackWhenNull()
    {
        var endpoint = CloudEndpoints.GetGraphEndpoint(CloudEnvironment.Custom, null);
        Assert.Equal("https://graph.microsoft.com", endpoint);
    }

    [Fact]
    public void Custom_GraphEndpoint_StripsTrailingSlash()
    {
        var endpoint = CloudEndpoints.GetGraphEndpoint(CloudEnvironment.Custom, "https://my.custom.graph/");
        Assert.Equal("https://my.custom.graph", endpoint);
    }

    [Fact]
    public void Custom_GraphEndpoint_RejectsHttpEndpoint()
    {
        var endpoint = CloudEndpoints.GetGraphEndpoint(CloudEnvironment.Custom, "http://insecure.graph");
        Assert.Equal("https://graph.microsoft.com", endpoint);
    }

    [Fact]
    public void Custom_GraphEndpoint_StripsPath()
    {
        var endpoint = CloudEndpoints.GetGraphEndpoint(CloudEnvironment.Custom, "https://my.graph/v1.0/me");
        Assert.Equal("https://my.graph", endpoint);
    }

    [Fact]
    public void GetDisplayName_ReturnsReadableNames()
    {
        Assert.Equal("Global (Public)", CloudEndpoints.GetDisplayName(CloudEnvironment.Global));
        Assert.Equal("US Gov (GCC High)", CloudEndpoints.GetDisplayName(CloudEnvironment.USGovGCCHigh));
        Assert.Equal("US Gov (DoD)", CloudEndpoints.GetDisplayName(CloudEnvironment.USGovDoD));
        Assert.Equal("Germany", CloudEndpoints.GetDisplayName(CloudEnvironment.Germany));
        Assert.Equal("China (21Vianet)", CloudEndpoints.GetDisplayName(CloudEnvironment.China));
        Assert.Equal("Custom", CloudEndpoints.GetDisplayName(CloudEnvironment.Custom));
    }
}
