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

            // VS applies display-language changes on restart, so cache a successful lookup (including the
            // service's own "unavailable" sentinel) per extension instance. A timeout or thrown exception is
            // treated as transient and is not cached that way, so a later call can retry once the cooldown
            // above elapses instead of caching the failure for the extension's whole lifetime.
            // A missing/unresponsive package must not block opening the tool window.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(1500));
            IVisualStudioLanguageService? proxy = null;
            try
            {
                proxy = await serviceBroker.GetProxyAsync<IVisualStudioLanguageService>(
                    VisualStudioLanguageService.Descriptor, cancellationToken: timeout.Token).ConfigureAwait(false);
                if (proxy is not null)
                {
                    locale = await proxy.GetUiLocaleAsync(timeout.Token).ConfigureAwait(false);
                }
                else
                {
                    logger.LogWarning("Visual Studio UI language service is unavailable; automatic UI language falls back to English.");
                }

                initialized = true;
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
