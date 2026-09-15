using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Security;

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
                fullRoot = NormalizeRootPath(root);
            }
            catch (ArgumentException)
            {
                continue;
            }
            catch (NotSupportedException)
            {
                continue;
            }
            if (!IsUnderRoot(fullPath, fullRoot) ||
                !TryGetCanonicalPath(fullRoot, directory: true, out var canonicalRoot) ||
                !TryGetCanonicalPath(fullPath, directory: false, out var canonicalPath) ||
                !IsUnderRoot(canonicalPath, canonicalRoot))
            {
                continue;
            }

            attachment = new ResolvedAttachment
            {
                AbsolutePath = canonicalPath,
                WorkspaceRoot = canonicalRoot,
                RelativePath = GetRelativePath(canonicalRoot, canonicalPath),
                DisplayName = Path.GetFileName(canonicalPath)
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
            string.IsNullOrWhiteSpace(attachment.WorkspaceRoot))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var stream = new FileStream(attachment.AbsolutePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (!TryGetFinalPath(stream.SafeFileHandle, out var finalPath) ||
                !TryGetCanonicalPath(attachment.WorkspaceRoot, directory: true, out var canonicalRoot) ||
                !IsUnderRoot(finalPath, canonicalRoot) ||
                !CopilotSupportedFilePolicy.IsSupported(finalPath))
            {
                return null;
            }

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var text = attachment.SelectionStartLine.HasValue || attachment.SelectionEndLine.HasValue
                ? await ReadSelectedLinesAsync(reader, attachment, cancellationToken).ConfigureAwait(false)
                : await ReadFullFileAsync(reader, cancellationToken).ConfigureAwait(false);
            return FileContextPromptBuilder.NormalizeExtractedText(text);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (SecurityException)
        {
            return null;
        }
    }

    private static async Task<string> ReadFullFileAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[MaxFileChars + 1];
        var count = 0;
        while (count < buffer.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await reader.ReadAsync(buffer, count, buffer.Length - count).ConfigureAwait(false);
            if (read == 0)
                break;
            count += read;
        }

        var text = new string(buffer, 0, Math.Min(count, MaxFileChars));
        if (count <= MaxFileChars)
            return text;

        const string omissionMarker = "\n[additional file content omitted]";
        var contentBudget = Math.Max(0, MaxFileChars - omissionMarker.Length);
        return text.Substring(0, Math.Min(text.Length, contentBudget)) + omissionMarker;
    }

    private static async Task<string> ReadSelectedLinesAsync(StreamReader reader, ResolvedAttachment attachment, CancellationToken cancellationToken)
    {
        var start = Math.Max(1, attachment.SelectionStartLine ?? attachment.SelectionEndLine ?? 1);
        var end = Math.Max(start, attachment.SelectionEndLine ?? start);
        var builder = new StringBuilder();
        var lineNumber = 0;
        while (lineNumber < end)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line is null)
                break;
            lineNumber++;
            if (lineNumber >= start)
            {
                if (builder.Length > 0)
                    builder.AppendLine();
                var remaining = MaxFileChars - builder.Length;
                if (remaining <= 0)
                    break;
                builder.Append(line, 0, Math.Min(line.Length, remaining));
                if (line.Length >= remaining)
                    break;
            }
        }

        return builder.ToString();
    }

    private static bool TryGetCanonicalPath(string path, bool directory, out string canonicalPath)
    {
        canonicalPath = string.Empty;
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return TryGetUnixCanonicalPath(path, out canonicalPath);

        var handle = CreateFile(path, 0, FileShareRead | FileShareWrite | FileShareDelete, IntPtr.Zero,
            OpenExisting, directory ? FileFlagBackupSemantics : 0, IntPtr.Zero);
        using (handle)
        {
            return !handle.IsInvalid && TryGetFinalPath(handle, out canonicalPath);
        }
    }

    private static bool TryGetFinalPath(SafeFileHandle handle, out string path)
    {
        path = string.Empty;
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return TryGetUnixHandlePath(handle, out path);

        var buffer = new StringBuilder(512);
        var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        if (length == 0)
            return false;
        if (length >= buffer.Capacity)
        {
            buffer = new StringBuilder((int)length + 1);
            length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        }
        if (length == 0)
            return false;
        path = NormalizeFinalPath(buffer.ToString());
        return true;
    }

    private static bool TryGetUnixCanonicalPath(string path, out string canonicalPath)
    {
        canonicalPath = string.Empty;
        var buffer = Marshal.AllocHGlobal(4096);
        try
        {
            var result = RealPath(path, buffer);
            if (result == IntPtr.Zero)
                return false;
            canonicalPath = Marshal.PtrToStringAnsi(result) ?? string.Empty;
            return canonicalPath.Length > 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool TryGetUnixHandlePath(SafeFileHandle handle, out string path)
    {
        path = string.Empty;
        var descriptor = checked((int)handle.DangerousGetHandle().ToInt64());
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var buffer = Marshal.AllocHGlobal(4096);
            try
            {
                if (Fcntl(descriptor, FGetPath, buffer) == -1)
                    return false;
                path = Marshal.PtrToStringAnsi(buffer) ?? string.Empty;
                return path.Length > 0;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        foreach (var prefix in new[] { "/proc/self/fd/", "/dev/fd/" })
        {
            var link = prefix + descriptor.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var buffer = Marshal.AllocHGlobal(4096);
            try
            {
                var length = ReadLink(link, buffer, (UIntPtr)4096);
                if (length == new IntPtr(-1) || length == IntPtr.Zero)
                    continue;
                path = Marshal.PtrToStringAnsi(buffer, checked((int)length.ToInt64())) ?? string.Empty;
                if (path.Length > 0)
                    return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return false;
    }

    private static string NormalizeFinalPath(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path.Substring(8);
        }

        return path.StartsWith(@"\\?\", StringComparison.Ordinal)
            ? path.Substring(4)
            : path;
    }

    private static bool IsUnderRoot(string path, string root)
    {
        var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var separator = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
            root.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? string.Empty
            : Path.DirectorySeparatorChar.ToString();
        return string.Equals(path, root, comparison) ||
            path.StartsWith(root + separator, comparison);
    }

    private static string NormalizeRootPath(string root)
    {
        var fullRoot = Path.GetFullPath(root.Trim());
        var pathRoot = Path.GetPathRoot(fullRoot);
        var minimumLength = pathRoot?.Length ?? 0;
        while (fullRoot.Length > minimumLength &&
            (fullRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
             fullRoot.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)))
        {
            fullRoot = fullRoot.Substring(0, fullRoot.Length - 1);
        }

        return fullRoot;
    }

    private static string GetRelativePath(string root, string path)
    {
        var rootUri = new Uri(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar);
        var pathUri = new Uri(path);
        return Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString()).Replace('/', Path.DirectorySeparatorChar);
    }

    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const int FGetPath = 50;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint bufferLength, uint flags);

    [DllImport("libc", EntryPoint = "realpath", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr RealPath(string path, IntPtr resolvedPath);

    [DllImport("libc", EntryPoint = "readlink", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr ReadLink(string path, IntPtr buffer, UIntPtr bufferSize);

    [DllImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static extern int Fcntl(int descriptor, int command, IntPtr buffer);
}
