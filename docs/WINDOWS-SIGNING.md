# Firma del ejecutable en Windows 11 con Smart App Control y WDAC

Estado de la investigación (2026). **FanForge nunca desactiva ni elude** Smart App Control (SAC),
WDAC, Secure Boot ni ninguna política de seguridad. Este documento solo recoge opciones **legítimas**
de firma y el método provisional autorizado mientras no exista un ejecutable firmado.

## Por qué el `.exe` recién compilado no arranca

En este equipo Smart App Control está en modo **aplicación** (`VerifiedAndReputablePolicyState=1`) y
existen políticas WDAC activas en `C:\Windows\System32\CodeIntegrity\CiPolicies\Active`. Un binario
sin firma de confianza lanzado directamente se bloquea con:

```
Una directiva de Control de aplicaciones bloqueó este archivo.
(Win32Exception 4551)
```

Según Microsoft, Smart App Control deja ejecutar una app si (a) su servicio en la nube la considera
segura, o (b) tiene una **firma válida emitida por un proveedor de confianza**. Un binario sin firma
o con firma inválida se considera no confiable y se bloquea. **No existe forma soportada de eximir una
app concreta** de SAC sin desactivarlo.

## Opciones legítimas

| Opción | Coste aprox. | Notas |
|---|---|---|
| **Azure Artifact Signing** (antes *Trusted Signing*) | ~9,99 $/mes | Recomendada por Microsoft para distribución fuera de la Store. Sin token físico; se integra con CI/CD. Organizaciones en EE. UU., Canadá, UE y Reino Unido; particulares solo EE. UU. y Canadá. La reputación de SmartScreen se acumula con el tiempo. |
| **Certificado OV** (DigiCert, Sectigo…) | ~150–300 $/año | Firma Authenticode estándar. La reputación se acumula; un binario nuevo aún muestra aviso hasta ganar historial. |
| **Certificado EV** | 400 $+/año | Desde 2024 **ya no** exime de SmartScreen al instante; mismo comportamiento que OV. Solo tiene sentido por validación de identidad/empresa. |
| **Microsoft Store (MSIX)** | Cuenta de desarrollador | Microsoft **re-firma** el paquete tras la certificación; no se necesita certificado de CA propio. Distribución vía Store. |
| **Política WDAC propia (empresa)** | — | En entornos gestionados, el administrador puede autorizar binarios firmados por su identidad mediante una política firmada. Requiere administración, no es un atajo. |

Un certificado **autofirmado** no es de confianza para Windows por defecto y provoca un bloqueo fuerte
de SmartScreen: **no sirve** para distribución pública.

## Método provisional autorizado (el que usa este repo)

Mientras no exista un ejecutable firmado y aceptado, las pruebas permitidas se ejecutan sobre el
**host de .NET ya firmado y de confianza**:

```powershell
& "C:\Program Files\dotnet\dotnet.exe" `
  "FanForge.Win\bin\Release\net8.0-windows\win-x64\FanForge.dll" --hardware-diagnose
```

`dotnet.exe` está firmado por Microsoft y es de confianza; carga nuestro ensamblado administrado
`FanForge.dll`. Esto permite validar toda la lógica (telemetría, perfiles, CSV, diagnóstico) **sin**
firmar ni desactivar nada. Los comandos expuestos por la CLI (`--probe`, `--hardware-diagnose`,
`--set-profile`, `--log-telemetry`, `--restore`) son los mismos que usa la GUI.

## Plan recomendado

1. **Ahora:** seguir con el host `dotnet.exe` para las pruebas autorizadas.
2. **Cuando haya presupuesto:** alta en **Azure Artifact Signing** y firmar el `.exe` en CI
   (GitHub Actions). Mantener una identidad de firma consistente para acumular reputación.
3. **Alternativa de distribución:** publicar como **MSIX** (Store o distribución firmada) para no
   depender de un certificado propio.
4. **Verificación:** probar la firma contra SAC en modo auditoría antes de distribuir, cubriendo todos
   los binarios del instalador y desinstalador.

## Lo que NO haremos

- No desactivar SAC/WDAC/Secure Boot.
- No añadir exclusiones no soportadas ni "bypass" de reputación.
- No distribuir binarios sin firma como solución permanente.
