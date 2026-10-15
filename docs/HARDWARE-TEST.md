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
| RPM reales vía ATKACPI | `--hardware-diagnose` elevado | **Verificado (CPU/GPU reales)** |
| Escritura de perfil ATKACPI | `--set-profile` + lectura posterior | **Verificado (3 perfiles, reversible)** |
| PWM 80% | Variación real en RPM | Pendiente (bloqueado por `AsusSAIO`) |
| PWM 100% | Variación real en RPM | Pendiente (bloqueado por `AsusSAIO`) |
| Volver a BIOS | Lecturas y comportamiento | Pendiente (bloqueado por `AsusSAIO`) |
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

## Telemetría alternativa ATKACPI — 2026-10-14 (v0.4, elevado)

Como la ruta HealthyTable está bloqueada, FanForge v0.4 incorpora el backend **ATKACPI**
(`\\.\ATKACPI`). La sonda multibackend de solo lectura confirma **RPM reales** incluso con el
driver `AsusSAIO` degradado:

```
Proveedores: AsusACPI (ATKACPI), ACPI thermal zone (WMI)
Estado: Supported   CPU/GPU=True/True
RPM: Cpu=4400..6100 / Gpu=3800..4000        (raw 44..61 / 38..40, unidad = 100 RPM)
Temperaturas: CPU=~90 (zona ACPI/WMI)  GPU=n/a
Control: perfiles=True  curvas=False  pwm=False
Raw VivoBookMode(0x00110019) = 131074     (soportado)
Raw PerformanceMode(0x00120075) = -65536   (no soportado)
Raw DevsCPUFanCurve/GPU = -65536           (curvas no soportadas)
```

Ejecutar con:

```powershell
.\dist\FanForge.exe --hardware-diagnose
```

Validación externa recomendada: comparar el orden de magnitud y la tendencia con
**ASUS ProArt Creator Hub** (que en este modelo muestra RPM y temperaturas).

### Escritura de perfil — VALIDADA en el equipo físico — 2026-10-15 (v0.4.7, elevado)

Autorizado por el usuario con la persona presente. El cambio de perfil ASUS (`--set-profile`)
se realiza por ATKACPI (`DEVS` sobre `VivoBookMode 0x00110019`) y se **verifica leyendo de nuevo**
el modo. Cada escritura devolvió `status=1` y `verificado=True`; se restauró al modo original:

```
Baseline / restaurado: VivoBookMode = 131074   (Performance, wire 2)
Whisper (Silent)  : raw 131074 -> 131073  status=1  verificado=True
Standard          : raw 131073 -> 131072  status=1  verificado=True
Performance       : raw 131072 -> 131074  status=1  verificado=True
```

Codificación confirmada: `raw = 0x20000 (131072) | wireMode`, con
`Silent=1`, `Standard=0`, `Performance=2`. RPM simultáneas: CPU 7000 → 6300, GPU 4300 → 4200
(CPU 77 °C, zona ACPI). La operación es completamente reversible y no toca EC crudo ni firmware.

### Robustez de lectura ATKACPI — 2026-10-15 (v0.4.7)

Se detectó que un método **no soportado** (p. ej. `Temp_CPU 0x00120094`) hace fallar
`DeviceIoControl`, lo que **invalida el handle para todas las lecturas posteriores**. Antes del
arreglo, `ReadAsync` perdía las RPM ya leídas al abortar en la lectura de temperatura (el gestor
descartaba la instantánea). Ahora cada endpoint se lee de forma tolerante (`SafeGet`): las RPM se
capturan primero y una temperatura fallida no las descarta. `--hardware-diagnose` reporta el error
por método en vez de abortar.

```
Raw CPU_Fan(0x00110013)=70    Raw GPU_Fan(0x00110014)=43    Raw VivoBookMode=131074
Raw Temp_CPU(0x00120094)= ERROR Win32Exception: ATKACPI DeviceIoControl failed
Raw Temp_GPU(0x00120097)= ERROR Win32Exception: ATKACPI DeviceIoControl failed
Raw PerformanceMode(0x00120075)= ERROR ...   Raw DevsCPUFanCurve/GPU = ERROR ...
```

Conclusión: en este BIOS los endpoints de **temperatura y curvas no son consultables** (la llamada
falla y deja el handle inservible); las **RPM y el modo VivoBook sí**. La temperatura se obtiene
por WMI (zona térmica ACPI).

### Perfil original — VALIDADA en el equipo físico — 2026-10-15 (v0.7, elevado, autorizado)

Antes de cambiar de perfil, FanForge captura el perfil ASUS activo y lo **restaura exactamente**
(no asume Estándar). Prueba con persona presente:

```
Inicial                      : VivoBookMode = 131074  (Performance, wire 2)
--set-profile Standard       : OK: Perfil Standard aplicado y confirmado (wire 2 -> 0); original 2 guardado.
--restore                    : Perfil ASUS: OK: Perfil original restaurado (wire 0 -> 2).
Final                        : VivoBookMode = 131074  (Performance, wire 2)
```

El PWM manual no se restauró porque `HealthyTable_FanCounts() = -1` (esperado). El original se guarda
en `%LocalAppData%\FanForge\original-profile.txt`, de modo que `--restore` y el watchdog pueden devolver
el equipo a su perfil tras un cierre inesperado. Operación **reversible**, solo ATKACPI.

### Estado actual del driver ASUS — revisar antes de atribuir a «degradado» — 2026-10-15

El 2026-10-15 el driver **`AsusSAIO` figura `Running` / `OK`** (`StartMode = Manual`) y el dispositivo
«ASUS System Control Interface v3» tiene `Error=0` (`--diagnose-driver`, solo lectura). `AsusOptimization`
figura `Running` / `Auto`. Aun así, `HealthyTable_FanCounts()` sigue devolviendo `-1`: en este modelo/BIOS
el firmware **no expone ventiladores** por esa vía, por lo que el PWM manual está **bloqueado por diseño**,
no necesariamente por un driver degradado. La rasquiña de `-1` descrita abajo sigue siendo válida cuando
el driver sí está degradado, pero no es la única causa posible.

## Causa probable (documentada, no modificada)

El driver de kernel **`AsusSAIO`** (el que sirve los datos a `AsusWinIO64.dll`) está en estado **`Stop Pending` y `Degraded`** (`sc query AsusSAIO`, `driverquery /v`). Con el driver degradado, la biblioteca OEM inicializa pero no puede leer el EC, por lo que devuelve `-1`.

Esto coincide con la regresión conocida del proyecto de referencia MIT [Karmel0x/AsusFanControl issue #44](https://github.com/Karmel0x/AsusFanControl/issues/44): tras ciertas actualizaciones del **ASUS System Control Interface / AsusSAIO**, todas las funciones devuelven `0` o `-1`, incluso el «Diagnóstico de ventilador» de MyASUS deja de funcionar.

## Próximos pasos recomendados (acción del usuario; FanForge no los ejecuta)

1. Reiniciar Windows (suele limpiar un driver kernel atascado en `Stop Pending`) y repetir `dist\FanForge.exe --probe`.
2. Comprobar que el **Diagnóstico de ventilador de MyASUS** funcione. Si no funciona, la API OEM está rota a nivel de driver y ninguna aplicación puede controlar los ventiladores hasta que ASUS lo corríja.
3. Si tras reiniciar sigue en `-1` y MyASUS tampoco funciona, considerar **revertir el driver «ASUS System Control Interface»** a una versión anterior (Administrador de dispositivos → Revertir controlador) y reiniciar. Es el workaround conocido de la comunidad; lo decide y ejecuta el usuario.

FanForge **no** escribe registros EC, no revierte drivers, no modifica firmware ni desactiva protecciones térmicas.

## v0.8 — Analizador y empaquetado (no requiere hardware)

- `--analyze` es solo lectura de archivos: no ejecuta pruebas contra el equipo.
- La verificación MSIX (`scripts/verificar-msix.ps1`) no toca hardware.
- La firma del MSIX es un paso manual autorizado y no se realiza en esta sesión.

