using System;
using System.Linq;
using Microsoft.VisualStudio.Extensibility.UI;

namespace ContextRelay.VSExtension.ToolWindows;

internal sealed class ContextRelayWindowContent : RemoteUserControl
{
    private const string EmbeddedXamlResourceName = "ContextRelay.VSExtension.ToolWindows.ContextRelayWindowContent.xaml";

    public ContextRelayWindowContent(ContextRelayWindowViewModel viewModel)
        : base(dataContext: viewModel)
    {
        ValidateEmbeddedXamlResource();
    }

    internal static void ValidateEmbeddedXamlResource()
    {
        var assembly = typeof(ContextRelayWindowContent).Assembly;
        var hasExpectedResource = assembly.GetManifestResourceNames().Contains(EmbeddedXamlResourceName, StringComparer.Ordinal);
        if (!hasExpectedResource)
        {
            throw new InvalidOperationException($"Embedded resource '{EmbeddedXamlResourceName}' was not found. Ensure the XAML file is marked as an EmbeddedResource in the project.");
        }
    }
}
