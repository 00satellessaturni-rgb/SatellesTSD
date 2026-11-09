using System.Globalization;
using System.Text;
using ExcelDataReader;
using SatellesTSD.Core.Import;

namespace SatellesTSD.Infrastructure;

/// <summary>Reads .xlsx and legacy .xls workbooks into sheets on ExcelDataReader. Every worksheet comes through,
/// hidden ones included, as the workbook took them. Typed values are rendered invariantly (a number as "1234.56", a
/// date as ISO 8601), text as it is; a personal code stored as a number comes back without exponent or decimals.
/// The code-page provider is registered once so that BIFF files in Windows-1257 or Windows-1251 decode.</summary>
public sealed class ExcelTableReader : ITableReader
{
    static ExcelTableReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public SourceWorkbook Read(string fileName, byte[] content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);
        var format = FileSniffer.Sniff(content);
        if (format is not (SourceFormat.Xlsx or SourceFormat.Xls))
        {
            throw new ImportException($"{fileName} is not an Excel workbook ({format}).");
        }

        using var stream = new MemoryStream(content, writable: false);
        IExcelDataReader reader;
        try
        {
            reader = ExcelReaderFactory.CreateReader(stream);
        }
        catch (Exception e) when (e is not ImportException)
        {
            throw new ImportException($"{fileName} could not be opened as an Excel workbook: {e.Message}", e);
        }

        using (reader)
        {
            var sheets = new List<SheetTable>();
            var index = 0;
            try
            {
                do
                {
                    var rows = new List<IReadOnlyList<Cell>>();
                    while (reader.Read())
                    {
                        var cells = new List<Cell>(reader.FieldCount);
                        for (var i = 0; i < reader.FieldCount; i++)
                        {
                            cells.Add(Convert(reader.GetValue(i)));
                        }

                        while (cells.Count > 0 && cells[^1].Kind == CellKind.Empty)
                        {
                            cells.RemoveAt(cells.Count - 1);
                        }

                        rows.Add(cells);
                    }

                    while (rows.Count > 0 && rows[^1].Count == 0)
                    {
                        rows.RemoveAt(rows.Count - 1);
                    }

                    sheets.Add(new SheetTable(index++, reader.Name, rows, !string.Equals(reader.VisibleState, "visible", StringComparison.OrdinalIgnoreCase)));
                }
                while (reader.NextResult());
            }
            catch (Exception e) when (e is not ImportException)
            {
                // a damaged sheet is this file's failure, named as such, so the other files of a batch are still imported
                throw new ImportException($"{fileName} could not be read (sheet {index + 1}): {e.Message}", e);
            }

            return new SourceWorkbook(fileName, format, sheets);
        }
    }

    private static Cell Convert(object? value) => value switch
    {
        null or DBNull => Cell.Empty,
        DateTime d => Cell.Date(d),
        // a whole number stays exact (an archive id of sixteen digits); (decimal)double would keep fifteen significant digits
        double d when d == Math.Truncate(d) && Math.Abs(d) < 9.2e18 => Cell.Number((long)d),
        double d when double.IsFinite(d) && Math.Abs(d) < 7.9e27 => Cell.Number((decimal)d),
        double d => Cell.Of(d.ToString("R", CultureInfo.InvariantCulture)),
        float f => Cell.Number((decimal)f),
        decimal m => Cell.Number(m),
        int i => Cell.Number(i),
        long l => Cell.Number(l),
        bool b => Cell.Bool(b),
        string s => Cell.Of(s),
        TimeSpan t => Cell.Of(t.ToString("c", CultureInfo.InvariantCulture)),
        _ => Cell.Of(System.Convert.ToString(value, CultureInfo.InvariantCulture)),
    };
}
