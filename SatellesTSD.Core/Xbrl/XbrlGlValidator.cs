using System.Collections.Concurrent;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace SatellesTSD.Core.Xbrl;

/// <summary>Validates an instance against the APA-restricted XBRL GL schema set in a folder laid out like the repo's
/// `schemas/apa-tsd` (its README lists the provenance). The set is compiled once per folder and cached. A schema
/// failure is what MTA refuses as KR_VALIDATE_XBRL_FILE_3 (BLACK, the whole file), so run this before every upload.</summary>
public sealed class XbrlGlValidator
{
    public const string EntrySchema = "gl/plt/case-c-b-m-e/gl-plt-2015-03-25.xsd";

    private static readonly ConcurrentDictionary<string, XmlSchemaSet> Cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly XmlSchemaSet _schemas;

    public XbrlGlValidator(string schemaRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaRoot);
        _schemas = Cache.GetOrAdd(Path.GetFullPath(schemaRoot), Load);
    }

    /// <summary>Every schema error, each with the line of the serialized document; empty when the document is valid.</summary>
    public IReadOnlyList<string> Validate(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Validate(document.ToString(SaveOptions.None));
    }

    public IReadOnlyList<string> ValidateFile(string path) => Validate(File.ReadAllText(path));

    public IReadOnlyList<string> Validate(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        var problems = new List<string>();
        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = _schemas,
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

    private static XmlSchemaSet Load(string root)
    {
        var entry = Path.Combine(root, EntrySchema);
        if (!File.Exists(entry))
        {
            throw new FileNotFoundException($"The APA TSD schema set is not at {root}: {EntrySchema} is missing.", entry);
        }

        var set = new XmlSchemaSet { XmlResolver = new LocalXbrlResolver(root) };
        var loadProblems = new List<string>();
        set.ValidationEventHandler += (_, e) =>
        {
            if (e.Severity == XmlSeverityType.Error)
            {
                loadProblems.Add(e.Message);
            }
        };
        set.Add(null, entry);
        set.Compile();
        if (loadProblems.Count > 0)
        {
            throw new InvalidOperationException("The schema set does not compile:" + Environment.NewLine + string.Join(Environment.NewLine, loadProblems));
        }

        return set;
    }

    /// <summary>Resolves the absolute xbrl.org references to the local copies and refuses anything that would go to the web.</summary>
    private sealed class LocalXbrlResolver : XmlUrlResolver
    {
        private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
        {
            ["http://www.xbrl.org/2003/xbrl-instance-2003-12-31.xsd"] = "xbrl/xbrl-instance-2003-12-31.xsd",
            ["http://www.xbrl.org/2003/xbrl-linkbase-2003-12-31.xsd"] = "xbrl/xbrl-linkbase-2003-12-31.xsd",
            ["http://www.xbrl.org/2003/xl-2003-12-31.xsd"] = "xbrl/xl-2003-12-31.xsd",
            ["http://www.xbrl.org/2003/xlink-2003-12-31.xsd"] = "xbrl/xlink-2003-12-31.xsd",
        };

        private readonly string _root;

        public LocalXbrlResolver(string root)
        {
            _root = root;
        }

        public override Uri ResolveUri(Uri? baseUri, string? relativeUri)
        {
            if (relativeUri is not null && Known.TryGetValue(relativeUri, out var local))
            {
                return new Uri(Path.Combine(_root, local));
            }

            var resolved = base.ResolveUri(baseUri, relativeUri);
            if (resolved.IsAbsoluteUri && resolved.Scheme is "http" or "https")
            {
                throw new InvalidOperationException($"Schema reference {resolved} is not available offline; add it to the schema folder.");
            }

            return resolved;
        }

        public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            if (absoluteUri.Scheme != Uri.UriSchemeFile)
            {
                throw new InvalidOperationException($"Schema reference {absoluteUri} is not a local file.");
            }

            return base.GetEntity(absoluteUri, role, ofObjectToReturn);
        }
    }
}
