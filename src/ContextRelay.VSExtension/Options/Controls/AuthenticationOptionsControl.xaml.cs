using System.Windows.Controls;

namespace ContextRelay.VSExtension.Options.Controls;

public partial class AuthenticationOptionsControl : UserControl
{
    public AuthenticationOptionsControl()
    {
        InitializeComponent();
        ApplyLocalization();
    }

    public string ClientId
    {
        get => ClientIdTextBox.Text;
        set => ClientIdTextBox.Text = value ?? string.Empty;
    }

    public string TenantId
    {
        get => TenantIdTextBox.Text;
        set => TenantIdTextBox.Text = value ?? string.Empty;
    }

    private void ApplyLocalization()
    {
        PageTitleText.Text = OptionsLocalizedStrings.AuthenticationPageTitle;
        ClientIdLabelText.Text = OptionsLocalizedStrings.ClientIdLabel;
        ClientIdDescriptionText.Text = OptionsLocalizedStrings.ClientIdDescription;
        TenantIdLabelText.Text = OptionsLocalizedStrings.TenantIdLabel;
        TenantIdDescriptionText.Text = OptionsLocalizedStrings.TenantIdDescription;
    }
}
