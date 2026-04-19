using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Identity.Client;

namespace ContextRelay.Core.Auth.Msal;

public sealed class MsalAuthProvider : IContextRelayAuthProvider
{
    private readonly IMsalPublicClientFacadeFactory facadeFactory;

    public MsalAuthProvider(IMsalPublicClientFacadeFactory? facadeFactory = null)
    {
        this.facadeFactory = facadeFactory ?? new MsalPublicClientFacadeFactory();
    }

    public IReadOnlyList<string> GetRequiredScopes(ContextRelayFeatureOptions featureOptions)
    {
        return AuthScopeCatalog.BuildRequiredScopes(featureOptions);
    }

    public async Task<ContextRelayAccessToken?> TryGetAccessTokenSilentAsync(
        ContextRelayAuthSettings settings,
        ContextRelayFeatureOptions featureOptions,
        CancellationToken cancellationToken = default)
    {
        ValidateSettings(settings);
        var scopes = GetRequiredScopes(featureOptions);
        var facade = await facadeFactory.CreateAsync(settings, cancellationToken).ConfigureAwait(false);
        var account = await GetFirstAccountAsync(facade, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return null;
        }

        try
        {
            return await facade
                .AcquireTokenSilentAsync(scopes, account.HomeAccountId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (MsalUiRequiredException)
        {
            return null;
        }
        catch (MsalServiceException ex)
        {
            throw WrapServiceException(ex);
        }
    }

    public async Task<ContextRelayAccessToken> GetAccessTokenAsync(
        ContextRelayAuthSettings settings,
        ContextRelayFeatureOptions featureOptions,
        CancellationToken cancellationToken = default)
    {
        ValidateSettings(settings);
        var scopes = GetRequiredScopes(featureOptions);
        var facade = await facadeFactory.CreateAsync(settings, cancellationToken).ConfigureAwait(false);
        var account = await GetFirstAccountAsync(facade, cancellationToken).ConfigureAwait(false);

        if (account is not null)
        {
            try
            {
                return await facade
                    .AcquireTokenSilentAsync(scopes, account.HomeAccountId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (MsalUiRequiredException)
            {
            }
            catch (MsalServiceException ex)
            {
                throw WrapServiceException(ex);
            }
        }

        try
        {
            return await facade.AcquireTokenInteractiveAsync(scopes, cancellationToken).ConfigureAwait(false);
        }
        catch (MsalServiceException ex)
        {
            throw WrapServiceException(ex);
        }
        catch (MsalException ex)
        {
            throw new ContextRelayAuthenticationException(
                ContextRelayAuthenticationErrorCode.InteractiveAuthenticationFailed,
                "Interactive sign-in failed. Verify broker availability, app registration, and delegated Graph permissions.",
                ex);
        }
    }

    public async Task<ContextRelayAccountInfo?> GetAccountAsync(
        ContextRelayAuthSettings settings,
        CancellationToken cancellationToken = default)
    {
        ValidateSettings(settings);
        var facade = await facadeFactory.CreateAsync(settings, cancellationToken).ConfigureAwait(false);
        return await GetFirstAccountAsync(facade, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateSettings(ContextRelayAuthSettings settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        if (string.IsNullOrWhiteSpace(settings.ClientId))
        {
            throw new ContextRelayAuthenticationException(
                ContextRelayAuthenticationErrorCode.MissingClientId,
                AuthScopeCatalog.GetMissingClientIdConfigurationMessage());
        }
    }

    private static async Task<ContextRelayAccountInfo?> GetFirstAccountAsync(
        IMsalPublicClientFacade facade,
        CancellationToken cancellationToken)
    {
        var accounts = await facade.GetAccountsAsync(cancellationToken).ConfigureAwait(false);
        return accounts.FirstOrDefault();
    }

    private static ContextRelayAuthenticationException WrapServiceException(MsalServiceException ex)
    {
        var message = ex.Message;
        if (message.IndexOf("AADSTS65001", StringComparison.OrdinalIgnoreCase) >= 0 ||
            message.IndexOf("AADSTS65002", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            message = $"{message} Confirm that the Entra app registration grants the required Microsoft Graph delegated permissions and tenant admin consent where needed.";
        }

        return new ContextRelayAuthenticationException(
            ContextRelayAuthenticationErrorCode.ServiceError,
            message,
            ex);
    }
}
