# Empaquetado MSIX (v0.8, base)

Este documento describe la **base de empaquetado MSIX** de FanForge. No afirma que un paquete sin
firma vaya a ser aceptado por Windows: un `.msix` **sin firmar no se instala** y no evita Smart App
Control (SAC) ni WDAC. La firma con un certificado de confianza **requiere que el usuario configure el servicio y su identidad**;
la integración segura automatizada está preparada en `.github/workflows/signed-release.yml`,
pero no se emiten firmas hasta contar con un perfil público válido.

## Qué hay en el repositorio

| Elemento | Rol |
| --- | --- |
| `packaging/msix/AppxManifest.xml` | Manifiesto (identidad x64, versión de 4 partes, entrada *full trust*). |
| `packaging/msix/Assets/*.png` | Logos placeholder (`FF`) generados por script, no identidad de marca. |
| `scripts/New-MsixAssets.ps1` | Genera los logos de forma reproducible (sin binarios a mano). |
| `scripts/verificar-msix.ps1` | Verifica la **estructura** del manifiesto y los recursos. Sin SDK de empaquetado. |
| `scripts/empaquetar-msix.ps1` | Publica, prepara el *layout*, verifica y (si hay SDK) empaqueta sin firmar; también permite `-SkipPublish -SourceDir` para usar un EXE firmado y `-Publisher` para alinear el manifiesto al certificado. |
| `.github/workflows/signed-release.yml` | Pipeline **manual** que firma EXE y MSIX únicamente después de configurar Microsoft Artifact Signing y superar validaciones. |
| `FanForge.Core/MsixPackaging.cs` | Validación del manifiesto como política comprobable por tests. |

## Diseño del manifiesto

- `Identity` `ProcessorArchitecture="x64"` (coincide con `RuntimeIdentifier` `win-x64`).
- `Version` de cuatro partes (`0.8.0.0`); el script la sustituye con `-Version`.
- `Application` `EntryPoint="Windows.FullTrustApplication"` (WinForms necesita *full trust*).
- Capacidad `runFullTrust` (restringida: documentar si se publica en Store).
- `TargetDeviceFamily=Windows.Desktop` (mínimo `10.0.19041.0`).

## Flujo reproducible

```powershell
# 1) Solo comprobar estructura (lo que corre la CI)
./scripts/New-MsixAssets.ps1
powershell -NoProfile -File scripts/verificar-msix.ps1

# 2) Preparar layout + empaquetar (requiere Windows SDK para MakeAppx.exe)
./scripts/empaquetar-msix.ps1 -Version 0.8.0.0
```

Resultado:
- Sin `MakeAppx.exe`: queda el *layout* en `dist/msix/layout` (publicación + manifiesto + assets).
- Con `MakeAppx.exe`: además `dist/msix/FanForge.msix` **sin firmar**.

## Firma mediante GitHub Actions (requiere credenciales de usuario)

El pipeline usa OIDC con Azure Artifact Signing para firmar primero el EXE publicado,
extraer el sujeto real del certificado, construir el manifiesto MSIX con **ese mismo Publisher**,
firmar el paquete y verificar ambas firmas. Si algo falla, no publica artefactos.

El flujo se habilita mediante GitHub Environment y credenciales reales del usuario;
consulta [WINDOWS-SIGNING.md](WINDOWS-SIGNING.md) para el alta y las variables.
El MSIX generado manualmente mediante `empaquetar-msix.ps1` sigue **sin firma**
hasta que se complete un flujo de firma válido.

## Firma manual alternativa

Un MSIX solo se instala si su firma encadena a una CA de confianza del equipo. Opciones legítimas,
ninguna habilitada aquí:

1. **Certificado de desarrollo propio** instalado en `Trusted People` (pruebas locales). No sirve
   para distribución.
2. **Certificado OV/EV** de una CA pública (identidad validada). De pago.
3. **Azure Trusted Signing / Artifact Signing** (firma gestionada por Microsoft).

```powershell
# Ejemplo MERAMENTE ilustrativo; requiere un .pfx y su contraseña (no incluidos).
signtool sign /fd SHA256 /a /f mi-certificado.pfx /p <password> dist/msix/FanForge.msix
```

## Relación con Smart App Control / WDAC

- SAC solo confía en paquetes firmados por editor de confianza o con reputación; un MSIX sin firmar
  **no** ayuda.
- **No** se desactivan SAC, WDAC ni Secure Boot para instalar nada.
- Alternativa provisional ya documentada en `docs/WINDOWS-SIGNING.md`: ejecutar con el host
  firmado `dotnet.exe` mientras se gestiona la firma real.

## Limitaciones conocidas

- El manifiesto fuente usa un `Publisher` placeholder (`CN=FanForge Development`);
  el flujo de firma lo reemplaza con el **sujeto real**. El empaquetado manual
  debe pasar `-Publisher` si utiliza otro certificado.
- Los logos son placeholders neutros.
- El *layout* autónomo es grande (self-contained); una variante framework-dependent requiere .NET 8
  en el equipo.

## Firma local cuando Artifact Signing Public Trust no está disponible

En países no habilitados para Artifact Signing Public Trust, un certificado OV
emitido por una CA pública con clave protegida puede firmar un paquete MSIX.
El comando `scripts/firmar-distribucion-local.ps1 -CertificateThumbprint <40-hex>`
publica, firma el EXE, incorpora su sujeto como Publisher, empaqueta, firma el
MSIX y verifica ambos con SignTool. Requisitos en [WINDOWS-SIGNING.md](WINDOWS-SIGNING.md).
No distribuyas el MSIX sin haber verificado su firma real en Windows.
