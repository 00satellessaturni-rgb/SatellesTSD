using System.Text;
using SatellesTSD.Core.Import;
using SatellesTSD.Infrastructure;

namespace SatellesTSD.Tests;

/// <summary>Whole files through the Excel reader and the importer: every sheet classified, row numbers true.</summary>
public class ImporterTests
{
    private static readonly Importer Importer = new(LayoutCatalog.Default, new ExcelTableReader());

    private static object?[] LhvHeaders => LayoutCatalog.Default.Find("lhv-et-short")!.Headers.ToArray<object?>();

    [Fact]
    public void A_workbook_with_the_tor_sheet_a_statement_and_a_stray_sheet_is_classified_sheet_by_sheet()
    {
        var xlsx = Tables.Xlsx(
            ("Töötajate loend", [TorHeaders.All.ToArray<object?>(), ["1", 48712219406d, "MAASIKAS", "MARI", new DateTime(2024, 2, 15), 1, "Tööleping", 2, "Kehtiv", null, null, null, null, null, null, null, null, "EE", null, null, null, null, null, null, null, null, 10235688, "Näidis OÜ"]], false),
            ("LHV", [LhvHeaders, ["EE497700771001131757", 1, new DateTime(2026, 10, 5), "EE12", "Mari Maasikas", "D", -905.92, null, "2026100500000001", "palk", "EUR"]], false),
            ("Märkmed", [["Konto väljavõte"], ["mingi tekst"]], true),
            ("Tühi", [], false));

        var import = Importer.Read("palgad.xlsx", xlsx);

        Assert.Equal(SourceFormat.Xlsx, import.Format);
        Assert.Equal(64, import.Sha256.Length);
        Assert.Equal(xlsx.Length, import.Size);
        Assert.Equal(ImportKind.Mixed, import.Kind);
        Assert.Equal([SheetKind.Tor, SheetKind.Statement, SheetKind.Unrecognised, SheetKind.Unrecognised], import.Sheets.Select(s => s.Kind));
        Assert.Equal(["Töötajate loend", "LHV", "Märkmed", "Tühi"], import.Sheets.Select(s => s.Name));
        Assert.Equal("48712219406", Assert.Single(import.Sheets[0].Tor!.Entries).PersonalCode);
        Assert.Equal(-905.92m, Assert.Single(import.Sheets[1].Statement!.Rows).Amount);
        Assert.True(import.Sheets[2].Hidden);
        Assert.Equal("No statement layout has this header row.", import.Sheets[2].Message);
        Assert.Equal("The sheet is empty.", import.Sheets[3].Message);
        Assert.Equal(2, import.Recognised.Count);
    }

    [Fact]
    public void Blank_rows_inside_a_sheet_keep_the_row_numbers_true()
    {
        var xlsx = Tables.Xlsx("LHV",
            LhvHeaders,
            null,
            ["EE497700771001131757", null, new DateTime(2026, 10, 5), "EE12", "Mari Maasikas", "D", -100d, null, null, "palk", "EUR"],
            null,
            null,
            ["EE497700771001131757", null, new DateTime(2026, 10, 6), "EE12", "Jaan Tamm", "D", -200d, null, null, "palk", "EUR"]);

        var statement = Importer.Read("lhv.xlsx", xlsx).Sheets[0].Statement!;

        Assert.Equal([2, 3, 4, 5, 6], statement.Rows.Select(r => r.RowNo));
        Assert.Equal([SkipReason.Blank, null, SkipReason.Blank, SkipReason.Blank, null], statement.Rows.Select(r => r.SkipReason));
        Assert.Equal(6, statement.Rows[4].RowNo);
    }

    [Fact]
    public void The_excel_reader_renders_typed_cells_invariantly()
    {
        var xlsx = Tables.Xlsx("S", ["text", 1234.56, 38001010000d, new DateTime(2026, 10, 1), new DateTime(2026, 10, 1, 9, 30, 0), true, 0.1 + 0.2]);

        var sheet = Tables.FirstSheet(xlsx);

        Assert.Equal(new Cell(CellKind.Text, "text"), sheet.At(1, 1));
        Assert.Equal(new Cell(CellKind.Number, "1234.56"), sheet.At(1, 2));
        Assert.Equal(new Cell(CellKind.Number, "38001010000"), sheet.At(1, 3));
        Assert.Equal(new Cell(CellKind.Date, "2026-10-01"), sheet.At(1, 4));
        Assert.Equal(new Cell(CellKind.Date, "2026-10-01T09:30:00"), sheet.At(1, 5));
        Assert.Equal(new Cell(CellKind.Bool, "TRUE"), sheet.At(1, 6));
        Assert.Equal(new Cell(CellKind.Number, "0.3"), sheet.At(1, 7));
        Assert.Equal(Cell.Empty, sheet.At(1, 8));
        Assert.Equal(Cell.Empty, sheet.At(9, 1));
    }

    [Fact]
    public void Files_that_are_not_what_their_name_says_are_refused_with_a_reason()
    {
        var html = Encoding.UTF8.GetBytes("<html><table><tr><td>Kliendi konto</td></tr></table></html>");
        var ex = Assert.Throws<ImportException>(() => Importer.Read("statement.xls", html));
        Assert.Contains("web page", ex.Message);

        Assert.Throws<ImportException>(() => Importer.Read("x.bin", [0, 1, 2, 3]));

        var noExcel = new Importer(LayoutCatalog.Default);
        Assert.Contains("no Excel reader", Assert.Throws<ImportException>(() => noExcel.Read("a.xlsx", Tables.Xlsx("S", ["a"]))).Message);

        var broken = new byte[] { 0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4, 5, 6, 7, 8 };
        Assert.Throws<ImportException>(() => Importer.Read("broken.xlsx", broken));
    }

    [Fact]
    public void A_csv_goes_through_the_same_door()
    {
        var text = "Дата;Плательщик / Получатель;Номер счета;Назначение платежа;Сумма;Валюта\n2026-10-01;Mari Maasikas;LT1;palk;100,00;EUR\n";

        var import = Importer.Read("paysera.csv", Encoding.UTF8.GetBytes(text));

        Assert.Equal(SourceFormat.Csv, import.Format);
        Assert.Equal(ImportKind.Statement, import.Kind);
        Assert.Equal("paysera-ru-short", import.Sheets[0].Statement?.Layout.Id);
        Assert.Equal("paysera", import.Sheets[0].Name);
    }
}
