---
name: contextrelay-vs-options-debugging
description: Debug Visual Studio Tools > Options registration issues for any Visual Studio extension. Use when agents need to investigate missing option pages, ActivityLog collection, VSIX/pkgdef registration, Experimental Instance mismatches, package activation failures, or in-proc package loading problems.
---

# Visual Studio Options Registration Debugging

This skill is a reusable playbook for diagnosing why a Visual Studio extension's **Tools > Options** category/page does not appear or cannot load.  
It is designed for **any extension architecture** (VSSDK in-proc, VisualStudio.Extensibility out-of-proc, or hybrid VSIX).

Primary workflow:
1. Identify the exact Visual Studio instance being inspected.
2. Collect a deterministic ActivityLog (`/log`).
3. Validate VSIX and pkgdef/package registration artifacts.
4. Separate "category invisible" failures from "page load failed" failures.
5. Only then modify packaging or registration.

Use these sample scripts first (they are examples and can be adapted to your extension/repo):
- [get-active-visual-studio-instance.ps1](sample_codes/get-active-visual-studio-instance.ps1)
- [start-exp-with-log.ps1](sample_codes/start-exp-with-log.ps1)
- [inspect-vsix-contents.ps1](sample_codes/inspect-vsix-contents.ps1)

## Key Concepts

### 1) Instance accuracy comes first

Do not assume the visible Visual Studio window is `/RootSuffix Exp`.  
Always inspect the active `devenv.exe` command line before checking registry/profile folders.

### 2) `/log` must be explicit when logs are missing

Activity logs are only written for sessions launched with `/log`.  
If no log file exists, relaunch with an explicit output path and use that session as your evidence source.

### 3) Manifest/image discovery and pkgdef discovery are different pipelines

An extension can be visible to one discovery path (for example image/manifest scanning) and still be absent from pkgdef merge/registration.  
Always verify both:
- where VS searches manifests/images
- where VS searches pkgdefs/packages

### 4) Prefer one authoritative install path

When debugging Options registration, avoid mixing install paths (for example "main VSIX + separate hand-placed sidecar") unless you can prove both are supported for your VS version.  
If one path is unsupported or ignored, keep registration on the path Visual Studio actually installs and merges.

### 5) Generated pkgdef is usually authoritative

For VSSDK package registration, prefer VSSDK-generated pkgdef output over static handwritten pkgdef files.  
Static copies often drift and can hide build/runtime mismatches.

### 6) Distinguish two failure classes

- **Options category does not appear** → usually registration/discovery/VSIX payload problem.
- **Category appears but page fails to load** → usually package activation/dependency/load-context problem.

Treat these as separate branches with separate evidence.

## Common Patterns

### Pattern 1: Identify the active Visual Studio target

```powershell
Get-CimInstance Win32_Process -Filter "name = 'devenv.exe'" |
    Select-Object ProcessId, ExecutablePath, CommandLine
```

### Pattern 2: Start Visual Studio with deterministic `/log`

```powershell
$logPath = Join-Path $env:APPDATA 'Microsoft\VisualStudio\ExtensionDebug-ActivityLog.xml'
$devenv = 'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\devenv.exe'
Start-Process -FilePath $devenv -ArgumentList '/RootSuffix', 'Exp', '/Log', $logPath
```

### Pattern 3: Inspect produced VSIX payload

```powershell
$vsix = Get-ChildItem '.\src' -Filter *.vsix -Recurse |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($vsix.FullName)
try {
    $zip.Entries |
        Where-Object { $_.FullName -match 'extension\.vsixmanifest|pkgdef|Package\.dll' } |
        Select-Object -ExpandProperty FullName
}
finally {
    $zip.Dispose()
}
```

### Pattern 4: Check for page-load signatures in ActivityLog

```powershell
$logPath = Join-Path $env:APPDATA 'Microsoft\VisualStudio\ExtensionDebug-ActivityLog.xml'
Select-String -Path $logPath -Pattern 'No InprocServer32 registered for package|CreateInstance failed|FileNotFoundException' -Context 2,4
```

## Investigation Checklist

1. **Active instance**
   - Confirm command line and root suffix.
   - Confirm exact VS SKU/version/channel.
2. **Log availability**
   - Use explicit `/log` and retain that file as evidence.
3. **Profile alignment**
   - Match local/roaming paths to the exact instance under test.
4. **Build artifact validation**
   - Check generated manifest and final VSIX payload.
   - Check pkgdef generation output from build artifacts.
5. **Runtime diagnostics**
   - Review ActivityLog for package registration/load failures.
   - If category appears but page load fails, prioritize package activation diagnostics.
6. **Registration strategy validation**
   - Confirm your chosen install path is actually supported by current VS behavior.
   - Avoid mixing unsupported "manual copy" paths with installer-managed paths.

## Known Installer Failure: invalid `[installdir]\Common7\IDE\VSExtensions\...` path

### Symptom signature

- VSIX installation fails before payload deployment with `System.InvalidOperationException`.
- VSIXInstaller log contains a message similar to:
  - `manifest.json ... path '[installdir]\Common7\IDE\VSExtensions\<hash>' is invalid`
- Failure appears during `PackageInstaller.ValidatePackage(...)` and installation rolls back.

### Why this happens

This usually indicates a **packaging-level manifest/path mismatch** for a hybrid extension (VisualStudio.Extensibility + VSSDK payload), not a runtime code issue.  
In observed cases, the installer operated with `PerMachine: False` while the generated installer manifest path used a machine-rooted token form that was rejected during validation.

### What to check first

1. Confirm this is an installer validation failure (not ActivityLog runtime activation failure).
2. Inspect final produced VSIX metadata and any generated install manifest traces.
3. Map the invalid path entry back to your manifest patch/repack logic (for example custom post-`CreateVsixContainer` tasks).
4. Reconcile install scope and manifest path/token generation rules for the targeted VS channel/version.

### Remediation direction

- Keep one authoritative packaging strategy and ensure generated install paths are valid for the selected install scope.
- If you patch `extension.vsixmanifest` and inject `Microsoft.VisualStudio.VsPackage` assets, verify that the resulting installer metadata does not emit invalid `[installdir]...\VSExtensions\...` entries.
- Re-test installation on all supported SKUs/channels (for example Enterprise + Insiders), not just one instance.

## Known Installer Failure: cross-manifest `Version` mismatch

### Symptom signature

- VSIXInstaller fails with an error similar to:
  - `Version value mismatch. VsixManifest value: 'X'; Catalog manifest value: 'Y'; Package manifest value: 'X'`
  - `InvalidOperationException: ... property 'Version' is not consistent across manifests`

### Why this happens

Hybrid repack flows often patch `extension.vsixmanifest` and `manifest.json` but forget `catalog.json`.  
Setup Engine validates all of them together, so stale `catalog.json` values (for example `0.0.0.0`) cause install rollback.

### What to verify

Ensure all three are aligned:
1. `extension.vsixmanifest` `<Identity ... Version="...">`
2. `manifest.json` root `"version"`
3. `catalog.json` package/info version values

### Quick audit pattern

```powershell
$vsix = Get-ChildItem '.\src' -Filter *.vsix -Recurse |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($vsix.FullName)
try {
    foreach ($name in 'extension.vsixmanifest','manifest.json','catalog.json') {
        $entry = $zip.Entries | Where-Object { $_.FullName -ieq $name } | Select-Object -First 1
        if (-not $entry) {
            Write-Host "Missing: $name"
            continue
        }

        $reader = New-Object System.IO.StreamReader($entry.Open())
        try {
            $text = $reader.ReadToEnd()
            Write-Host "---- $name ----"
            if ($name -eq 'extension.vsixmanifest') {
                ($text | Select-String 'Identity .* Version=\"([^\"]+)\"').Matches.Value
            } else {
                $text | Select-String '"version"\s*:\s*"[^"]+"' -AllMatches | ForEach-Object { $_.Matches.Value }
            }
        }
        finally {
            $reader.Dispose()
        }
    }
}
finally {
    $zip.Dispose()
}
```

### Remediation direction

- If you patch one manifest, patch all three (`extension.vsixmanifest`, `manifest.json`, `catalog.json`) in the same post-pack step.
- Add build assertions that fail when:
  - any installer-facing manifest still contains `0.0.0.0`,
  - or these files disagree on extension version.

## Guardrails

- Do not assume behavior from an older Visual Studio version applies to current preview/insiders builds.
- Do not use `VsRegEdit.exe` as a substitute for full pkgdef install flow unless you explicitly need registry diagnostics.
- Do not accept a one-SKU-only fix if your extension targets Community/Pro/Enterprise.
- Do not merge changes without validating both:
  1. Options node visibility
  2. property page load behavior

## How to Adapt This Skill to a Specific Extension

Replace these placeholders in your investigation notes:
- Extension/package IDs and GUIDs
- Expected VSIX entries (dll/pkgdef/asset paths)
- Known-good baseline commit/PR
- Known error signatures for your package
- Required supported VS SKUs/architectures

You can then keep this same workflow and checklist unchanged.

## Learn More

| Topic | How to Find |
|-------|-------------|
| `devenv /log` syntax | `microsoft_docs_search(query="/Log devenv Visual Studio")` |
| Activity log usage | `microsoft_docs_search(query="Visual Studio use the activity log VSPackage")` |
| Experimental Instance behavior | `microsoft_docs_search(query="Visual Studio experimental instance /RootSuffix Exp")` |
| Package registration and pkgdef | `microsoft_docs_search(query="Visual Studio registering VSPackages pkgdef")` |
| Tools > Options pages | `microsoft_docs_search(query="Visual Studio ProvideOptionPage Tools Options")` |

## CLI Alternative

If the Learn MCP server is not available, use the `mslearn` CLI instead:

| MCP Tool | CLI Command |
|----------|-------------|
| `microsoft_docs_search(query: "...")` | `mslearn search "..."` |
| `microsoft_code_sample_search(query: "...", language: "...")` | `mslearn code-search "..." --language ...` |
| `microsoft_docs_fetch(url: "...")` | `mslearn fetch "..."` |

Run directly with `npx @microsoft/learn-cli <command>` or install globally with `npm install -g @microsoft/learn-cli`.
