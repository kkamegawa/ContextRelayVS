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

            // VS applies display-language changes on restart, so cache per extension instance.
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
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError("Unable to read the Visual Studio UI language; automatic UI language falls back to English.", ex);
            }
            finally
            {
                (proxy as IDisposable)?.Dispose();
            }

            initialized = true;
            return locale;
        }
        finally
        {
            gate.Release();
        }
    }
}
