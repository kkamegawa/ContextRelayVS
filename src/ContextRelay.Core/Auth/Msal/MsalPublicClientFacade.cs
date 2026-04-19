using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Identity.Client;

namespace ContextRelay.Core.Auth.Msal;

public sealed class MsalPublicClientFacade : IMsalPublicClientFacade
{
    private readonly IPublicClientApplication publicClientApplication;

    public MsalPublicClientFacade(IPublicClientApplication publicClientApplication)
    {
        this.publicClientApplication = publicClientApplication;
    }

    public async Task<IReadOnlyList<ContextRelayAccountInfo>> GetAccountsAsync(CancellationToken cancellationToken)
    {
        var accounts = await publicClientApplication.GetAccountsAsync().ConfigureAwait(false);
        return accounts
            .Select(account => new ContextRelayAccountInfo
            {
                HomeAccountId = account.HomeAccountId?.Identifier ?? string.Empty,
                Username = account.Username ?? string.Empty,
                Environment = account.Environment
            })
            .Where(account => !string.IsNullOrWhiteSpace(account.HomeAccountId))
            .ToArray();
    }

    public async Task<ContextRelayAccessToken> AcquireTokenSilentAsync(
        IReadOnlyList<string> scopes,
        string homeAccountId,
        CancellationToken cancellationToken)
    {
        var account = await ResolveAccountAsync(homeAccountId).ConfigureAwait(false);
        var result = await publicClientApplication
            .AcquireTokenSilent(scopes, account)
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);

        return Map(result);
    }

    public async Task<ContextRelayAccessToken> AcquireTokenInteractiveAsync(
        IReadOnlyList<string> scopes,
        CancellationToken cancellationToken)
    {
        var result = await publicClientApplication
            .AcquireTokenInteractive(scopes)
            .WithPrompt(Prompt.SelectAccount)
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);

        return Map(result);
    }

    private async Task<IAccount> ResolveAccountAsync(string homeAccountId)
    {
        var accounts = await publicClientApplication.GetAccountsAsync().ConfigureAwait(false);
        var account = accounts.FirstOrDefault(candidate => candidate.HomeAccountId?.Identifier == homeAccountId);
        if (account is null)
        {
            throw new MsalUiRequiredException("no_account", "No cached account matched the requested home account identifier.");
        }

        return account;
    }

    private static ContextRelayAccessToken Map(AuthenticationResult result)
    {
        return new ContextRelayAccessToken
        {
            AccessToken = result.AccessToken,
            ExpiresOn = result.ExpiresOn,
            Account = result.Account is null
                ? null
                : new ContextRelayAccountInfo
                {
                    HomeAccountId = result.Account.HomeAccountId?.Identifier ?? string.Empty,
                    Username = result.Account.Username ?? string.Empty,
                    Environment = result.Account.Environment
                }
        };
    }
}
