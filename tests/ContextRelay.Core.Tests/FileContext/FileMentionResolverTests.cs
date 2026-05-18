using System;
using System.IO;
using System.Threading.Tasks;
using ContextRelay.Core.FileContext;
using Xunit;

namespace ContextRelay.Core.Tests.FileContext;

public sealed class FileMentionResolverTests : IDisposable
{
    private readonly string root;

    public FileMentionResolverTests()
    {
        root = Path.Combine(Path.GetTempPath(), "context-relay-vs-file-mentions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    [Fact]
    public void ExtractCandidates_IgnoresCSharpAndIssueReferences()
    {
        var candidates = FileMentionResolver.ExtractCandidates("Use C# for API #123 and read #docs/plan.md and #\"notes/release plan.md\"");

        Assert.Collection(
            candidates,
            first => Assert.Equal("docs/plan.md", first.RawPath),
            second => Assert.Equal("notes/release plan.md", second.RawPath));
    }

    [Fact]
    public void Resolve_ResolvesWorkspaceRelativeFilesAndStripsMentions()
    {
        WriteFile("docs\\plan.md", "Ship checklist");

        var result = FileMentionResolver.Resolve("Summarize #docs/plan.md for me", new[] { root });

        Assert.Empty(result.Errors);
        Assert.Equal("Summarize for me", result.CleanedPrompt);
        Assert.Single(result.Files);
        Assert.Equal("docs/plan.md", result.Files[0].RelativePath);
        Assert.StartsWith("file:///", result.Files[0].Uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_RejectsPathTraversalOutsideWorkspace()
    {
        var outsideRoot = Path.Combine(Path.GetTempPath(), "context-relay-vs-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideRoot);
        File.WriteAllText(Path.Combine(outsideRoot, "secret.md"), "secret");

        try
        {
            var relativeEscape = Path.Combine("..", Path.GetFileName(outsideRoot), "secret.md");
            var result = FileMentionResolver.Resolve($"Inspect #{relativeEscape}", new[] { root });

            Assert.Empty(result.Files);
            Assert.Contains("outside", result.Errors[0], StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(outsideRoot, recursive: true);
        }
    }

    [Fact]
    public void Resolve_RejectsUnsupportedExtensions()
    {
        WriteFile("capture.pcap", "binary");

        var result = FileMentionResolver.Resolve("Inspect #capture.pcap", new[] { root });

        Assert.Empty(result.Files);
        Assert.Contains("Unsupported file type", result.Errors[0]);
    }

    [Fact]
    public void Resolve_AllowsDotfilesAndSupportedBasenames()
    {
        WriteFile(".env", "KEY=value");
        WriteFile("Dockerfile", "FROM scratch");

        var envResult = FileMentionResolver.Resolve("Check #.env", new[] { root });
        var dockerResult = FileMentionResolver.Resolve("Check #Dockerfile", new[] { root });

        Assert.Empty(envResult.Errors);
        Assert.Single(envResult.Files);
        Assert.Empty(dockerResult.Errors);
        Assert.Single(dockerResult.Files);
    }

    [Fact]
    public void Resolve_RequiresWorkspaceRootWhenMentionsExist()
    {
        var result = FileMentionResolver.Resolve("Summarize #README.md", Array.Empty<string>());

        Assert.Empty(result.Files);
        Assert.Contains("opened solution or folder", result.Errors[0]);
    }

    [Fact]
    public void Resolve_EnforcesMaximumMentionCount()
    {
        for (var index = 0; index < FileMentionResolver.MaxFileMentions + 1; index++)
        {
            WriteFile($"file{index}.md", index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var result = FileMentionResolver.Resolve("Read #file0.md #file1.md #file2.md #file3.md #file4.md #file5.md", new[] { root });

        Assert.Empty(result.Files);
        Assert.Contains("up to", result.Errors[0]);
    }

    [Fact]
    public async Task BuildWorkIqPromptAsync_AppendsBoundedLocalFileSections()
    {
        var path = WriteFile("notes.md", new string('x', FileContextPromptBuilder.MaxWorkIqFileChars + 100));
        var resolved = new[]
        {
            new ResolvedFileMention
            {
                AbsolutePath = path,
                WorkspaceRoot = root,
                RelativePath = "notes.md",
                Uri = new Uri(path).AbsoluteUri
            }
        };

        var prompt = await FileContextPromptBuilder.BuildWorkIqPromptAsync("Summarize", resolved, TestContext.Current.CancellationToken);

        Assert.Contains("ContextRelay local file context", prompt);
        Assert.Contains("[File: notes.md]", prompt);
        Assert.Contains("[truncated", prompt);
        Assert.True(prompt.Length <= "Summarize\n\nContextRelay local file context:\n".Length + "[File: notes.md]\n".Length + FileContextPromptBuilder.MaxWorkIqFileChars);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private string WriteFile(string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content);
        return path;
    }
}
