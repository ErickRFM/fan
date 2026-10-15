#Requires -Version 5.1
<#
.SYNOPSIS
  FanForge: recolector de evidencia de ventiladores. SOLO LECTURA.

.DESCRIPTION
  Ejecuta unicamente comandos de solo lectura de FanForge y del sistema, y guarda un paquete con
  marca de tiempo (CSV/TXT) para determinar si este equipo expone un canal PWM por ventilador.

  NUNCA escribe PWM, no entra en modo de prueba, no cambia el perfil, no instala/activa drivers y no
  toca protecciones de Windows. Es seguro ejecutarlo en cualquier momento.

  Las lecturas reales (ATKACPI/WMI/OEM) requieren consola ELEVADA. Sin elevacion, el script omite
  esos comandos, avisa y aun asi conserva la evidencia disponible (temperaturas WMI, drivers, etc.).

.PARAMETER Segundos
  Duracion opcional (0-3600) del muestreo 1 Hz de solo lectura. 0 = sin muestreo.

.PARAMETER Salida
  Directorio de salida. Por defecto: <repo>\evidencia-fan\<yyyyMMdd-HHmmss>.

.PARAMETER Exe
  Ruta al ejecutable de FanForge. Por defecto busca dist\FanForge.exe y el binario de Release.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\scripts\recolectar-evidencia-fan.ps1 -Segundos 60

.NOTES
  Protocolo: docs/DIRECT-FAN-CONTROL-VALIDATION.md
#>
[CmdletBinding()]
param(
    [ValidateRange(0, 3600)]
    [int]$Segundos = 0,

    [string]$Salida,

    [string]$Exe
)

$ErrorActionPreference = 'Continue'
$repo = Split-Path -Parent $PSScriptRoot

function Write-Step([string]$text)  { Write-Host "`n== $text ==" -ForegroundColor Yellow }
function Write-Info([string]$text)  { Write-Host $text -ForegroundColor Cyan }
function Write-Warn2([string]$text) { Write-Host $text -ForegroundColor DarkYellow }
function Write-Ok([string]$text)    { Write-Host $text -ForegroundColor Green }

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
if (-not $Salida) { $Salida = Join-Path $repo "evidencia-fan\$stamp" }
New-Item -ItemType Directory -Force -Path $Salida | Out-Null
$Salida = (Resolve-Path -LiteralPath $Salida).Path

Write-Host 'FANFORGE: evidencia de ventiladores (SOLO LECTURA). No se escribe PWM.' -ForegroundColor Cyan
Write-Info "Salida: $Salida"
Write-Info ("Elevacion: " + $(if ($isAdmin) { 'SI' } else { 'NO (se omiten lecturas que la requieren)' }))

# Resolver el ejecutable.
$candidates = @()
if ($Exe) { $candidates += $Exe }
$candidates += (Join-Path $repo 'dist\FanForge.exe')
$candidates += (Join-Path $repo 'FanForge.Win\bin\Release\net8.0-windows\win-x64\FanForge.exe')
$exePath = $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1

# --- 1. Ficha del equipo (solo lectura) ---
Write-Step 'Equipo'
$machine = [ordered]@{
    timestamp_utc = (Get-Date).ToUniversalTime().ToString('o')
    elevation     = $isAdmin
    model         = (Get-CimInstance Win32_ComputerSystem).Model
    bios          = (Get-CimInstance Win32_BIOS).SMBIOSBIOSVersion
    cpu           = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name
    gpu           = ((Get-CimInstance Win32_VideoController).Name -join ' | ')
}
$machine.GetEnumerator() | ForEach-Object { "{0} = {1}" -f $_.Key, $_.Value } |
    Set-Content -LiteralPath (Join-Path $Salida 'equipo.txt') -Encoding UTF8
$machine.GetEnumerator() | ForEach-Object { Write-Info ("  {0}: {1}" -f $_.Key, $_.Value) }

# --- 2. Estado de drivers y servicios ASUS (solo lectura) ---
Write-Step 'Drivers y servicios ASUS'
Get-CimInstance Win32_PnPSignedDriver |
    Where-Object { $_.DeviceName -like '*ASUS System Control Interface*' } |
    Select-Object DeviceName, DriverVersion, DriverDate |
    Export-Csv -NoTypeInformation -LiteralPath (Join-Path $Salida 'driver-asus.csv') -Encoding UTF8
Get-Service | Where-Object { $_.Name -like '*ASUS*' } |
    Select-Object Name, Status, StartType, DisplayName |
    Export-Csv -NoTypeInformation -LiteralPath (Join-Path $Salida 'servicios-asus.csv') -Encoding UTF8
Get-Process -ErrorAction SilentlyContinue |
    Where-Object { $_.ProcessName -match 'MyASUS|Armoury|AsusOptimization|AsusSAIO|ATKACPI|g-helper|FanForge' } |
    Select-Object Id, ProcessName, Path |
    Export-Csv -NoTypeInformation -LiteralPath (Join-Path $Salida 'procesos-control.csv') -Encoding UTF8
Write-Info '  driver-asus.csv, servicios-asus.csv, procesos-control.csv'

# --- 3. Temperaturas via WMI (no requiere elevacion) ---
Write-Step 'Temperaturas de zona ACPI (WMI)'
try {
    $temps = Get-CimInstance -Namespace 'root\wmi' -ClassName 'MSAcpi_ThermalZoneTemperature' -ErrorAction Stop |
        ForEach-Object {
            [pscustomobject]@{
                timestamp_utc = (Get-Date).ToUniversalTime().ToString('o')
                instance      = $_.InstanceName
                celsius       = [math]::Round($_.CurrentTemperature / 10 - 273.15, 1)
            }
        }
    $temps | Export-Csv -NoTypeInformation -LiteralPath (Join-Path $Salida 'temperaturas-wmi.csv') -Encoding UTF8
    Write-Info ("  {0} zonas -> temperaturas-wmi.csv" -f @($temps).Count)
} catch {
    Write-Warn2 ("  WMI termico no disponible: " + $_.Exception.Message)
}

# --- 4. Comandos de solo lectura de FanForge ---
if (-not $exePath) {
    Write-Warn2 'No se encontro FanForge.exe; compila con scripts\compilar.ps1. Se conserva el resto de la evidencia.'
} elseif (-not $isAdmin) {
    Write-Warn2 'FanForge.exe encontrado pero la sesion no es elevada: se omiten sus lecturas.'
    Write-Warn2 '  Abre una consola elevada y vuelve a ejecutar este script para capturarlas.'
} else {
    Write-Step 'Lecturas FanForge (solo lectura)'
    $readOnly = @(
        '--probe',
        '--diagnose-driver',
        '--fan-backends',
        '--fan-capabilities',
        '--fan-read',
        '--fan-diagnostics',
        '--fan-research-report',
        '--fan-recovery-status'
    )
    foreach ($cmd in $readOnly) {
        $name = $cmd.TrimStart('-') + '.txt'
        Write-Info "  $cmd"
        try { & $exePath $cmd *>&1 | Out-File -FilePath (Join-Path $Salida $name) -Encoding UTF8 }
        catch { Write-Warn2 ("  fallo " + $cmd + ": " + $_.Exception.Message) }
    }

    # Paquete estructurado (CSV + JSON) con veredicto.
    & $exePath --fan-evidence $Salida
    if ($Segundos -gt 0) {
        & $exePath --fan-evidence $Salida --seconds $Segundos
        & $exePath --log-telemetry $Segundos $Salida
    }
    Write-Info '  fan-evidence-*.csv / .json y (si -Segundos) telemetria'
}

# --- 5. Resumen e interpretacion ---
Write-Step 'Resumen'
$verdict = 'Blocked'
$verdictSource = 'sin --fan-evidence (se requiere elevacion)'
$evidenceJson = Get-ChildItem -LiteralPath $Salida -Filter 'fan-evidence-*.json' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($evidenceJson) {
    try {
        $doc = Get-Content -LiteralPath $evidenceJson.FullName -Raw | ConvertFrom-Json
        $verdict = $doc.verdict
        $verdictSource = $evidenceJson.Name
    } catch { Write-Warn2 ("  no se pudo leer " + $evidenceJson.Name + ": " + $_.Exception.Message) }
}

$readme = @"
FANFORGE - paquete de evidencia (SOLO LECTURA)
Generado: $stamp
Elevacion: $isAdmin
Regla de decision: $verdict   (fuente: $verdictSource)

Interpretacion (ver docs/DIRECT-FAN-CONTROL-VALIDATION.md):
  Blocked              -> ningun backend expone canal de control; no hay nada que validar.
  InsufficientEvidence -> hay via expuesta pero SIN respuesta fisica confirmada (>=2 lecturas).
  RpmResponded         -> la RPM respondio, pero falta demostrar la restauracion segura.
  Verified             -> RPM confirmada y restauracion (normal + watchdog + reanudacion) probada.

Esta captura es SOLO de lectura. Un canal NO se considera controlable hasta una prueba fisica
supervisada (persona presente) que confirme la RPM Y la restauracion segura. FanForge mantiene el
control manual BLOQUEADO mientras no exista esa evidencia.

Archivos:
  equipo.txt               ficha del equipo
  driver-asus.csv          controlador ASUS System Control Interface
  servicios-asus.csv       servicios ASUS
  procesos-control.csv     herramientas de control en ejecucion (posibles conflictos)
  temperaturas-wmi.csv     temperaturas de zona ACPI
  fan-*.txt                salidas de los comandos de solo lectura de FanForge
  fan-evidence-*.csv       hoja de evidencia por backend
  fan-evidence-*.json      manifiesto + veredicto
  log-telemetry-*.csv      muestreo 1 Hz (si -Segundos > 0)

No se escribio PWM, no se activaron drivers y no se modificaron protecciones de Windows.
"@
Set-Content -LiteralPath (Join-Path $Salida 'LEEME.txt') -Value $readme -Encoding UTF8

Write-Ok ("Veredicto: {0}" -f $verdict)
Write-Ok "Paquete de evidencia: $Salida"
Write-Info 'Solo lectura: no se escribio PWM.'
