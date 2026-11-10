#!/usr/bin/env pwsh
<#
  .SYNOPSIS
  Patches the generated .pkgdef for the ContextRelay options pages.

  .DESCRIPTION
  Visual Studio 18 hides legacy options pages when IsInUnifiedSettings is set to 1.
  ContextRelay does not yet provide the additional Unified Settings registration, so
  this script keeps the legacy page visible by forcing IsInUnifiedSettings back to 0.
  It also replaces numeric resource references with literal display names for the
  ContextRelay category, General page, and SharePoint Embedded page.

  .PARAMETER PkgdefPath
  Path to the .pkgdef file to modify.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$PkgdefPath
)

if (-not (Test-Path -LiteralPath $PkgdefPath -PathType Leaf)) {
    Write-Error "pkgdef not found at: $PkgdefPath"
    exit 1
}

$content = [System.IO.File]::ReadAllText($PkgdefPath)
$lineEnding = if ($content.Contains("`r`n")) { "`r`n" } else { "`n" }
$sectionPattern = '(?ms)^(?<header>\[(?<path>[^\]]*\\ToolsOptionsPages\\ContextRelay(?:\\General|\\SharePoint Embedded)?)\]\r?\n)(?<body>.*?)(?=^\[|\z)'
$sectionNames = @{
    'ToolsOptionsPages\ContextRelay' = 'ContextRelay'
    'ToolsOptionsPages\ContextRelay\General' = 'General'
    'ToolsOptionsPages\ContextRelay\SharePoint Embedded' = 'SharePoint Embedded'
}

# Only patch these three registration sections so unrelated option pages retain
# their generated names and Unified Settings metadata.
$content = [System.Text.RegularExpressions.Regex]::Replace(
    $content,
    $sectionPattern,
    [System.Text.RegularExpressions.MatchEvaluator] {
        param($match)

        $path = $match.Groups['path'].Value
        $key = $path.Substring($path.IndexOf('ToolsOptionsPages\', [System.StringComparison]::Ordinal))
        if (-not $sectionNames.ContainsKey($key)) {
            return $match.Value
        }

        $displayName = $sectionNames[$key]
        $body = $match.Groups['body'].Value
        $displayNameLine = '@="' + $displayName + '"'
        $defaultValuePattern = '(?m)^@=.*(?=\r?$)'
        if ([System.Text.RegularExpressions.Regex]::IsMatch($body, $defaultValuePattern)) {
            $body = [System.Text.RegularExpressions.Regex]::Replace(
                $body,
                $defaultValuePattern,
                $displayNameLine,
                1)
        }
        else {
            $body = $displayNameLine + $lineEnding + $body
        }

        if ($key -ne 'ToolsOptionsPages\ContextRelay') {
            $unifiedSettingsPattern = '(?m)^"IsInUnifiedSettings"\s*=\s*"?dword:[0-9A-Fa-f]{8}"?\s*$'
            if ([System.Text.RegularExpressions.Regex]::IsMatch($body, $unifiedSettingsPattern)) {
                $body = [System.Text.RegularExpressions.Regex]::Replace(
                    $body,
                    $unifiedSettingsPattern,
                    '"IsInUnifiedSettings"=dword:00000000')
            }
            else {
                if ($body.Length -gt 0 -and $body -notmatch '(?:\r\n|\n|\r)$') {
                    $body += $lineEnding
                }

                $body += '"IsInUnifiedSettings"=dword:00000000'
            }
        }

        if ($body -notmatch '(?:\r\n|\n|\r)$') {
            $body += $lineEnding
        }

        return $match.Groups['header'].Value + $body
    })

[System.IO.File]::WriteAllText($PkgdefPath, $content, [System.Text.UTF8Encoding]::new($false))
Write-Host "Patched pkgdef: $PkgdefPath"
