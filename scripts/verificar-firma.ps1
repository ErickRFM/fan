#Requires -Version 5.1
<#
.SYNOPSIS
  Fails closed if an executable is not signed by a locally trusted publisher.
.DESCRIPTION
  Checks Authenticode validation on Windows. It does not bypass Smart App Control,
  change trust stores or accept self-signed certificates as a distribution solution.
  The signed-release workflow additionally runs Windows SDK SignTool verify /pa.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Path
)
$ErrorActionPreference = 'Stop'
$file = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
$signature = Get-AuthenticodeSignature -LiteralPath $file
if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
    throw "Authenticode signature is not trusted: $($signature.Status) - $($signature.StatusMessage). File: $file"
}
if ($null -eq $signature.SignerCertificate) {
    throw "No signing certificate found: $file"
}
$codeSigningOid = '1.3.6.1.5.5.7.3.3'
$ekuPresent = $false
foreach ($extension in $signature.SignerCertificate.Extensions) {
    if ($extension.Oid.Value -eq '2.5.29.37') {
        $eku = [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]$extension
        $ekuPresent = @($eku.EnhancedKeyUsages | Where-Object { $_.Value -eq $codeSigningOid }).Count -gt 0
    }
}
if (-not $ekuPresent) {
    throw "Signing certificate lacks Code Signing EKU: $file"
}
Write-Host "VALID signature - $($signature.SignerCertificate.Subject)" -ForegroundColor Green
Write-Host "SHA256 $((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash)"
