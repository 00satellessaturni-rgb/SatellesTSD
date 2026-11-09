using SatellesTSD.Core.Import;
using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Review;
using SatellesTSD.Core.Store;
using SatellesTSD.Core.Tax;
using SatellesTSD.Infrastructure;

namespace SatellesTSD.Tests;

/// <summary>The matching half of the store: a period's run stored and read back whole, replaced by a re-run, kept
/// past Draft; the owner's catalog; the files a run took.</summary>
public class MatchStoreTests
{
    private static readonly Importer Importer = new(LayoutCatalog.Default, new ExcelTableReader());

    private static byte[] Workbook() => Tables.Xlsx(
        ("Töötajate loend", [TorHeaders.All.ToArray<object?>(),
            ["1", 48712219406d, "MAASIKAS", "MARI", new DateTime(2024, 2, 15), 1, "Tööleping", 2, "Kehtiv", null, null, null, null, null, null, null, null, "EE", null, null, null, null, null, null, null, null, 10235688, "Näidis OÜ"],
            ["2", 38810140309d, "TAMM", "JAAN", new DateTime(2020, 1, 1), 600, "VÕS leping", 4, "Lõpetatud", new DateTime(2026, 9, 30), null, null, null, null, null, null, null, "EE", null, null, null, null, null, null, null, null, 10235688, "Näidis OÜ"]], false),
        ("LHV", [LayoutCatalog.Default.Find("lhv-et-short")!.Headers.ToArray<object?>(),
            ["EE497700771001131757", 1, new DateTime(2026, 10, 5), "EE12", "Mari Maasikas", "D", -905.92, null, "2026100500000001", "palk", "EUR", 48712219406d],
            ["EE497700771001131757", 2, new DateTime(2026, 10, 6), null, null, "D", -0.38, null, null, "Teenustasu", "EUR"],
            ["EE497700771001131757", 3, new DateTime(2026, 11, 2), "EE13", "Jaan Tamm", "D", -300d, null, "2026110200000002", "töövõtt", "EUR"]], false));

    private static (SqliteStore Store, Company Company, Period Period, ImportFile File) Setup()
    {
        var store = SqliteStore.InMemory();
        var company = store.AddCompany("10235688", "Näidis OÜ");
        var period = store.GetOrCreatePeriod(company.Id, 2026, 10);
        var bytes = Workbook();
        var file = store.AddImport(company.Id, Importer.Read("palgad.xlsx", bytes), bytes);
        store.LinkImport(period.Id, file.Id);
        return (store, company, period, file);
    }

    [Fact]
    public void A_run_is_stored_and_read_back_whole()
    {
        var (store, company, period, file) = Setup();
        using (store)
        {
            var runner = new MatchRunner(store);
            var result = runner.Run(period.Id);

            var run = store.MatchRun(period.Id);
            Assert.NotNull(run);
            Assert.Equal(period.Id, run.PeriodId);
            Assert.Equal(MatchCatalog.Default.ToJson(), run.CatalogJson);
            Assert.Equal(result.Problems, run.Problems);

            var persons = store.MatchPersons(run.Id);
            Assert.Equal(["48712219406", "38810140309"], persons.Select(p => p.Key));
            var entries = store.TorEntries(company.Id, file.Id);
            Assert.Equal([entries[0].Id], persons[0].EntryIds);
            Assert.Equal(entries[0].Id, persons[0].ActiveEntryId);
            Assert.Null(persons[1].ActiveEntryId); // ended in September
            Assert.Equal((0, "MAASIKAS", "MARI", "48712219406"), (persons[0].Order, persons[0].LastName, persons[0].FirstName, persons[0].PersonalCode));

            var sheet = store.Sheets(file.Id)[1];
            var source = Assert.Single(store.MatchSources(run.Id));
            Assert.Equal((sheet.Id, "LHV pank *1757", "lhv-et-short", 0), (source.SheetId, source.Name, source.LayoutId, source.Order));

            var hits = store.MatchHits(run.Id);
            var rows = store.StatementRows(sheet.Id);
            Assert.Equal(["[Palgad]", "kuupaev ei kuulu perioodi [2026-10]", ""], hits.Select(h => h.Text));
            Assert.Equal([rows[0].Id, rows[2].Id, rows[1].Id], hits.Select(h => h.RowId));
            Assert.Equal([persons[0].Id, persons[1].Id, null], hits.Select(h => h.PersonId));
            Assert.Equal([VerdictKind.Matched, VerdictKind.None, VerdictKind.None], hits.Select(h => h.VerdictKind));
            Assert.Equal(("Palgad", true, true), (hits[0].Category, hits[0].InPeriod, hits[0].Counted));
            Assert.Equal(["Palgad"], hits[0].Matched);
            Assert.Equal([0, 1, 2], hits.Select(h => h.Order));

            var loaded = store.LoadMatchResult(period.Id);
            Assert.Equal(result, loaded);
            Assert.Equal(905.92m, loaded!.SalaryTotal("48712219406"));
        }
    }

    [Fact]
    public void A_second_run_replaces_the_first_and_a_period_past_draft_keeps_its_run()
    {
        var (store, _, period, _) = Setup();
        using (store)
        {
            var runner = new MatchRunner(store);
            runner.Run(period.Id);
            var first = store.MatchRun(period.Id)!;
            runner.Run(period.Id);
            var second = store.MatchRun(period.Id)!;
            Assert.NotEqual(first.Id, second.Id);
            Assert.Equal(3, store.MatchHits(second.Id).Count);
            Assert.Empty(store.MatchHits(first.Id));

            store.SetPeriodState(period.Id, PeriodState.Exported, DateTime.UtcNow);
            Assert.Contains("Exported", Assert.Throws<InvalidOperationException>(() => runner.Run(period.Id)).Message);
            Assert.Throws<InvalidOperationException>(() => store.ClearMatchRun(period.Id));
            Assert.Equal(second, store.MatchRun(period.Id));

            store.SetPeriodState(period.Id, PeriodState.Draft, DateTime.UtcNow);
            store.ClearMatchRun(period.Id);
            Assert.Null(store.MatchRun(period.Id));
            Assert.Null(store.LoadMatchResult(period.Id));
            Assert.Empty(store.MatchHits(second.Id));
        }
    }

    [Fact]
    public void Another_employers_register_rows_are_left_out_of_the_run_and_said_once()
    {
        var store = SqliteStore.InMemory();
        using (store)
        {
            var company = store.AddCompany("10235688", "Näidis OÜ");
            var period = store.GetOrCreatePeriod(company.Id, 2026, 10);
            object?[] Row(string torId, double? code, string last, string first, object? employerCode, string employer) =>
                [torId, code, last, first, new DateTime(2024, 2, 15), 1, "Tööleping", 2, "Kehtiv", null, null, null, null, null, null, null, null, "EE", null, null, null, null, null, null, null, null, employerCode, employer];
            var bytes = Tables.Xlsx(
                ("Töötajate loend", [TorHeaders.All.ToArray<object?>(),
                    Row("1", 48712219406d, "MAASIKAS", "MARI", 10235688, "Näidis OÜ"),
                    Row("2", 38810140309d, "TAMM", "JAAN", null, "Näidis OÜ"), // no code: the company's
                    Row("3", 60001010008d, "KASK", "PEETER", 99999999, "Teine OÜ"),
                    Row("4", 50001010006d, "SEPP", "ANTS", 99999999, "Teine OÜ")], false),
                ("LHV", [LayoutCatalog.Default.Find("lhv-et-short")!.Headers.ToArray<object?>(),
                    ["EE497700771001131757", 1, new DateTime(2026, 10, 5), "EE12", "Mari Maasikas", "D", -905.92, null, "2026100500000001", "palk", "EUR"],
                    ["EE497700771001131757", 2, new DateTime(2026, 10, 5), "EE13", "Peeter Kask", "D", -500d, null, "2026100500000002", "palk", "EUR"]], false));
            var file = store.AddImport(company.Id, Importer.Read("palgad.xlsx", bytes), bytes);
            store.LinkImport(period.Id, file.Id);

            var result = new MatchRunner(store).Run(period.Id);

            Assert.Equal(["48712219406", "38810140309"], result.Persons.Select(p => p.Key)); // the strangers are nobody's persons here
            Assert.DoesNotContain(result.Hits, h => h.PersonKey == "60001010008");
            Assert.Contains(result.Hits, h => h.Row.Counterparty == "Peeter Kask" && h.PersonKey is null); // his payment a row nobody matched
            var said = Assert.Single(result.Problems, p => p.Code == MatchProblem.OtherEmployer);
            Assert.StartsWith("Register rows 4, 5 are of another employer (99999999 Teine OÜ), not of 10235688: left out", said.Message);
            Assert.Contains(store.LoadMatchResult(period.Id)!.Problems, p => p.Code == MatchProblem.OtherEmployer); // kept with the run
            var workspace = new ReviewWorkspace(store, TaxRates.Year2026);
            var view = workspace.Load(period.Id);
            Assert.Contains(view.Unmatched.Problems, p => p.Code == MatchProblem.OtherEmployer); // seen whenever the month is opened
            Assert.False(view.RunIsStale);

            // a search made before the rows were left out still holds the strangers as persons: it is stale until made again
            var all = store.TorEntries(company.Id, file.Id).Select(e => e.Entry).ToList();
            var (_, sources, torSheetId, _) = new MatchRunner(store).Prepare(period.Id);
            store.ClearMatchRun(period.Id);
            store.SaveMatchRun(period.Id, torSheetId, new PeriodMatcher(MatchCatalog.Default).Run(2026, 10, MatchPerson.FromTor(all, 2026, 10), sources));
            Assert.True(workspace.Load(period.Id).RunIsStale);
            new MatchRunner(store).Run(period.Id);
            Assert.False(workspace.Load(period.Id).RunIsStale);
        }
    }

    [Fact]
    public void A_run_must_come_from_the_periods_files_and_rows()
    {
        var (store, company, period, file) = Setup();
        using (store)
        {
            var runner = new MatchRunner(store);
            var (persons, sources, torSheetId, _) = runner.Prepare(period.Id);
            var matcher = new PeriodMatcher(MatchCatalog.Default);

            var foreignSource = sources[0] with { SheetId = Guid.NewGuid() };
            Assert.Contains("not a statement sheet", Assert.Throws<InvalidOperationException>(() => store.SaveMatchRun(period.Id, torSheetId, matcher.Run(2026, 10, persons, [foreignSource]))).Message);

            var noSheet = sources[0] with { SheetId = null };
            Assert.Throws<ArgumentException>(() => store.SaveMatchRun(period.Id, torSheetId, matcher.Run(2026, 10, persons, [noSheet])));

            var strangeRow = sources[0] with { Rows = sources[0].Rows.Select(r => r with { RowNo = r.RowNo + 100 }).ToList() };
            Assert.Equal(MatchCatalog.Default.ToJson(), matcher.Run(2026, 10, persons, sources).CatalogJson);
            Assert.Contains("not a stored row", Assert.Throws<InvalidOperationException>(() => store.SaveMatchRun(period.Id, torSheetId, matcher.Run(2026, 10, persons, [strangeRow]))).Message);

            Assert.Throws<ArgumentException>(() => store.SaveMatchRun(period.Id, torSheetId, matcher.Run(2026, 11, persons, sources)));
            Assert.Throws<InvalidOperationException>(() => store.SaveMatchRun(period.Id, Guid.NewGuid(), matcher.Run(2026, 10, persons, sources)));

            // a second register file linked: the run does not choose
            var second = Tables.Xlsx("Töötajate loend", TorHeaders.All.ToArray<object?>(), ["3", 39001010000d, "KASK", "PEETER", new DateTime(2024, 1, 1), 1, "Tööleping", 2, "Kehtiv", null, null, null, null, null, null, null, null, "EE", null, null, null, null, null, null, null, null, 10235688, "Näidis OÜ"]);
            var secondFile = store.AddImport(company.Id, Importer.Read("tor2.xlsx", second), second);
            store.LinkImport(period.Id, secondFile.Id);
            Assert.Contains("2 register sheets", Assert.Throws<MatchException>(() => runner.Run(period.Id)).Message);

            var empty = store.GetOrCreatePeriod(company.Id, 2026, 9);
            Assert.Contains("No file is linked", Assert.Throws<MatchException>(() => runner.Run(empty.Id)).Message);
            Assert.Throws<KeyNotFoundException>(() => runner.Run(Guid.NewGuid()));
            Assert.NotNull(file);
        }
    }

    [Fact]
    public void The_owners_catalog_is_saved_checked_and_in_force_for_the_next_run()
    {
        var (store, _, period, _) = Setup();
        using (store)
        {
            var runner = new MatchRunner(store);
            Assert.Null(store.SavedMatchCatalog());
            Assert.Same(MatchCatalog.Default, runner.Catalog());

            Assert.Throws<MatchException>(() => store.SaveMatchCatalog("{not json"));
            Assert.Throws<MatchException>(() => store.SaveMatchCatalog(MatchCatalog.Default.ToJson().Replace("\"lhv-en\": \"standard\"", "\"lhv-en\": \"nowhere\"")));
            Assert.Null(store.SavedMatchCatalog());

            var mine = MatchCatalog.Default.ToJson().Replace("\"Laenud\": \"*laen*\"", "\"Laenud\": \"*laen*,*võlg*\"");
            store.SaveMatchCatalog(mine);
            Assert.Equal(mine, store.SavedMatchCatalog());
            Assert.Equal(["*laen*", "*võlg*"], runner.Catalog().FiltersFor("swedbank-et")!["Laenud"].Include);

            runner.Run(period.Id);
            Assert.Equal(mine, store.MatchRun(period.Id)!.CatalogJson);
            Assert.NotNull(store.LoadMatchResult(period.Id));

            store.ClearMatchCatalog();
            Assert.Null(store.SavedMatchCatalog());
            Assert.Equal(mine, store.MatchRun(period.Id)!.CatalogJson); // the stored run keeps the catalog it ran with
        }
    }

    [Fact]
    public void A_run_is_stored_with_the_catalog_it_was_made_with_whatever_is_in_force_at_the_time()
    {
        var (store, _, period, _) = Setup();
        using (store)
        {
            var runner = new MatchRunner(store);
            var (persons, sources, torSheetId, _) = runner.Prepare(period.Id);
            var result = new PeriodMatcher(MatchCatalog.Default).Run(2026, 10, persons, sources);
            store.SaveMatchCatalog(MatchCatalog.Default.ToJson().Replace("\"Laenud\": \"*laen*\"", "\"Laenud\": \"*laen*,*võlg*\"")); // another catalog is in force now

            store.SaveMatchRun(period.Id, torSheetId, result);

            Assert.Equal(MatchCatalog.Default.ToJson(), store.MatchRun(period.Id)!.CatalogJson);
            Assert.Equal(result, store.LoadMatchResult(period.Id));

            // a result made with a catalog that has no category of a stored verdict cannot be read back as it was
            var empty = MatchCatalog.Parse("""{"Categories":[],"FilterSets":{},"LayoutFilterSets":{},"Aliases":[]}""").ToJson();
            store.SaveMatchRun(period.Id, torSheetId, result with { CatalogJson = empty });
            Assert.Contains("no category 'Palgad'", Assert.Throws<InvalidOperationException>(() => store.LoadMatchResult(period.Id)).Message);
        }
    }

    [Fact]
    public void A_file_a_run_took_stays_until_the_run_is_cleared()
    {
        var (store, _, period, file) = Setup();
        using (store)
        {
            new MatchRunner(store).Run(period.Id);
            Assert.Contains("match run", Assert.Throws<InvalidOperationException>(() => store.UnlinkImport(period.Id, file.Id)).Message);
            Assert.Contains("match run", Assert.Throws<InvalidOperationException>(() => store.RemoveImport(file.Id)).Message);

            store.ClearMatchRun(period.Id);
            store.UnlinkImport(period.Id, file.Id);
            Assert.Empty(store.PeriodImports(period.Id));
            store.RemoveImport(file.Id);
            Assert.Empty(store.Imports(period.CompanyId));
        }
    }

    [Fact]
    public void A_v2_store_upgrades_to_v3_with_its_rows_intact()
    {
        var path = Path.Combine(Path.GetTempPath(), $"satellestsd-{Guid.NewGuid():N}.db");
        try
        {
            using (var v2 = SqliteStore.OpenAtVersion(path, 2))
            {
                var company = v2.AddCompany("10235688", "Näidis OÜ");
                v2.GetOrCreatePeriod(company.Id, 2026, 10);
                Assert.Equal(2L, v2.ScalarRaw<long>("SELECT MAX(version) FROM schema_version"));
                Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => v2.ScalarRaw<long>("SELECT COUNT(*) FROM match_run"));
            }

            using var v3 = SqliteStore.Open(path);
            Assert.Equal((long)SqliteStore.SchemaVersion, v3.ScalarRaw<long>("SELECT MAX(version) FROM schema_version"));
            Assert.Equal(0L, v3.ScalarRaw<long>("SELECT COUNT(*) FROM match_run"));
            var period = Assert.Single(v3.Periods(v3.FindCompany("10235688")!.Id));
            Assert.Null(v3.MatchRun(period.Id));
            Assert.Null(v3.SavedMatchCatalog());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
