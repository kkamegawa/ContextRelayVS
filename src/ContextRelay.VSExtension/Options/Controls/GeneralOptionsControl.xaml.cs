using System.Windows.Controls;

namespace ContextRelay.VSExtension.Options.Controls;

public partial class GeneralOptionsControl : UserControl
{
    public GeneralOptionsControl()
    {
        InitializeComponent();
        ApplyLocalization();
    }

    public int MaxResults
    {
        get => int.TryParse(MaxResultsTextBox.Text, out var value) ? value : 10;
        set => MaxResultsTextBox.Text = value.ToString();
    }

    public string OutputDirectory
    {
        get => OutputDirectoryTextBox.Text;
        set => OutputDirectoryTextBox.Text = value ?? string.Empty;
    }

    public bool EnableChatPreview
    {
        get => EnableChatPreviewCheckBox.IsChecked == true;
        set => EnableChatPreviewCheckBox.IsChecked = value;
    }

    public bool EnableGraphDebugLogging
    {
        get => EnableGraphDebugLoggingCheckBox.IsChecked == true;
        set => EnableGraphDebugLoggingCheckBox.IsChecked = value;
    }

    public bool EnableWorkIqDebugLogging
    {
        get => EnableWorkIqDebugLoggingCheckBox.IsChecked == true;
        set => EnableWorkIqDebugLoggingCheckBox.IsChecked = value;
    }

    private void ApplyLocalization()
    {
        PageTitleText.Text = OptionsLocalizedStrings.GeneralPageTitle;
        MaxResultsLabelText.Text = OptionsLocalizedStrings.MaxResultsLabel;
        MaxResultsDescriptionText.Text = OptionsLocalizedStrings.MaxResultsDescription;
        OutputDirectoryLabelText.Text = OptionsLocalizedStrings.OutputDirectoryLabel;
        OutputDirectoryDescriptionText.Text = OptionsLocalizedStrings.OutputDirectoryDescription;
        EnableChatPreviewLabelText.Text = OptionsLocalizedStrings.EnableChatPreviewLabel;
        EnableChatPreviewDescriptionText.Text = OptionsLocalizedStrings.EnableChatPreviewDescription;
        EnableGraphDebugLoggingLabelText.Text = OptionsLocalizedStrings.EnableGraphDebugLoggingLabel;
        EnableGraphDebugLoggingDescriptionText.Text = OptionsLocalizedStrings.EnableGraphDebugLoggingDescription;
        EnableWorkIqDebugLoggingLabelText.Text = OptionsLocalizedStrings.EnableWorkIqDebugLoggingLabel;
        EnableWorkIqDebugLoggingDescriptionText.Text = OptionsLocalizedStrings.EnableWorkIqDebugLoggingDescription;
    }
}
