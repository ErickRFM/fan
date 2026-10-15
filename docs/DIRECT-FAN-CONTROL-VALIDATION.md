# Validación física del control directo por ventilador (v1.1)

Este documento es el **protocolo operativo** para decidir si el ASUS M3500QC (o cualquier equipo
autorizado) expone un canal PWM **por ventilador** que FanForge pueda usar con seguridad. Es el puente
entre la investigación (`docs/DIRECT-FAN-CONTROL-RESEARCH.md`, `docs/MYASUS-FAN-DIAGNOSTICS.md`) y la
prueba supervisada resumida en `docs/FANSPEED-PHYSICAL-TESTS.md`.

> **Regla de oro:** FanForge **no declara PWM compatible** hasta observar una **respuesta física
> verificable** de RPM **y** demostrar la **restauración segura**. La captura de evidencia es 100 % de
> solo lectura; cualquier escritura de PWM exige **persona presente** y el procedimiento supervisado.

## 0. Precondiciones y reglas no negociables

**Persona presente** durante toda la sesión de escritura. Para la parte de solo lectura no es
estrictamente necesaria, pero se recomienda.

- Equipo enchufado a la corriente y sin suspensión automática.
- Consola **elevada** para las lecturas ATKACPI/WMI/OEM (y para `--restore`).
- Cerrar herramientas que compitan por los ventiladores: MyASUS, Armoury Crate, ASUS Optimization,
  G-Helper. Registro en `procesos-control.csv`.
- **Prohibido** (sin autorización explícita y presencial): instalar o activar drivers experimentales,
  tocar el EC por vías no documentadas, o desactivar Secure Boot / Smart App Control / WDAC / HVCI.
- **Prohibido** copiar o descompilar código GPL-3.0 de G-Helper; solo se usan hechos de interfaz
  públicos.
- FanForge mantiene el control manual **bloqueado** mientras el veredicto no sea `Verified`.

**Estado actual (M3500QC, BIOS `M3500QC.316`, 2026-10-15):** veredicto **`Blocked`**.
`HealthyTable_FanCounts() = -1` y las curvas ATKACPI responden `-65536`: el firmware no expone PWM
fijo. La validación física queda documentada pero **no ejecutable hoy** en este equipo.

## 1. Paso 0 — Recuperar y sanear el estado previo

Antes de medir nada, asegurarse de que no queda un estado manual colgado ni una instancia vieja.

1. **Recuperación pendiente** (solo lectura):
   ```powershell
   .\dist\FanForge.exe --fan-recovery-status
   ```
   Si indica «perfil original guardado», hay un cambio previo sin restaurar.
2. **Instancias en ejecución**: comprobar `procesos-control.csv` (o `Get-Process FanForge`). Una
   instancia elevada antigua debe cerrarse desde una consola elevada antes de continuar.
3. **Restaurar el control automático** solo si el paso 1 detecta un pendiente (acción explícita, la
   decide la persona presente; **no** forma parte del recolector de solo lectura):
   ```powershell
   .\dist\FanForge.exe --restore   # devuelve PWM/servicio a BIOS y restaura el perfil capturado
   ```
   Repetir `--fan-recovery-status` hasta que no quede registro pendiente.
4. Confirmar que el equipo está en un **perfil ASUS conocido** (Whisper/Standard/Performance).

## 2. Captura de evidencia (solo lectura)

Una sola orden produce el paquete completo. No escribe PWM, no entra en modo de prueba, no cambia el
perfil ni toca drivers o protecciones.

```powershell
# Consola elevada (para las lecturas reales). -Segundos N añade un muestreo 1 Hz.
powershell -ExecutionPolicy Bypass -File .\scripts\recolectar-evidencia-fan.ps1 -Segundos 60
```

El script reúne, sin escribir nada:

| Comando / fuente | Qué aporta |
| --- | --- |
| `--probe` | `FanCounts`/`FanRPM` OEM y estado de `AsusWinIO64.dll` |
| `--diagnose-driver` | estado de `AsusSAIO` / ASUS System Control Interface |
| `--fan-backends` | capacidades por backend (PWM/curvas/perfiles/telemetría) |
| `--fan-capabilities` | si existe un canal PWM **verificado** |
| `--fan-read` | RPM CPU/GPU y temperatura por backend |
| `--fan-diagnostics` | capacidades **por ventilador** (CPU/GPU) + motivo exacto del bloqueo |
| `--fan-research-report` | matriz de interfaces + detección en vivo |
| `--fan-recovery-status` | registro de recuperación pendiente y perfil actual |
| `--fan-evidence <dir>` | hoja CSV por backend + manifiesto JSON + veredicto |
| `--log-telemetry <s> <dir>` | muestreo 1 Hz (RPM/temperatura/perfil) a CSV |
| WMI `MSAcpi_ThermalZoneTemperature` | temperaturas de zona (no requiere elevación) |

Artefactos generados en `evidencia-fan\<yyyyMMdd-HHmmss>\`: ver §7.

**Interpretación rápida:**

| Observación | Significado |
| --- | --- |
| `FanCounts() >= 1` | la vía OEM está expuesta → candidata a validación física |
| `FanCounts() == -1` | firmware no expone la vía OEM → `Blocked` |
| `DevsCPUFanCurve`/`DevsGPUFanCurve == -65536` | curvas no soportadas |
| `--fan-capabilities` → «PWM directo verificado: no» | sin canal escribible; manual bloqueado |

## 3. Diagnóstico «fan testing» de MyASUS (supervisado)

MyASUS usa la biblioteca OEM para un control **directo y con estado** (ver `docs/MYASUS-FAN-DIAGNOSTICS.md`).
Es una referencia del comportamiento que se busca, **no** una vía que FanForge active por su cuenta.

Solo con persona presente y **únicamente** como comprobación de referencia del OEM:

1. Habilitar el «Diagnóstico de ventilador» de MyASUS y **observar** si los ventiladores cambian de
   velocidad (subjetivamente y, si se puede, con `--fan-read` en paralelo).
2. Al terminar, salir del modo de prueba de MyASUS y confirmar la vuelta al comportamiento automático.
3. Anotar hora, perfil y cualquier anomalía.

> Si el «Diagnóstico de ventilador» de MyASUS **no funciona** (caso del M3500QC tras ciertas versiones
> del «ASUS System Control Interface»), es señal fuerte de que la vía OEM está rota/bloqueada a nivel de
> firmware; FanForge **no** debe forzarla.

FanForge **no** ejecuta `SetFanTestMode`/`SetFanPwmDuty` en este paso: solo el OEM, bajo supervisión.

## 4. Prueba de causalidad de RPM (solo si hay canal expuesto)

Aplicable **solo** si `--fan-capabilities` reporta una vía `Compatible`/`Unverified` (hoy no ocurre).
Requiere persona presente y el canal habilitado por el equipo de desarrollo (nunca por el recolector).

1. Baseline: dos `--raw`/`--fan-read` separados en reposo, y otro bajo carga sostenida unos minutos.
2. Elegir un objetivo **conservador** (70–80 %, no 100 %) dentro de la ventana 70–100 %.
3. Aplicar la orden **solo** por el canal autorizado (GUI/comando), nunca por el script de evidencia.
4. Releer RPM tras un intervalo estable; considerar **confirmado** únicamente si la RPM **aumenta**
   claramente por encima de la histéresis (`FanPolicy.Increased`, delta 150 RPM) y **se repite** en una
   **segunda** lectura independiente.
5. Repetir para CPU y para GPU por separado; y «ambos 100 %» si se autoriza.
6. Restaurar y repetir §5.

> Un cambio de RPM **no prueba** por sí solo causalidad: temperatura, carga e histéresis influyen. Se
> registra como **asociación medida**, nunca como demostración.

## 5. Seguridad, restauración y criterios de aborto

Con control manual activo (solo en la fase supervisada):

1. `--restore` y verificar por lectura que la RPM vuelve al comportamiento automático (perfil original).
2. **Watchdog:** forzar el cierre del proceso y comprobar que el watchdog restaura el perfil original.
3. **Reanudación:** suspender y reanudar; verificar que no queda control manual colgado.
4. `--fan-recovery-status`: sin registro pendiente tras una restauración correcta.

**Abortar de inmediato** (restaurar y cerrar) si ocurre cualquiera de estos:

- Un ventilador bajo control manual reporta **0 RPM** o lectura inválida (`EvaluateSafetyAsync` ya
  dispara restauración preventiva).
- La temperatura sube de forma anómala o sostenida con el ventilador supuestamente acelerado.
- El handle ATKACPI/OEM deja de responder o el servicio ASUS se degrada.
- Cualquier cierre anómalo del proceso durante el control manual (riesgo declarado por el propio autor
  del control manual experimental: el ventilador puede quedar fijado).
- Se pierde la restauración verificable.

Registrar hora, perfil, RPM antes/después y una nota; cerrar la evidencia en el paquete.

## 6. Criterios de aceptación y matriz de decisión

Un canal se considera **`Verified`** solo si se cumplen **todos**:

- [ ] Baseline documentado en reposo y bajo carga.
- [ ] PWM por ventilador solicitado de forma conservadora **y** respuesta de RPM **confirmada en ≥ 2
      lecturas** independientes.
- [ ] Restauración demostrada: `--restore` **y** watchdog **y** reanudación.
- [ ] **Sin falsos positivos:** el % solicitado nunca se presenta como RPM medida.
- [ ] Auditoría del motor coherente (estados y marcas de tiempo).

La regla está implementada en `FanForge.Core.FanEvidence.Evaluate`:

| Canal expuesto | Lecturas confirmadas | Restauración completa | Veredicto | Acción |
| --- | --- | --- | --- | --- |
| No | — | — | `Blocked` | mantener el control manual **bloqueado** |
| Sí | < 2 | — | `InsufficientEvidence` | seguir midiendo; **bloqueado** |
| Sí | ≥ 2 | incompleta | `RpmResponded` | completar prueba de restauración; **bloqueado** |
| Sí | ≥ 2 | completa | `Verified` | habilitar PWM **por ventilador** (tras PR y revisión) |

Cualquier veredicto distinto de `Verified` mantiene el control manual deshabilitado. La seguridad del
hardware tiene prioridad sobre la funcionalidad.

## 7. Artefactos de evidencia

Paquete por ejecución en `evidencia-fan\<yyyyMMdd-HHmmss>\`:

| Archivo | Contenido |
| --- | --- |
| `LEEME.txt` | resumen + veredicto + interpretación |
| `equipo.txt` | modelo, BIOS, CPU, GPU, elevación |
| `driver-asus.csv` | ASUS System Control Interface (versión/fecha) |
| `servicios-asus.csv` | servicios ASUS (estado/tipo) |
| `procesos-control.csv` | herramientas de control en ejecución (posibles conflictos) |
| `temperaturas-wmi.csv` | temperaturas de zona ACPI |
| `fan-*.txt` | salidas de los comandos de solo lectura |
| `fan-evidence-*.csv` | hoja de evidencia por backend (columnas `pwm_cpu`/`pwm_gpu`) |
| `fan-evidence-*.json` | manifiesto: `control_path_exposed`, `verdict`, backends |
| `log-telemetry-*.csv` | muestreo 1 Hz (si `-Segundos > 0`) |

Columnas del CSV de evidencia:
`timestamp_utc,model,bios,backend,pwm_cpu,pwm_gpu,telemetry,cpu_rpm,gpu_rpm,cpu_celsius,gpu_celsius,profile,quality`.

## 8. Límites y qué NO hacer

- La captura de evidencia **nunca** confirma PWM: por diseño registra
  `verdict=Blocked`/`InsufficientEvidence` (lecturas confirmadas = 0).
- No se escribe PWM, no se entra en modo de prueba, no se cambian perfiles, no se instalan drivers, no
  se modifican protecciones de Windows desde FanForge.
- No se declara compatible un ventilador hasta que **ambos** (respuesta de RPM y restauración segura)
  estén demostrados por hardware real.
- Si el firmware bloquea: se documenta el bloqueo exacto y se mantiene la mejor herramienta de perfil
  segura, sin forzar métodos desconocidos.
