using System.Threading;
using System.Threading.Tasks;

namespace ContextRelay.Core.Auth.Msal;

public interface IMsalPublicClientFacadeFactory
{
    Task<IMsalPublicClientFacade> CreateAsync(
        ContextRelayAuthSettings settings,
        CancellationToken cancellationToken);
}
