using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.Auth;
using ContextRelay.Core.Auth.Msal;
using Microsoft.Identity.Client;
using Xunit;

namespace ContextRelay.Core.Tests.Auth;

public sealed class MsalAuthProviderTests
{
    [Fact]
    public async Task TryGetAccessTokenSilentAsync_ReturnsNullWhenNoAccountExists()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var facade = new FakeMsalPublicClientFacade();
        var provider = new MsalAuthProvider(new FakeMsalPublicClientFacadeFactory(facade));

        var result = await provider.TryGetAccessTokenSilentAsync(
            new ContextRelayAuthSettings { ClientId = "client-id" },
            new ContextRelayFeatureOptions(),
            cancellationToken);

        Assert.Null(result);
        Assert.Equal(0, facade.InteractiveAcquireCalls);
    }

    [Fact]
    public async Task GetAccessTokenAsync_FallsBackToInteractiveWhenSilentRequiresUi()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var facade = new FakeMsalPublicClientFacade
        {
            Accounts = new[] { new ContextRelayAccountInfo { HomeAccountId = "account-1", Username = "user@contoso.com" } },
            SilentException = new MsalUiRequiredException("ui_required", "Interactive sign-in required."),
            InteractiveToken = new ContextRelayAccessToken { AccessToken = "interactive-token" }
        };
        var provider = new MsalAuthProvider(new FakeMsalPublicClientFacadeFactory(facade));

        var token = await provider.GetAccessTokenAsync(
            new ContextRelayAuthSettings { ClientId = "client-id" },
            new ContextRelayFeatureOptions(),
            cancellationToken);

        Assert.Equal("interactive-token", token.AccessToken);
        Assert.Equal(1, facade.SilentAcquireCalls);
        Assert.Equal(1, facade.InteractiveAcquireCalls);
    }

    [Fact]
    public async Task GetAccessTokenAsync_ThrowsHelpfulErrorWhenClientIdIsMissing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var provider = new MsalAuthProvider(new FakeMsalPublicClientFacadeFactory(new FakeMsalPublicClientFacade()));

        var exception = await Assert.ThrowsAsync<ContextRelayAuthenticationException>(() =>
            provider.GetAccessTokenAsync(new ContextRelayAuthSettings(), new ContextRelayFeatureOptions(), cancellationToken));

        Assert.Equal(ContextRelayAuthenticationErrorCode.MissingClientId, exception.ErrorCode);
        Assert.Contains("contextRelay.auth.clientId", exception.Message);
    }

    private sealed class FakeMsalPublicClientFacadeFactory : IMsalPublicClientFacadeFactory
    {
        private readonly IMsalPublicClientFacade facade;

        public FakeMsalPublicClientFacadeFactory(IMsalPublicClientFacade facade)
        {
            this.facade = facade;
        }

        public Task<IMsalPublicClientFacade> CreateAsync(ContextRelayAuthSettings settings, CancellationToken cancellationToken)
        {
            return Task.FromResult(facade);
        }
    }

    private sealed class FakeMsalPublicClientFacade : IMsalPublicClientFacade
    {
        public IReadOnlyList<ContextRelayAccountInfo> Accounts { get; set; } = Array.Empty<ContextRelayAccountInfo>();

        public Exception? SilentException { get; set; }

        public ContextRelayAccessToken SilentToken { get; set; } = new() { AccessToken = "silent-token" };

        public ContextRelayAccessToken InteractiveToken { get; set; } = new() { AccessToken = "interactive-token" };

        public int SilentAcquireCalls { get; private set; }

        public int InteractiveAcquireCalls { get; private set; }

        public Task<IReadOnlyList<ContextRelayAccountInfo>> GetAccountsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Accounts);
        }

        public Task<ContextRelayAccessToken> AcquireTokenSilentAsync(
            IReadOnlyList<string> scopes,
            string homeAccountId,
            CancellationToken cancellationToken)
        {
            SilentAcquireCalls++;
            if (SilentException is not null)
            {
                throw SilentException;
            }

            return Task.FromResult(SilentToken);
        }

        public Task<ContextRelayAccessToken> AcquireTokenInteractiveAsync(
            IReadOnlyList<string> scopes,
            CancellationToken cancellationToken)
        {
            InteractiveAcquireCalls++;
            return Task.FromResult(InteractiveToken);
        }
    }
}
