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
5. For tool window/panel failures, separate provider-construction failures from command-triggered cancellation failures.
6. Only then modify packaging, registration, or panel initialization flow.
7. When a new VS extension bug is confirmed, append a "Known ... Failure" section in this skill with:
   - symptom signature,
   - root cause,
   - remediation,
   - and verification steps.

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

## Known CI Failure: xUnit v3 test process startup failure

### Symptom signature

- CI `dotnet test` step exits with exit code 1 before any test is reported as passed/failed/skipped.
- Log shows:
  ```
  [xUnit.net] ContextRelay.Core.Tests: Catastrophic failure:
  System.InvalidOperationException: Test process did not return valid JSON (non-object).
  ```
- Output block shows a one-line JSON metadata line (arch/framework info) then aborts.
- `No test is available in ... ContextRelay.Core.Tests.dll` is reported immediately after.

### Why this happens

Setting `<OutputType>Exe</OutputType>` and `<UseAppHost>true</UseAppHost>` in the test project forces it into an **executable host shape**.  
The `xunit.runner.visualstudio` VSTest adapter (version 3.x) launches the test binary as a subprocess and expects to receive a multi-object JSON stream back.  
When the binary runs as a standalone executable, it emits only initial metadata JSON and then exits; the adapter rejects this non-streaming output with the above error.

### What to check first

1. Open `tests/ContextRelay.Core.Tests/ContextRelay.Core.Tests.csproj`.
2. Confirm neither `<OutputType>Exe</OutputType>` nor `<UseAppHost>true</UseAppHost>` is set.

### Remediation

Remove both properties from the test project `<PropertyGroup>`:

```diff
 <PropertyGroup>
   <TargetFramework>net8.0</TargetFramework>
   <LangVersion>latest</LangVersion>
   <Nullable>enable</Nullable>
   <IsPackable>false</IsPackable>
   <IsTestProject>true</IsTestProject>
-  <OutputType>Exe</OutputType>
-  <UseAppHost>true</UseAppHost>
   <RootNamespace>ContextRelay.Core.Tests</RootNamespace>
 </PropertyGroup>
```

The `xunit.runner.visualstudio` package version in use (`3.1.5`) remains compatible once the executable host forcing is removed.  
Do **not** replace it with the non-existent `xunit.runner.visualstudio.v3` package — that package ID does not exist on NuGet.

---

## Known Tool Window Failure: frame construction canceled (`OperationCanceledException`)

### Symptom signature

- Visual Studio shows:
  - `Construction of frame content failed`
  - frame caption is your extension panel/tool window
- ActivityLog stack is mostly VS internals, for example:
  - `ThreadingTools.WithCancellationSlow`
  - `RemoteToolWindow.EnsureToolWindowProviderAsync`
  - `RemoteToolWindow.GetContentAsync`
  - `WindowFrame.ConstructContent...`
- Exception type:
  - `System.OperationCanceledException`

### Why this happens

Panel/tool window creation can inherit cancellation tokens from command execution or shell lifecycle transitions.  
If panel initialization depends on those transient tokens, cancellation can occur during provider/content construction and surface as a user-visible frame construction failure.

### What to check first

1. In your `ToolWindow.GetContentAsync(...)`, verify whether deferred initialization is started with the incoming `cancellationToken`.
2. In command handlers that call `ShowToolWindowAsync(...)`, verify whether command cancellation token is passed through.
3. Inspect catch filters that swallow cancellation only when a specific token is canceled (`when (token.IsCancellationRequested)`), which can miss linked/transient cancellation sources.

### Remediation direction

- Decouple deferred panel initialization from transient shell/command cancellation:
  - run deferred initialization with `CancellationToken.None` (or a dedicated lifetime token).
- For open-panel commands, avoid passing short-lived command tokens directly into `ShowToolWindowAsync(...)` when this causes intermittent panel creation aborts.
- Treat initialization-time `OperationCanceledException` as non-fatal unless the extension is being disposed intentionally.

### Verification

1. Rebuild/reinstall VSIX.
2. Open panel repeatedly from command + menu paths.
3. Confirm ActivityLog no longer records frame-construction errors for the panel.
4. Confirm normal disposal/shutdown still works (no leaked background work).

---

## Known Publish Failure: publisher name mismatch

### Symptom signature

- Marketplace publish step (for example `VsixPublisher.exe publish`) fails with an error similar to:
  - `The publisher '<name>' is not the publisher of identity '<registered-publisher>'`
  - or `The publisher field value does not match the authenticated publisher account`
- Or the VSIX installs locally but shows the wrong publisher/author in VS Extension Manager.

### Why this happens

The publisher/author display name in the VSIX metadata must exactly match the **Publisher display name** registered on Visual Studio Marketplace. This is distinct from the Marketplace Publisher ID used by publishing tools:

| File | Field |
|------|-------|
| `source.extension.vsixmanifest` | `<Identity ... Publisher="...">`  |
| `extension.vsixmanifest` (output) | same after detokenization |
| `ContextRelayExtension.cs` | `ExtensionConfiguration.Metadata.publisherName` |
| `vs-publish.json` | `"publisher"` is the Marketplace Publisher ID, not the display name |

For this repository, the Marketplace Publisher ID is **`KazushiKamegawa`** and the correct publisher/author display name is **`kkamegawa`**.

### What to check

1. Confirm `source.extension.vsixmanifest` contains `Publisher="kkamegawa"`.
2. Confirm `vs-publish.json` contains Publisher ID `"publisher": "KazushiKamegawa"`.
3. After any repack/patch step, open the produced VSIX and verify the display name is preserved.
4. If you use a build task that patches publisher metadata, ensure it does not substitute the Publisher ID for the display name.

### Quick audit pattern

```powershell
$vsix = Get-ChildItem '.\src' -Filter *.vsix -Recurse |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($vsix.FullName)
try {
    foreach ($name in 'extension.vsixmanifest') {
        $entry = $zip.Entries | Where-Object { $_.FullName -ieq $name } | Select-Object -First 1
        if (-not $entry) { Write-Host "Missing: $name"; continue }
        $reader = New-Object System.IO.StreamReader($entry.Open())
        try {
            $text = $reader.ReadToEnd()
            Write-Host "---- $name ----"
            $text | Select-String -Pattern 'Publisher|publisher' -AllMatches |
                ForEach-Object { $_.Matches.Value }
        } finally { $reader.Dispose() }
    }
} finally { $zip.Dispose() }
```

Expected output: VSIX publisher/author display-name metadata references `kkamegawa`.

### Remediation direction

- Keep `Publisher="kkamegawa"` in `source.extension.vsixmanifest` as the display-name source of truth.
- Keep `"publisher": "KazushiKamegawa"` in `vs-publish.json` as the Marketplace Publisher ID.
- Ensure any post-pack manifest patch task preserves the VSIX display-name metadata.

---

## Guardrails

- Do not assume behavior from an older Visual Studio version applies to current preview/insiders builds.
- Do not use `VsRegEdit.exe` as a substitute for full pkgdef install flow unless you explicitly need registry diagnostics.
- Do not accept a one-SKU-only fix if your extension targets Community/Pro/Enterprise.
- Do not merge changes without validating both:
  1. Options node visibility
  2. property page load behavior
- Do not set `OutputType=Exe` or `UseAppHost=true` in the test project; this causes xUnit v3 catastrophic startup failure in CI.
- Do not conflate the Marketplace Publisher ID `KazushiKamegawa` with the publisher display name `kkamegawa`.
- Prefer VSSDK / VisualStudio.Extensibility APIs over direct Win32 API usage whenever both are available (for example, prefer VS shell-owned file dialog APIs over `Microsoft.Win32.OpenFileDialog`), because VS-owned APIs preserve proper IDE ownership, modality, and compatibility.
- For VS extension panel/tool window fixes, invoke this skill first and follow its panel-cancellation checklist before changing runtime code.

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
