#requires -Version 5.1
<#
.SYNOPSIS
  Reproducible MSIX layout/packaging for FanForge (WinForms, .NET 8, win-x64, full trust).
.DESCRIPTION
  1. Publishes FanForge.Win self-contained win-x64 single-file.
  2. Generates the logo assets and stages AppxManifest.xml (version substituted).
  3. Verifies the structure (scripts\verificar-msix.ps1).
  4. Packs with MakeAppx.exe when the Windows SDK is available; otherwise leaves the layout.
  The produced .msix is UNSIGNED. It will not be accepted until signed with a trusted certificate;
  see docs/MSIX.md. This script never disables Smart App Control, WDAC or Secure Boot.
.NOTES
  MakeAppx.exe must be on PATH or in a Windows Kits\10\bin\...\x64 folder to produce a .msix.
#>
param(
    [string]$Configuration = 'Release',
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')][string]$Version = '0.8.0.0',
    [string]$OutputDir = (Join-Path $PSScriptRoot '..\dist\msix'),
    [switch]$SkipPublish,
    [string]$SourceDir,
    [string]$Publisher
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$layout = Join-Path $OutputDir 'layout'
$manifestSource = Join-Path $root 'packaging\msix\AppxManifest.xml'

if (Test-Path -LiteralPath $layout) { Remove-Item -LiteralPath $layout -Recurse -Force }
New-Item -ItemType Directory -Path $layout -Force | Out-Null

if ($SkipPublish) {
    if ([string]::IsNullOrWhiteSpace($SourceDir) -or -not (Test-Path -LiteralPath (Join-Path $SourceDir 'FanForge.exe'))) {
        throw 'SkipPublish requires -SourceDir containing the signed FanForge.exe.'
    }
    Copy-Item -LiteralPath (Join-Path $SourceDir 'FanForge.exe') -Destination (Join-Path $layout 'FanForge.exe')
}
else {
    Write-Host 'Publishing FanForge (win-x64, self-contained, single-file)...'
    & dotnet publish (Join-Path $root 'FanForge.Win\FanForge.Win.csproj') -c $Configuration -r win-x64 `
        --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o $layout
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
}

& (Join-Path $PSScriptRoot 'New-MsixAssets.ps1') -OutputDir (Join-Path $layout 'Assets')

$manifestText = Get-Content -LiteralPath $manifestSource -Raw
$manifestText = [System.Text.RegularExpressions.Regex]::Replace(
    $manifestText, 'Version="\d+\.\d+\.\d+\.\d+"', "Version=`"$Version`"")
if (-not [string]::IsNullOrWhiteSpace($Publisher)) {
    [xml]$manifestXml = $manifestText
    $namespace = New-Object System.Xml.XmlNamespaceManager($manifestXml.NameTable)
    $namespace.AddNamespace('f', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
    $identity = $manifestXml.SelectSingleNode('/f:Package/f:Identity', $namespace)
    if ($null -eq $identity) { throw 'Missing Identity in MSIX manifest.' }
    $identity.SetAttribute('Publisher', $Publisher)
    $manifestXml.Save((Join-Path $layout 'AppxManifest.xml'))
}
else {
    Set-Content -LiteralPath (Join-Path $layout 'AppxManifest.xml') -Value $manifestText -Encoding UTF8
}

& (Join-Path $PSScriptRoot 'verificar-msix.ps1') `
    -Manifest (Join-Path $layout 'AppxManifest.xml') -AssetsDir (Join-Path $layout 'Assets')
if ($LASTEXITCODE -ne 0) { throw 'MSIX structure verification failed.' }

$makeAppx = Get-Command MakeAppx.exe -ErrorAction SilentlyContinue
if ($null -eq $makeAppx) {
    $candidate = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin\*\x64\MakeAppx.exe' -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if ($candidate) { $makeAppx = $candidate.FullName }
    else { $makeAppx = $null }
}
else { $makeAppx = $makeAppx.Source }

if ($null -eq $makeAppx) {
    Write-Host "MakeAppx.exe not found: layout prepared at $layout (not packed)." -ForegroundColor Yellow
    Write-Host 'Install the Windows SDK to produce the .msix, then sign it; see docs/MSIX.md.' -ForegroundColor Yellow
    exit 0
}

$packagePath = Join-Path $OutputDir 'FanForge.msix'
if (Test-Path -LiteralPath $packagePath) { Remove-Item -LiteralPath $packagePath -Force }
& $makeAppx pack /d $layout /p $packagePath /o
if ($LASTEXITCODE -ne 0) { throw 'MakeAppx pack failed.' }

Write-Host "Unsigned MSIX created: $packagePath" -ForegroundColor Yellow
Write-Host 'Sign it with a trusted certificate before installation; see docs/MSIX.md.' -ForegroundColor Yellow
)][string]$Version = '0.8.0.0',
    [string]$OutputDir = (Join-Path $PSScriptRoot '..\dist\msix'),
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$layout = Join-Path $OutputDir 'layout'
$manifestSource = Join-Path $root 'packaging\msix\AppxManifest.xml'

if (Test-Path -LiteralPath $layout) { Remove-Item -LiteralPath $layout -Recurse -Force }
New-Item -ItemType Directory -Path $layout -Force | Out-Null

if (-not $SkipPublish) {
    Write-Host 'Publishing FanForge (win-x64, self-contained, single-file)...'
    & dotnet publish (Join-Path $root 'FanForge.Win\FanForge.Win.csproj') -c $Configuration -r win-x64 `
        --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o $layout
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
}

& (Join-Path $PSScriptRoot 'New-MsixAssets.ps1') -OutputDir (Join-Path $layout 'Assets')

$manifestText = Get-Content -LiteralPath $manifestSource -Raw
$manifestText = [System.Text.RegularExpressions.Regex]::Replace(
    $manifestText, 'Version="\d+\.\d+\.\d+\.\d+"', "Version=`"$Version`"")
Set-Content -LiteralPath (Join-Path $layout 'AppxManifest.xml') -Value $manifestText -Encoding UTF8

& (Join-Path $PSScriptRoot 'verificar-msix.ps1') `
    -Manifest (Join-Path $layout 'AppxManifest.xml') -AssetsDir (Join-Path $layout 'Assets')
if ($LASTEXITCODE -ne 0) { throw 'MSIX structure verification failed.' }

$makeAppx = Get-Command MakeAppx.exe -ErrorAction SilentlyContinue
if ($null -eq $makeAppx) {
    $candidate = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin\*\x64\MakeAppx.exe' -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if ($candidate) { $makeAppx = $candidate.FullName }
    else { $makeAppx = $null }
}
else { $makeAppx = $makeAppx.Source }

if ($null -eq $makeAppx) {
    Write-Host "MakeAppx.exe not found: layout prepared at $layout (not packed)." -ForegroundColor Yellow
    Write-Host 'Install the Windows SDK to produce the .msix, then sign it; see docs/MSIX.md.' -ForegroundColor Yellow
    exit 0
}

$packagePath = Join-Path $OutputDir 'FanForge.msix'
if (Test-Path -LiteralPath $packagePath) { Remove-Item -LiteralPath $packagePath -Force }
& $makeAppx pack /d $layout /p $packagePath /o
if ($LASTEXITCODE -ne 0) { throw 'MakeAppx pack failed.' }

Write-Host "Unsigned MSIX created: $packagePath" -ForegroundColor Yellow
Write-Host 'Sign it with a trusted certificate before installation; see docs/MSIX.md.' -ForegroundColor Yellow
