#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  throw 'Instala .NET 8 SDK: winget install Microsoft.DotNet.SDK.8'
}
& dotnet run --project './FanForge.Tests/FanForge.Tests.csproj' -c Release
if ($LASTEXITCODE -ne 0) { throw 'Policy tests failed.' }
& dotnet publish './FanForge.Win/FanForge.Win.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o './dist'
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
Write-Host 'EXE generado en dist/FanForge.exe (hardware NO validado).' -ForegroundColor Green
