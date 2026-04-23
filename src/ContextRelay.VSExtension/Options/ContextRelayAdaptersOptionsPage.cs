using System.ComponentModel;
using System.Windows;
using ContextRelay.VSExtension.Options.Controls;
using Microsoft.VisualStudio.Shell;

namespace ContextRelay.VSExtension.Options;

public sealed class ContextRelayAdaptersOptionsPage : UIElementDialogPage
{
    private AdaptersOptionsControl? control;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public bool Mail { get; set; } = true;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public bool Teams { get; set; } = true;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public bool SharePoint { get; set; } = true;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public bool OneDrive { get; set; } = true;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public bool Connectors { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public bool OneNote { get; set; } = true;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public bool Planner { get; set; } = true;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public bool Todo { get; set; } = true;

    protected override UIElement Child => control ??= new AdaptersOptionsControl();

    protected override void OnActivate(CancelEventArgs e)
    {
        base.OnActivate(e);
        if (control is null)
        {
            return;
        }

        control.Mail = Mail;
        control.Teams = Teams;
        control.SharePoint = SharePoint;
        control.OneDrive = OneDrive;
        control.Connectors = Connectors;
        control.OneNote = OneNote;
        control.Planner = Planner;
        control.Todo = Todo;
    }

    protected override void OnApply(PageApplyEventArgs e)
    {
        if (e.ApplyBehavior == ApplyKind.Apply && control is not null)
        {
            Mail = control.Mail;
            Teams = control.Teams;
            SharePoint = control.SharePoint;
            OneDrive = control.OneDrive;
            Connectors = control.Connectors;
            OneNote = control.OneNote;
            Planner = control.Planner;
            Todo = control.Todo;
        }

        base.OnApply(e);
    }
}
