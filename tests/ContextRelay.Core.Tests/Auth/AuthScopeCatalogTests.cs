using System.Linq;
using ContextRelay.Core.Auth;
using Xunit;

namespace ContextRelay.Core.Tests.Auth;

public sealed class AuthScopeCatalogTests
{
    [Fact]
    public void BuildRequiredScopes_IncludesDefaultAndEnabledFeatureScopes()
    {
        var scopes = AuthScopeCatalog.BuildRequiredScopes(new ContextRelayFeatureOptions()).ToArray();

        Assert.Contains("User.Read", scopes);
        Assert.Contains("Mail.Read", scopes);
        Assert.Contains("Files.Read.All", scopes);
        Assert.Contains("Sites.Read.All", scopes);
        Assert.Contains("Chat.Read", scopes);
        Assert.DoesNotContain("ExternalItem.Read.All", AuthScopeCatalog.BuildRequiredScopes(new ContextRelayFeatureOptions
        {
            ConnectorsEnabled = false,
            ChatPreviewEnabled = false
        }));
    }

    [Fact]
    public void BuildQualifiedGraphScopes_QualifiesGraphScopesAndOptionallyOidcScopes()
    {
        var scopes = AuthScopeCatalog.BuildQualifiedGraphScopes(
            new ContextRelayFeatureOptions { MailEnabled = true, TeamsEnabled = false, SharePointEnabled = false, OneDriveEnabled = false, ChatPreviewEnabled = false },
            includeOidcScopes: true);

        Assert.Contains("https://graph.microsoft.com/User.Read", scopes);
        Assert.Contains("https://graph.microsoft.com/Mail.Read", scopes);
        Assert.Contains("offline_access", scopes);
        Assert.Contains("openid", scopes);
        Assert.Contains("profile", scopes);
    }

    [Fact]
    public void BuildQualifiedGraphScopes_UsesCustomGraphResource()
    {
        var scopes = AuthScopeCatalog.BuildQualifiedGraphScopes(
            new ContextRelayFeatureOptions { MailEnabled = true, TeamsEnabled = false, SharePointEnabled = false, OneDriveEnabled = false, ChatPreviewEnabled = false },
            graphResource: "https://graph.microsoft.us");

        Assert.Contains("https://graph.microsoft.us/User.Read", scopes);
        Assert.Contains("https://graph.microsoft.us/Mail.Read", scopes);
        Assert.DoesNotContain("https://graph.microsoft.com/User.Read", scopes);
    }

    [Fact]
    public void QualifyGraphScope_UsesDefaultResourceWhenNullPassed()
    {
        var qualified = AuthScopeCatalog.QualifyGraphScope("User.Read");
        Assert.Equal("https://graph.microsoft.com/User.Read", qualified);
    }

    [Fact]
    public void QualifyGraphScope_UsesCustomResource()
    {
        var qualified = AuthScopeCatalog.QualifyGraphScope("User.Read", "https://graph.microsoft.de");
        Assert.Equal("https://graph.microsoft.de/User.Read", qualified);
    }

    [Fact]
    public void QualifyGraphScope_PreservesOidcScopes()
    {
        Assert.Equal("offline_access", AuthScopeCatalog.QualifyGraphScope("offline_access", "https://graph.microsoft.us"));
        Assert.Equal("openid", AuthScopeCatalog.QualifyGraphScope("openid", "https://graph.microsoft.us"));
    }

    [Fact]
    public void QualifyGraphScope_PreservesAlreadyQualifiedScopes()
    {
        var qualified = AuthScopeCatalog.QualifyGraphScope("https://graph.microsoft.com/User.Read", "https://graph.microsoft.us");
        Assert.Equal("https://graph.microsoft.com/User.Read", qualified);
    }

    [Fact]
    public void BuildWorkIqScopes_IncludesQualifiedWorkIqScope()
    {
        var scopes = AuthScopeCatalog.BuildWorkIqScopes().ToArray();

        Assert.Single(scopes);
        Assert.Contains("api://workiq.svc.cloud.microsoft/WorkIQAgent.Ask", scopes);
    }

    [Fact]
    public void BuildWorkIqScopes_CanIncludeOidcScopes()
    {
        var scopes = AuthScopeCatalog.BuildWorkIqScopes(includeOidcScopes: true).ToArray();

        Assert.Contains("offline_access", scopes);
        Assert.Contains("openid", scopes);
        Assert.Contains("profile", scopes);
        Assert.Contains("api://workiq.svc.cloud.microsoft/WorkIQAgent.Ask", scopes);
    }
}
