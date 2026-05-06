using System;
using System.IO;

namespace ContextRelay.Core.Tests.ToolWindows;

/// <summary>
/// Locates built extension artifacts produced by the main Visual Studio extension project.
/// </summary>
internal static class BuiltExtensionArtifactLocator
{
    /// <summary>
    /// Resolves a built artifact from the extension project's bin output.
    /// </summary>
    /// <param name="fileName">The file name to locate, such as a DLL or VSIX.</param>
    /// <returns>The absolute path to the requested artifact.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the built artifact is not present.</exception>
    public static string ResolveExtensionArtifactPath(string fileName)
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
                        var candidate = Path.Combine(tfmDir, fileName);
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
            $"Built extension artifact '{fileName}' was not found. Build the solution before running this test.");
    }
}
