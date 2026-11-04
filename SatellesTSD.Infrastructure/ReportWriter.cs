using ClosedXML.Excel;
using SatellesTSD.Core.Report;

namespace SatellesTSD.Infrastructure;

/// <summary>Writes a <see cref="ReportBook"/> as an .xlsx with ClosedXML: typed cells (numbers with two decimals,
/// dates as dd.MM.yyyy, text as it is), bold headers, sheet names fitted to Excel's rules.</summary>
public static class ReportWriter
{
    private const string AmountFormat = "#,##0.00";
    private const string CodeFormat = "0";
    private const string DateFormat = "dd.MM.yyyy";
    private const int MaxSheetNameLength = 31;
    private static readonly char[] ForbiddenInSheetName = ['[', ']', ':', '*', '?', '/', '\\', '\'', '\0'];

    /// <summary>Writes the book into the folder under its own file name and returns the path. The file is written
    /// beside its name first and moved onto it whole, so that a failed write leaves no half-written report behind.</summary>
    public static ReportExportResult Write(ReportBook book, string folder)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, book.FileName);
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, ToBytes(book));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }

        return new ReportExportResult(path, book.Sheets.Count);
    }

    /// <summary>The book as .xlsx bytes (the tests' round trip).</summary>
    public static byte[] ToBytes(ReportBook book)
    {
        ArgumentNullException.ThrowIfNull(book);
        using var workbook = Fill(book);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>The names the sheets get, in order: the workbook's, fitted to Excel (no []:*?/\ or apostrophes, at most
    /// 31 characters, no two alike ignoring case — a repeat gets " (2)", " (3)" within the limit).</summary>
    public static IReadOnlyList<string> SheetNames(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var name in names)
        {
            var cleaned = new string(name.Where(c => Array.IndexOf(ForbiddenInSheetName, c) < 0).ToArray()).Trim();
            if (cleaned.Length == 0)
            {
                cleaned = "Leht";
            }

            if (cleaned.Length > MaxSheetNameLength)
            {
                cleaned = cleaned[..MaxSheetNameLength].TrimEnd();
            }

            var candidate = cleaned;
            for (var k = 2; !taken.Add(candidate); k++)
            {
                var suffix = $" ({k})";
                var stem = cleaned.Length + suffix.Length > MaxSheetNameLength ? cleaned[..(MaxSheetNameLength - suffix.Length)].TrimEnd() : cleaned;
                candidate = stem + suffix;
            }

            result.Add(candidate);
        }

        return result;
    }

    private static XLWorkbook Fill(ReportBook book)
    {
        var workbook = new XLWorkbook();
        try
        {
            var names = SheetNames(book.Sheets.Select(s => s.Name));
            for (var i = 0; i < book.Sheets.Count; i++)
            {
                var sheet = workbook.AddWorksheet(names[i]);
                FillSheet(sheet, book.Sheets[i]);
            }

            return workbook;
        }
        catch
        {
            workbook.Dispose();
            throw;
        }
    }

    private static void FillSheet(IXLWorksheet sheet, ReportSheet source)
    {
        var width = 0;
        for (var r = 0; r < source.Rows.Count; r++)
        {
            var row = source.Rows[r];
            width = Math.Max(width, row.Cells.Count);
            for (var c = 0; c < row.Cells.Count; c++)
            {
                var value = row.Cells[c];
                if (value.IsEmpty)
                {
                    continue;
                }

                var cell = sheet.Cell(r + 1, c + 1);
                switch (value.Kind)
                {
                    case ReportCellKind.Number:
                        cell.Value = (double)value.Number;
                        cell.Style.NumberFormat.Format = AmountFormat;
                        break;
                    case ReportCellKind.Code:
                        cell.Value = (double)value.Number;
                        cell.Style.NumberFormat.Format = CodeFormat;
                        break;
                    case ReportCellKind.Date:
                        cell.Value = value.Date!.Value.ToDateTime(TimeOnly.MinValue);
                        cell.Style.DateFormat.Format = DateFormat;
                        break;
                    default:
                        cell.SetValue(value.Text ?? "");
                        if (value.Text is { } text && text.Contains('\n'))
                        {
                            cell.Style.Alignment.WrapText = true;
                        }

                        break;
                }
            }

            if (row.Header && row.Cells.Count > 0)
            {
                sheet.Range(r + 1, 1, r + 1, row.Cells.Count).Style.Font.Bold = true;
            }
        }

        // fixed widths: measuring every cell of a long sheet is slow, and the workbook set none
        for (var c = 1; c <= width; c++)
        {
            sheet.Column(c).Width = 16;
        }

        if (source.Rows.Count > 1 && source.Rows[0].Header)
        {
            sheet.SheetView.FreezeRows(1);
        }
    }
}
