using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.Settings;
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

    [Fact]
    public void MergeSelectedFilesIntoQuery_WhenCurrentQueryIsAskCommand_PreservesSlashCommand()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var hostType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHost", throwOnError: true);
        var mergeMethod = hostType!.GetMethod("MergeSelectedFilesIntoQuery", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(mergeMethod);

        var workspaceRoot = CreateTemporaryWorkspace();
        try
        {
            var absolutePath = Path.Combine(workspaceRoot, "docs", "summary.md");
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
            File.WriteAllText(absolutePath, "sample");

            var result = mergeMethod!.Invoke(obj: null, parameters: new object[]
            {
                "/ask",
                new[] { absolutePath },
                new[] { workspaceRoot }
            });

            Assert.NotNull(result);
            var queryText = GetStringProperty(result!, "QueryText");
            Assert.Equal("/ask #docs/summary.md", queryText);
        }
        finally
        {
            TryDeleteDirectory(workspaceRoot);
        }
    }

    [Fact]
    public void MergeSelectedFilesIntoQuery_WhenCurrentQueryContainsAskInstruction_PreservesInstructionShape()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var hostType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHost", throwOnError: true);
        var mergeMethod = hostType!.GetMethod("MergeSelectedFilesIntoQuery", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(mergeMethod);

        var workspaceRoot = CreateTemporaryWorkspace();
        try
        {
            var absolutePath = Path.Combine(workspaceRoot, "docs", "summary.md");
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
            File.WriteAllText(absolutePath, "sample");

            var existingQuery = "/ask  Summarize this\nas markdown";
            var result = mergeMethod!.Invoke(obj: null, parameters: new object[]
            {
                existingQuery,
                new[] { absolutePath },
                new[] { workspaceRoot }
            });

            Assert.NotNull(result);
            var queryText = GetStringProperty(result!, "QueryText");
            Assert.Equal("/ask  Summarize this\nas markdown #docs/summary.md", queryText);
        }
        finally
        {
            TryDeleteDirectory(workspaceRoot);
        }
    }

    [Fact]
    public void MergeSelectedFilesIntoQuery_WhenSelectionIsUnsupported_DoesNotAppend()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var hostType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHost", throwOnError: true);
        var mergeMethod = hostType!.GetMethod("MergeSelectedFilesIntoQuery", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(mergeMethod);

        var workspaceRoot = CreateTemporaryWorkspace();
        try
        {
            var absolutePath = Path.Combine(workspaceRoot, "capture.pcap");
            File.WriteAllText(absolutePath, "binary");

            var result = mergeMethod!.Invoke(obj: null, parameters: new object[]
            {
                string.Empty,
                new[] { absolutePath },
                new[] { workspaceRoot }
            });

            Assert.NotNull(result);
            var queryText = GetStringProperty(result!, "QueryText");
            var statusMessage = GetStringProperty(result!, "StatusMessage");
            Assert.Equal(string.Empty, queryText);
            Assert.False(string.IsNullOrWhiteSpace(statusMessage));
        }
        finally
        {
            TryDeleteDirectory(workspaceRoot);
        }
    }

    [Fact]
    public void MergeSelectedFilesIntoQuery_WhenWorkspaceRootsAreUnavailable_InfersWorkspaceRootFromSelection()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var hostType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHost", throwOnError: true);
        var mergeMethod = hostType!.GetMethod("MergeSelectedFilesIntoQuery", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(mergeMethod);

        var workspaceRoot = CreateTemporaryWorkspace();
        try
        {
            Directory.CreateDirectory(Path.Combine(workspaceRoot, ".git"));
            var absolutePath = Path.Combine(workspaceRoot, "src", "sample.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
            File.WriteAllText(absolutePath, "class Sample {}");

            var result = mergeMethod!.Invoke(obj: null, parameters: new object[]
            {
                string.Empty,
                new[] { absolutePath },
                Array.Empty<string>()
            });

            Assert.NotNull(result);
            var queryText = GetStringProperty(result!, "QueryText");
            Assert.Contains("#src/sample.cs", queryText, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(workspaceRoot);
        }
    }

    [Fact]
    public async Task ResolveCreatedFileTargetContextAsync_WhenSolutionRootExists_SkipsFolderPickerAndEnablesSolutionAdd()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var folderPickerCallCount = 0;
        var workspaceRoot = CreateTemporaryWorkspace();

        try
        {
            var result = await InvokeResolveCreatedFileTargetContextAsync(
                assembly,
                _ => Task.FromResult<string?>(workspaceRoot),
                (_, _) =>
                {
                    folderPickerCallCount++;
                    return Task.FromResult<string?>(Path.Combine(workspaceRoot, "fallback"));
                },
                initialDirectory: workspaceRoot);

            Assert.NotNull(result);
            Assert.Equal(Path.GetFullPath(workspaceRoot), GetStringProperty(result!, "RootDirectory"));
            Assert.True(GetBooleanProperty(result!, "ShouldAddToSolutionExplorer"));
            Assert.False(GetBooleanProperty(result!, "FolderWasSelected"));
            Assert.Equal(0, folderPickerCallCount);
        }
        finally
        {
            TryDeleteDirectory(workspaceRoot);
        }
    }

    [Fact]
    public async Task ResolveCreatedFileTargetContextAsync_WhenSolutionRootIsMissing_UsesPickedFolderWithoutSolutionAdd()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var initialDirectory = CreateTemporaryWorkspace();
        var selectedFolder = Path.Combine(initialDirectory, "chosen-output");
        Directory.CreateDirectory(selectedFolder);

        try
        {
            string? observedInitialDirectory = null;
            var result = await InvokeResolveCreatedFileTargetContextAsync(
                assembly,
                _ => Task.FromResult<string?>(null),
                (candidateInitialDirectory, _) =>
                {
                    observedInitialDirectory = candidateInitialDirectory;
                    return Task.FromResult<string?>(selectedFolder);
                },
                initialDirectory);

            Assert.NotNull(result);
            Assert.Equal(initialDirectory, observedInitialDirectory);
            Assert.Equal(Path.GetFullPath(selectedFolder), GetStringProperty(result!, "RootDirectory"));
            Assert.False(GetBooleanProperty(result!, "ShouldAddToSolutionExplorer"));
            Assert.True(GetBooleanProperty(result!, "FolderWasSelected"));
        }
        finally
        {
            TryDeleteDirectory(initialDirectory);
        }
    }

    [Fact]
    public async Task ResolveCreatedFileTargetContextAsync_WhenFolderPickerIsCanceled_ReturnsNull()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var initialDirectory = CreateTemporaryWorkspace();

        try
        {
            var result = await InvokeResolveCreatedFileTargetContextAsync(
                assembly,
                _ => Task.FromResult<string?>(null),
                (_, _) => Task.FromResult<string?>(null),
                initialDirectory);

            Assert.Null(result);
        }
        finally
        {
            TryDeleteDirectory(initialDirectory);
        }
    }

    [Fact]
    public void ResolveOutputDirectory_WhenFolderIsPicked_UsesThatFolderAsTheWriteRoot()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var hostType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHost", throwOnError: true);
        var resolveMethod = hostType!.GetMethod("ResolveOutputDirectory", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(resolveMethod);

        var selectedFolder = CreateTemporaryWorkspace();
        try
        {
            var settings = new ContextRelaySettingsSnapshot
            {
                OutputDirectory = ".contextrelay"
            };

            var resolvedPath = Assert.IsType<string>(resolveMethod!.Invoke(obj: null, parameters: new object[] { settings, selectedFolder }));
            Assert.Equal(Path.Combine(Path.GetFullPath(selectedFolder), ".contextrelay"), resolvedPath);
        }
        finally
        {
            TryDeleteDirectory(selectedFolder);
        }
    }

    private static string GetStringProperty(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        return Assert.IsType<string>(property!.GetValue(target));
    }

    private static bool GetBooleanProperty(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        return Assert.IsType<bool>(property!.GetValue(target));
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

    private static async Task<object?> InvokeResolveCreatedFileTargetContextAsync(
        Assembly assembly,
        Func<CancellationToken, Task<string?>> getSolutionRootAsync,
        Func<string?, CancellationToken, Task<string?>> pickFolderAsync,
        string initialDirectory)
    {
        var hostType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHost", throwOnError: true);
        var method = hostType!.GetMethod(
            "ResolveCreatedFileTargetContextAsync",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types: new[]
            {
                typeof(Func<CancellationToken, Task<string?>>),
                typeof(Func<string?, CancellationToken, Task<string?>>),
                typeof(string),
                typeof(CancellationToken)
            },
            modifiers: null);
        Assert.NotNull(method);

        var invocation = method!.Invoke(obj: null, parameters: new object[]
        {
            getSolutionRootAsync,
            pickFolderAsync,
            initialDirectory,
            CancellationToken.None
        });

        var task = Assert.IsAssignableFrom<Task>(invocation);
        await task.ConfigureAwait(false);
        return task.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public)!.GetValue(task);
    }
}
