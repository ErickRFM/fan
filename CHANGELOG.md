# Historial

## 0.3.0 — Validación Windows y arquitectura (rama `feat/windows-hardware-validation-v03`)
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
