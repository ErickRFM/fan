using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FanForge.Core;

/// <summary>
/// Validates an MSIX <c>AppxManifest.xml</c> against the requirements FanForge relies on
/// (x64 identity, four-part version, full-trust desktop entry point). Pure XML logic so the package
/// structure can be verified in CI without the Windows packaging SDK or a signing certificate.
/// It never decides whether a package will be *accepted*: an unsigned MSIX needs a trusted signature.
/// </summary>
public static class MsixManifest
{
    private static readonly Regex VersionPattern = new(@"^\d+\.\d+\.\d+\.\d+$", RegexOptions.Compiled);

    public const string ExpectedArchitecture = "x64";
    public const string FullTrustEntryPoint = "Windows.FullTrustApplication";

    /// <summary>Returns the list of problems found; an empty list means the manifest is well formed.</summary>
    public static IReadOnlyList<string> Validate(string? xml)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(xml))
        {
            issues.Add("El manifiesto está vacío.");
            return issues;
        }

        XDocument document;
        try { document = XDocument.Parse(xml); }
        catch (System.Xml.XmlException ex)
        {
            issues.Add("XML no válido: " + ex.Message);
            return issues;
        }

        var root = document.Root;
        if (root is null || !Is(root, "Package"))
        {
            issues.Add("La raíz debe ser <Package>.");
            return issues;
        }

        var identity = FirstChild(root, "Identity");
        if (identity is null)
        {
            issues.Add("Falta <Identity>.");
        }
        else
        {
            if (Blank(identity, "Name")) issues.Add("Falta Identity@Name.");
            if (Blank(identity, "Publisher")) issues.Add("Falta Identity@Publisher.");

            string version = identity.Attribute("Version")?.Value ?? string.Empty;
            if (!VersionPattern.IsMatch(version))
                issues.Add("Identity@Version debe tener el formato de cuatro partes (p. ej. 0.8.0.0).");

            string arch = identity.Attribute("ProcessorArchitecture")?.Value ?? string.Empty;
            if (!string.Equals(arch, ExpectedArchitecture, StringComparison.OrdinalIgnoreCase))
                issues.Add($"Identity@ProcessorArchitecture debe ser '{ExpectedArchitecture}'.");
        }

        var properties = FirstChild(root, "Properties");
        if (properties is null)
        {
            issues.Add("Falta <Properties>.");
        }
        else
        {
            if (BlankChild(properties, "DisplayName")) issues.Add("Falta Properties/DisplayName.");
            if (BlankChild(properties, "PublisherDisplayName")) issues.Add("Falta Properties/PublisherDisplayName.");
            if (BlankChild(properties, "Logo")) issues.Add("Falta Properties/Logo.");
        }

        var application = root.Descendants().FirstOrDefault(e => Is(e, "Application"));
        if (application is null)
        {
            issues.Add("Falta <Applications>/<Application>.");
        }
        else
        {
            if (Blank(application, "Executable")) issues.Add("Falta Application@Executable.");
            string entryPoint = application.Attribute("EntryPoint")?.Value ?? string.Empty;
            if (!string.Equals(entryPoint, FullTrustEntryPoint, StringComparison.OrdinalIgnoreCase))
                issues.Add($"Application@EntryPoint debe ser '{FullTrustEntryPoint}'.");
        }

        if (!root.Descendants().Any(e => Is(e, "TargetDeviceFamily")))
            issues.Add("Falta <Dependencies>/<TargetDeviceFamily>.");

        return issues;
    }

    private static bool Is(XElement element, string local) =>
        string.Equals(element.Name.LocalName, local, StringComparison.Ordinal);

    private static XElement? FirstChild(XElement parent, string local) =>
        parent.Elements().FirstOrDefault(e => Is(e, local));

    private static bool Blank(XElement element, string attribute) =>
        string.IsNullOrWhiteSpace(element.Attribute(attribute)?.Value);

    private static bool BlankChild(XElement parent, string local) =>
        string.IsNullOrWhiteSpace(FirstChild(parent, local)?.Value);
}
