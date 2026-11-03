using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace SatellesTSD.Core.Confirmation;

/// <summary>Writes the confirmDeclaration XML exactly as MTA's sample and confirmations-2026-09-23.xsd lay it out:
/// no namespace, the elements in sequence order, amounts with two decimals.</summary>
public static class ConfirmationWriter
{
    private static readonly HashSet<string> IdentifierTypes = new(StringComparer.Ordinal) { "ARIREGISTRIKOOD", "ISIKUKOOD", "MRR_KOOD" };
    private static readonly HashSet<string> Sections = new(StringComparer.Ordinal) { "PAYMENTS", "L3", "L4", "L5", "L6", "L7", "L8", "INF1", "ESM_SM", "ESM_KP" };
    private static readonly HashSet<string> TaxTypes = new(StringComparer.Ordinal) { "110", "114", "115", "116", "117", "118", "119", "ESM_SM_KOKKU", "ESM_KP_KOKKU" };

    public static XDocument Write(ConfirmDeclaration confirmation)
    {
        ArgumentNullException.ThrowIfNull(confirmation);
        var problems = Validate(confirmation);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException("The confirmation cannot be written:" + Environment.NewLine + string.Join(Environment.NewLine, problems));
        }

        var root = new XElement("confirmDeclaration");
        if (confirmation.PersonCode is not null)
        {
            root.Add(new XElement("personCode", confirmation.PersonCode));
        }

        root.Add(
            new XElement("declaration", confirmation.DeclarationText),
            new XElement("taxablePersonIdentifier", confirmation.TaxablePersonIdentifier),
            new XElement("taxablePersonIdentifierType", confirmation.TaxablePersonIdentifierType),
            new XElement("declarationType", confirmation.Kind.ToString()),
            new XElement("month", confirmation.Month.ToString(CultureInfo.InvariantCulture)),
            new XElement("year", confirmation.Year.ToString(CultureInfo.InvariantCulture)),
            new XElement("sections", confirmation.Sections.Select(s => new XElement("sectionItem",
                new XElement("section", s.Section),
                new XElement("hasData", s.HasData ? "true" : "false")))),
            new XElement("taxSums", confirmation.TaxSums.Select(t => new XElement("taxSum",
                new XElement("taxType", t.TaxType),
                new XElement("taxAmount", t.Amount.ToString("0.00", CultureInfo.InvariantCulture))))));

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
    }

    public static string Serialize(ConfirmDeclaration confirmation)
    {
        var document = Write(confirmation);
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "    ",
            OmitXmlDeclaration = true,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, settings))
        {
            document.Save(writer);
        }

        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Environment.NewLine + Encoding.UTF8.GetString(stream.ToArray());
    }

    public static void Save(ConfirmDeclaration confirmation, string path) =>
        File.WriteAllText(path, Serialize(confirmation), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    /// <summary>The XSD's own constraints, checked before writing so the message names the field.</summary>
    public static IReadOnlyList<string> Validate(ConfirmDeclaration c)
    {
        ArgumentNullException.ThrowIfNull(c);
        var problems = new List<string>();
        if (c.PersonCode is not null && !(c.PersonCode.Length == 11 && c.PersonCode.All(char.IsAsciiDigit)))
        {
            problems.Add("personCode must be 11 digits.");
        }

        if (string.IsNullOrWhiteSpace(c.DeclarationText) || c.DeclarationText.Length > 500)
        {
            problems.Add("declaration must be 1–500 characters.");
        }

        if (string.IsNullOrWhiteSpace(c.TaxablePersonIdentifier) || c.TaxablePersonIdentifier.Length > 50)
        {
            problems.Add("taxablePersonIdentifier must be 1–50 characters.");
        }

        if (!IdentifierTypes.Contains(c.TaxablePersonIdentifierType))
        {
            problems.Add($"taxablePersonIdentifierType {c.TaxablePersonIdentifierType} is not ARIREGISTRIKOOD, ISIKUKOOD or MRR_KOOD.");
        }

        if (c.Month is < 1 or > 12)
        {
            problems.Add($"month {c.Month} is not 1–12.");
        }

        if (c.Year is < 1000 or > 9999)
        {
            problems.Add($"year {c.Year} is not four digits.");
        }

        if (c.Sections.Count == 0)
        {
            problems.Add("at least one sectionItem is required.");
        }

        foreach (var s in c.Sections.Where(s => !Sections.Contains(s.Section)))
        {
            problems.Add($"section {s.Section} is not in the enumeration.");
        }

        if (c.TaxSums.Count == 0)
        {
            problems.Add("at least one taxSum is required.");
        }

        foreach (var t in c.TaxSums)
        {
            if (!TaxTypes.Contains(t.TaxType))
            {
                problems.Add($"taxType {t.TaxType} is not in the enumeration.");
            }

            if (t.Amount < 0m)
            {
                problems.Add($"taxAmount for {t.TaxType} is negative ({t.Amount}); the schema allows 0.00 and above.");
            }

            if (t.Amount != Math.Round(t.Amount, 2))
            {
                problems.Add($"taxAmount for {t.TaxType} ({t.Amount}) has more than two decimals.");
            }
        }

        return problems;
    }
}
