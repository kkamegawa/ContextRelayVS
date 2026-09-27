using System;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace ContextRelay.Core.Tests.ToolWindows;

public sealed class ContextRelayWindowContentEmbeddedResourceTests
{
    private const string XamlResourceName = "ContextRelay.VSExtension.ToolWindows.ContextRelayWindowContent.xaml";

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

    [Fact]
    public void EmbeddedXaml_ThemedListBoxStyle_EnablesPixelScrollingAndDisablesHorizontalScroll()
    {
        // Without these setters a single chat message taller than the viewport is one
        // indivisible scroll unit (its bottom unreachable) and text wrapping is defeated
        // by the infinite-width horizontal measure.
        var assemblyPath = BuiltExtensionArtifactLocator.ResolveExtensionArtifactPath("ContextRelay.VSExtension.dll");
        var assembly = Assembly.LoadFrom(assemblyPath);

        using var stream = assembly.GetManifestResourceStream(XamlResourceName);
        Assert.NotNull(stream);

        var document = XDocument.Load(stream!);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var themedListBoxStyle = document.Descendants()
            .SingleOrDefault(element =>
                element.Name.LocalName == "Style" &&
                (string?)element.Attribute(x + "Key") == "ThemedListBoxStyle");
        Assert.NotNull(themedListBoxStyle);

        Assert.Equal("False", GetSetterValue(themedListBoxStyle!, "ScrollViewer.CanContentScroll"));
        Assert.Equal("Disabled", GetSetterValue(themedListBoxStyle!, "ScrollViewer.HorizontalScrollBarVisibility"));
    }

    private static string? GetSetterValue(XElement style, string propertyName)
    {
        return style.Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .SingleOrDefault(element => (string?)element.Attribute("Property") == propertyName)
            ?.Attribute("Value")?.Value;
    }
}
