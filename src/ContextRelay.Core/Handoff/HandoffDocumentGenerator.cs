using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.SharedStore;
using ContextRelay.Core.Utilities;

namespace ContextRelay.Core.Handoff;

public sealed class HandoffDocumentGenerator : IHandoffDocumentGenerator
{
    private readonly IClock clock;
    private readonly ISharedSessionStore? sharedSessionStore;

    public HandoffDocumentGenerator(ISharedSessionStore? sharedSessionStore = null, IClock? clock = null)
    {
        this.sharedSessionStore = sharedSessionStore;
        this.clock = clock ?? SystemClock.Instance;
    }

    public string GeneratePlan(HandoffContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var builder = new StringBuilder();
        builder.AppendLine($"## Update ({FormatTimestamp(clock.UtcNow)})");
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(context.SearchSummary))
        {
            builder.AppendLine("### Summary");
            builder.AppendLine();
            builder.AppendLine(context.SearchSummary);
            builder.AppendLine();
        }

        builder.AppendLine("### Saved Context");
        builder.AppendLine();

        if (context.Snippets.Count > 0)
        {
            foreach (var snippet in context.Snippets)
            {
                builder.AppendLine($"- **{snippet.Name}** ({snippet.Source}) — {TruncateSnippet(snippet.Snippet)}...");
            }
        }
        else
        {
            builder.AppendLine("_No snippets saved._");
        }

        return builder.ToString().TrimEnd();
    }

    public string GenerateTasks()
    {
        return string.Join(
            "\n",
            $"## Update ({FormatTimestamp(clock.UtcNow)})",
            string.Empty,
            "### Open Tasks",
            string.Empty,
            "- [ ] Review search results and refine queries as needed.",
            "- [ ] Select important text from preview and add it to the Handoff tab.",
            "- [ ] Review saved handoff excerpts and remove anything unnecessary.",
            "- [ ] Generate HANDOFF.md before starting a new Copilot Chat session.") ;
    }

    public string GenerateTestPlan()
    {
        return string.Join(
            "\n",
            $"## Update ({FormatTimestamp(clock.UtcNow)})",
            string.Empty,
            "### Test Cases",
            string.Empty,
            "- Verify search results match expected content from Microsoft 365.",
            "- Confirm selected preview excerpts can be added to the Handoff tab.",
            "- Confirm saved handoff excerpts persist across VS Code window reloads.",
            "- Validate handoff document format and timestamps.");
    }

    public string GenerateHandoff(HandoffContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var builder = new StringBuilder();
        builder.AppendLine($"## Update ({FormatTimestamp(clock.UtcNow)})");
        builder.AppendLine();
        builder.AppendLine("### Current Decisions");
        builder.AppendLine();
        builder.AppendLine(context.SearchSummary ?? "_No search summary available._");
        builder.AppendLine();
        builder.AppendLine("### Open Questions");
        builder.AppendLine();
        builder.AppendLine("- _Add open questions here._");
        builder.AppendLine();
        builder.AppendLine("### Next Tasks");
        builder.AppendLine();
        builder.AppendLine("- _Add next tasks here._");
        builder.AppendLine();
        builder.AppendLine("### Saved Handoff Excerpts");
        builder.AppendLine();

        if (context.Snippets.Count > 0)
        {
            foreach (var snippet in context.Snippets)
            {
                builder.AppendLine($"### {snippet.Name}");
                builder.AppendLine($"- **Source**: {snippet.Source}");
                builder.AppendLine($"- **Saved**: {snippet.UpdatedAt}");
                if (!string.IsNullOrWhiteSpace(snippet.SourceUrl))
                {
                    builder.AppendLine($"- **Link**: {snippet.SourceUrl}");
                }

                builder.AppendLine();
                builder.AppendLine(snippet.Snippet);
                builder.AppendLine();
            }
        }
        else
        {
            builder.AppendLine("_No snippets saved._");
        }

        return builder.ToString().TrimEnd();
    }

    public async Task<HandoffGenerationResult> GenerateAsync(
        HandoffContext context,
        HandoffGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        options ??= new HandoffGenerationOptions();
        var outputDirectory = ResolveOutputDirectory(options);
        var workspaceRoot = ResolveWorkspaceRoot(options, outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var planPath = Path.Combine(outputDirectory, "PLAN.md");
        var tasksPath = Path.Combine(outputDirectory, "TASKS.md");
        var testPlanPath = Path.Combine(outputDirectory, "TEST_PLAN.md");
        var handoffPath = options.IncludeHandoffDocument ? Path.Combine(outputDirectory, "HANDOFF.md") : null;

        await AppendUpdateAsync(planPath, GeneratePlan(context), cancellationToken).ConfigureAwait(false);
        await AppendUpdateAsync(tasksPath, GenerateTasks(), cancellationToken).ConfigureAwait(false);
        await AppendUpdateAsync(testPlanPath, GenerateTestPlan(), cancellationToken).ConfigureAwait(false);
        if (handoffPath is not null)
        {
            await AppendUpdateAsync(handoffPath, GenerateHandoff(context), cancellationToken).ConfigureAwait(false);
        }

        var writtenFiles = new List<string> { planPath, tasksPath, testPlanPath };
        if (handoffPath is not null)
        {
            writtenFiles.Add(handoffPath);
        }

        if (sharedSessionStore is not null)
        {
            await sharedSessionStore.UpsertHandoffIndexAsync(
                new[]
                {
                    new SharedHandoffIndexItem
                    {
                        WorkspaceRoot = workspaceRoot,
                        UpdatedAt = clock.UtcNow.ToString("O"),
                        Docs = new HandoffDocumentPaths
                        {
                            Plan = ToStoredDocumentPath(planPath, workspaceRoot),
                            Tasks = ToStoredDocumentPath(tasksPath, workspaceRoot),
                            TestPlan = ToStoredDocumentPath(testPlanPath, workspaceRoot),
                            Handoff = handoffPath is null ? null : ToStoredDocumentPath(handoffPath, workspaceRoot)
                        }
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }

        return new HandoffGenerationResult
        {
            OutputDirectory = outputDirectory,
            WorkspaceRoot = workspaceRoot,
            PlanPath = planPath,
            TasksPath = tasksPath,
            TestPlanPath = testPlanPath,
            HandoffPath = handoffPath,
            WrittenFiles = writtenFiles
        };
    }

    private static string ResolveOutputDirectory(HandoffGenerationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
        {
            throw new ArgumentException("Output directory must not be empty.", nameof(options));
        }

        if (Path.IsPathRooted(options.OutputDirectory))
        {
            return Path.GetFullPath(options.OutputDirectory);
        }

        var baseDirectory = !string.IsNullOrWhiteSpace(options.WorkspaceRoot)
            ? options.WorkspaceRoot
            : !string.IsNullOrWhiteSpace(options.FallbackRootDirectory)
                ? options.FallbackRootDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return Path.GetFullPath(Path.Combine(baseDirectory!, options.OutputDirectory));
    }

    private static string ResolveWorkspaceRoot(HandoffGenerationOptions options, string outputDirectory)
    {
        if (!string.IsNullOrWhiteSpace(options.WorkspaceRoot))
        {
            return Path.GetFullPath(options.WorkspaceRoot);
        }

        if (!string.IsNullOrWhiteSpace(options.FallbackRootDirectory))
        {
            return Path.GetFullPath(options.FallbackRootDirectory);
        }

        if (Path.IsPathRooted(options.OutputDirectory))
        {
            return outputDirectory;
        }

        return Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    private static async Task AppendUpdateAsync(string filePath, string content, CancellationToken cancellationToken)
    {
        var prefix = File.Exists(filePath) && new FileInfo(filePath).Length > 0 ? "\n\n" : string.Empty;
        using var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync($"{prefix}{content.TrimEnd()}\n").ConfigureAwait(false);
    }

    private static string FormatTimestamp(DateTimeOffset timestamp)
    {
        return timestamp.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
    }

    private static string TruncateSnippet(string snippet)
    {
        if (snippet.Length <= 120)
        {
            return snippet;
        }

        return snippet.Substring(0, 120);
    }

    private static string ToStoredDocumentPath(string filePath, string workspaceRoot)
    {
        var fullFilePath = Path.GetFullPath(filePath);
        var fullWorkspaceRoot = EnsureTrailingSeparator(Path.GetFullPath(workspaceRoot));

        if (fullFilePath.StartsWith(fullWorkspaceRoot, StringComparison.OrdinalIgnoreCase))
        {
            return fullFilePath.Substring(fullWorkspaceRoot.Length).Replace('\\', '/');
        }

        return fullFilePath.Replace('\\', '/');
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
            path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
        {
            return path;
        }

        return path + Path.DirectorySeparatorChar;
    }
}
