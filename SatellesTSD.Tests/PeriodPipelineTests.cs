using System.Text;
using SatellesTSD.Core.Declare;
using SatellesTSD.Core.Import;
using SatellesTSD.Core.Lookups;
using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Review;
using SatellesTSD.Core.Store;
using SatellesTSD.Core.Tax;
using SatellesTSD.Infrastructure;

namespace SatellesTSD.Tests;

/// <summary>The window's three steps over a period: files imported and linked once, the match run, the lookups
/// reported person by person — and what a period past Draft refuses.</summary>
public class PeriodPipelineTests
{
    private sealed class FakeResidency : IResidencyLookup
    {
        public Task<LookupResult<ResidencyFinding>> QueryAsync(string personalCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(LookupResult<ResidencyFinding>.Found(new ResidencyFinding(new DateOnly(2026, 11, 4), [], []), null));
    }

    private sealed class FakePension : IPensionLookup
    {
        public Task<LookupResult<PensionFinding>> QueryAsync(string personalCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(LookupResult<PensionFinding>.Found(new PensionFinding(PensionStatus.NotJoined, null, null, []), null));
    }

    private sealed class Collected<T> : IProgress<T>
    {
        public readonly List<T> Items = [];

        public void Report(T value) => Items.Add(value);
    }

    private static (SqliteStore Store, Company Company, Period Period) Setup()
    {
        var store = SqliteStore.InMemory();
        var company = store.AddCompany("10235688", "Näidis OÜ");
        return (store, company, store.GetOrCreatePeriod(company.Id, 2026, 10));
    }

    [Fact]
    public void A_file_is_read_stored_and_linked_once_and_a_register_of_another_employer_is_said()
    {
        var (store, company, period) = Setup();
        using (store)
        {
            var pipeline = new PeriodPipeline(store, ReviewFixtures.Importer);
            var bytes = ReviewFixtures.Workbook();

            var first = pipeline.ImportFile(period.Id, "palgad.xlsx", bytes);
            Assert.True(first.Succeeded, first.Error);
            Assert.Equal((false, true, ImportKind.Mixed), (first.AlreadyImported, first.Linked, first.Kind));
            Assert.DoesNotContain(first.Messages, m => m.StartsWith("sheet 'Töötajate loend'", StringComparison.Ordinal));

            var again = pipeline.ImportFile(period.Id, "palgad-koopia.xlsx", bytes);
            Assert.True(again.AlreadyImported);
            Assert.Equal(first.File!.Id, again.File!.Id);
            Assert.Single(store.PeriodImports(period.Id));
            Assert.Single(store.Imports(company.Id));

            var other = store.AddCompany("99999999", "Teine OÜ");
            var otherPeriod = store.GetOrCreatePeriod(other.Id, 2026, 10);
            var mismatch = pipeline.ImportFile(otherPeriod.Id, "palgad.xlsx", bytes);
            Assert.True(mismatch.Succeeded);
            Assert.Contains(mismatch.Messages, m => m.StartsWith("sheet 'Töötajate loend'", StringComparison.Ordinal));
            Assert.Single(store.Imports(other.Id)); // the same bytes once per company
        }
    }

    [Fact]
    public void A_file_nothing_is_known_of_is_reported_not_stored()
    {
        var (store, company, period) = Setup();
        using (store)
        {
            var pipeline = new PeriodPipeline(store, ReviewFixtures.Importer);

            var outcome = pipeline.ImportFile(period.Id, "x.csv", Encoding.UTF8.GetBytes("a;b;c\n1;2;3\n"));
            Assert.False(outcome.Succeeded);
            Assert.Equal(PeriodPipeline.NothingRecognised, outcome.Error);
            Assert.Contains(outcome.Messages, m => m.Contains("layout", StringComparison.OrdinalIgnoreCase));
            Assert.Empty(store.Imports(company.Id));

            var html = pipeline.ImportFile(period.Id, "x.xlsx", Encoding.UTF8.GetBytes("<html><body>x</body></html>"));
            Assert.Contains("web page", html.Error);
            Assert.Empty(store.Imports(company.Id));
        }
    }

    [Fact]
    public void The_files_and_the_run_are_draft_only()
    {
        var (store, _, period) = Setup();
        using (store)
        {
            var pipeline = new PeriodPipeline(store, ReviewFixtures.Importer);
            Assert.Throws<MatchException>(() => pipeline.RunMatch(period.Id)); // no file yet
            var file = pipeline.ImportFile(period.Id, "palgad.xlsx", ReviewFixtures.Workbook()).File!;
            var result = pipeline.RunMatch(period.Id);
            Assert.Equal(2, result.Persons.Count);
            Assert.NotNull(store.MatchRun(period.Id));

            store.SetPeriodState(period.Id, PeriodState.Exported, DateTime.UtcNow);
            Assert.Equal(ReviewWorkspace.PeriodNotDraft, Assert.Throws<ReviewException>(() => pipeline.RunMatch(period.Id)).Code);
            Assert.Equal(ReviewWorkspace.PeriodNotDraft, Assert.Throws<ReviewException>(() => pipeline.ImportFile(period.Id, "x.xlsx", ReviewFixtures.Workbook(1m))).Code);
            Assert.Equal(ReviewWorkspace.PeriodNotDraft, Assert.Throws<ReviewException>(() => pipeline.ClearRun(period.Id)).Code);
            Assert.Equal(ReviewWorkspace.PeriodNotDraft, Assert.Throws<ReviewException>(() => pipeline.UnlinkFile(period.Id, file.Id)).Code);

            store.SetPeriodState(period.Id, PeriodState.Draft, DateTime.UtcNow);
            Assert.Throws<InvalidOperationException>(() => pipeline.UnlinkFile(period.Id, file.Id)); // the run took it
            pipeline.ClearRun(period.Id);
            pipeline.UnlinkFile(period.Id, file.Id);
            Assert.Empty(store.PeriodImports(period.Id));
            Assert.Throws<MatchException>(() => pipeline.RunMatch(period.Id));
        }
    }

    [Fact]
    public async Task The_lookups_are_reported_person_by_person_and_need_the_registers()
    {
        var (store, _, period) = Setup();
        using (store)
        {
            Assert.False(new PeriodPipeline(store, ReviewFixtures.Importer).HasLookups);
            await Assert.ThrowsAsync<InvalidOperationException>(() => new PeriodPipeline(store, ReviewFixtures.Importer).RunLookupsAsync(period.Id));

            var pipeline = new PeriodPipeline(store, ReviewFixtures.Importer, new FakeResidency(), new FakePension(), new LookupOptions(TimeSpan.Zero));
            Assert.True(pipeline.HasLookups);
            pipeline.ImportFile(period.Id, "palgad.xlsx", ReviewFixtures.Workbook());
            pipeline.RunMatch(period.Id);

            var progress = new Collected<LookupProgress>();
            var report = await pipeline.RunLookupsAsync(period.Id, progress);
            Assert.Equal(2, report.Persons.Count);
            Assert.Equal(new LookupProgress(0, 2, null), progress.Items[0]); // how many are asked, before the first
            Assert.Equal(report.Persons, progress.Items.Skip(1).Select(p => p.Outcome));
            Assert.Equal([1, 2], progress.Items.Skip(1).Select(p => p.Done));
            Assert.All(progress.Items, p => Assert.Equal(2, p.Total));
            Assert.Equal(2, report.Queries + report.Persons.Count(p => p.Steps.Contains("pension not asked: a non-resident")) - report.Persons.Count); // residency asked for both, the pension for both residents
            var mari = store.FindPerson(period.CompanyId, ReviewFixtures.MariCode)!;
            Assert.Equal((Residency.Resident, LookupSource.Emta, false, LookupSource.Pensionikeskus), (mari.Residency, mari.ResidencySource, mari.FundedPensionMember, mari.PensionSource));

            var one = await pipeline.LookUpAsync(mari, period);
            Assert.Equal(mari.Id, one.PersonId);
            Assert.Contains("reused", string.Join(" ", one.Steps));
            Assert.NotNull(store.FindPerson(period.CompanyId, ReviewFixtures.MariCode)!.FactsControlDate); // the day the registers were asked for

            // a month past Draft asks the registers no more: the facts other months are built with would change under it
            store.SetPeriodState(period.Id, PeriodState.Exported, DateTime.UtcNow);
            Assert.Equal(ReviewWorkspace.PeriodNotDraft, (await Assert.ThrowsAsync<ReviewException>(() => pipeline.RunLookupsAsync(period.Id))).Code);
            Assert.Equal(ReviewWorkspace.PeriodNotDraft, (await Assert.ThrowsAsync<ReviewException>(() => pipeline.LookUpAsync(mari, period))).Code);
        }
    }

    [Fact]
    public void A_file_linked_after_the_run_makes_the_run_stale_until_it_is_unlinked_or_the_search_made_again()
    {
        var (store, _, period) = Setup();
        using (store)
        {
            var pipeline = new PeriodPipeline(store, ReviewFixtures.Importer);
            pipeline.ImportFile(period.Id, "palgad.xlsx", ReviewFixtures.Workbook());
            pipeline.RunMatch(period.Id);
            var workspace = new ReviewWorkspace(store, TaxRates.Year2026);
            var declarer = new PeriodDeclarer(store, TaxRates.Year2026);
            Assert.False(workspace.Load(period.Id).RunIsStale);

            // a second file (its own register and statement) linked after the search: the run never saw it
            var second = pipeline.ImportFile(period.Id, "teine.xlsx", ReviewFixtures.Workbook(salary: 1000m)).File!;
            Assert.True(workspace.Load(period.Id).RunIsStale);
            Assert.Equal(PeriodDeclarer.RunStale, Assert.Throws<DeclareException>(() => declarer.Build(period.Id)).Code);
            Assert.Empty(store.Payments(period.Id)); // refused before any write
            Assert.False(declarer.DraftsCurrent(period.Id)); // the export is told: no draft can be what a search of every file would make

            pipeline.UnlinkFile(period.Id, second.Id);
            Assert.False(workspace.Load(period.Id).RunIsStale);
            Assert.Null(Record.Exception(() => declarer.Build(period.Id)));

            // the run cleared (the way to unlink a file it took): files to search and no search is stale too — a build then
            // would retire every bank-row line; the search made again, the month builds
            var lines = store.Payments(period.Id).Count;
            pipeline.ClearRun(period.Id);
            Assert.True(workspace.Load(period.Id).RunIsStale);
            var refusal = Assert.Throws<DeclareException>(() => declarer.Build(period.Id));
            Assert.Equal(PeriodDeclarer.RunStale, refusal.Code);
            Assert.Contains("not been searched", refusal.Message, StringComparison.Ordinal);
            Assert.Equal(lines, store.Payments(period.Id).Count);
            Assert.False(declarer.DraftsCurrent(period.Id));
            pipeline.RunMatch(period.Id);
            Assert.False(workspace.Load(period.Id).RunIsStale);
            Assert.Null(Record.Exception(() => declarer.Build(period.Id)));

            // a month with no file to search (hand lines only) needs no search
            var november = store.GetOrCreatePeriod(period.CompanyId, 2026, 11);
            Assert.False(workspace.Load(november.Id).RunIsStale);
            Assert.Null(Record.Exception(() => declarer.Build(november.Id)));
        }
    }

    [Fact]
    public void A_file_read_again_keeps_its_place_and_the_decisions_on_the_rows_read_again_and_says_what_went()
    {
        var (store, company, period) = Setup();
        using (store)
        {
            var pipeline = new PeriodPipeline(store, ReviewFixtures.Importer);
            var first = pipeline.ImportFile(period.Id, "palgad.xlsx", ReviewFixtures.Workbook()).File!;
            var secondBytes = Tables.Xlsx("LHV", LayoutCatalog.Default.Find("lhv-et-short")!.Headers.ToArray<object?>(), ["EE497700771001131757", 1, new DateTime(2026, 10, 20), "EE12", "Mari Maasikas", "D", -100d, null, "2026102000000009", "lisatasu", "EUR", 48712219406d]);
            var second = pipeline.ImportFile(period.Id, "teine.xlsx", secondBytes).File!;
            pipeline.RunMatch(period.Id);
            var workspace = new ReviewWorkspace(store, TaxRates.Year2026);
            var view = workspace.Load(period.Id);
            var salary = view.Persons[0].Payments.Single(p => p.Source == ReviewFixtures.Source && !p.IsByHand);
            workspace.SetNote(salary, "kontrollitud");
            var fee = view.Unmatched.Payments.Single(p => p.Source == ReviewFixtures.Source && p.Amount == 0.38m);
            workspace.SetCategory(fee, "Üür");
            workspace.AddHandLine(period.Id, view.Persons[0].PersonId, new DateOnly(2026, 10, 25), 50m, "Mari Maasikas", "käsitsi", "Palgad");
            workspace.SavePerson(view.Persons[0].Person! with { FundedPensionMember = false }, confirmResidency: true);
            new PeriodDeclarer(store, TaxRates.Year2026).Build(period.Id); // drafts from both files' rows and the hand line
            var firstRows = store.Sheets(first.Id).SelectMany(s => store.StatementRows(s.Id)).Select(r => r.Id).ToHashSet();
            var secondRows = store.Sheets(second.Id).SelectMany(s => store.StatementRows(s.Id)).Select(r => r.Id).ToHashSet();
            Assert.Contains(store.Payments(period.Id), p => p.SourceRowId is { } r && firstRows.Contains(r));
            var others = store.Payments(period.Id).Where(p => p.SourceRowId is not { } r || !firstRows.Contains(r)).Select(p => p.Id).ToList();
            Assert.Contains(store.Payments(period.Id), p => p.SourceRowId is { } r && secondRows.Contains(r));

            var outcome = pipeline.RereadFile(period.Id, first.Id);
            Assert.True(outcome.Succeeded, outcome.Error);
            Assert.Equal((2, 0, 1, 1), (outcome.Carried, outcome.Dropped, outcome.RunsCleared.Count, outcome.DraftsDeleted));
            Assert.NotEqual(first.Id, outcome.File!.Id);
            Assert.Equal(first.ImportedAt, outcome.File.ImportedAt); // the day it came in stays its record
            Assert.Equal([outcome.File.Id, second.Id], store.PeriodImports(period.Id).Select(f => f.Id)); // its place kept: it still holds the originals (decision 86)
            Assert.Equal(2, store.Imports(company.Id).Count);
            Assert.Null(store.LoadMatchResult(period.Id)); // searched again
            Assert.True(workspace.Load(period.Id).RunIsStale);
            Assert.Equal(others, store.Payments(period.Id).Select(p => p.Id)); // only the drafts from the old rows went; the next build makes them again
            Assert.Single(store.ReviewLines(period.Id), l => l.IsByHand); // the hand line is nobody's row: untouched
            pipeline.RunMatch(period.Id);
            var again = workspace.Load(period.Id);
            Assert.Equal("kontrollitud", again.Persons[0].Payments.Single(p => p.Source == ReviewFixtures.Source && !p.IsByHand).Note);
            Assert.Equal("Üür", again.Unmatched.Payments.Single(p => p.Source == ReviewFixtures.Source && p.Amount == 0.38m).Category);

            // read again with a layout that reads the columns otherwise: the fingerprints differ, the decisions go, and it is counted
            var lhv = LayoutCatalog.Default.Find("lhv-et-short")!;
            var swapped = lhv with { Roles = lhv.Roles with { Counterparty = lhv.Roles.Description, Description = lhv.Roles.Counterparty } };
            var changed = new PeriodPipeline(store, new Importer(LayoutCatalog.Default.WithOverrides([swapped]), new ExcelTableReader()));
            var dropped = changed.RereadFile(period.Id, outcome.File.Id);
            Assert.True(dropped.Succeeded, dropped.Error);
            Assert.Equal((0, 2), (dropped.Carried, dropped.Dropped));
            Assert.Single(store.ReviewLines(period.Id), l => l.IsByHand);
            Assert.Equal([dropped.File!.Id, second.Id], store.PeriodImports(period.Id).Select(f => f.Id));

            // a month past Draft that lists the file refuses the re-read
            var november = store.GetOrCreatePeriod(company.Id, 2026, 11);
            store.LinkImport(november.Id, dropped.File.Id);
            store.SetPeriodState(november.Id, PeriodState.Exported, DateTime.UtcNow);
            var refused = pipeline.RereadFile(period.Id, dropped.File.Id);
            Assert.False(refused.Succeeded);
            Assert.Contains("2026-11", refused.Error);
        }
    }

    [Fact]
    public void Two_identical_rows_keep_their_own_decisions_through_a_re_read()
    {
        var (store, _, period) = Setup();
        using (store)
        {
            var pipeline = new PeriodPipeline(store, ReviewFixtures.Importer);
            var headers = LayoutCatalog.Default.Find("lhv-et-short")!.Headers.ToArray<object?>();
            var bytes = Tables.Xlsx("LHV", headers,
                ["EE497700771001131757", 1, new DateTime(2026, 10, 6), null, null, "D", -0.38, null, null, "Teenustasu", "EUR"],
                ["EE497700771001131757", 2, new DateTime(2026, 10, 6), null, null, "D", -0.38, null, null, "Teenustasu", "EUR"],
                ["EE497700771001131757", 3, new DateTime(2026, 10, 12), "EE14", "Kinnisvara OÜ", "D", -450d, null, "2026101200000003", "ruumide kasutamine", "EUR"]); // a named row after them: the payee-less rows before the last named row are listed (decision 29)
            var file = pipeline.ImportFile(period.Id, "tasud.xlsx", bytes).File!;
            pipeline.RunMatch(period.Id);
            var workspace = new ReviewWorkspace(store, TaxRates.Year2026);
            var rows = workspace.Load(period.Id).Unmatched.Payments.Where(p => p.Amount == 0.38m).OrderBy(p => p.RowNo).ToList();
            Assert.Equal(2, rows.Count);
            workspace.SetCategory(rows[0], "Üür");
            workspace.SetCategory(rows[1], "Kommunaalkulud");

            var outcome = pipeline.RereadFile(period.Id, file.Id);
            Assert.True(outcome.Succeeded, outcome.Error);
            Assert.Equal((2, 0), (outcome.Carried, outcome.Dropped));
            pipeline.RunMatch(period.Id);
            var again = workspace.Load(period.Id).Unmatched.Payments.Where(p => p.Amount == 0.38m).OrderBy(p => p.RowNo).ToList();
            Assert.Equal(["Üür", "Kommunaalkulud"], again.Select(p => p.Category)); // the n-th row of a fingerprint keeps the n-th decision
        }
    }

    [Fact]
    public void A_file_is_taken_out_of_the_store_from_the_one_period_that_lists_it_or_from_no_period_at_all()
    {
        var (store, company, period) = Setup();
        using (store)
        {
            var pipeline = new PeriodPipeline(store, ReviewFixtures.Importer);
            var file = pipeline.ImportFile(period.Id, "palgad.xlsx", ReviewFixtures.Workbook()).File!;
            pipeline.RunMatch(period.Id);
            var workspace = new ReviewWorkspace(store, TaxRates.Year2026);
            var view = workspace.Load(period.Id);
            workspace.SavePerson(view.Persons[0].Person! with { FundedPensionMember = false }, confirmResidency: true);
            new PeriodDeclarer(store, TaxRates.Year2026).Build(period.Id);
            Assert.NotEmpty(store.Payments(period.Id));
            var november = store.GetOrCreatePeriod(company.Id, 2026, 11);
            store.LinkImport(november.Id, file.Id);

            store.SetPeriodState(november.Id, PeriodState.Exported, DateTime.UtcNow);
            Assert.Contains("cannot be taken out", Assert.Throws<InvalidOperationException>(() => pipeline.RemoveFile(period.Id, file.Id)).Message); // a declared month's record for good
            store.SetPeriodState(november.Id, PeriodState.Draft, DateTime.UtcNow);
            Assert.Contains("2026-11", Assert.Throws<InvalidOperationException>(() => pipeline.RemoveFile(period.Id, file.Id)).Message); // listed in another Draft: taken out there first
            store.UnlinkImport(november.Id, file.Id);
            Assert.Contains("match run", Assert.Throws<InvalidOperationException>(() => pipeline.RemoveFile(period.Id, file.Id)).Message); // the run took it: the second word, as for an unlink
            Assert.NotNull(store.LoadMatchResult(period.Id)); // a refusal costs no search
            Assert.Single(store.PeriodImports(period.Id));

            pipeline.RemoveFile(period.Id, file.Id, clearRun: true); // the run, the drafts from the file's rows and the file go together
            Assert.Empty(store.Imports(company.Id));
            Assert.Empty(store.PeriodImports(period.Id));
            Assert.Empty(store.Payments(period.Id));
            Assert.Null(store.LoadMatchResult(period.Id));
            Assert.Throws<KeyNotFoundException>(() => pipeline.RemoveFile(period.Id, file.Id));

            // a file no period lists any more (unlinked in another month) is listed as such and taken out from any Draft month
            var stray = pipeline.ImportFile(november.Id, "teine.xlsx", ReviewFixtures.Workbook(salary: 1000m)).File!;
            pipeline.UnlinkFile(november.Id, stray.Id);
            Assert.Equal([stray.Id], pipeline.UnlistedFiles(company.Id).Select(f => f.Id));
            pipeline.RemoveFile(period.Id, stray.Id);
            Assert.Empty(pipeline.UnlistedFiles(company.Id));
            Assert.Empty(store.Imports(company.Id));
        }
    }

    [Fact]
    public void A_register_without_an_employer_code_is_imported_and_the_log_says_it_was_not_checked()
    {
        var (store, _, period) = Setup();
        using (store)
        {
            var bytes = Tables.Xlsx(("Töötajate loend", [TorHeaders.All.ToArray<object?>(),
                ["1", 48712219406d, "MAASIKAS", "MARI", new DateTime(2024, 2, 15), 1, "Tööleping", 2, "Kehtiv", null, null, null, null, null, null, null, null, "EE", null, null, null, null, null, null, null, null, null, "Näidis OÜ"]], false));
            var pipeline = new PeriodPipeline(store, ReviewFixtures.Importer);
            Assert.Null(pipeline.EmployerOf([("tor.xlsx", bytes)])); // nothing to find the company by: the company shown takes the file
            var outcome = pipeline.ImportFile(period.Id, "tor.xlsx", bytes);
            Assert.True(outcome.Succeeded, outcome.Error);
            Assert.Contains(outcome.Messages, m => m.StartsWith("sheet 'Töötajate loend'", StringComparison.Ordinal) && m.Contains("no employer registry code", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void A_hidden_sheet_is_read_and_said()
    {
        var (store, _, period) = Setup();
        using (store)
        {
            var bytes = Tables.Xlsx(("LHV", [LayoutCatalog.Default.Find("lhv-et-short")!.Headers.ToArray<object?>(), ["EE497700771001131757", 1, new DateTime(2026, 10, 5), "EE12", "Mari Maasikas", "D", -905.92, null, "2026100500000001", "palk", "EUR"]], true));
            var outcome = new PeriodPipeline(store, ReviewFixtures.Importer).ImportFile(period.Id, "peidetud.xlsx", bytes);
            Assert.True(outcome.Succeeded, outcome.Error);
            Assert.Contains(outcome.Messages, m => m.StartsWith("sheet 'LHV'", StringComparison.Ordinal) && m.Contains("hidden", StringComparison.Ordinal));
        }
    }
}
