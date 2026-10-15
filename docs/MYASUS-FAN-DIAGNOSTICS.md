# Diagnóstico del ventilador estilo MyASUS (v1.0, F3)

Este documento describe la vía que usa **MyASUS** para su "Diagnóstico de ventilador" ("fan testing"),
cómo se relaciona con el control manual experimental de G-Helper, y cómo FanForge la **detecta y
diagnostica sin escribir nada** en el equipo.

> Fuente: el mantenedor de G-Helper describe su control manual como *"same method as MyASUS does when
> runs your fan testing"* (discussion #2272). El código del build experimental **no está publicado**;
> la descripción se toma como comportamiento observado, no como especificación.

## 1. Qué usa MyASUS

MyASUS delega en la biblioteca OEM **`AsusWinIO64.dll`** (instalada con el "ASUS System Control
Interface"). Las funciones relevantes del OEM son:

| Función | Rol |
| --- | --- |
| `InitializeWinIo()` / `ShutdownWinIo()` | Abre/cierra el acceso al EC a través del driver `AsusSAIO` |
| `HealthyTable_FanCounts()` | Número de ventiladores controlables |
| `HealthyTable_FanRPM()` | RPM por ventilador |
| `HealthyTable_SetFanIndex()` | Selecciona el ventilador objetivo |
| `HealthyTable_SetFanTestMode()` | Entra/sale del modo de prueba (PWM manual) |
| `HealthyTable_SetFanPwmDuty()` | Fija el ciclo de trabajo (PWM) del ventilador seleccionado |
| `Thermal_Read_Cpu_Temperature()` | Temperatura de CPU |

El "fan testing" de MyASUS pone el EC en **modo de prueba**, selecciona cada ventilador y le aplica un
PWM; al terminar, sale del modo de prueba y el EC vuelve al control automático del BIOS. Es un control
**directo y con estado** (mientras dura), igual que el experimental de G-Helper.

## 2. Estado de esta vía en el M3500QC

La ruta OEM está **bloqueada por firmware** en este equipo. La sonda de solo lectura de FanForge
(`--probe`) reportó (2026-10-14/15, elevado):

```
InitializeWinIo()              => 0
HealthyTable_FanCounts()       => -1
HealthyTable_FanRPM()          => -1
Thermal_Read_Cpu_Temperature() => 2147483647 (0x7FFFFFFF)
```

`FanCounts() = -1` significa que el firmware **no expone ventiladores controlables** por esa vía; sin un
conteo válido no se puede seleccionar ventilador ni escribir PWM. Es la misma rasquiña que reporta la
comunidad tras ciertas actualizaciones del "ASUS System Control Interface" (ver
[Karmel0x/AsusFanControl #44](https://github.com/Karmel0x/AsusFanControl/issues/44)): incluso el
"Diagnóstico de ventilador" de MyASUS deja de funcionar cuando la API OEM se rompe a nivel de driver.

> Nota: el 2026-10-15 `AsusSAIO` figuraba `Running/OK` y el dispositivo «ASUS System Control Interface
> v3» sin error, y aun así `FanCounts()` siguió devolviendo `-1`. Por tanto el bloqueo puede ser **por
> diseño de firmware** y no solo por un driver degradado.

## 3. Cómo diagnostica FanForge (solo lectura)

FanForge **nunca** entra en modo de prueba ni escribe PWM OEM. Se limita a comprobar si la vía existe:

```powershell
# Requiere consola elevada. Solo lectura.
.\dist\FanForge.exe --probe               # FanCounts / FanRPM / temperatura CPU
.\dist\FanForge.exe --fan-diagnostics     # (v1.0) capacidades por ventilador + bloqueo
.\dist\FanForge.exe --fan-read            # RPM actuales por backend
.\dist\FanForge.exe --diagnose-driver     # estado de AsusSAIO / AsusOptimization
```

Criterio de interpretación:

| Observación | Significado | Acción de FanForge |
| --- | --- | --- |
| `FanCounts() >= 1` | La vía OEM está expuesta | backends detectan `DetectedUnverified`; **sigue bloqueada** hasta validación física con RPM |
| `FanCounts() == -1` | Firmware no expone la vía | `Unsupported`; no se intenta escribir |
| `FanRPM() == -1` | No hay lectura OEM | se usa ATKACPI/WMI para RPM |
| `Thermal_Read_Cpu_Temperature() == 0x7FFFFFFF` | Sentinela | temperatura vía WMI (zona ACPI) |

## 4. Procedimiento de validación (requiere persona presente)

Solo si algún día `FanCounts() >= 1` en este equipo, la validación seguiría `docs/FANSPEED-PHYSICAL-TESTS.md`:

1. Capturar RPM basales en AUTO.
2. Aplicar un PWM bajo supervisado y medir **variación real de RPM** (no basta el acuse del controlador).
3. Restaurar el control automático y **confirmar** la vuelta al comportamiento del BIOS.
4. Repetir con escalones y registrar; detener ante cualquier anomalía.

Mientras `FanCounts()` siga en `-1`, estas pruebas están **bloqueadas por firmware** y FanForge
documenta el bloqueo en lugar de forzar métodos desconocidos.

## 5. Límites y seguridad

- FanForge **no** ejecuta `SetFanTestMode`/`SetFanPwmDuty` sin validación previa; hacerlo en un equipo
  con `FanCounts() = -1` es escribir a ciegas en el EC.
- **No** se revierten drivers, se modifican registros EC ni firmware de forma automática; esas acciones
  son decisión del usuario (ver `docs/HARDWARE-TEST.md`, "Próximos pasos").
- El modo de prueba, si se usara, **debe** salir explícitamente al terminar; un cierre anómalo podría
  dejar el ventilador fijado (riesgo idéntico al declarado por G-Helper).
