using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using SatellesTSD.Core.Model;

namespace SatellesTSD.Core.Xbrl;

/// <summary>Reads an APA TSD XBRL GL instance back into the <see cref="Declaration"/> model — a file this app wrote
/// earlier (for M/T corrections) or one of MTA's examples. Unknown classifiers are kept as extra accountSub entries;
/// amounts and measurables are kept as written.</summary>
public static class XbrlGlReader
{
    private static readonly XNamespace Xbrli = XbrlGlWriter.Xbrli;
    private static readonly XNamespace GlCor = XbrlGlWriter.GlCor;
    private static readonly XNamespace GlBus = XbrlGlWriter.GlBus;

    public static Declaration Load(string path) => Read(XDocument.Load(path));

    public static Declaration Read(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var root = document.Root ?? throw new InvalidDataException("The document has no root element.");
        if (root.Name != Xbrli + "xbrl")
        {
            throw new InvalidDataException($"Expected xbrli:xbrl as the root element, found {root.Name}.");
        }

        var context = Required(root, Xbrli + "context");
        var registryCode = Required(Required(context, Xbrli + "entity"), Xbrli + "identifier").Value.Trim();

        var moneyUnits = root.Elements(Xbrli + "unit")
            .Where(u => string.Equals(u.Element(Xbrli + "measure")?.Value.Trim(), "iso4217:EUR", StringComparison.OrdinalIgnoreCase))
            .Select(u => (string?)u.Attribute("id"))
            .Where(id => id is not null)
            .ToHashSet(StringComparer.Ordinal)!;

        var entries = Required(root, GlCor + "accountingEntries");
        var info = Required(entries, GlCor + "documentInfo");
        var periodStart = ParseDate(Required(info, GlCor + "periodCoveredStart").Value);
        var creationDate = ParseDate(Required(info, GlCor + "creationDate").Value);

        var sections = entries.Elements(GlCor + "entryHeader").Select(h => ReadSection(h, moneyUnits!)).ToList();

        return new Declaration(
            RegistryCode: registryCode,
            Year: periodStart.Year,
            Month: periodStart.Month,
            CreationDate: creationDate,
            UniqueId: Required(info, GlCor + "uniqueID").Value.Trim(),
            Creator: Required(info, GlBus + "creator").Value.Trim(),
            SourceApplication: Required(info, GlBus + "sourceApplication").Value.Trim(),
            EntriesComment: info.Element(GlCor + "entriesComment")?.Value.Trim(),
            Sections: sections);
    }

    private static SectionEntries ReadSection(XElement header, HashSet<string> moneyUnits)
    {
        var number = Required(header, GlCor + "entryNumber").Value.Trim();
        if (!Enum.TryParse<Section>(number, out var section))
        {
            throw new InvalidDataException($"Unsupported data section {number}.");
        }

        var lines = header.Elements(GlCor + "entryDetail").Select(d => ReadLine(d, moneyUnits)).ToList();
        return new SectionEntries(section, header.Element(GlCor + "entryComment")?.Value.Trim(), lines);
    }

    private static PaymentLine ReadLine(XElement detail, HashSet<string> moneyUnits)
    {
        var lineNumber = Required(detail, GlCor + "lineNumber").Value.Trim();
        var counterValue = ParseDecimal(Required(detail, GlCor + "lineNumberCounter").Value);
        if (counterValue != decimal.Truncate(counterValue) || counterValue < 1 || counterValue > int.MaxValue)
        {
            throw new InvalidDataException($"Line {lineNumber}: lineNumberCounter {counterValue} is not a positive whole number.");
        }

        var counter = (int)counterValue;

        string? typeCode = null;
        LineAction? action = null;
        var extras = new List<AccountSubEntry>();
        foreach (var sub in Required(detail, GlCor + "account").Elements(GlCor + "accountSub"))
        {
            var id = Required(sub, GlCor + "accountSubID").Value.Trim();
            var type = Required(sub, GlCor + "accountSubType").Value.Trim();
            if (type == XbrlGlWriter.PaymentTypeClassifier)
            {
                typeCode = id;
            }
            else if (type == XbrlGlWriter.ActionClassifier)
            {
                action = Enum.Parse<LineAction>(id);
            }
            else
            {
                var tuple = sub.Element(GlCor + "segmentParentTuple");
                extras.Add(new AccountSubEntry(
                    id,
                    type,
                    tuple?.Element(GlCor + "parentSubaccountCode")?.Value.Trim(),
                    tuple?.Element(GlCor + "parentSubaccountType")?.Value.Trim()));
            }
        }

        if (action is null)
        {
            throw new InvalidDataException($"Line {lineNumber} has no {XbrlGlWriter.ActionClassifier} entry.");
        }

        var amountText = detail.Element(GlCor + "amount")?.Value;
        decimal? amount = amountText is null ? null : ParseDecimal(amountText);

        PayeeId? payee = null;
        if (detail.Element(GlCor + "identifierReference") is { } reference)
        {
            payee = new PayeeId(
                Required(reference, GlCor + "identifierCode").Value.Trim(),
                Required(reference, GlCor + "identifierDescription").Value.Trim());
        }

        var documentDateText = detail.Element(GlCor + "documentDate")?.Value;
        DateOnly? documentDate = documentDateText is null ? null : ParseDate(documentDateText);

        var events = new List<MeasurableEvent>();
        var amounts = new List<MeasurableAmount>();
        foreach (var measurable in detail.Elements(GlBus + "measurable"))
        {
            if (measurable.Element(GlBus + "measurableID") is { } id)
            {
                var endText = measurable.Element(GlBus + "measurableEndDateTime")?.Value;
                events.Add(new MeasurableEvent(
                    id.Value.Trim(),
                    ParseDate(Required(measurable, GlBus + "measurableStartDateTime").Value),
                    endText is null ? null : ParseDate(endText)));
            }
            else
            {
                var quantity = Required(measurable, GlBus + "measurableQuantity");
                var unit = (string?)quantity.Attribute("unitRef");
                amounts.Add(new MeasurableAmount(
                    Required(measurable, GlBus + "measurableDescription").Value.Trim(),
                    ParseDecimal(quantity.Value),
                    Money: unit is not null && moneyUnits.Contains(unit)));
            }
        }

        var taxes = detail.Elements(GlCor + "taxes").Select(t =>
        {
            var rateText = t.Element(GlCor + "taxPercentageRate")?.Value;
            return new TaxLine(
                Required(t, GlCor + "taxCode").Value.Trim(),
                ParseDecimal(Required(t, GlCor + "taxAmount").Value),
                rateText is null ? null : ParseDecimal(rateText));
        }).ToList();

        return new PaymentLine(
            lineNumber,
            counter,
            action.Value,
            typeCode,
            extras,
            amount,
            detail.Element(GlBus + "allocationCode")?.Value.Trim(),
            payee,
            documentDate,
            events,
            amounts,
            taxes);
    }

    private static XElement Required(XElement parent, XName name) =>
        parent.Element(name) ?? throw new InvalidDataException($"{parent.Name.LocalName} has no {name.LocalName} element.");

    /// <summary>An XML Schema decimal: optional sign, digits, one dot. A decimal comma or a thousands separator is
    /// a malformed file, not a number (MTA's feedback reports do use commas, but they are parsed elsewhere).</summary>
    private static decimal ParseDecimal(string text)
    {
        try
        {
            return XmlConvert.ToDecimal(text.Trim());
        }
        catch (FormatException e)
        {
            throw new InvalidDataException($"'{text.Trim()}' is not an XML decimal.", e);
        }
    }

    private static DateOnly ParseDate(string text) =>
        DateOnly.ParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
