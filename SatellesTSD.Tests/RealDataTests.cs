using SatellesTSD.Core.Import;
using SatellesTSD.Infrastructure;

namespace SatellesTSD.Tests;

/// <summary>The owner's real exports, kept outside the repository (they hold personal data). The folder is
/// <c>SATELLESTSD_TEST_DATA</c> or the owner's default; without it these tests pass with nothing to check. Each bank
/// file exists twice there: the bank's own CSV and the .xlsx the accountant made of it by hand for the workbook. The
/// app must read both to the same rows.</summary>
public class RealDataTests
{
    private const string DefaultFolder = @"C:\Users\satelles-it\source\claude\Test-files-per-app\SatellesTSD-test-data";

    private static readonly Importer Importer = new(LayoutCatalog.Default, new ExcelTableReader());

    internal static string? Folder
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("SATELLESTSD_TEST_DATA");
            if (configured is not null && !Directory.Exists(configured))
            {
                throw new DirectoryNotFoundException($"SATELLESTSD_TEST_DATA points at {configured}, which does not exist."); // a misconfiguration must not pass as green
            }

            var folder = configured ?? DefaultFolder;
            return Directory.Exists(folder) ? folder : null;
        }
    }

    private static ParsedImport Read(string path) => Importer.Read(Path.GetFileName(path), File.ReadAllBytes(path));

    private static bool QuestionMarksMatch(string written, string meant) =>
        written.Length == meant.Length && written.Zip(meant).All(pair => pair.First == pair.Second || (pair.First == '?' && pair.Second >= 0x80));

    [Fact]
    public void Every_bank_csv_and_its_hand_made_xlsx_read_to_the_same_payment_rows()
    {
        if (Folder is not { } folder)
        {
            return;
        }

        var csvFiles = Directory.GetFiles(folder, "*.csv", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).StartsWith("search-export", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(f).StartsWith("tsd-", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.NotEmpty(csvFiles);
        var checkedFiles = 0;
        foreach (var csv in csvFiles)
        {
            var xlsx = Path.ChangeExtension(csv, ".xlsx");
            var fromCsv = Read(csv);
            var csvSheet = Assert.Single(fromCsv.Sheets);
            Assert.True(csvSheet.Kind == SheetKind.Statement, $"{csv}: {csvSheet.Message}");
            if (csvSheet.Statement!.Payments.Count == 0)
            {
                // a statement of a month without a single transaction: the header alone, said as such — not a file whose rows all went astray
                Assert.Contains(csvSheet.Statement.Problems, p => p.Code == ImportProblem.NoRows);
                Assert.True(csvSheet.Statement.Rows.All(r => r.SkipReason == SkipReason.Blank), $"{csv}: rows without a counterparty");
                continue;
            }

            Assert.All(csvSheet.Statement.Payments, r => Assert.True(r.Problem is null, $"{csv} row {r.RowNo}: {r.Problem}"));
            Assert.All(csvSheet.Statement.Payments, r => Assert.NotEqual(Direction.Unknown, r.Direction));

            if (!File.Exists(xlsx))
            {
                continue;
            }

            var fromXlsx = Read(xlsx);
            var xlsxSheet = fromXlsx.Sheets.Single(s => s.Kind != SheetKind.Unrecognised);
            Assert.Equal(csvSheet.Statement.Layout.Id, xlsxSheet.Statement!.Layout.Id);
            Assert.Equal(csvSheet.Statement.DisplayName, xlsxSheet.Statement.DisplayName);
            // the accountant's .xlsx carries the names as Excel garbled them ("StopFire OЬ"); the look-alike fix undoes that
            static List<(DateOnly?, decimal?, Direction, string, string)> Shape(ParsedStatement s) =>
                s.Payments.Select(r => (r.Date, r.Amount, r.Direction, NameText.Demojibake(NameText.Normalize(r.Counterparty)), r.RowHash)).ToList(); // the fingerprint is format-independent
            var fromBank = Shape(csvSheet.Statement);
            var fromExcel = Shape(xlsxSheet.Statement);
            Assert.True(fromBank.Count == fromExcel.Count, $"{csv}: {fromBank.Count} payment rows in the CSV, {fromExcel.Count} in the xlsx");
            for (var i = 0; i < fromBank.Count; i++)
            {
                var bankRow = csvSheet.Statement.Payments[i];
                // one bank's CSV writes every letter it cannot encode as '?'; and Excel reads a description like "01/10" or "16:30" as a date or a time
                var excelRow = xlsxSheet.Statement.Payments[i];
                var lossy = (bankRow.Counterparty ?? "").Contains('?') || (bankRow.Description ?? "").Contains('?')
                            // Excel reads a description like "01/10" or "16:30" as a date or a time
                            || (bankRow.Description is { Length: > 0 } d && d.All(c => char.IsAsciiDigit(c) || c is '/' or ':' or '-' or ' ' or '.' or ',') && d.Any(c => c is '/' or ':' or '-'))
                            // the accountant's conversion split a description at its ';' and dropped the rest of the row
                            || (excelRow.Cells.Count(c => c.Length > 0) < bankRow.Cells.Count(c => c.Length > 0)
                                && (bankRow.Description ?? "").Contains(';') && (bankRow.Description ?? "").StartsWith(excelRow.Description ?? "\0", StringComparison.Ordinal));
                var same = lossy
                    // one bank's CSV writes every letter it cannot encode as '?' ("CLEARENT O?"); the accountant's copy came from a better source, so the names and the fingerprint cannot agree
                    ? (fromBank[i].Item1, fromBank[i].Item2, fromBank[i].Item3) == (fromExcel[i].Item1, fromExcel[i].Item2, fromExcel[i].Item3) && QuestionMarksMatch(fromBank[i].Item4, fromExcel[i].Item4)
                    : fromBank[i] == fromExcel[i];
                Assert.True(same,
                    $"{csv} row {bankRow.RowNo}: CSV {fromBank[i]} | XLSX {fromExcel[i]}\n  CSV cells: {string.Join("|", bankRow.Cells)}\n  XLSX cells: {string.Join("|", xlsxSheet.Statement.Payments[i].Cells)}");
            }

            checkedFiles++;
        }

        Assert.True(checkedFiles >= 5, $"only {checkedFiles} CSV/xlsx pairs compared");
    }

    [Fact]
    public void Every_paysera_export_reads_as_a_statement_whatever_sits_above_its_header()
    {
        if (Folder is not { } folder)
        {
            return;
        }

        // the exports of September 2026 start with the header row; those of October carry a logo, a title block and
        // the account above it (the header on row 6) — both read as the same layout, with every row below the header
        var files = Directory.GetFiles(folder, "EVP*.xlsx", SearchOption.AllDirectories).ToList();
        Assert.True(files.Count >= 20, $"only {files.Count} Paysera exports found");
        var headerRows = new HashSet<int>();
        foreach (var file in files)
        {
            var sheet = Assert.Single(Read(file).Sheets);
            Assert.True(sheet.Kind == SheetKind.Statement, $"{file}: {sheet.Message}");
            Assert.Equal("paysera-ru", sheet.Statement!.Layout.Id);
            Assert.All(sheet.Statement.Rows, r => Assert.True(r.RowNo > sheet.Statement.HeaderRowNo, $"{file}: row {r.RowNo} above the header"));
            Assert.All(sheet.Statement.Payments, r => Assert.True(r.Problem is null, $"{file} row {r.RowNo}: {r.Problem}"));
            headerRows.Add(sheet.Statement.HeaderRowNo);
        }

        Assert.Contains(1, headerRows);
        Assert.Contains(6, headerRows);
    }

    [Fact]
    public void Every_tor_export_reads_with_its_employer_and_no_row_problems()
    {
        if (Folder is not { } folder)
        {
            return;
        }

        var expectedEmployer = new Dictionary<string, string> { ["rr-arenduse-palgad"] = "12016683", ["stopfire-palgad"] = "12223047", ["weld24-palgad"] = "14463293" };
        var files = Directory.GetFiles(folder, "search-export*", SearchOption.AllDirectories).ToList();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var import = Read(file);
            var sheet = Assert.Single(import.Sheets);
            Assert.True(sheet.Kind == SheetKind.Tor, $"{file}: {sheet.Message}");
            var tor = sheet.Tor!;
            Assert.NotEmpty(tor.Entries);
            Assert.All(tor.Entries, e => Assert.True(e.Problem is null, $"{file} row {e.RowNo}: {e.Problem}"));
            Assert.All(tor.Entries, e => Assert.True(PersonalCode.LooksValid(e.PersonalCode), $"{file} row {e.RowNo}: {e.PersonalCode}"));
            Assert.All(tor.Entries, e => Assert.True(int.TryParse(e.TypeId, out var t) && t > 0, $"{file} row {e.RowNo}: type id '{e.TypeId}' ({e.Type})")); // the register has more types than the workbook's six; S5 maps them
            Assert.All(tor.Entries, e => Assert.True(int.TryParse(e.StateId, out var s) && s > 0, $"{file} row {e.RowNo}: state id '{e.StateId}' ({e.State})"));
            var code = Assert.Single(tor.EmployerCodes); // one employer per export
            if (expectedEmployer.TryGetValue(Path.GetFileName(Path.GetDirectoryName(file)!), out var company))
            {
                Assert.Equal(company, code);
                Assert.Null(TorParser.CheckEmployer(tor, company));
            }

            Assert.Equal(TorParser.EmployerMismatch, TorParser.CheckEmployer(tor, "00000000")?.Code);

            var xlsx = Path.ChangeExtension(file, ".xlsx");
            if (File.Exists(xlsx))
            {
                var fromXlsx = Read(xlsx).Sheets.Single().Tor!;
                Assert.Equal(tor.Entries.Select(e => (e.TorId, e.PersonalCode, e.FirstName, e.LastName, e.StartDate, e.EndDate, e.TypeId, e.StateId)),
                    fromXlsx.Entries.Select(e => (e.TorId, e.PersonalCode, e.FirstName, e.LastName, e.StartDate, e.EndDate, e.TypeId, e.StateId)));
            }
        }
    }

    [Fact]
    public void The_workbooks_own_report_is_not_mistaken_for_a_source()
    {
        if (Folder is not { } folder)
        {
            return;
        }

        var banks = LayoutCatalog.Default.Layouts.Select(l => l.Bank).Distinct().ToList();
        var reports = Directory.GetFiles(folder, "*report-details*.xlsx", SearchOption.AllDirectories);
        Assert.NotEmpty(reports);
        var copies = 0;
        foreach (var report in reports)
        {
            var import = Read(report);
            Assert.Contains(import.Sheets, s => s.Name == "Vahearuanne" && s.Kind == SheetKind.Unrecognised);
            // the report's copy of the TÖR sheet has extra columns: it still reads as TÖR, and says so
            var torCopy = Assert.Single(import.Sheets, s => s.Name == "TOR kontroll");
            Assert.Equal(SheetKind.Tor, torCopy.Kind);
            Assert.Contains(torCopy.Tor!.Problems, p => p.Code == TorParser.ExtraColumns);
            // each statement copy ("Swedbank 2708") is the bank's sheet with the workbook's 'Vaste leitud' column: where the workbook labelled that column in the
            // header row the copy has one column too many and the bank's layout is named as nearest; where it did not, the copy reads as that bank's statement
            foreach (var copy in import.Sheets.Where(s => banks.Any(b => s.Name!.StartsWith(b + " ", StringComparison.Ordinal))))
            {
                var bank = banks.First(b => copy.Name!.StartsWith(b + " ", StringComparison.Ordinal));
                if (copy.Kind == SheetKind.Statement)
                {
                    Assert.Equal(bank, copy.Statement!.Layout.Bank);
                }
                else
                {
                    Assert.True(copy.Kind == SheetKind.Unrecognised, $"{report} / {copy.Name}: {copy.Kind}");
                    Assert.Equal(bank, LayoutCatalog.Default.Find(copy.NearestLayoutId ?? "")?.Bank);
                    Assert.Contains(copy.Differences, d => d.Contains("header columns"));
                }

                copies++;
            }
        }

        Assert.True(copies > 0, "no statement copy sheet found in any report");
    }
}
