using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;
using Microsoft.Identity.Client.Extensions.Msal;

namespace ContextRelay.Core.Auth.Msal;

public sealed class MsalPublicClientFacadeFactory : IMsalPublicClientFacadeFactory
{
    private readonly MsalCacheConfiguration cacheConfiguration;

    public MsalPublicClientFacadeFactory(MsalCacheConfiguration? cacheConfiguration = null)
    {
        this.cacheConfiguration = cacheConfiguration ?? MsalCacheConfiguration.CreateDefault();
    }

    public async Task<IMsalPublicClientFacade> CreateAsync(
        ContextRelayAuthSettings settings,
        CancellationToken cancellationToken)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        Directory.CreateDirectory(cacheConfiguration.CacheDirectory);

        var application = PublicClientApplicationBuilder
            .Create(settings.ClientId.Trim())
            .WithAuthority(AzureCloudInstance.AzurePublic, NormalizeTenant(settings.TenantId))
            .WithDefaultRedirectUri()
            .WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows))
            .Build();

        try
        {
            var storageProperties = new StorageCreationPropertiesBuilder(
                    cacheConfiguration.CacheFileName,
                    cacheConfiguration.CacheDirectory)
                .Build();

            var cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties).ConfigureAwait(false);
            cacheHelper.RegisterCache(application.UserTokenCache);
        }
        catch (Exception ex)
        {
            throw new ContextRelayAuthenticationException(
                ContextRelayAuthenticationErrorCode.CacheInitializationFailed,
                $"Failed to initialize the MSAL token cache under '{cacheConfiguration.CacheDirectory}'.",
                ex);
        }

        return new MsalPublicClientFacade(application);
    }

    private static string NormalizeTenant(string tenantId)
    {
        return string.IsNullOrWhiteSpace(tenantId) ? "organizations" : tenantId.Trim();
    }
}
