# Roadmap FanForge
## F0 — Base y calidad
- [x] Interfaz Windows 11 WinForms/.NET 8.
- [x] Guardas de modelo M3500QC, controles PWM mínimos 70%.
- [x] Biblioteca ASUS OEM externa, sin distribuir DLL propietaria.
- [x] Watchdog proceso separado, devolución a BIOS y diagnóstico.
- [x] Pruebas lógicas independientes del hardware y pipeline Windows.
- [x] Compilación Release y ejecutable x64 verificados localmente (`dist/FanForge.exe`).
- [x] Selección determinista de la versión más reciente de `AsusWinIO64.dll`.
- [x] Núcleo `FanForge.Core` con `IFanBackend`/`FanSession` y controlador falso para pruebas.
- [x] Sonda de solo lectura `--probe`.
- [ ] Verificar ejecución verde del workflow en GitHub Actions (no confundir con workflow creado).

## F1 — Validación física obligatoria
- [x] Leer RPM reales en VivoBook M3500QC mediante backend ATKACPI (`--hardware-diagnose`): CPU ~4400–6100, GPU ~3800–4000 RPM.
- [x] Confirmar temperatura vía zona térmica ACPI (WMI), aproximada (~90 °C).
- [x] Confirmar capacidades reales: perfiles = sí; curvas = no; PWM ATKACPI = no.
- [ ] Confirmar control PWM 70–100% por HealthyTable (bloqueado por driver `AsusSAIO`).
- [ ] Validar escritura de perfil ATKACPI en banco (pendiente de autorización del usuario).
- [ ] Confirmar recuperación en fallas, suspensión y salida normal.
- [ ] Comparar RPM con ProArt Creator Hub.

## F4 — Multibackend y telemetría (v0.4)
- [x] Contratos `IFanTelemetryProvider`/`IFanControlBackend`, capacidades y `FanBackendManager`.
- [x] Proveedor ATKACPI de RPM (solo lectura) y proveedor de temperatura ACPI/WMI.
- [x] Control de perfiles ASUS verificado por lectura posterior.
- [x] UI con reconocimiento de capacidades (perfiles vs. PWM manual).
- [x] Historial acotado de telemetría y mini-gráficas de temperatura/RPM en la UI.

## F2 — Robustez
- [ ] Validar heartbeat bidireccional watchdog y manejo de crashes.
- [ ] Detección de conflictos con MyASUS/G-Helper.
- [x] Control aislado del hilo de GUI y timeouts de driver (inicialización asíncrona).
- [ ] Firma/versión verificada de la biblioteca OEM.

## F3 — UX
- [ ] Historial y gráficas RPM/temperatura.
- [ ] Perfiles personalizables validados contra sensores.
- [ ] Accesibilidad 125–200% y pruebas teclado.
- [ ] Empaquetado instalador firmado.

Nunca declarar compatibilidad real sin mediciones en el equipo físico.

## v0.5 — Interfaz y control por capacidades
- [x] Evitar estado falso de Perfil Máximo = 100% (mismo código que Rendimiento).
- [x] Mostrar tarjetas CPU/GPU con rpm ACPI aunque HealthyTable esté bloqueado.
- [x] Proteger confirmación de escritura de perfil frente a sentinelas negativos.
- [x] Aplicar control de perfiles solo cuando el hardware declare soporte.
- [ ] Validar en M3500QC real que cada modo solicitado se acepta y que las RPM cambian según temperatura.
- [ ] Investigar otra vía OEM oficialmente admitida para control manual; ATKACPI no expone curvas/PWM en este BIOS.
