using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using ContextRelay.Core.Settings;
using Microsoft.VisualStudio.Shell;

namespace ContextRelay.VSExtension.Package.Options;

/// <summary>Hosts the SharePoint Embedded settings in Tools &gt; Options.</summary>
[ComVisible(true)]
[Guid(ContextRelayPackageGuids.EmbeddedOptionsPageString)]
public sealed class SharePointEmbeddedOptionsPage : UIElementDialogPage
{
    private EmbeddedOptionsControl? control;
    private bool loadedForDialog;

    protected override UIElement Child => View;

    private EmbeddedOptionsControl View => control ??= new EmbeddedOptionsControl();

    protected override void OnActivate(CancelEventArgs e)
    {
        var settings = ContextRelaySettingsStore.LoadSettings();
        if (!loadedForDialog)
        {
            View.LoadSettings(settings);
            loadedForDialog = true;
        }

        View.RefreshContext(settings);
        base.OnActivate(e);
    }

    protected override void OnApply(PageApplyEventArgs e)
    {
        if (e.ApplyBehavior != ApplyKind.Apply || !loadedForDialog)
        {
            return;
        }

        if (!View.TryGetSettings(out var enabled, out var sources))
        {
            e.ApplyBehavior = ApplyKind.Cancel;
            return;
        }

        try
        {
            var settings = ContextRelaySettingsStore.UpdateSettings(snapshot =>
            {
                snapshot.EmbeddedEnabled = enabled;
                snapshot.EmbeddedSources = sources;
            });
            View.RefreshContext(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
        {
            View.ShowSaveError(ex.Message);
            e.ApplyBehavior = ApplyKind.Cancel;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        // The shell can keep the page alive. The next activation starts with a fresh saved snapshot.
        loadedForDialog = false;
        base.OnClosed(e);
    }
}
