#requires -Version 5.1
<#
.SYNOPSIS
  Generates the placeholder MSIX logo assets reproducibly (no binary blobs committed by hand).
.NOTES
  These are neutral "FF" tiles, not a branded identity. Replace them before any real distribution.
#>
param(
    [string]$OutputDir = (Join-Path $PSScriptRoot '..\packaging\msix\Assets')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not (Test-Path -LiteralPath $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

function New-Logo {
    param([int]$Width, [int]$Height, [string]$Path)
    $bitmap = New-Object System.Drawing.Bitmap($Width, $Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::FromArgb(14, 19, 30))
        $size = [Math]::Max(12, [int]($Height / 3))
        $font = New-Object System.Drawing.Font('Segoe UI', $size, [System.Drawing.FontStyle]::Bold)
        $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(69, 212, 196))
        $format = New-Object System.Drawing.StringFormat
        $format.Alignment = [System.Drawing.StringAlignment]::Center
        $format.LineAlignment = [System.Drawing.StringAlignment]::Center
        $rect = New-Object System.Drawing.RectangleF(0, 0, $Width, $Height)
        $graphics.DrawString('FF', $font, $brush, $rect, $format)
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
        $font.Dispose(); $brush.Dispose()
    }
    finally {
        $graphics.Dispose(); $bitmap.Dispose()
    }
}

New-Logo -Width 50  -Height 50  -Path (Join-Path $OutputDir 'StoreLogo.png')
New-Logo -Width 44  -Height 44  -Path (Join-Path $OutputDir 'Square44x44Logo.png')
New-Logo -Width 150 -Height 150 -Path (Join-Path $OutputDir 'Square150x150Logo.png')
New-Logo -Width 310 -Height 150 -Path (Join-Path $OutputDir 'Wide310x150Logo.png')

Write-Host "MSIX assets generated in $OutputDir"
