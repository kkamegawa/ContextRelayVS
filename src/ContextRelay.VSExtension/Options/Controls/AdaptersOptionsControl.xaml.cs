using System.Windows.Controls;

namespace ContextRelay.VSExtension.Options.Controls;

public partial class AdaptersOptionsControl : UserControl
{
    public AdaptersOptionsControl()
    {
        InitializeComponent();
        ApplyLocalization();
    }

    public bool Mail
    {
        get => MailCheckBox.IsChecked == true;
        set => MailCheckBox.IsChecked = value;
    }

    public bool Teams
    {
        get => TeamsCheckBox.IsChecked == true;
        set => TeamsCheckBox.IsChecked = value;
    }

    public bool SharePoint
    {
        get => SharePointCheckBox.IsChecked == true;
        set => SharePointCheckBox.IsChecked = value;
    }

    public bool OneDrive
    {
        get => OneDriveCheckBox.IsChecked == true;
        set => OneDriveCheckBox.IsChecked = value;
    }

    public bool Connectors
    {
        get => ConnectorsCheckBox.IsChecked == true;
        set => ConnectorsCheckBox.IsChecked = value;
    }

    public bool OneNote
    {
        get => OneNoteCheckBox.IsChecked == true;
        set => OneNoteCheckBox.IsChecked = value;
    }

    public bool Planner
    {
        get => PlannerCheckBox.IsChecked == true;
        set => PlannerCheckBox.IsChecked = value;
    }

    public bool Todo
    {
        get => TodoCheckBox.IsChecked == true;
        set => TodoCheckBox.IsChecked = value;
    }

    private void ApplyLocalization()
    {
        PageTitleText.Text = OptionsLocalizedStrings.AdaptersPageTitle;
        MailLabelText.Text = OptionsLocalizedStrings.MailLabel;
        MailDescriptionText.Text = OptionsLocalizedStrings.MailDescription;
        TeamsLabelText.Text = OptionsLocalizedStrings.TeamsLabel;
        TeamsDescriptionText.Text = OptionsLocalizedStrings.TeamsDescription;
        SharePointLabelText.Text = OptionsLocalizedStrings.SharePointLabel;
        SharePointDescriptionText.Text = OptionsLocalizedStrings.SharePointDescription;
        OneDriveLabelText.Text = OptionsLocalizedStrings.OneDriveLabel;
        OneDriveDescriptionText.Text = OptionsLocalizedStrings.OneDriveDescription;
        ConnectorsLabelText.Text = OptionsLocalizedStrings.ConnectorsLabel;
        ConnectorsDescriptionText.Text = OptionsLocalizedStrings.ConnectorsDescription;
        OneNoteLabelText.Text = OptionsLocalizedStrings.OneNoteLabel;
        OneNoteDescriptionText.Text = OptionsLocalizedStrings.OneNoteDescription;
        PlannerLabelText.Text = OptionsLocalizedStrings.PlannerLabel;
        PlannerDescriptionText.Text = OptionsLocalizedStrings.PlannerDescription;
        TodoLabelText.Text = OptionsLocalizedStrings.TodoLabel;
        TodoDescriptionText.Text = OptionsLocalizedStrings.TodoDescription;
    }
}
