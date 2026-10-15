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

## Pruebas antes de disponer de una firma

El código se puede compilar y probar en CI con tests lógicos. Un
`FanForge.exe` sin firma **no** debe presentarse como ejecutable
confiable ni forzar su apertura en Windows con Smart App Control activo.
No recomendamos usar otros ejecutables firmados como anfitrión
para intentar sortear la política. Para distribución y validación en
el equipo físico, emplea el flujo de firma pública de este documento.

## Importante: México y disponibilidad de Artifact Signing Public Trust

Según los [requisitos oficiales de Microsoft](https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart),
a octubre de 2026 **México no está en la lista de países habilitados para
identidades Public Trust** de Artifact Signing. Para desarrolladores individuales,
Public Trust se limita a Estados Unidos y Canadá. Los perfiles `Private Trust`
no sustituyen una firma de confianza pública para Smart App Control.

Si tu identidad de facturación y residencia no cumplen los requisitos,
**NO intentes registrar una identidad extranjera ficticia**. El workflow OIDC
es una opción disponible para quienes sí cumplan los requisitos; para México
puedes usar la siguiente alternativa con una autoridad de certificación.

## Alternativa: certificado OV de CA y firma local protegida

Adquiere legítimamente un certificado de firma de código **OV** de una autoridad
reconocida y utiliza su clave en token/HSM o proveedor seguro compatible con
Windows Certificate Store. No exportes ni subas PFX/clave privada a GitHub.
El script `scripts/firmar-distribucion-local.ps1` toma el thumbprint del
certificado que YA tengas instalado y firma EXE y MSIX con ese mismo editor,
verificando ambas firmas antes de crear ZIP/MSIX y hashes.

```powershell
# Visualizar certificados de firma disponibles; no muestra claves privadas
Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
  Select-Object Subject, Thumbprint, NotAfter, HasPrivateKey

# Solo DESPUÉS de instalar tu certificado público de firma válido:
.\scripts\firmar-distribucion-local.ps1 -CertificateThumbprint 'TU_THUMBPRINT_40_HEX' -Version '0.9.1.0'
```

Necesitas SDK de Windows (SignTool, MakeAppx), .NET 8 y acceso al dispositivo
criptográfico (puede solicitar PIN). Si el certificado está en
`Cert:\LocalMachine\My`, pasa `-MachineStore`. Esto no es gratis: los
certificados OV públicos suelen tener coste y verifican la identidad del editor.
Nunca añadas certificados personales a Trusted Root para simular confianza pública.

## CI de firma confiable (GitHub Actions, preparado en esta rama)

FanForge incorpora `.github/workflows/signed-release.yml`, un flujo **manual**
(`Actions → FanForge signed Windows distribution → Run workflow`) que solo firma
desde `main`. Por seguridad, usa el environment `fanforge-signing` (se recomienda
exigir aprobaciones), acceso GitHub `id-token: write` para federación OIDC, y
**nunca** se ejecuta sobre Pull Requests. Solo publica los artefactos si pasan
las pruebas, la firma y la verificación.

### Requisitos que debes configurar por tu cuenta

1. Contratar o activar Microsoft **Artifact Signing** con un perfil de certificado de
   firma de código de **confianza pública**, sujeto a disponibilidad y requisitos
   de identidad por país y tipo de cuenta. La cuenta del usuario debe ser elegible;
   no se puede crear aquí una identidad ficticia.
2. Crear en Microsoft Entra una app con **Federated Credential** para el repositorio
   `ErickRFM/fan`, con subject
   `repo:ErickRFM/fan:environment:fanforge-signing`, audience
   `api://AzureADTokenExchange`; asignarle el rol
   **Artifact Signing Certificate Profile Signer** en el perfil correspondiente.
3. En GitHub, crear Environment `fanforge-signing`, con aprobación requerida
   si tu plan permite esa protección, y acceso limitado a `main`.
   Añadir sus **secrets**:

   - `AZURE_CLIENT_ID` — aplicación Entra de la federación
   - `AZURE_TENANT_ID`
   - `AZURE_SUBSCRIPTION_ID`
   - `AZURE_SIGNING_ENDPOINT` — URL regional (ej. `https://eus.codesigning.azure.net/`)
   - `AZURE_SIGNING_ACCOUNT` — nombre de la cuenta
   - `AZURE_CERT_PROFILE` — nombre del perfil

   Se usan para autenticación OIDC, **sin client secret, PFX ni claves privadas**.
4. Después del merge, abrir Actions, elegir `FanForge signed Windows distribution`,
   seleccionar rama `main`, introducir versión de **cuatro cifras**
   (por ejemplo `0.9.1.0`) y ejecutar. La firma **no funciona** sin completar
   los requisitos anteriores.

El pipeline prueba FanForge, publica un `FanForge.exe` autónomo, firma
con `azure/artifact-signing-action@v2`, verifica la firma localmente,
genera el MSIX con su **Publisher real extraído del certificado**, firma
el MSIX, verifica ambas firmas con Windows SDK SignTool y publica
`FanForge-signed-win-x64.zip`, `FanForge.msix` y `SHA256SUMS.txt`.
Un error cancela la publicación: **no hay fallback a distribución sin firma**.
Los artefactos quedan disponibles en la ejecución de GitHub Actions,
no se publican automáticamente como GitHub Release.

Validación del EXE descargado en PowerShell:

```powershell
Get-AuthenticodeSignature .\FanForge.exe | Format-List Status,StatusMessage,SignerCertificate
.\scripts\verificar-firma.ps1 -Path .\dist\signed\FanForge.exe
```

La verificación del usuario sigue siendo imprescindible: que la firma
sea válida **no garantiza** compatibilidad con todas las políticas corporativas
WDAC, confianza de SmartScreen inmediata ni funcionalidad PWM del M3500QC.
No se desactivan Smart App Control, WDAC ni Secure Boot.

Documentación del proveedor:
https://github.com/Azure/artifact-signing-action
https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control

## Plan recomendado

1. **Ahora:** ejecutar tests y validación estructural en CI; no sortear Smart App Control.
2. **Cuando esté disponible el servicio y haya identidad/certificado:** configurar los secrets OIDC y ejecutar manualmente el workflow de firma indicado arriba. Mantener una identidad de firma consistente para acumular reputación.
3. **Alternativa de distribución:** publicar como **MSIX** (Store o distribución firmada) para no
   depender de un certificado propio.
4. **Verificación:** probar la firma contra SAC en modo auditoría antes de distribuir, cubriendo todos
   los binarios del instalador y desinstalador.

## Lo que NO haremos

- No desactivar SAC/WDAC/Secure Boot.
- No añadir exclusiones no soportadas ni "bypass" de reputación.
- No distribuir binarios sin firma como solución permanente.
