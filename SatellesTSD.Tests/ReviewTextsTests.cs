using System.Text.Json;
using SatellesTSD.Core.Import;
using SatellesTSD.Core.Lookups;
using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Tax;
using SatellesTSD.Infrastructure;

namespace SatellesTSD.Tests;

/// <summary>The figures the Core keeps beside its English for the window to word a text in Estonian (decision 46 kept): a
/// problem's figures saved with the run and read back, a run stored before them read without any, the equality of the
/// records with a list in them, and the register checks' notes going one for one with their English.</summary>
public class ReviewTextsTests
{
    [Fact]
    public void A_problems_figures_are_kept_with_the_run_and_an_old_run_reads_without_them()
    {
        var problem = new MatchProblem(MatchProblem.SeveralEntries, "MARI: the register lists the person 2 times (rows 1, 5).", "x", Args: ["MARI", "2", "1, 5"]);
        var json = JsonSerializer.Serialize(new[] { problem }, LayoutCatalog.CompactJson);
        Assert.Equal(problem, Assert.Single(JsonSerializer.Deserialize<MatchProblem[]>(json, LayoutCatalog.CompactJson)!));

        // a run stored before the figures were kept: no figures, the English stays
        var old = Assert.Single(JsonSerializer.Deserialize<MatchProblem[]>("""[{"Code":"SEVERAL_ENTRIES","Message":"MARI: english"}]""", LayoutCatalog.CompactJson)!);
        Assert.Null(old.Args);

        // the run as the store keeps it: each problem with its figures
        var (store, _, period, _) = ReviewFixtures.Setup(mariSecond: true);
        using (store)
        {
            var result = new MatchRunner(store).Run(period.Id);
            var several = result.Problems.First(p => p.Code == MatchProblem.SeveralEntries);
            Assert.Equal(3, several.Args!.Count);
            Assert.Equal("2", several.Args[1]);
            Assert.Equal(result.Problems, store.MatchRun(period.Id)!.Problems);
        }
    }

    [Fact]
    public void Records_with_figures_are_equal_by_them_and_none_is_not_an_empty_list()
    {
        Assert.Equal(new MatchProblem("C", "m", Args: ["a", "b"]), new MatchProblem("C", "m", Args: new List<string> { "a", "b" }));
        Assert.Equal(new MatchProblem("C", "m", Args: ["a"]).GetHashCode(), new MatchProblem("C", "m", Args: new List<string> { "a" }).GetHashCode());
        Assert.NotEqual(new MatchProblem("C", "m", Args: ["a"]), new MatchProblem("C", "m", Args: ["b"]));
        Assert.NotEqual(new MatchProblem("C", "m", Args: []), new MatchProblem("C", "m"));
        Assert.NotEqual(new MatchProblem("C", "m", Variant: "x", Args: ["a"]), new MatchProblem("C", "m", Args: ["a"]));

        Assert.Equal(new Refusal("C", "m", ["1.5"]), new Refusal("C", "m", new List<string> { "1.5" }));
        Assert.Equal(new Refusal("C", "m", ["1.5"]).GetHashCode(), new Refusal("C", "m", new List<string> { "1.5" }).GetHashCode());
        Assert.NotEqual(new Refusal("C", "m", ["1.5"]), new Refusal("C", "m", ["2"]));
        Assert.NotEqual(new Refusal("C", "m", []), new Refusal("C", "m"));
        Assert.Equal(new Refusal("C", "m"), new Refusal("C", "m"));
    }

    [Fact]
    public void A_refusals_figures_are_written_with_a_point_whatever_the_culture()
    {
        var before = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("et-EE");
            var refusal = Assert.Single(PaymentChecks.Check(PaymentTypes.Get("10"), PayeeTaxFacts.ResidentEmployee() with { BasicExemptionMonthly = 100.005m }, 100m, TaxRates.Year2026),
                r => r.Code == PaymentChecks.ExemptionSubCent);
            Assert.Equal(["100.005"], refusal.Args);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = before;
        }
    }

    [Fact]
    public async Task A_register_checks_notes_go_one_for_one_with_its_English_and_the_queries_are_counted_as_before()
    {
        var (store, company, period, _) = ReviewFixtures.Setup();
        using (store)
        {
            new MatchRunner(store).Run(period.Id);
            var runner = new LookupRunner(store, new FailingResidency(), new JoinedPension(), new LookupOptions(TimeSpan.Zero));
            var report = await runner.RunAsync(period.Id);
            Assert.Equal(report.Problems.Count, report.ProblemNotes.Count);
            Assert.NotEmpty(report.Persons);
            Assert.All(report.Persons, outcome =>
            {
                Assert.Equal(outcome.Steps.Count, outcome.StepNotes.Count);
                Assert.Equal(outcome.Problems.Count, outcome.ProblemNotes.Count);
                Assert.All(outcome.Problems.Zip(outcome.ProblemNotes), pair => Assert.StartsWith(pair.Second.Code + ":", pair.First, StringComparison.Ordinal));
                Assert.All(outcome.Steps.Zip(outcome.StepNotes), pair => Assert.Equal(pair.First.StartsWith("asked ", StringComparison.Ordinal), pair.Second.Code == LookupNote.Asked));
            });
            Assert.Equal(report.Persons.Sum(p => p.Steps.Count(s => s.StartsWith("asked", StringComparison.Ordinal))), report.Queries);

            // the residency failed with the site's own detail, kept in the note for the window
            var mari = report.Persons.Single(p => p.PersonalCode == ReviewFixtures.MariCode);
            var failed = mari.ProblemNotes.First(n => n.Code == LookupRunner.Failed);
            Assert.Equal(LookupKind.Residency, failed.Kind);
            Assert.Equal("request timeout or access denied: HTTP 503", failed.Failure);
            Assert.Contains(mari.StepNotes, n => n is { Code: LookupNote.Asked, Kind: LookupKind.Residency });

            // the notes count in the outcome's equality
            Assert.NotEqual(mari, mari with { StepNotes = [] });
            Assert.NotEqual(report, report with { ProblemNotes = [new LookupNote(LookupNote.NoRun)] });
            _ = company;
        }
    }

    private sealed class FailingResidency : IResidencyLookup
    {
        public Task<LookupResult<ResidencyFinding>> QueryAsync(string personalCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(LookupResult<ResidencyFinding>.Failed("request timeout or access denied: HTTP 503"));
    }

    private sealed class JoinedPension : IPensionLookup
    {
        public Task<LookupResult<PensionFinding>> QueryAsync(string personalCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(LookupResult<PensionFinding>.Found(new PensionFinding(PensionStatus.Joined, new DateOnly(2019, 1, 1), null, [new RatePeriod(new DateOnly(2019, 1, 1), null, 0.02m, "x")]), null));
    }
}
