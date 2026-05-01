using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ContextRelay.Core.Auth;

public interface IContextRelayAuthProvider
{
    IReadOnlyList<string> GetRequiredScopes(ContextRelayFeatureOptions featureOptions);

    Task<ContextRelayAccessToken?> TryGetAccessTokenSilentAsync(
        ContextRelayAuthSettings settings,
        ContextRelayFeatureOptions featureOptions,
        CancellationToken cancellationToken = default);

    Task<ContextRelayAccessToken> GetAccessTokenAsync(
        ContextRelayAuthSettings settings,
        ContextRelayFeatureOptions featureOptions,
        CancellationToken cancellationToken = default);

    Task<ContextRelayAccessToken> GetWorkIqAccessTokenAsync(
        ContextRelayAuthSettings settings,
        CancellationToken cancellationToken = default);

    Task<ContextRelayAccountInfo?> GetAccountAsync(
        ContextRelayAuthSettings settings,
        CancellationToken cancellationToken = default);
}
