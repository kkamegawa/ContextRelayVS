using ContextRelay.Core.Adapters;
using Xunit;

namespace ContextRelay.Core.Tests.Adapters;

public sealed class GraphHttpClientTests
{
    [Fact]
    public void DefaultBaseUrl_IsGlobalGraph()
    {
        var client = new GraphHttpClient();
        Assert.Equal("https://graph.microsoft.com", client.BaseUrl);
    }

    [Fact]
    public void DefaultTimeout_AllowsLongCopilotResponses()
    {
        var client = new GraphHttpClient();
        Assert.Equal(GraphHttpClient.DefaultTimeout, client.Timeout);
    }

    [Fact]
    public void CustomBaseUrl_IsPreserved()
    {
        var client = new GraphHttpClient(baseUrl: "https://graph.microsoft.us");
        Assert.Equal("https://graph.microsoft.us", client.BaseUrl);
    }

    [Fact]
    public void CustomBaseUrl_StripsTrailingSlash()
    {
        var client = new GraphHttpClient(baseUrl: "https://graph.microsoft.us/");
        Assert.Equal("https://graph.microsoft.us", client.BaseUrl);
    }

    [Fact]
    public void NullBaseUrl_FallsBackToDefault()
    {
        var client = new GraphHttpClient(baseUrl: null);
        Assert.Equal("https://graph.microsoft.com", client.BaseUrl);
    }

    [Fact]
    public void EmptyBaseUrl_FallsBackToDefault()
    {
        var client = new GraphHttpClient(baseUrl: "");
        Assert.Equal("https://graph.microsoft.com", client.BaseUrl);
    }

    [Fact]
    public void BaseUrl_CanBeUpdatedAtRuntime()
    {
        var client = new GraphHttpClient();
        Assert.Equal("https://graph.microsoft.com", client.BaseUrl);

        client.BaseUrl = "https://dod-graph.microsoft.us";
        Assert.Equal("https://dod-graph.microsoft.us", client.BaseUrl);
    }

    [Fact]
    public void BaseUrl_Setter_NormalizesWhitespaceAndTrailingSlash()
    {
        var client = new GraphHttpClient();

        client.BaseUrl = "  https://graph.microsoft.us/  ";

        Assert.Equal("https://graph.microsoft.us", client.BaseUrl);
    }

    [Fact]
    public void BaseUrl_Setter_FallsBackWhenInsecure()
    {
        var client = new GraphHttpClient();

        client.BaseUrl = "http://graph.microsoft.us";

        Assert.Equal("https://graph.microsoft.com", client.BaseUrl);
    }
}
