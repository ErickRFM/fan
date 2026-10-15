# Recuperación y auditoría de seguridad (v0.8)

Auditoría de los caminos de recuperación de FanForge y de lo que el programa **nunca** hace. Todo lo
descrito aquí es política verificable por tests simulados (`FanForge.Tests`), sin tocar hardware.

## Principio rector

FanForge solo modifica el registro de perfil (**VivoBookMode**, `0x00110019`) si puede **leer** el
valor activo antes. Nunca asume “Estándar”. Si no puede leer, **no escribe** (se preserva el estado
desconocido en vez de imponer uno). El original capturado se restaura exactamente, sea 0/1/2.

`ProfileGuard`:
- Captura el original **solo** cuando va a producirse un cambio real (`Apply`).
- Persiste el original (`%LocalAppData%\FanForge\original-profile.txt`) para sobrevivir un cierre
  inesperado.
- `--restore` y el watchdog arrancan con `recoverPersisted: true` (cargan el registro persistido).
- Una sesión interactiva normal arranca con `recoverPersisted: false`: un registro viejo **no** puede
  pisar un perfil cambiado en otra herramienta.

## Formato del registro persistido

` versión|cableado|ticks` → p. ej. `1|2|638000000000000000`. Versionado (`CurrentVersion = 1`),
wire mode en `0..2`, ticks UTC no negativos. Cualquier desviación se rechaza (`TryDecode` → false) y
se ignora; el fichero corrupto **no** bloquea una captura nueva.

## Catálogo de fallos y garantías

| Escenario | Comportamiento esperado | Cobertura |
| --- | --- | --- |
| Registro ilegible antes de un cambio | No se modifica nada (`OriginalUnavailable`). | test «read failure blocks modification” |
| Escritura rechazada o sin confirmación | Se conserva el original; **no** se persiste (`WriteRejected`). | “write rejection reported” |
| Restauración fallida | El registro persistido **se conserva** para reintentar (`WriteRejected`). | “failed restore keeps persisted original” |
| Cierre inesperado tras aplicar | `--restore`/watchdog recargan el original del disco y lo restauran. | “recovery restores original from disk” |
| El registro ya coincide con el original | No hay escritura; se limpia el registro (`Unchanged`). | “no write when firmware already matches…” |
| Registro ilegible durante la recuperación | Se conserva el registro para un intento posterior (`OriginalUnavailable`). | “unreadable register does not drop…” |
| Registro con valor fuera de rango (p. ej. `9`) | Se ignora, no se confía (`NotCaptured`). | “out-of-range persisted mode ignored” |
| Registro corrupto / versión desconocida / vacío | Se rechaza en el códec; no bloquea la captura limpia. | “codec rejects …”, “corrupted persisted record …” |
| Recuperación sin registro | No-op, sin escritura (`NotCaptured`). | “second recovery with empty store is a no-op” |
| Escritura sin confirmación de read-back | Se trata como rechazo; el original se conserva. | “reject failed readback even for balanced” |

## Escenarios de ciclo completo (simulados)

1. **Aplicar → crash → recuperar.** Sesión captura Whisper (1) al aplicar Rendimiento (2); un cierre
   inesperado deja el firmware en 2 y el registro en disco. La siguiente ejecución de recuperación
   vuelve a 1 y limpia el registro.
2. **Reaplicar tras fallo.** Una restauración fallida mantiene `Modified` y el registro, de modo que
   un reintento puede volver a intentarlo.

## Garantías de seguridad (lo que FanForge no hace)

- **No** desactiva ni puentea Smart App Control, WDAC ni Secure Boot.
- **No** modifica firmware/EC ni servicios/drivers de ASUS.
- **No** escribe PWM si el firmware no expone los ventiladores (`HealthyTable_FanCounts() = -1`).
- El registro persistido contiene **solo** `versión|wireMode|ticks`; no hay secretos, rutas de
  usuario ni datos personales.
- El CSV de telemetría (`docs/THERMAL-COMPARISON.md`) contiene marca de tiempo UTC, RPM,
  temperaturas y el nombre del perfil; ningún identificador de usuario.
- `--analyze` es **solo lectura de archivos**; nunca toca hardware.

## Limitaciones conocidas

- Si entre el crash y la recuperación el usuario cambia el perfil en otra herramienta, la
  recuperación restaura el original capturado por FanForge (revierte también ese cambio ajeno). Es el
  comportamiento seguro para deshacer la escritura de FanForge; el usuario puede reelegir su perfil.
- La validación física del ciclo de crash (matar el proceso durante una sesión real con permisos
  elevados) requiere autorización presencial y no se ejecuta en CI.
