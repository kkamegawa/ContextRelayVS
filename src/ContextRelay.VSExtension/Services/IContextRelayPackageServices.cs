using System.Threading;
using System.Threading.Tasks;
using ContextRelay.VSExtension.Options;

namespace ContextRelay.VSExtension.Services;

internal interface IContextRelayPackageServices
{
    Task<ContextRelaySettingsSnapshot> GetSettingsSnapshotAsync(CancellationToken cancellationToken = default);
    Task<string?> GetSolutionRootAsync(CancellationToken cancellationToken = default);
    Task OpenDocumentAsync(string filePath, CancellationToken cancellationToken = default);
    Task<bool> AppendToActiveDocumentAsync(string text, CancellationToken cancellationToken = default);
    Task<bool> ReplaceActiveDocumentAsync(string text, CancellationToken cancellationToken = default);
    Task<bool> TryOpenCopilotChatAsync(CancellationToken cancellationToken = default);
    Task CopyTextToClipboardAsync(string text, CancellationToken cancellationToken = default);
    Task OpenSettingsFileAsync(CancellationToken cancellationToken = default);
}
