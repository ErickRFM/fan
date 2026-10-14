# FanForge — control experimental de ventiladores ASUS

**Aplicación de escritorio para Windows 11 x64, enfocada inicialmente en el ASUS VivoBook Pro 15 OLED M3500QC.** Implementada en C#/.NET 8 con Windows Forms, acceso a APIs de ASUS System Analysis, lectura de RPM, botones 80%/100%, controles individuales y restauración a modo BIOS.

> **Estado: prototipo en validación (v0.4).** La compilación y las pruebas automatizadas (simuladas) pasan. En el M3500QC físico, la API HealthyTable/`AsusWinIO64.dll` sigue devolviendo `-1` porque el driver de kernel `AsusSAIO` está `Degraded`/`Stop Pending` (regresión conocida del driver ASUS). Para no depender de ella, FanForge v0.4 añade un **backend ATKACPI** que **lee RPM reales de CPU y GPU** y permite **cambiar el perfil ASUS** (verificado), y un backend de **zona térmica ACPI** para temperatura aproximada. El control PWM manual se habilita únicamente si HealthyTable responde. Ver [docs/ASUS-BACKENDS.md](docs/ASUS-BACKENDS.md) y [docs/HARDWARE-TEST.md](docs/HARDWARE-TEST.md). Si aparece `100%` significa **PWM solicitado**, no ventilador a 100% comprobado. No se deben desactivar protecciones térmicas ni instalar drivers no oficiales.

## Funcionalidades

- Rechaza escritura fuera del identificador de modelo `M3500QC` (guardas de política y de backend).
- Localiza la biblioteca ASUS original en `Windows\System32\DriverStore\FileRepository\asussci*.inf_amd64_*\ASUSSystemAnalysis\AsusWinIO64.dll` y **elige determinísticamente la versión más reciente** (las carpetas obsoletas del DriverStore ya no pueden ganar por orden de enumeración).
- Lee conteo de ventiladores y RPM con `HealthyTable_FanCounts()` y `HealthyTable_FanRPM()`.
- Escribe PWM 70–100% mediante `HealthyTable_SetFanTestMode(1)` y `HealthyTable_SetFanPwmDuty()`.
- Supervisa la sesión manual con `FanSession` (núcleo independiente del hardware): reaplica PWM, detecta respuesta real de RPM y **restaura preventivamente a BIOS** si un ventilador deja de girar.
- Interfaz que nunca confunde PWM solicitado con RPM medidas; muestra estado ASUS, modo, watchdog, CPU y registro de eventos; el trabajo de hardware ocurre fuera del hilo de UI.
- Botón **VOLVER A BIOS**, comando `--restore`, restauración al cerrar y observador en proceso separado (`--watchdog`).
- Comando **`--probe`** de solo lectura para leer modelo, BIOS, ventiladores, RPM y CPU sin activar el control manual.
- **Backend ATKACPI** (v0.4): lee **RPM reales de CPU/GPU** (`0x00110013`/`0x00110014`, unidad = 100 RPM) aunque HealthyTable falle, y ofrece **perfiles ASUS** (Silencioso/Estándar/Rendimiento/Máximo) con verificación por lectura posterior.
- **Temperatura de zona ACPI** (v0.4): proveedor suplementario vía WMI `MSAcpi_ThermalZoneTemperature` (aproximada, requiere elevación).
- **Diagnóstico de capacidades** multibackend: comando `--hardware-diagnose` (solo lectura, vuelca valores RAW) y UI que habilita cada función según lo que el hardware realmente soporta.
- **Historial y mini-gráficas** de temperatura CPU y RPM en el dashboard (búfer acotado, autoescala).
- Pruebas de reglas de negocio y de la máquina de estados con **controlador simulado (mock)**, pipeline Windows y scripts de diagnóstico.
- Evita valores PWM bajos en el prototipo para no reducir de forma peligrosa la refrigeración.

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

Perfiles válidos: `Auto`, `Whisper`, `Standard`, `Performance`, `Max`. Solo debe usarse cuando
la persona esté presente y ningún otro programa controle los ventiladores.

## Prueba supervisada

Antes de escribir PWM consulta [docs/HARDWARE-TEST.md](docs/HARDWARE-TEST.md). Registra RPM en AUTO, prueba 80% durante ~10 segundos y luego 100% durante ~10 segundos. Verifica la lectura REAL de RPM. Usa **VOLVER A BIOS** inmediatamente ante resultados incoherentes; detén la prueba ante ruido mecánico anormal, temperaturas de riesgo o ventilador detenido.

Para intentar recuperar el control automático desde PowerShell elevado, si la interfaz no abre:

```powershell
.\scripts\restaurar.ps1
```

El watchdog es **best effort**, no una medida de seguridad certificada: no puede proteger de cortes de energía, drivers colgados, firmware que ignore órdenes o cierre de todos los procesos.

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
| `FanForge.Win/AsusNative.cs` | Interop de exports ASUS, carga de DLL OEM |
| `FanForge.Win/AsusHardware.cs` | Lectura, escritura y restauración serializadas (I/O nativo) |
| `FanForge.Win/AsusAcpiNative.cs` | Interop ATKACPI (`DeviceGet`/`DeviceSet`, métodos y constantes) |
| `FanForge.Win/AsusAcpiTelemetry.cs` | Proveedor de RPM ATKACPI (solo lectura) |
| `FanForge.Win/AsusAcpiControlBackend.cs` | Cambio de perfil ASUS verificado por lectura posterior |
| `FanForge.Win/AcpiThermalZoneTelemetry.cs` | Temperatura de zona ACPI (WMI, aproximada) |
| `FanForge.Win/FanBackendManager.cs` | Selección/combinación de proveedores y capacidades |
| `FanForge.Win/Watchdog.cs` | Recuperación al terminar el proceso padre |
| `FanForge.Win/Sparkline.cs` | Mini-gráfica de línea con autoescala |
| `FanForge.Win/FanToolConflictDetector.cs` | Enumera procesos y avisa de MyASUS/Armoury Crate/G-Helper activos |
| `FanForge.Win/OemLibraryVerifier.cs` | Lee versión y firmante Authenticode de AsusWinIO64.dll |
| `FanForge.Win/MainForm.cs` | Dashboard WinForms con reconocimiento de capacidades |
| `FanForge.Tests/` | Pruebas de política y de sesión con backend falso |
| `scripts/` | Diagnóstico, compilación y restauración |
| `docs/` | Plan y protocolo de pruebas |

## Próximas fases

Consulta [ROADMAP](docs/ROADMAP.md): primero validación física en el M3500QC real y por último endurecimiento del control y UX avanzada.

## Créditos, licencia y avisos

Proyecto independiente bajo licencia MIT. Los símbolos y orden de comandos de la DLL se contrastan con [Karmel0x/AsusFanControl](https://github.com/Karmel0x/AsusFanControl) (MIT). ASUS y `AsusWinIO64.dll` son propiedad de sus respectivos titulares. No se distribuye la DLL ni un controlador de kernel alternativo. No afiliado a ASUS.
