using System;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.VisualStudio;
using Microsoft.ServiceHub.Framework;

namespace ContextRelay.VSExtension.Services;

/// <summary>Reads the owning Visual Studio session's UI locale, never the OS culture.</summary>
internal sealed class VisualStudioUiLanguageProvider
{
    private readonly IServiceBroker serviceBroker;
    private readonly ContextRelayOutputLogger logger;
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool initialized;
    private int? locale;

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

            // VS applies display-language changes on restart, so cache a successful lookup (including the
            // service's own "unavailable" sentinel) per extension instance. A timeout or thrown exception is
            // treated as transient and is not cached, so a later call can retry once the service recovers.
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
            }
            catch (Exception ex)
            {
                logger.LogError("Unable to read the Visual Studio UI language; automatic UI language falls back to English for now and will retry.", ex);
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
