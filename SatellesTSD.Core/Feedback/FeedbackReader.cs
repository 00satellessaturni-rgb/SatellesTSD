using System.IO.Compression;
using System.Xml.Linq;
using SatellesTSD.Core.Model;

namespace SatellesTSD.Core.Feedback;

/// <summary>Reads MTA's tagasisideraport (the processing message of a submission or of a confirmation). Tolerant where
/// MTA's own samples are: codes with leading spaces, a counter missing on black errors, an entryHeader without an
/// entryNumber, decimal commas and dots, a default namespace should one ever appear. With two or more messages the
/// download is a ZIP; <see cref="LoadAll"/> recognises one by its content, whatever the file is called.</summary>
public static class FeedbackReader
{
    private static readonly byte[] ZipMagic = [0x50, 0x4B, 0x03, 0x04];

    public static FeedbackReport Load(string path) => Read(XDocument.Load(path)) with { Source = Path.GetFileName(path) };

    /// <summary>Every report in a file: one for an XML, one per XML inside a ZIP (sniffed, not by extension).</summary>
    public static IReadOnlyList<FeedbackReport> LoadAll(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (IsZip(path))
        {
            using var zip = ZipFile.OpenRead(path);
            return ReadZip(zip);
        }

        return [Load(path)];
    }

    public static IReadOnlyList<FeedbackReport> ReadZip(ZipArchive zip)
    {
        ArgumentNullException.ThrowIfNull(zip);
        var reports = new List<FeedbackReport>();
        foreach (var entry in zip.Entries.Where(e => e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.FullName, StringComparer.Ordinal))
        {
            using var stream = entry.Open();
            reports.Add(Read(XDocument.Load(stream)) with { Source = entry.FullName });
        }

        if (reports.Count == 0)
        {
            throw new InvalidDataException("The ZIP holds no .xml report.");
        }

        return reports;
    }

    public static FeedbackReport Read(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var root = document.Root ?? throw new InvalidDataException("The report has no root element.");
        if (root.Name.LocalName != "processing")
        {
            throw new InvalidDataException($"Expected a <processing> root, found <{root.Name.LocalName}>.");
        }

        var ns = root.Name.Namespace;
        var resultRaw = ((string?)root.Attribute("result") ?? string.Empty).Trim();
        var result = resultRaw.ToUpperInvariant() switch
        {
            "OK" => ProcessingResult.Ok,
            "WARNING" => ProcessingResult.Warning,
            "ERROR" => ProcessingResult.Error,
            "PARTLYERROR" => ProcessingResult.PartlyError,
            _ => ProcessingResult.Unknown,
        };

        var sections = root.Elements(ns + "entryHeader").Select(h => ReadSection(h, ns)).ToList();
        if (sections.Count == 0)
        {
            throw new InvalidDataException($"The report (result \"{resultRaw}\") has no entryHeader; the schema requires at least one.");
        }

        return new FeedbackReport(result, resultRaw, sections);
    }

    private static FeedbackSection ReadSection(XElement header, XNamespace ns)
    {
        var entryNumber = Text(header.Element(ns + "entryNumber"));
        Section? section = entryNumber is not null && Enum.TryParse<Section>(entryNumber, out var parsed) && Enum.IsDefined(parsed) ? parsed : null;

        var validations = header.Elements(ns + "validations").Elements(ns + "validation").Select(v => new Validation(
            Level: Text(v.Element(ns + "errorLevel"))?.ToUpperInvariant() switch
            {
                "OK" => ValidationLevel.Ok,
                "WARNING" => ValidationLevel.Warning,
                "ERROR" => ValidationLevel.Error,
                _ => ValidationLevel.Unknown,
            },
            Code: Text(v.Element(ns + "errorCode")) ?? string.Empty,
            Description: Text(v.Element(ns + "errorDescription")) ?? string.Empty,
            DescriptionEn: Text(v.Element(ns + "errorDescription_EN")),
            DescriptionRu: Text(v.Element(ns + "errorDescription_RU")),
            Lines: v.Elements(ns + "lineReferences").Elements(ns + "lineReference").Select(r => ReadLineRef(r, ns)).ToList())).ToList();

        var lines = header.Elements(ns + "responseData").Elements(ns + "entryDetail").Select(d => new ResponseLine(
            d.Elements(ns + "lineReferences").Elements(ns + "lineReference").Select(r => ReadLineRef(r, ns)).ToList(),
            d.Elements(ns + "data").Select(x => new ResponseItem(
                Text(x.Element(ns + "code")) ?? string.Empty,
                Text(x.Element(ns + "description")),
                Text(x.Element(ns + "info")) ?? string.Empty,
                Text(x.Element(ns + "info_EN")),
                Text(x.Element(ns + "info_RU")))).ToList())).ToList();

        return new FeedbackSection(entryNumber, section, validations, lines);
    }

    private static LineRef ReadLineRef(XElement reference, XNamespace ns)
    {
        var id = Text(reference.Element(ns + "lineInternalId")) ?? throw new InvalidDataException("A lineReference has no lineInternalId.");
        var counterText = Text(reference.Element(ns + "lineNumberCounter"));
        int? counter = counterText is not null && int.TryParse(counterText, out var c) ? c : null;
        return new LineRef(id, counter);
    }

    private static bool IsZip(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> head = stackalloc byte[4];
        var read = stream.Read(head);
        return read == 4 && head.SequenceEqual(ZipMagic);
    }

    /// <summary>Trimmed text, null when the element is absent or blank — MTA's samples carry stray spaces and tabs.</summary>
    private static string? Text(XElement? element)
    {
        var value = element?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
