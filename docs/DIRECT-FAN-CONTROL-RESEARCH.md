# Control directo de ventiladores — investigación (v1.0, F1)

Investigación de las vías reales para **fijar el porcentaje de velocidad de cada ventilador de forma
individual** (CPU/GPU) en el ASUS M3500QC, base del trabajo de FanForge v1.0. Este documento complementa
`docs/FANSPEED-RESEARCH.md` (v0.9) y no lo sustituye.

> Aviso de licencia: G-Helper es **GPL-3.0**; FanForge es MIT. No se copia código de G-Helper. Solo se
> documentan **hechos de interfaz** (números de método, endpoints, semántica de bits) obtenidos de
> fuentes públicas, que se reimplementan de forma independiente contra las mismas APIs de ASUS.

## 1. Fuentes analizadas

| Fuente | Qué aporta | Publicada |
| --- | --- | --- |
| `seerge/g-helper` → `app/AsusACPI.cs` | `DSTS`/`DEVS` por `\\.\ATKACPI` (IOCTL `0x0022240C`), endpoints, perfiles, curvas, `-65536` | sí (GPL-3.0) |
| `seerge/g-helper` → discussion #2272 "[Experimental] Manual Fan Control" | Semántica del control manual por % (método MyASUS fan-test), límites, riesgos, cadencia | sí (texto) |
| `seerge/g-helper-experimental-release` | Binarios del build experimental de control manual | **solo binarios + README; el código del control manual NO está publicado** |
| `namazso/PawnIO` | Driver de kernel firmado para MSR/SMU; sin módulo de ventilador | sí (GPL-2.0 / MIT según componente) |
| Pruebas propias M3500QC (`docs/HARDWARE-TEST.md`) | `HealthyTable_FanCounts()=-1`; endpoints de curva `-65536`; perfiles verificados | propio |

> **Hecho clave:** el código fuente del control manual de G-Helper experimental **no está publicado**.
> El repositorio `g-helper-experimental-release` contiene únicamente `.github/` y `README.md`; las builds
> se distribuyen como binarios en *Releases*. Por tanto la mecánica exacta solo se conoce por el
> README del maintainer y por analogía con el "fan testing" de MyASUS, **no** por lectura de código.
> No se copia ni se decompila.

## 1.1 Qué afirma el método experimental (según seerge, discussion #2272)

- Se puede **fijar la velocidad por ventilador en porcentaje**, de forma individual.
- Usa **"el mismo método que MyASUS cuando ejecuta su 'fan testing'"**.
- CPU fan sigue la temperatura de CPU, GPU fan la de GPU, Mid fan la de CPU.
- Velocidad de ventilador actualizada **cada segundo** (build posterior: cada 500 ms).
- **Mínimo 20 %** salvo apagado total; **máximo 100%** (interpretación de % propia de ASUS).
- A diferencia de las curvas estándar, el firmware/BIOS no maneja la velocidad: la app la fija
  directamente. Al desactivar, el comportamiento vuelve al BIOS.
- **Riesgos declarados por el propio autor:** si se cierra la app de forma incorrecta durante el control
  manual, los ventiladores **pueden quedarse en la última velocidad fijada** y no reaccionar a la
  temperatura; "no hay protección contra el usuario" (si se fija bajo, se calienta).

Implicación para FanForge: el control manual directo es una **escritura continua y con estado**, no una
única orden. Esto obliga a un canal serializado, un watchdog de restauración y una confirmación por RPM
medida; es exactamente el modelo que ya adopta `FanSpeedEngine`.

## 2. Interfaces candidatas

### 2.1 ATKACPI (`\\.\ATKACPI`, IOCTL `0x0022240C`)
Canal principal ASUS. Métodos `DSTS` (get `0x53545344`), `DEVS` (set `0x53564544`), `INIT`, `WDOG`.
Endpoints de interés: `CPU_Fan 0x00110013`, `GPU_Fan 0x00110014`, `Mid_Fan 0x00110031`,
`DevsCPUFan 0x00110022`, `DevsGPUFan 0x00110023`, `DevsCPUFanCurve 0x00110024`,
`DevsGPUFanCurve 0x00110025`, `VivoBookMode 0x00110019`.

En el M3500QC: RPM (`CPU/GPU_Fan`) **se leen**; perfiles (`VivoBookMode`) **se leen/escriben y están
verificados**; curvas (`Devs*FanCurve`) responden `-65536` (**no soportado**); `Devs*Fan` (rango
min/max) **no confirmado**.

### 2.2 WMI (`root\wmi:AsusAtkWmi_WMNB`)
Misma familia ACPI expuesta por WMI. `DSTS` con parámetro de salida nombrado `device_status`; el valor
viene con **bit de validez `0x10000`** (`value = raw - 0x10000`, ver `AsusWmiCodec`). Verificado como
canal de **lectura** en el M3500QC; la firma para **escritura** (`DEVS` vía WMI) no está validada aquí.

### 2.3 HealthyTable (`AsusWinIO64.dll`, OEM / MyASUS)
Es la vía histórica del OEM para PWM fijo: `HealthyTable_FanCounts()`, `HealthyTable_SetFanTestMode`,
`HealthyTable_SetFanPwmDuty`, `HealthyTable_SetFanIndex`. Es la candidata más plausible para el
método "igual que MyASUS fan testing". **En el M3500QC `HealthyTable_FanCounts()` devuelve -1**, por lo
que la ruta de escritura no está expuesta por firmware y permanece `Unsupported` salvo validación física.

### 2.4 PawnIO (namazso)
Driver de kernel **firmado** que ejecuta módulos en un intérprete sandbox para MSR/IO/PCI. G-Helper lo
usa para Ryzen SMU / Intel MSR, **no** para ventiladores. No existe módulo oficial de ventilador para
el M3500QC; escribir EC por esta vía se considera equivalente a un driver no oficial y **queda
prohibido** en FanForge. Se expone como backend **deshabilitado**.

## 3. Matriz de compatibilidad (M3500QC, BIOS `M3500QC.316`)

| Vía de control | Detección | Verificado físicamente | Acción v1.0 |
| --- | --- | --- | --- |
| Perfiles ASUS (`VivoBookMode`) | sí | **sí** | habilitado (con `ProfileGuard`) |
| Curvas térmicas (`Devs*FanCurve`) | no (`-65536`) | no | `Unsupported` |
| PWM fijo directo (`HealthyTable` / MyASUS) | `FanCounts=-1` | no | `DetectedUnverified`/`Unsupported`, escritura bloqueada |
| PWM directo vía ATKACPI | no | no | `Unsupported` |
| PWM directo vía WMI (escritura) | no | no | `Unsupported` |
| PawnIO | sin módulo | no | deshabilitado |
| Lectura RPM (ATKACPI + WMI) | sí | sí | habilitado (telemetría) |

Conclusión: en el M3500QC actual **no existe ninguna vía de PWM directo verificada**. FanForge debe
modelar el control por ventilador, medir y ofrecer restauración, pero **no puede presentar el control
manual como operativo** hasta que una vía responda y se valide en hardware con RPM.

## 4. Riesgos y restricciones

- **Bloqueo por firmware:** forzar métodos desconocidos puede colgar el handle ATKACPI o el servicio
  ASUS; las lecturas deben ser tolerantes y hacerse por endpoint.
- **Estado persistente:** el control manual directo deja la velocidad fijada; un cierre anómalo puede
  no restaurar. Requiere watchdog + restauración al salir/reanudar.
- **Causalidad de RPM:** un cambio de RPM no prueba por sí solo que la orden lo causó (temperatura,
  histéresis). Registrar como **asociación**, no como demostración.
- **No confundir solicitado con real:** el "100 %" solicitado nunca se presenta como alcanzado sin RPM.
- **Licencia:** no copiar código GPL-3.0; reimplementar hechos de interfaz de forma independiente.
- **Seguridad del sistema:** no desactivar Secure Boot/Smart App Control/WDAC/HVCI; no usar drivers no
  firmados; no tocar el EC por vías no documentadas.

## 5. Recomendación técnica

1. Evolucionar `FanSpeedEngine` para modelar **capacidades por ventilador** (CPU/GPU independientes) y
   los estados `Unsupported, DetectedUnverified, Compatible, Ready, Applying, AcceptedUnverified,
   Verified, Restoring, Restored, Failed`.
2. Mantener la escritura **gated**: solo se escribe por un backend cuyo PWM sea `Verified`.
3. Solicitudes 70–100 % por ventilador y "ambos 100 %"; cada orden deja registro de auditoría con
   porcentaje solicitado y RPM observadas **separados**.
4. Restauración automática ante cierre, reanudación o lectura inválida (watchdog).
5. Exponer `--fan-diagnostics` y `--fan-research-report` (solo lectura) para inspección sin riesgo.
6. Si el firmware bloquea: documentar el bloqueo exacto y dejar la mejor herramienta de perfil segura.

Backends: ATKACPI y WMI (lectura), HealthyTable (gated, nunca `Verified` por defecto), PawnIO
(deshabilitado), simulado (pruebas).
