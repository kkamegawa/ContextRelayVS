#!/usr/bin/env pwsh
<#
  .SYNOPSIS
  Patches the generated .pkgdef for the ContextRelay options page.

  .DESCRIPTION
  Visual Studio 18 hides legacy options pages when IsInUnifiedSettings is set to 1.
  ContextRelay does not yet provide the additional Unified Settings registration, so
  this script keeps the legacy page visible by forcing IsInUnifiedSettings back to 0.
  It also replaces numeric resource references (@="#0") with literal display names so
  the ContextRelay category and General page remain readable in Tools > Options.

  .PARAMETER PkgdefPath
  Path to the .pkgdef file to modify.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$PkgdefPath
)

if (-not (Test-Path $PkgdefPath)) {
    Write-Error "pkgdef not found at: $PkgdefPath"
    exit 1
}

$content = Get-Content $PkgdefPath -Raw

# Keep the legacy options page visible until a true Unified Settings registration exists.
$content = $content -replace '"IsInUnifiedSettings"=dword:00000001', '"IsInUnifiedSettings"=dword:00000000'

# Replace numeric resource references with literal display names.
#    VSSDK generates @="#0" when no satellite resource DLL exists.
#    Visual Studio uses the default value (@=...) as the category/page title
#    displayed in the Tools > Options tree. We replace the placeholder with
#    the actual strings so the tree entry is always visible.
$content = $content -replace '(?m)^(\[.*ToolsOptionsPages\\ContextRelay\]\s*\r?\n)@="#\d+"', '$1@="ContextRelay"'
$content = $content -replace '(?m)^(\[.*ToolsOptionsPages\\ContextRelay\\General\]\s*\r?\n)@="#\d+"', '$1@="General"'

Set-Content $PkgdefPath -Value $content -NoNewline
Write-Host "Patched pkgdef: $PkgdefPath"
