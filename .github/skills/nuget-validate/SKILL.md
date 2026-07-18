---
name: nuget-validate
description: 'Validate NuGet package versions before adding or updating packages in .NET projects. Use when choosing a NuGet package version, checking package vulnerabilities, avoiding deprecated packages, auditing dotnet package list results, or requiring confirmation for packages not published in over one year.'
---

# NuGet Validate

## Overview

Use this skill to validate NuGet packages before adding or updating them in .NET projects. It complements the `nuget-manager` skill by focusing on package safety: known vulnerabilities, deprecation status, latest safe version selection, and package freshness.

The bundled validator is a single C# file-based app. It runs with `dotnet run <path-to-cs-file> -- ...` and does not require a `.csproj`.

## Prerequisites

- .NET SDK with file-based app support for `dotnet run app.cs` (documented for .NET 10 Preview 4 and later).
- Network access to the NuGet service index endpoint, represented here as `<NUGET_SERVICE_INDEX_ENDPOINT>`.
- `dotnet` CLI available on `PATH`.

## Core Rules

1. Do not add a package version that is deprecated.
2. Do not add a package version with known vulnerabilities.
3. Prefer the latest version that is not deprecated and has no known vulnerabilities.
4. If the candidate package version was last published more than 365 days ago, ask the user for confirmation before proceeding.
5. After adding or updating a package, audit the actual project dependency graph with `dotnet package list` or `dotnet list package`.
6. Use `dotnet add package` and `dotnet remove package` for package add/remove operations. Direct file edits are only acceptable for changing existing version values.

## Validator Commands

### PowerShell

```powershell
dotnet run .github\skills\nuget-validate\scripts\nuget-validate.cs -- latest-safe Newtonsoft.Json
dotnet run .github\skills\nuget-validate\scripts\nuget-validate.cs -- validate Newtonsoft.Json 13.0.3
dotnet run .github\skills\nuget-validate\scripts\nuget-validate.cs -- audit-project .\src\MyProject\MyProject.csproj
```

### Bash

```bash
dotnet run .github/skills/nuget-validate/scripts/nuget-validate.cs -- latest-safe Newtonsoft.Json
dotnet run .github/skills/nuget-validate/scripts/nuget-validate.cs -- validate Newtonsoft.Json 13.0.3
dotnet run .github/skills/nuget-validate/scripts/nuget-validate.cs -- audit-project ./src/MyProject/MyProject.csproj
```

### JSON Output

Add `--json` to `latest-safe`, `validate`, or `audit-project`:

```powershell
dotnet run .github\skills\nuget-validate\scripts\nuget-validate.cs -- validate Newtonsoft.Json 13.0.3 --json
```

## Workflows

### Add a Package Safely

1. Identify the target project file.
2. Find the latest safe version:
   - PowerShell: `dotnet run .github\skills\nuget-validate\scripts\nuget-validate.cs -- latest-safe <PACKAGE_ID>`
   - Bash: `dotnet run .github/skills/nuget-validate/scripts/nuget-validate.cs -- latest-safe <PACKAGE_ID>`
3. Validate the selected version:
   - PowerShell: `dotnet run .github\skills\nuget-validate\scripts\nuget-validate.cs -- validate <PACKAGE_ID> <VERSION>`
   - Bash: `dotnet run .github/skills/nuget-validate/scripts/nuget-validate.cs -- validate <PACKAGE_ID> <VERSION>`
4. If validation reports `isStale: true`, ask the user for confirmation before continuing.
5. Add the package:
   - PowerShell: `dotnet add .\src\MyProject\MyProject.csproj package <PACKAGE_ID> --version <VERSION>`
   - Bash: `dotnet add ./src/MyProject/MyProject.csproj package <PACKAGE_ID> --version <VERSION>`
6. Run restore/build commands used by the repository.
7. Audit the resolved dependency graph:
   - PowerShell: `dotnet run .github\skills\nuget-validate\scripts\nuget-validate.cs -- audit-project .\src\MyProject\MyProject.csproj`
   - Bash: `dotnet run .github/skills/nuget-validate/scripts/nuget-validate.cs -- audit-project ./src/MyProject/MyProject.csproj`

### Update an Existing Package Version

1. Verify the requested version exists on NuGet.org.
2. Validate the requested version with `validate <PACKAGE_ID> <VERSION>`.
3. If the package is stale, ask the user for confirmation before editing.
4. Determine whether versions are managed centrally in `Directory.Packages.props` or directly in a `.csproj`.
5. Update only the version value.
6. Run `dotnet restore`.
7. Run the `audit-project` command for affected projects.

## Command Behavior

| Command | Purpose |
| --- | --- |
| `latest-safe <package-id>` | Finds the newest stable listed version that is not deprecated and has no known vulnerabilities. Add `--include-prerelease` only when the user explicitly allows prerelease packages. |
| `validate <package-id> <version>` | Reports vulnerability, deprecation, and publish-age status for one package version. |
| `audit-project <project-or-solution>` | Runs `dotnet package list --project <path> --vulnerable --include-transitive`, falling back to `dotnet list <path> package --vulnerable --include-transitive` if needed. |

## Freshness Policy

- A package is stale when the selected version was last published more than 365 days ago.
- Staleness is a warning, not a vulnerability.
- Do not proceed with adding or updating a stale package until the user confirms.

## Troubleshooting

| Problem | Resolution |
| --- | --- |
| `dotnet run app.cs` is not supported | Install a .NET SDK that supports file-based apps, or run validation manually with NuGet.org metadata and `dotnet package list`. |
| NuGet.org cannot be reached | Stop and report the network/API failure; do not guess package safety. |
| `dotnet package list` is unavailable | Use the fallback `dotnet list <project> package --vulnerable --include-transitive`. |
| No safe version is found | Do not add the package unless the user explicitly accepts the risk and there is a documented mitigation. |

## References

- `scripts/nuget-validate.cs`
- Microsoft Learn or .NET Blog documentation for file-based apps, represented here as `<DOTNET_FILE_BASED_APP_DOCUMENTATION>`.
