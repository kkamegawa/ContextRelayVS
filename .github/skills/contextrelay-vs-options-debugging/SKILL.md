---
name: contextrelay-vs-options-debugging
description: Debug Visual Studio Tools > Options registration issues for ContextRelay and similar VS extensions. Use when agents need to investigate missing option pages, ActivityLog collection, VSIX/pkgdef registration, Experimental Instance mismatches, or in-proc package loading problems.
---

# ContextRelay VS Options Debugging

This skill is for diagnosing why a Visual Studio extension's Tools > Options page does not appear. It is tuned for ContextRelay's mixed architecture: a net8 out-of-process extension plus a net48 in-proc package used for options registration. The first priority is to identify the exact Visual Studio instance being inspected, then force an explicit activity log, and only then inspect VSIX/pkgdef/package registration.

## Quick Start

1. Identify the active Visual Studio instance and command line.
2. Verify whether the user is looking at a normal instance or `/RootSuffix Exp`.
3. If no log file exists, relaunch the target instance with an explicit `/log` path.
4. Inspect the built VSIX for `Microsoft.VisualStudio.VsPackage`, the package DLL, and the package pkgdef.
5. Compare the current build wiring against the known-good PR #36 path before changing registration again.
6. **Do not** propose or apply a hand-placed extension folder under `%LOCALAPPDATA%\...\Extensions\Publisher\...\` as a debug shortcut. That path is silently ignored on Visual Studio Insiders v18 and produced a real regression (PR #57). The fix is always to ship the in-proc Package inside the main VSIX.

Use these sample scripts first:

- [get-active-visual-studio-instance.ps1](sample_codes/get-active-visual-studio-instance.ps1)
- [start-exp-with-log.ps1](sample_codes/start-exp-with-log.ps1)
- [inspect-vsix-contents.ps1](sample_codes/inspect-vsix-contents.ps1)

## Key Concepts

### Instance accuracy comes first

Do not assume the visible Visual Studio window is the Experimental Instance. Always inspect the running `devenv.exe` command line. In this repository, a previous regression was misdiagnosed because the investigation wrote and read the Exp hive while the user was actually looking at a normal Insiders instance.

### `/log` must be explicit when logs are missing

Visual Studio only writes `ActivityLog.xml` to disk for sessions launched with `/log`. If the expected log file is missing, treat that as an investigation blocker and relaunch with an explicit path such as `%APPDATA%\Microsoft\VisualStudio\ContextRelayVS-Exp-ActivityLog.xml`.

### Sidecar drop-in registration does NOT work on Visual Studio Insiders v18 — never reintroduce it

**Proven by ActivityLog evidence on this repository (PR #57, May 2026).** On Visual Studio 18 Insiders (`/RootSuffix Exp`), hand-placing an extension folder under `%LOCALAPPDATA%\Microsoft\VisualStudio\<root>\Extensions\Publisher\ExtensionName\Version\` is silently ignored by the Extension Manager — even when the folder contains a valid `extension.vsixmanifest`, a valid pkgdef, and all required DLLs. The pkgdef is never merged into `privateregistry.bin`, and no diagnostic is emitted.

Concrete evidence collected from a clean `/log` Exp launch:

| Signal | Observation |
|---|---|
| ActivityLog record for `PkgDefSearchPath` | Lists only `Program Files\...\Common7\IDE\Extensions`, `...\CommonExtensions`, and `devenv.admin.pkgdef`. The per-user Exp `Extensions` folder is **absent**. |
| ActivityLog record for `ImageManifestSearchPath` | **Does** include the per-user Exp `Extensions` folder. Image discovery and pkgdef discovery use different roots. |
| Extension Manager log entries | Only emits warnings for preinstalled disabled extensions; emits **nothing** about a hand-placed sidecar folder. The sidecar is not enumerated at all. |
| `privateregistry.bin` after the launch | Contains zero occurrences of the sidecar's Package GUID, class name, or any sidecar string in any encoding. |
| `HKCU\Software\Microsoft\VisualStudio\<root>_Config\` | No keys for the sidecar's Package GUID or Options pages. |

**Conclusion**: the only supported registration path is to ship the in-proc Package **inside the main VSIX** so the Extension Manager itself performs the install (`VSIXInstaller.exe` or F5 deploy). Marketplace install and F5 must use the **same single VSIX**. Do not write to `%LOCALAPPDATA%\...\Extensions\` by hand, do not author a "debug-only sidecar," and do not split the in-proc Package into a separate deployable folder. Any future agent that proposes a sidecar must first reproduce the ActivityLog evidence above and prove that VS v18's behavior has changed.

The main VSIX must include:

- `Microsoft.VisualStudio.VsPackage` asset in `extension.vsixmanifest` pointing at the package pkgdef
- `ContextRelay.VSExtension.Package.pkgdef` (VSSDK-generated, never a static checked-in copy)
- `ContextRelay.VSExtension.Package.dll`
- `Community.VisualStudio.Toolkit.dll` beside the package DLL

The main extension manifest must declare `ExtensionType="VSSDK+VisualStudio.Extensibility"` (this is the same pattern Microsoft's own preinstalled `Microsoft.VisualStudio.Copilot.Testing.UI` uses on Insiders v18) so the single VSIX carries both the net8 OOP extension and the net48 in-proc Package.

### Sync install targets from the source manifest after detokenization

The Extensibility build pipeline can collapse the generated `extension.vsixmanifest` down to Community-only install targets even when `source.extension.vsixmanifest` lists Community, Pro, and Enterprise. For ContextRelay, the final manifest must re-sync `Installation`, `Dependencies`, and `Prerequisites` from the source manifest after detokenization and then re-assert the `Microsoft.VisualStudio.VsPackage` asset. Do not replace the whole `Metadata` section at that stage because that can clobber tokenized values.

Support for **all Visual Studio SKUs is mandatory** in this repository. Do not accept a fix that only works for one SKU or one local profile. Any registration or manifest change must keep Community, Professional, and Enterprise install targets valid at the same time.

### Generated pkgdef is the authoritative registration source

For this repository, the package project's VSSDK-generated pkgdef is the canonical source of options registration. A handwritten static pkgdef already caused ActivityLog parse errors and should not be treated as the primary path.

### Distinguish visibility failures from property-load failures

For ContextRelay, there are at least two separate failure classes:

- **The category does not appear at all**: usually a VSIX/pkgdef/discovery problem.
- **The category appears but the property page fails to load**: usually a package activation problem.

If `ContextRelay` and `General` appear in Tools > Options but the page shows a load failure dialog, immediately check the activity log for this exact signature before changing unrelated packaging logic:

- `No InprocServer32 registered for package [ContextRelayOptionsPackage]`

This failure already recurred after PR #36-style work, so treat it as a known regression pattern, not a new symptom.

## Common Patterns

### Pattern 1: Identify the actual VS target before touching the hive

```powershell
Get-CimInstance Win32_Process -Filter "name = 'devenv.exe'" |
    Select-Object ProcessId, ExecutablePath, CommandLine
```

If the command line does not include `/RootSuffix Exp`, do not assume Experimental Instance registry or deployment paths are relevant to the screenshot in front of you.

### Pattern 2: Force an activity log for the debugging session

```powershell
$logPath = Join-Path $env:APPDATA 'Microsoft\VisualStudio\ContextRelayVS-Exp-ActivityLog.xml'
$devenv = 'C:\Program Files\Microsoft Visual Studio\18\Insiders\Common7\IDE\devenv.exe'
Start-Process -FilePath $devenv -ArgumentList '/RootSuffix', 'Exp', '/Log', $logPath
```

Per official docs, `/Log` must appear after the other switches and writes diagnostics only for sessions started with `/Log`.

### Pattern 3: Inspect the built VSIX instead of guessing

```powershell
$vsix = Get-ChildItem '.\src\ContextRelay.VSExtension\bin\Debug' -Filter *.vsix -Recurse |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($vsix.FullName)
try {
    $zip.Entries |
        Where-Object { $_.FullName -match 'extension\.vsixmanifest|ContextRelay\.VSExtension\.Package|pkgdef' } |
        Select-Object -ExpandProperty FullName
}
finally {
    $zip.Dispose()
}
```

For ContextRelay, the built VSIX must contain:

- `extension.vsixmanifest`
- `ContextRelay.VSExtension.Package.pkgdef`
- `ContextRelay.VSExtension.Package.dll`

### Pattern 4: Compare against the known-good registration path

For this repository, PR #36 is the baseline. Check for:

- `EnsureVsPackageAssetInManifest`
- `PatchPackagePkgdefOptionsRegistrationBeforeVsix`
- `IncludeInProcPackagePayloadInVsix`
- package project `GeneratePkgDefFile=true`
- `Community.VisualStudio.Toolkit.dll` included in the main VSIX payload beside `ContextRelay.VSExtension.Package.dll`

If any of these are missing, assume packaging drift before assuming runtime registration drift.

Also compare the deployed main extension payload in the Experimental Instance. If `ActivityLog.xml` contains `Invalid path found for content "|%CurrentProject%;PkgdefProjectOutputGroup|"`, treat that as a broken local-deployment path for the main OOP extension caused by leaking project-output-group tokens into local deployment. Replace that path with explicit payload inclusion before `GetVsixSourceItems` so both VSIX packaging and local deployment use resolved file paths.

### Pattern 5: Check for the known package-load signature

```powershell
$logPath = Join-Path $env:APPDATA 'Microsoft\VisualStudio\ContextRelayVS-Exp-ActivityLog.xml'
Select-String -Path $logPath -Pattern 'No InprocServer32 registered for package \[ContextRelayOptionsPackage\]' -Context 2,4
```

If this message is present, the options category can still appear while the property page itself fails to load. Investigate package activation and registration for `ContextRelayOptionsPackage` before revisiting menu visibility work.

## Investigation Checklist

1. **Active instance**
   - Read `devenv.exe` command lines.
   - Record whether the target is normal or `Exp`.
2. **Log availability**
   - If no log exists, relaunch with `/Log`.
   - Prefer an explicit file path under `%APPDATA%\Microsoft\VisualStudio\`.
3. **Profile alignment**
   - Match Local/Roaming profile folders to the active instance.
   - Do not inspect `*Exp` folders if the active instance is normal.
4. **Build artifact validation**
   - Check the intermediate manifest for `Microsoft.VisualStudio.VsPackage`.
   - Check the VSIX for the package DLL and pkgdef.
   - Check the package project for generated pkgdef output.
5. **Runtime diagnostics**
   - Read the activity log for pkgdef syntax errors, package load failures, and missing dependencies.
   - If the options node is visible but the page fails to load, search first for `No InprocServer32 registered for package [ContextRelayOptionsPackage]`.
   - If editor components are involved, also inspect `%LOCALAPPDATA%\Microsoft\VisualStudio\<version>\ComponentModelCache\Microsoft.VisualStudio.Default.err`.
6. **Fallback registration**
   - Do not use `VsRegEdit.exe` to replay pkgdef-style macro values like `$WinDir$\SYSTEM32\MSCOREE.DLL`; pkgdef macros are expanded by the pkgdef engine, not by `VsRegEdit.exe`.
   - Do not hand-place a sidecar folder under `%LOCALAPPDATA%\...\Extensions\Publisher\...\`. VS Insiders v18 ignores that layout (see "Sidecar drop-in registration does NOT work…" above). The only supported debug path is `VSIXInstaller.exe /rootSuffix:Exp <built.vsix>` or the equivalent F5 deploy.

## Repository-Specific Notes

- The current running VS can be a normal Insiders instance even while development work targets `Exp`.
- The main extension is net8 OOP and the options package is net48 in-proc; both ship in a single VSIX.
- Reintroducing hybrid in-proc hosting for the OOP tool window provider causes `System.Runtime, Version=8.0.0.0` load failures.
- Do not set `VssdkcompatibleExtension=true` on the main extension project — that property forces VSSDK in-proc hosting onto the net8 assembly and breaks the OOP host (regression seen in PR #43).
- The current repo sets `StartArguments` with `/rootsuffix Exp /log "$(VisualStudioActivityLogPath)"` in both the main extension project and the package project so F5 sessions emit a deterministic log file.
- Another known ContextRelay regression signature is `No InprocServer32 registered for package [ContextRelayOptionsPackage]`, which means the options node may be visible while the page itself still cannot load.
- The packaged VSIX manifest must preserve Community, Pro, and Enterprise install targets by syncing `Installation`, `Dependencies`, and `Prerequisites` from `source.extension.vsixmanifest` after detokenization.
- Community, Professional, and Enterprise SKU support is a hard requirement for this repository. Do not merge a fix until all three targets remain present in the packaged manifest and the runtime path is consistent with that manifest.
- The main VSIX must include `Community.VisualStudio.Toolkit.dll` beside `ContextRelay.VSExtension.Package.dll`; otherwise `ContextRelayOptionsPackage` can fail to load even when pkgdef registration itself is present.
- The main extension's `source.extension.vsixmanifest` must use `ExtensionType="VSSDK+VisualStudio.Extensibility"` so a single VSIX carries both the net8 OOP extension and the net48 in-proc Package.
- The main extension's `source.extension.vsixmanifest` must never carry `Version="0.0.0.0"`; assert real product version at build time.
- Any project-output-group reference in the VSIX inclusion list must resolve to file paths at build time. If the produced `extension.vsixmanifest` contains literal `|...|` tokens such as `%CurrentProject%;PkgdefProjectOutputGroup`, the build must fail. Leaking such tokens produces ActivityLog errors like `Invalid path found for content "|%CurrentProject%;PkgdefProjectOutputGroup|"`.
- Do not introduce a debug-only sidecar deploy that copies the in-proc Package outside the main VSIX. F5 deploy and Marketplace install must produce and consume the **same** VSIX through the Extension Manager. See "Sidecar drop-in registration does NOT work on Visual Studio Insiders v18" above.

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
