#!/usr/bin/env pwsh
<#
  .SYNOPSIS
  Deploys the in-proc VSSDK package (pkgdef + DLL) to the correct VS Extensions folder
  so that the pkgdef engine can register Tools > Options pages.

  Background: The OOP Extensibility host deploys to VSExtensions\, but the pkgdef engine
  only scans Extensions\ (user) and Program Files (system). This script bridges the gap
  by placing the pkgdef and DLL in the Extensions folder and touching
  extensions.configurationchanged so VS rebuilds its pkgdef cache on next launch.

  .PARAMETER PkgdefPath
  Full path to the generated ContextRelay.VSExtension.Package.pkgdef file.

  .PARAMETER DllPath
  Full path to the compiled ContextRelay.VSExtension.Package.dll file.

  .PARAMETER VsLocalAppData
  Path to the VS local AppData root (e.g. %LocalAppData%\Microsoft\VisualStudio).

  .PARAMETER RootSuffix
  The VS experimental instance root suffix (default: Exp).

  .PARAMETER SupportingAssemblies
  Additional assembly files that must live beside the in-proc package DLL.

  .PARAMETER ExtensionId
  Optional VSIX identity for the main extension deployment. When provided, the
  supporting assemblies are also copied beside that installed extension payload.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$PkgdefPath,

    [Parameter(Mandatory = $true)]
    [string]$DllPath,

    [Parameter(Mandatory = $true)]
    [string]$VsLocalAppData,

    [Parameter(Mandatory = $false)]
    [string]$RootSuffix = "Exp",

    [Parameter(Mandatory = $false)]
    [string[]]$SupportingAssemblies = @(),

    [Parameter(Mandatory = $false)]
    [string]$ExtensionId = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Find the hashed experimental instance directory (e.g. 18.0_24b3d224Exp)
$expDirs = @(Get-ChildItem $VsLocalAppData -Directory | Where-Object { $_.Name -match "\d+\.\d+_[0-9a-fA-F]+${RootSuffix}$" })
if ($expDirs.Count -eq 0) {
    Write-Warning "No experimental instance directory found under $VsLocalAppData matching *${RootSuffix}. Skipping in-proc package deployment."
    exit 0
}

foreach ($expDir in $expDirs) {
    $extensionsRoot = Join-Path $expDir.FullName "Extensions"
    if (-not (Test-Path $extensionsRoot)) {
        Write-Host "Extensions folder not found. Creating: $extensionsRoot"
        New-Item -ItemType Directory -Path $extensionsRoot -Force | Out-Null
    }

    # Find the hashed sub-folder (e.g. Extensions-18.0_24b3d224)
    $subDirs = @(Get-ChildItem $extensionsRoot -Directory | Where-Object { $_.Name -match "^Extensions-" })
    if ($subDirs.Count -eq 0) {
        Write-Host "No Extensions-* subfolder found. Creating one based on experimental instance."
        # Extract version from expDir name (e.g. "18.0_24b3d224Exp" -> "Extensions-18.0_24b3d224")
        $expName = $expDir.Name -replace "${RootSuffix}$", ""
        $expectedSubDir = "Extensions-$expName"
        $subDirPath = Join-Path $extensionsRoot $expectedSubDir
        New-Item -ItemType Directory -Path $subDirPath -Force | Out-Null
        $subDirs = @(Get-Item $subDirPath)
    }

    $targetDir = Join-Path $subDirs[0].FullName "KazushiKamegawa\ContextRelay.Package"
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

    # Copy pkgdef
    if (Test-Path $PkgdefPath) {
        Copy-Item -Path $PkgdefPath -Destination $targetDir -Force
        Write-Host "Copied pkgdef -> $targetDir"
    } else {
        Write-Warning "pkgdef not found: $PkgdefPath"
    }

    # Copy the primary package assembly.
    if (Test-Path $DllPath) {
        Copy-Item -Path $DllPath -Destination $targetDir -Force
        Write-Host "Copied DLL    -> $targetDir"
    } else {
        Write-Warning "DLL not found: $DllPath"
    }

    foreach ($assemblyPath in $SupportingAssemblies) {
        if ([string]::IsNullOrWhiteSpace($assemblyPath)) {
            continue
        }

        if (Test-Path $assemblyPath) {
            Copy-Item -Path $assemblyPath -Destination $targetDir -Force
            Write-Host "Copied dependency -> $targetDir\$([System.IO.Path]::GetFileName($assemblyPath))"
        } else {
            Write-Warning "Dependency not found: $assemblyPath"
        }
    }

    # Write extension.vsixmanifest so VS pkgdef engine discovers the pkgdef
    # (VS scans Extensions\ folders but only processes entries with a manifest)
    $manifestContent = @"
<PackageManifest Version="2.0.0" xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011">
  <Metadata>
    <Identity Id="ContextRelayVS.kkamegawa.d0dd4dd5-7d88-4b80-8d4d-9dd18fa4cf11.Package" Version="0.3.0.0" Language="en-US" Publisher="KazushiKamegawa" />
    <DisplayName>ContextRelay for Visual Studio (Package)</DisplayName>
    <Description xml:space="preserve">In-proc VSSDK package for ContextRelay Tools &gt; Options registration.</Description>
  </Metadata>
  <Installation>
    <InstallationTarget Id="Microsoft.VisualStudio.Enterprise" Version="[17.9,)">
      <ProductArchitecture>amd64</ProductArchitecture>
    </InstallationTarget>
    <InstallationTarget Id="Microsoft.VisualStudio.Pro" Version="[17.9,)">
      <ProductArchitecture>amd64</ProductArchitecture>
    </InstallationTarget>
    <InstallationTarget Id="Microsoft.VisualStudio.Community" Version="[17.9,)">
      <ProductArchitecture>amd64</ProductArchitecture>
    </InstallationTarget>
  </Installation>
  <Prerequisites>
    <Prerequisite Id="Microsoft.VisualStudio.Component.CoreEditor" Version="[17.9,)" DisplayName="Visual Studio core editor" />
  </Prerequisites>
  <Assets>
    <Asset Type="Microsoft.VisualStudio.VsPackage" Path="ContextRelay.VSExtension.Package.pkgdef" />
  </Assets>
</PackageManifest>
"@
    Set-Content -Path "$targetDir\extension.vsixmanifest" -Value $manifestContent -Encoding UTF8
    Write-Host "Wrote extension.vsixmanifest -> $targetDir"

    # Touch extensions.configurationchanged to invalidate pkgdef cache
    $changeFile = Join-Path $extensionsRoot "extensions.configurationchanged"
    Set-Content -Path $changeFile -Value (Get-Date).ToString("o") -Force
    Write-Host "Touched $changeFile (pkgdef cache invalidated)"

    if (-not [string]::IsNullOrWhiteSpace($ExtensionId)) {
        $mainExtensionDirs = @(
            Get-ChildItem $extensionsRoot -Directory | ForEach-Object {
                $manifestPath = Join-Path $_.FullName "extension.vsixmanifest"
                if (-not (Test-Path $manifestPath)) {
                    return
                }

                $manifestContent = Get-Content $manifestPath -Raw
                if ($manifestContent -match [regex]::Escape("Id=`"$ExtensionId`"")) {
                    $_.FullName
                }
            }
        )

        foreach ($mainExtensionDir in $mainExtensionDirs) {
            foreach ($assemblyPath in $SupportingAssemblies) {
                if ([string]::IsNullOrWhiteSpace($assemblyPath) -or -not (Test-Path $assemblyPath)) {
                    continue
                }

                Copy-Item -Path $assemblyPath -Destination $mainExtensionDir -Force
                Write-Host "Copied dependency -> $mainExtensionDir\$([System.IO.Path]::GetFileName($assemblyPath))"
            }
        }
    }
}

Write-Host "DeployInProcPackage complete."
