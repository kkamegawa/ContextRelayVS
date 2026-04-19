using System;
using System.Collections.Generic;
using System.Linq;

namespace ContextRelay.Core.Auth;

public static class AuthScopeCatalog
{
    public const string GraphResource = "https://graph.microsoft.com";

    private static readonly IReadOnlyList<string> OidcScopes = new[] { "offline_access", "openid", "profile" };
    private static readonly IReadOnlyList<string> DefaultGraphScopes = new[] { "User.Read" };
    private static readonly IReadOnlyList<string> MailScopes = new[] { "Mail.Read" };
    private static readonly IReadOnlyList<string> TeamsScopes = new[] { "Chat.Read", "ChannelMessage.Read.All" };
    private static readonly IReadOnlyList<string> RetrievalScopes = new[] { "Files.Read.All", "Sites.Read.All" };
    private static readonly IReadOnlyList<string> ConnectorsScopes = new[] { "ExternalItem.Read.All" };
    private static readonly IReadOnlyList<string> ChatScopes = new[]
    {
        "Sites.Read.All",
        "Mail.Read",
        "People.Read.All",
        "OnlineMeetingTranscript.Read.All",
        "Chat.Read",
        "ChannelMessage.Read.All",
        "ExternalItem.Read.All"
    };

    public static IReadOnlyList<string> BuildRequiredScopes(ContextRelayFeatureOptions featureOptions)
    {
        if (featureOptions is null)
        {
            throw new ArgumentNullException(nameof(featureOptions));
        }

        var scopes = new HashSet<string>(DefaultGraphScopes, StringComparer.Ordinal);

        if (featureOptions.MailEnabled)
        {
            scopes.UnionWith(MailScopes);
        }

        if (featureOptions.TeamsEnabled)
        {
            scopes.UnionWith(TeamsScopes);
        }

        if (featureOptions.SharePointEnabled || featureOptions.OneDriveEnabled)
        {
            scopes.UnionWith(RetrievalScopes);
        }

        if (featureOptions.ConnectorsEnabled)
        {
            scopes.UnionWith(ConnectorsScopes);
        }

        if (featureOptions.ChatPreviewEnabled)
        {
            scopes.UnionWith(ChatScopes);
        }

        return scopes.OrderBy(scope => scope, StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyList<string> BuildQualifiedGraphScopes(ContextRelayFeatureOptions featureOptions, bool includeOidcScopes = false)
    {
        var scopes = new HashSet<string>(BuildRequiredScopes(featureOptions), StringComparer.Ordinal);
        if (includeOidcScopes)
        {
            scopes.UnionWith(OidcScopes);
        }

        return scopes
            .Select(QualifyGraphScope)
            .OrderBy(scope => scope, StringComparer.Ordinal)
            .ToArray();
    }

    public static string QualifyGraphScope(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            throw new ArgumentException("Scope must not be empty.", nameof(scope));
        }

        if (OidcScopes.Contains(scope, StringComparer.Ordinal))
        {
            return scope;
        }

        return scope.Contains("://", StringComparison.Ordinal)
            ? scope
            : $"{GraphResource}/{scope}";
    }

    public static string GetMissingClientIdConfigurationMessage()
    {
        return string.Join(
            " ",
            "ContextRelay for Visual Studio requires contextRelay.auth.clientId to be configured.",
            "Use your own Entra app registration with Microsoft Graph delegated permissions,",
            "and optionally set contextRelay.auth.tenantId to pin sign-in to one tenant.");
    }
}
