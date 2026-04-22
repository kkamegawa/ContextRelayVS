using System;

namespace ContextRelay.Core.Auth;

public static class CloudEndpoints
{
    public const string GlobalGraph = "https://graph.microsoft.com";
    public const string GlobalAuth = "https://login.microsoftonline.com";

    public const string USGovGCCHighGraph = "https://graph.microsoft.us";
    public const string USGovGCCHighAuth = "https://login.microsoftonline.us";

    public const string USGovDoDGraph = "https://dod-graph.microsoft.us";
    public const string USGovDoDAuth = "https://login.microsoftonline.us";

    public const string GermanyGraph = "https://graph.microsoft.de";
    public const string GermanyAuth = "https://login.microsoftonline.de";

    public const string ChinaGraph = "https://microsoftgraph.chinacloudapi.cn";
    public const string ChinaAuth = "https://login.chinacloudapi.cn";

    public static string GetGraphEndpoint(CloudEnvironment environment, string? customEndpoint = null)
    {
        return environment switch
        {
            CloudEnvironment.Global => GlobalGraph,
            CloudEnvironment.USGovGCCHigh => USGovGCCHighGraph,
            CloudEnvironment.USGovDoD => USGovDoDGraph,
            CloudEnvironment.Germany => GermanyGraph,
            CloudEnvironment.China => ChinaGraph,
            CloudEnvironment.Custom => NormalizeEndpoint(customEndpoint, GlobalGraph),
            _ => GlobalGraph
        };
    }

    public static string GetAuthEndpoint(CloudEnvironment environment, string? customEndpoint = null)
    {
        return environment switch
        {
            CloudEnvironment.Global => GlobalAuth,
            CloudEnvironment.USGovGCCHigh => USGovGCCHighAuth,
            CloudEnvironment.USGovDoD => USGovDoDAuth,
            CloudEnvironment.Germany => GermanyAuth,
            CloudEnvironment.China => ChinaAuth,
            CloudEnvironment.Custom => NormalizeEndpoint(customEndpoint, GlobalAuth),
            _ => GlobalAuth
        };
    }

    public static string GetDisplayName(CloudEnvironment environment)
    {
        return environment switch
        {
            CloudEnvironment.Global => "Global (Public)",
            CloudEnvironment.USGovGCCHigh => "US Gov (GCC High)",
            CloudEnvironment.USGovDoD => "US Gov (DoD)",
            CloudEnvironment.Germany => "Germany",
            CloudEnvironment.China => "China (21Vianet)",
            CloudEnvironment.Custom => "Custom",
            _ => "Global (Public)"
        };
    }

    internal static string NormalizeEndpoint(string? endpoint, string fallback)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return fallback;
        }

        var trimmed = endpoint!.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            return fallback;
        }

        if (!string.IsNullOrEmpty(uri.PathAndQuery.TrimStart('/')))
        {
            return $"{uri.Scheme}://{uri.Host}";
        }

        return trimmed;
    }
}
