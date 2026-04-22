using System;
using System.Threading;
using Azure.Core;
using ContextRelay.Core.Auth;
using ContextRelay.Core.Auth.Msal;
using Microsoft.Graph;

namespace ContextRelay.Core.Adapters;

public sealed class GraphServiceClientFactory : IGraphServiceClientFactory
{
    private readonly IContextRelayAuthProvider authProvider;

    public GraphServiceClientFactory(IContextRelayAuthProvider? authProvider = null)
    {
        this.authProvider = authProvider ?? new MsalAuthProvider();
    }

    public GraphServiceClient Create(ContextRelayAuthSettings settings, ContextRelayFeatureOptions featureOptions)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        if (featureOptions is null)
        {
            throw new ArgumentNullException(nameof(featureOptions));
        }

        var scopes = AuthScopeCatalog.BuildRequiredScopes(featureOptions);
        var credential = new ContextRelayTokenCredential(authProvider, settings, featureOptions);
        return new GraphServiceClient(credential, scopes, settings.GraphEndpoint);
    }

    private sealed class ContextRelayTokenCredential : TokenCredential
    {
        private readonly IContextRelayAuthProvider authProvider;
        private readonly ContextRelayAuthSettings settings;
        private readonly ContextRelayFeatureOptions featureOptions;

        public ContextRelayTokenCredential(
            IContextRelayAuthProvider authProvider,
            ContextRelayAuthSettings settings,
            ContextRelayFeatureOptions featureOptions)
        {
            this.authProvider = authProvider;
            this.settings = settings;
            this.featureOptions = featureOptions;
        }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            var token = authProvider.GetAccessTokenAsync(settings, featureOptions, cancellationToken).GetAwaiter().GetResult();
            return new AccessToken(token.AccessToken, token.ExpiresOn);
        }

        public override async System.Threading.Tasks.ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            var token = await authProvider.GetAccessTokenAsync(settings, featureOptions, cancellationToken).ConfigureAwait(false);
            return new AccessToken(token.AccessToken, token.ExpiresOn);
        }
    }
}
