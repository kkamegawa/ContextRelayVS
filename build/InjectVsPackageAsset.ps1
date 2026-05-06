#!/usr/bin/env pwsh
<#
  .SYNOPSIS
  Injects the Microsoft.VisualStudio.VsPackage asset into the VSIX manifest if it's missing.

  .PARAMETER ManifestPath
  Path to the extension.vsixmanifest file to modify.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,

    [Parameter(Mandatory = $false)]
    [string]$SourceManifestPath
)

if (-not (Test-Path $ManifestPath)) {
    Write-Error "Manifest not found at: $ManifestPath"
    exit 1
}

[xml]$xml = Get-Content $ManifestPath
$ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
$ns.AddNamespace('pm', 'http://schemas.microsoft.com/developer/vsx-schema/2011')

function Sync-ManifestSection {
    param(
        [Parameter(Mandatory = $true)]
        [xml]$TargetManifest,

        [Parameter(Mandatory = $true)]
        [System.Xml.XmlNamespaceManager]$TargetNamespaceManager,

        [Parameter(Mandatory = $true)]
        [xml]$SourceManifest,

        [Parameter(Mandatory = $true)]
        [System.Xml.XmlNamespaceManager]$SourceNamespaceManager,

        [Parameter(Mandatory = $true)]
        [string]$SectionName
    )

    $targetRoot = $TargetManifest.DocumentElement
    $existingNode = $targetRoot.SelectSingleNode("pm:$SectionName", $TargetNamespaceManager)
    if ($existingNode -ne $null) {
        $targetRoot.RemoveChild($existingNode) | Out-Null
    }

    $sourceNode = $SourceManifest.SelectSingleNode("//pm:$SectionName", $SourceNamespaceManager)
    if ($sourceNode -ne $null) {
        $importedNode = $TargetManifest.ImportNode($sourceNode, $true)
        $targetRoot.AppendChild($importedNode) | Out-Null
    }
}

if (-not [string]::IsNullOrWhiteSpace($SourceManifestPath)) {
    if (-not (Test-Path $SourceManifestPath)) {
        Write-Error "Source manifest not found at: $SourceManifestPath"
        exit 1
    }

    [xml]$sourceXml = Get-Content $SourceManifestPath
    $sourceNs = New-Object System.Xml.XmlNamespaceManager($sourceXml.NameTable)
    $sourceNs.AddNamespace('pm', 'http://schemas.microsoft.com/developer/vsx-schema/2011')

    Sync-ManifestSection -TargetManifest $xml -TargetNamespaceManager $ns -SourceManifest $sourceXml -SourceNamespaceManager $sourceNs -SectionName 'Metadata'
    Sync-ManifestSection -TargetManifest $xml -TargetNamespaceManager $ns -SourceManifest $sourceXml -SourceNamespaceManager $sourceNs -SectionName 'Installation'
    Sync-ManifestSection -TargetManifest $xml -TargetNamespaceManager $ns -SourceManifest $sourceXml -SourceNamespaceManager $sourceNs -SectionName 'Dependencies'
    Sync-ManifestSection -TargetManifest $xml -TargetNamespaceManager $ns -SourceManifest $sourceXml -SourceNamespaceManager $sourceNs -SectionName 'Prerequisites'

    Write-Host "Synchronized manifest metadata and installation targets from source manifest"
}

$assetsNode = $xml.SelectSingleNode('//pm:Assets', $ns)
if ($assetsNode -eq $null) {
    $root = $xml.DocumentElement
    $assetsNode = $xml.CreateElement('Assets', 'http://schemas.microsoft.com/developer/vsx-schema/2011')
    $root.AppendChild($assetsNode) | Out-Null
    Write-Host "Created Assets element"
}

$vsPackageAsset = $xml.SelectSingleNode("//pm:Asset[@Type='Microsoft.VisualStudio.VsPackage']", $ns)
if ($vsPackageAsset -eq $null) {
    $assetNode = $xml.CreateElement('Asset', 'http://schemas.microsoft.com/developer/vsx-schema/2011')
    $assetNode.SetAttribute('Type', 'Microsoft.VisualStudio.VsPackage')
    $assetNode.SetAttribute('Path', 'ContextRelay.VSExtension.Package.pkgdef')
    $assetsNode.AppendChild($assetNode) | Out-Null
    Write-Host "Injected VsPackage asset into manifest: $ManifestPath"
}
else {
    $vsPackageAsset.SetAttribute('Path', 'ContextRelay.VSExtension.Package.pkgdef')
    Write-Host "Normalized VsPackage asset in manifest"
}

$xml.Save($ManifestPath)
