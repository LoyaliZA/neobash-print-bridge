using System.Xml.Linq;

namespace Neobash.PrintBridge.Core.Capture;

public static class VirtualPrinterPrintTicket
{
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> DeclaredFeatures =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["PageMediaSize"] = new HashSet<string>(StringComparer.Ordinal) { "ISOA4", "NorthAmericaLetter" },
            ["PageOrientation"] = new HashSet<string>(StringComparer.Ordinal) { "Portrait", "Landscape" },
            ["PageOutputColor"] = new HashSet<string>(StringComparer.Ordinal) { "Color", "Monochrome" }
        };

    public static bool IsValid(string? printTicketXml)
    {
        if (string.IsNullOrWhiteSpace(printTicketXml))
        {
            return false;
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(printTicketXml);
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }

        if (document.Root?.Name.LocalName != "PrintTicket")
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var feature in document.Root.Elements().Where(element => element.Name.LocalName == "Feature"))
        {
            var featureName = LocalName(feature.Attribute("name")?.Value);
            if (featureName is null || !DeclaredFeatures.TryGetValue(featureName, out var allowed))
            {
                continue;
            }

            if (!seen.Add(featureName))
            {
                return false;
            }

            var options = feature.Elements().Where(element => element.Name.LocalName == "Option").ToArray();
            if (options.Length != 1)
            {
                return false;
            }

            var optionName = LocalName(options[0].Attribute("name")?.Value);
            if (optionName is null || !allowed.Contains(optionName))
            {
                return false;
            }
        }

        return true;
    }

    private static string? LocalName(string? qualifiedName)
    {
        if (string.IsNullOrWhiteSpace(qualifiedName))
        {
            return null;
        }

        var separator = qualifiedName.LastIndexOf(':');
        var localName = separator >= 0 ? qualifiedName[(separator + 1)..] : qualifiedName;
        return string.IsNullOrWhiteSpace(localName) ? null : localName;
    }
}
