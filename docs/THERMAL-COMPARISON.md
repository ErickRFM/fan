# Comparación térmica reproducible (FanForge vs ASUS ProArt Creator Hub)

Objetivo: comprobar, con evidencia reproducible, que los perfiles ASUS que FanForge aplica por
ATKACPI producen el mismo comportamiento observado que el panel oficial, **sin** ejecutar PWM manual
ni pruebas de estrés no autorizadas.

> Este procedimiento es **de solo lectura** por parte de FanForge (salvo el cambio explícito de
> perfil, ya validado). No fuerza cargas, no toca el EC y no modifica drivers.

## Distinción clave

| Concepto | Dónde vive | Qué significa |
|---|---|---|
| **Cambio de perfil confirmado** | columna `profile` + `profile_confirmed` | La escritura ATKACPI se aceptó y la relectura del registro coincide. |
| **Aumento de velocidad observado** | columnas `cpu_rpm` / `gpu_rpm` | Las RPM *medidas* cambiaron respecto a la línea base. |

Un perfil confirmado **no** implica por sí solo un aumento de RPM: pueden cambiar otras variables
(temperatura, histéresis del firmware, carga real). Por eso el CSV separa ambos conceptos.

## Formato CSV

Cabecera:

```
timestamp_utc,cpu_rpm,gpu_rpm,cpu_celsius,gpu_celsius,profile,profile_confirmed,note
```

- `timestamp_utc`: ISO-8601 en UTC (`...Z`).
- `cpu_rpm`/`gpu_rpm`: enteros o vacío si no hubo lectura válida.
- `cpu_celsius`/`gpu_celsius`: temperatura de zona ACPI/WMI o vacío.
- `profile`: `Standard`, `Whisper`, `Performance`, `original`, `auto`, `unknown`.
- `profile_confirmed`: `true`/`false`/vacío (confirmado por relectura, no por intención).
- `note`: texto libre (marcas de error o eventos puntuales).

## Grabación (línea base)

1. Cierra herramientas ASUS de control (MyASUS/Armoury Crate) para no competir por el perfil.
2. Mantén el mismo estado de energía (conectado a la red) y la misma carga en ambos equipos.
3. Graba 120 s en reposo con el perfil que ya estaba activo:

```powershell
# host .NET autorizado mientras no exista un ejecutable firmado (ver WINDOWS-SIGNING.md)
& "C:\Program Files\dotnet\dotnet.exe" "$PWD\FanForge.Win\bin\Release\net8.0-windows\win-x64\FanForge.dll" --log-telemetry 120
```

4. Al terminar, el CSV queda en `%LocalAppData%\FanForge\logs\telemetry-AAAAAMMDD-HHMMSS.csv`.

## Comparación con ProArt Creator Hub

1. Abre ProArt Creator Hub y sitúalo junto a FanForge.
2. Fija los mismos perfiles en ambos: uno por uno (Silent, Standard, Performance).
3. Para cada perfil, anota cada 10 s el RPM que muestra ProArt y el que muestra FanForge, junto con
   la temperatura. Usa la plantilla:

| Tiempo (s) | Perfil | ProArt CPU RPM | FanForge CPU RPM | ProArt GPU RPM | FanForge GPU RPM | Temp °C |
|---|---|---|---|---|---|---|
| 0 | Standard | | | | | |
| 10 | Standard | | | | | |
| ... | | | | | | |

4. Repite la grabación CSV de 120 s una vez por perfil y compara la **tendencia** (no valores
   instantáneos): dirección y orden de magnitud deben coincidir.

## Criterio de éxito

- `profile_confirmed=true` para cada perfil aplicado (ya verificado físicamente el 2026-10-15).
- Las RPM de FanForge siguen la misma tendencia que ProArt entre perfiles.
- Ninguna fila con `error` ni escrituras inesperadas de PWM en el log de la CLI.

## Bloqueos conocidos

- PWM manual 70–100% no disponible (`AsusSAIO` degradado); por eso la comparación se limita a perfiles.
- No se autoriza prueba de estrés térmico sin presenciación del usuario.
