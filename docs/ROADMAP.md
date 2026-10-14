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
- [ ] Leer RPM, CPU y conteo en VivoBook M3500QC con `--probe` (consola elevada).
- [ ] Confirmar si OEM API permite control 80% y 100% (medir RPM reales).
- [ ] Confirmar recuperación en fallas, suspensión y salida normal.

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
