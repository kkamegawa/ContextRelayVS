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
            var candidate = Path.Combine(current.FullName, "src", "ContextRelay.VSExtension", "bin", "Debug", "net8.0-windows10.0.22621.0", "ContextRelay.VSExtension.dll");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("ContextRelay.VSExtension.dll が見つかりませんでした。先にソリューションのビルドを実行してください。");
    }
}
