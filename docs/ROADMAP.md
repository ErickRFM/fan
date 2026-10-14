# Roadmap FanForge
## F0 — Base y calidad
- [x] Interfaz Windows 11 WinForms/.NET 8.
- [x] Guardas de modelo M3500QC, controles PWM mínimos 70%.
- [x] Biblioteca ASUS OEM externa, sin distribuir DLL propietaria.
- [x] Watchdog proceso separado, devolución a BIOS y diagnóstico.
- [x] Pruebas lógicas independientes del hardware y pipeline Windows.
- [ ] Verificar compilación efectiva en GitHub Actions (no confundir con workflow creado).

## F1 — Validación física obligatoria
- [ ] Leer RPM, CPU y conteo en VivoBook M3500QC.
- [ ] Confirmar si OEM API permite control 80% y 100%.
- [ ] Confirmar recuperación en fallas, suspensión y salida normal.

## F2 — Robustez
- [ ] Validar heartbeat bidireccional watchdog y manejo de crashes.
- [ ] Detección de conflictos con MyASUS/G-Helper.
- [ ] Control aislado del hilo de GUI y timeouts de driver.
- [ ] Firma/versión verificada de la biblioteca OEM.

## F3 — UX
- [ ] Historial y gráficas RPM/temperatura.
- [ ] Perfiles personalizables validados contra sensores.
- [ ] Accesibilidad 125–200% y pruebas teclado.
- [ ] Empaquetado instalador firmado.

Nunca declarar compatibilidad real sin mediciones en el equipo físico.
