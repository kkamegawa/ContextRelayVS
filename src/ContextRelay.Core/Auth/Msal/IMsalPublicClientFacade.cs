using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ContextRelay.Core.Auth.Msal;

public interface IMsalPublicClientFacade
{
    Task<IReadOnlyList<ContextRelayAccountInfo>> GetAccountsAsync(CancellationToken cancellationToken);

    Task<ContextRelayAccessToken> AcquireTokenSilentAsync(
        IReadOnlyList<string> scopes,
        string homeAccountId,
        CancellationToken cancellationToken);

    Task<ContextRelayAccessToken> AcquireTokenInteractiveAsync(
        IReadOnlyList<string> scopes,
        CancellationToken cancellationToken);
}
