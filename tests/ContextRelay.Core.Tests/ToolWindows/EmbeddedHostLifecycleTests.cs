using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.Adapters;
using ContextRelay.Core.Auth;
using ContextRelay.Core.Chat;
using ContextRelay.Core.Embedded;
using ContextRelay.Core.FileContext;
using ContextRelay.Core.Models;
using ContextRelay.Core.Router;
using ContextRelay.Core.Settings;
using ContextRelay.Core.SharedStore;
using ContextRelay.Core.Snippets;
using Xunit;

namespace ContextRelay.Core.Tests.ToolWindows;

[Collection(ContextRelayLocalizedStringsStateCollection.Name)]
public sealed class EmbeddedHostLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopDuringAuthentication_DoesNotRetrieveEvenWhenProviderIgnoresCancellation(bool honorCancellation)
    {
        using var fixture = new HostFixture();
        fixture.Auth.HonorCancellation = honorCancellation;
        var pending = fixture.SearchAsync(TestContext.Current.CancellationToken);
        await fixture.Auth.Started.Task.WaitAsync(TestContext.Current.CancellationToken);

        fixture.Invoke("StopGeneration");
        Assert.True(fixture.Auth.RequestToken.IsCancellationRequested);
        fixture.Auth.Release.TrySetResult(true);
        var result = await pending;

        Assert.Equal(0, fixture.Handler.CallCount);
        Assert.Null(fixture.GetField("activeEmbeddedRequestCancellation"));
        var status = Assert.IsType<string>(result.GetType().GetProperty("StatusMessage")!.GetValue(result));
        Assert.Contains("stopp", status, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Authentication_PropagatesCallerAndDisposalCancellation(bool disposing)
    {
        using var fixture = new HostFixture();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var pending = fixture.SearchAsync(caller.Token);
        await fixture.Auth.Started.Task.WaitAsync(TestContext.Current.CancellationToken);

        if (disposing)
        {
            fixture.Disposal.Cancel();
        }
        else
        {
            caller.Cancel();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(0, fixture.Handler.CallCount);
        Assert.Null(fixture.GetField("activeEmbeddedRequestCancellation"));
    }

    [Fact]
    public async Task InitialStateRefresh_DoesNotStartAuthenticationBeforeDeferredLookup()
    {
        using var fixture = new HostFixture();

        await fixture.InvokeAsync("GetStateAsync");

        Assert.False(((Lazy<IContextRelayAuthProvider>)fixture.GetField("authProvider")!).IsValueCreated);
    }

    [Fact]
    public async Task AccountRefresh_SignOutClearsResultsAndCacheBeforeSameAccountSignsInAgain()
    {
        using var fixture = new HostFixture();
        // Existing results belong to a session whose auth provider has already been initialized.
        _ = ((Lazy<IContextRelayAuthProvider>)fixture.GetField("authProvider")!).Value;
        fixture.Service.ObserveAccount("account-1");
        var first = await fixture.Service.SearchAsync(fixture.CreateRequest(), TestContext.Current.CancellationToken);
        fixture.SetField("currentSearchResults", new[] { first.Items[0], new ContextItem { Source = ContextSource.Mail, Title = "mail" } });
        fixture.SetField("lastSearchRoute", SlashCommandRouter.Parse("/embedded renewal"));
        fixture.SetField("lastSearchSummary", "old Embedded summary");
        fixture.Auth.Account = null;

        var state = await fixture.InvokeAsync("GetStateAsync");

        Assert.Equal(0, fixture.Service.CachedEntryCount);
        Assert.Null(state.GetType().GetProperty("SignedInUser")!.GetValue(state));
        var results = Assert.IsAssignableFrom<IReadOnlyList<ContextItem>>(state.GetType().GetProperty("SearchResults")!.GetValue(state));
        Assert.Equal(ContextSource.Mail, Assert.Single(results).Source);
        Assert.Equal(string.Empty, state.GetType().GetProperty("SearchSummary")!.GetValue(state));

        fixture.Auth.Account = new ContextRelayAccountInfo { HomeAccountId = "account-1", Username = "user" };
        await fixture.InvokeAsync("GetStateAsync");
        var next = await fixture.Service.SearchAsync(fixture.CreateRequest(), TestContext.Current.CancellationToken);
        Assert.False(next.FromCache);
        Assert.Equal(2, fixture.Handler.CallCount);
    }

    private sealed class HostFixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "ContextRelay-EmbeddedHost-" + Guid.NewGuid().ToString("N"));
        private readonly Type hostType;
        private readonly object host;
        private readonly Type stringsType;
        private readonly object originalLanguage;
        private readonly HttpClient http;
        private readonly SemaphoreSlim gate = new(1, 1);
        private readonly ChatRequestStateMachine chat = new();

        public HostFixture()
        {
            var assembly = Assembly.LoadFrom(BuiltExtensionArtifactLocator.ResolveExtensionArtifactPath("ContextRelay.VSExtension.dll"));
            hostType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHost", true)!;
            host = RuntimeHelpers.GetUninitializedObject(hostType);
            var packageType = assembly.GetType("ContextRelay.VSExtension.Services.IContextRelayPackageServices", true)!;
            var package = DispatchProxy.Create(packageType, typeof(PackageProxy));
            var settings = ((PackageProxy)package).Settings;
            settings.ClientId = "11111111-2222-3333-4444-555555555555";
            settings.EmbeddedEnabled = true;
            settings.EmbeddedSources.Add(new EmbeddedSourceSettings { Name = "Sample", ContainerTypeId = "11111111-2222-3333-4444-555555555555", Enabled = true });
            stringsType = assembly.GetType("ContextRelay.VSExtension.ToolWindows.ContextRelayLocalizedStrings", true)!;
            originalLanguage = stringsType.GetProperty("CurrentUiLanguage")!.GetValue(null)!;
            stringsType.GetMethod("SetUiLanguage")!.Invoke(null, new object[] { "en" });
            settings.UiLanguage = "en";
            http = new HttpClient(Handler);
            var graph = new GraphHttpClient(http);
            Service = new EmbeddedSearchService(new EmbeddedRetrievalAdapter(new RetrievalClient(graph)));
            var store = new FileSystemSharedSessionStore(new SharedStoreOptions { RootDirectory = root });
            SetField("packageServices", package);
            SetField("logger", RuntimeHelpers.GetUninitializedObject(assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayOutputLogger", true)!));
            SetField("authProvider", new Lazy<IContextRelayAuthProvider>(() => Auth));
            SetField("graphClient", graph);
            SetField("embeddedSearchService", Service);
            SetField("sharedStore", store);
            SetField("snippetRepository", new SharedSnippetRepository(store));
            SetField("state", Activator.CreateInstance(assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHostState", true)!, true)!);
            SetField("gate", gate);
            SetField("disposeCancellation", Disposal);
            SetField("activeChatRequestSync", new object());
            SetField("chatRequestStateMachine", chat);
            SetField("draftQuerySync", new object());
            SetField("draftQueryText", string.Empty);
            SetField("pendingAttachmentsSync", new object());
            SetField("pendingAttachments", new List<ResolvedAttachment>());
            SetField("currentSearchResults", Array.Empty<ContextItem>());
        }

        public TestAuth Auth { get; } = new();
        public CountingHandler Handler { get; } = new();
        public EmbeddedSearchService Service { get; }
        public CancellationTokenSource Disposal { get; } = new();

        public Task<object> SearchAsync(CancellationToken token) => InvokeAsync("HandleEmbeddedCommandAsync", "/embedded renewal", SlashCommandRouter.Parse("/embedded renewal"), token);
        public object? GetField(string name) => hostType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host);
        public void SetField(string name, object value) => hostType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, value);
        public object? Invoke(string name, params object[] args) => hostType.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(host, args);

        public async Task<object> InvokeAsync(string name, params object[] args)
        {
            var task = (Task)Invoke(name, args)!;
            await task;
            return task.GetType().GetProperty("Result")!.GetValue(task)!;
        }

        public EmbeddedSearchRequest CreateRequest() => new()
        {
            AccessToken = "test-token", AccountKey = "account-1", EndpointKey = "global",
            Source = new EmbeddedSourceSettings { Name = "Sample", ContainerTypeId = "11111111-2222-3333-4444-555555555555" },
            Query = "renewal", MaxResults = 10
        };

        public void Dispose()
        {
            stringsType.GetMethod("SetUiLanguage")!.Invoke(null, new[] { originalLanguage });
            chat.Dispose();
            gate.Dispose();
            Disposal.Dispose();
            http.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    public class PackageProxy : DispatchProxy
    {
        public ContextRelaySettingsSnapshot Settings { get; } = new();

        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
        {
            "GetSettingsSnapshotAsync" => Task.FromResult(Settings),
            "GetSolutionRootAsync" => Task.FromResult<string?>(null),
            "GetWorkspaceFilesAsync" => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()),
            _ => throw new NotSupportedException(method.Name)
        };
    }

    private sealed class TestAuth : IContextRelayAuthProvider
    {
        public bool HonorCancellation { get; set; } = true;
        public ContextRelayAccountInfo? Account { get; set; } = new() { HomeAccountId = "account-1", Username = "user" };
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken RequestToken { get; private set; }
        public IReadOnlyList<string> GetRequiredScopes(ContextRelayFeatureOptions options) => Array.Empty<string>();
        public Task<ContextRelayAccountInfo?> GetAccountAsync(ContextRelayAuthSettings settings, CancellationToken cancellationToken = default) => Task.FromResult(Account);
        public Task<ContextRelayAccessToken?> TryGetAccessTokenSilentAsync(ContextRelayAuthSettings settings, ContextRelayFeatureOptions options, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ContextRelayAccessToken> GetAccessTokenAsync(ContextRelayAuthSettings settings, ContextRelayFeatureOptions options, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ContextRelayAccessToken> GetWorkIqAccessTokenAsync(ContextRelayAuthSettings settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public async Task<ContextRelayAccessToken> GetEmbeddedAccessTokenAsync(ContextRelayAuthSettings settings, CancellationToken cancellationToken = default)
        {
            RequestToken = cancellationToken;
            Started.TrySetResult(true);
            if (HonorCancellation) await Release.Task.WaitAsync(cancellationToken);
            else await Release.Task;
            return new ContextRelayAccessToken { AccessToken = "test-token", Account = Account };
        }
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"retrievalHits\":[{\"webUrl\":\"https://example.test/document\",\"extracts\":[{\"text\":\"sample\"}],\"resourceMetadata\":{\"title\":\"sample\"}}]}", Encoding.UTF8, "application/json")
            });
        }
    }
}
