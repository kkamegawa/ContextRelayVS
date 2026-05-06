# Build Infrastructure

This directory contains build scripts and infrastructure components for ContextRelayVS.

## MSBuild Properties and Tasks

**ContextRelay.Build.props**
- Centralized MSBuild property and UsingTask definitions
- Imported by all build projects via `<Import>` element
- Contains C# implementations of build-time file manipulation tasks:
  - `PatchPkgdefFile`: Regex-based pkgdef file modifications
  - `InjectVsPackageAsset`: VSIX manifest XML manipulation
  - `RemoveDuplicateExperimentalExtensions`: Cleanup of duplicate VS extension deployments
  - `DeployInProcPackage`: In-proc package deployment to Extensions folder

## Active Scripts

**Invoke-PackageAudit.ps1**
- Called by CI/CD workflows (see `.github/workflows/ci.yml` and `.github/workflows/release.yml`)
- Enforces security policy: zero vulnerable and zero deprecated NuGet packages
- Used in: `.github/workflows/ci.yml` line 32, `.github/workflows/release.yml` line 45

## Legacy Scripts (Replaced by C# UsingTasks)

The following PowerShell scripts are **no longer called by the build system** as their functionality has been replaced by C# UsingTask implementations:

- **EnableUnifiedSettings.ps1** → Functionality moved to `PatchPkgdefFile` UsingTask
- **InjectVsPackageAsset.ps1** → Functionality moved to `InjectVsPackageAsset` UsingTask  
- **RemoveDuplicateExperimentalExtensions.ps1** → Functionality moved to `RemoveDuplicateExperimentalExtensions` UsingTask
- **DeployInProcPackage.ps1** → Functionality moved to `DeployInProcPackage` UsingTask

These scripts are kept as reference documentation for the original PowerShell logic that was ported to C#. They may be safely archived or deleted in future cleanup.

## Why C# Tasks Instead of PowerShell?

PowerShell scripts are subject to execution policy restrictions on Windows systems:
- **RestrictedMode** blocks all script execution by default
- **RemoteSigned** requires digital signatures for downloaded scripts
- **Bypass** must be explicitly set and doesn't persist across sessions

MSBuild UsingTasks with inline C# code bypass these restrictions entirely, making the build infrastructure more reliable across different environments (local machines, CI/CD systems, automated build servers).

## Build Properties

The following custom MSBuild properties are defined in ContextRelay.Build.props:
- `InProcPackageTargetFramework`: Framework version for in-proc package DLL (net48)
- `VsixExtensionId`: Unique identifier for the VSIX extension
- `InProcPackageOutputDir`: Output directory for in-proc package binaries
- `InProcToolkitAssemblyPath`: Path to toolkit assembly for deployment
