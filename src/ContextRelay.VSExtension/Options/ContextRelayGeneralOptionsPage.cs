using System.ComponentModel;
using System.Windows;
using ContextRelay.VSExtension.Options.Controls;
using Microsoft.VisualStudio.Shell;

namespace ContextRelay.VSExtension.Options;

public sealed class ContextRelayGeneralOptionsPage : UIElementDialogPage
{
    private GeneralOptionsControl? control;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public int MaxResults { get; set; } = 10;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public string OutputDirectory { get; set; } = ".contextrelay";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public bool EnableChatPreview { get; set; } = true;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public bool EnableGraphDebugLogging { get; set; }

    protected override UIElement Child => control ??= new GeneralOptionsControl();

    protected override void OnActivate(CancelEventArgs e)
    {
        base.OnActivate(e);
        if (control is null)
        {
            return;
        }

        control.MaxResults = MaxResults;
        control.OutputDirectory = OutputDirectory;
        control.EnableChatPreview = EnableChatPreview;
        control.EnableGraphDebugLogging = EnableGraphDebugLogging;
    }

    protected override void OnApply(PageApplyEventArgs e)
    {
        if (e.ApplyBehavior == ApplyKind.Apply && control is not null)
        {
            MaxResults = control.MaxResults;
            OutputDirectory = control.OutputDirectory;
            EnableChatPreview = control.EnableChatPreview;
            EnableGraphDebugLogging = control.EnableGraphDebugLogging;
        }

        base.OnApply(e);
    }
}
