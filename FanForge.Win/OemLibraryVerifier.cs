using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FanForge.Core;

namespace FanForge;

/// <summary>Reads the OEM library version and Authenticode signer for logging (no native load).</summary>
internal static class OemLibraryVerifier
{
    public static (Version? Version, string? Signer, bool Trusted) Inspect(string dllPath)
    {
        Version? version = Version.TryParse(FileVersionInfo.GetVersionInfo(dllPath).FileVersion, out var parsed)
            ? parsed
            : null;
        string? signer = null;
        try
        {
            using var certificate = X509Certificate.CreateFromSignedFile(dllPath);
            signer = certificate.Subject;
        }
        catch (CryptographicException) { }
        catch (ArgumentException) { }
        return (version, signer, OemLibraryPolicy.TrustedSigner(signer));
    }
}
