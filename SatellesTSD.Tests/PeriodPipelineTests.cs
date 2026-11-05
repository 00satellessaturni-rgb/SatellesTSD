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

            var progress = new Collected<PersonLookupOutcome>();
            var report = await pipeline.RunLookupsAsync(period.Id, progress);
            Assert.Equal(2, report.Persons.Count);
            Assert.Equal(report.Persons, progress.Items);
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
        }
    }
}
