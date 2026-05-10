param(
    [string]$VsixPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($VsixPath)) {
    throw 'Provide -VsixPath pointing to a built VSIX file.'
}

if (-not (Test-Path $VsixPath)) {
    throw "VSIX file was not found at '$VsixPath'."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path $VsixPath))
try {
    $zip.Entries |
        Where-Object {
            $_.FullName -match 'extension\.vsixmanifest|ContextRelay\.VSExtension\.Package|pkgdef|Community\.VisualStudio\.Toolkit'
        } |
        Select-Object -ExpandProperty FullName
}
finally {
    $zip.Dispose()
}
