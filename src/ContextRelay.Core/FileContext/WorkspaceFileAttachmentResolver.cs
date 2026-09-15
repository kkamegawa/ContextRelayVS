using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ContextRelay.Core.FileContext;

/// <summary>
/// Resolves and reads explicit local attachments while enforcing trusted workspace roots.
/// </summary>
public static class WorkspaceFileAttachmentResolver
{
    public const int MaxFileChars = 12000;

    public static bool TryResolve(
        string path,
        IReadOnlyList<string> workspaceRoots,
        out ResolvedAttachment? attachment)
    {
        attachment = null;
        if (string.IsNullOrWhiteSpace(path) || workspaceRoots is null)
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path.Trim());
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        if (!File.Exists(fullPath) || !CopilotSupportedFilePolicy.IsSupported(fullPath))
        {
            return false;
        }

        foreach (var root in workspaceRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            string fullRoot;
            try
            {
                fullRoot = Path.GetFullPath(root.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (ArgumentException)
            {
                continue;
            }
            catch (NotSupportedException)
            {
                continue;
            }
            if (!IsUnderRoot(fullPath, fullRoot))
            {
                continue;
            }

            attachment = new ResolvedAttachment
            {
                AbsolutePath = fullPath,
                WorkspaceRoot = fullRoot,
                RelativePath = GetRelativePath(fullRoot, fullPath),
                DisplayName = Path.GetFileName(fullPath)
            };
            return true;
        }

        return false;
    }

    public static async Task<string?> ReadTextAsync(
        ResolvedAttachment attachment,
        CancellationToken cancellationToken = default)
    {
        if (attachment is null || string.IsNullOrWhiteSpace(attachment.AbsolutePath) ||
            !TryResolve(attachment.AbsolutePath, new[] { attachment.WorkspaceRoot }, out var current) ||
            current is null || !string.Equals(current.AbsolutePath, Path.GetFullPath(attachment.AbsolutePath), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new FileStream(current.AbsolutePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var buffer = new char[MaxFileChars + 1];
        var count = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var text = new string(buffer, 0, Math.Min(count, MaxFileChars));
        if (count > MaxFileChars)
        {
            text = FileContextPromptBuilder.TruncateForBudget(text + "\n[additional file content omitted]", MaxFileChars);
        }

        return FileContextPromptBuilder.NormalizeExtractedText(text);
    }

    private static bool IsUnderRoot(string path, string root)
    {
        return string.Equals(path, root, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetRelativePath(string root, string path)
    {
        var rootUri = new Uri(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar);
        var pathUri = new Uri(path);
        return Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString()).Replace('/', Path.DirectorySeparatorChar);
    }
}
