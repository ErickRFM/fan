#requires -Version 5.1
<#
.SYNOPSIS
  Verifies the structural requirements of the FanForge MSIX package without the packaging SDK.
.DESCRIPTION
  Checks the manifest identity (x64, four-part version, name/publisher), the full-trust desktop
  entry point and the presence of every referenced logo asset. It does NOT claim the package will be
  accepted by Windows: an unsigned MSIX still needs a trusted certificate signature.
#>
param(
    [string]$Manifest = (Join-Path $PSScriptRoot '..\packaging\msix\AppxManifest.xml'),
    [string]$AssetsDir = (Join-Path $PSScriptRoot '..\packaging\msix\Assets')
)

$ErrorActionPreference = 'Stop'
$errors = @()

if (-not (Test-Path -LiteralPath $Manifest)) { throw "Manifest not found: $Manifest" }

[xml]$xml = Get-Content -LiteralPath $Manifest -Raw
$ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
$ns.AddNamespace('f', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')

$identity = $xml.SelectSingleNode('/f:Package/f:Identity', $ns)
if (-not $identity) {
    $errors += 'Missing <Identity>.'
}
else {
    if ($identity.ProcessorArchitecture -ne 'x64') { $errors += "ProcessorArchitecture must be x64 (found '$($identity.ProcessorArchitecture)')." }
    if ($identity.Version -notmatch '^\d+\.\d+\.\d+\.\d+$') { $errors += "Version must be four-part (found '$($identity.Version)')." }
    if ([string]::IsNullOrWhiteSpace($identity.Name)) { $errors += 'Identity@Name is empty.' }
    if ([string]::IsNullOrWhiteSpace($identity.Publisher)) { $errors += 'Identity@Publisher is empty.' }
}

$application = $xml.SelectSingleNode('/f:Package/f:Applications/f:Application', $ns)
if (-not $application) {
    $errors += 'Missing <Applications>/<Application>.'
}
else {
    if ($application.EntryPoint -ne 'Windows.FullTrustApplication') { $errors += "EntryPoint must be Windows.FullTrustApplication (found '$($application.EntryPoint)')." }
    if ([string]::IsNullOrWhiteSpace($application.Executable)) { $errors += 'Application@Executable is empty.' }
}

if (-not $xml.SelectSingleNode('/f:Package/f:Dependencies/f:TargetDeviceFamily', $ns)) {
    $errors += 'Missing <Dependencies>/<TargetDeviceFamily>.'
}

foreach ($logo in @('StoreLogo.png', 'Square44x44Logo.png', 'Square150x150Logo.png', 'Wide310x150Logo.png')) {
    if (-not (Test-Path -LiteralPath (Join-Path $AssetsDir $logo))) { $errors += "Missing asset: $logo" }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Host "MSIX FAIL: $_" -ForegroundColor Red }
    exit 1
}
Write-Host 'MSIX OK: x64 identity, full-trust entry point and all assets present.' -ForegroundColor Green
exit 0
