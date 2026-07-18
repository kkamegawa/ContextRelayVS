using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

const string ServiceIndexUrl = "https://api.nuget.org/v3/index.json";
const int StaleAfterDays = 365;

var commandArgs = args.ToList();
var jsonOutput = commandArgs.Remove("--json");
var includePrerelease = commandArgs.Remove("--include-prerelease");

if (commandArgs.Count == 0 || commandArgs[0] is "-h" or "--help")
{
    PrintUsage();
    return 0;
}

try
{
    using var httpClient = new HttpClient(new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
    });
    httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("nuget-validate/1.0");

    var command = commandArgs[0].ToLowerInvariant();
    var result = command switch
    {
        "latest-safe" when commandArgs.Count >= 2 => await LatestSafeAsync(httpClient, commandArgs[1], includePrerelease),
        "validate" when commandArgs.Count >= 3 => await ValidateAsync(httpClient, commandArgs[1], commandArgs[2]),
        "audit-project" when commandArgs.Count >= 2 => await AuditProjectAsync(commandArgs[1]),
        _ => CommandResult.Error("Invalid command or missing arguments. Run with --help for usage.")
    };

    if (jsonOutput)
    {
        Console.WriteLine(ToJson(result));
    }
    else
    {
        PrintResult(result);
    }

    return result.ExitCode;
}
catch (Exception ex)
{
    var result = CommandResult.Error(ex.Message);
    if (jsonOutput)
    {
        Console.WriteLine(ToJson(result));
    }
    else
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
    }

    return 1;
}

async Task<CommandResult> LatestSafeAsync(HttpClient httpClient, string packageId, bool includePrerelease)
{
    var versions = await GetPackageVersionsAsync(httpClient, packageId);
    if (versions.Count == 0)
    {
        return CommandResult.Error($"Package '{packageId}' was not found.");
    }

    foreach (var version in versions.OrderByDescending(v => SemVerKey.Parse(v), SemVerKeyComparer.Instance))
    {
        if (!includePrerelease && version.Contains('-', StringComparison.Ordinal))
        {
            continue;
        }

        var package = await GetPackageAsync(httpClient, packageId, version);
        if (package is null || !package.Listed)
        {
            continue;
        }

        var validation = CreateValidation(package);
        if (!validation.HasErrors)
        {
            return CommandResult.Success("Latest safe version found.", validation);
        }
    }

    var prereleaseHint = includePrerelease ? string.Empty : " Try --include-prerelease only if the user explicitly allows prerelease packages.";
    return CommandResult.Error($"No stable non-deprecated, non-vulnerable version found for '{packageId}'.{prereleaseHint}");
}

async Task<CommandResult> ValidateAsync(HttpClient httpClient, string packageId, string version)
{
    var package = await GetPackageAsync(httpClient, packageId, version);
    if (package is null)
    {
        return CommandResult.Error($"Package '{packageId}' version '{version}' was not found.");
    }

    var validation = CreateValidation(package);
    return new CommandResult
    {
        Succeeded = !validation.HasErrors,
        ExitCode = validation.HasErrors ? 1 : 0,
        Message = validation.HasErrors ? "Package validation failed." : "Package validation passed.",
        Validation = validation
    };
}

async Task<CommandResult> AuditProjectAsync(string projectOrSolutionPath)
{
    var first = await RunDotnetAsync("package", "list", "--project", projectOrSolutionPath, "--vulnerable", "--include-transitive");
    var audit = first.ExitCode == 0 || !LooksLikeUnsupportedCommand(first.Output)
        ? first
        : await RunDotnetAsync("list", projectOrSolutionPath, "package", "--vulnerable", "--include-transitive");

    var hasVulnerabilities = LooksLikeVulnerabilityOutput(audit.Output);
    var commandFailed = audit.ExitCode != 0;
    return new CommandResult
    {
        Succeeded = !commandFailed && !hasVulnerabilities,
        ExitCode = commandFailed || hasVulnerabilities ? 1 : 0,
        Message = commandFailed
            ? "Project audit command failed."
            : hasVulnerabilities
                ? "Project audit reported vulnerable packages."
                : "Project audit completed without reported vulnerable packages.",
        Audit = new AuditResult(projectOrSolutionPath, audit.Command, audit.ExitCode, audit.Output, hasVulnerabilities)
    };
}

ValidationResult CreateValidation(PackageInfo package)
{
    var issues = new List<ValidationIssue>();

    if (!package.Listed)
    {
        issues.Add(new ValidationIssue(IssueSeverity.Error, "Package version is unlisted."));
    }

    foreach (var vulnerability in package.Vulnerabilities)
    {
        issues.Add(new ValidationIssue(IssueSeverity.Error, $"Known vulnerability: {vulnerability}"));
    }

    if (package.IsDeprecated)
    {
        issues.Add(new ValidationIssue(IssueSeverity.Error, $"Package is deprecated. {package.DeprecationMessage}".Trim()));
    }

    var daysSincePublished = package.Published == DateTimeOffset.MinValue
        ? int.MaxValue
        : (int)(DateTimeOffset.UtcNow - package.Published).TotalDays;

    if (daysSincePublished > StaleAfterDays)
    {
        issues.Add(new ValidationIssue(IssueSeverity.Warning, $"Package was last published {daysSincePublished} days ago. User confirmation is required."));
    }

    return new ValidationResult(package, daysSincePublished, daysSincePublished > StaleAfterDays, issues);
}

async Task<PackageInfo?> GetPackageAsync(HttpClient httpClient, string packageId, string requestedVersion)
{
    var leaves = await GetRegistrationLeavesAsync(httpClient, packageId);
    foreach (var leaf in leaves)
    {
        if (!leaf.TryGetProperty("catalogEntry", out var catalogEntry))
        {
            continue;
        }

        var version = GetString(catalogEntry, "version");
        if (!string.Equals(version, requestedVersion, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        return MapPackage(catalogEntry);
    }

    return null;
}

async Task<List<string>> GetPackageVersionsAsync(HttpClient httpClient, string packageId)
{
    var leaves = await GetRegistrationLeavesAsync(httpClient, packageId);
    var versions = new List<string>();

    foreach (var leaf in leaves)
    {
        if (leaf.TryGetProperty("catalogEntry", out var catalogEntry) &&
            GetString(catalogEntry, "version") is { Length: > 0 } version)
        {
            versions.Add(version);
        }
    }

    return versions;
}

async Task<List<JsonElement>> GetRegistrationLeavesAsync(HttpClient httpClient, string packageId)
{
    var baseUrl = await GetRegistrationBaseUrlAsync(httpClient);
    var url = $"{baseUrl.TrimEnd('/')}/{packageId.ToLowerInvariant()}/index.json";
    using var indexDocument = await GetJsonDocumentAsync(httpClient, url);
    var root = indexDocument.RootElement.Clone();

    if (!root.TryGetProperty("items", out var pages))
    {
        return [];
    }

    var leaves = new List<JsonElement>();
    foreach (var page in pages.EnumerateArray())
    {
        if (page.TryGetProperty("items", out var inlineItems))
        {
            leaves.AddRange(inlineItems.EnumerateArray().Select(item => item.Clone()));
            continue;
        }

        if (GetString(page, "@id") is not { Length: > 0 } pageUrl)
        {
            continue;
        }

        using var pageDocument = await GetJsonDocumentAsync(httpClient, pageUrl);
        if (pageDocument.RootElement.TryGetProperty("items", out var pageItems))
        {
            leaves.AddRange(pageItems.EnumerateArray().Select(item => item.Clone()));
        }
    }

    return leaves;
}

async Task<string> GetRegistrationBaseUrlAsync(HttpClient httpClient)
{
    using var document = await GetJsonDocumentAsync(httpClient, ServiceIndexUrl);
    if (!document.RootElement.TryGetProperty("resources", out var resources))
    {
        throw new InvalidOperationException("NuGet service index did not contain resources.");
    }

    // RegistrationsBaseUrl has several variants. Only RegistrationsBaseUrl/3.6.0 (and /Versioned)
    // point to registration5-gz-semver2 which includes the deprecation field in catalog entries.
    // The base RegistrationsBaseUrl points to registration5-semver1 which omits the deprecation field.
    // Priority (highest first): /Versioned > /3.6.0 > /3.4.0 > /3.0.0-* > base
    static int RegistrationTypePriority(string? type) => type switch
    {
        not null when type.EndsWith("/Versioned", StringComparison.OrdinalIgnoreCase) => 4,
        not null when type.EndsWith("/3.6.0", StringComparison.OrdinalIgnoreCase) => 3,
        not null when type.EndsWith("/3.4.0", StringComparison.OrdinalIgnoreCase) => 2,
        not null when type.StartsWith("RegistrationsBaseUrl/", StringComparison.OrdinalIgnoreCase) => 1,
        not null when type.Equals("RegistrationsBaseUrl", StringComparison.OrdinalIgnoreCase) => 0,
        _ => -1
    };

    string? bestId = null;
    var bestPriority = -1;

    foreach (var resource in resources.EnumerateArray())
    {
        if (!resource.TryGetProperty("@type", out var typeElement))
        {
            continue;
        }

        var types = typeElement.ValueKind == JsonValueKind.Array
            ? typeElement.EnumerateArray().Select(t => t.GetString()).ToList()
            : (IList<string?>)[typeElement.GetString()];

        var priority = types.Max(RegistrationTypePriority);
        if (priority < 0)
        {
            continue;
        }

        if (GetString(resource, "@id") is { Length: > 0 } id && priority > bestPriority)
        {
            bestId = id;
            bestPriority = priority;
        }
    }

    return bestId ?? throw new InvalidOperationException("RegistrationsBaseUrl was not found in NuGet service index.");
}

async Task<JsonDocument> GetJsonDocumentAsync(HttpClient httpClient, string url)
{
    using var response = await httpClient.GetAsync(url);
    if (!response.IsSuccessStatusCode)
    {
        throw new InvalidOperationException($"NuGet API request failed ({(int)response.StatusCode}) for {url}");
    }

    await using var stream = await response.Content.ReadAsStreamAsync();
    return await JsonDocument.ParseAsync(stream);
}

PackageInfo MapPackage(JsonElement catalogEntry)
{
    var packageId = GetString(catalogEntry, "id") ?? "unknown";
    var version = GetString(catalogEntry, "version") ?? "unknown";
    var published = GetDate(catalogEntry, "published");
    var listed = !catalogEntry.TryGetProperty("listed", out var listedElement) || listedElement.GetBoolean();
    var deprecationMessage = GetDeprecationMessage(catalogEntry);
    var vulnerabilities = GetVulnerabilities(catalogEntry);

    return new PackageInfo(
        packageId,
        version,
        published,
        listed,
        !string.IsNullOrWhiteSpace(deprecationMessage),
        deprecationMessage,
        vulnerabilities);
}

List<string> GetVulnerabilities(JsonElement catalogEntry)
{
    if (!catalogEntry.TryGetProperty("vulnerabilities", out var vulnerabilities) ||
        vulnerabilities.ValueKind != JsonValueKind.Array)
    {
        return [];
    }

    var results = new List<string>();
    foreach (var vulnerability in vulnerabilities.EnumerateArray())
    {
        var severity = GetString(vulnerability, "severity") ?? "unknown severity";
        var advisoryUrl = GetString(vulnerability, "advisoryUrl") ?? GetString(vulnerability, "@id") ?? "unknown advisory";
        results.Add($"{severity}: {advisoryUrl}");
    }

    return results;
}

string? GetDeprecationMessage(JsonElement catalogEntry)
{
    if (!catalogEntry.TryGetProperty("deprecation", out var deprecation) ||
        deprecation.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
    {
        return null;
    }

    var parts = new List<string>();
    if (GetString(deprecation, "message") is { Length: > 0 } message)
    {
        parts.Add(message);
    }

    if (deprecation.TryGetProperty("reasons", out var reasons) && reasons.ValueKind == JsonValueKind.Array)
    {
        var reasonText = string.Join(", ", reasons.EnumerateArray().Select(r => r.GetString()).Where(r => !string.IsNullOrWhiteSpace(r)));
        if (!string.IsNullOrWhiteSpace(reasonText))
        {
            parts.Add($"Reasons: {reasonText}");
        }
    }

    if (deprecation.TryGetProperty("alternatePackage", out var alternate) &&
        GetString(alternate, "id") is { Length: > 0 } alternateId)
    {
        var range = GetString(alternate, "range");
        parts.Add(string.IsNullOrWhiteSpace(range) ? $"Alternate: {alternateId}" : $"Alternate: {alternateId} {range}");
    }

    return parts.Count == 0 ? "No deprecation details were provided." : string.Join(" ", parts);
}

string? GetString(JsonElement element, string propertyName)
{
    return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
        ? value.GetString()
        : null;
}

DateTimeOffset GetDate(JsonElement element, string propertyName)
{
    return GetString(element, propertyName) is { Length: > 0 } value &&
           DateTimeOffset.TryParse(value, out var parsed)
        ? parsed
        : DateTimeOffset.MinValue;
}

async Task<ProcessResult> RunDotnetAsync(params string[] arguments)
{
    var startInfo = new ProcessStartInfo("dotnet")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };

    foreach (var argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start dotnet process.");
    var output = await process.StandardOutput.ReadToEndAsync();
    var error = await process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();

    return new ProcessResult(BuildCommandDisplay(arguments), process.ExitCode, string.Join(Environment.NewLine, [output, error]).Trim());
}

bool LooksLikeUnsupportedCommand(string output)
{
    return output.Contains("Unrecognized command or argument", StringComparison.OrdinalIgnoreCase) ||
           output.Contains("認識されないコマンドまたは引数", StringComparison.OrdinalIgnoreCase) ||
           output.Contains("Could not execute because the specified command or file was not found", StringComparison.OrdinalIgnoreCase);
}

bool LooksLikeVulnerabilityOutput(string output)
{
    return output.Contains("has the following vulnerable packages", StringComparison.OrdinalIgnoreCase) ||
           output.Contains("vulnerable packages", StringComparison.OrdinalIgnoreCase) &&
           !output.Contains("no vulnerable packages", StringComparison.OrdinalIgnoreCase);
}

void PrintResult(CommandResult result)
{
    Console.WriteLine(result.Message);

    if (result.Validation is { } validation)
    {
        Console.WriteLine($"Package: {validation.Package.PackageId} {validation.Package.Version}");
        Console.WriteLine($"Published: {(validation.Package.Published == DateTimeOffset.MinValue ? "unknown" : validation.Package.Published.ToString("yyyy-MM-dd"))}");
        Console.WriteLine($"Days since published: {(validation.DaysSincePublished == int.MaxValue ? "unknown" : validation.DaysSincePublished)}");
        Console.WriteLine($"Deprecated: {validation.Package.IsDeprecated}");
        Console.WriteLine($"Vulnerabilities: {validation.Package.Vulnerabilities.Count}");
        Console.WriteLine($"Stale: {validation.IsStale}");

        foreach (var issue in validation.Issues)
        {
            Console.WriteLine($"[{issue.Severity}] {issue.Message}");
        }
    }

    if (result.Audit is { } audit)
    {
        Console.WriteLine($"Command: {audit.Command}");
        Console.WriteLine($"Exit code: {audit.ExitCode}");
        Console.WriteLine($"Has vulnerabilities: {audit.HasVulnerabilities}");
        if (!string.IsNullOrWhiteSpace(audit.Output))
        {
            Console.WriteLine(audit.Output);
        }
    }
}

string ToJson(CommandResult result)
{
    var builder = new StringBuilder();
    builder.AppendLine("{");
    AppendBoolProperty(builder, "succeeded", result.Succeeded, 1);
    AppendIntProperty(builder, "exitCode", result.ExitCode, 1);
    AppendStringProperty(builder, "message", result.Message, 1, trailingComma: result.Validation is not null || result.Audit is not null);

    if (result.Validation is { } validation)
    {
        AppendIndent(builder, 1).AppendLine("\"validation\": {");
        AppendIndent(builder, 2).AppendLine("\"package\": {");
        AppendStringProperty(builder, "packageId", validation.Package.PackageId, 3);
        AppendStringProperty(builder, "version", validation.Package.Version, 3);
        AppendStringProperty(builder, "published", validation.Package.Published == DateTimeOffset.MinValue ? null : validation.Package.Published.ToString("O"), 3);
        AppendBoolProperty(builder, "listed", validation.Package.Listed, 3);
        AppendBoolProperty(builder, "isDeprecated", validation.Package.IsDeprecated, 3);
        AppendStringProperty(builder, "deprecationMessage", validation.Package.DeprecationMessage, 3);
        AppendStringArray(builder, "vulnerabilities", validation.Package.Vulnerabilities, 3, trailingComma: false);
        AppendIndent(builder, 2).AppendLine("},");
        AppendIntProperty(builder, "daysSincePublished", validation.DaysSincePublished, 2);
        AppendBoolProperty(builder, "isStale", validation.IsStale, 2);
        AppendIndent(builder, 2).AppendLine("\"issues\": [");
        for (var i = 0; i < validation.Issues.Count; i++)
        {
            var issue = validation.Issues[i];
            AppendIndent(builder, 3).AppendLine("{");
            AppendStringProperty(builder, "severity", issue.Severity.ToString(), 4);
            AppendStringProperty(builder, "message", issue.Message, 4, trailingComma: false);
            AppendIndent(builder, 3).Append(i == validation.Issues.Count - 1 ? "}" : "},").AppendLine();
        }

        AppendIndent(builder, 2).AppendLine("]");
        AppendIndent(builder, 1).Append(result.Audit is null ? "}" : "},").AppendLine();
    }

    if (result.Audit is { } audit)
    {
        AppendIndent(builder, 1).AppendLine("\"audit\": {");
        AppendStringProperty(builder, "target", audit.Target, 2);
        AppendStringProperty(builder, "command", audit.Command, 2);
        AppendIntProperty(builder, "exitCode", audit.ExitCode, 2);
        AppendStringProperty(builder, "output", audit.Output, 2);
        AppendBoolProperty(builder, "hasVulnerabilities", audit.HasVulnerabilities, 2, trailingComma: false);
        AppendIndent(builder, 1).AppendLine("}");
    }

    builder.AppendLine("}");
    return builder.ToString();
}

string BuildCommandDisplay(IEnumerable<string> arguments)
{
    return string.Join(" ", new[] { "dotnet" }.Concat(arguments.Select(QuoteArgument)));
}

string QuoteArgument(string argument)
{
    if (argument.Length == 0)
    {
        return "\"\"";
    }

    var needsQuoting = argument.Any(char.IsWhiteSpace) || argument.Contains('"') || argument.Contains('\'');
    if (!needsQuoting)
    {
        return argument;
    }

    return "\"" + argument.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}

StringBuilder AppendIndent(StringBuilder builder, int level) => builder.Append(' ', level * 2);

void AppendStringProperty(StringBuilder builder, string name, string? value, int indent, bool trailingComma = true)
{
    AppendIndent(builder, indent)
        .Append('"').Append(EscapeJson(name)).Append("\": ")
        .Append(value is null ? "null" : $"\"{EscapeJson(value)}\"")
        .Append(trailingComma ? "," : string.Empty)
        .AppendLine();
}

void AppendBoolProperty(StringBuilder builder, string name, bool value, int indent, bool trailingComma = true)
{
    AppendIndent(builder, indent)
        .Append('"').Append(EscapeJson(name)).Append("\": ")
        .Append(value ? "true" : "false")
        .Append(trailingComma ? "," : string.Empty)
        .AppendLine();
}

void AppendIntProperty(StringBuilder builder, string name, int value, int indent, bool trailingComma = true)
{
    AppendIndent(builder, indent)
        .Append('"').Append(EscapeJson(name)).Append("\": ")
        .Append(value)
        .Append(trailingComma ? "," : string.Empty)
        .AppendLine();
}

void AppendStringArray(StringBuilder builder, string name, IReadOnlyList<string> values, int indent, bool trailingComma = true)
{
    AppendIndent(builder, indent)
        .Append('"').Append(EscapeJson(name)).Append("\": [");

    for (var i = 0; i < values.Count; i++)
    {
        builder.Append('"').Append(EscapeJson(values[i])).Append('"');
        if (i < values.Count - 1)
        {
            builder.Append(", ");
        }
    }

    builder.Append(']').Append(trailingComma ? "," : string.Empty).AppendLine();
}

string EscapeJson(string value)
{
    return value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\t", "\\t", StringComparison.Ordinal);
}

void PrintUsage()
{
    Console.WriteLine("""
    nuget-validate - validate NuGet package safety before package changes

    Usage:
      dotnet run scripts/nuget-validate.cs -- latest-safe <package-id> [--include-prerelease] [--json]
      dotnet run scripts/nuget-validate.cs -- validate <package-id> <version> [--json]
      dotnet run scripts/nuget-validate.cs -- audit-project <project-or-solution> [--json]

    Rules:
      - Deprecated or vulnerable packages fail validation.
      - Packages published more than 365 days ago are stale and require user confirmation.
      - audit-project checks the resolved project graph with dotnet package list/list package.
    """);
}

public sealed record CommandResult
{
    public bool Succeeded { get; init; }
    public int ExitCode { get; init; }
    public required string Message { get; init; }
    public ValidationResult? Validation { get; init; }
    public AuditResult? Audit { get; init; }

    public static CommandResult Success(string message, ValidationResult validation) =>
        new() { Succeeded = true, ExitCode = 0, Message = message, Validation = validation };

    public static CommandResult Error(string message) =>
        new() { Succeeded = false, ExitCode = 1, Message = message };
}

public sealed record ValidationResult(
    PackageInfo Package,
    int DaysSincePublished,
    bool IsStale,
    IReadOnlyList<ValidationIssue> Issues)
{
    public bool HasErrors => Issues.Any(issue => issue.Severity == IssueSeverity.Error);
}

public sealed record PackageInfo(
    string PackageId,
    string Version,
    DateTimeOffset Published,
    bool Listed,
    bool IsDeprecated,
    string? DeprecationMessage,
    IReadOnlyList<string> Vulnerabilities);

public sealed record ValidationIssue(IssueSeverity Severity, string Message);

public sealed record AuditResult(
    string Target,
    string Command,
    int ExitCode,
    string Output,
    bool HasVulnerabilities);

public sealed record ProcessResult(string Command, int ExitCode, string Output);

public enum IssueSeverity
{
    Warning,
    Error
}

public sealed record SemVerKey(int[] Parts, SemVerIdentifier[] Prerelease)
{
    public static SemVerKey Parse(string version)
    {
        var normalized = version.Split('+', 2)[0];
        var split = normalized.Split('-', 2);
        var parts = split[0]
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0)
            .ToArray();

        var prerelease = split.Length > 1
            ? split[1].Split('.', StringSplitOptions.RemoveEmptyEntries).Select(SemVerIdentifier.Parse).ToArray()
            : [];

        return new SemVerKey(parts, prerelease);
    }
}

public sealed class SemVerKeyComparer : IComparer<SemVerKey>
{
    public static readonly SemVerKeyComparer Instance = new();

    public int Compare(SemVerKey? x, SemVerKey? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var length = Math.Max(x.Parts.Length, y.Parts.Length);
        for (var i = 0; i < length; i++)
        {
            var left = i < x.Parts.Length ? x.Parts[i] : 0;
            var right = i < y.Parts.Length ? y.Parts[i] : 0;
            var comparison = left.CompareTo(right);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        if (x.Prerelease.Length == 0 && y.Prerelease.Length > 0)
        {
            return 1;
        }

        if (x.Prerelease.Length > 0 && y.Prerelease.Length == 0)
        {
            return -1;
        }

        var prereleaseLength = Math.Max(x.Prerelease.Length, y.Prerelease.Length);
        for (var i = 0; i < prereleaseLength; i++)
        {
            if (i >= x.Prerelease.Length)
            {
                return -1;
            }

            if (i >= y.Prerelease.Length)
            {
                return 1;
            }

            var comparison = ComparePrereleaseIdentifier(x.Prerelease[i], y.Prerelease[i]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    int ComparePrereleaseIdentifier(SemVerIdentifier left, SemVerIdentifier right)
    {
        if (left.IsNumeric && right.IsNumeric)
        {
            return left.NumericValue.CompareTo(right.NumericValue);
        }

        if (left.IsNumeric)
        {
            return -1;
        }

        if (right.IsNumeric)
        {
            return 1;
        }

        return string.Compare(left.Value, right.Value, StringComparison.Ordinal);
    }
}

public sealed record SemVerIdentifier(string Value, bool IsNumeric, int NumericValue)
{
    public static SemVerIdentifier Parse(string value)
    {
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? new SemVerIdentifier(value, true, number)
            : new SemVerIdentifier(value, false, 0);
    }
}
