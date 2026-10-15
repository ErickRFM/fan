# FanForge — control experimental de ventiladores ASUS

**Aplicación de escritorio para Windows 11 x64, enfocada inicialmente en el ASUS VivoBook Pro 15 OLED M3500QC.** Implementada en C#/.NET 8 con Windows Forms, acceso a APIs de ASUS System Analysis, lectura de RPM, botones 80%/100%, controles individuales y restauración a modo BIOS.

> **Estado: software v0.9 integrado; hardware PWM en validación.** La compilación y las pruebas automatizadas (simuladas) pasan (**252/252**). En el M3500QC físico, la API HealthyTable/`AsusWinIO64.dll` sigue devolviendo `-1` (`HealthyTable_FanCounts()`) y los endpoints ATKACPI de escritura fallan: el firmware **no expone PWM fijo ni curvas**, así que el control manual queda **bloqueado por diseño** (`Unsupported`). v0.9 añade un **motor FanSpeed multibackend** con estados de verificación (nunca reporta como completado lo no confirmado) y telemetría **WMI** defensiva, además de los backends ATKACPI (RPM reales de CPU/GPU y perfiles ASUS verificados) y de zona térmica ACPI. El control PWM manual solo se habilita si existe un canal **verificado físicamente**. Ver [docs/FANSPEED-RESEARCH.md](docs/FANSPEED-RESEARCH.md), [docs/ASUS-BACKENDS.md](docs/ASUS-BACKENDS.md), [docs/HARDWARE-TEST.md](docs/HARDWARE-TEST.md) y [docs/OEM-INTERFACES.md](docs/OEM-INTERFACES.md). Si aparece `100%` significa **PWM solicitado**, no ventilador a 100% comprobado. No se deben desactivar protecciones térmicas ni instalar drivers no oficiales.

## Funcionalidades

- Rechaza escritura fuera del identificador de modelo `M3500QC` (guardas de política y de backend).
- Localiza la biblioteca ASUS original en `Windows\System32\DriverStore\FileRepository\asussci*.inf_amd64_*\ASUSSystemAnalysis\AsusWinIO64.dll` y **elige determinísticamente la versión más reciente** (las carpetas obsoletas del DriverStore ya no pueden ganar por orden de enumeración).
- Lee conteo de ventiladores y RPM con `HealthyTable_FanCounts()` y `HealthyTable_FanRPM()`.
- Escribe PWM 70–100% mediante `HealthyTable_SetFanTestMode(1)` y `HealthyTable_SetFanPwmDuty()`.
- Supervisa la sesión manual con `FanSession` (núcleo independiente del hardware): reaplica PWM, detecta respuesta real de RPM y **restaura preventivamente a BIOS** si un ventilador deja de girar.
- Interfaz que nunca confunde PWM solicitado con RPM medidas; muestra estado ASUS, modo, watchdog, CPU y registro de eventos; el trabajo de hardware ocurre fuera del hilo de UI.
- Botón **VOLVER A BIOS**, comando `--restore`, restauración al cerrar y observador en proceso separado (`--watchdog`).
- Comando **`--probe`** de solo lectura para leer modelo, BIOS, ventiladores, RPM y CPU sin activar el control manual.
- **Backend ATKACPI** (v0.4): lee **RPM reales de CPU/GPU** (`0x00110013`/`0x00110014`, unidad = 100 RPM) aunque HealthyTable falle, y ofrece **perfiles ASUS** (Silencioso/Estándar/Rendimiento) con verificación por lectura posterior. No hay perfil independiente «Máximo».
- **Perfil original (v0.7):** antes del primer cambio se captura el perfil ASUS activo y se **restaura exactamente ese perfil** (no Estándar por defecto); si no puede leerse, la escritura se bloquea. El original se persiste en `%LocalAppData%\FanForge\original-profile.txt` y `--restore`/el watchdog lo recuperan tras un cierre inesperado.
- **Registro CSV (v0.7):** RPM CPU/GPU, temperatura, perfil activo y hora → `%LocalAppData%\FanForge\logs`; CLI `--log-telemetry` y botón **GRABAR CSV** (solo lectura).
- **Diagnóstico de driver (v0.7, solo lectura):** CLI `--diagnose-driver` informa `AsusSAIO`, `AsusOptimization`, `AsIO3/2` y dispositivos ASUS/ATK sin modificar nada.
- **Motor FanSpeed (v0.9):** `FanSpeedEngine` (Core) orquesta varios backends con un único canal de escritura serializado y separa *detección / verificación / solicitud / respuesta / RPM medidas*. Una operación **nunca se presenta como completada** si no está confirmada físicamente (`Accepted` ≠ `Confirmed`). Incluye guarda de concurrencia CPU/GPU, restauración preventiva ante lecturas incoherentes y auditoría.
- **Códec y telemetría WMI (v0.9):** `AsusWmiCodec` decodifica el valor con bit de validez `0x10000` (RPM = valor×100) y rechaza centinelas; el backend WMI (`root\wmi:AsusAtkWmi_WMNB`) es defensivo y de solo lectura.
- **Centro de control FanSpeed (v0.9, GUI):** pestaña **FanSpeed** con estado del motor, capacidades por backend, objetivos CPU/GPU (70–100 %), botones *Aplicar / Confirmar RPM / Volver a automático* y auditoría. Los controles de escritura quedan **deshabilitados** salvo que exista un canal PWM `Verified`.
- **Diagnóstico FanSpeed (v0.9, CLI solo lectura):** `--fan-backends`, `--fan-capabilities`, `--fan-read` y `--fan-recovery-status`.
- **Analizador térmico (v0.8):** comando `--analyze` (solo lectura de archivos) que parsea los CSV de FanForge y las exportaciones de ProArt, calcula estadísticas (min/max/media/p90/p95), correlación temperatura–RPM (con aviso de no causalidad) y por perfil, y genera `thermal-report.html` (gráficas SVG) y `thermal-stats.csv`. Sin acceso a hardware.
- **Base MSIX (v0.8):** manifiesto, logos generados por script, verificación estructural y empaquetado reproducible (`scripts/empaquetar-msix.ps1`). Un MSIX sin firmar **no** se instala; la firma es un paso manual autorizado. Ver [docs/MSIX.md](docs/MSIX.md).
- **Temperatura de zona ACPI** (v0.4): proveedor suplementario vía WMI `MSAcpi_ThermalZoneTemperature` (aproximada, requiere elevación).
- **Diagnóstico de capacidades** multibackend: comando `--hardware-diagnose` (solo lectura, vuelca valores RAW) y UI que habilita cada función según lo que el hardware realmente soporta.
- **Historial y mini-gráficas** de temperatura CPU y RPM en el dashboard (búfer acotado, autoescala).
- **Accesibilidad:** escalado DPI `PerMonitorV2` (125–200 %), atajos `Esc` (volver a BIOS/ASUS) y `F5` (refrescar), y nombres accesibles.
- **Seguridad v0.6:** comprobación de cadena local y firma Authenticode con WinVerifyTrust antes de cargar la DLL WinIO (la revocación en línea no se consulta); CN ASUS reconocido y versión compatible obligatorios. Rechazo de escrituras fuera del modelo M3500QC y handles nativos ACPI independientes.
- Pruebas de reglas de negocio y de la máquina de estados con **controlador simulado (mock)**, pipeline Windows y scripts de diagnóstico.
- Evita valores PWM bajos en el prototipo para no reducir de forma peligrosa la refrigeración.

## Distribución firmada para Smart App Control (opcional)

La CI normal genera software de pruebas y no lo presenta como firmado. Para distribuir
un EXE o instalador MSIX aceptado por Smart App Control, el proyecto incluye
`.github/workflows/signed-release.yml`, un flujo **manual**, restringido a
`main`, que usa Microsoft Artifact Signing con autenticación OIDC (sin PFX ni
claves privadas en GitHub).

**No hay firma automática todavía:** necesitas un certificado público autorizado,
una cuenta Artifact Signing compatible y configurar el environment
`fanforge-signing` con credenciales federadas. Después, el workflow firma el
EXE y MSIX, verifica ambas firmas y entrega artefactos en GitHub Actions.
No se permite subir paquetes sin firmar bajo un nombre de distribución confiable.
Para identidades no elegibles para Artifact Signing (por ejemplo, desarrolladores
individuales en México), también existe firma local con certificado OV de CA
mediante `scripts/firmar-distribucion-local.ps1`.
Pasos completos: [Firma en Windows](docs/WINDOWS-SIGNING.md) y
[Empaquetado MSIX](docs/MSIX.md).

## Instalar dependencias y compilar

1. Instala los controladores y servicios oficiales de ASUS mediante MyASUS, y ejecuta Windows 11 x64.
2. Instala **.NET 8 SDK**:

   ```powershell
   winget install Microsoft.DotNet.SDK.8
   ```

3. Clona este repositorio en PowerShell:

   ```powershell
   git clone https://github.com/ErickRFM/fan.git
   cd fan
   ```

4. Ejecuta el diagnóstico informativo **sin escritura al hardware**:

   ```powershell
   .\scripts\diagnostico.ps1
   ```

5. Prueba las reglas y compila:

   ```powershell
   .\scripts\compilar.ps1
   ```

6. Inicia el ejecutable de previsualización como administrador:

   ```powershell
   .\dist\FanForge.exe
   ```

   La compilación es autónoma (`win-x64`, single-file), pero **no contiene** la DLL propietaria de ASUS. La aplicación buscará la instalación OEM local.

## Sonda de solo lectura

Con una consola **elevada**, sin activar el control manual:

```powershell
.\dist\FanForge.exe --probe
```

Imprime modelo, versión BIOS, ventiladores detectados, RPM y temperatura de CPU. No escribe PWM.

## Diagnóstico multibackend (solo lectura)

Con una consola **elevada**, sin escribir nada:

```powershell
.\dist\FanForge.exe --hardware-diagnose
```

Proporciona el estado de cada proveedor (ATKACPI y zona térmica ACPI), RPM de CPU/GPU,
temperatura, capacidades reales (`perfiles`, `curvas`, `pwm`) y el volcado de valores RAW de
los métodos consultados. Para cambiar de perfil (escritura verificada por lectura posterior):

```powershell
.\dist\FanForge.exe --set-profile Standard
```

Perfiles válidos: `Whisper`, `Standard`, `Performance`. `Auto` y `Max` no son escrituras válidas de ATKACPI; `Performance` no fija PWM al 100%. Solo debe usarse cuando
la persona esté presente y ningún otro programa controle los ventiladores. Antes del primer cambio se guarda el perfil original y `--restore` lo devuelve.

## Registro CSV de telemetría (solo lectura)

```powershell
.\dist\FanForge.exe --log-telemetry 120
```

Muestrea RPM CPU/GPU, temperatura, perfil activo y hora a ~1 Hz durante los segundos indicados
(1–600) y escribe un CSV en `%LocalAppData%\FanForge\logs` (o en la carpeta indicada como segundo
argumento). No escribe PWM. Procedimiento de comparación con ProArt en
[docs/THERMAL-COMPARISON.md](docs/THERMAL-COMPARISON.md).

## Analizador térmico de CSV (solo lectura de archivos)

```powershell
.\dist\FanForge.exe --analyze "%LocalAppData%\FanForge\logs" --out .
```

Parsea los CSV indicados (un archivo o una carpeta; `--proart <csv>` para exportaciones de ProArt
Creator Hub) y escribe `thermal-report.html` (gráficas SVG y estadísticas por perfil) y
`thermal-stats.csv`. **No toca hardware.** El informe indica la correlación temperatura–RPM como
asociación, no causalidad.

## Diagnóstico de driver ASUS (solo lectura)

```powershell
.\dist\FanForge.exe --diagnose-driver
```

Informa el estado de `AsusSAIO`, `AsusOptimization`, `AsIO3`/`AsIO2` y dispositivos ASUS/ATK vía WMI,
sin iniciar, detener ni reconfigurar nada. Detalles en [docs/OEM-INTERFACES.md](docs/OEM-INTERFACES.md).

## Diagnóstico FanSpeed (solo lectura)

```powershell
.\dist\FanForge.exe --fan-backends
.\dist\FanForge.exe --fan-capabilities
.\dist\FanForge.exe --fan-read
.\dist\FanForge.exe --fan-recovery-status
```

`--fan-backends` enumera cada backend y sus capacidades; `--fan-capabilities` resume si existe un canal
PWM **verificado**; `--fan-read` imprime una lectura por backend; `--fan-recovery-status` muestra el
registro de recuperación guardado y el perfil ASUS actual. **Ninguno escribe PWM ni habilita control
manual.** En el M3500QC ningún backend reporta PWM verificado, por lo que el motor permanece en
`AvailableUnverified` y el control manual sigue bloqueado. Protocolo de validación física en
[docs/FANSPEED-PHYSICAL-TESTS.md](docs/FANSPEED-PHYSICAL-TESTS.md).

## Prueba supervisada

Antes de escribir PWM consulta [docs/HARDWARE-TEST.md](docs/HARDWARE-TEST.md). Registra RPM en AUTO, prueba 80% durante ~10 segundos y luego 100% durante ~10 segundos. Verifica la lectura REAL de RPM. Usa **VOLVER A BIOS** inmediatamente ante resultados incoherentes; detén la prueba ante ruido mecánico anormal, temperaturas de riesgo o ventilador detenido.

Para intentar recuperar el control automático desde PowerShell elevado, si la interfaz no abre:

```powershell
.\scripts\restaurar.ps1
```

`--restore` recupera el **perfil ASUS original** guardado antes del primer cambio (y, cuando
HealthyTable responde, también el PWM a BIOS). El watchdog es **best effort**, no una medida de seguridad certificada: no puede proteger de cortes de energía, drivers colgados, firmware que ignore órdenes o cierre de todos los procesos.

## Arquitectura

| Ruta | Responsabilidad |
|---|---|
| `FanForge.Core/FanPolicy.cs` | Política de modelo, porcentaje, duty, confirmación, decodificación ATKACPI y selección de DLL |
| `FanForge.Core/FanSession.cs` | Máquina de estados manual (reaplicar, confirmar, restaurar), testeable |
| `FanForge.Core/Abstractions.cs` | `IFanBackend`, `FanReading`, eventos de control |
| `FanForge.Core/Multibackend.cs` | `IFanTelemetryProvider`, `IFanControlBackend`, `FanCapabilities`, `TelemetrySnapshot`, `TelemetryMerge` |
| `FanForge.Core/TelemetryHistory.cs` | Búfer circular acotado de muestras para gráficas |
| `FanForge.Core/ControlConflicts.cs` | Detección de herramientas ASUS que compiten por el control |
| `FanForge.Core/OemLibraryPolicy.cs` | Validación (versión/firmante) de la DLL OEM |
| `FanForge.Core/WatchdogPolicy.cs` | Estado bidireccional del watchdog y restauración segura |
| `FanForge.Win/AsusNative.cs` | Interop de exports ASUS, carga de DLL OEM |
| `FanForge.Win/AsusHardware.cs` | Lectura, escritura y restauración serializadas (I/O nativo) |
| `FanForge.Win/AsusAcpiNative.cs` | Interop ATKACPI (`DeviceGet`/`DeviceSet`, métodos y constantes) |
| `FanForge.Win/AsusAcpiTelemetry.cs` | Proveedor de RPM ATKACPI (solo lectura) |
| `FanForge.Win/AsusAcpiControlBackend.cs` | Cambio de perfil ASUS verificado por lectura posterior |
| `FanForge.Win/AsusAcpiProfileChannel.cs` | Canal ATKACPI del registro de perfil (lectura/escritura confirmada) |
| `FanForge.Win/FileProfileStore.cs` | Persistencia del perfil original en archivo local |
| `FanForge.Core/ProfileGuard.cs` | Captura/restauración del perfil ASUS original y registro |
| `FanForge.Core/TelemetryCsv.cs` | Formato CSV (perfil confirmado vs. RPM observado) |
| `FanForge.Core/ThermalAnalysis.cs` | Tipos del analizador (`TelemetrySample`, `Stat`, `DataQuality`) |
| `FanForge.Core/ThermalAnalyzer.cs` | Parseo de CSV y estadísticas/correlación por perfil |
| `FanForge.Core/ThermalReport.cs` | Informe HTML (SVG) y CSV de estadísticas |
| `FanForge.Core/MsixPackaging.cs` | Validación del manifiesto MSIX (política) |
| `FanForge.Core/FanSpeed.cs` | Contratos FanSpeed (estados, capacidades, `IFanSpeedBackend`) |
| `FanForge.Core/FanSpeedEngine.cs` | Motor multibackend con canal serializado y verificación |
| `FanForge.Core/AsusWmiCodec.cs` | Decodificación de valores WMI/ACPI con bit de validez |
| `FanForge.Core/SimulatedFanSpeedBackend.cs` | Backend simulado (pruebas sin hardware) |
| `FanForge.Win/FanSpeedBackends.cs` | Backends Win: ATKACPI, WMI, HealthyTable (gated), PawnIO (stub) |
| `FanForge.Win/TelemetryRecorder.cs` | Escritura del CSV de telemetría |
| `FanForge.Win/DriverDiagnostics.cs` | Estado de servicios/drivers ASUS vía WMI (solo lectura) |
| `FanForge.Win/AcpiThermalZoneTelemetry.cs` | Temperatura de zona ACPI (WMI, aproximada) |
| `FanForge.Win/FanBackendManager.cs` | Selección/combinación de proveedores y capacidades |
| `FanForge.Win/Watchdog.cs` | Recuperación al terminar el proceso padre |
| `FanForge.Win/Sparkline.cs` | Mini-gráfica de línea con autoescala |
| `FanForge.Win/FanToolConflictDetector.cs` | Enumera procesos y avisa de MyASUS/Armoury Crate/G-Helper activos |
| `FanForge.Win/OemLibraryVerifier.cs` | Lee versión y firmante Authenticode de AsusWinIO64.dll |
| `FanForge.Win/MainForm.cs` | Dashboard WinForms con reconocimiento de capacidades |
| `FanForge.Tests/` | Pruebas de política, sesión, motor FanSpeed y códec con backends falsos |
| `scripts/` | Diagnóstico, compilación y restauración |
| `docs/` | Plan y protocolo de pruebas |

## Próximas fases

Consulta [ROADMAP](docs/ROADMAP.md): primero validación física en el M3500QC real y por último endurecimiento del control y UX avanzada. Para distribuir un `.exe` aceptado por Windows 11 (Smart App Control/WDAC) sin desactivar seguridad, ver [docs/WINDOWS-SIGNING.md](docs/WINDOWS-SIGNING.md).

## Documentación adicional

- [docs/ROADMAP.md](docs/ROADMAP.md) — hoja de ruta.
- [docs/FANSPEED-RESEARCH.md](docs/FANSPEED-RESEARCH.md) — interfaces y matriz de compatibilidad del motor FanSpeed.
- [docs/FANSPEED-PHYSICAL-TESTS.md](docs/FANSPEED-PHYSICAL-TESTS.md) — protocolo de validación física presencial.
- [docs/MSIX.md](docs/MSIX.md) — empaquetado MSIX y firma.
- [docs/RECOVERY.md](docs/RECOVERY.md) — recuperación y auditoría de seguridad.
- [docs/PWM-RESEARCH.md](docs/PWM-RESEARCH.md) — por qué el PWM manual está bloqueado en este modelo.
- [docs/WINDOWS-SIGNING.md](docs/WINDOWS-SIGNING.md) — firma para SAC/WDAC.
- [docs/OEM-INTERFACES.md](docs/OEM-INTERFACES.md) — interfaces ASUS investigadas.
- [docs/THERMAL-COMPARISON.md](docs/THERMAL-COMPARISON.md) — comparación térmica con ProArt.

## Créditos, licencia y avisos

Proyecto independiente bajo licencia MIT. Los símbolos y orden de comandos de la DLL se contrastan con [Karmel0x/AsusFanControl](https://github.com/Karmel0x/AsusFanControl) (MIT). ASUS y `AsusWinIO64.dll` son propiedad de sus respectivos titulares. No se distribuye la DLL ni un controlador de kernel alternativo. No afiliado a ASUS.
