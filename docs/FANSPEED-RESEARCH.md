# FanSpeed Engine — investigación (v0.9, P11)

Investigación de las vías reales de control/telemetría de ventiladores ASUS para el motor
**FanSpeedEngine** de FanForge v0.9. Fuentes: código de G-Helper (GPL-3.0) leído como referencia de
**interfaces y endpoints** (sin copiar código), conocimiento del driver **PawnIO**, y las pruebas ya
realizadas en el M3500QC recogidas en `docs/HARDWARE-TEST.md`.

> Aviso de licencia: G-Helper es **GPL-3.0**. FanForge es MIT. No se copia código de G-Helper; solo
> se documentan hechos de interfaz (números de método, tamaño de estructuras, semántica de bits) que
> se reimplementan de forma independiente contra las mismas APIs públicas de ASUS.

## 1. Fuentes analizadas

| Fuente | Qué aporta |
| --- | --- |
| `seerge/g-helper` → `app/AsusACPI.cs` | `DSTS`/`DEVS` por `\\.\ATKACPI` (IOCTL `0x0022240C`), endpoints, decodificación de RPM, curvas, perfiles, offset `-65536` |
| `seerge/g-helper` → `app/Fans.cs`, `app/Fan/FanSensorControl.cs` | Uso de curvas por punto (16 bytes), escritura con `SetFanCurve`, lectura con `GetFanCurve` |
| `seerge/g-helper` → `app/Pawn/*` (`PawnIO`, `RyzenSmu.cs`, `IntelMSR.bin`) | PawnIO se usa para **MSR/SMU de CPU** (undervolt), **no** para PWM de ventiladores |
| `seerge/g-helper-experimental-release` | No aporta un protocolo PWM distinto; mismas interfaces ACPI/WMI |
| Pruebas propias M3500QC (`docs/HARDWARE-TEST.md`) | Perfiles verificados; `HealthyTable_FanCounts() = -1`; endpoints de curva `-65536` |

## 2. Interfaces identificadas

### 2.1 ATKACPI (canal principal de ASUS en Windows)

- Dispositivo `\\.\ATKACPI`, `DeviceIoControl` con `CONTROL_CODE = 0x0022240C`.
- Métodos: `DSTS` (`0x53545344`, get), `DEVS` (`0x53564544`, set), `INIT` (`0x54494E49`), `WDOG`
  (`0x474F4457`).
- Trama: `[método u32][nArgs u32][args...]`; salida de 16 bytes; el valor get se obtiene como
  `ToInt32(out) - 65536` (equivalente a `raw & 0xFFFF` tras restar el flag `0x10000`).

Endpoints relevantes:

| Endpoint | Método | Estado en M3500QC |
| --- | --- | --- |
| `CPU_Fan 0x00110013` | `DSTS` | **leído** (RPM×100) |
| `GPU_Fan 0x00110014` | `DSTS` | **leído** (RPM×100) |
| `VivoBookMode 0x00110019` | `DSTS`/`DEVS` | **leído y escrito** (perfiles, verificado por read-back) |
| `DevsCPUFan 0x00110022` / `DevsGPUFan 0x00110023` | `DEVS` | rango min/max (2 bytes) — **no confirmado** en este modelo |
| `DevsCPUFanCurve 0x00110024` / `DevsGPUFanCurve 0x00110025` | `DSTS`/`DEVS` | **rechazado** (`-65536`) en este modelo |
| `PerformanceMode 0x00120075` | `DSTS`/`DEVS` | **no soportado** |

### 2.2 Curvas (según la interfaz observada en G-Helper)

- Curva = **16 bytes**: 8 puntos de temperatura (`curve[0..7]`, ºC crecientes) + 8 valores de duty
  (`curve[8..15]`, 0–100 %). Se rechaza si es todo ceros o longitud ≠ 16.
- `SetFanRange`: `min = curve[8]*255/100`, `max = curve[15]*255/100` → 2 bytes a `DevsCPUFan/GPUFan`.
- `SetFanCurve`: normaliza el duty a 0–100 y lo escala; escribe a `Devs*FanCurve`.
- `GetFanCurve(device, mode)`: lee la curva del modo (con intercambio 1↔2 propio de ASUS).

Esto es **control por curva térmica**, no un PWM fijo; aun así requiere que los endpoints de curva
respondan. En el M3500QC respondieron `-65536`, por lo que **no están soportados**.

### 2.3 WMI (`root\wmi`)

- Clase `AsusAtkWmi_WMNB`; G-Helper la usa para el canal WMI de `DEVS` y para suscribirse a
  `AsusAtkWmiEvent`. La misma familia ACPI expone `DSTS` por WMI.
- Semántica de estado: el valor viene con un **bit de validez** `0x10000`. Patrón observado en el
  escaneo de rangos de G-Helper: `if ((raw & 0x10000) == 0) continue; value = raw - 0x10000;`.
- Ejemplos del M3500QC: `0x0001002D → 45 → 4500 RPM`, `0x00010032 → 50 → 5000 RPM`,
  `0x00010024 → 36 → 3600 RPM`, `0x00010026 → 38 → 3800 RPM`; `0xFFFFFFFE` → **inválido**
  (bit `0x10000` a 0).

### 2.4 HealthyTable / `AsusWinIO64.dll`

- `HealthyTable_FanCounts()` = **-1** y `HealthyTable_SetFanTestMode`/`SetFanPwmDuty` no operativos
  en este modelo/BIOS (ver `docs/HARDWARE-TEST.md`, QA). Es la única vía histórica de PWM fijo y está
  **bloqueada por firmware**.

### 2.5 PawnIO

- **PawnIO** es un driver de kernel **firmado** (de namazso) que ejecuta módulos pequeños en un
  intérprete sandbox para acceder a MSR/IO/PCI. G-Helper lo usa para **Ryzen SMU** (undervolt/curve
  optimizer) e **Intel MSR**, **no** para ventiladores.
- Ventajas: firmado, sin desactivar HVCI/SAC. Limitaciones: no existe ningún módulo oficial que
  controle el ventilador del M3500QC; escribir un módulo propio para tocar el EC sería equivalente de
  riesgo a un driver no oficial y **queda prohibido** en este proyecto.
- Conclusión: PawnIO se expone como **backend opcional, deshabilitado por defecto** y sin ninguna
  operación de escritura; solo sirve como punto de extensión futuro si aparece soporte verificado.

## 3. Matriz de compatibilidad (M3500QC, BIOS `M3500QC.316`)

| Operación | Vía | Detectado | Verificado físicamente | Decisión v0.9 |
| --- | --- | --- | --- | --- |
| Leer RPM CPU/GPU | ATKACPI `DSTS` | sí | sí | **Activar** (telemetría) |
| Leer RPM CPU/GPU | WMI `AsusAtkWmi_WMNB` `DSTS` | sí (clase) | **no** | Activar con validación de centinelas; marcar `Unverified` |
| Cambiar perfil ASUS | ATKACPI `DEVS` `VivoBookMode` | sí | sí | **Activar** (con `ProfileGuard`) |
| Curva CPU/GPU | ATKACPI `DEVS` `Devs*FanCurve` | rechazado `-65536` | no | **Bloquear** (`Unsupported`) |
| Rango min/max | ATKACPI `DEVS` `Devs*Fan` | no confirmado | no | **Bloquear** (`Unsupported`) |
| PWM fijo | HealthyTable | `FanCounts=-1` | no | **Bloquear** (`Unsupported`) |
| PWM fijo | PawnIO | sin módulo | no | **Bloquear/deshabilitado** |
| Temperatura CPU | WMI `MSAcpi_ThermalZoneTemperature` | sí | sí | **Activar** (aproximada) |

## 4. Riesgos y restricciones

- **Bloqueo por firmware**: los endpoints de escritura de ventilador del M3500QC no aceptan
  operaciones; forzar métodos desconocidos podría colgar el handle o el servicio ASUS.
- **Causalidad de RPM**: un cambio de RPM tras una orden no prueba que la orden lo causó (temperatura,
  carga, histéresis). La confirmación debe comparar RPM antes/después bajo condiciones controladas y
  aun así registrarse como *asociación*.
- **Concurrencia**: no emitir comandos CPU y GPU a la vez; un único canal de escritura serializado.
- **Suspensión/reanudación y crash**: el canal de restauración (perfil) debe persistir y recuperarse.
- **WMI/CIM**: la clase puede no existir o lanzar; hay que tolerar excepciones y no mostrar RPM falsas.
- **Licencia**: no copiar código GPL-3.0; reimplementación independiente de hechos de interfaz.

## 5. Recomendación técnica

1. Implementar un **FanSpeedEngine** multibackend que **separe** detección / verificación / solicitud /
  respuesta / RPM medidas, y que devuelva `Unsupported` (nunca éxito) cuando el PWM no esté soportado.
- **Telemetría** por **WMI** (con codec de bits de estado) y por **ATKACPI**, fusionadas y validadas.
- **Control** habilitado **solo** para operaciones verificadas: en el M3500QC, **perfiles ASUS**. El
  PWM fijo queda `Unsupported` en todos los backends reales.
- **Backend simulado** para pruebas sin hardware.
- **PawnIO** como adaptador opcional deshabilitado.
- Mantener la guarda de perfil original, el watchdog y la persistencia de recuperación.

## 5.1 Inventario de métodos probados

- `FanPolicy.AcpiFanRpm` / `AcpiTemperature` (ATKACPI) — probados.
- `ProfileGuard` sobre `VivoBookMode` — probado físicamente.
- Nuevo `AsusWmiCodec` (WMI `0x10000`-flag) — probado con los RAW del M3500QC (unidad).
- Endpoints de curva ATKACPI — probados como **no soportados** (`-65536`).

## 5.2 Recomendación final

Mantener el **PWM fijo deshabilitado** hasta disponer de un endpoint de escritura soportado y validado
físicamente con procedimiento de restauración comprobable. El motor debe existir, detectar, medir y
**condicionar** el control; el botón de 100 % solo se habilitará cuando haya un canal compatible y
verificado (hoy: ninguno).
