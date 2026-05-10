using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ContextRelay.Core.Tests.ToolWindows;

/// <summary>
/// Guards the local VSSDK sidecar registration used to surface Tools > Options pages.
/// </summary>
public sealed class OptionsSidecarRegistrationTests
{
    [Fact]
    public void DeploymentTask_CreatesFlatVssdkSidecarManifest()
    {
        var buildPropsPath = ResolveRepositoryFile("build", "ContextRelay.Build.props");
        var buildProps = File.ReadAllText(buildPropsPath);
        var extensionProjectPath = ResolveRepositoryFile(
            "src",
            "ContextRelay.VSExtension",
            "ContextRelay.VSExtension.csproj");
        var extensionProject = File.ReadAllText(extensionProjectPath);

        Assert.Contains("Path.Combine(extensionsPath, \"ContextRelay.Package\")", buildProps, StringComparison.Ordinal);
        Assert.Contains("Path.Combine(legacySubDirPath, \"ContextRelay.Package\")", buildProps, StringComparison.Ordinal);
        Assert.Contains("Installation ExtensionType=\"\"VSSDK\"\"", buildProps, StringComparison.Ordinal);
        Assert.Contains("Microsoft.VisualStudio.VsPackage", buildProps, StringComparison.Ordinal);
        Assert.Contains("ContextRelay.VSExtension.Package.pkgdef", buildProps, StringComparison.Ordinal);
        Assert.Contains("VsRegEdit.exe", extensionProject, StringComparison.Ordinal);
        Assert.Contains("<_VsRegEditPath Include=", extensionProject, StringComparison.Ordinal);
        Assert.Contains("%(_VsRegEditPath.Identity)", extensionProject, StringComparison.Ordinal);
        Assert.DoesNotContain("<_VsRegEditPath Condition=\"'$(_VsRegEditPath)' == ''", extensionProject, StringComparison.Ordinal);
        Assert.Contains(@"ToolsOptionsPages\ContextRelay", extensionProject, StringComparison.Ordinal);
        Assert.Contains("ProductArchitecture>amd64", buildProps, StringComparison.Ordinal);
        Assert.Contains("ProductArchitecture>arm64", buildProps, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticPkgdef_RegistersContextRelayOptionsPage()
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
