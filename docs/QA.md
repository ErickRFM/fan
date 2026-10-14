# QA — Resultados automatizados (v0.4)

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

Resultado: **88/88 PASAN**. Cubren:
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
- Máquina de estados `FanSession` con **controlador falso**: aplicar, reaplicar por intervalo, evento de reaplicación, confirmación/no confirmación, restauración por parada, propagación de error OEM con restauración, y `Dispose` idempotente.

Ninguna prueba toca hardware; el backend es un mock (`FakeBackend`) y el reloj es determinista (`ManualTime`).

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
- Conclusión: **telemetría de RPM vía ATKACPI operativa** y **cambio de perfil disponible**; PWM/curvas ATKACPI no existen en este modelo. La escritura de perfil queda pendiente de autorización del usuario presente.

