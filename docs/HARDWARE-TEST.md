# Validación F1 en ASUS VivoBook Pro M3500QC

**No ejecutar pruebas a ciegas ni dejar el modo de prueba desatendido.**

## Preparación (solo lectura)

1. Conectar el cargador, despejar rejillas y cerrar otros controladores de ventilación (MyASUS, Armoury Crate, G-Helper).
2. Ejecutar el diagnóstico informativo y anotar modelo, versión BIOS, controlador ASUS y DLL detectada:

   ```powershell
   .\scripts\diagnostico.ps1
   ```

3. En una consola **elevada**, ejecutar la sonda de solo lectura y anotar ventiladores, RPM y CPU:

   ```powershell
   .\dist\FanForge.exe --probe
   ```

   Detenerse si el conteo de ventiladores es inválido o las RPM no son números plausibles.

## Prueba supervisada de PWM (requiere persona presente)

4. Abrir la app elevada (`.\dist\FanForge.exe`) y capturar RPM basales en AUTO.
5. Confirmar cargador conectado y que no hay otro controlador manual activo. Aceptar el diálogo de confirmación.
6. Pulsar **TODOS 80%** durante ~10 segundos y comprobar variación real de RPM y ruido.
7. Pulsar **VOLVER A BIOS** y confirmar retorno al comportamiento automático.
8. Solo si el paso anterior fue correcto, pulsar **TODOS 100%** durante ~10 segundos, medir RPM y restaurar con **VOLVER A BIOS**.
9. Cuando lo anterior funcione, probar suspender, cerrar la app y recuperación por watchdog.

Detener inmediatamente ante RPM cero anómalas, bloqueos, temperaturas excesivas, olor a quemado o ruido mecánico; pulsar **VOLVER A BIOS**. No escribir puertos EC arbitrarios, modificar firmware, sustituir drivers firmados, ni desactivar seguridad térmica.

## Resultados

| Verificación | Evidencia requerida | Estado |
|---|---|---|
| Modelo M3500QC y BIOS | Salida diagnóstico (`M3500QC.316`) | Verificado (info) |
| Versión más reciente de `AsusWinIO64.dll` | Salida diagnóstico | Verificado (info, 1.1.4.0) |
| FanCount y RPM automático | `--probe` en consola elevada | Pendiente (requiere elevación) |
| PWM 80% | Variación real en RPM | Pendiente (física) |
| PWM 100% | Variación real en RPM | Pendiente (física) |
| Volver a BIOS | Lecturas y comportamiento | Pendiente (física) |
| Recuperación watchdog | Resultado en PC físico | Pendiente (física) |
