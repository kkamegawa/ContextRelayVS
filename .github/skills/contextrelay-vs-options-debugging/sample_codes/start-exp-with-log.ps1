param(
    [string]$DevEnvPath = 'C:\Program Files\Microsoft Visual Studio\18\Insiders\Common7\IDE\devenv.exe',
    [string]$RootSuffix = 'Exp',
    [string]$LogPath = "$env:APPDATA\Microsoft\VisualStudio\ContextRelayVS-Exp-ActivityLog.xml"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path $DevEnvPath)) {
    throw "devenv.exe was not found at '$DevEnvPath'."
}

Start-Process -FilePath $DevEnvPath -ArgumentList '/RootSuffix', $RootSuffix, '/Log', $LogPath
Write-Host "Started Visual Studio with log path: $LogPath"
