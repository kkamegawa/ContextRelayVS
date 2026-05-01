using System;
using System.Collections.Generic;
using System.Linq;

namespace ContextRelay.Core.Auth;

public static class AuthScopeCatalog
{
    public const string DefaultGraphResource = "https://graph.microsoft.com";
    public const string DefaultWorkIqResource = "api://workiq.svc.cloud.microsoft";
    public const string WorkIqPermission = "WorkIQAgent.Ask";

    private static readonly IReadOnlyList<string> OidcScopes = new[] { "offline_access", "openid", "profile" };
    private static readonly IReadOnlyList<string> DefaultGraphScopes = new[] { "User.Read" };
    private static readonly IReadOnlyList<string> MailScopes = new[] { "Mail.Read" };
    private static readonly IReadOnlyList<string> TeamsScopes = new[] { "Chat.Read", "ChannelMessage.Read.All" };
    private static readonly IReadOnlyList<string> RetrievalScopes = new[] { "Files.Read.All", "Sites.Read.All" };
    private static readonly IReadOnlyList<string> ConnectorsScopes = new[] { "ExternalItem.Read.All" };
    private static readonly IReadOnlyList<string> OneNoteScopes = new[] { "Notes.Read" };
    private static readonly IReadOnlyList<string> PlannerScopes = new[] { "Tasks.Read" };
    private static readonly IReadOnlyList<string> TodoScopes = new[] { "Tasks.Read" };
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

        if (featureOptions.OneNoteEnabled)
        {
            scopes.UnionWith(OneNoteScopes);
        }

        if (featureOptions.PlannerEnabled)
        {
            scopes.UnionWith(PlannerScopes);
        }

        if (featureOptions.TodoEnabled)
        {
            scopes.UnionWith(TodoScopes);
        }

        if (featureOptions.ChatPreviewEnabled)
        {
            scopes.UnionWith(ChatScopes);
        }

        return scopes.OrderBy(scope => scope, StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyList<string> BuildQualifiedGraphScopes(ContextRelayFeatureOptions featureOptions, bool includeOidcScopes = false, string? graphResource = null)
    {
        var scopes = new HashSet<string>(BuildRequiredScopes(featureOptions), StringComparer.Ordinal);
        if (includeOidcScopes)
        {
            scopes.UnionWith(OidcScopes);
        }

        var resource = string.IsNullOrWhiteSpace(graphResource) ? DefaultGraphResource : graphResource!;
        return scopes
            .Select(scope => QualifyGraphScope(scope, resource))
            .OrderBy(scope => scope, StringComparer.Ordinal)
            .ToArray();
    }

    public static string QualifyGraphScope(string scope, string? graphResource = null)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            throw new ArgumentException("Scope must not be empty.", nameof(scope));
        }

        if (OidcScopes.Contains(scope, StringComparer.Ordinal))
        {
            return scope;
        }

        var resource = string.IsNullOrWhiteSpace(graphResource) ? DefaultGraphResource : graphResource!;
        return QualifyResourceScope(scope, resource);
    }

    public static IReadOnlyList<string> BuildWorkIqScopes(bool includeOidcScopes = false, string? workIqResource = null)
    {
        var scopes = new HashSet<string>(StringComparer.Ordinal);
        if (includeOidcScopes)
        {
            scopes.UnionWith(OidcScopes);
        }

        var resource = string.IsNullOrWhiteSpace(workIqResource) ? DefaultWorkIqResource : workIqResource!;
        scopes.Add(QualifyResourceScope(WorkIqPermission, resource));
        return scopes.OrderBy(scope => scope, StringComparer.Ordinal).ToArray();
    }

    public static string GetMissingClientIdConfigurationMessage()
    {
        return string.Join(
            " ",
            "ContextRelay for Visual Studio requires contextRelay.auth.clientId to be configured.",
            "Use your own Entra app registration with Microsoft Graph delegated permissions,",
            "and optionally set contextRelay.auth.tenantId to pin sign-in to one tenant.");
    }

    private static string QualifyResourceScope(string scope, string resource)
    {
        return scope.Contains("://", StringComparison.Ordinal)
            ? scope
            : $"{resource}/{scope}";
    }
}
