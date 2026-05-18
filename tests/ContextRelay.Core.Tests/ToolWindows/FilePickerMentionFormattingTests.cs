using System;
using System.IO;
using System.Reflection;
using Xunit;

namespace ContextRelay.Core.Tests.ToolWindows;

public sealed class FilePickerMentionFormattingTests
{
    [Fact]
    public void MergeSelectedFilesIntoQuery_WhenPathContainsWhitespace_UsesQuotedMentionToken()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var hostType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHost", throwOnError: true);
        var mergeMethod = hostType!.GetMethod("MergeSelectedFilesIntoQuery", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(mergeMethod);

        var workspaceRoot = CreateTemporaryWorkspace();
        try
        {
            var absolutePath = Path.Combine(workspaceRoot, "docs", "my file.md");
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
            File.WriteAllText(absolutePath, "sample");

            var result = mergeMethod!.Invoke(obj: null, parameters: new object[]
            {
                string.Empty,
                new[] { absolutePath },
                new[] { workspaceRoot }
            });

            Assert.NotNull(result);
            var queryText = GetStringProperty(result!, "QueryText");
            Assert.Contains("#\"docs/my file.md\"", queryText, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(workspaceRoot);
        }
    }

    [Fact]
    public void MergeSelectedFilesIntoQuery_WhenMentionLimitAlreadyReached_DoesNotAppend()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var hostType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHost", throwOnError: true);
        var mergeMethod = hostType!.GetMethod("MergeSelectedFilesIntoQuery", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(mergeMethod);

        var workspaceRoot = CreateTemporaryWorkspace();
        try
        {
            var absolutePath = Path.Combine(workspaceRoot, "src", "sample.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
            File.WriteAllText(absolutePath, "class Sample {}");

            var existingQuery = "#a #b #c #d #e";
            var result = mergeMethod!.Invoke(obj: null, parameters: new object[]
            {
                existingQuery,
                new[] { absolutePath },
                new[] { workspaceRoot }
            });

            Assert.NotNull(result);
            var queryText = GetStringProperty(result!, "QueryText");
            Assert.Equal(existingQuery, queryText);
        }
        finally
        {
            TryDeleteDirectory(workspaceRoot);
        }
    }

    private static string GetStringProperty(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        return Assert.IsType<string>(property!.GetValue(target));
    }

    private static string CreateTemporaryWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), "ContextRelayVS.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup for transient test artifacts.
        }
    }

    private static Assembly LoadBuiltExtensionAssembly()
    {
        var assemblyPath = BuiltExtensionArtifactLocator.ResolveExtensionArtifactPath("ContextRelay.VSExtension.dll");
        return Assembly.LoadFrom(assemblyPath);
    }
}
