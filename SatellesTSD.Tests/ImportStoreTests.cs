using System.Text;
using SatellesTSD.Core.Import;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Store;
using SatellesTSD.Core.Tax;
using SatellesTSD.Core.Xbrl;
using SatellesTSD.Infrastructure;

namespace SatellesTSD.Tests;

/// <summary>The import half of the store: files with their sheets and rows, the period's files, the bank-row link of a
/// declared line, the owner's layouts, and the upgrade from schema v1.</summary>
public class ImportStoreTests
{
    private static readonly TaxEngine Engine = new(TaxRates.Year2026);
    private static readonly Importer Importer = new(LayoutCatalog.Default, new ExcelTableReader());

    private static byte[] MixedWorkbook() => Tables.Xlsx(
        ("Töötajate loend", [TorHeaders.All.ToArray<object?>(),
            ["1", 48712219406d, "MAASIKAS", "MARI", new DateTime(2024, 2, 15), 1, "Tööleping", 2, "Kehtiv", null, null, null, null, null, null, null, null, "EE", null, null, null, null, null, null, null, null, 10235688, "Näidis OÜ"],
            ["2", 38810140309d, "TAMM", "JAAN", new DateTime(2020, 1, 1), 600, "VÕS leping", 4, "Lõpetatud", new DateTime(2026, 9, 30), null, null, null, null, null, null, null, "EE", null, null, null, null, null, null, null, null, 10235688, "Näidis OÜ"]], false),
        ("LHV", [LayoutCatalog.Default.Find("lhv-et-short")!.Headers.ToArray<object?>(),
            ["EE497700771001131757", 1, new DateTime(2026, 10, 5), "EE12", "Mari Maasikas", "D", -905.92, null, "2026100500000001", "palk", "EUR", 48712219406d],
            ["EE497700771001131757", 2, new DateTime(2026, 10, 6), null, null, "D", -0.38, null, null, "Teenustasu", "EUR"],
            ["EE497700771001131757", 3, new DateTime(2026, 11, 2), "EE13", "Jaan Tamm", "D", -300d, null, "2026110200000002", "töövõtt", "EUR"]], false));

    private static (SqliteStore Store, Company Company, Period Period) Setup()
    {
        var store = SqliteStore.InMemory();
        var company = store.AddCompany("10235688", "Näidis OÜ");
        var period = store.GetOrCreatePeriod(company.Id, 2026, 10);
        return (store, company, period);
    }

    [Fact]
    public void An_import_round_trips_with_its_sheets_rows_and_bytes()
    {
        var (store, company, _) = Setup();
        using (store)
        {
            var bytes = MixedWorkbook();
            var parsed = Importer.Read("palgad.xlsx", bytes);

            var file = store.AddImport(company.Id, parsed, bytes, "oktoober");

            Assert.Equal((ImportKind.Mixed, "palgad.xlsx", SourceFormat.Xlsx, parsed.Sha256, (long)bytes.Length, "oktoober"), (file.Kind, file.FileName, file.Format, file.Sha256, file.Size, file.Notes));
            Assert.Equal(file, Assert.Single(store.Imports(company.Id)));
            Assert.Equal(file, store.FindImport(company.Id, parsed.Sha256.ToUpperInvariant()));
            Assert.Equal(bytes, store.ImportContent(file.Id));

            var sheets = store.Sheets(file.Id);
            Assert.Equal(2, sheets.Count);
            var tor = sheets[0];
            Assert.Equal((0, "Töötajate loend", SheetKind.Tor, (string?)null, 1, 2, "TÖR"), (tor.SheetIndex, tor.SheetName, tor.Kind, tor.LayoutId, tor.HeaderRowNo, tor.RowCount, tor.DisplayName));
            Assert.Equal(TorHeaders.All, tor.Headers);
            Assert.Null(tor.Layout);
            var lhv = sheets[1];
            Assert.Equal((1, "LHV", SheetKind.Statement, "lhv-et-short", 1, 3, "LHV pank *1757", "EE497700771001131757"), (lhv.SheetIndex, lhv.SheetName, lhv.Kind, lhv.LayoutId, lhv.HeaderRowNo, lhv.RowCount, lhv.DisplayName, lhv.Account));
            Assert.Equal(((DateOnly?)new DateOnly(2026, 10, 5), (DateOnly?)new DateOnly(2026, 11, 2)), (lhv.FirstDate, lhv.LastDate));
            Assert.Equal(LayoutCatalog.Default.Find("lhv-et-short"), lhv.Layout);
            Assert.Equal(lhv, store.Sheet(lhv.Id));

            var rows = store.StatementRows(lhv.Id);
            Assert.Equal(parsed.Sheets[1].Statement!.Rows, rows.Select(r => r.Row));
            Assert.All(rows, r => Assert.Equal(lhv.Id, r.SheetId));
            Assert.Equal(rows[0], store.StatementRow(rows[0].Id));
            Assert.Equal("48712219406", rows[0].Row.CounterpartyCode);
            Assert.Equal(12, rows[0].Row.Cells.Count);

            var entries = store.TorEntries(company.Id);
            Assert.Equal(parsed.Sheets[0].Tor!.Entries, entries.Select(e => e.Entry));
            Assert.All(entries, e => Assert.Equal((tor.Id, company.Id), (e.SheetId, e.CompanyId)));
            Assert.Equal(entries, store.TorEntries(company.Id, file.Id));
            Assert.Empty(store.TorEntries(company.Id, Guid.NewGuid()));
        }
    }

    [Fact]
    public void The_same_bytes_are_not_imported_twice_and_nothing_recognised_is_not_imported_at_all()
    {
        var (store, company, _) = Setup();
        using (store)
        {
            var bytes = MixedWorkbook();
            var parsed = Importer.Read("palgad.xlsx", bytes);
            store.AddImport(company.Id, parsed, bytes);

            var again = Assert.Throws<ImportException>(() => store.AddImport(company.Id, parsed with { FileName = "koopia.xlsx" }, bytes));
            Assert.Contains("already imported", again.Message);
            Assert.Contains("palgad.xlsx", again.Message);

            var other = store.AddCompany("12223047", "Teine OÜ");
            Assert.NotNull(store.AddImport(other.Id, parsed, bytes)); // another company may hold the same bytes

            var stray = Tables.Xlsx("S", ["a", "b"], ["1", "2"]);
            var nothing = Assert.Throws<ImportException>(() => store.AddImport(company.Id, Importer.Read("stray.xlsx", stray), stray));
            Assert.Contains("no recognised sheet", nothing.Message);
        }
    }

    [Fact]
    public void A_period_records_the_files_its_run_used()
    {
        var (store, company, period) = Setup();
        using (store)
        {
            var bytes = MixedWorkbook();
            var file = store.AddImport(company.Id, Importer.Read("palgad.xlsx", bytes), bytes);

            store.LinkImport(period.Id, file.Id);
            store.LinkImport(period.Id, file.Id); // idempotent
            Assert.Equal(file, Assert.Single(store.PeriodImports(period.Id)));

            store.UnlinkImport(period.Id, file.Id);
            Assert.Empty(store.PeriodImports(period.Id));

            var other = store.AddCompany("12223047", "Teine OÜ");
            var otherPeriod = store.GetOrCreatePeriod(other.Id, 2026, 10);
            Assert.Throws<InvalidOperationException>(() => store.LinkImport(otherPeriod.Id, file.Id));
            Assert.Throws<KeyNotFoundException>(() => store.LinkImport(period.Id, Guid.NewGuid()));
        }
    }

    [Fact]
    public void Statement_rows_of_a_company_can_be_asked_for_by_date_range()
    {
        var (store, company, _) = Setup();
        using (store)
        {
            var bytes = MixedWorkbook();
            store.AddImport(company.Id, Importer.Read("palgad.xlsx", bytes), bytes);

            var october = store.StatementRows(company.Id, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
            Assert.Equal(["Mari Maasikas"], october.Select(r => r.Row.Counterparty)); // the fee row has no counterparty, Jaan is November
            Assert.Equal(["Mari Maasikas", "Jaan Tamm"], store.StatementRows(company.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)).Select(r => r.Row.Counterparty));
        }
    }

    [Fact]
    public void A_declared_line_points_at_its_bank_row_through_every_version_and_the_file_stays_while_it_does()
    {
        var (store, company, period) = Setup();
        using (store)
        {
            var bytes = MixedWorkbook();
            var file = store.AddImport(company.Id, Importer.Read("palgad.xlsx", bytes), bytes);
            var bankRow = store.StatementRows(store.Sheets(file.Id)[1].Id)[0];
            var person = store.AddPerson(Person.New(company.Id, "Mari", "Maasikas", "48712219406") with
            {
                FundedPensionMember = true, PensionRate = 0.02m, PensionCheckedOn = new DateOnly(2026, 11, 1), ResidencyCheckedOn = new DateOnly(2026, 11, 1), BasicExemptionApplied = true,
            });

            var draft = store.AddPayment(Draft(store, period, person, LineAction.R, bankRow.Id));
            Assert.Equal(bankRow.Id, draft.SourceRowId);
            Assert.Equal(bankRow.Id, Assert.Single(store.Payments(period.Id)).SourceRowId);

            var replaced = store.ReplaceDraft(draft.Id, draft with { NetPaid = 905.92m, SourceRowId = null });
            Assert.Equal(bankRow.Id, replaced.SourceRowId); // a replacement without a row keeps the draft's

            var removal = Assert.Throws<InvalidOperationException>(() => store.RemoveImport(file.Id));
            Assert.Contains("1 declared line", removal.Message);

            store.LinkImport(period.Id, file.Id);
            store.DeleteDraft(replaced.Id);
            store.RemoveImport(file.Id); // a Draft period's link goes with the file
            Assert.Empty(store.Imports(company.Id));
            Assert.Empty(store.PeriodImports(period.Id));
            Assert.Empty(store.TorEntries(company.Id));
            Assert.Null(store.StatementRow(bankRow.Id));
            Assert.Throws<KeyNotFoundException>(() => store.RemoveImport(file.Id));

            // a line added by hand has no row
            var byHand = store.AddPayment(Draft(store, period, person, LineAction.R, null));
            Assert.Null(byHand.SourceRowId);
        }
    }

    [Fact]
    public void Every_version_of_a_key_keeps_its_bank_row_and_a_row_of_another_company_or_of_nowhere_is_refused()
    {
        var (store, company, period) = Setup();
        using (store)
        {
            var bytes = MixedWorkbook();
            var file = store.AddImport(company.Id, Importer.Read("palgad.xlsx", bytes), bytes);
            var rows = store.StatementRows(store.Sheets(file.Id)[1].Id);
            var person = store.AddPerson(Person.New(company.Id, "Mari", "Maasikas", "48712219406") with
            {
                FundedPensionMember = true, PensionRate = 0.02m, PensionCheckedOn = new DateOnly(2026, 11, 1), ResidencyCheckedOn = new DateOnly(2026, 11, 1), BasicExemptionApplied = true,
            });

            Assert.Throws<KeyNotFoundException>(() => store.AddPayment(Draft(store, period, person, LineAction.R, Guid.NewGuid())));

            var other = store.AddCompany("12223047", "Teine OÜ");
            var otherFile = store.AddImport(other.Id, Importer.Read("teine.xlsx", bytes), bytes);
            var foreignRow = store.StatementRows(store.Sheets(otherFile.Id)[1].Id)[0];
            Assert.Contains("another company", Assert.Throws<InvalidOperationException>(() => store.AddPayment(Draft(store, period, person, LineAction.R, foreignRow.Id))).Message);

            // a fee line without a payee is a source for a declared line once the accountant has named the person it went to
            Assert.Equal(SkipReason.NoName, rows[1].Row.SkipReason);
            var onFee = store.AddPayment(Draft(store, period, person, LineAction.R, rows[1].Id));
            Assert.Equal(rows[1].Id, onFee.SourceRowId);
            store.DeleteDraft(onFee.Id);

            // R sent and accepted, then an M without a row inherits it; an M with another row is refused, and so is re-pointing the M draft
            var r = store.AddPayment(Draft(store, period, person, LineAction.R, rows[0].Id));
            var submission = store.AddSubmission(period.Id, "tsd.xml", "uid-1", [r.Id], DateTime.UtcNow);
            store.ResolveLine(r.Id, onServer: true);
            var m = store.AddPayment(Draft(store, period, person, LineAction.M, null, r.LineNumber));
            Assert.Equal(rows[0].Id, m.SourceRowId);
            Assert.Contains("another bank row", Assert.Throws<InvalidOperationException>(() => store.ReplaceDraft(m.Id, m with { SourceRowId = rows[2].Id })).Message);
            store.DeleteDraft(m.Id);
            Assert.Contains("another bank row", Assert.Throws<InvalidOperationException>(() => store.AddPayment(Draft(store, period, person, LineAction.M, rows[2].Id, r.LineNumber))).Message);

            // a refused line never moves the key counter past its hand-supplied key (four numbers were drawn above; 050 is not reserved)
            Assert.Throws<KeyNotFoundException>(() => store.AddPayment(Draft(store, period, person, LineAction.R, Guid.NewGuid(), "10235688-202610-01-050")));
            Assert.Equal("10235688-202610-01-005", store.NewLineNumber(period.Id, Section.EE0201001));

            // a bank row feeds one key: a second key on the row of the accepted R is refused, on add and on re-pointing
            Assert.Contains("already feeds line", Assert.Throws<InvalidOperationException>(() => store.AddPayment(Draft(store, period, person, LineAction.R, rows[0].Id))).Message);
            var draft = store.AddPayment(Draft(store, period, person, LineAction.R, rows[2].Id));
            Assert.Contains("already feeds line", Assert.Throws<InvalidOperationException>(() => store.ReplaceDraft(draft.Id, draft with { SourceRowId = rows[0].Id })).Message);
            Assert.Throws<InvalidOperationException>(() => store.ReplaceDraft(draft.Id, draft with { SourceRowId = foreignRow.Id }));
            Assert.Equal("10235688-202610-01-008", store.NewLineNumber(period.Id, Section.EE0201001)); // the fixture drew 006 and 007 above; the refused line itself moved nothing

            // a draft may be re-pointed at a free row of the company, never at a foreign one
            store.DeleteDraft(draft.Id);
            var byHand2 = store.AddPayment(Draft(store, period, person, LineAction.R, null));
            Assert.Equal(rows[2].Id, store.ReplaceDraft(byHand2.Id, byHand2 with { SourceRowId = rows[2].Id }).SourceRowId);
            Assert.Throws<InvalidOperationException>(() => store.ReplaceDraft(byHand2.Id, byHand2 with { SourceRowId = foreignRow.Id }));
            Assert.NotNull(submission);

            // a dead key (its T accepted) holds its bank row no more: the payment's new key, sent as R, takes the row
            var cancel = store.AddPayment(StoredPayment.New(period.Id, person.Id, Section.EE0201001, PaymentLine.Cancellation(r.LineNumber, 0), null, null));
            store.AddSubmission(period.Id, "tsd-t.xml", "uid-t", [cancel.Id], DateTime.UtcNow);
            store.ResolveLine(cancel.Id, onServer: true);
            var again = store.AddPayment(Draft(store, period, person, LineAction.R, rows[0].Id));
            Assert.Equal(rows[0].Id, again.SourceRowId);
            Assert.NotEqual(r.LineNumber, again.LineNumber);
            Assert.Contains("already feeds line", Assert.Throws<InvalidOperationException>(() => store.AddPayment(Draft(store, period, person, LineAction.R, rows[0].Id))).Message);
        }
    }

    [Fact]
    public void A_file_a_period_past_draft_used_cannot_be_removed()
    {
        var (store, company, period) = Setup();
        using (store)
        {
            var bytes = MixedWorkbook();
            var file = store.AddImport(company.Id, Importer.Read("palgad.xlsx", bytes), bytes);
            store.LinkImport(period.Id, file.Id);
            store.SetPeriodState(period.Id, PeriodState.Exported, DateTime.UtcNow);

            Assert.Contains("past Draft", Assert.Throws<InvalidOperationException>(() => store.RemoveImport(file.Id)).Message);
            Assert.Contains("Exported", Assert.Throws<InvalidOperationException>(() => store.UnlinkImport(period.Id, file.Id)).Message); // no way round through the link
            Assert.Single(store.Imports(company.Id));
            Assert.Single(store.PeriodImports(period.Id));

            var later = Tables.Xlsx("LHV", LayoutCatalog.Default.Find("lhv-et-short")!.Headers.ToArray<object?>(), ["EE497700771001131757", 9, new DateTime(2026, 10, 9), "EE12", "Mari Maasikas", "D", -1d, null, null, "palk", "EUR"]);
            var correction = store.AddImport(company.Id, Importer.Read("parandus.xlsx", later), later);
            store.LinkImport(period.Id, correction.Id); // a correction run may still record a new file
            Assert.Equal([file.Id, correction.Id], store.PeriodImports(period.Id).Select(f => f.Id));
        }
    }

    [Fact]
    public void A_deleted_layout_must_leave_the_merged_catalog_usable_too()
    {
        var (store, _, _) = Setup();
        using (store)
        {
            var lhv = LayoutCatalog.Default.Find("lhv-et-long")!;
            store.SaveLayout(lhv with { Headers = lhv.Headers.Select(h => h == "Summa" ? "Summa EUR" : h).ToList(), Roles = lhv.Roles with { Amount = "Summa EUR" } }); // the owner's own LHV
            store.SaveLayout(lhv with { Id = "old-lhv" }); // the bank's old header row, free now

            Assert.Contains("same header row", Assert.Throws<ImportException>(() => store.DeleteLayout("lhv-et-long")).Message); // the default would come back and collide
            Assert.Equal(2, store.Layouts().Count);

            store.DeleteLayout("old-lhv");
            store.DeleteLayout("lhv-et-long");
            Assert.Empty(store.Layouts());
        }
    }

    [Fact]
    public void The_date_range_query_keeps_to_the_company_and_its_bounds_and_leaves_undated_rows_to_the_sheet_query()
    {
        var (store, company, _) = Setup();
        using (store)
        {
            var headers = LayoutCatalog.Default.Find("lhv-et-short")!.Headers.ToArray<object?>();
            var bytes = Tables.Xlsx("LHV",
                headers,
                ["EE497700771001131757", 1, new DateTime(2026, 9, 30), "EE12", "September", "D", -1d, null, null, "palk", "EUR"],
                ["EE497700771001131757", 2, new DateTime(2026, 10, 1), "EE12", "First", "D", -2d, null, null, "palk", "EUR"],
                ["EE497700771001131757", 3, new DateTime(2026, 10, 31), "EE12", "Last", "D", -3d, null, null, "palk", "EUR"],
                ["EE497700771001131757", 4, new DateTime(2026, 11, 1), "EE12", "November", "D", -4d, null, null, "palk", "EUR"],
                ["EE497700771001131757", 5, "eile", "EE12", "Undated", "D", -5d, null, null, "palk", "EUR"]);
            var file = store.AddImport(company.Id, Importer.Read("lhv.xlsx", bytes), bytes);

            var other = store.AddCompany("12223047", "Teine OÜ");
            var otherBytes = Tables.Xlsx("LHV", headers, ["EE787700771001177795", 1, new DateTime(2026, 10, 15), "EE12", "Other", "D", -9d, null, null, "palk", "EUR"]);
            store.AddImport(other.Id, Importer.Read("teine.xlsx", otherBytes), otherBytes);

            var october = store.StatementRows(company.Id, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
            Assert.Equal(["First", "Last"], october.Select(r => r.Row.Counterparty)); // bounds inclusive, the other company's rows absent

            var sheet = Assert.Single(store.Sheets(file.Id));
            var undated = store.StatementRows(sheet.Id).Single(r => r.Row.Counterparty == "Undated");
            Assert.Null(undated.Row.Date);
            Assert.True(undated.Row.IsPayment);
            Assert.Contains("eile", undated.Row.Problem);
            Assert.Contains(sheet.Problems, p => p.Code == ImportProblem.BadDate && p.RowNo == 6); // the sheet carries it too
            Assert.DoesNotContain(store.StatementRows(company.Id, new DateOnly(2000, 1, 1), new DateOnly(2099, 12, 31)), r => r.Row.Counterparty == "Undated");
        }
    }

    [Fact]
    public void Tor_rows_come_file_by_file_in_import_order()
    {
        var (store, company, _) = Setup();
        using (store)
        {
            var first = Tables.Xlsx("Töötajate loend", TorHeaders.All.ToArray<object?>(),
                ["1", 48712219406d, "MAASIKAS", "MARI", new DateTime(2024, 2, 15), 1, "Tööleping", 2, "Kehtiv", null, null, null, null, null, null, null, null, "EE", null, null, null, null, null, null, null, null, 10235688, "Näidis OÜ"]);
            var second = Tables.Xlsx("Töötajate loend", TorHeaders.All.ToArray<object?>(),
                ["2", 38810140309d, "TAMM", "JAAN", new DateTime(2020, 1, 1), 1, "Tööleping", 2, "Kehtiv", null, null, null, null, null, null, null, null, "EE", null, null, null, null, null, null, null, null, 10235688, "Näidis OÜ"],
                ["1", 48712219406d, "MAASIKAS", "MARI", new DateTime(2024, 2, 15), 1, "Tööleping", 2, "Kehtiv", null, null, null, null, null, null, null, null, "EE", null, null, null, null, null, null, null, null, 10235688, "Näidis OÜ"]);
            var f1 = store.AddImport(company.Id, Importer.Read("tor-1.xlsx", first), first);
            var f2 = store.AddImport(company.Id, Importer.Read("tor-2.xlsx", second), second);

            Assert.Equal([f1.Id, f2.Id], store.Imports(company.Id).Select(f => f.Id));
            Assert.Equal(["MARI", "JAAN", "MARI"], store.TorEntries(company.Id).Select(e => e.Entry.FirstName));
            Assert.Equal(["JAAN", "MARI"], store.TorEntries(company.Id, f2.Id).Select(e => e.Entry.FirstName));
        }
    }

    [Fact]
    public void A_parse_that_does_not_belong_to_the_bytes_is_refused_and_the_encoding_and_problems_are_kept()
    {
        var (store, company, _) = Setup();
        using (store)
        {
            var bytes = MixedWorkbook();
            var parsed = Importer.Read("palgad.xlsx", bytes);
            Assert.Throws<ArgumentException>(() => store.AddImport(company.Id, parsed with { Sha256 = new string('0', 64) }, bytes));
            Assert.Throws<ArgumentException>(() => store.AddImport(company.Id, parsed with { Size = parsed.Size + 1 }, bytes));
            Assert.Throws<ArgumentException>(() => store.AddImport(company.Id, parsed, bytes.Concat(new byte[] { 0 }).ToArray()));
            Assert.Empty(store.Imports(company.Id));

            var upper = store.AddImport(company.Id, parsed with { Sha256 = parsed.Sha256.ToUpperInvariant() }, bytes);
            Assert.Equal(parsed.Sha256, upper.Sha256); // stored lower-case, whatever the caller wrote
            Assert.Null(upper.Encoding);

            var csv = "\"Kliendi konto\";\"Dok nr\";\"Makse kuupäev\";\"Saaja/maksja konto\";\"Saaja/maksja nimi\";\"Saaja/maksja pank\";\"Tühi (ei kasutata)\";\"Deebet/Kreedit (D/C)\";\"Summa\";\"Viitenumber\";\"Arhiveerimistunnus\";\"Selgitus\";\"Teenustasu\";\"VALUUTA TÄHIS\";\"Isikukood / Reg. nr\";\r\n"
                      + "\"EE162200221054367099\";\"26034\";\"eile\";\"EE4977\";\"StopFire OÜ\";\"689\";\"\";\"X\";\"-8500,00\";\"\";\"2026100802767783\";\"Firma SM\";\"0\";\"EUR\";\"\"\r\n";
            var csvBytes = CsvTableReader.Windows1257.GetBytes(csv);
            var swedbank = store.AddImport(company.Id, Importer.Read("statement.csv", csvBytes), csvBytes);
            Assert.Equal(CsvTableReader.BalticEncodingName, swedbank.Encoding);
            Assert.Equal(CsvTableReader.BalticEncodingName, Assert.Single(store.Imports(company.Id), f => f.Id == swedbank.Id).Encoding);
            var sheet = Assert.Single(store.Sheets(swedbank.Id));
            Assert.Equal([ImportProblem.BadDate, ImportProblem.BadMarker], sheet.Problems.Select(p => p.Code));
            Assert.Equal(Importer.Read("statement.csv", csvBytes).Sheets[0].Statement!.Problems, sheet.Problems); // what the parser said, as it said it
            Assert.Equal(sheet, store.Sheet(sheet.Id));

            // the reader's warning lands among the sheet's problems, so the stored rows never look complete when they are not
            var unclosed = Encoding.UTF8.GetBytes("Дата;Плательщик / Получатель;Номер счета;Назначение платежа;Сумма;Валюта\n2026-10-01;\"Mari Maasikas;LT1;palk;100,00;EUR\n2026-10-02;Jaan Tamm;LT2;palk;5,00;EUR\n");
            var paysera = store.AddImport(company.Id, Importer.Read("p.csv", unclosed), unclosed);
            var payseraSheet = Assert.Single(store.Sheets(paysera.Id));
            Assert.Contains(payseraSheet.Problems, p => p.Code == ImportProblem.ReaderWarning && p.Message.Contains("never closed"));
        }
    }

    [Fact]
    public void A_saved_layout_must_leave_the_merged_catalog_usable()
    {
        var (store, _, _) = Setup();
        using (store)
        {
            var copy = LayoutCatalog.Default.Find("lhv-et-long")! with { Id = "lhv-copy" };
            Assert.Contains("same header row", Assert.Throws<ImportException>(() => store.SaveLayout(copy)).Message);
            Assert.Empty(store.Layouts());

            var mine = new StatementLayout("my-bank", "My Bank", LayoutKind.Bank, ["Nimi", "Summa", "Kuupäev", "DC"], new LayoutRoles(null, "Kuupäev", "Nimi", "Summa", null, "DC"), DebitMarker: "D");
            store.SaveLayout(mine);
            Assert.Throws<ImportException>(() => store.SaveLayout(mine with { Id = "my-bank-2" })); // the same header row as a saved one
            store.SaveLayout(mine with { Bank = "Renamed" }); // the same id replaces itself
            Assert.Equal("Renamed", Assert.Single(store.Layouts()).Bank);
        }
    }

    [Fact]
    public void The_shipped_scripts_are_pinned()
    {
        // a shipped migration script is never edited: an existing firm file already ran it; a change goes in the next script
        var hashes = SqliteStore.MigrationScripts.Select(s => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(s.Replace("\r\n", "\n"))))).ToList();

        Assert.Equal(6, hashes.Count);
        Assert.Equal("c765c3c39d8e978730321685f8b5d5d0f69b47668a23679b819baec2b24ed59c", hashes[0]); // v1 as committed in 3c86599
        Assert.Equal("001c1f2471e82c2701c5dc910d6d6d158ac6b73fcef7e7eab78d9bde8097fcf7", hashes[1]); // v2 as committed in 5a89cc8
        Assert.Equal("9b309118542e3ea9e46d0ed0e928a41f255b66a5837c9b3be0a69559c2b5dda3", hashes[2]); // v3 as committed in d33c564
        Assert.Equal("5d8615b641ae61eb68019c89580dc294fe91a841b27fdd1cd85c75d95809adcd", hashes[3]); // v4 as committed in 1d47bf1
        Assert.Equal("59fcc4f15494178f7cfb6367345eeb2b62d210fd20bb8cc3d0569541529a71c1", hashes[4]); // v5 as committed in 53640df
        Assert.Equal("25a0f31daee23757ad8a200bd6db133fd80728bc87d20e215c8c85a70ee3185c", hashes[5]); // v6 as committed with S8
    }

    [Fact]
    public void The_owners_layouts_are_kept_and_merged_over_the_defaults()
    {
        var (store, _, _) = Setup();
        using (store)
        {
            Assert.Empty(store.Layouts());
            var edited = LayoutCatalog.Default.Find("swedbank-et")! with { Notes = "muudetud" };
            var added = new StatementLayout("my-bank", "My Bank", LayoutKind.Bank, ["Nimi", "Summa", "Kuupäev", "DC"], new LayoutRoles(null, "Kuupäev", "Nimi", "Summa", null, "DC"), DebitMarker: "D");

            store.SaveLayout(edited);
            store.SaveLayout(added);
            store.SaveLayout(added with { Bank = "My Bank 2" }); // upsert

            Assert.Equal(["my-bank", "swedbank-et"], store.Layouts().Select(l => l.Id));
            var catalog = LayoutCatalog.Default.WithOverrides(store.Layouts());
            Assert.Equal("muudetud", catalog.Find("swedbank-et")!.Notes);
            Assert.Equal("My Bank 2", catalog.Find("my-bank")!.Bank);

            store.DeleteLayout("my-bank");
            Assert.Equal(["swedbank-et"], store.Layouts().Select(l => l.Id));

            var broken = added with { Roles = added.Roles with { Amount = "Puudub" } };
            Assert.Throws<ImportException>(() => store.SaveLayout(broken));
        }
    }

    [Fact]
    public void A_v1_store_is_upgraded_to_v2_with_its_payments_intact()
    {
        var path = Path.Combine(Path.GetTempPath(), $"satellestsd-{Guid.NewGuid():N}.db");
        try
        {
            Guid paymentId;
            using (var v1 = SqliteStore.OpenAtVersion(path, 1))
            {
                var company = v1.AddCompany("10235688", "Näidis OÜ");
                var period = v1.GetOrCreatePeriod(company.Id, 2026, 10);
                // v1 has neither the v4 source columns nor source_row_id: write the person and the line the v1 way
                var person = Person.New(company.Id, "Mari", "Maasikas", "48712219406") with
                {
                    Id = Guid.NewGuid(), FundedPensionMember = true, PensionRate = 0.02m, PensionCheckedOn = new DateOnly(2026, 11, 1), ResidencyCheckedOn = new DateOnly(2026, 11, 1), BasicExemptionApplied = true,
                };
                v1.ExecuteRaw(
                    """
                    INSERT INTO person (id, company_id, first_name, last_name, personal_code, residency, residency_checked_on, funded_pension_member, pension_rate, pension_checked_on,
                                        on_pension, exemption_applied, minimum_applies, a1_case)
                    VALUES ($id, $company, 'Mari', 'Maasikas', '48712219406', 'Resident', '2026-11-01', 1, '0.02', '2026-11-01', 0, 1, 1, 'None')
                    """,
                    ("$id", person.Id.ToString("N")), ("$company", company.Id.ToString("N")));
                var line = Draft(v1, period, person, LineAction.R, null);
                v1.ExecuteRaw(
                    """
                    INSERT INTO payment (id, period_id, person_id, section, line_number, version, action, status, line_json, created_at)
                    VALUES ($id, $period, $person, 'EE0201001', $line, 1, 'R', 'Accepted', $json, '2026-11-03T10:00:00.000Z')
                    """,
                    ("$id", (paymentId = Guid.NewGuid()).ToString("N")), ("$period", period.Id.ToString("N")), ("$person", person.Id.ToString("N")), ("$line", line.LineNumber),
                    ("$json", System.Text.Json.JsonSerializer.Serialize(line.Line, new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull })));
                Assert.Equal(1L, v1.ScalarRaw<long>("SELECT MAX(version) FROM schema_version"));
                Assert.Equal(0L, v1.ScalarRaw<long>("SELECT COUNT(*) FROM sqlite_master WHERE name = 'import_file'"));
            }

            using var v2 = SqliteStore.Open(path);
            Assert.Equal((long)SqliteStore.SchemaVersion, v2.ScalarRaw<long>("SELECT MAX(version) FROM schema_version"));
            var company2 = v2.FindCompany("10235688")!;
            var period2 = Assert.Single(v2.Periods(company2.Id));
            var payment = Assert.Single(v2.Payments(period2.Id));
            Assert.Equal((paymentId, PaymentStatus.Accepted, (Guid?)null), (payment.Id, payment.Status, payment.SourceRowId));
            Assert.Empty(v2.Imports(company2.Id));
            Assert.Empty(v2.Layouts());

            var bytes = MixedWorkbook();
            var file = v2.AddImport(company2.Id, Importer.Read("palgad.xlsx", bytes), bytes);
            Assert.Equal(2, v2.Sheets(file.Id).Count);

            // the column the upgrade added works like on a fresh file: a line points at a row, and the file then stays
            var person2 = Assert.Single(v2.Persons(company2.Id));
            var row = v2.StatementRows(v2.Sheets(file.Id)[1].Id)[0];
            var linked = v2.AddPayment(Draft(v2, period2, person2, LineAction.R, row.Id));
            Assert.Equal(row.Id, Assert.Single(v2.Payments(period2.Id), p => p.Id == linked.Id).SourceRowId);
            Assert.Throws<InvalidOperationException>(() => v2.RemoveImport(file.Id));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static StoredPayment Draft(SqliteStore store, Period period, Person person, LineAction action, Guid? sourceRowId, string? lineNumber = null)
    {
        var facts = person.FactsFor(period.Year);
        var result = Engine.Calculate(new TaxInput(PaymentTypes.Get("10"), 1000m, false, 700m), facts);
        var key = lineNumber ?? store.NewLineNumber(period.Id, Section.EE0201001);
        var line = DeclarationLineFactory.Create(key, 0, action, PaymentTypes.Get("10"), facts, person.PayeeId!, new DateOnly(period.Year, period.Month, 5), result);
        return StoredPayment.New(period.Id, person.Id, Section.EE0201001, line, facts, result, netPaid: result.Net, sourceRef: "LHV pank *1757 [2]", sourceRowId: sourceRowId);
    }
}
