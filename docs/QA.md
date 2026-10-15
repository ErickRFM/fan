# QA — Resultados automatizados (v1.1)

## Entorno de ejecución
- OS: Microsoft Windows 11 Home Single Language, 10.0.26300, x64.
- Equipo: ASUSTeK `Vivobook_ASUSLaptop M3500QC_D3500QC`.
- BIOS: `M3500QC.316` (2023-05-24).
- CPU: AMD Ryzen 5 5600H · GPU: AMD Radeon + NVIDIA GeForce RTX 3050 Laptop.
- ASUS System Control Interface v3: 3.1.72.0.
- `AsusWinIO64.dll`: 1.1.4.0 y 1.1.2.0 en DriverStore (todos los símbolos requeridos presentes).
- SDK: .NET 8.0.426.

## Pruebas simuladas (sin hardware)
Comando:

```powershell
dotnet run --project .\FanForge.Tests\FanForge.Tests.csproj -c Release
```

Resultado: **291/291 PASAN** (la accesibilidad de la UI no aporta pruebas unitarias; se valida visualmente). Cubren:
- Guarda de modelo M3500QC (positivo, mayúsculas/negativo, null).
- Conteo válido/inválido de ventiladores.
- Conversión 70/80/100% a duty y rechazo de 8 valores fuera de rango.
- Confirmación de RPM (`Increased`, `AnyIncreased`, baseline inválido, ventilador parado).
- Restauración preventiva (dentro/fuera de gracia; solo en modo manual; ventilador girando).
- Selección de la DLL OEM más reciente (incluye conjunto vacío y ruta en blanco).
- **Decodificación ATKACPI** (`AcpiFanRpm` de raw a RPM×100 con límites, `AcpiTemperature`) y **cambio de modo VivoBook** (intercambio Turbo/Silencio).
- **Semántica de perfiles VivoBook** (`VivoBookProfileWireMode`, `VivoBookProfileConfirmed`): sin falso perfil Máximo, sin éxito ante centinelas inválidos ni respuestas IOCTL incompletas.
- **Combinación de telemetría** (`TelemetryMerge`): prioridad del primer proveedor con RPM válidos, descarte de RPM `-1`, relleno de temperatura desde proveedores posteriores y caso vacío.
- **Historial acotado** (`TelemetryHistory`): recorte a capacidad, orden cronológico, mapeo de RPM por tipo, descarte de valores no legibles, `Latest` y `Clear`.
- **Conflictos de control** (`ControlConflicts`): detección insensible a mayúsculas y `.exe`, deduplicación, orden y aviso; procesos desconocidos se ignoran.
- **Biblioteca OEM** (`OemLibraryPolicy`): aceptación por versión mínima, confianza solo en firmantes ASUS/ASUSTeK y descripción legible del estado.
- **Watchdog bidireccional** (`WatchdogPolicy`): clasificación inactivo/activo/perdido y restauración obligatoria solo cuando se pierde durante control manual.
- Máquina de estados `FanSession` con **controlador falso**: aplicar, reaplicar por intervalo, evento de reaplicación, confirmación/no confirmación, restauración por parada, propagación de error OEM con restauración, y `Dispose` idempotente.
- **Perfil original** (`ProfileGuard` con `FakeProfileChannel`/`FakeProfileStore`): captura antes del primer cambio, restaura **ese** perfil (no Estándar), bloqueo cuando el original no es legible, omisión de escritura cuando el perfil pedido es igual al actual, rechazo de escrituras no confirmadas sin persistir, y recuperación del registro tras un cierre inesperado.
- **Persistencia del perfil original** (`OriginalProfileCodec`): ida y vuelta del registro, tolerancia a datos vacíos/corruptos y no uso de registros obsoletos en sesión interactiva.
- **Formato CSV** (`TelemetryCsv`): cabecera, escape de comas/comillas, separación de perfil confirmado y RPM observado, y línea por muestra.
- **Analizador térmico** (`ThermalAnalyzer`/`TelemetryCsvParser`/`ThermalReport`): parseo de CSV (ida y vuelta, detección de huecos temporales y filas inválidas, import de ProArt con alias de cabecera), estadísticas (min/max/media/p90/p95), correlación temperatura–RPM y desglose por perfil, y generación del informe HTML/CSV.
- **Manifiesto MSIX** (`MsixManifest`): acepta un manifiesto válido y rechaza XML mal formado, vacío, arquitectura no x64, versión no de cuatro partes, entrada no *full trust* y ausencia de identidad; también valida el manifiesto real del repositorio.
- **Recuperación ampliada**: ciclo completo aplicar → crash → recuperar, registro `Unchanged` cuando el firmware ya coincide (limpiando el registro), retención del registro si el registro es ilegible, rechazo de valores fuera de rango y de versiones desconocidas del códec.
- **Códec WMI** (`AsusWmiCodec`): decodificación del bit de validez `0x10000` (casos `0x0001002D→4500`, `0x00010032→5000`, `0x00010024→3600`, `0x00010026→3800`), rechazo de centinelas (`0xFFFFFFFE`, `0xFFFFFFFF`) y de payloads fuera de rango (>120), y flag de validez ausente.
- **Motor FanSpeed** (`FanSpeedEngine`, `SimulatedFanSpeedBackend`, `FakeSpeedBackend`): detección con backend verificado; aplicación confirmada vs. aceptada-sin-confirmar (nunca se reporta completa si no hay confirmación); promoción a `Verified` solo con aumento de RPM; rechazo de porcentaje fuera de rango; `Unsupported` cuando no hay PWM verificado; bloqueo de escritura ante lecturas incoherentes; degradación a `Unsupported` si la detección falla; restauración que limpia el estado manual; y pasada de seguridad sin control manual.
- **Evidencia y veredicto (v1.1)** (`FanEvidence`): cabecera y fila CSV exactas, escape de comas, huecos numéricos en blanco, detección de canal expuesto (Unsupported/Detected/Unverified), veredicto que **nunca** llega a `Verified` sin ≥2 confirmaciones de RPM **y** restauración completa (`Blocked`/`InsufficientEvidence`/`RpmResponded`/`Verified`) y manifiesto JSON con `control_path_exposed`, `verdict` y `pwm_cpu` por backend.
- **Control por ventilador (v1.0)** (`PerFanSpeedBackend`): capacidades CPU/GPU independientes; `Both` rechazado si un ventilador no es controlable; aplicación CPU independiente de GPU (y viceversa); *ambos 100 %*; estado por ventilador (`ManualCpuPercent`/`ManualGpuPercent`); auditoría de `RequestedPercent`; restauración que limpia ambos ventiladores; `Compatible` para ruta no verificada (nunca escribible); `DetectedUnverified` para backend solo-telemetría; y restauración preventiva ante 0 RPM en control manual.

Ninguna prueba toca hardware; los backends son mocks (`FakeBackend`, `FakeProfileChannel`, `FakeProfileStore`) y el reloj es determinista (`ManualTime`).

## Compilación
```powershell
dotnet build .\FanForge.Win\FanForge.Win.csproj -c Release   # 0 advertencias, 0 errores
.\scripts\compilar.ps1                                        # tests + publish
```
Resultado: `dist/FanForge.exe` (PE Machine 0x8664, x64). No incluye `AsusWinIO64.dll`.

## Pruebas de integración / físicas
Pendientes: requieren consola elevada y presencia del operador. Ver `docs/HARDWARE-TEST.md`.

### Sonda física (elevada) — 2026-10-14
- `--probe` ejecutado con UAC: detectó modelo `M3500QC`, BIOS `M3500QC.316` y la DLL más reciente (1.1.4.0).
- `HealthyTable_FanCounts()` devolvió `-1`; prueba aislada contra ambas DLL (1.1.4.0 y 1.1.2.0) devolvió `FanCounts=-1`, `FanRPM=-1`, `CPUTemp=0x7FFFFFFF` tras `InitializeWinIo()=0`.
- Causa: driver de kernel `AsusSAIO` en estado `Stop Pending`/`Degraded`. Coincide con la regresión conocida de `AsusWinIO64.dll` (Karmel0x/AsusFanControl #44).
- Conclusión: **el control PWM manual queda bloqueado a nivel de driver ASUS** en el estado actual. No se modificaron drivers ni el EC. Próximos pasos en `docs/HARDWARE-TEST.md`.

### Sonda multibackend ATKACPI (elevada) — 2026-10-14 (v0.4)
- `--hardware-diagnose` en consola elevada: proveedores `AsusACPI (ATKACPI)` + `ACPI thermal zone (WMI)`.
- **RPM reales:** `CPU_Fan(0x00110013)` = 44–61 (→ 4400–6100 RPM), `GPU_Fan(0x00110014)` = 38–40 (→ 3800–4000 RPM).
- **Temperatura:** `Temp_CPU(0x00120094)`/`Temp_GPU(0x00120097)` = `-65536` (**no soportado**); la zona térmica ACPI (WMI) aportó ~90 °C (aproximada).
- **Capacidades:** `perfiles=True`, `curvas=False`, `pwm=False`. `VivoBookMode(0x00110019)` = `131074` (soportado); `PerformanceMode(0x00120075)` = `-65536`.
- Conclusión: **telemetría de RPM vía ATKACPI operativa** y **cambio de perfil disponible**; PWM/curvas ATKACPI no existen en este modelo.

### Escritura de perfil + robustez de lectura — 2026-10-15 (v0.4.7, elevado, autorizado)
- `--set-profile` ejecutado en el M3500QC: Silent `131074→131073`, Standard `131073→131072`, Performance `131072→131074`; todas `status=1`, `verificado=True`, y se restauró al modo original (`131074`). Codificación `raw = 131072 | wireMode`.
- **Bug de robustez corregido:** un endpoint no soportado (`Temp_CPU`) hacía fallar `DeviceIoControl` y **invalidaba el handle** para TODAS las lecturas siguientes; `ReadAsync` perdía las RPM ya leídas. Ahora cada endpoint se lee con `SafeGet` (tolerante) y `--hardware-diagnose` reporta por método.
- Evidencia: RPM reales `CPU=7000/6300`, `GPU=4300/4200`; `Temp_CPU/GPU`, `PerformanceMode` y curvas devuelven `ERROR ... DeviceIoControl failed` (no consultables en este BIOS).
- Conclusión: **cambio de perfil físico verificado y reversible**; la temperatura sigue viniendo de WMI.


## Auditoría correctiva v0.6 — verificación automatizada
- Pruebas nuevas de CN estricto y política `MayLoad` (firma válida + versión + proveedor autorizado).
- `WinVerifyTrust` nativo se invoca únicamente al intentar cargar `AsusWinIO64.dll` en Windows; **no puede validarse en CI sin una DLL ASUS real**.
- Operaciones ATKACPI desacopladas por handle; perfil de escritura con guarda del M3500QC.
- Validación física de firma sigue **pendiente**; el cambio de perfiles **sí** se validó en el M3500QC el 2026-10-15 (ver más abajo). No inferir compatibilidad PWM.

## v0.6 — Auditoría en CI Windows (GitHub)
- **117/117 comprobaciones de política y mocks aprobadas** (115 de v0.6 + 2 de la integración del fix de lectura ATKACPI; PR #15).
- Compilación Release y publicación win-x64: PASS. Paquete de previsualización no firmado como app.
- Autenticidad del archivo nativo: `WinVerifyTrust` integrado; **sin prueba con la DLL instalada físicamente** en el runner de CI. No se verifica revocación remota.
- Se requiere probar en M3500QC que el driver OEM legítimo sea aceptado y que el comportamiento de perfiles no cambie inesperadamente.
- Esto no comprueba PWM ni ventiladores al 100%; se mantienen bloqueados hasta recuperar HealthyTable y medir RPM.

### Perfil original — VALIDADO en el equipo físico — 2026-10-15 (v0.7, elevado, autorizado)
- Estado inicial: `VivoBookMode = 131074` (Performance, wire 2).
- `--set-profile Standard` → `OK: Perfil Standard aplicado y confirmado (wire 2 -> 0); original 2 guardado.` El guard capturó el original **antes** de escribir y lo persistió.
- `--restore` → `Perfil ASUS: OK: Perfil original restaurado (wire 0 -> 2).` El PWM no se restauró (esperado: `HealthyTable_FanCounts() = -1`).
- Estado final: `VivoBookMode = 131074` (Performance). **Restauración exacta del original, reversible.**
- Conclusión: el control de perfiles ya no asume Estándar; devuelve el equipo al perfil en el que estaba.

### Estado del driver ASUS — 2026-10-15 (solo lectura)
- `AsusSAIO`: `Running` / `OK`, `StartMode = Manual`; dispositivo «ASUS System Control Interface v3» `Error=0`.
- `AsusOptimization`: `Running` / `Auto` (posible competencia por el control, avisada en la UI).
- Aun con el driver en `Running/OK`, `HealthyTable_FanCounts() = -1`: el firmware **no expone ventiladores** por esa vía en este modelo/BIOS, de modo que el PWM manual está **bloqueado por diseño**, no por un driver degradado.

### Registro CSV de telemetría — 2026-10-15 (v0.7, solo lectura)
- `--log-telemetry` escribe `telemetry-*.csv` con `timestamp,RPM CPU,GPU,temperatura,perfil` a ~1 Hz.
- No activa PWM ni el modo de prueba; puede ejecutarse con el perfil en cualquiera de los modos ASUS.

## v0.8 — Analizador, MSIX, recuperación e investigación PWM
- **Analizador térmico** (`--analyze`): probado de extremo a extremo con un CSV de ejemplo (30 filas) → `thermal-report.html` y `thermal-stats.csv` generados; estadísticas y correlación correctas. Solo lectura de archivos, sin hardware.
- **MSIX**: `scripts/verificar-msix.ps1` en local y en CI (`windows-latest`) verifica identidad x64, versión de 4 partes, *entry point* full trust y presencia de los cuatro logos. El empaquetado real (`MakeAppx`) requiere el Windows SDK; no se firma ningún paquete en CI (un MSIX sin firmar no se instala).
- **Recuperación**: ampliada con escenarios de ciclo completo; ver `docs/RECOVERY.md`.
- **PWM**: documentado el bloqueo por firmware (`docs/PWM-RESEARCH.md`); sin cambios de drivers/EC.
- Pruebas: **223/223**; Release 0 avisos / 0 errores.

## v0.9 — Motor FanSpeed, telemetría WMI y centro de control
- **Motor FanSpeed** (`FanSpeedEngine`): probado con backends simulados/falsos; distingue aceptado (`AppliedUnverified`) de confirmado (`Verified`) y **nunca presenta como completada** una operación no confirmada. Verificación promovida solo con aumento medido de RPM (>150).
- **Códec WMI** (`AsusWmiCodec`): validado con los casos RAW verificados del M3500QC y centinelas inválidos.
- **CLI de solo lectura** (`--fan-backends`, `--fan-capabilities`, `--fan-read`, `--fan-recovery-status`): ejecutado en el M3500QC; ningún backend reporta PWM `Verified`, el motor queda `AvailableUnverified` y el control manual permanece bloqueado. `--fan-read` confirmó RPM ATKACPI (CPU 5500 / GPU 3900) y todos los backends responden sin lanzar excepciones.
- **Centro de control GUI**: pestaña **FanSpeed**; los controles de escritura quedan deshabilitados (ningún canal verificado). No se activó ningún control manual.
- **Backends Win**: ATKACPI (RPM/perfiles), WMI (solo lectura, tolerante), HealthyTable (PWM *gated*, nunca auto-verificado) y PawnIO (stub deshabilitado). No se modificaron drivers ni el EC.
- Pruebas: **252/252**; Release 0 avisos / 0 errores.

## v1.0 — Control directo por ventilador
- **Modelo por ventilador** (`FanChannelCapabilities`): probado que CPU y GPU se rastrean por separado; `Both` solo se considera controlable si **cada** ventilador lo es; una ruta `Unverified` reporta `Compatible` y **nunca** es escribible; un backend solo-telemetría reporta `DetectedUnverified`.
- **Control independiente**: aplicar a CPU no altera el estado de GPU (ni al revés); *ambos 100 %* aplica a los dos; `RestoreAutomaticAsync` limpia ambos ventiladores.
- **Auditoría**: cada transición registra `RequestedPercent` y `ObservedRpm` por separado (un solicitado nunca equivale a RPM alcanzadas).
- **Seguridad**: con control manual activo, un ventilador que reporta 0 RPM dispara restauración preventiva (`EvaluateSafetyAsync` → `Restored`).
- **CLI de solo lectura**: `--fan-diagnostics` (capacidades por ventilador y motivo del bloqueo) y `--fan-research-report` (matriz de interfaces + detección en vivo). Ninguno escribe PWM.
- **UI**: pestaña FanSpeed responsiva (reflujo de texto por ancho) y gating por ventilador; revisada a 100/125/150/200 % DPI.
- **M3500QC**: sigue **sin canal PWM verificado** (`HealthyTable_FanCounts()=-1`, curvas ATKACPI `-65536`); el control manual permanece bloqueado. No se forzó el EC ni se modificaron drivers.
- Pruebas: **268/268** (antes 252); Release 0 avisos / 0 errores.

## v1.1 — Evidencia de solo lectura y protocolo de validación física
- **Evidencia** (`FanEvidence`): hoja CSV determinista por backend + manifiesto JSON y veredicto. Probado que el veredicto **nunca** alcanza `Verified` sin un canal expuesto, ≥2 confirmaciones de RPM y restauración completa; `Blocked` sin canal; `InsufficientEvidence` con <2 confirmaciones; `RpmResponded` cuando falta cualquier parte de la restauración.
- **CLI** `--fan-evidence <dir> [--seconds N]`: escribe CSV/JSON y veredicto; **no** escribe PWM (por diseño `confirmedReadings = 0`, así que jamás declara `Verified`). No smoke-tested en esta sesión por requerir elevación (manifiesto `requireAdministrator`); cubierto por pruebas unitarias puras.
- **Script** `scripts/recolectar-evidencia-fan.ps1`: verificado sintácticamente (parser de PowerShell). Solo invoca comandos de solo lectura; omite lecturas si no hay elevación.
- **Protocolo** `docs/DIRECT-FAN-CONTROL-VALIDATION.md`: revisado contra `FanEvidence.Evaluate` y `FanPolicy` (ventana 70–100 %, histéresis 150 RPM).
- **Corrección**: `FanSpeedBackends.cs` vuelve a UTF-8 sin BOM (se eliminó mojibake de v1.0); `FanForge.Tests/Program.cs` sin BOM.
- **M3500QC**: veredicto `Blocked`. No se ejecutó ninguna prueba física ni se declaró PWM compatible.
- Pruebas: **291/291** (antes 268); Release 0 avisos / 0 errores.
