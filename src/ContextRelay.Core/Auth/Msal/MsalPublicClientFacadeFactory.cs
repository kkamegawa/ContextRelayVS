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

        var builder = PublicClientApplicationBuilder
            .Create(settings.ClientId.Trim())
            .WithDefaultRedirectUri();

        if (settings.UseBroker)
        {
            builder.WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows));
        }

        var cloudInstance = MapCloudInstance(settings.CloudEnvironment);
        if (settings.CloudEnvironment == CloudEnvironment.Custom &&
            !string.IsNullOrWhiteSpace(settings.CustomAuthEndpoint))
        {
            var authority = $"{CloudEndpoints.NormalizeEndpoint(settings.CustomAuthEndpoint, CloudEndpoints.GlobalAuth)}/{NormalizeTenant(settings.TenantId)}";
            builder.WithAuthority(authority);
        }
        else
        {
            builder.WithAuthority(cloudInstance, NormalizeTenant(settings.TenantId));
        }

        var application = builder.Build();

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

    internal static AzureCloudInstance MapCloudInstance(CloudEnvironment environment)
    {
        return environment switch
        {
            CloudEnvironment.Global => AzureCloudInstance.AzurePublic,
            CloudEnvironment.USGovGCCHigh => AzureCloudInstance.AzureUsGovernment,
            CloudEnvironment.USGovDoD => AzureCloudInstance.AzureUsGovernment,
            CloudEnvironment.Germany => AzureCloudInstance.AzureGermany,
            CloudEnvironment.China => AzureCloudInstance.AzureChina,
            CloudEnvironment.Custom => AzureCloudInstance.AzurePublic,
            _ => AzureCloudInstance.AzurePublic
        };
    }
}
