using System;
using System.IO;
using System.Linq;

namespace ContextRelay.VSExtension.Services;

internal static class WorkspaceRootInference
{
    internal static string? InferWorkspaceRootFromPath(string filePath, bool requireWorkspaceMarker = false)
    {
        var directory = File.Exists(filePath)
            ? Path.GetDirectoryName(filePath)
            : filePath;
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var current = new DirectoryInfo(directory);
        while (current is not null)
        {
            try
            {
                if (current.EnumerateFiles("*.sln").Any() ||
                    current.EnumerateFiles("*.slnx").Any() ||
                    current.EnumerateDirectories(".git").Any())
                {
                    return current.FullName;
                }
            }
            catch (UnauthorizedAccessException)
            {
                return directory;
            }
            catch (IOException)
            {
                return directory;
            }

            current = current.Parent;
        }

        return requireWorkspaceMarker ? null : directory;
    }
}
