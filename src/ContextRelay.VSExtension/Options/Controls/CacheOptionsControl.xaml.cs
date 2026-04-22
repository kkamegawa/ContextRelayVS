using System.Windows.Controls;

namespace ContextRelay.VSExtension.Options.Controls;

public partial class CacheOptionsControl : UserControl
{
    public CacheOptionsControl()
    {
        InitializeComponent();
        ApplyLocalization();
    }

    public int TtlSeconds
    {
        get => int.TryParse(TtlSecondsTextBox.Text, out var value) ? value : 300;
        set => TtlSecondsTextBox.Text = value.ToString();
    }

    public int MaxEntries
    {
        get => int.TryParse(MaxEntriesTextBox.Text, out var value) ? value : 200;
        set => MaxEntriesTextBox.Text = value.ToString();
    }

    public bool PersistWorkspaceState
    {
        get => PersistWorkspaceStateCheckBox.IsChecked == true;
        set => PersistWorkspaceStateCheckBox.IsChecked = value;
    }

    private void ApplyLocalization()
    {
        PageTitleText.Text = OptionsLocalizedStrings.CachePageTitle;
        TtlSecondsLabelText.Text = OptionsLocalizedStrings.TtlSecondsLabel;
        TtlSecondsDescriptionText.Text = OptionsLocalizedStrings.TtlSecondsDescription;
        MaxEntriesLabelText.Text = OptionsLocalizedStrings.MaxEntriesLabel;
        MaxEntriesDescriptionText.Text = OptionsLocalizedStrings.MaxEntriesDescription;
        PersistWorkspaceStateLabelText.Text = OptionsLocalizedStrings.PersistWorkspaceStateLabel;
        PersistWorkspaceStateDescriptionText.Text = OptionsLocalizedStrings.PersistWorkspaceStateDescription;
    }
}
