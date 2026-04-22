using System;
using System.Windows;
using System.Windows.Controls;
using ContextRelay.Core.Auth;

namespace ContextRelay.VSExtension.Options.Controls;

public partial class GraphApiOptionsControl : UserControl
{
    private bool suppressSelectionChanged;
    private Func<CloudEnvironment, string, string, string>? requiredScopesProvider;

    public GraphApiOptionsControl()
    {
        InitializeComponent();
        ApplyLocalization();
        PopulateCloudEnvironments();
    }

    public Func<CloudEnvironment, string, string, string>? RequiredScopesProvider
    {
        get => requiredScopesProvider;
        set
        {
            requiredScopesProvider = value;
            UpdateRequiredScopes();
        }
    }

    public CloudEnvironment CloudEnvironment
    {
        get
        {
            if (CloudEnvironmentComboBox.SelectedItem is CloudEnvironmentItem item)
            {
                return item.Value;
            }

            return CloudEnvironment.Global;
        }
        set
        {
            suppressSelectionChanged = true;
            try
            {
                foreach (CloudEnvironmentItem item in CloudEnvironmentComboBox.Items)
                {
                    if (item.Value == value)
                    {
                        CloudEnvironmentComboBox.SelectedItem = item;
                        break;
                    }
                }
            }
            finally
            {
                suppressSelectionChanged = false;
            }

            UpdateCustomEndpointVisibility();
            UpdateComputedValues();
        }
    }

    public string CustomGraphEndpoint
    {
        get => CustomGraphEndpointTextBox.Text;
        set
        {
            CustomGraphEndpointTextBox.Text = value ?? string.Empty;
            UpdateComputedValues();
        }
    }

    public string CustomAuthEndpoint
    {
        get => CustomAuthEndpointTextBox.Text;
        set
        {
            CustomAuthEndpointTextBox.Text = value ?? string.Empty;
            UpdateComputedValues();
        }
    }

    public string RequiredScopes
    {
        get => RequiredScopesTextBox.Text;
        set => RequiredScopesTextBox.Text = value ?? string.Empty;
    }

    private void ApplyLocalization()
    {
        PageTitleText.Text = OptionsLocalizedStrings.GraphApiPageTitle;
        CloudEnvironmentLabelText.Text = OptionsLocalizedStrings.CloudEnvironmentLabel;
        CloudEnvironmentDescriptionText.Text = OptionsLocalizedStrings.CloudEnvironmentDescription;
        CustomGraphEndpointLabelText.Text = OptionsLocalizedStrings.CustomGraphEndpointLabel;
        CustomGraphEndpointDescriptionText.Text = OptionsLocalizedStrings.CustomGraphEndpointDescription;
        CustomAuthEndpointLabelText.Text = OptionsLocalizedStrings.CustomAuthEndpointLabel;
        CustomAuthEndpointDescriptionText.Text = OptionsLocalizedStrings.CustomAuthEndpointDescription;
        EffectiveGraphEndpointLabelText.Text = OptionsLocalizedStrings.EffectiveGraphEndpointLabel;
        EffectiveAuthEndpointLabelText.Text = OptionsLocalizedStrings.EffectiveAuthEndpointLabel;
        RequiredScopesLabelText.Text = OptionsLocalizedStrings.RequiredScopesLabel;
        RequiredScopesDescriptionText.Text = OptionsLocalizedStrings.RequiredScopesDescription;
    }

    private void PopulateCloudEnvironments()
    {
        foreach (CloudEnvironment env in Enum.GetValues(typeof(CloudEnvironment)))
        {
            CloudEnvironmentComboBox.Items.Add(new CloudEnvironmentItem
            {
                Value = env,
                DisplayName = CloudEndpoints.GetDisplayName(env)
            });
        }

        CloudEnvironmentComboBox.SelectedIndex = 0;
    }

    private void CloudEnvironmentComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressSelectionChanged)
        {
            return;
        }

        UpdateCustomEndpointVisibility();
        UpdateComputedValues();
    }

    private void CustomEndpointTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateComputedValues();
    }

    private void UpdateCustomEndpointVisibility()
    {
        CustomEndpointPanel.Visibility = CloudEnvironment == CloudEnvironment.Custom
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateEffectiveEndpoints()
    {
        EffectiveGraphEndpointTextBox.Text = CloudEndpoints.GetGraphEndpoint(
            CloudEnvironment, CustomGraphEndpointTextBox.Text);
        EffectiveAuthEndpointTextBox.Text = CloudEndpoints.GetAuthEndpoint(
            CloudEnvironment, CustomAuthEndpointTextBox.Text);
    }

    private void UpdateRequiredScopes()
    {
        RequiredScopesTextBox.Text = requiredScopesProvider?.Invoke(
            CloudEnvironment,
            CustomGraphEndpointTextBox.Text,
            CustomAuthEndpointTextBox.Text) ?? string.Empty;
    }

    private void UpdateComputedValues()
    {
        UpdateEffectiveEndpoints();
        UpdateRequiredScopes();
    }

    private sealed class CloudEnvironmentItem
    {
        public CloudEnvironment Value { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public override string ToString() => DisplayName;
    }
}
