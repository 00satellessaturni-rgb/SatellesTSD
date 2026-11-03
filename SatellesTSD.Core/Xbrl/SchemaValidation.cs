using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace SatellesTSD.Core.Xbrl;

/// <summary>Validation against one self-contained XSD (MTA's feedback report and confirmation schemas import nothing).
/// For the XBRL GL set with its imports use <see cref="XbrlGlValidator"/>.</summary>
public static class SchemaValidation
{
    public static IReadOnlyList<string> Validate(string xsdPath, XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Validate(xsdPath, document.ToString(SaveOptions.None));
    }

    public static IReadOnlyList<string> Validate(string xsdPath, string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xsdPath);
        ArgumentNullException.ThrowIfNull(xml);

        var set = new XmlSchemaSet { XmlResolver = null };
        using (var schemaStream = File.OpenRead(xsdPath))
        using (var schemaReader = XmlReader.Create(schemaStream, new XmlReaderSettings { XmlResolver = null, DtdProcessing = DtdProcessing.Prohibit }))
        {
            var schema = XmlSchema.Read(schemaReader, null) ?? throw new InvalidDataException($"{xsdPath} is not an XML schema.");
            set.Add(schema);
        }

        set.Compile();

        var problems = new List<string>();
        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = set,
            XmlResolver = null,
            DtdProcessing = DtdProcessing.Prohibit,
            // Warnings count: an element the schema does not know (a wrong root, a stray namespace) is only a warning
            // to .NET, and its content is then skipped unvalidated.
            ValidationFlags = XmlSchemaValidationFlags.ReportValidationWarnings,
        };
        settings.ValidationEventHandler += (_, e) =>
            problems.Add($"{e.Severity} at line {e.Exception?.LineNumber}, position {e.Exception?.LinePosition}: {e.Message}");

        using var text = new StringReader(xml);
        using var reader = XmlReader.Create(text, settings);
        while (reader.Read())
        {
        }

        return problems;
    }
}
