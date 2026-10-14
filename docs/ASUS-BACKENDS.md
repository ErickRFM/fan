# Backends ASUS de FanForge (v0.4)

FanForge separa **telemetría** (lectura) de **control** (escritura). Cada backend declara sus
capacidades reales; nunca se presenta una lectura como capacidad de escritura.

## Panorama

| Backend | Interfaz | Rol | Estado en M3500QC |
|---|---|---|---|
| HealthyTable / WinIO | `AsusWinIO64.dll` (ASUS System Analysis) | Control PWM 70–100% + lectura | **Bloqueado**: driver `AsusSAIO` en `Stop Pending`/`Degraded`; devuelve `-1` |
| ATKACPI | `\\.\ATKACPI` (IOCTL `0x0022240C`, métodos `DSTS`/`DEVS`) | Telemetría RPM + cambio de perfil | **Operativo (lectura + perfiles)** |
| Zona térmica ACPI | WMI `root\WMI:MSAcpi_ThermalZoneTemperature` | Temperatura de zona (aproximada) | **Operativo** (requiere elevación) |

## ATKACPI — cómo funciona

- Dispositivo: `\\.\ATKACPI` (`CreateFile` con `GENERIC_READ|GENERIC_WRITE`).
- Llamada: `DeviceIoControl(handle, 0x0022240C, buffer, ...)`.
- Buffer de entrada: `[uint method][uint argLen][args...]`.
  - `method = DSTS (0x53545344)` → **leer** (`DeviceGet`).
  - `method = DEVS (0x53564544)` → **escribir** (`DeviceSet`).
- `DeviceGet` devuelve `Int32(outBuffer) - 65536`. Un método no soportado devuelve `-65536`
  (es decir, `outBuffer` = `0x00000000`).

### Métodos usados (referencia comparable a G-Helper / AsusACPI)

| Constante | ID | Descripción | Resultado en M3500QC |
|---|---|---|---|
| `CPU_Fan` | `0x00110013` | RPM CPU (unidad = 100 RPM) | **44–61 → 4400–6100 RPM** |
| `GPU_Fan` | `0x00110014` | RPM GPU (unidad = 100 RPM) | **38–40 → 3800–4000 RPM** |
| `VivoBookMode` | `0x00110019` | Modo de rendimiento VivoBook (perfiles) | `131074` (`0x00020002`) → **soportado** |
| `PerformanceMode` | `0x00120075` | Modo ROG | `-65536` → no soportado |
| `Temp_CPU` | `0x00120094` | Temperatura CPU ACPI | `-65536` → no soportado |
| `Temp_GPU` | `0x00120097` | Temperatura GPU ACPI | `-65536` → no soportado |
| `DevsCPUFanCurve` | `0x00110024` | Curva de ventilador CPU | `-65536` → no soportado |
| `DevsGPUFanCurve` | `0x00110025` | Curva de ventilador GPU | `-65536` → no soportado |

### Decodificación de RPM

`raw` se interpreta con la misma regla que G-Helper:

```
valor = raw & 0xFFFF
si valor > 120  ó  (valor == 0 y raw < 0) → inválido (-1)
si no                                       → valor * 100 RPM
```

Implementado y probado en `FanPolicy.AcpiFanRpm`. La comparación con ProArt Creator Hub es
la validación externa recomendada (debe coincidir el orden de magnitud y la tendencia).

### Temperaturas

ATKACPI no expone temperatura en este modelo (`0x00120094`/`0x00120097` = `-65536`).
El proveedor suplementario `AcpiThermalZoneTelemetry` lee `MSAcpi_ThermalZoneTemperature`
(ACPI `THRM_0`), que da una temperatura **de zona aproximada** (p. ej. 90 °C). No es la
temperatura de núcleo exacta; se etiqueta como tal.

### Control por perfiles

`AsusAcpiControlBackend` escribe `VivoBookMode` mediante `DeviceSet` y **verifica** leyendo
de nuevo. La codificación de Turbo/Silencio está invertida respecto a las bases internas
(`FanPolicy.VivoBookWireMode` intercambia 1↔2).

| Perfil solicitado | Base | Valor de escritura |
|---|---|---|
| Silencioso (Whisper) | 2 | 1 |
| Estándar | 0 | 0 |
| Rendimiento (Performance) | 1 | 2 |
| Máximo | 1 | 2 |

`RestoreAutomaticAsync` escribe Estándar (base 0). Las curvas personalizadas y el PWM directo
**no** se ofrecen porque el firmware no los expone en este modelo.

## Selección y recuperación de proveedores

`FanBackendManager` combina proveedores: los RPM provienen del primer proveedor que los
entregue y las temperaturas de cualquiera que las aporte. La UI solo habilita el control PWM
si HealthyTable está disponible; en caso contrario muestra telemetría ACPI de solo lectura y
los botones de perfil.

## Garantías de seguridad

- Solo lectura por defecto; las escrituras se validan por lectura posterior.
- No se escriben registros EC arbitrarios ni se modifican drivers, firmware o BIOS.
- No se declara éxito físico sin medición observada.
