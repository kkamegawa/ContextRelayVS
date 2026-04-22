using System.ComponentModel;
using System.Windows;
using ContextRelay.VSExtension.Options.Controls;
using Microsoft.VisualStudio.Shell;

namespace ContextRelay.VSExtension.Options;

public sealed class ContextRelayAuthenticationOptionsPage : UIElementDialogPage
{
    private AuthenticationOptionsControl? control;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public string ClientId { get; set; } = string.Empty;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public string TenantId { get; set; } = "organizations";

    protected override UIElement Child => control ??= new AuthenticationOptionsControl();

    protected override void OnActivate(CancelEventArgs e)
    {
        base.OnActivate(e);
        if (control is null)
        {
            return;
        }

        control.ClientId = ClientId;
        control.TenantId = TenantId;
    }

    protected override void OnApply(PageApplyEventArgs e)
    {
        if (e.ApplyBehavior == ApplyKind.Apply && control is not null)
        {
            ClientId = control.ClientId;
            TenantId = control.TenantId;
        }

        base.OnApply(e);
    }
}
