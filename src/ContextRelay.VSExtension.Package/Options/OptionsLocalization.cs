using System;
using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace ContextRelay.VSExtension.Package.Options;

internal static class OptionsLocalization
{
    private static readonly ResourceManager Resources = new(
        "ContextRelay.VSExtension.Package.Options.ContextRelayOptionsStrings",
        typeof(OptionsLocalization).Assembly);

    internal static string Get(string key)
    {
        return Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;
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
