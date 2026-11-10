using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Windows;
using System.Windows.Controls;
using ContextRelay.Core.Auth;
using ContextRelay.Core.Settings;

namespace ContextRelay.VSExtension.Package.Options;

internal partial class EmbeddedOptionsControl : UserControl
{
    private static readonly ResourceManager Strings = new(
        "ContextRelay.VSExtension.Package.Options.EmbeddedOptionsStrings",
        typeof(EmbeddedOptionsControl).Assembly);

    private readonly List<EmbeddedSourceSettings> sources = new();
    private LocalizedText text = new(OptionsLocalization.ResolveCulture());
    private int editingIndex;

    internal EmbeddedOptionsControl()
    {
        InitializeComponent();
    }

    internal void LoadSettings(ContextRelaySettingsSnapshot settings)
    {
        EnableRetrieval.IsChecked = settings.EmbeddedEnabled;
        sources.Clear();
        sources.AddRange(settings.EmbeddedSources.Select(source => source?.Clone() ?? new EmbeddedSourceSettings()));
        CloseEditor();
        SaveError.Text = string.Empty;
        RefreshSources(-1);
    }

    internal void RefreshContext(ContextRelaySettingsSnapshot settings)
    {
        text = new LocalizedText(OptionsLocalization.ResolveCulture(settings.UiLanguage));
        DataContext = text;
        CloudWarning.Visibility = settings.CloudEnvironment == CloudEnvironment.Global ? Visibility.Collapsed : Visibility.Visible;
        RefreshSources(Sources.SelectedIndex);
        RefreshEditorHeading();
    }

    internal bool TryGetSettings(out bool enabled, out List<EmbeddedSourceSettings> normalizedSources)
    {
        enabled = EnableRetrieval.IsChecked == true;
        normalizedSources = new List<EmbeddedSourceSettings>();
        SaveError.Text = string.Empty;
        if (Editor.Visibility == Visibility.Visible && !TrySaveSource())
        {
            return false;
        }

        var results = EmbeddedSourceValidator.Validate(sources);
        var invalid = results.FirstOrDefault(entry => !entry.IsValid);
        if (invalid is not null)
        {
            Sources.SelectedIndex = invalid.Index;
            OpenEditor(invalid.Index);
            ShowFieldErrors(invalid);
            return false;
        }

        normalizedSources.AddRange(results.Select(entry => entry.Normalized));
        return true;
    }

    internal void ShowSaveError(string details)
    {
        SaveError.Text = string.Format(text["SaveError"], details);
        SaveError.BringIntoView();
    }

    private void OnAdd(object sender, RoutedEventArgs e) => OpenEditor(-1);

    private void OnEdit(object sender, RoutedEventArgs e) => OpenEditor(Sources.SelectedIndex);

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        var index = Sources.SelectedIndex;
        sources.RemoveAt(index);
        RefreshSources(Math.Min(index, sources.Count - 1));
        FocusListAction();
    }

    private void OnSaveSource(object sender, RoutedEventArgs e)
    {
        if (TrySaveSource())
        {
            FocusListAction();
        }
    }

    private void OnDiscardSource(object sender, RoutedEventArgs e)
    {
        CloseEditor();
        FocusListAction();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        EditSource.IsEnabled = Sources.SelectedIndex >= 0;
        RemoveSource.IsEnabled = EditSource.IsEnabled;
    }

    private void OpenEditor(int index)
    {
        editingIndex = index;
        var source = index < 0 ? new EmbeddedSourceSettings() : sources[index];
        SourceName.Text = source.Name ?? string.Empty;
        ContainerTypeId.Text = source.ContainerTypeId ?? string.Empty;
        EnableSource.IsChecked = source.Enabled;
        NameError.Text = string.Empty;
        IdError.Text = string.Empty;
        SaveError.Text = string.Empty;
        RefreshEditorHeading();
        Editor.Visibility = Visibility.Visible;
        Sources.IsEnabled = false;
        ListActions.IsEnabled = false;
        SourceName.Focus();
        SourceName.BringIntoView();
    }

    private void CloseEditor()
    {
        Editor.Visibility = Visibility.Collapsed;
        Sources.IsEnabled = true;
        ListActions.IsEnabled = true;
    }

    private bool TrySaveSource()
    {
        // Validate the draft last so duplicate errors attach to the field being edited, regardless of its position.
        var candidates = sources.Where((_, index) => index != editingIndex).ToList();
        candidates.Add(new EmbeddedSourceSettings
        {
            Name = SourceName.Text,
            ContainerTypeId = ContainerTypeId.Text,
            Enabled = EnableSource.IsChecked == true,
        });
        var result = EmbeddedSourceValidator.Validate(candidates).Last();
        if (!result.IsValid)
        {
            ShowFieldErrors(result);
            return false;
        }

        if (editingIndex < 0)
        {
            editingIndex = sources.Count;
            sources.Add(result.Normalized);
        }
        else
        {
            sources[editingIndex] = result.Normalized;
        }

        CloseEditor();
        RefreshSources(editingIndex);
        return true;
    }

    private void ShowFieldErrors(EmbeddedSourceValidationEntry result)
    {
        NameError.Text = GetErrorText(result.NameError);
        IdError.Text = GetErrorText(result.ContainerTypeIdError);
        var field = result.NameError != EmbeddedSourceError.None ? SourceName : ContainerTypeId;
        field.Focus();
        field.BringIntoView();
    }

    private void RefreshSources(int selectedIndex)
    {
        Sources.ItemsSource = sources.Select(source => new SourceRow(source, text[source.Enabled ? "Enabled" : "Disabled"])).ToArray();
        Sources.SelectedIndex = selectedIndex;
        EmptyList.Visibility = sources.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshEditorHeading() => EditorHeading.Text = text[editingIndex < 0 ? "AddTitle" : "EditTitle"];

    private void FocusListAction()
    {
        var button = Sources.SelectedIndex >= 0 ? EditSource : AddSource;
        button.Focus();
        button.BringIntoView();
    }

    private string GetErrorText(EmbeddedSourceError error) => error == EmbeddedSourceError.None ? string.Empty : text[error.ToString()];

    private sealed class LocalizedText
    {
        private readonly CultureInfo culture;

        public LocalizedText(CultureInfo culture) => this.culture = culture;

        public string this[string key] => Strings.GetString(key, culture) ?? key;
    }

    private sealed class SourceRow
    {
        public SourceRow(EmbeddedSourceSettings source, string status)
        {
            Source = source;
            var id = source.ContainerTypeId ?? string.Empty;
            ShortId = id.Length > 8 ? id.Substring(0, 8) + "…" : id;
            Status = status;
        }

        public EmbeddedSourceSettings Source { get; }

        public string ShortId { get; }

        public string Status { get; }

        public string AccessibleName => $"{Source.Name}, {Source.ContainerTypeId}, {Status}";
    }
}
