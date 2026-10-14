#Requires -Version 5.1
$ErrorActionPreference = 'Continue'
Write-Host 'FANFORGE: lectura informativa; no escribe PWM.' -ForegroundColor Cyan
Get-CimInstance Win32_ComputerSystem | Select-Object Manufacturer,Model | Format-List
Get-CimInstance Win32_BIOS | Select-Object SMBIOSBIOSVersion,ReleaseDate | Format-List
Get-Service | Where-Object { $_.Name -like '*ASUS*' } |
  Select-Object Name,Status,DisplayName | Format-Table -AutoSize
$root = Join-Path $env:WINDIR 'System32\DriverStore\FileRepository'
Get-ChildItem -Path $root -Directory -Filter 'asussci*.inf_amd64_*' -ErrorAction SilentlyContinue |
 ForEach-Object { Join-Path $_.FullName 'ASUSSystemAnalysis\AsusWinIO64.dll' } |
 Where-Object { Test-Path $_ } | ForEach-Object { Write-Host "ASUS DLL: $_" }
Write-Host 'Diagnóstico terminado; no se escribieron velocidades.' -ForegroundColor Green
