using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using SatellesTSD.Core.Model;

namespace SatellesTSD.Core.Xbrl;

/// <summary>Writes a <see cref="Declaration"/> as the XBRL GL 2015-03-25 instance MTA's APA TSD accepts: the fixed
/// header, one context, the eur and pure units, and every element in the order the APA XSD sequences require
/// (docs/reference/apa-xml-reference.md, sections A–C).</summary>
public static class XbrlGlWriter
{
    public static readonly XNamespace Xbrli = "http://www.xbrl.org/2003/instance";
    public static readonly XNamespace Xlink = "http://www.w3.org/1999/xlink";
    public static readonly XNamespace Xbrll = "http://www.xbrl.org/2003/linkbase";
    public static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";
    public static readonly XNamespace GlUsk = "http://www.xbrl.org/int/gl/usk/2015-03-25";
    public static readonly XNamespace GlCor = "http://www.xbrl.org/int/gl/cor/2015-03-25";
    public static readonly XNamespace GlExt = "http://www.xbrl.org/int/gl/ext/2015-03-25";
    public static readonly XNamespace Iso639 = "http://www.xbrl.org/2005/iso639";
    public static readonly XNamespace Iso4217 = "http://www.xbrl.org/2003/iso4217";
    public static readonly XNamespace GlPlt = "http://www.xbrl.org/int/gl/plt/2015-03-25";
    public static readonly XNamespace GlBus = "http://www.xbrl.org/int/gl/bus/2015-03-25";
    public static readonly XNamespace GlMuc = "http://www.xbrl.org/int/gl/muc/2015-03-25";

    public const string SchemaLocation =
        "http://www.xbrl.org/int/gl/plt/2015-03-25 https://www.xbrl.org/taxonomy/int/gl/2015-03-25/plt/case-c-b-m-u-t/gl-plt-all-2015-03-25.xsd";

    /// <summary>The href every MTA example instance uses (the guide's header snippet differs; the examples win).</summary>
    public const string SchemaRefHref = "../plt/case-c-b-m-e/gl-plt-2015-03-25.xsd";

    public const string IdentifierScheme = "http://www.ariregister.rik.ee";
    public const string ContextId = "now";
    public const string MoneyUnitId = "eur";
    public const string PureUnitId = "NotUsed";
    public const string PaymentTypeClassifier = "VALJAMLIIK2023ap";
    public const string ActionClassifier = "TOIMINGUTYYP2023ap";

    /// <summary>Builds the document. Throws <see cref="InvalidOperationException"/> listing every problem
    /// <see cref="DeclarationValidator"/> finds, so an invalid file is never produced.</summary>
    public static XDocument Write(Declaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        var problems = DeclarationValidator.Validate(declaration);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException("The declaration cannot be written:" + Environment.NewLine + string.Join(Environment.NewLine, problems));
        }

        var root = new XElement(Xbrli + "xbrl",
            new XAttribute(XNamespace.Xmlns + "xbrli", Xbrli),
            new XAttribute(XNamespace.Xmlns + "xlink", Xlink),
            new XAttribute(XNamespace.Xmlns + "xbrll", Xbrll),
            new XAttribute(XNamespace.Xmlns + "xsi", Xsi),
            new XAttribute(XNamespace.Xmlns + "gl-usk", GlUsk),
            new XAttribute(XNamespace.Xmlns + "gl-cor", GlCor),
            new XAttribute(XNamespace.Xmlns + "gl-ext", GlExt),
            new XAttribute(XNamespace.Xmlns + "iso639", Iso639),
            new XAttribute(XNamespace.Xmlns + "iso4217", Iso4217),
            new XAttribute(XNamespace.Xmlns + "gl-plt", GlPlt),
            new XAttribute(XNamespace.Xmlns + "gl-bus", GlBus),
            new XAttribute(XNamespace.Xmlns + "gl-muc", GlMuc),
            new XAttribute(Xsi + "schemaLocation", SchemaLocation),
            new XElement(Xbrll + "schemaRef",
                new XAttribute(Xlink + "href", SchemaRefHref),
                new XAttribute(Xlink + "arcrole", "http://www.w3.org/1999/xlink/properties/linkbase"),
                new XAttribute(Xlink + "type", "simple")),
            new XElement(Xbrli + "context",
                new XAttribute("id", ContextId),
                new XElement(Xbrli + "entity",
                    new XElement(Xbrli + "identifier", new XAttribute("scheme", IdentifierScheme), declaration.RegistryCode)),
                new XElement(Xbrli + "period",
                    new XElement(Xbrli + "instant", Date(declaration.CreationDate)))),
            new XElement(Xbrli + "unit", new XAttribute("id", MoneyUnitId), new XElement(Xbrli + "measure", "iso4217:EUR")),
            new XElement(Xbrli + "unit", new XAttribute("id", PureUnitId), new XElement(Xbrli + "measure", "pure")),
            new XElement(GlCor + "accountingEntries",
                DocumentInfo(declaration),
                new XElement(GlCor + "entityInformation",
                    new XElement(GlBus + "organizationIdentifiers",
                        Fact(GlBus + "organizationIdentifier", declaration.RegistryCode),
                        Fact(GlBus + "organizationDescription", "ARIREGISTRIKOOD"))),
                declaration.Sections.Select(WriteSection)));

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
    }

    /// <summary>UTF-8 without BOM, indented, with the declaration spelled as MTA's own files spell it ("UTF-8";
    /// XmlWriter would write "utf-8", which is equivalent but not identical).</summary>
    public static string Serialize(Declaration declaration)
    {
        var document = Write(declaration);
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

    public static void Save(Declaration declaration, string path)
    {
        File.WriteAllText(path, Serialize(declaration), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static XElement WriteSection(SectionEntries section)
    {
        ArgumentNullException.ThrowIfNull(section);
        var header = new XElement(GlCor + "entryHeader", Fact(GlCor + "entryNumber", section.Section.ToString()));
        if (!string.IsNullOrEmpty(section.CorrectionReason))
        {
            header.Add(Fact(GlCor + "entryComment", section.CorrectionReason));
        }

        header.Add(section.Lines.Select(WriteLine));
        return header;
    }

    public static XElement WriteLine(PaymentLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.Action != LineAction.T && string.IsNullOrEmpty(line.TypeCode))
        {
            throw new InvalidOperationException($"Line {line.LineNumber}: a payment type is required unless the action is T.");
        }

        var account = new XElement(GlCor + "account");
        if (line.Action != LineAction.T)
        {
            account.Add(AccountSub(line.TypeCode!, PaymentTypeClassifier, null, null));
        }

        account.Add(AccountSub(line.Action.ToString(), ActionClassifier, null, null));
        if (line.Action != LineAction.T)
        {
            account.Add(line.ExtraAccountSubs.Select(e => AccountSub(e.Id, e.Type, e.ParentCode, e.ParentType)));
        }

        var detail = new XElement(GlCor + "entryDetail",
            Fact(GlCor + "lineNumber", line.LineNumber),
            new XElement(GlCor + "lineNumberCounter",
                new XAttribute("contextRef", ContextId),
                new XAttribute("unitRef", PureUnitId),
                line.Counter.ToString(CultureInfo.InvariantCulture)),
            account);

        if (line.Action == LineAction.T)
        {
            return detail;
        }

        if (line.Amount is { } amount)
        {
            detail.Add(Money(GlCor + "amount", amount));
        }

        if (!string.IsNullOrEmpty(line.AllocationCode))
        {
            detail.Add(Fact(GlBus + "allocationCode", line.AllocationCode));
        }

        if (line.Payee is { } payee)
        {
            detail.Add(new XElement(GlCor + "identifierReference",
                Fact(GlCor + "identifierCode", payee.Code),
                Fact(GlCor + "identifierDescription", payee.Description)));
        }

        if (line.DocumentDate is { } documentDate)
        {
            detail.Add(Fact(GlCor + "documentDate", Date(documentDate)));
        }

        foreach (var e in line.Events)
        {
            var measurable = new XElement(GlBus + "measurable",
                Fact(GlBus + "measurableID", e.Id),
                Fact(GlBus + "measurableIDSchema", MeasurableEvent.Schema),
                Fact(GlBus + "measurableStartDateTime", Date(e.Start)));
            if (e.End is { } end)
            {
                measurable.Add(Fact(GlBus + "measurableEndDateTime", Date(end)));
            }

            detail.Add(measurable);
        }

        foreach (var a in line.Amounts)
        {
            detail.Add(new XElement(GlBus + "measurable",
                Fact(GlBus + "measurableDescription", a.Description),
                Number(GlBus + "measurableQuantity", a.Quantity, a.Money ? MoneyUnitId : PureUnitId)));
        }

        foreach (var t in line.Taxes)
        {
            var taxes = new XElement(GlCor + "taxes", Money(GlCor + "taxAmount", t.Amount));
            if (t.Rate is { } rate)
            {
                taxes.Add(Number(GlCor + "taxPercentageRate", rate, PureUnitId));
            }

            taxes.Add(Fact(GlCor + "taxCode", t.Code));
            detail.Add(taxes);
        }

        return detail;
    }

    private static XElement DocumentInfo(Declaration d)
    {
        var info = new XElement(GlCor + "documentInfo",
            Fact(GlCor + "entriesType", "other"),
            Fact(GlCor + "uniqueID", d.UniqueId),
            Fact(GlCor + "language", "iso639:et"),
            Fact(GlCor + "creationDate", Date(d.CreationDate)),
            Fact(GlBus + "creator", d.Creator));
        if (!string.IsNullOrEmpty(d.EntriesComment))
        {
            info.Add(Fact(GlCor + "entriesComment", d.EntriesComment));
        }

        info.Add(
            Fact(GlCor + "periodCoveredStart", Date(d.PeriodStart)),
            Fact(GlCor + "periodCoveredEnd", Date(d.PeriodEnd)),
            Fact(GlBus + "sourceApplication", d.SourceApplication),
            Fact(GlMuc + "defaultCurrency", "iso4217:eur"));
        return info;
    }

    private static XElement AccountSub(string id, string type, string? parentCode, string? parentType)
    {
        var sub = new XElement(GlCor + "accountSub",
            Fact(GlCor + "accountSubID", id),
            Fact(GlCor + "accountSubType", type));
        if (!string.IsNullOrEmpty(parentCode))
        {
            var tuple = new XElement(GlCor + "segmentParentTuple", Fact(GlCor + "parentSubaccountCode", parentCode));
            if (!string.IsNullOrEmpty(parentType))
            {
                tuple.Add(Fact(GlCor + "parentSubaccountType", parentType));
            }

            sub.Add(tuple);
        }

        return sub;
    }

    private static XElement Fact(XName name, string value) =>
        new(name, new XAttribute("contextRef", ContextId), value);

    private static XElement Money(XName name, decimal value) => Number(name, value, MoneyUnitId);

    private static XElement Number(XName name, decimal value, string unitId) =>
        new(name,
            new XAttribute("contextRef", ContextId),
            new XAttribute("decimals", "2"),
            new XAttribute("unitRef", unitId),
            Amount(value));

    /// <summary>Two decimals, dot, invariant: "1000.00", "-1.60", "0.22".</summary>
    public static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    public static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
