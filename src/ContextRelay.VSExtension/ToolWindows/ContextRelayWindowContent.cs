using Microsoft.VisualStudio.Extensibility.UI;

namespace ContextRelay.VSExtension.ToolWindows;

internal sealed class ContextRelayWindowContent : RemoteUserControl
{
    public ContextRelayWindowContent(ContextRelayWindowViewModel viewModel)
        : base(dataContext: viewModel) { }
}
