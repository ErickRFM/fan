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
| FanCount y RPM automático | `--probe` en consola elevada | **Bloqueado: API devuelve -1** |
| PWM 80% | Variación real en RPM | Pendiente (bloqueado) |
| PWM 100% | Variación real en RPM | Pendiente (bloqueado) |
| Volver a BIOS | Lecturas y comportamiento | Pendiente (bloqueado) |
| Recuperación watchdog | Resultado en PC físico | Pendiente |

## Hallazgo físico — 2026-10-14 (elevado)

La sonda de solo lectura (`dist\FanForge.exe --probe`, ejecutada elevada) devolvió:

```
Modelo: Vivobook_ASUSLaptop M3500QC_D3500QC
BIOS: M3500QC.316
DLL OEM: ...\asussci2.inf_amd64_197d319fab43e0fe\...\AsusWinIO64.dll   (v1.1.4.0)
ERROR: NotSupportedException: Invalid fan count returned by ASUS: -1
```

Una prueba aislada y de solo lectura contra **ambas** versiones de `AsusWinIO64.dll` (1.1.4.0 y 1.1.2.0) dio el mismo patrón de sentinelas:

```
InitializeWinIo()          => 0
HealthyTable_FanCounts()   => -1
HealthyTable_FanRPM()      => -1
Thermal_Read_Cpu_Temperature() => 2147483647 (0x7FFFFFFF)
```

## Causa probable (documentada, no modificada)

El driver de kernel **`AsusSAIO`** (el que sirve los datos a `AsusWinIO64.dll`) está en estado **`Stop Pending` y `Degraded`** (`sc query AsusSAIO`, `driverquery /v`). Con el driver degradado, la biblioteca OEM inicializa pero no puede leer el EC, por lo que devuelve `-1`.

Esto coincide con la regresión conocida del proyecto de referencia MIT [Karmel0x/AsusFanControl issue #44](https://github.com/Karmel0x/AsusFanControl/issues/44): tras ciertas actualizaciones del **ASUS System Control Interface / AsusSAIO**, todas las funciones devuelven `0` o `-1`, incluso el «Diagnóstico de ventilador» de MyASUS deja de funcionar.

## Próximos pasos recomendados (acción del usuario; FanForge no los ejecuta)

1. Reiniciar Windows (suele limpiar un driver kernel atascado en `Stop Pending`) y repetir `dist\FanForge.exe --probe`.
2. Comprobar que el **Diagnóstico de ventilador de MyASUS** funcione. Si no funciona, la API OEM está rota a nivel de driver y ninguna aplicación puede controlar los ventiladores hasta que ASUS lo corríja.
3. Si tras reiniciar sigue en `-1` y MyASUS tampoco funciona, considerar **revertir el driver «ASUS System Control Interface»** a una versión anterior (Administrador de dispositivos → Revertir controlador) y reiniciar. Es el workaround conocido de la comunidad; lo decide y ejecuta el usuario.

FanForge **no** escribe registros EC, no revierte drivers, no modifica firmware ni desactiva protecciones térmicas.

