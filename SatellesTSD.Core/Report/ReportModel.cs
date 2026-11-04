namespace SatellesTSD.Core.Report;

/// <summary>What a report cell holds: text, a number, a date, or nothing. The writer types the cell accordingly;
/// nothing here depends on a spreadsheet library.</summary>
public enum ReportCellKind
{
    Empty,
    Text,
    /// <summary>A money amount or a sum: two decimals.</summary>
    Number,
    /// <summary>An identifier of digits (a personal code) written as a whole number, so that Excel shows plain digits
    /// without a "number stored as text" mark; <see cref="ReportCell.Text"/> keeps the digits.</summary>
    Code,
    Date,
}

/// <summary>One cell of a report sheet.</summary>
public readonly record struct ReportCell(ReportCellKind Kind, string? Text = null, decimal Number = 0m, DateOnly? Date = null)
{
    public static readonly ReportCell Empty = new(ReportCellKind.Empty);

    public static ReportCell Of(string? text) => string.IsNullOrEmpty(text) ? Empty : new(ReportCellKind.Text, text);

    public static ReportCell Amount(decimal value) => new(ReportCellKind.Number, Number: Math.Round(value, 2, MidpointRounding.ToEven));

    public static ReportCell Day(DateOnly? date) => date is { } d ? new(ReportCellKind.Date, Date: d) : Empty;

    /// <summary>A personal code or another identifier: a whole number when it is digits only, without a leading zero
    /// and within what a double holds exactly (15 digits); text otherwise, so nothing is lost.</summary>
    public static ReportCell Identifier(string? code) =>
        !string.IsNullOrEmpty(code) && code.Length <= 15 && code[0] != '0' && code.All(char.IsAsciiDigit)
            ? new ReportCell(ReportCellKind.Code, code, decimal.Parse(code, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture))
            : Of(code);

    /// <summary>A stored statement or register cell: an ISO date becomes a date cell, everything else stays text (a
    /// number stays the text the file had, so a long identifier keeps its digits).</summary>
    public static ReportCell FromStored(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Empty;
        }

        if (text.Length == 10 && text[4] == '-' && text[7] == '-' && DateOnly.TryParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var day))
        {
            return Day(day);
        }

        return Of(text);
    }

    public bool IsEmpty => Kind == ReportCellKind.Empty;
}

/// <summary>One row of a report sheet; a header row is written bold.</summary>
public sealed class ReportRow
{
    public ReportRow(IReadOnlyList<ReportCell> cells, bool header = false)
    {
        Cells = cells;
        Header = header;
    }

    public IReadOnlyList<ReportCell> Cells { get; }

    public bool Header { get; }

    public static ReportRow Blank { get; } = new([]);

    public static ReportRow Texts(params string?[] texts) => new(texts.Select(ReportCell.Of).ToList());

    public static ReportRow Headers(params string?[] texts) => new(texts.Select(ReportCell.Of).ToList(), header: true);
}

/// <summary>One sheet of the report: its name as the workbook named it (the writer fits it to Excel's rules) and its
/// rows, <see cref="Rows"/>[i] being sheet row i+1 — a blank row where the workbook left one.</summary>
public sealed class ReportSheet
{
    public ReportSheet(string name, IReadOnlyList<ReportRow> rows)
    {
        Name = name;
        Rows = rows;
    }

    public string Name { get; }

    public IReadOnlyList<ReportRow> Rows { get; }

    public ReportRow Row(int rowNo) => rowNo >= 1 && rowNo <= Rows.Count ? Rows[rowNo - 1] : ReportRow.Blank;

    public ReportCell At(int rowNo, int colNo)
    {
        var row = Row(rowNo);
        return colNo >= 1 && colNo <= row.Cells.Count ? row.Cells[colNo - 1] : ReportCell.Empty;
    }

    /// <summary>The 1-based column whose header (row 1) is this text, 0 when there is none.</summary>
    public int ColumnOf(string header)
    {
        var first = Row(1);
        for (var i = 0; i < first.Cells.Count; i++)
        {
            if (first.Cells[i].Text == header)
            {
                return i + 1;
            }
        }

        return 0;
    }
}

/// <summary>The report-details workbook of one period: the sheets in the workbook's order, and the file name the
/// workbook would have given it.</summary>
public sealed class ReportBook
{
    public ReportBook(string fileName, IReadOnlyList<ReportSheet> sheets)
    {
        FileName = fileName;
        Sheets = sheets;
    }

    /// <summary>"report-details-<company>-<yyyy-MM-dd_HH-mm-ss>.xlsx".</summary>
    public string FileName { get; }

    public IReadOnlyList<ReportSheet> Sheets { get; }

    public ReportSheet? Sheet(string name) => Sheets.FirstOrDefault(s => s.Name == name);

    /// <summary>The longest the company's part of the file name gets, so that the path stays within Windows' limits.</summary>
    public const int MaxCompanyNameLength = 80;

    /// <summary>The workbook's file name: the company's name in lower case with spaces as dashes, the moment of the
    /// export, and nothing Windows refuses in a file name (nor a trailing dot or space).</summary>
    public static string FileNameFor(string companyName, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(companyName);
        var name = companyName.Trim().ToLowerInvariant().Replace(' ', '-');
        var cleaned = new string(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
        if (cleaned.Length > MaxCompanyNameLength)
        {
            cleaned = cleaned[..MaxCompanyNameLength];
        }

        cleaned = cleaned.Trim('.', ' ', '-');
        if (cleaned.Length == 0)
        {
            cleaned = "ettevote";
        }

        return $"report-details-{cleaned}-{at:yyyy-MM-dd_HH-mm-ss}.xlsx";
    }
}

/// <summary>Where the report went.</summary>
public sealed record ReportExportResult(string Path, int Sheets);
