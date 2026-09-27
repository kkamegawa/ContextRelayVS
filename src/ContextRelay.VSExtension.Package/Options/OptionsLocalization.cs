using System;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using ContextRelay.Core.Settings;
using ContextRelay.VisualStudio;
using ContextRelay.VSExtension.Package.Services;

namespace ContextRelay.VSExtension.Package.Options;

internal static class OptionsLocalization
{
    private const string UiLanguageAuto = "auto";
    private const string UiLanguageEnglish = "en";
    private const string UiLanguageJapanese = "ja";

    private static readonly ResourceManager Resources = new(
        "ContextRelay.VSExtension.Package.Options.ContextRelayOptionsStrings",
        typeof(OptionsLocalization).Assembly);

    private static readonly CultureInfo EnglishCulture = new(UiLanguageEnglish);
    private static readonly CultureInfo JapaneseCulture = new(UiLanguageJapanese);

    private static string configuredUiLanguage = UiLanguageAuto;

    /// <summary>
    /// Gets the normalized ContextRelay UI language currently applied to option labels.
    /// </summary>
    internal static string CurrentUiLanguage => configuredUiLanguage;

    /// <summary>
    /// Applies the persisted ContextRelay UI language so option labels follow the same setting as the
    /// tool window instead of the Visual Studio process culture.
    /// </summary>
    /// <param name="uiLanguage">The configured UI language, normalized to "auto", "en", or "ja".</param>
    internal static void SetUiLanguage(string? uiLanguage)
    {
        configuredUiLanguage = ContextRelaySettingsStore.NormalizeUiLanguage(uiLanguage);
    }

    internal static string Get(string key)
    {
        return Resources.GetString(key, ResolveCulture()) ?? key;
    }

    private static CultureInfo ResolveCulture()
    {
        var hostLcid = VisualStudioLanguageServiceExport.CapturedUiLocale;
        return VisualStudioLanguageService.ResolveLanguage(configuredUiLanguage, hostLcid) == UiLanguageJapanese
            ? JapaneseCulture
            : EnglishCulture;
    }

}

internal sealed class LocalizedCategoryAttribute : CategoryAttribute
{
    public LocalizedCategoryAttribute(string key) : base(key) { }

    protected override string GetLocalizedString(string value) => OptionsLocalization.Get(value);
}

internal sealed class LocalizedDisplayNameAttribute : DisplayNameAttribute
{
    private readonly string key;

    public LocalizedDisplayNameAttribute(string key) => this.key = key;

    public override string DisplayName => OptionsLocalization.Get(key);
}

internal sealed class LocalizedDescriptionAttribute : DescriptionAttribute
{
    private readonly string key;

    public LocalizedDescriptionAttribute(string key) => this.key = key;

    public override string Description => OptionsLocalization.Get(key);
}
