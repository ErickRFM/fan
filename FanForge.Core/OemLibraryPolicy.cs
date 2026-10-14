namespace FanForge.Core;

/// <summary>
/// Fail-closed rules for deciding whether an OEM DLL may be loaded.
/// The signature's cryptographic and local chain validation occurs in the Windows host.
/// Subject matching alone is never a security verification.
/// </summary>
public static class OemLibraryPolicy
{
    public static readonly Version MinimumVersion = new(0, 0, 0, 1);

    public static bool VersionSupported(Version? version) =>
        version is not null && version >= MinimumVersion;

    /// <summary>
    /// Accept exact known ASUS signer common names, not misleading substrings such as
    /// "Untrusted ASUS Helper" or an arbitrary company name containing ASUS.
    /// </summary>
    public static bool TrustedSigner(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return false;
        foreach (string part in subject.Split(','))
        {
            string field = part.Trim();
            if (!field.StartsWith("CN=", StringComparison.OrdinalIgnoreCase)) continue;
            string name = field.Substring(3).Trim().Trim('"');
            return name.Equals("ASUSTeK COMPUTER INC.", StringComparison.OrdinalIgnoreCase)
                || name.Equals("ASUS", StringComparison.OrdinalIgnoreCase)
                || name.Equals("ASUS COMPUTER INTERNATIONAL", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    /// <summary>Requires genuine Authenticode trust, recognized ASUS signer and supported version.</summary>
    public static bool MayLoad(Version? version, string? subject, bool authenticodeValid) =>
        VersionSupported(version) && authenticodeValid && TrustedSigner(subject);

    public static string Describe(Version? version, string? signerSubject)
    {
        string v = VersionSupported(version) ? version!.ToString() :
            (version?.ToString() ?? "desconocida") + " (no soportada)";
        string s = string.IsNullOrWhiteSpace(signerSubject) ? "sin firmante legible"
            : (TrustedSigner(signerSubject) ? "firmante ASUS reconocido: " : "firmante no autorizado: ") + signerSubject;
        return "versión " + v + "; " + s +
            "; el nombre del firmante no prueba la validez criptográfica de Authenticode";
    }
}
