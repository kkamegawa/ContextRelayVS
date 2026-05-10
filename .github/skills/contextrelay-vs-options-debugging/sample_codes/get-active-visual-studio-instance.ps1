Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Get-CimInstance Win32_Process -Filter "name = 'devenv.exe'" |
    Select-Object ProcessId, ExecutablePath, CommandLine |
    Format-Table -AutoSize
