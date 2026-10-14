# Historial

## 0.4.0 — Multibackend ASUS, telemetría ATKACPI y perfiles (rama `feat/v04-asus-acpi-hardware-integration`)
- **Nuevo backend ATKACPI (solo lectura + perfiles):** abre `\\.\ATKACPI` e invoca los métodos `DSTS`/`DEVS` (IOCTL `0x0022240C`). En el M3500QC físico **lee RPM reales** de CPU y GPU (`0x00110013`/`0x00110014`) que, con la unidad de 100 RPM, corresponden a ~4400–6100 (CPU) y ~3800–4000 (GPU).
- **Backend suplementario de temperatura:** `AcpiThermalZoneTelemetry` lee la zona térmica ACPI vía WMI (`MSAcpi_ThermalZoneTemperature`), etiquetada como temperatura de zona aproximada (no de núcleo). Requiere elevación.
- **Arquitectura multibackend:** `IFanTelemetryProvider` / `IFanControlBackend`, `FanCapabilities`, `TelemetrySnapshot` y `FanBackendManager` (combina RPM + temperaturas y expone capacidades). El backend HealthyTable/`AsusWinIO64` se conserva como proveedor de control PWM.
- **Control de perfiles verificado:** `AsusAcpiControlBackend` cambia el modo VivoBook (`0x00110019`) y **confirma por lectura posterior**. Curvas y PWM directo se reportan como **no soportados** en este modelo (`0x00110024`/`0x00110025` = `-65536`).
- **Hallazgo:** `PerformanceMode` (`0x00120075`) y las temperaturas ATKACPI no están soportados en el M3500QC; solo el modo VivoBook. Documentado en `docs/ASUS-BACKENDS.md`.
- **CLI:** `--hardware-diagnose` (probe multibackend de solo lectura, con volcado de valores RAW) y `--set-profile <Perfil>` (escritura verificada). El diagnóstico sigue sin escribir nada.
- **UI con reconocimiento de capacidades:** si HealthyTable está bloqueado, muestra RPM CPU/GPU y temperatura en solo lectura y habilita los perfiles ASUS (Silencioso/Estándar/Rendimiento/Máximo); el control PWM manual solo se habilita cuando HealthyTable responde.
- **Pruebas:** 55 comprobaciones (antes 44), incluidas la decodificación ATKACPI (`AcpiFanRpm`, `AcpiTemperature`) y el intercambio de modo VivoBook. Ninguna escribe PWM físico.

## 0.4.1 — Combinación de telemetría testeable
- **`TelemetryMerge` en Core:** la prioridad entre proveedores (primer RPM válido, relleno de temperatura) deja de depender del hardware y se cubre con pruebas unitarias.
- **Pruebas:** 61 comprobaciones (antes 55): prioridad del primer proveedor, descarte de RPM inválidas, relleno de temperatura y conjunto vacío.
- Sin cambios de comportamiento en hardware; `FanBackendManager` usa ahora la función pura.

## 0.3.0 — Validación Windows y arquitectura (rama `feat/windows-hardware-validation-v03`)
- **Diagnóstico físico (M3500QC, elevado):** modelo/BIOS/DLL detectados correctamente; `HealthyTable_FanCounts()` y `FanRPM()` devuelven `-1`. El driver de kernel `AsusSAIO` está en estado `Stop Pending`/`Degraded`, coincidiendo con la regresión conocida de `AsusWinIO64.dll`. El control manual queda bloqueado a nivel de driver ASUS; no se modificaron drivers ni el EC. Ver `docs/HARDWARE-TEST.md` y `docs/QA.md`.
- **Mensaje de error accionable** cuando la API OEM devuelve un conteo inválido, con pasos seguros (revisar MyASUS Fan Diagnosis, reiniciar, revertir driver).
- **Entorno:** verificado en ASUS VivoBook M3500QC_D3500QC, Windows 11 x64, BIOS M3500QC.316, ASUS System Control Interface v3 3.1.72.0, .NET 8 SDK 8.0.426.
- **Selección de DLL OEM determinista:** la biblioteca se elige por versión más reciente entre varias carpetas del DriverStore (se detectaron 1.1.4.0 y 1.1.2.0); ya no depende del orden de enumeración.
- **Núcleo testeable:** `IFanBackend`, `FanReading` y `FanSession` en `FanForge.Core`; la lógica de reaplicar/confirmar/restaurar deja de depender del hardware.
- **Pruebas ampliadas:** policía de modelo, límites, duty porcentual, selección de DLL, confirmación de RPM, restauración preventiva y máquina de estados con controlador falso. 44 comprobaciones (antes 20). No escriben PWM físico.
- **Sonda `--probe`:** lectura de solo lectura (modelo, BIOS, ventiladores, RPM, CPU) sin activar el control manual.
- **UI/UX:** estado ASUS/modo/watchdog, indicador de actualización, temperatura y registro de eventos; inicialización de hardware fuera del hilo de UI; confirmación previa al primer control manual con aviso de energía.
- **Suspensión/reanudación:** restauración a BIOS al suspender y limpieza del watchdog si murió.
- Sin cambios en las firmas nativas (coinciden con la referencia MIT) y sin distribuir la DLL propietaria.

## 0.2.1 — Rama de revisión
- Inicialización en ErickRFM/fan.
- Separación de política de control, WinForms, hardware OEM y recuperación.
- Bloqueo de modelos distintos del M3500QC también en el backend.
- PWM de 70-100% manual; restauración a BIOS y lectura de RPM reales.
- Pipeline CI Windows y guía de prueba física.
- Sin declaración de éxito físico ni distribución de drivers.
