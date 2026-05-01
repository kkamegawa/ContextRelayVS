using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using ContextRelay.Core.Auth;
using ContextRelay.VSExtension.Options.Controls;
using Microsoft.VisualStudio.Shell;

namespace ContextRelay.VSExtension.Options;

public sealed class ContextRelayGraphApiOptionsPage : UIElementDialogPage
{
    private GraphApiOptionsControl? control;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public CloudEnvironment CloudEnvironment { get; set; } = CloudEnvironment.Global;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public string CustomGraphEndpoint { get; set; } = string.Empty;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public string CustomAuthEndpoint { get; set; } = string.Empty;

    protected override UIElement Child => control ??= new GraphApiOptionsControl();

    protected override void OnActivate(CancelEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        base.OnActivate(e);
        if (control is null)
        {
            return;
        }

        control.RequiredScopesProvider = BuildScopeSummary;
        control.CloudEnvironment = CloudEnvironment;
        control.CustomGraphEndpoint = CustomGraphEndpoint;
        control.CustomAuthEndpoint = CustomAuthEndpoint;
    }

    protected override void OnApply(PageApplyEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (e.ApplyBehavior == ApplyKind.Apply && control is not null)
        {
            CloudEnvironment = control.CloudEnvironment;
            CustomGraphEndpoint = control.CustomGraphEndpoint;
            CustomAuthEndpoint = control.CustomAuthEndpoint;
        }

        base.OnApply(e);
    }

    private string BuildScopeSummary(CloudEnvironment cloudEnvironment, string customGraphEndpoint, string customAuthEndpoint)
    {
        try
        {
            var package = ContextRelayPackage.Instance;
            if (package is null)
            {
                return string.Empty;
            }

            ThreadHelper.ThrowIfNotOnUIThread();
            var adapters = (ContextRelayAdaptersOptionsPage)package.GetDialogPage(typeof(ContextRelayAdaptersOptionsPage));
            var general = (ContextRelayGeneralOptionsPage)package.GetDialogPage(typeof(ContextRelayGeneralOptionsPage));
            var featureOptions = new ContextRelayFeatureOptions
            {
                MailEnabled = adapters.Mail,
                TeamsEnabled = adapters.Teams,
                SharePointEnabled = adapters.SharePoint,
                OneDriveEnabled = adapters.OneDrive,
                ConnectorsEnabled = adapters.Connectors,
                OneNoteEnabled = adapters.OneNote,
                PlannerEnabled = adapters.Planner,
                TodoEnabled = adapters.Todo,
                ChatPreviewEnabled = general.EnableChatPreview
            };

            var graphEndpoint = CloudEndpoints.GetGraphEndpoint(cloudEnvironment, customGraphEndpoint);
            var graphScopes = AuthScopeCatalog.BuildQualifiedGraphScopes(featureOptions, graphResource: graphEndpoint);
            var workIqScopes = AuthScopeCatalog.BuildWorkIqScopes();
            return string.Join(
                Environment.NewLine,
                graphScopes
                    .Concat(new[] { string.Empty, OptionsLocalizedStrings.WorkIqScopesSectionLabel })
                    .Concat(workIqScopes));
        }
        catch
        {
            return string.Empty;
        }
    }
}
