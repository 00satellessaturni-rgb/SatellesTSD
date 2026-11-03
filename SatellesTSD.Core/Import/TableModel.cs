using System.Globalization;

namespace SatellesTSD.Core.Import;

/// <summary>What a cell held before it became text: the parsers treat a typed number or date from Excel differently
/// from the bank's own text in a CSV.</summary>
public enum CellKind
{
    Empty,
    Text,
    /// <summary>A number; <see cref="Cell.Text"/> is its invariant decimal rendering.</summary>
    Number,
    /// <summary>A date (with a time when the cell had one); <see cref="Cell.Text"/> is ISO 8601.</summary>
    Date,
    /// <summary>A boolean; <see cref="Cell.Text"/> is TRUE or FALSE.</summary>
    Bool,
}

/// <summary>One cell of a sheet as the reader delivered it: typed values are rendered invariantly, text is kept as the
/// file had it (mojibake and all), so parsing never depends on the machine's locale.</summary>
public readonly record struct Cell(CellKind Kind, string Text)
{
    public static readonly Cell Empty = new(CellKind.Empty, "");

    public static Cell Of(string? text) => string.IsNullOrEmpty(text) ? Empty : new(CellKind.Text, text);

    public static Cell Number(decimal value) => new(CellKind.Number, value.ToString(CultureInfo.InvariantCulture));

    public static Cell Date(DateTime value) => new(CellKind.Date, value.TimeOfDay == TimeSpan.Zero
        ? value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        : value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));

    public static Cell Bool(bool value) => new(CellKind.Bool, value ? "TRUE" : "FALSE");

    /// <summary>Empty, or text that is nothing but whitespace.</summary>
    public bool IsBlank => Kind == CellKind.Empty || string.IsNullOrWhiteSpace(Text);
}

/// <summary>One sheet of a source file as a grid of cells. <see cref="Rows"/>[i] is sheet row i+1, blank rows
/// included, so a row number here is the row number the accountant sees in Excel.</summary>
public sealed record SheetTable(int Index, string? Name, IReadOnlyList<IReadOnlyList<Cell>> Rows, bool Hidden = false)
{
    /// <summary>The cell at a 1-based row and column; <see cref="Cell.Empty"/> outside the grid.</summary>
    public Cell At(int rowNo, int colNo)
    {
        if (rowNo < 1 || rowNo > Rows.Count || colNo < 1)
        {
            return Cell.Empty;
        }

        var row = Rows[rowNo - 1];
        return colNo <= row.Count ? row[colNo - 1] : Cell.Empty;
    }

    /// <summary>The 1-based column of the last non-blank cell of a row (0 when the row is blank) — the workbook's
    /// <c>End(xlToLeft)</c>, which decides a header row's width.</summary>
    public int LastColumnOf(int rowNo)
    {
        if (rowNo < 1 || rowNo > Rows.Count)
        {
            return 0;
        }

        var row = Rows[rowNo - 1];
        for (var i = row.Count - 1; i >= 0; i--)
        {
            if (!row[i].IsBlank)
            {
                return i + 1;
            }
        }

        return 0;
    }

    /// <summary>The 1-based number of the last row with any non-blank cell (0 for an empty sheet).</summary>
    public int LastRow
    {
        get
        {
            for (var i = Rows.Count - 1; i >= 0; i--)
            {
                if (LastColumnOf(i + 1) > 0)
                {
                    return i + 1;
                }
            }

            return 0;
        }
    }

    /// <summary>The texts of a row, trailing blanks dropped; an empty list for a row outside the grid.</summary>
    public IReadOnlyList<string> TextsOf(int rowNo)
    {
        var last = LastColumnOf(rowNo);
        var texts = new string[last];
        for (var i = 0; i < last; i++)
        {
            texts[i] = At(rowNo, i + 1).Text;
        }

        return texts;
    }
}

public enum SourceFormat
{
    Unknown,
    /// <summary>Office Open XML (a ZIP).</summary>
    Xlsx,
    /// <summary>Legacy BIFF (an OLE compound file).</summary>
    Xls,
    /// <summary>Delimited text.</summary>
    Csv,
    /// <summary>An HTML page saved under an Excel extension — some banks do this; Excel opens it, the app refuses it.</summary>
    Html,
}

/// <summary>A source file read into sheets, whatever its format.</summary>
/// <param name="Encoding">The text encoding a CSV was decoded with; null for Excel files.</param>
/// <param name="Warnings">What the reader had to make do with (an unclosed quote in a CSV).</param>
public sealed record SourceWorkbook(string FileName, SourceFormat Format, IReadOnlyList<SheetTable> Sheets, string? Encoding = null, IReadOnlyList<string>? Warnings = null)
{
    public IReadOnlyList<string> Warnings { get; init; } = Warnings ?? [];
}

/// <summary>Reads an Excel file into sheets; the implementation lives in Infrastructure (it needs a package).</summary>
public interface ITableReader
{
    SourceWorkbook Read(string fileName, byte[] content);
}

/// <summary>Tells a file's format by its first bytes, never by its extension: banks hand out HTML and text under
/// ".xls", and a renamed file is still what it is.</summary>
public static class FileSniffer
{
    public static SourceFormat Sniff(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04)
        {
            return SourceFormat.Xlsx;
        }

        if (bytes.Length >= 8 && bytes[0] == 0xD0 && bytes[1] == 0xCF && bytes[2] == 0x11 && bytes[3] == 0xE0 && bytes[4] == 0xA1 && bytes[5] == 0xB1 && bytes[6] == 0x1A && bytes[7] == 0xE1)
        {
            return SourceFormat.Xls;
        }

        if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
        {
            // UTF-16 (Excel's "Unicode Text"): judge the characters, not the bytes — a Cyrillic or Baltic letter has a control byte in it
            var encoding = bytes[0] == 0xFF ? System.Text.Encoding.Unicode : System.Text.Encoding.BigEndianUnicode;
            var head = encoding.GetString(bytes[2..Math.Min(bytes.Length, 2 + 2048)]).TrimStart(' ', '\t', '\r', '\n');
            if (head.Length == 0)
            {
                return SourceFormat.Unknown;
            }

            return head[0] == '<' ? SourceFormat.Html : head.All(c => c >= ' ' || c is '\t' or '\r' or '\n') ? SourceFormat.Csv : SourceFormat.Unknown;
        }

        var start = SkipBomAndSpace(bytes);
        if (start.Length == 0)
        {
            return SourceFormat.Unknown;
        }

        if (start[0] == (byte)'<')
        {
            return SourceFormat.Html;
        }

        return LooksLikeText(start) ? SourceFormat.Csv : SourceFormat.Unknown;
    }

    private static ReadOnlySpan<byte> SkipBomAndSpace(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            bytes = bytes[3..];
        }

        var i = 0;
        while (i < bytes.Length && (bytes[i] == ' ' || bytes[i] == '\t' || bytes[i] == '\r' || bytes[i] == '\n'))
        {
            i++;
        }

        return bytes[i..];
    }

    /// <summary>Text has no control bytes other than tab, CR and LF in its first kilobyte.</summary>
    private static bool LooksLikeText(ReadOnlySpan<byte> bytes)
    {
        var length = Math.Min(bytes.Length, 1024);
        for (var i = 0; i < length; i++)
        {
            var b = bytes[i];
            if (b < 0x20 && b != 0x09 && b != 0x0A && b != 0x0D)
            {
                return false;
            }
        }

        return true;
    }
}
