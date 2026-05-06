#!/usr/bin/env pwsh
<#
  .SYNOPSIS
  Removes stale duplicate VSIX deployments from Visual Studio experimental instances.

  .DESCRIPTION
  Visual Studio may leave multiple hashed extension folders behind for the same VSIX ID.
  When that happens, the extension cache can reject every copy as a conflict. This script
  keeps only the newest deployment for the specified extension ID in each experimental
  instance and touches extensions.configurationchanged so Visual Studio rebuilds its cache.

  .PARAMETER VsLocalAppData
  Path to the Visual Studio local AppData root (for example %LocalAppData%\Microsoft\VisualStudio).

  .PARAMETER RootSuffix
  The experimental instance root suffix (default: Exp).

  .PARAMETER ExtensionId
  The VSIX identity to deduplicate.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$VsLocalAppData,

    [Parameter(Mandatory = $false)]
    [string]$RootSuffix = "Exp",

    [Parameter(Mandatory = $true)]
    [string]$ExtensionId
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$expDirs = @(Get-ChildItem $VsLocalAppData -Directory | Where-Object { $_.Name -match "\d+\.\d+_[0-9a-fA-F]+${RootSuffix}$" })
if ($expDirs.Count -eq 0) {
    Write-Host "No experimental instance directories found under $VsLocalAppData."
    exit 0
}

foreach ($expDir in $expDirs) {
    $extensionsRoot = Join-Path $expDir.FullName "Extensions"
    if (-not (Test-Path $extensionsRoot)) {
        continue
    }

    $matchingExtensions = @(@(
        Get-ChildItem $extensionsRoot -Directory | ForEach-Object {
            $manifestPath = Join-Path $_.FullName "extension.vsixmanifest"
            if (-not (Test-Path $manifestPath)) {
                return
            }

            $manifestContent = Get-Content $manifestPath -Raw
            if ($manifestContent -match [regex]::Escape("Id=`"$ExtensionId`"")) {
                [PSCustomObject]@{
                    Folder = $_.FullName
                    LastWriteTime = $_.LastWriteTimeUtc
                }
            }
        }
    ) | Sort-Object LastWriteTime -Descending)

    if ($matchingExtensions.Count -le 1) {
        continue
    }

    $foldersToRemove = $matchingExtensions | Select-Object -Skip 1
    foreach ($duplicate in $foldersToRemove) {
        Remove-Item $duplicate.Folder -Recurse -Force
        Write-Host "Removed duplicate extension deployment: $($duplicate.Folder)"
    }

    $changeFile = Join-Path $extensionsRoot "extensions.configurationchanged"
    Set-Content -Path $changeFile -Value (Get-Date).ToString("o") -Force
    Write-Host "Touched $changeFile after removing duplicates."
}
