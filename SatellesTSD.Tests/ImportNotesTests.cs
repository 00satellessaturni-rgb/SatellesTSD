using System.Text;
using SatellesTSD.Core.Import;
using SatellesTSD.Core.Review;
using SatellesTSD.Core.Store;
using SatellesTSD.Infrastructure;

namespace SatellesTSD.Tests;

/// <summary>The import's notes carry their code and figures beside the Core's English, which stays as it was (the window
/// words them in Estonian by the code); the re-read's and the store's refusals of a file carry their codes too.</summary>
public class ImportNotesTests
{
    private const string Own = "10235688";

    private static (SqliteStore Store, Company Company, Period Period) Setup()
    {
        var store = SqliteStore.InMemory();
        var company = store.AddCompany(Own, "Näidis OÜ");
        return (store, company, store.GetOrCreatePeriod(company.Id, 2026, 10));
    }

    /// <summary>A workbook with a sheet of every kind the import log speaks of: empty, a header row no layout knows, one near a
    /// layout, a TÖR lacking columns, a TÖR copy with a column of its own and another employer, a hidden SEB export with a
    /// payment row above its header and a row with two faults.</summary>
    private static byte[] EverySheet()
    {
        var lhv = LayoutCatalog.Default.Find("lhv-et-short")!;
        var renamed = lhv.Headers.Select(h => h == lhv.Roles.Amount ? "Summa EUR" : h).ToArray<object?>();
        var seb = LayoutCatalog.Default.Find("seb-et")!;
        object?[] torRow = ["1", 48712219406d, "MAASIKAS", "MARI", new DateTime(2024, 2, 15), 1, "Tööleping", 2, "Kehtiv", null, null, null, null, null, null, null, null, "EE", null, null, null, null, null, null, null, null, 12345678, "Teine OÜ", "x"];
        return Tables.Xlsx(
            ("Tühi", [], false),
            ("Kirjad", [["a", "b", "c"], ["1", "2", "3"]], false),
            ("Võõras", [renamed, ["EE497700771001131757", 1, new DateTime(2026, 10, 5), "EE12", "Mari Maasikas", "D", -905.92, null, "2026100500000001", "palk", "EUR"]], false),
            ("Pooleli", [TorHeaders.All.Take(4).ToArray<object?>(), ["1", 48712219406d, "MAASIKAS", "MARI"]], false),
            ("TÖR", [[.. TorHeaders.All, "Kontroll"], torRow], false),
            ("SEB", [
                ["EE591010220215133228", "TT", "02.10.2026", "EE497700771001131757", "StopFire OÜ", "LHVBEE22", "H", "D", "500,00", "", "RO4262101896L02", "Reserv SEB", "0", "EUR", ""],
                seb.Headers.ToArray<object?>(),
                ["EE591010220215133228", "TM168358936", "01.10.2026", "", "AS SEB PANK", "EEUHEE2X", "H", "C", "1,01", "", "RO4248947827L02", "intresside väljamakse EUR", "0", "EUR", ""],
                ["EE591010220215133228", "TM168358937", "eile", "", "AS SEB PANK", "EEUHEE2X", "H", "C", "palju", "", "RO4248947827L03", "intress", "0", "EUR", ""]], true));
    }

    [Fact]
    public void Every_note_carries_its_code_and_figures_and_the_English_stays_as_it_was()
    {
        var (store, _, period) = Setup();
        using (store)
        {
            var outcome = new PeriodPipeline(store, ReviewFixtures.Importer).ImportFile(period.Id, "koik.xlsx", EverySheet());
            Assert.True(outcome.Succeeded, outcome.Error);
            Assert.Equal(outcome.Notes.Select(n => n.English), outcome.Messages); // the English is the notes' own
            ImportNote Of(string code) => Assert.Single(outcome.Notes, n => n.Code == code);

            var empty = Of(SheetNotes.Empty);
            Assert.Equal(("Tühi", 1, "sheet 'Tühi': The sheet is empty."), (empty.Sheet, empty.SheetNo, empty.English));
            Assert.Equal(("Kirjad", 2, "sheet 'Kirjad': No statement layout has this header row."), (Of(SheetNotes.NoLayout).Sheet, Of(SheetNotes.NoLayout).SheetNo, Of(SheetNotes.NoLayout).English));

            var lhv = LayoutCatalog.Default.Find("lhv-et-short")!;
            var nearest = Of(SheetNotes.NearestLayout);
            Assert.Equal(["lhv-et-short"], nearest.Args);
            var column = lhv.Headers.ToList().IndexOf(lhv.Roles.Amount) + 1;
            var difference = Assert.Single(nearest.Differences);
            Assert.Equal(new HeaderDifference(1, "lhv-et-short", column, "Summa EUR", lhv.Roles.Amount), difference);
            Assert.Equal($"sheet 'Võõras': Nearest layout 'lhv-et-short': row 1: column {column}: 'Summa EUR' is not '{lhv.Roles.Amount}'", nearest.English);

            var lacks = Of(SheetNotes.TorLacks);
            Assert.NotEmpty(lacks.Args);
            Assert.Equal($"sheet 'Pooleli': Looks like the TÖR export but lacks the columns {string.Join(", ", lacks.Args)}.", lacks.English);

            Assert.Equal(["Kontroll"], Of(TorParser.ExtraColumns).Args);
            Assert.Equal(["12345678", Own], Of(TorParser.EmployerMismatch).Args);
            Assert.Equal(5, Of(TorParser.EmployerMismatch).SheetNo);

            Assert.Equal("sheet 'SEB': the sheet is hidden in the workbook; it was read all the same.", Of(SheetNotes.Hidden).English);
            Assert.Equal(1, Of(ImportProblem.AboveHeader).RowNo);
            var rows = Of(SheetNotes.RowProblems);
            Assert.Equal(1, rows.Count);
            Assert.Equal(["BAD_AMOUNT 1", "BAD_DATE 1"], rows.Args);
            Assert.Contains("sheet 'SEB': 1 row(s) with a problem (BAD_AMOUNT 1, BAD_DATE 1).", outcome.Messages);
        }
    }

    [Fact]
    public void The_row_problems_and_the_registers_problems_carry_their_figures()
    {
        var parsed = ReviewFixtures.Importer.Read("koik.xlsx", EverySheet());
        var seb = parsed.Sheets.Single(s => s.Name == "SEB");
        Assert.Equal(["eile"], Assert.Single(seb.Problems, p => p.Code == ImportProblem.BadDate).Args);
        Assert.Equal(["palju"], Assert.Single(seb.Problems, p => p.Code == ImportProblem.BadAmount).Args);
        var tor = parsed.Sheets.Single(s => s.Name == "TÖR").Tor!;
        var mismatch = TorParser.CheckEmployer(tor, Own)!;
        Assert.Equal("The export is of employer 12345678, not of 10235688.", mismatch.Message);

        // a TÖR row without a code, a name and a date: the figures beside the English (never shown, kept with the sheet)
        var sheet = Tables.FirstSheet(Tables.Xlsx("TÖR", TorHeaders.All.ToArray<object?>(),
            ["1", null, "MAASIKAS", "MARI", "eile", 1, "Tööleping", 2, "Kehtiv", null, null, null, null, null, null, null, null, "EE", null, null, null, null, null, null, null, null, null, "Näidis OÜ"],
            ["2", 48712219406d, "", "JAAN", new DateTime(2024, 2, 15), 1, "Tööleping", 2, "Kehtiv", null, null, null, null, null, null, null, null, "EE", null, null, null, null, null, null, null, null, Own, "Näidis OÜ"]));
        var problems = TorParser.Parse(sheet, TorParser.Detect(sheet, out _)!).Problems;
        Assert.Equal(["MARI", "MAASIKAS"], Assert.Single(problems, p => p.Code == TorParser.NoCode).Args);
        Assert.Equal(["48712219406", "last"], Assert.Single(problems, p => p.Code == TorParser.NoName).Args);
        Assert.Equal([TorHeaders.StartDate, "eile"], Assert.Single(problems, p => p.Code == ImportProblem.BadDate).Args);
        var codeless = Assert.Single(problems, p => p.Code == TorParser.NoEmployerCode);
        Assert.Equal(["1"], codeless.Args);
        Assert.StartsWith("1 row(s) carry no employer registry code", codeless.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_readers_unclosed_quote_carries_its_record_and_the_English_keeps_its_place()
    {
        var (store, _, period) = Setup();
        using (store)
        {
            var headers = string.Join(",", LayoutCatalog.Default.Find("lhv-et-short")!.Headers);
            var csv = Encoding.UTF8.GetBytes(headers + "\n" + "EE497700771001131757,1,2026-10-05,EE12,Mari Maasikas,D,-905.92,,2026100500000001,palk,EUR\n" + "EE497700771001131757,2,2026-10-06,EE13,\"Jaan Tamm,D,-300,,,töövõtt,EUR\n");
            var outcome = new PeriodPipeline(store, ReviewFixtures.Importer).ImportFile(period.Id, "lhv.csv", csv);
            Assert.True(outcome.Succeeded, outcome.Error);
            var quote = outcome.Notes[0];
            Assert.Equal((SheetNotes.UnclosedQuote, (string?)null, 0), (quote.Code, quote.Sheet, quote.SheetNo));
            var record = Assert.Single(quote.Args);
            Assert.Equal($"a quote opened at record {record} is never closed; everything after it was read as that one field.", quote.English); // the file's name taken off, as before
        }
    }

    [Fact]
    public void The_re_read_and_the_stores_refusals_of_a_file_carry_their_codes()
    {
        var (store, company, period) = Setup();
        using (store)
        {
            var pipeline = new PeriodPipeline(store, ReviewFixtures.Importer);
            var file = pipeline.ImportFile(period.Id, "palgad.xlsx", ReviewFixtures.Workbook()).File!;
            Assert.Equal(ImportRefusal.NotListed, ImportRefusal.CodeOf(Assert.Throws<KeyNotFoundException>(() => pipeline.RereadFile(period.Id, Guid.NewGuid()))));
            Assert.Equal(ImportRefusal.PeriodNotFound, ImportRefusal.CodeOf(Assert.Throws<KeyNotFoundException>(() => pipeline.RereadFile(Guid.NewGuid(), file.Id))));
            Assert.Equal(ImportRefusal.NotFound, ImportRefusal.CodeOf(Assert.Throws<KeyNotFoundException>(() => store.LinkImport(period.Id, Guid.NewGuid()))));
            Assert.Equal(ImportRefusal.PeriodNotFound, ImportRefusal.CodeOf(Assert.Throws<KeyNotFoundException>(() => store.LinkImport(Guid.NewGuid(), file.Id))));
            var other = store.AddCompany("99999999", "Teine OÜ");
            Assert.Equal(ImportRefusal.OtherCompany, ImportRefusal.CodeOf(Assert.Throws<InvalidOperationException>(() => store.LinkImport(store.GetOrCreatePeriod(other.Id, 2026, 10).Id, file.Id))));
            store.SetPeriodState(period.Id, PeriodState.Exported, DateTime.UtcNow);
            Assert.Equal(ImportRefusal.PeriodNotDraft, ImportRefusal.CodeOf(Assert.Throws<InvalidOperationException>(() => store.UnlinkImport(period.Id, file.Id))));
            Assert.Single(store.Imports(company.Id));
        }
    }
}
