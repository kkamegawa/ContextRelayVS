using System;

namespace ContextRelay.VSExtension.Package.Options;

/// <summary>
/// Defines GUID values used by the in-proc Visual Studio package.
/// </summary>
internal static class ContextRelayPackageGuids
{
    /// <summary>
    /// Identifies the package that registers the ContextRelay options page.
    /// </summary>
    public const string OptionsPackageString = "B1609362-6F9D-4E65-A1D8-EC73608F326C";

    /// <summary>
    /// Identifies the ContextRelay options page implementation.
    /// </summary>
    public const string OptionsPageString = "68A2D2D2-54F0-4D97-9AE7-861330F6231F";

    /// <summary>
    /// Identifies the SharePoint Embedded options page implementation.
    /// </summary>
    public const string EmbeddedOptionsPageString = "D67FC8D0-11B4-4B4B-9C18-BD778E9F9CF1";

    /// <summary>
    /// Gets the package GUID value.
    /// </summary>
    public static readonly Guid OptionsPackage = new(OptionsPackageString);

    /// <summary>
    /// Gets the options page GUID value.
    /// </summary>
    public static readonly Guid OptionsPage = new(OptionsPageString);
}
