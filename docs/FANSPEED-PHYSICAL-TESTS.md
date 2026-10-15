# FanSpeed — protocolo de validación física (M3500QC)

Este documento describe cómo se validaría **físicamente** un canal de control de ventiladores antes de
habilitarlo en FanForge. Hasta que estos pasos se completen con resultados concluyentes y autorización
presencial, el motor reporta el canal como `Unsupported`/`Unverified` y **no permite escrituras**.

> **Requisitos previos obligatorios**
> - Presencia física de la persona propietaria del equipo durante toda la prueba.
> - Equipo conectado a la red eléctrica.
> - Ningún otro programa controlando los ventiladores (MyASUS, Armoury Crate, G-Helper).
> - Consola elevada, `--restore` disponible para volver a BIOS/perfil original.
> - No se modifican drivers, firmware ni EC. No se desactivan Secure Boot / Smart App Control / WDAC.

## 1. Estado actual en este equipo (bloqueo conocido)

| Comprobación | Comando | Resultado observado |
|---|---|---|
| Backends y PWM verificado | `--fan-backends` / `--fan-capabilities` | Ningún backend con PWM `Verified` |
| Lectura ATKACPI | `--fan-read` | CPU/GPU RPM válidos (p. ej. CPU 5500, GPU 3900) |
| Ruta OEM HealthyTable | `--probe` / GUI | `HealthyTable_FanCounts() = -1` (no soportada) |
| Endpoints de curva ATKACPI | `--hardware-diagnose` | `Devs*FanCurve` → `-65536` (rechazado) |
| Estado del motor | `--fan-capabilities` | `AvailableUnverified` |

**Conclusión:** el firmware de este modelo/BIOS no expone un canal de PWM fijo ni curvas. La validación
física solo tiene sentido si, en el futuro, un backend reportase PWM `Detected`/`Unverified` o si se
prueba otro modelo/BIOS autorizado.

## 2. Línea base (sin control)

1. Ejecutar `--fan-read` y `--hardware-diagnose` con el equipo en reposo (≥5 min sin carga).
2. Anotar RPM CPU/GPU y temperatura de zona.
3. Registrar durante 60 s con `--log-telemetry 60` en reposo y después bajo carga (p. ej. una tarea de
   CPU sostenida unos minutos) para conocer el rango natural de RPM.

## 3. Prueba de causalidad de RPM (solo si hay canal `Unverified`)

1. Elegir un objetivo conservador (no 100 %) dentro de 70–100 %.
2. Capturar baseline con `--fan-read` (dos lecturas separadas).
3. Solicitar el cambio **solo** por la GUI o el comando autorizado correspondiente.
4. Releer RPM tras un intervalo estable; considerar **confirmado** únicamente si la RPM **aumenta** de
   forma clara por encima de la histéresis (`FanPolicy.Increased`, delta 150 RPM) **y** se repite en una
   segunda lectura.
5. Repetir con un segundo valor y registrar todo (RPM antes/después, hora, perfil, carga).

> Un cambio de RPM **no prueba** por sí solo que la orden lo causó: temperatura, carga e histéresis
> influyen. La confirmación es una **asociación medida**, no una garantía.

## 4. Prueba de seguridad y restauración

1. Con control manual activo, ejecutar `--restore` y verificar por lectura que la RPM vuelve al
   comportamiento automático (perfil original restaurado).
2. Forzar un cierre del proceso y comprobar que el watchdog recupera el control (perfil original).
3. Suspender y reanudar el equipo; verificar que no queda control manual colgado y que el perfil se
   restaura.
4. Comprobar `--fan-recovery-status`: sin registro pendiente tras una restauración correcta.

## 5. Criterios de aceptación

- [ ] Baseline documentado en reposo y bajo carga.
- [ ] Solicitud conservadora con **aumento de RPM confirmado** en dos lecturas.
- [ ] Restauración verificada por `--restore`, watchdog y reanudación.
- [ ] Ningún falso positivo: el valor solicitado nunca se presenta como RPM medida.
- [ ] Registro de auditoría del motor coherente (estados y marcas de tiempo).

Si cualquiera de estos criterios falla, el canal se mantiene `Unverified` y el control manual sigue
deshabilitado. La seguridad del hardware tiene prioridad sobre la funcionalidad.
