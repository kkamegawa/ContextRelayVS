using System.ComponentModel;
using System.Windows;
using ContextRelay.VSExtension.Options.Controls;
using Microsoft.VisualStudio.Shell;

namespace ContextRelay.VSExtension.Options;

public sealed class ContextRelayCacheOptionsPage : UIElementDialogPage
{
    private CacheOptionsControl? control;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public int TtlSeconds { get; set; } = 300;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public int MaxEntries { get; set; } = 200;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public bool PersistWorkspaceState { get; set; } = true;

    protected override UIElement Child => control ??= new CacheOptionsControl();

    protected override void OnActivate(CancelEventArgs e)
    {
        base.OnActivate(e);
        if (control is null)
        {
            return;
        }

        control.TtlSeconds = TtlSeconds;
        control.MaxEntries = MaxEntries;
        control.PersistWorkspaceState = PersistWorkspaceState;
    }

    protected override void OnApply(PageApplyEventArgs e)
    {
        if (e.ApplyBehavior == ApplyKind.Apply && control is not null)
        {
            TtlSeconds = control.TtlSeconds;
            MaxEntries = control.MaxEntries;
            PersistWorkspaceState = control.PersistWorkspaceState;
        }

        base.OnApply(e);
    }
}
