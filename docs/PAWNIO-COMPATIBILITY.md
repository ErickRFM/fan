# Compatibilidad con PawnIO (v1.0, F4)

**PawnIO** (de namazso) es un **driver de kernel firmado** que ejecuta pequeños módulos en un
intérprete *sandbox* y expone acceso controlado a operaciones privilegiadas (MSR de CPU, puertos de
E/S, PCI). Este documento resume para qué se usa, qué se puede y qué **no** se puede hacer con él en
FanForge, y por qué se mantiene **deshabilitado** en el M3500QC.

> Imagen firmada y software libre: <https://github.com/namazso/PawnIO>. Al ser un driver firmado, no
> requiere desactivar **Secure Boot**, **Smart App Control**, **WDAC** ni **HVCI**.

## 1. Cómo funciona

- El driver carga un **módulo** (`.bin`) que define funciones que el intérprete PawnIO ejecuta en modo
  kernel. El módulo declara qué operaciones realiza (leer/escribir MSR, puerto de E/S, PCI, memoria).
- El driver **valida y firma** la interfaz; los módulos se cargan solo desde ubicaciones de confianza y
  la superficie expuesta es mínima (una función `ioctl` genérica que alimenta el intérprete).
- Para escribir en el EC de un portátil habría que **escribir un módulo propio** que hable con el
  controlador embebido a bajo nivel.

## 2. Para qué lo usan otras herramientas

G-Helper usa PawnIO para:
- **Ryzen SMU** (undervolt / Curve Optimizer).
- **Intel MSR** (ajustes de CPU).

Es decir, acceso a **MSR/SMU de CPU**, **no** a ventiladores. No hay ningún módulo oficial de PawnIO
que controle el PWM del ventilador del M3500QC.

## 3. Restricción de este proyecto

Escribir un módulo PawnIO propio para tocar el EC del ventilador sería, en la práctica, **equivalente de
riesgo a un driver no oficial**:

- Aumenta la superficie de ataque en kernel (modo de mayor privilegio).
- Requiere ingeniería inversa del EC, no documentada por ASUS.
- Puede dejar el ventilador en un estado inseguro si falla.
- Va contra la política de FanForge (no forzar escrituras EC no documentadas, no usar drivers no
  oficiales).

Por eso, en FanForge **PawnIO se expone como backend deshabilitado y de solo lectura**: aparece en el
listado de backends (`--fan-backends`) para transparencia, pero:

- `DetectAsync` devuelve `Unsupported` para PWM y curvas.
- `SetSpeedAsync` / `RestoreAutomaticAsync` devuelven `Unsupported` y **no** ejecutan nada.
- No se envía ningún módulo ni se abre el driver.

## 4. Compatibilidad con seguridad de Windows

| Requisito | Estado |
| --- | --- |
| Secure Boot | **no** se desactiva |
| Smart App Control | **no** se desactiva |
| WDAC / HVCI / VBS | **no** se desactiva |
| Firma de kernel exigida | respetada (PawnIO está firmado) |

FanForge **no** pide desactivar ninguna de estas protecciones. Al no existir una operación de
ventilador validada, PawnIO permanece inerte.

## 5. Punto de extensión futuro

Si en el futuro apareciera un **módulo firmado y validado** que controle el ventilador del M3500QC de
forma segura, la integración seguiría el mismo contrato `IFanSpeedBackend` con PWM `Verified` **solo**
tras confirmación por RPM en hardware. Hasta entonces, el backend permanece deshabilitado.
