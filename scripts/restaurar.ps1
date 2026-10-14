#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$path = Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\FanForge.exe'
if (-not (Test-Path $path)) { throw 'Compila primero: scripts/compilar.ps1' }
& $path --restore
