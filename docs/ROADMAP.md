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
- [x] Verificar ejecución verde del workflow en GitHub Actions (PR #5, #6, #8).

## F1 — Validación física obligatoria
- [x] Leer RPM reales en VivoBook M3500QC mediante backend ATKACPI (`--hardware-diagnose`): CPU ~4400–6100, GPU ~3800–4000 RPM.
- [x] Confirmar temperatura vía zona térmica ACPI (WMI), aproximada (~90 °C).
- [x] Confirmar capacidades reales: perfiles = sí; curvas = no; PWM ATKACPI = no.
- [ ] Confirmar control PWM 70–100% por HealthyTable (bloqueado por driver `AsusSAIO`).
- [x] Escritura de perfil ATKACPI validada en el M3500QC (Silent/Standard/Performance, verificado por lectura y reversible).
- [ ] Confirmar recuperación en fallas, suspensión y salida normal.
- [ ] Comparar RPM con ProArt Creator Hub.

## F4 — Multibackend y telemetría (v0.4)
- [x] Contratos `IFanTelemetryProvider`/`IFanControlBackend`, capacidades y `FanBackendManager`.
- [x] Proveedor ATKACPI de RPM (solo lectura) y proveedor de temperatura ACPI/WMI.
- [x] Control de perfiles ASUS verificado por lectura posterior.
- [x] UI con reconocimiento de capacidades (perfiles vs. PWM manual).
- [x] Historial acotado de telemetría y mini-gráficas de temperatura/RPM en la UI.

## F2 — Robustez
- [x] Heartbeat bidireccional del watchdog (lógico): la UI detecta un observador perdido y restaura BIOS (`WatchdogPolicy`).
- [ ] Validar físicamente la restauración por crash real de la UI/observador en el M3500QC.
- [x] Detección de conflictos con MyASUS/Armoury Crate/G-Helper (aviso en UI y `--hardware-diagnose`).
- [x] Control aislado del hilo de GUI y timeouts de driver (inicialización asíncrona).
- [x] Firma/versión verificada de la biblioteca OEM (`OemLibraryPolicy`/`OemLibraryVerifier`).

## F3 — UX
- [x] Historial y gráficas RPM/temperatura.
- [ ] Perfiles personalizables validados contra sensores.
- [x] Accesibilidad de escalado (`PerMonitorV2` + `AutoScaleMode.Dpi`) y atajos `Esc`/`F5` con nombres accesibles.
- [ ] Validar visualmente 125–200% y navegación por teclado en el equipo físico.
- [ ] Empaquetado instalador firmado.

Nunca declarar compatibilidad real sin mediciones en el equipo físico.

## v0.5 — Interfaz y control por capacidades
- [x] Evitar estado falso de Perfil Máximo = 100% (mismo código que Rendimiento).
- [x] Mostrar tarjetas CPU/GPU con rpm ACPI aunque HealthyTable esté bloqueado.
- [x] Proteger confirmación de escritura de perfil frente a sentinelas negativos.
- [x] Aplicar control de perfiles solo cuando el hardware declare soporte.
- [x] Validado en M3500QC real: Silent/Standard/Performance se aceptan y se confirman por lectura (reversible).
- [ ] Medir de forma sostenida que las RPM siguen la temperatura en cada modo (comparar con ProArt Creator Hub).
- [ ] Investigar otra vía OEM oficialmente admitida para control manual; ATKACPI no expone curvas/PWM en este BIOS.

## v0.6 — Hardening de interoperabilidad
- [x] Guardas de modelo en backend de perfiles y comandos CLI, además del controlador PWM.
- [x] Handles ATKACPI locales por operación; se evita el campo compartido por lecturas y escrituras.
- [x] Telemetría no declara `Supported` si el driver/ventiladores no responden.
- [x] Bloqueo de selección de perfil mientras hay una sesión PWM manual.
- [ ] Validar perfil y restauración en VivoBook real; no se ejecutaron escrituras de hardware.

## Auditoría de software v0.6 — entrega
- [x] Tests y compilación v0.6 verificados (115 PASS; PR #13).
- [x] Guardas ATKACPI y firma nativa en ruta WinIO.
- [ ] Verificación presencial de firma de AsusWinIO64.dll en la instalación real.
- [ ] Confirmar cambios de perfiles ASUS comparando RPM con ProArt.
- [ ] PWM manual y retorno tras suspensión/crash pendientes de medición.
