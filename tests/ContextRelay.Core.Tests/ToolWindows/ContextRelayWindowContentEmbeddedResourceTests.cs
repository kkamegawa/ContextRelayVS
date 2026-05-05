using System;
using System.IO;
using System.Reflection;
using Xunit;

namespace ContextRelay.Core.Tests.ToolWindows;

public sealed class ContextRelayWindowContentEmbeddedResourceTests
{
    [Fact]
    public void ValidateEmbeddedXamlResource_WhenInvokedFromBuiltExtension_DoesNotThrow()
    {
        var assemblyPath = ResolveExtensionAssemblyPath();
        var assembly = Assembly.LoadFrom(assemblyPath);
        var type = assembly.GetType("ContextRelay.VSExtension.ToolWindows.ContextRelayWindowContent", throwOnError: true);
        var method = type!.GetMethod("ValidateEmbeddedXamlResource", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var exception = Record.Exception(() => method!.Invoke(obj: null, parameters: null));
        if (exception is TargetInvocationException { InnerException: not null } invocationException)
        {
            exception = invocationException.InnerException;
        }

        Assert.Null(exception);
    }

    private static string ResolveExtensionAssemblyPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var extensionBinDir = Path.Combine(current.FullName, "src", "ContextRelay.VSExtension", "bin");
            if (Directory.Exists(extensionBinDir))
            {
                // Probe Release before Debug so CI builds (which use -c Release) are found first.
                foreach (var configuration in new[] { "Release", "Debug" })
                {
                    var configDir = Path.Combine(extensionBinDir, configuration);
                    if (!Directory.Exists(configDir))
                    {
                        continue;
                    }

                    foreach (var tfmDir in Directory.EnumerateDirectories(configDir))
                    {
                        var candidate = Path.Combine(tfmDir, "ContextRelay.VSExtension.dll");
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                }
            }

            current = current.Parent;
        }

        throw new FileNotFoundException(
            "ContextRelay.VSExtension.dll was not found. Build the solution before running this test.");
    }
}
