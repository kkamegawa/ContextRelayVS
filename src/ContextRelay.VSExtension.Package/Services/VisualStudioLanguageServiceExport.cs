using System;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.VisualStudio;
using Microsoft.ServiceHub.Framework;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace ContextRelay.VSExtension.Package.Services;

internal sealed class VisualStudioLanguageServiceImpl : IVisualStudioLanguageService
{
    private readonly int? uiLocale;

    internal VisualStudioLanguageServiceImpl(int? uiLocale) => this.uiLocale = uiLocale;

    public Task<int> GetUiLocaleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(uiLocale ?? -1);
    }
}

internal static class VisualStudioLanguageServiceExport
{
    internal static int? CapturedUiLocale { get; private set; }

    internal static void CaptureUiLocale()
    {
        // This probe is optional: a failure here must fall back to null (English) rather than
        // fail package initialization and broker registration.
        try
        {
            var localeService = ServiceProvider.GlobalProvider.GetService(typeof(SUIHostLocale));
            CapturedUiLocale = localeService is IUIHostLocale3 locale &&
                locale.GetUILocale(out var lcid) == VSConstants.S_OK
                ? unchecked((int)lcid)
                : null;
        }
        catch (Exception)
        {
            CapturedUiLocale = null;
        }
    }

    internal static ValueTask<object?> CreateServiceAsync(
        ServiceMoniker moniker,
        ServiceActivationOptions options,
        IServiceBroker serviceBroker,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<object?>(new VisualStudioLanguageServiceImpl(CapturedUiLocale));
    }
}
