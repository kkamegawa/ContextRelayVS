using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ContextRelay.Core.Tests.ToolWindows;

/// <summary>
/// Guards the build wiring used to surface Tools > Options pages in both packaged
/// VSIX installs and local Experimental Instance deployments.
/// </summary>
public sealed class OptionsSidecarRegistrationTests
{
    [Fact]
    public void BuildConfiguration_RestoresVsPackageRegistrationPipeline()
    {
        var buildPropsPath = ResolveRepositoryFile("build", "ContextRelay.Build.props");
        var buildProps = File.ReadAllText(buildPropsPath);
        var extensionProjectPath = ResolveRepositoryFile(
            "src",
            "ContextRelay.VSExtension",
            "ContextRelay.VSExtension.csproj");
        var extensionProject = File.ReadAllText(extensionProjectPath);
        var packageProjectPath = ResolveRepositoryFile(
            "src",
            "ContextRelay.VSExtension.Package",
            "ContextRelay.VSExtension.Package.csproj");
        var packageProject = File.ReadAllText(packageProjectPath);
        var manifestPath = ResolveRepositoryFile(
            "src",
            "ContextRelay.VSExtension",
            "source.extension.vsixmanifest");
        var manifest = File.ReadAllText(manifestPath);

        Assert.Contains("SourceManifestPath", buildProps, StringComparison.Ordinal);
        Assert.Contains("Synchronized manifest installation metadata from source manifest", buildProps, StringComparison.Ordinal);
        Assert.Contains("SyncSection(doc, root, sourceDoc, \"Installation\")", buildProps, StringComparison.Ordinal);
        Assert.Contains("SyncSection(doc, root, sourceDoc, \"Dependencies\")", buildProps, StringComparison.Ordinal);
        Assert.Contains("SyncSection(doc, root, sourceDoc, \"Prerequisites\")", buildProps, StringComparison.Ordinal);
        Assert.Contains("Path.Combine(extensionsPath, publisherName, sidecarName, sidecarVersion)", buildProps, StringComparison.Ordinal);
        Assert.Contains("Path.Combine(extensionsPath, publisherName, sidecarName)", buildProps, StringComparison.Ordinal);
        Assert.Contains("Path.Combine(extensionsPath, \"ContextRelay.Package\")", buildProps, StringComparison.Ordinal);
        Assert.DoesNotContain("bool isStaleHybridPackage", buildProps, StringComparison.Ordinal);
        Assert.Contains("Installation ExtensionType=\"\"VSSDK\"\"", buildProps, StringComparison.Ordinal);
        Assert.Contains("Microsoft.VisualStudio.VsPackage", buildProps, StringComparison.Ordinal);
        Assert.Contains("ContextRelay.VSExtension.Package.pkgdef", buildProps, StringComparison.Ordinal);
        Assert.Contains("<Assets />", manifest, StringComparison.Ordinal);
        Assert.Contains("VisualStudioActivityLogPath", extensionProject, StringComparison.Ordinal);
        Assert.Contains("/rootsuffix Exp /log", extensionProject, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVsPackageAssetInManifest", extensionProject, StringComparison.Ordinal);
        Assert.DoesNotContain("InjectVsPackageAsset", extensionProject, StringComparison.Ordinal);
        Assert.DoesNotContain("PatchPackagePkgdefOptionsRegistrationBeforeVsix", extensionProject, StringComparison.Ordinal);
        Assert.DoesNotContain("PackagePkgdefPath", extensionProject, StringComparison.Ordinal);
        Assert.DoesNotContain("LatestPackagePkgdefPath", extensionProject, StringComparison.Ordinal);
        Assert.Contains("<IncludeOutputGroupsInVSIX></IncludeOutputGroupsInVSIX>", extensionProject, StringComparison.Ordinal);
        Assert.DoesNotContain("IncludeOutputGroupsInVSIX>BuiltProjectOutputGroup;PkgdefProjectOutputGroup", extensionProject, StringComparison.Ordinal);
        Assert.DoesNotContain("IncludeOutputGroupsInVSIXLocalOnly>DebugSymbolsProjectOutputGroup", extensionProject, StringComparison.Ordinal);
        Assert.Contains("<Content Include=\"source.extension.vsixmanifest\">", extensionProject, StringComparison.Ordinal);
        Assert.DoesNotContain("IncludeInProcPackagePayloadInVsix", extensionProject, StringComparison.Ordinal);
        Assert.DoesNotContain("<Link>ContextRelay.VSExtension.Package.dll</Link>", extensionProject, StringComparison.Ordinal);
        Assert.DoesNotContain("<Link>ContextRelay.VSExtension.Package.pkgdef</Link>", extensionProject, StringComparison.Ordinal);
        Assert.DoesNotContain("<Link>Community.VisualStudio.Toolkit.dll</Link>", extensionProject, StringComparison.Ordinal);
        Assert.Contains("PublisherName=\"KazushiKamegawa\"", extensionProject, StringComparison.Ordinal);
        Assert.Contains("SidecarVersion=\"$(Version)\"", extensionProject, StringComparison.Ordinal);
        Assert.DoesNotContain("VsRegEdit.exe", extensionProject, StringComparison.Ordinal);
        Assert.Contains("ContextRelay.Core.dll", extensionProject, StringComparison.Ordinal);
        Assert.Contains("System.Text.Json.dll", extensionProject, StringComparison.Ordinal);
        Assert.DoesNotContain("ExtensionType=\"VSSDK+VisualStudio.Extensibility\"", manifest, StringComparison.Ordinal);
        Assert.Contains("<GeneratePkgDefFile>true</GeneratePkgDefFile>", packageProject, StringComparison.Ordinal);
        Assert.Contains("VisualStudioActivityLogPath", packageProject, StringComparison.Ordinal);
        Assert.Contains("/rootsuffix Exp /log", packageProject, StringComparison.Ordinal);
        Assert.Contains("SetCreatePkgDefAssemblyToProcess", packageProject, StringComparison.Ordinal);
        Assert.Contains("CreatePkgDefAssemblyToProcess", packageProject, StringComparison.Ordinal);
        Assert.Contains("PatchPkgdefOptionsRegistration", packageProject, StringComparison.Ordinal);
        Assert.Contains("PatchPkgdefFile", packageProject, StringComparison.Ordinal);
        Assert.Contains("Microsoft.VisualStudio.Community", manifest, StringComparison.Ordinal);
        Assert.Contains("Microsoft.VisualStudio.Pro", manifest, StringComparison.Ordinal);
        Assert.Contains("Microsoft.VisualStudio.Enterprise", manifest, StringComparison.Ordinal);
        Assert.Contains("ProductArchitecture>amd64", buildProps, StringComparison.Ordinal);
        Assert.Contains("ProductArchitecture>arm64", buildProps, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticPkgdef_IsThePrimaryDeploymentSource()
    {
        var pkgdefPath = ResolveRepositoryFile(
            "src",
            "ContextRelay.VSExtension.Package",
            "ContextRelay.VSExtension.Package.pkgdef");
        var pkgdef = File.ReadAllText(pkgdefPath);

        Assert.Contains("[$RootKey$\\Packages\\{b1609362-6f9d-4e65-a1d8-ec73608f326c}]", pkgdef, StringComparison.Ordinal);
        Assert.Contains("\"Class\"=\"ContextRelay.VSExtension.Package.Options.ContextRelayOptionsPackage\"", pkgdef, StringComparison.Ordinal);
        Assert.Contains("[$RootKey$\\ToolsOptionsPages\\ContextRelay]", pkgdef, StringComparison.Ordinal);
        Assert.Contains("@=\"ContextRelay\"", pkgdef, StringComparison.Ordinal);
        Assert.Contains("[$RootKey$\\ToolsOptionsPages\\ContextRelay\\General]", pkgdef, StringComparison.Ordinal);
        Assert.Contains("\"Page\"=\"{68a2d2d2-54f0-4d97-9ae7-861330f6231f}\"", pkgdef, StringComparison.Ordinal);
        Assert.Contains("\"IsInUnifiedSettings\"=dword:00000000", pkgdef, StringComparison.Ordinal);

        var extensionProjectPath = ResolveRepositoryFile(
            "src",
            "ContextRelay.VSExtension",
            "ContextRelay.VSExtension.csproj");
        var extensionProject = File.ReadAllText(extensionProjectPath);

        // The deployment target reads the static pkgdef directly from the project directory.
        Assert.Contains("ContextRelay.VSExtension.Package', 'ContextRelay.VSExtension.Package.pkgdef'", extensionProject, StringComparison.Ordinal);
        Assert.DoesNotContain("VsRegEdit.exe", extensionProject, StringComparison.Ordinal);
    }

    private static string ResolveRepositoryFile(params string[] pathSegments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(pathSegments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Repository file '{Path.Combine(pathSegments)}' was not found.");
    }
}
