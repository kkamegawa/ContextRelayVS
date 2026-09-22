using System;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.VisualStudio;
using Microsoft.ServiceHub.Framework;

namespace ContextRelay.VSExtension.Services;

/// <summary>Reads the owning Visual Studio session's UI locale, never the OS culture.</summary>
internal sealed class VisualStudioUiLanguageProvider
{
    // GetSettingsSnapshotAsync calls this on every automatic-mode settings read (search, chat, file, and
    // handoff operations all go through it), so a failed lookup must not retry the broker, and its timeout,
    // on every single one of those calls; this cooldown bounds how often a failing lookup is retried while
    // still allowing eventual recovery once the underlying problem clears.
    private static readonly TimeSpan FailureRetryCooldown = TimeSpan.FromSeconds(10);

    // The first automatic-mode read of a VS session races the in-process Options package's on-demand
    // activation (assembly load/JIT plus MEF composition), which is slow immediately after an install or
    // update with no warm JIT/NGen cache. A short budget here made that very first read time out and fall
    // back to English even though the same lookup succeeded well within a second once the package was
    // warm on a later read (Issue #200 verification on a freshly reinstalled VSIX).
    private static readonly TimeSpan BrokerTimeout = TimeSpan.FromSeconds(5);

    private readonly IServiceBroker serviceBroker;
    private readonly ContextRelayOutputLogger logger;
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool initialized;
    private int? locale;
    private DateTime nextRetryUtc = DateTime.MinValue;

    public VisualStudioUiLanguageProvider(IServiceBroker serviceBroker, ContextRelayOutputLogger logger)
    {
        this.serviceBroker = serviceBroker;
        this.logger = logger;
    }

    public async Task<int?> GetUiLocaleAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (initialized)
            {
                return locale;
            }

            if (DateTime.UtcNow < nextRetryUtc)
            {
                // A previous attempt failed recently; fall back to English without hitting the broker (and
                // its timeout) again until the cooldown elapses.
                return null;
            }

            // VS applies display-language changes on restart, so cache a successful lookup permanently per
            // extension instance. A missing proxy, a timeout, or a thrown exception are all treated as
            // transient and are not cached that way, so a later call can retry once the cooldown above
            // elapses instead of caching the failure for the extension's whole lifetime.
            // A missing/unresponsive package must not block opening the tool window.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(BrokerTimeout);
            IVisualStudioLanguageService? proxy = null;
            try
            {
                proxy = await serviceBroker.GetProxyAsync<IVisualStudioLanguageService>(
                    VisualStudioLanguageService.Descriptor, cancellationToken: timeout.Token).ConfigureAwait(false);
                if (proxy is not null)
                {
                    // A successful response is cached permanently, including the service's own "unavailable"
                    // sentinel (-1, meaning the Options package captured no locale) — that is a stable answer
                    // from the package, not a transient failure.
                    locale = await proxy.GetUiLocaleAsync(timeout.Token).ConfigureAwait(false);
                    initialized = true;
                }
                else
                {
                    // A null proxy can mean the service is genuinely not registered, but it can equally mean
                    // the Options package has not finished activating and profferring it yet (a startup race,
                    // not a permanent condition). Treat it like any other transient failure so a later
                    // settings read can still recover, instead of caching English for the rest of the session.
                    logger.LogWarning("Visual Studio UI language service is unavailable; automatic UI language falls back to English for now and will retry.");
                    nextRetryUtc = DateTime.UtcNow + FailureRetryCooldown;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning("Reading the Visual Studio UI language timed out; automatic UI language falls back to English for now and will retry.");
                nextRetryUtc = DateTime.UtcNow + FailureRetryCooldown;
            }
            catch (Exception ex)
            {
                logger.LogError("Unable to read the Visual Studio UI language; automatic UI language falls back to English for now and will retry.", ex);
                nextRetryUtc = DateTime.UtcNow + FailureRetryCooldown;
            }
            finally
            {
                (proxy as IDisposable)?.Dispose();
            }

            return locale;
        }
        finally
        {
            gate.Release();
        }
    }
}
