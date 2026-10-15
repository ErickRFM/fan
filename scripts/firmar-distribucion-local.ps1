#Requires -Version 5.1
<#
.SYNOPSIS
  Sign FanForge.exe and FanForge.msix with a publicly trusted, locally installed code-signing certificate.
.DESCRIPTION
  The signing key stays in the certificate provider (e.g. hardware token or HSM).
  No PFX export, no security bypass, no certificates added to Trusted Root.
  Requires Windows SDK SignTool and MakeAppx; authenticates any PIN through the provider.
.EXAMPLE
  .\scripts\firmar-distribucion-local.ps1 -CertificateThumbprint '0123456789ABCDEF0123456789ABCDEF01234567' -Version '0.9.1.0'
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$CertificateThumbprint,
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$Version = '0.9.1.0',
    [switch]$MachineStore
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
Set-Location $root

$store = if ($MachineStore) { 'Cert:\LocalMachine\My' } else { 'Cert:\CurrentUser\My' }
$thumbprint = $CertificateThumbprint.ToUpperInvariant()
$cert = Get-ChildItem -Path $store -ErrorAction Stop |
    Where-Object { $_.Thumbprint -eq $thumbprint } | Select-Object -First 1
if ($null -eq $cert) { throw "Certificate not found in $store (thumbprint $thumbprint)." }
if (-not $cert.HasPrivateKey) { throw 'Certificate does not expose an accessible signing private key.' }
if ((Get-Date) -lt $cert.NotBefore -or (Get-Date) -gt $cert.NotAfter) {
    throw 'Signing certificate is not currently valid.'
}
$hasCodeSigningEku = $false
foreach ($ext in $cert.Extensions) {
    if ($ext.Oid.Value -eq '2.5.29.37') {
        $eku = [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]$ext
        $hasCodeSigningEku = @($eku.EnhancedKeyUsages | Where-Object { $_.Value -eq '1.3.6.1.5.5.7.3.3' }).Count -gt 0
    }
}
if (-not $hasCodeSigningEku) { throw 'Certificate lacks the Code Signing enhanced key usage.' }

$tool = Get-Command signtool.exe -ErrorAction SilentlyContinue
if ($tool) { $signTool = $tool.Source }
else {
    $signTool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe' -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $signTool) { throw 'Install the Windows SDK: signtool.exe is required.' }

$signArgs = @('sign', '/sha1', $thumbprint, '/fd', 'SHA256',
              '/tr', 'http://timestamp.acs.microsoft.com', '/td', 'SHA256', '/v')
if ($MachineStore) { $signArgs += '/sm' }

Write-Host 'Running policy tests and building FanForge...' -ForegroundColor Cyan
& dotnet run --project './FanForge.Tests/FanForge.Tests.csproj' -c Release
if ($LASTEXITCODE -ne 0) { throw 'Tests failed; refusing to sign.' }

$staged = Join-Path $root 'dist\signed-local'
& dotnet publish './FanForge.Win/FanForge.Win.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o $staged
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
$exe = Join-Path $staged 'FanForge.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'FanForge.exe not found after publish.' }

Write-Host 'Signing executable with local certificate provider...' -ForegroundColor Cyan
& $signTool @signArgs $exe
if ($LASTEXITCODE -ne 0) { throw 'SignTool failed to sign FanForge.exe.' }
& (Join-Path $PSScriptRoot 'verificar-firma.ps1') -Path $exe
$signature = Get-AuthenticodeSignature -LiteralPath $exe
$subject = $signature.SignerCertificate.Subject
if ([string]::IsNullOrWhiteSpace($subject)) { throw 'No certificate publisher subject.' }

Write-Host 'Packing MSIX from the signed executable...' -ForegroundColor Cyan
& (Join-Path $PSScriptRoot 'empaquetar-msix.ps1') -SkipPublish -SourceDir $staged -Publisher $subject -Version $Version
$msix = Join-Path $root 'dist\msix\FanForge.msix'
if (-not (Test-Path -LiteralPath $msix)) { throw 'Missing MSIX package (MakeAppx required).' }

Write-Host 'Signing MSIX with the same certificate...' -ForegroundColor Cyan
& $signTool @signArgs $msix
if ($LASTEXITCODE -ne 0) { throw 'SignTool failed to sign the MSIX.' }
& $signTool verify /pa /v $exe
if ($LASTEXITCODE -ne 0) { throw 'EXE signature verification failed.' }
& $signTool verify /pa /v $msix
if ($LASTEXITCODE -ne 0) { throw 'MSIX signature verification failed.' }

$zip = Join-Path $root 'dist\FanForge-local-signed-win-x64.zip'
Compress-Archive -LiteralPath $exe -DestinationPath $zip -Force
$hashes = foreach ($file in @($zip, $msix)) {
    $hash = Get-FileHash -LiteralPath $file -Algorithm SHA256
    "$($hash.Hash)  $(Split-Path $file -Leaf)"
}
$hashes | Set-Content (Join-Path $root 'dist\SHA256SUMS-local.txt') -Encoding Ascii
Write-Host 'Signed and verified: ZIP, MSIX and SHA256SUMS-local.txt in dist/.' -ForegroundColor Green
