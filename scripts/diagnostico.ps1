#Requires -Version 5.1
# FANFORGE: diagnostico informativo. Por defecto NO escribe PWM.
$ErrorActionPreference = 'Continue'
Write-Host 'FANFORGE: diagnostico de solo lectura; no escribe velocidades de ventilador.' -ForegroundColor Cyan

Write-Host "`n== Equipo ==" -ForegroundColor Yellow
Get-CimInstance Win32_ComputerSystem | Select-Object Manufacturer,Model | Format-List
Get-CimInstance Win32_BIOS | Select-Object SMBIOSBIOSVersion,ReleaseDate | Format-List
Get-CimInstance Win32_Processor | Select-Object Name | Format-List

Write-Host "== Controlador ASUS ==" -ForegroundColor Yellow
Get-CimInstance Win32_PnPSignedDriver |
  Where-Object { $_.DeviceName -like '*ASUS System Control Interface*' } |
  Select-Object DeviceName,DriverVersion | Format-Table -AutoSize

Write-Host "== Servicios ASUS ==" -ForegroundColor Yellow
Get-Service | Where-Object { $_.Name -like '*ASUS*' } |
  Select-Object Name,Status,DisplayName | Format-Table -AutoSize

Write-Host "== Biblioteca OEM AsusWinIO64.dll (DriverStore) ==" -ForegroundColor Yellow
$root = Join-Path $env:WINDIR 'System32\DriverStore\FileRepository'
$found = Get-ChildItem -Path $root -Recurse -Filter 'AsusWinIO64.dll' -File -ErrorAction SilentlyContinue
if (-not $found) {
  Write-Host 'No se encontro AsusWinIO64.dll en DriverStore.' -ForegroundColor Red
} else {
  $found | ForEach-Object {
    [pscustomobject]@{ Ruta = $_.FullName; Version = $_.VersionInfo.FileVersion; Modificado = $_.LastWriteTime }
  } | Format-Table -AutoSize
  $newest = $found | Sort-Object { [version]($_.VersionInfo.FileVersion) } -Descending | Select-Object -First 1
  Write-Host ("Version mas reciente (la que FanForge priorizara): {0} ({1})" -f $newest.VersionInfo.FileVersion, $newest.FullName) -ForegroundColor Green
}

$exe = Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\FanForge.exe'
Write-Host "`n== Sonda de lectura (opcional) ==" -ForegroundColor Yellow
if (Test-Path $exe) {
  if (([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host 'Ejecutando probe de solo lectura (no activa control manual)...' -ForegroundColor Cyan
    & $exe --probe
  } else {
    Write-Host "Para leer RPM/CPU reales ejecuta en una consola elevada:`n  `"$exe`" --probe" -ForegroundColor DarkYellow
  }
} else {
  Write-Host 'dist\FanForge.exe no existe; compila con scripts\compilar.ps1.' -ForegroundColor DarkYellow
}

Write-Host "`nDiagnostico terminado; no se escribieron velocidades." -ForegroundColor Green
