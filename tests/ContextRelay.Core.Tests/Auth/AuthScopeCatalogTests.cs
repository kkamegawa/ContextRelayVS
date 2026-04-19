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
}
