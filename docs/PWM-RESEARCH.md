# Investigación: control PWM manual en el M3500QC (v0.8)

Objetivo: determinar si es posible el control manual (curva/% PWM) de los ventiladores del ASUS
VivoBook M3500QC mediante interfaces **soportadas**, sin modificar drivers, EC ni firmware. Este
documento resume la investigación; **no** es una guía para eludir protecciones.

## Interfaces investigadas

| Vía | Detalle | Resultado |
| --- | --- | --- |
| ATKACPI device `\\.\ATKACPI` (IOCTL `0x0022240C`) | `CPU_Fan 0x00110013`, `GPU_Fan 0x00110014`, curvas `0x00110024/25` | **Falla**: `Win32Exception: ATKACPI DeviceIoControl failed` (handle bloqueado por el driver ASUS) |
| OEM library `AsusWinIO64.dll` | `HealthyTable_FanCounts()` y métodos de tabla *health* | **Devuelve `-1`**: el método OEM no expone ventiladores en este modelo/BIOS |
| ASUS System Control Interface v3 | `AsusSAIO` `Running/OK StartMode=Manual`; `AsusOptimization` `Running/Auto` | Conflicto de servicios detectado (`ControlConflicts`), pero sin canal de fans |
| Perfil `VivoBookMode 0x00110019` | Silencioso(1)/Estándar(0)/Rendimiento(2) | **Soportado y confirmado** por read-back |

## Evidencia

- Lectura de temperaturas: vía WMI `root\WMI:MSAcpi_ThermalZoneTemperature` (funciona).
- Lectura de RPM: `CPU_Fan`/`GPU_Fan` responden como **consulta**, pero los endpoints de escritura de
  curvas/perfil de ventilador lanzan excepción y dejan el handle inutilizable → se adoptó **lectura
  tolerante por endpoint**, leyendo primero los ventiladores.
- Diagnóstico estructurado en `--diagnose-driver` (`FanForge.Win/DriverDiagnostics.cs`): expone
  servicio, estado, `StartMode` y resultado de `HealthyTable_FanCounts()`.

## Conclusión

En el modelo/BIOS actual, el control PWM manual **no está disponible** por las interfaces
soportadas: el método OEM declara 0 ventiladores controlables y los endpoints ATKACPI de escritura
fallan. Es una decisión del firmware, no un fallo de FanForge.

## Opciones evaluadas

| Opción | Estado |
| --- | --- |
| Usar `VivoBookMode` (Silencioso/Estándar/Rendimiento) | **Adoptada**: soportada, reversible, confirmada por read-back |
| Depender de los métodos OEM de ventilador | **Descartada**: `FanCounts = -1` en este modelo |
| Modificar/reemplazar drivers ASUS | **Rechazada**: riesgo de sistema, fuera de alcance y prohibido en esta sesión |
| Reflashear EC/firmware | **Rechazada**: riesgo irreversible, fuera de alcance |
| Driver de kernel propio para hablar con el EC | **Rechazada**: requiere firma *kernel* (WHQL), enorme riesgo y complejidad |

## Línea de trabajo

- Vigilar actualizaciones de BIOS / "ASUS System Control Interface v3" que pudieran exponer la tabla
  de ventiladores.
- Mantener el control por perfil como vía soportada (`docs/THERMAL-COMPARISON.md`, `--analyze`).
- Si en el futuro aparece un `HealthyTable_FanCounts() > 0`, habilitar ramas de PWM manual **detrás
  de la misma guarda de perfil original** (`ProfileGuard`) y con validación física autorizada.

## Notas de seguridad

La aplicación **nunca** desactiva Smart App Control, WDAC ni Secure Boot, y **no** modifica drivers,
servicios de ASUS, EC ni firmware. Toda escritura soportada es reversible y confirmada por read-back.
