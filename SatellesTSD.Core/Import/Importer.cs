using System.Security.Cryptography;
using System.Text;

namespace SatellesTSD.Core.Import;

public enum SheetKind
{
    Statement,
    Tor,
    Unrecognised,
}

/// <summary>What a file holds.</summary>
public enum ImportKind
{
    Statement,
    Tor,
    /// <summary>The TÖR export and statement sheets in one workbook.</summary>
    Mixed,
}

/// <summary>One sheet of a source file, classified. An unrecognised sheet says why, so the accountant can tell a
/// stray title row from a layout the app does not know.</summary>
public sealed record ImportedSheet(
    int Index,
    string? Name,
    SheetKind Kind,
    ParsedStatement? Statement,
    ParsedTor? Tor,
    string? Message,
    string? NearestLayoutId,
    IReadOnlyList<string> Differences,
    bool Hidden)
{
    public bool Recognised => Kind != SheetKind.Unrecognised;

    public IReadOnlyList<ImportProblem> Problems => Statement?.Problems ?? Tor?.Problems ?? [];
}

/// <summary>A source file read and classified, ready for the accountant's eye and the store.</summary>
/// <param name="Warnings">What the reader had to make do with (an unclosed quote in a CSV).</param>
public sealed record ParsedImport(string FileName, SourceFormat Format, string Sha256, long Size, string? Encoding, IReadOnlyList<ImportedSheet> Sheets, IReadOnlyList<string>? Warnings = null)
{
    public IReadOnlyList<string> Warnings { get; init; } = Warnings ?? [];

    public IReadOnlyList<ImportedSheet> Recognised => Sheets.Where(s => s.Recognised).ToList();

    public bool HasData => Sheets.Any(s => s.Recognised);

    /// <summary>What the file holds, from its recognised sheets; null when nothing was recognised.</summary>
    public ImportKind? Kind
    {
        get
        {
            var hasTor = Sheets.Any(s => s.Kind == SheetKind.Tor);
            var hasStatement = Sheets.Any(s => s.Kind == SheetKind.Statement);
            return (hasTor, hasStatement) switch
            {
                (true, true) => ImportKind.Mixed,
                (true, false) => ImportKind.Tor,
                (false, true) => ImportKind.Statement,
                _ => null,
            };
        }
    }
}

/// <summary>Reads a file of any supported format and classifies every sheet — the TÖR export first, then the
/// statement layouts, as the workbook did for every worksheet of every picked workbook.</summary>
public sealed class Importer
{
    private readonly LayoutCatalog _catalog;
    private readonly ITableReader? _excelReader;
    private readonly int _headerSearchRows;

    /// <param name="excelReader">The Excel reader from Infrastructure; without one, only CSV can be read.</param>
    public Importer(LayoutCatalog catalog, ITableReader? excelReader = null, int headerSearchRows = StatementParser.DefaultHeaderSearchRows)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _excelReader = excelReader;
        _headerSearchRows = headerSearchRows;
    }

    public ParsedImport Read(string fileName, byte[] content, Encoding? csvEncoding = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);
        var format = FileSniffer.Sniff(content);
        var sha256 = Sha256Of(content);
        return format switch
        {
            SourceFormat.Xlsx or SourceFormat.Xls => Classify((_excelReader ?? throw new ImportException($"{fileName} is an Excel file and no Excel reader is configured.")).Read(fileName, content), sha256, content.LongLength),
            SourceFormat.Csv => ReadCsv(fileName, content, csvEncoding, sha256),
            SourceFormat.Html => throw new ImportException($"{fileName} is a web page saved under an Excel name; open it in Excel and save it as .xlsx first."),
            _ => throw new ImportException($"{fileName} is neither an Excel file nor text."),
        };
    }

    /// <summary>A CSV that is not UTF-8 is decoded as Windows-1257 first; when that gives no layout whose header
    /// row matches to the letter, Windows-1251 is tried (a Russian-language export), and the decoding under which a
    /// layout matches exactly wins. A caller who names the encoding is not second-guessed.</summary>
    private ParsedImport ReadCsv(string fileName, byte[] content, Encoding? encoding, string sha256)
    {
        var first = Classify(new CsvTableReader(encoding).Read(fileName, content), sha256, content.LongLength);
        if (encoding is not null || first.Encoding is null || first.Encoding.StartsWith("utf-", StringComparison.OrdinalIgnoreCase) || first.Sheets.Any(ExactStatement))
        {
            return first;
        }

        var retry = Classify(new CsvTableReader(CsvTableReader.Windows1251).Read(fileName, content), sha256, content.LongLength);
        return retry.Sheets.Any(ExactStatement) ? retry : first;

        static bool ExactStatement(ImportedSheet sheet) => sheet.Kind == SheetKind.Statement && sheet.Statement!.ExactHeaders;
    }

    public ParsedImport Classify(SourceWorkbook workbook, string sha256, long size)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        var sheets = workbook.Sheets.Select(Classify).ToList();
        return new ParsedImport(workbook.FileName, workbook.Format, sha256, size, workbook.Encoding, sheets, workbook.Warnings);
    }

    public ImportedSheet Classify(SheetTable sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        if (sheet.LastRow == 0)
        {
            return new ImportedSheet(sheet.Index, sheet.Name, SheetKind.Unrecognised, null, null, "The sheet is empty.", null, [], sheet.Hidden);
        }

        var tor = TorParser.Detect(sheet, out var missing, _headerSearchRows);
        if (tor is not null)
        {
            return new ImportedSheet(sheet.Index, sheet.Name, SheetKind.Tor, null, TorParser.Parse(sheet, tor), null, null, [], sheet.Hidden);
        }

        var detection = StatementParser.Detect(sheet, _catalog, _headerSearchRows);
        if (detection.Match is not null)
        {
            return new ImportedSheet(sheet.Index, sheet.Name, SheetKind.Statement, StatementParser.Parse(sheet, detection.Match), null, null, null, [], sheet.Hidden);
        }

        var message = missing.Count > 0
            ? "Looks like the TÖR export but lacks the columns " + string.Join(", ", missing) + "."
            : detection.NearestLayoutId is null
                ? "No statement layout has this header row."
                : $"Nearest layout '{detection.NearestLayoutId}': " + string.Join("; ", detection.Differences);
        return new ImportedSheet(sheet.Index, sheet.Name, SheetKind.Unrecognised, null, null, message, detection.NearestLayoutId, detection.Differences, sheet.Hidden);
    }

    public static string Sha256Of(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));
}
