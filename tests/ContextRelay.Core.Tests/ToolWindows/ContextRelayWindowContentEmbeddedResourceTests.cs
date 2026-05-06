using System;
using System.Reflection;
using Xunit;

namespace ContextRelay.Core.Tests.ToolWindows;

public sealed class ContextRelayWindowContentEmbeddedResourceTests
{
    [Fact]
    public void ValidateEmbeddedXamlResource_WhenInvokedFromBuiltExtension_DoesNotThrow()
    {
        var assemblyPath = BuiltExtensionArtifactLocator.ResolveExtensionArtifactPath("ContextRelay.VSExtension.dll");
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
}
