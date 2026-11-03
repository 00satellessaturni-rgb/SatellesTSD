using ClosedXML.Excel;
using SatellesTSD.Core.Import;
using SatellesTSD.Infrastructure;

namespace SatellesTSD.Tests;

/// <summary>Sheets for the import tests: in-memory grids, and real .xlsx bytes built with ClosedXML and read back
/// through the Infrastructure reader.</summary>
internal static class Tables
{
    public static SheetTable Sheet(params string?[][] rows) => Sheet("Sheet1", rows);

    public static SheetTable Sheet(string name, params string?[][] rows) =>
        new(0, name, rows.Select(r => (IReadOnlyList<Cell>)r.Select(Cell.Of).ToList()).ToList());

    public static SheetTable Typed(params Cell[][] rows) => new(0, "Sheet1", rows.Select(r => (IReadOnlyList<Cell>)r.ToList()).ToList());

    public static string[] Row(params string?[] cells) => cells.Select(c => c ?? "").ToArray();

    /// <summary>An .xlsx with one sheet per tuple; a null cell is left empty, a null row is left blank.</summary>
    public static byte[] Xlsx(params (string Name, object?[]?[] Rows, bool Hidden)[] sheets)
    {
        using var workbook = new XLWorkbook();
        foreach (var (name, rows, hidden) in sheets)
        {
            var sheet = workbook.AddWorksheet(name);
            for (var r = 0; r < rows.Length; r++)
            {
                var row = rows[r];
                if (row is null)
                {
                    continue;
                }

                for (var c = 0; c < row.Length; c++)
                {
                    var cell = sheet.Cell(r + 1, c + 1);
                    switch (row[c])
                    {
                        case null:
                            break;
                        case string s:
                            cell.Value = s;
                            break;
                        case double d:
                            cell.Value = d;
                            break;
                        case decimal m:
                            cell.Value = (double)m;
                            break;
                        case int i:
                            cell.Value = (double)i;
                            break;
                        case long l:
                            cell.Value = (double)l;
                            break;
                        case DateTime t:
                            cell.Value = t;
                            cell.Style.DateFormat.Format = "dd.MM.yyyy";
                            break;
                        case bool b:
                            cell.Value = b;
                            break;
                        default:
                            throw new ArgumentException($"Unsupported fixture value {row[c]!.GetType()}");
                    }
                }
            }

            if (hidden)
            {
                sheet.Visibility = XLWorksheetVisibility.Hidden;
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static byte[] Xlsx(string name, params object?[]?[] rows) => Xlsx((name, rows, false));

    public static SourceWorkbook ReadXlsx(byte[] bytes, string fileName = "fixture.xlsx") => new ExcelTableReader().Read(fileName, bytes);

    public static SheetTable FirstSheet(byte[] xlsx) => ReadXlsx(xlsx).Sheets[0];
}
