using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace ContextRelay.Core.Tests.ToolWindows;

/// <summary>
/// Asserts the produced .vsix contains the in-proc options package payload and that the
/// post-build patched extension manifest exposes the VsPackage asset and all required
/// Community / Pro / Enterprise installation targets. These are the build-time guarantees
/// that keep Tools &gt; Options &gt; ContextRelay &gt; General visible inside Visual Studio.
/// Regression coverage for the loss of registration observed between PR #36 and PR #57.
/// </summary>
public sealed class InProcPackageVsixPackagingTests
{
    private const string VsxNamespace = "http://schemas.microsoft.com/developer/vsx-schema/2011";

    [Fact]
    public void BuiltVsix_ContainsInProcPackagePayloadAndHybridManifest()
    {
        var vsixPath = BuiltExtensionArtifactLocator.ResolveExtensionArtifactPath("ContextRelay.VSExtension.vsix");

        using var archive = ZipFile.OpenRead(vsixPath);

        AssertEntryPresent(archive, "extension.vsixmanifest");
        AssertEntryPresent(archive, "ContextRelay.VSExtension.Package.dll");
        AssertEntryPresent(archive, "ContextRelay.VSExtension.Package.pkgdef");
        AssertEntryPresent(archive, "Community.VisualStudio.Toolkit.dll");

        var manifestEntry = archive.GetEntry("extension.vsixmanifest");
        Assert.NotNull(manifestEntry);

        XDocument manifest;
        using (var stream = manifestEntry!.Open())
        {
            manifest = XDocument.Load(stream);
        }

        var root = manifest.Root;
        Assert.NotNull(root);
        XNamespace ns = VsxNamespace;

        var identity = root!.Element(ns + "Metadata")?.Element(ns + "Identity");
        Assert.NotNull(identity);
        Assert.NotEqual("0.0.0.0", identity!.Attribute("Version")?.Value);

        var installation = root.Element(ns + "Installation");
        Assert.NotNull(installation);

        var extensionType = installation!.Attribute("ExtensionType")?.Value;
        Assert.Equal("VSSDK+VisualStudio.Extensibility", extensionType);

        var installationTargetIds = installation
            .Elements(ns + "InstallationTarget")
            .Select(e => e.Attribute("Id")?.Value)
            .Where(v => v is not null)
            .Select(v => v!)
            .ToArray();

        Assert.Contains("Microsoft.VisualStudio.Community", installationTargetIds);
        Assert.Contains("Microsoft.VisualStudio.Pro", installationTargetIds);
        Assert.Contains("Microsoft.VisualStudio.Enterprise", installationTargetIds);

        var assets = root.Element(ns + "Assets")?.Elements(ns + "Asset").ToArray() ?? Array.Empty<XElement>();
        var vsPackageAsset = assets.SingleOrDefault(a => a.Attribute("Type")?.Value == "Microsoft.VisualStudio.VsPackage");
        Assert.True(vsPackageAsset is not null, "VsPackage asset is missing from the patched extension.vsixmanifest.");
        Assert.Equal("ContextRelay.VSExtension.Package.pkgdef", vsPackageAsset!.Attribute("Path")?.Value);

        var manifestXml = manifest.ToString();
        Assert.DoesNotContain("%CurrentProject%", manifestXml, StringComparison.Ordinal);
    }

    private static void AssertEntryPresent(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        Assert.True(entry is not null, $"Built VSIX is missing required entry '{entryName}'.");
    }
}
