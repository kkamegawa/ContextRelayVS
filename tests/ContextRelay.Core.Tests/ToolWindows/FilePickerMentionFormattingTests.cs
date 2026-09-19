using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.Chat;
using ContextRelay.Core.FileContext;
using ContextRelay.Core.Settings;
using Xunit;

namespace ContextRelay.Core.Tests.ToolWindows;

public sealed class FilePickerMentionFormattingTests
{
    [Fact]
    public void SelectSendAttachments_UsesPriorityDeduplicationLimitAndPendingId()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var hostType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHost", throwOnError: true)!;
        var method = hostType.GetMethod("SelectSendAttachments", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var mentions = new[] { Mention("mention.md"), Mention("shared.md") };
        var pending = new[] { Attachment("pending-id", "shared.md"), Attachment("pending-only", "pending.md") };
        var active = Attachment("active-id", "active.md");
        var selection = method!.Invoke(null, new object[] { mentions, pending, active, 3 });
        var attachments = GetProperty<IReadOnlyList<ResolvedAttachment>>(selection!, "Attachments");
        var submitted = GetProperty<IReadOnlyList<string>>(selection!, "SubmittedPendingIds");

        Assert.Equal(new[] { "mention.md", "shared.md", "pending.md" }, attachments.Select(item => item.RelativePath));
        Assert.Equal(new[] { "pending-id", "pending-only" }, submitted);
        Assert.DoesNotContain(attachments, item => item.RelativePath == "active.md");

        var withRoom = method.Invoke(null, new object[] { mentions, pending, active, 4 });
        var withRoomAttachments = GetProperty<IReadOnlyList<ResolvedAttachment>>(withRoom!, "Attachments");
        Assert.Contains(withRoomAttachments, item => item.RelativePath == "active.md");
    }

    [Fact]
    public async Task GetIncludedPendingAttachmentIds_ExcludesUnreadableAttachment()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var hostType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHost", throwOnError: true)!;
        var select = hostType.GetMethod("SelectSendAttachments", BindingFlags.Static | BindingFlags.NonPublic)!;
        var filter = hostType.GetMethod("GetIncludedPendingAttachmentIds", BindingFlags.Static | BindingFlags.NonPublic)!;
        var root = CreateTemporaryWorkspace();
        try
        {
            var readablePath = Path.Combine(root, "readable.md");
            File.WriteAllText(readablePath, "readable");
            var readable = Attachment("readable-id", "readable.md", readablePath, root);
            var unreadable = Attachment("unreadable-id", "missing.md", Path.Combine(root, "missing.md"), root);
            var selection = select.Invoke(null, new object[] { Array.Empty<ResolvedFileMention>(), new[] { readable, unreadable }, null!, 5 });
            var selected = GetProperty<IReadOnlyList<ResolvedAttachment>>(selection!, "Attachments");
            var payload = await ChatContextPayloadBuilder.BuildAsync(
                selected,
                Array.Empty<ContextRelay.Core.SharedStore.SharedSnippetItem>(),
                cancellationToken: TestContext.Current.CancellationToken);
            var included = Assert.IsAssignableFrom<IReadOnlyList<string>>(filter.Invoke(null, new[] { selection, payload }));
            Assert.Equal(new[] { "readable-id" }, included);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void CanonicalizePendingAttachments_DropsAttachmentsFromPreviousSolution()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var hostType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHost", throwOnError: true)!;
        var canonicalize = hostType.GetMethod("CanonicalizePendingAttachments", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(canonicalize);

        var workspaceA = CreateTemporaryWorkspace();
        var workspaceB = CreateTemporaryWorkspace();
        try
        {
            var pathA = Path.Combine(workspaceA, "from-a.md");
            var pathB = Path.Combine(workspaceB, "from-b.md");
            File.WriteAllText(pathA, "a");
            File.WriteAllText(pathB, "b");

            var pending = new[] { Attachment("a-id", "from-a.md", pathA, workspaceA) };
            var afterSwitch = Assert.IsAssignableFrom<IReadOnlyList<ResolvedAttachment>>(
                canonicalize!.Invoke(null, new object[] { pending, new[] { workspaceB } }));
            Assert.Empty(afterSwitch);

            var withoutCurrentRoots = Assert.IsAssignableFrom<IReadOnlyList<ResolvedAttachment>>(
                canonicalize.Invoke(null, new object[] { pending, Array.Empty<string>() }));
            Assert.Empty(withoutCurrentRoots);

            var currentPending = new[] { Attachment("b-id", "from-b.md", pathB, workspaceB) };
            var canonical = Assert.IsAssignableFrom<IReadOnlyList<ResolvedAttachment>>(
                canonicalize.Invoke(null, new object[] { currentPending, new[] { workspaceB } }));
            var attachment = Assert.Single(canonical);
            Assert.Equal("b-id", attachment.Id);
            Assert.Equal(Path.GetFileName(pathB), Path.GetFileName(attachment.AbsolutePath));
            Assert.Equal("from-b.md", attachment.RelativePath);
        }
        finally
        {
            TryDeleteDirectory(workspaceA);
            TryDeleteDirectory(workspaceB);
        }
    }

    [Fact]
    public void GetAuthorizedRememberedRoots_UsesOnlyRootsAuthorizedByCurrentWorkspace()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var servicesType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayVsServices", throwOnError: true)!;
        var getAuthorizedRoots = servicesType.GetMethod("GetAuthorizedRememberedRoots", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(getAuthorizedRoots);

        var workspaceA = CreateTemporaryWorkspace();
        var workspaceB = CreateTemporaryWorkspace();
        try
        {
            var rememberedB = Path.Combine(workspaceB, "nested");
            Directory.CreateDirectory(rememberedB);
            var afterSwitch = Assert.IsAssignableFrom<IReadOnlyList<string>>(
                getAuthorizedRoots!.Invoke(null, new object[] { new[] { workspaceA, rememberedB }, new[] { workspaceB } }));
            Assert.Equal(new[] { rememberedB }, afterSwitch);

            // A remembered directory inside the workspace that redirects outside it must not be trusted.
            var redirected = Path.Combine(workspaceB, "redirected");
            try
            {
                Directory.CreateSymbolicLink(redirected, workspaceA);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                Assert.Skip($"Creating a directory symbolic link is unavailable: {ex.Message}");
            }

            var withRedirectedRoot = Assert.IsAssignableFrom<IReadOnlyList<string>>(
                getAuthorizedRoots.Invoke(null, new object[] { new[] { redirected }, new[] { workspaceB } }));
            Assert.Empty(withRedirectedRoot);

            var withNoWorkspace = Assert.IsAssignableFrom<IReadOnlyList<string>>(
                getAuthorizedRoots.Invoke(null, new object[] { new[] { workspaceA }, Array.Empty<string>() }));
            Assert.Empty(withNoWorkspace);
        }
        finally
        {
            TryDeleteDirectory(workspaceA);
            TryDeleteDirectory(workspaceB);
        }
    }

    [Fact]
    public void PrunePendingAttachments_RemovesStaleEntriesAndCanonicalizesRetainedEntries()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var hostType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHost", throwOnError: true)!;
        var prune = hostType.GetMethod("PrunePendingAttachments", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(prune);

        var workspaceA = CreateTemporaryWorkspace();
        var workspaceB = CreateTemporaryWorkspace();
        try
        {
            var pathA = Path.Combine(workspaceA, "from-a.md");
            var pathB = Path.Combine(workspaceB, "from-b.md");
            File.WriteAllText(pathA, "a");
            File.WriteAllText(pathB, "b");
            var pending = new List<ResolvedAttachment>
            {
                Attachment("a-id", "from-a.md", pathA, workspaceA),
                Attachment("b-id", "from-b.md", pathB, workspaceB)
            };

            var pruned = Assert.IsAssignableFrom<IReadOnlyList<string>>(
                prune!.Invoke(null, new object[] { pending, new[] { workspaceB } }));

            Assert.Equal(new[] { "a-id" }, pruned);
            var retained = Assert.Single(pending);
            Assert.Equal("b-id", retained.Id);
            Assert.Equal("from-b.md", retained.RelativePath);
        }
        finally
        {
            TryDeleteDirectory(workspaceA);
            TryDeleteDirectory(workspaceB);
        }
    }

    [Fact]
    public void ChatRoutes_ApplyIncludedPendingFilter()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "ContextRelay.VSExtension", "Services", "ContextRelayHost.cs"));
        Assert.Equal(2, source.Split("GetIncludedPendingAttachmentIds(attachmentSelection, contextPayload)", StringSplitOptions.None).Length - 1);
        Assert.Contains("var includedAttachmentCount = contextPayload.IncludedAttachmentIds.Count;", source, StringComparison.Ordinal);
        var addFilesStart = source.IndexOf("public async Task<ContextRelayHostState> AddFilesToQueryAsync", StringComparison.Ordinal);
        var nextMethodStart = source.IndexOf("public async Task<ContextRelayHostState> GenerateHandoffAsync", addFilesStart, StringComparison.Ordinal);
        var addFilesBody = source.Substring(addFilesStart, nextMethodStart - addFilesStart);
        var pruneIndex = addFilesBody.IndexOf("PrunePendingAttachmentsFromCurrentWorkspace(workspaceRoots);", StringComparison.Ordinal);
        var limitIndex = addFilesBody.IndexOf("pendingAttachments.Count >= settings.ChatMaxAttachedFiles", StringComparison.Ordinal);
        Assert.True(
            pruneIndex >= 0 && limitIndex >= 0 && pruneIndex < limitIndex,
            "Stale attachments must be pruned before enforcing the picker limit.");
    }

    [Fact]
    public void SymlinkAliasAndTarget_ResolveToOneAttachmentSlot()
    {
        var root = CreateTemporaryWorkspace();
        try
        {
            var target = Path.Combine(root, "target.md");
            var alias = Path.Combine(root, "alias.md");
            File.WriteAllText(target, "target");
            try
            {
                File.CreateSymbolicLink(alias, target);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                Assert.Skip($"Creating a symbolic link is unavailable: {ex.Message}");
            }

            var resolution = FileMentionResolver.Resolve("#target.md #alias.md", new[] { root }, 5);
            Assert.Single(resolution.Files);
            var assembly = LoadBuiltExtensionAssembly();
            var hostType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHost", throwOnError: true)!;
            var canonicalize = hostType.GetMethod("CanonicalizeMentions", BindingFlags.Static | BindingFlags.NonPublic)!;
            var method = hostType.GetMethod("SelectSendAttachments", BindingFlags.Static | BindingFlags.NonPublic)!;
            var canonicalMentions = Assert.IsAssignableFrom<IReadOnlyList<ResolvedFileMention>>(
                canonicalize.Invoke(null, new object[] { resolution.Files, new[] { root } }));
            Assert.Single(canonicalMentions);
            var selection = method.Invoke(null, new object[] { canonicalMentions, Array.Empty<ResolvedAttachment>(), null!, 5 });
            Assert.Single(GetProperty<IReadOnlyList<ResolvedAttachment>>(selection!, "Attachments"));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void TryClaimPendingAttachments_RemovesSubmittedAndPreservesNextTurnAttachments()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var hostType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHost", throwOnError: true)!;
        var claim = hostType.GetMethod("TryClaimPendingAttachments", BindingFlags.Static | BindingFlags.NonPublic)!;
        var pending = new List<ResolvedAttachment>
        {
            Attachment("submitted-1", "submitted-1.md"),
            Attachment("submitted-2", "submitted-2.md"),
            Attachment("next-turn", "next-turn.md")
        };

        var claimed = Assert.IsType<bool>(claim.Invoke(null, new object[]
        {
            pending,
            new[] { "submitted-1", "submitted-2" }
        }));

        Assert.True(claimed);
        Assert.Equal(new[] { "next-turn" }, pending.Select(item => item.Id));
    }

    [Fact]
    public void TryClaimPendingAttachments_WhenAnAttachmentWasRemoved_LeavesSnapshotUnchanged()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var hostType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHost", throwOnError: true)!;
        var claim = hostType.GetMethod("TryClaimPendingAttachments", BindingFlags.Static | BindingFlags.NonPublic)!;
        var pending = new List<ResolvedAttachment>
        {
            Attachment("still-pending", "still-pending.md")
        };

        var claimed = Assert.IsType<bool>(claim.Invoke(null, new object[]
        {
            pending,
            new[] { "still-pending", "removed-during-build" }
        }));

        Assert.False(claimed);
        Assert.Equal(new[] { "still-pending" }, pending.Select(item => item.Id));
    }

    [Fact]
    public void PublishAttachmentStateAsync_DuringGenerationPublishesImmediately()
    {
        var assembly = LoadBuiltExtensionAssembly();
        var hostType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHost", throwOnError: true)!;
        var host = RuntimeHelpers.GetUninitializedObject(hostType);
        var stateType = assembly.GetType("ContextRelay.VSExtension.Services.ContextRelayHostState", throwOnError: true)!;
        var state = Activator.CreateInstance(stateType, nonPublic: true)!;
        var stateField = hostType.GetField("state", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var pendingField = hostType.GetField("pendingAttachments", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var pendingSyncField = hostType.GetField("pendingAttachmentsSync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var machineField = hostType.GetField("chatRequestStateMachine", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var machine = new ChatRequestStateMachine();
        Assert.True(machine.TryBegin(Array.Empty<string>(), CancellationToken.None, out var request));
        stateField.SetValue(host, state);
        pendingField.SetValue(host, new List<ResolvedAttachment> { Attachment("pending", "pending.md") });
        pendingSyncField.SetValue(host, new object());
        machineField.SetValue(host, machine);

        var method = hostType.GetMethod("PublishAttachmentStateAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = Assert.IsAssignableFrom<Task>(method.Invoke(host, new object[] { "updated" }));
        Assert.True(task.IsCompletedSuccessfully);
        Assert.Equal("updated", stateType.GetProperty("StatusMessage")!.GetValue(state));
        var published = Assert.IsAssignableFrom<IReadOnlyList<ResolvedAttachment>>(stateType.GetProperty("PendingAttachments")!.GetValue(state));
        Assert.Single(published);
        machine.Complete(request);
        machine.Dispose();
    }

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

    private static ResolvedFileMention Mention(string relativePath) => new()
    {
        AbsolutePath = Path.Combine("C:\\workspace", relativePath),
        WorkspaceRoot = "C:\\workspace",
        RelativePath = relativePath,
        Uri = "file:///" + relativePath
    };

    private static ResolvedAttachment Attachment(string id, string relativePath, string? absolutePath = null, string? root = null) => new()
    {
        Id = id,
        AbsolutePath = absolutePath ?? Path.Combine("C:\\workspace", relativePath),
        WorkspaceRoot = root ?? "C:\\workspace",
        RelativePath = relativePath,
        DisplayName = relativePath
    };

    private static T GetProperty<T>(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        return Assert.IsAssignableFrom<T>(property!.GetValue(target));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "ContextRelayVS.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
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
