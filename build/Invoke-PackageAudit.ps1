param(
    [string]$SolutionPath = ".\ContextRelayVS.sln"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-JsonPayload {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RawOutput
    )

    $jsonStart = $RawOutput.IndexOf("{", [System.StringComparison]::Ordinal)
    if ($jsonStart -lt 0)
    {
        throw "dotnet list package did not return JSON output.`n$RawOutput"
    }

    return $RawOutput.Substring($jsonStart)
}

function Invoke-DotNetPackageList {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $rawOutput = (& dotnet list $SolutionPath package @Arguments --format json 2>&1 | Out-String)
    if ($LASTEXITCODE -ne 0)
    {
        throw "dotnet list package $($Arguments -join ' ') failed.`n$rawOutput"
    }

    $payload = Get-JsonPayload -RawOutput $rawOutput | ConvertFrom-Json -Depth 100
    $problemsProperty = $payload.PSObject.Properties["problems"]
    if ($problemsProperty -and $problemsProperty.Value)
    {
        $messages = @($problemsProperty.Value | ForEach-Object { "$($_.level): $($_.text)" })
        throw "dotnet list package reported problems.`n$($messages -join "`n")"
    }

    return $payload
}

function Get-FindingCount {
    param(
        [Parameter(Mandatory = $false)]
        [object]$Node
    )

    if ($null -eq $Node -or $Node -is [string] -or $Node -is [System.ValueType])
    {
        return 0
    }

    $count = 0

    if ($Node -is [System.Collections.IEnumerable] -and $Node -isnot [pscustomobject])
    {
        foreach ($item in $Node)
        {
            $count += Get-FindingCount -Node $item
        }

        return $count
    }

    foreach ($property in $Node.PSObject.Properties)
    {
        if ($property.Name -in @("topLevelPackages", "transitivePackages"))
        {
            $count += @($property.Value).Count
            continue
        }

        $count += Get-FindingCount -Node $property.Value
    }

    return $count
}

$vulnerable = Invoke-DotNetPackageList -Arguments @("--vulnerable", "--include-transitive")
$deprecated = Invoke-DotNetPackageList -Arguments @("--deprecated")

$vulnerableCount = Get-FindingCount -Node $vulnerable
$deprecatedCount = Get-FindingCount -Node $deprecated

if ($vulnerableCount -gt 0)
{
    throw "Detected $vulnerableCount vulnerable package entries.`n$($vulnerable | ConvertTo-Json -Depth 100)"
}

if ($deprecatedCount -gt 0)
{
    throw "Detected $deprecatedCount deprecated package entries.`n$($deprecated | ConvertTo-Json -Depth 100)"
}

Write-Host "Package audit passed: 0 vulnerable entries, 0 deprecated entries."
