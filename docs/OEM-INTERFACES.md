# Interfaces OEM de ventilador/EC en el ASUS VivoBook M3500QC

Documento de investigación **de solo lectura**. FanForge nunca escribe registros EC desconocidos ni
fuerza protocolos no verificados. Cualquier vía de PWM manual solo se habilitaría tras validación
física presencial y autorizada.

## Estado verificado en este equipo (2026-10-15)

| Vía | Estado | Evidencia |
|---|---|---|
| **ATKACPI perfiles** (`VivoBookMode 0x00110019`) | ✅ Escritura y lectura | Silent/Standard/Performance confirmados por relectura y reversibles. |
| **ATKACPI RPM** (`CPU_Fan 0x00110013`, `GPU_Fan 0x00110014`) | ✅ Lectura | RPM reales (~6900 / ~4500). |
| **ATKACPI temperaturas** (`Temp_CPU/GPU 0x120094/97`) | ❌ | El IOCTL *falla* (no solo devuelve sentinela); temperatura vía WMI. |
| **ATKACPI curvas/rendimiento** (`0x00110024/25`, `0x00120075`) | ❌ | El IOCTL falla en este BIOS. |
| **HealthyTable / AsusSAIO** (PWM manual) | ⛔ Bloqueado | Driver `Running/OK` pero `HealthyTable_FanCounts()` = −1 (el firmware no expone ventiladores por esta vía). |
| **ACPI thermal zone** (`root\WMI:MSAcpi_ThermalZoneTemperature`) | ✅ Lectura | Zona de chasis aproximada (~77–90 °C). |

## Interfaces consideradas

### 1. ATKACPI (`\\.\ATKACPI`, IOCTL `0x0022240C`)
Es la vía que usa FanForge. En el M3500QC solo expone de forma **operativa** RPM y el selector de
perfil VivoBook. Los endpoints de curvas, PWM directo y temperaturas por ACPI devuelven error en la
llamada `DeviceIoControl` para este BIOS, por lo que **no hay PWM manual verificado** por aquí.

### 2. ASUS WMI (`AsusAtkWmi_WMNB` / `ATKWMI`)
Muchas gamas ASUS exponen el control de ventilador mediante el método WMI `DEVS`/`DSTS` sobre un
dispositivo ACPI WMI. En este modelo los equivalentes ya se probaron vía ATKACPI y **no** responden
para curvas/PWM. No se localizó ninguna interfaz WMI adicional documentada y verificada para PWM
manual en el M3500QC; no se fuerza ningún GUID ni método no documentado.

### 3. ASUS System Control Interface / `AsusSAIO` (HealthyTable)
El paquete «ASUS System Control Interface» instala `AsusWinIO64.dll` y el driver `AsusSAIO`, que
ofrecen las funciones `HealthyTable_*` usadas por la ruta de PWM manual de FanForge. Estado medido el
2026-10-15 (`--diagnose-driver`): el driver `AsusSAIO` figura `Running/OK` y el dispositivo «ASUS
System Control Interface v3» con `Error=0`; **sin embargo** `HealthyTable_FanCounts()` devuelve −1.
Es decir, el driver carga correctamente pero el firmware **no expone** los ventiladores por esta vía
en el M3500QC, por lo que el PWM manual queda **bloqueado por diseño**. FanForge no reinstala ni
revierte drivers ni toca el EC.

### 4. ACPI thermal zone (WMI)
Solo temperatura, solo lectura. Es la única fuente de temperatura disponible cuando ATKACPI falla.

## Diagnóstico seguro

```powershell
& "C:\Program Files\dotnet\dotnet.exe" "$PWD\FanForge.Win\bin\Release\net8.0-windows\win-x64\FanForge.dll" --diagnose-driver
```

Informa el estado de `AsusSAIO`, `AsusOptimization`, `AsIO3/AsIO2` y los dispositivos ASUS/ATK
enumerados por WMI, **sin** iniciar/detener/reconfigurar nada.

## Conclusión

En el M3500QC no existe una vía OEM oficial **verificada** de PWM manual accesible sin el driver
`AsusSAIO` operativo. FanForge mantiene el PWM manual deshabilitado, ofrece perfiles ASUS verificados y
lectura de RPM/temperatura, y **no** escribirá registros EC desconocidos ni forzará protocolos no
documentados para sortear esta limitación.
