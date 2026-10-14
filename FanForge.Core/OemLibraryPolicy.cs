namespace FanForge.Core;

/// <summary>
/// Validation rules for the OEM fan library (AsusWinIO64.dll). Pure so the decision can be
/// unit-tested without loading any native code; the file/signature read happens in FanForge.Win.
/// </summary>
public static class OemLibraryPolicy
{
    /// <summary>Lowest file version we accept; anything older is treated as unknown/stale.</summary>
    public static readonly Version MinimumVersion = new(0, 0, 0, 1);

    public static bool VersionSupported(Version? version) =>
        version is not null && version >= MinimumVersion;

    /// <summary>Accepts Authenticode signers whose subject names ASUS (ASUSTeK).</summary>
    public static bool TrustedSigner(string? subject) =>
        !string.IsNullOrWhiteSpace(subject) &&
        (subject.Contains("ASUS", StringComparison.OrdinalIgnoreCase) ||
         subject.Contains("ASUSTeK", StringComparison.OrdinalIgnoreCase));

    /// <summary>Human-readable verification summary for logs and diagnostics.</summary>
    public static string Describe(Version? version, string? signerSubject)
    {
        string v = VersionSupported(version) ? version!.ToString() : (version?.ToString() ?? "desconocida") + " (no soportada)";
        string s = string.IsNullOrWhiteSpace(signerSubject)
            ? "sin firma legible"
            : (TrustedSigner(signerSubject) ? "firmante ASUS: " : "firmante no ASUS: ") + signerSubject;
        return "versión " + v + "; " + s;
    }
}
