using System.Diagnostics;
using System.Text.Json;
using SatellesTSD.Core.Import;
using SatellesTSD.Core.Lookups;
using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Store;
using SatellesTSD.Infrastructure;

namespace SatellesTSD.Tests;

/// <summary>The run over a period's persons with the registers in a box: what is asked, what is recorded, what lands
/// on the person, what the accountant's hand keeps, and what is said.</summary>
public class LookupRunnerTests
{
    private static readonly Importer Importer = new(LayoutCatalog.Default, new ExcelTableReader());
    private static readonly DateOnly Control = new(2026, 10, 31);

    private sealed class FakeResidency : IResidencyLookup
    {
        public readonly Dictionary<string, LookupResult<ResidencyFinding>> Answers = new(StringComparer.Ordinal);
        public readonly List<string> Asked = [];

        public Task<LookupResult<ResidencyFinding>> QueryAsync(string personalCode, CancellationToken cancellationToken = default)
        {
            Asked.Add(personalCode);
            return Task.FromResult(Answers.TryGetValue(personalCode, out var a) ? a : LookupResult<ResidencyFinding>.Failed("request timeout or access denied: nobody home"));
        }
    }

    private sealed class FakePension : IPensionLookup
    {
        public readonly Dictionary<string, LookupResult<PensionFinding>> Answers = new(StringComparer.Ordinal);
        public readonly List<string> Asked = [];

        public Task<LookupResult<PensionFinding>> QueryAsync(string personalCode, CancellationToken cancellationToken = default)
        {
            Asked.Add(personalCode);
            return Task.FromResult(Answers.TryGetValue(personalCode, out var a) ? a : LookupResult<PensionFinding>.Failed("request timeout or access denied: nobody home"));
        }
    }

    private static ResidencyFinding Resident() => new(new DateOnly(2026, 11, 4), [], []);

    private static ResidencyFinding NonResidentSince(DateOnly start) => new(new DateOnly(2026, 11, 4), [new DatePeriod(start, null)], []);

    private static PensionFinding Member(decimal rate) => new(PensionStatus.Joined, new DateOnly(2019, 1, 1), null, [new RatePeriod(new DateOnly(2019, 1, 1), null, rate, "Vaikimisi määr")]);

    private static readonly PensionFinding NotJoined = new(PensionStatus.NotJoined, null, null, []);

    private static (SqliteStore Store, Company Company, Period Period) Setup()
    {
        var store = SqliteStore.InMemory();
        var company = store.AddCompany("10235688", "Näidis OÜ");
        var period = store.GetOrCreatePeriod(company.Id, 2026, 10);
        return (store, company, period);
    }

    private static LookupRunner Runner(SqliteStore store, FakeResidency residency, FakePension pension, LookupOptions? options = null, TimeProvider? clock = null) =>
        new(store, residency, pension, options ?? new LookupOptions(TimeSpan.Zero), clock);

    private sealed class FixedClock(DateTime at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(at, DateTimeKind.Utc), TimeSpan.Zero);

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class CancellingPension : IPensionLookup
    {
        public Task<LookupResult<PensionFinding>> QueryAsync(string personalCode, CancellationToken cancellationToken = default) => throw new OperationCanceledException();
    }

    [Fact]
    public void The_control_date_is_the_latest_payout_of_the_month_and_never_later_than_its_last_day()
    {
        var october = new Period(Guid.NewGuid(), Guid.NewGuid(), 2026, 10, PeriodState.Draft, null, null, null);
        Assert.Equal(new DateOnly(2026, 10, 31), LookupRunner.ControlDateOf(october));
        Assert.Equal(new DateOnly(2028, 2, 29), LookupRunner.ControlDateOf(new Period(Guid.NewGuid(), Guid.NewGuid(), 2028, 2, PeriodState.Draft, null, null, null)));
        Assert.Equal(new DateOnly(2026, 10, 20), LookupRunner.ControlDateOf(october, [new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 20), new DateOnly(2026, 11, 2)]));
        Assert.Equal(new DateOnly(2026, 10, 31), LookupRunner.ControlDateOf(october, []));
        Assert.Equal(new DateOnly(2026, 10, 31), LookupRunner.ControlDateOf(october, [new DateOnly(2026, 9, 30), new DateOnly(2026, 11, 2)])); // no payout in the month: its last day
    }

    [Fact]
    public async Task A_run_checks_each_person_on_their_latest_payout_of_the_month()
    {
        var (store, company, period, _) = ReviewFixtures.Setup();
        using (store)
        {
            new MatchRunner(store).Run(period.Id);
            var residency = new FakeResidency
            {
                Answers = { [ReviewFixtures.MariCode] = LookupResult<ResidencyFinding>.Found(Resident(), "ei leitud"), [ReviewFixtures.JaanCode] = LookupResult<ResidencyFinding>.Found(Resident(), "ei leitud") },
            };
            var pension = new FakePension
            {
                Answers = { [ReviewFixtures.MariCode] = LookupResult<PensionFinding>.Found(Member(0.02m), "liitunud"), [ReviewFixtures.JaanCode] = LookupResult<PensionFinding>.Found(Member(0.02m), "liitunud") },
            };
            var runner = Runner(store, residency, pension);

            var report = await runner.RunAsync(period.Id);
            Assert.Equal(Control, report.ControlDate); // the period's last day bounds every check
            var mari = store.FindPerson(company.Id, ReviewFixtures.MariCode)!;
            var jaan = store.FindPerson(company.Id, ReviewFixtures.JaanCode)!;
            // each person's outcome names the day they were checked for, which a line about them shows
            Assert.Equal(new DateOnly(2026, 10, 5), report.Persons.Single(p => p.PersonId == mari.Id).ControlDate);
            Assert.Equal(new DateOnly(2026, 10, 31), report.Persons.Single(p => p.PersonId == jaan.Id).ControlDate);
            Assert.NotEqual(report.Persons[0], report.Persons[0] with { ControlDate = new DateOnly(2026, 10, 6) });
            // nobody asked, no day named
            Assert.Null((await runner.LookUpAsync(Person.New(company.Id, "Kati", "Kask", null), Control)).ControlDate);
            Assert.All(store.Lookups(mari.Id), r => Assert.Equal(new DateOnly(2026, 10, 5), r.ControlDate)); // paid on the 5th
            Assert.All(store.Lookups(jaan.Id), r => Assert.Equal(new DateOnly(2026, 10, 31), r.ControlDate)); // paid in November only: the month's last day
            Assert.Equal(new DateOnly(2026, 10, 5), runner.ControlDateFor(mari, period));
            Assert.Equal(new DateOnly(2026, 10, 31), runner.ControlDateFor(jaan, period));
            Assert.Equal(new DateOnly(2026, 10, 31), runner.ControlDateFor(Person.New(company.Id, "Kati", "Kask", "47001010000"), period)); // not in the run
        }
    }

    [Fact]
    public async Task A_resident_member_gets_both_facts_and_both_answers_recorded()
    {
        var (store, company, period) = Setup();
        using (store)
        {
            var mari = store.AddPerson(Person.New(company.Id, "Mari", "Maasikas", "48712219406"));
            var residency = new FakeResidency { Answers = { ["48712219406"] = LookupResult<ResidencyFinding>.Found(Resident(), "Otsingutulemustele vastavaid isikuid ei leitud.") } };
            var pension = new FakePension { Answers = { ["48712219406"] = LookupResult<PensionFinding>.Found(Member(0.04m), "Isik on kohustusliku kogumispensioniga liitunud.") } };

            var report = await Runner(store, residency, pension).RunAsync(period.Id);

            Assert.Equal(Control, report.ControlDate);
            Assert.Contains("no match run", Assert.Single(report.Problems));
            var outcome = Assert.Single(report.Persons);
            Assert.Equal(["asked residency", "residency resident", "asked pension", "pension member, rate 4 %"], outcome.Steps);
            Assert.Empty(outcome.Problems);
            Assert.Equal(2, report.Queries);

            var person = store.FindPerson(company.Id, "48712219406")!;
            Assert.Equal((Residency.Resident, LookupSource.Emta, true, 0.04m, LookupSource.Pensionikeskus), (person.Residency, person.ResidencySource, person.FundedPensionMember, person.PensionRate, person.PensionSource));
            Assert.NotNull(person.ResidencyCheckedOn);
            Assert.NotNull(person.PensionCheckedOn);

            var records = store.Lookups(mari.Id);
            Assert.Equal([LookupKind.Residency, LookupKind.Pension], records.Select(r => r.Kind));
            Assert.Equal([LookupSource.Emta, LookupSource.Pensionikeskus], records.Select(r => r.Source));
            Assert.All(records, r => Assert.Equal(Control, r.ControlDate));
            Assert.Equal(Member(0.04m), JsonSerializer.Deserialize<PensionFinding>(records[1].FindingJson!, LayoutCatalog.CompactJson));
            Assert.Equal("Isik on kohustusliku kogumispensioniga liitunud.", records[1].Page);
            Assert.Equal(records[1], store.LatestLookup(mari.Id, LookupKind.Pension, Control));
            Assert.Null(store.LatestLookup(mari.Id, LookupKind.Pension, Control.AddMonths(1)));
        }
    }

    [Fact]
    public async Task A_non_resident_is_not_asked_about_the_pension_and_a_second_run_reuses_the_answers()
    {
        var (store, company, period) = Setup();
        using (store)
        {
            store.AddPerson(Person.New(company.Id, "Jaan", "Tamm", "38810140309"));
            var residency = new FakeResidency { Answers = { ["38810140309"] = LookupResult<ResidencyFinding>.Found(NonResidentSince(new DateOnly(2026, 3, 1)), null) } };
            var pension = new FakePension();
            var runner = Runner(store, residency, pension);

            var report = await runner.RunAsync(period.Id);

            var outcome = Assert.Single(report.Persons);
            Assert.Equal(["asked residency", "residency non-resident", "pension not asked: a non-resident", "pension not a member"], outcome.Steps);
            Assert.Empty(pension.Asked);
            var person = store.FindPerson(company.Id, "38810140309")!;
            Assert.Equal((Residency.NonResident, false, (decimal?)null, LookupSource.Derived), (person.Residency, person.FundedPensionMember, person.PensionRate, person.PensionSource));
            var derived = store.LatestLookup(person.Id, LookupKind.Pension, Control)!;
            Assert.Equal((LookupSource.Derived, "not asked: a non-resident"), (derived.Source, derived.Note));

            // the second run for the same month asks nothing: the answers are recorded
            var again = await runner.RunAsync(period.Id);
            Assert.Single(residency.Asked);
            Assert.Contains("reused", Assert.Single(again.Persons).Steps[0]);
            Assert.Equal(2, store.Lookups(person.Id).Count);

            // another month asks again; a forced run too
            var november = store.GetOrCreatePeriod(company.Id, 2026, 11);
            await runner.RunAsync(november.Id);
            Assert.Equal(2, residency.Asked.Count);
            await Runner(store, residency, pension, new LookupOptions(TimeSpan.Zero, Force: true)).RunAsync(period.Id);
            Assert.Equal(3, residency.Asked.Count);
        }
    }

    [Fact]
    public async Task A_failed_answer_is_recorded_and_the_facts_stand_as_they_were()
    {
        var (store, company, period) = Setup();
        using (store)
        {
            var before = store.AddPerson(Person.New(company.Id, "Mari", "Maasikas", "48712219406") with
            {
                FundedPensionMember = true, PensionRate = 0.02m, PensionCheckedOn = new DateOnly(2026, 9, 30), ResidencyCheckedOn = new DateOnly(2026, 9, 30), Notes = "a note",
            });
            var residency = new FakeResidency(); // answers nothing
            var pension = new FakePension();

            var report = await Runner(store, residency, pension).RunAsync(period.Id);

            var outcome = Assert.Single(report.Persons);
            Assert.StartsWith(LookupRunner.Failed, Assert.Single(outcome.Problems));
            Assert.Contains("nobody home", outcome.Problems[0]);
            Assert.Equal(before, store.FindPerson(company.Id, "48712219406")); // untouched, the note included
            var record = Assert.Single(store.Lookups(before.Id));
            Assert.False(record.Succeeded);
            Assert.Contains("nobody home", record.Failure);
            Assert.Empty(pension.Asked); // no residency, no pension question

            // a failed answer is not reused: the next run asks again
            residency.Answers["48712219406"] = LookupResult<ResidencyFinding>.Found(Resident(), null);
            pension.Answers["48712219406"] = LookupResult<PensionFinding>.Failed("content error: no known answer on the page");
            var second = await Runner(store, residency, pension).RunAsync(period.Id);
            Assert.Equal(2, residency.Asked.Count);
            var after = store.FindPerson(company.Id, "48712219406")!;
            Assert.Equal((LookupSource.Emta, true, 0.02m, new DateOnly(2026, 9, 30)), (after.ResidencySource, after.FundedPensionMember, after.PensionRate, after.PensionCheckedOn)); // the pension facts untouched by the failed pension answer
            Assert.Equal(before.FactsControlDate, after.FactsControlDate); // stamped once both register facts are of the month, not by the residency alone
            Assert.Contains(second.Persons[0].Problems, p => p.StartsWith(LookupRunner.Failed) && p.Contains("pension"));
        }
    }

    [Fact]
    public async Task The_accountants_hand_is_kept_and_a_disagreement_is_said()
    {
        var (store, company, period) = Setup();
        using (store)
        {
            store.AddPerson(Person.New(company.Id, "Jaan", "Tamm", "38810140309", Residency.NonResident) with
            {
                ResidencySource = LookupSource.Manual, ResidenceCountry = "FI", FundedPensionMember = false, PensionSource = LookupSource.Manual,
            });
            var residency = new FakeResidency { Answers = { ["38810140309"] = LookupResult<ResidencyFinding>.Found(Resident(), null) } };
            var pension = new FakePension { Answers = { ["38810140309"] = LookupResult<PensionFinding>.Found(Member(0.02m), null) } };

            var report = await Runner(store, residency, pension).RunAsync(period.Id);

            var outcome = Assert.Single(report.Persons);
            Assert.Equal(["asked residency", "residency set by hand, kept", "pension not asked: a non-resident", "pension set by hand, kept"], outcome.Steps);
            var disagreement = Assert.Single(outcome.Problems);
            Assert.StartsWith(LookupRunner.Disagrees, disagreement);
            Assert.Contains("register says resident", disagreement);
            Assert.DoesNotContain("Pensionikeskus says", string.Join(" ", outcome.Problems)); // not asked: nothing to credit it with
            var person = store.FindPerson(company.Id, "38810140309")!;
            Assert.Equal((Residency.NonResident, "FI", LookupSource.Manual, false, LookupSource.Manual), (person.Residency, person.ResidenceCountry, person.ResidencySource, person.FundedPensionMember, person.PensionSource));
            Assert.Empty(pension.Asked); // the hand says non-resident: no pension question
            Assert.Single(store.Lookups(person.Id), r => r.Kind == LookupKind.Residency); // the answer is still recorded

            // a hand-set pension fact against a fresh answer
            var mari = store.AddPerson(Person.New(company.Id, "Mari", "Maasikas", "48712219406") with { FundedPensionMember = true, PensionRate = 0.06m, PensionSource = LookupSource.Manual });
            residency.Answers["48712219406"] = LookupResult<ResidencyFinding>.Found(Resident(), null);
            pension.Answers["48712219406"] = LookupResult<PensionFinding>.Found(Member(0.02m), null);
            var second = await Runner(store, residency, pension).RunAsync(period.Id);
            var mariOutcome = Assert.Single(second.Persons, p => p.PersonId == mari.Id);
            Assert.Contains(mariOutcome.Problems, p => p.StartsWith(LookupRunner.Disagrees) && p.Contains("2 %") && p.Contains("6 %"));
            Assert.Equal(0.06m, store.FindPerson(company.Id, "48712219406")!.PensionRate);
        }
    }

    [Fact]
    public async Task A_derived_answer_is_never_reused_as_the_registers_once_the_person_counts_as_a_resident()
    {
        var (store, company, period) = Setup();
        using (store)
        {
            var jaan = store.AddPerson(Person.New(company.Id, "Jaan", "Tamm", "38810140309"));
            var residency = new FakeResidency { Answers = { ["38810140309"] = LookupResult<ResidencyFinding>.Found(NonResidentSince(new DateOnly(2026, 3, 1)), null) } };
            var pension = new FakePension { Answers = { ["38810140309"] = LookupResult<PensionFinding>.Found(Member(0.02m), null) } };
            await Runner(store, residency, pension).RunAsync(period.Id);
            Assert.Equal((false, LookupSource.Derived), (store.FindPerson(company.Id, "38810140309")!.FundedPensionMember, store.FindPerson(company.Id, "38810140309")!.PensionSource));

            // the accountant sets the person resident by hand: the Derived "not a member" must not pass for Pensionikeskus's answer
            store.UpdatePerson(store.FindPerson(company.Id, "38810140309")! with { Residency = Residency.Resident, ResidencySource = LookupSource.Manual });
            var report = await Runner(store, residency, pension).RunAsync(period.Id);

            Assert.Equal(["38810140309"], pension.Asked);
            var person = store.FindPerson(company.Id, "38810140309")!;
            Assert.Equal((true, 0.02m, LookupSource.Pensionikeskus), (person.FundedPensionMember, person.PensionRate, person.PensionSource));
            Assert.Contains(report.Persons[0].Problems, p => p.StartsWith(LookupRunner.Disagrees)); // the register still says non-resident
            Assert.NotNull(jaan);
        }
    }

    [Fact]
    public async Task A_failed_pension_answer_after_a_residency_flip_leaves_no_stale_derived_fact()
    {
        var (store, company, period) = Setup();
        using (store)
        {
            store.AddPerson(Person.New(company.Id, "Jaan", "Tamm", "38810140309"));
            var residency = new FakeResidency { Answers = { ["38810140309"] = LookupResult<ResidencyFinding>.Found(NonResidentSince(new DateOnly(2026, 3, 1)), null) } };
            var pension = new FakePension();
            await Runner(store, residency, pension).RunAsync(period.Id);

            // EMTA now says resident (a corrected register) and Pensionikeskus does not answer
            residency.Answers["38810140309"] = LookupResult<ResidencyFinding>.Found(Resident(), null);
            var report = await Runner(store, residency, pension, new LookupOptions(TimeSpan.Zero, Force: true)).RunAsync(period.Id);

            var person = store.FindPerson(company.Id, "38810140309")!;
            Assert.Equal((Residency.Resident, (bool?)null, (decimal?)null, LookupSource.Unknown), (person.Residency, person.FundedPensionMember, person.PensionRate, person.PensionSource));
            Assert.Contains(report.Persons[0].Problems, p => p.StartsWith(LookupRunner.Reset));
            Assert.Contains(report.Persons[0].Problems, p => p.StartsWith(LookupRunner.Failed) && p.Contains("pension"));
        }
    }

    [Fact]
    public async Task A_corrected_personal_code_is_asked_about_afresh()
    {
        var (store, company, period) = Setup();
        using (store)
        {
            var person = store.AddPerson(Person.New(company.Id, "Mari", "Maasikas", "48712219406"));
            var residency = new FakeResidency { Answers = { ["48712219406"] = LookupResult<ResidencyFinding>.Found(Resident(), null), ["48712219417"] = LookupResult<ResidencyFinding>.Found(NonResidentSince(new DateOnly(2020, 1, 1)), null) } };
            var pension = new FakePension { Answers = { ["48712219406"] = LookupResult<PensionFinding>.Found(Member(0.02m), null) } };
            await Runner(store, residency, pension).RunAsync(period.Id);

            store.UpdatePerson(store.FindPerson(company.Id, "48712219406")! with { PersonalCode = "48712219417" });
            var report = await Runner(store, residency, pension).RunAsync(period.Id);

            Assert.Equal(["48712219406", "48712219417"], residency.Asked);
            var after = store.FindPerson(company.Id, "48712219417")!;
            Assert.Equal((person.Id, Residency.NonResident), (after.Id, after.Residency));
            Assert.Equal("48712219417", store.LatestLookup(person.Id, LookupKind.Residency, Control)!.PersonalCode);
            Assert.DoesNotContain("reused", string.Join(" ", report.Persons[0].Steps));
        }
    }

    [Fact]
    public async Task A_hand_set_residency_carries_the_run_past_a_failed_register_answer()
    {
        var (store, company, period) = Setup();
        using (store)
        {
            store.AddPerson(Person.New(company.Id, "Mari", "Maasikas", "48712219406") with { ResidencySource = LookupSource.Manual });
            var residency = new FakeResidency(); // EMTA does not answer
            var pension = new FakePension { Answers = { ["48712219406"] = LookupResult<PensionFinding>.Found(Member(0.02m), null) } };

            var report = await Runner(store, residency, pension).RunAsync(period.Id);

            var outcome = Assert.Single(report.Persons);
            Assert.Contains(outcome.Problems, p => p.StartsWith(LookupRunner.Failed) && p.Contains("accountant's own Resident"));
            Assert.Equal(["48712219406"], pension.Asked);
            var person = store.FindPerson(company.Id, "48712219406")!;
            Assert.Equal((true, 0.02m, LookupSource.Pensionikeskus, LookupSource.Manual), (person.FundedPensionMember, person.PensionRate, person.PensionSource, person.ResidencySource));
        }
    }

    [Fact]
    public async Task A_member_without_a_rate_row_for_the_day_keeps_an_unknown_rate_never_a_default()
    {
        var (store, company, period) = Setup();
        using (store)
        {
            store.AddPerson(Person.New(company.Id, "Mari", "Maasikas", "48712219406"));
            var oldRateOnly = new PensionFinding(PensionStatus.Joined, new DateOnly(2010, 1, 1), null, [new RatePeriod(new DateOnly(2010, 1, 1), new DateOnly(2024, 12, 31), 0.02m, null)]);
            var residency = new FakeResidency { Answers = { ["48712219406"] = LookupResult<ResidencyFinding>.Found(Resident(), null) } };
            var pension = new FakePension { Answers = { ["48712219406"] = LookupResult<PensionFinding>.Found(oldRateOnly, null) } };

            var report = await Runner(store, residency, pension).RunAsync(period.Id);

            var outcome = Assert.Single(report.Persons);
            Assert.StartsWith(LookupRunner.RateUnknown, Assert.Single(outcome.Problems));
            var person = store.FindPerson(company.Id, "48712219406")!;
            Assert.Equal((true, (decimal?)null, LookupSource.Pensionikeskus), (person.FundedPensionMember, person.PensionRate, person.PensionSource));
            Assert.Contains("rate unknown", outcome.Steps[^1]);
        }
    }

    [Fact]
    public async Task The_persons_of_a_match_run_are_looked_up_and_created_from_the_register_when_missing()
    {
        var (store, company, period) = Setup();
        using (store)
        {
            var bytes = Tables.Xlsx(
                ("Töötajate loend", [TorHeaders.All.ToArray<object?>(),
                    ["1", 48712219406d, "MAASIKAS", "MARI", new DateTime(2024, 2, 15), 1, "Tööleping", 2, "Kehtiv", null, null, null, null, null, null, null, null, "EE", null, null, null, null, null, null, null, null, 10235688, "Näidis OÜ"],
                    ["2", 38810140309d, "TAMM", "JAAN", new DateTime(2020, 1, 1), 600, "VÕS leping", 2, "Kehtiv", null, null, null, null, null, null, null, null, "EE", null, null, null, null, null, null, null, null, 10235688, "Näidis OÜ"],
                    ["3", null, "KASK", "PEETER", new DateTime(2020, 1, 1), 1, "Tööleping", 2, "Kehtiv", null, null, null, null, null, null, null, null, "EE", null, null, null, null, null, null, null, null, 10235688, "Näidis OÜ"]], false),
                ("LHV", [LayoutCatalog.Default.Find("lhv-et-short")!.Headers.ToArray<object?>(),
                    ["EE497700771001131757", 1, new DateTime(2026, 10, 5), "EE12", "Mari Maasikas", "D", -905.92, null, "1", "palk", "EUR"]], false));
            var file = store.AddImport(company.Id, Importer.Read("palgad.xlsx", bytes), bytes);
            store.LinkImport(period.Id, file.Id);
            new MatchRunner(store).Run(period.Id);
            var jaan = store.AddPerson(Person.New(company.Id, "Jaan", "Tamm", "38810140309") with { Notes = "already here" });
            var residency = new FakeResidency { Answers = { ["48712219406"] = LookupResult<ResidencyFinding>.Found(Resident(), null), ["38810140309"] = LookupResult<ResidencyFinding>.Found(Resident(), null) } };
            var pension = new FakePension { Answers = { ["48712219406"] = LookupResult<PensionFinding>.Found(Member(0.02m), null), ["38810140309"] = LookupResult<PensionFinding>.Found(NotJoined, null) } };

            var report = await Runner(store, residency, pension).RunAsync(period.Id);

            Assert.Equal(["48712219406", "38810140309"], report.Persons.Select(p => p.PersonalCode)); // register order
            Assert.Contains(report.Problems, p => p.StartsWith(LookupRunner.Skipped) && p.Contains("no personal code"));
            var mari = store.FindPerson(company.Id, "48712219406")!;
            Assert.Equal(("MARI", "MAASIKAS", "1", true, 0.02m), (mari.FirstName, mari.LastName, mari.TorEmploymentTypeId, mari.FundedPensionMember, mari.PensionRate)); // created from the register
            var jaanAfter = store.FindPerson(company.Id, "38810140309")!;
            Assert.Equal((jaan.Id, "already here", false), (jaanAfter.Id, jaanAfter.Notes, jaanAfter.FundedPensionMember)); // found, not duplicated
            Assert.Equal(2, store.Persons(company.Id).Count); // Mari created, Jaan kept, Peeter never (no code)
        }
    }

    [Fact]
    public async Task Requests_are_paced_and_a_person_without_a_valid_code_is_skipped()
    {
        var (store, company, period) = Setup();
        using (store)
        {
            store.AddPerson(Person.New(company.Id, "Mari", "Maasikas", "48712219406"));
            store.AddPerson(Person.New(company.Id, "Jaan", "Tamm", "38810140309"));
            store.AddPerson(Person.New(company.Id, "Peeter", "Kask", "123"));
            var residency = new FakeResidency { Answers = { ["48712219406"] = LookupResult<ResidencyFinding>.Found(Resident(), null), ["38810140309"] = LookupResult<ResidencyFinding>.Found(Resident(), null) } };
            var pension = new FakePension { Answers = { ["48712219406"] = LookupResult<PensionFinding>.Found(NotJoined, null), ["38810140309"] = LookupResult<PensionFinding>.Found(NotJoined, null) } };

            var watch = Stopwatch.StartNew();
            var report = await Runner(store, residency, pension, new LookupOptions(TimeSpan.FromMilliseconds(60))).RunAsync(period.Id);
            watch.Stop();

            Assert.Equal(4, report.Queries); // four requests, three gaps of at least 60 ms between them
            Assert.True(watch.ElapsedMilliseconds >= 150, $"{watch.ElapsedMilliseconds} ms");
            var peeter = Assert.Single(report.Persons, p => p.PersonalCode == "123");
            Assert.StartsWith(LookupRunner.Skipped, Assert.Single(peeter.Problems));
            Assert.Empty(peeter.Steps);
        }
    }

    [Fact]
    public async Task A_run_reports_how_many_it_asks_before_the_first_and_a_cancelled_run_reports_no_more()
    {
        var (store, _, period, _) = ReviewFixtures.Setup();
        using (store)
        {
            new MatchRunner(store).Run(period.Id);
            using var cancel = new CancellationTokenSource();
            var reports = new List<LookupProgress>();
            var progress = new Reported(p =>
            {
                reports.Add(p);
                if (p.Done == 1)
                {
                    cancel.Cancel(); // the stop pressed after the first person
                }
            });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Runner(store, new FakeResidency(), new FakePension()).RunAsync(period.Id, cancel.Token, progress));
            Assert.Equal([(0, 2, false), (1, 2, true)], reports.Select(r => (r.Done, r.Total, r.Outcome is not null)));
        }
    }

    /// <summary>A progress reported at once, on the run's own thread (a Progress&lt;T&gt; would post it later).</summary>
    private sealed class Reported(Action<LookupProgress> report) : IProgress<LookupProgress>
    {
        public void Report(LookupProgress value) => report(value);
    }

    [Fact]
    public async Task The_run_refuses_a_period_it_does_not_know()
    {
        var (store, _, _) = Setup();
        using (store)
        {
            var runner = Runner(store, new FakeResidency(), new FakePension());
            await Assert.ThrowsAsync<KeyNotFoundException>(() => runner.RunAsync(Guid.NewGuid()));
        }
    }
    [Fact]
    public async Task A_rent_payee_the_review_made_a_person_is_looked_up_on_the_day_of_the_rent()
    {
        const string renterCode = "49001010008";
        var (store, company, period, _) = ReviewFixtures.Setup(renter: "KATI KASK", renterCode: renterCode);
        using (store)
        {
            new MatchRunner(store).Run(period.Id);
            new SatellesTSD.Core.Review.ReviewWorkspace(store).Load(period.Id); // the review makes her a person of the company
            var residency = new FakeResidency();
            var pension = new FakePension();
            var runner = Runner(store, residency, pension);
            await runner.RunAsync(period.Id);
            Assert.Contains(renterCode, residency.Asked);
            var kati = store.FindPerson(company.Id, renterCode)!;
            Assert.Equal(new DateOnly(2026, 10, 15), runner.ControlDateFor(kati, period)); // the rent's day
        }
    }
    [Fact]
    public async Task A_register_persons_control_date_follows_the_review_not_the_searchs_raw_hits()
    {
        var (store, company, period, _) = ReviewFixtures.Setup();
        using (store)
        {
            new MatchRunner(store).Run(period.Id);
            var workspace = new SatellesTSD.Core.Review.ReviewWorkspace(store);
            var view = workspace.Load(period.Id);
            var residency = new FakeResidency { Answers = { [ReviewFixtures.MariCode] = LookupResult<ResidencyFinding>.Found(Resident(), null), [ReviewFixtures.JaanCode] = LookupResult<ResidencyFinding>.Found(Resident(), null) } };
            var pension = new FakePension { Answers = { [ReviewFixtures.MariCode] = LookupResult<PensionFinding>.Found(NotJoined, null), [ReviewFixtures.JaanCode] = LookupResult<PensionFinding>.Found(NotJoined, null) } };
            var runner = Runner(store, residency, pension, new LookupOptions(TimeSpan.Zero, Force: true));
            var mari = store.FindPerson(company.Id, ReviewFixtures.MariCode)!;
            Assert.Equal(new DateOnly(2026, 10, 5), runner.ControlDateFor(mari, period)); // the salary's day, as the search gave it

            // the salary given to nobody: no payout of hers stands, the month's last day
            var salary = view.Persons[0].Payments.Single(p => !p.IsByHand);
            workspace.SetPerson(salary, null);
            Assert.Equal(new DateOnly(2026, 10, 31), runner.ControlDateFor(mari, period));

            // a row the accountant gave her, and a line added by hand: the latest of what stands
            workspace.SetPerson(view.Unmatched.Payments.Single(p => p.Payee == "Kinnisvara OÜ"), mari.Id); // the 12th
            Assert.Equal(new DateOnly(2026, 10, 12), runner.ControlDateFor(mari, period));
            workspace.AddHandLine(period.Id, mari.Id, new DateOnly(2026, 10, 20), 100m, "Mari Maasikas", "lisatasu", "Palgad");
            Assert.Equal(new DateOnly(2026, 10, 20), runner.ControlDateFor(mari, period));

            // the batch asks on the same day
            await runner.RunAsync(period.Id);
            Assert.Contains(ReviewFixtures.MariCode, residency.Asked);
            var records = store.Lookups(mari.Id);
            Assert.Equal(2, records.Count); // the residency and the pension
            Assert.All(records, r => Assert.Equal(new DateOnly(2026, 10, 20), r.ControlDate));

            // the hand line taken away: the row given to her dates the lookup again
            workspace.RemoveHandLine(period.Id, workspace.Load(period.Id).Persons[0].Payments.Single(p => p.IsByHand).LineId!.Value);
            Assert.Equal(new DateOnly(2026, 10, 12), runner.ControlDateFor(mari, period));
        }
    }

    [Fact]
    public void A_row_the_search_found_for_two_persons_dates_nobody_until_the_accountant_names_one()
    {
        var (store, company, period, _) = ReviewFixtures.Setup(twin: true); // a twin of the first person by name: the salary row is theirs both
        using (store)
        {
            new MatchRunner(store).Run(period.Id);
            var workspace = new SatellesTSD.Core.Review.ReviewWorkspace(store);
            var view = workspace.Load(period.Id);
            var runner = Runner(store, new FakeResidency(), new FakePension());
            var mari = store.FindPerson(company.Id, ReviewFixtures.MariCode)!;
            var twin = store.FindPerson(company.Id, ReviewFixtures.TwinCode)!;
            var shared = view.Unmatched.Payments.Single(p => p.IsShared);
            Assert.Equal((new DateOnly(2026, 10, 31), new DateOnly(2026, 10, 31)), (runner.ControlDateFor(mari, period), runner.ControlDateFor(twin, period))); // nobody's row dates nobody

            workspace.SetPerson(shared, mari.Id);
            Assert.Equal((new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 31)), (runner.ControlDateFor(mari, period), runner.ControlDateFor(twin, period)));
        }
    }

    [Fact]
    public async Task A_person_outside_the_register_given_a_row_by_hand_is_looked_up_on_its_day_and_a_row_given_nobody_asks_nobody()
    {
        const string otherCode = "39001010008";
        var (store, company, period, _) = ReviewFixtures.Setup(renter: "KATI KASK", renterCode: "49001010008");
        using (store)
        {
            new MatchRunner(store).Run(period.Id);
            var workspace = new SatellesTSD.Core.Review.ReviewWorkspace(store);
            var view = workspace.Load(period.Id);
            var peeter = workspace.AddPerson(company.Id, "Peeter", "Pihl", otherCode, null);
            workspace.SetPerson(view.Unmatched.Payments.Single(p => p.Payee == "Kinnisvara OÜ"), peeter.Id);
            workspace.SetPerson(view.Persons.Single(p => p.Person?.LastName == "Kask").Payments.Single(), null); // the rent is nobody's

            var residency = new FakeResidency();
            var runner = Runner(store, residency, new FakePension());
            await runner.RunAsync(period.Id);
            Assert.Contains(otherCode, residency.Asked);
            Assert.DoesNotContain("49001010008", residency.Asked);
            Assert.Equal(new DateOnly(2026, 10, 12), runner.ControlDateFor(peeter, period));
        }
    }

    [Fact]
    public async Task A_register_person_listed_without_a_code_is_asked_by_the_code_on_the_card()
    {
        var (store, company, period, _) = ReviewFixtures.Setup(jaanCodeless: true);
        using (store)
        {
            // the person the codeless register row made (bound to its names key), given the code on the card
            var jaan = store.AddPerson(Person.New(company.Id, "Jaan", "Tamm", null) with { RegisterKey = SearchPerson.NameKey("TAMM", "JAAN") });
            store.UpdatePerson(jaan with { PersonalCode = ReviewFixtures.JaanCode });
            new MatchRunner(store).Run(period.Id);
            var residency = new FakeResidency { Answers = { [ReviewFixtures.MariCode] = LookupResult<ResidencyFinding>.Found(Resident(), null), [ReviewFixtures.JaanCode] = LookupResult<ResidencyFinding>.Found(Resident(), null) } };
            var pension = new FakePension { Answers = { [ReviewFixtures.MariCode] = LookupResult<PensionFinding>.Found(NotJoined, null), [ReviewFixtures.JaanCode] = LookupResult<PensionFinding>.Found(NotJoined, null) } };

            var report = await Runner(store, residency, pension).RunAsync(period.Id);

            Assert.Contains(ReviewFixtures.JaanCode, report.Persons.Select(p => p.PersonalCode));
            Assert.DoesNotContain(report.Problems, p => p.StartsWith(LookupRunner.Skipped, StringComparison.Ordinal));
            Assert.Equal(LookupSource.Emta, store.FindPerson(company.Id, ReviewFixtures.JaanCode)!.ResidencySource);
        }
    }

    [Fact]
    public async Task A_cancel_between_the_two_questions_leaves_no_stale_not_a_member_on_a_person_now_resident()
    {
        var (store, company, period) = Setup();
        using (store)
        {
            store.AddPerson(Person.New(company.Id, "Jaan", "Tamm", "38810140309") with { Residency = Residency.NonResident, ResidencySource = LookupSource.Emta, FundedPensionMember = false, PensionSource = LookupSource.Derived });
            var residency = new FakeResidency { Answers = { ["38810140309"] = LookupResult<ResidencyFinding>.Found(Resident(), null) } };
            var runner = new LookupRunner(store, residency, new CancellingPension(), new LookupOptions(TimeSpan.Zero));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(period.Id));

            var person = store.FindPerson(company.Id, "38810140309")!;
            Assert.Equal((Residency.Resident, (bool?)null, LookupSource.Unknown), (person.Residency, person.FundedPensionMember, person.PensionSource)); // the inference went with the residency it followed from
        }
    }

    [Fact]
    public async Task A_resident_now_whose_pension_answer_comes_is_a_member_with_nothing_said_of_the_reset()
    {
        var (store, company, period) = Setup();
        using (store)
        {
            store.AddPerson(Person.New(company.Id, "Jaan", "Tamm", "38810140309") with { Residency = Residency.NonResident, ResidencySource = LookupSource.Emta, FundedPensionMember = false, PensionSource = LookupSource.Derived });
            var residency = new FakeResidency { Answers = { ["38810140309"] = LookupResult<ResidencyFinding>.Found(Resident(), null) } };
            var pension = new FakePension { Answers = { ["38810140309"] = LookupResult<PensionFinding>.Found(Member(0.02m), null) } };

            var report = await Runner(store, residency, pension).RunAsync(period.Id);

            var outcome = Assert.Single(report.Persons);
            Assert.DoesNotContain(outcome.Problems, p => p.StartsWith(LookupRunner.Reset, StringComparison.Ordinal)); // the inference went, the answer came: nothing to say
            var person = store.FindPerson(company.Id, "38810140309")!;
            Assert.Equal((Residency.Resident, true, 0.02m, LookupSource.Pensionikeskus), (person.Residency, person.FundedPensionMember, person.PensionRate, person.PensionSource));
        }
    }

    [Fact]
    public async Task One_person_asked_afresh_gets_todays_answer_while_a_run_reuses_the_recorded_one()
    {
        var (store, company, period) = Setup();
        using (store)
        {
            var added = store.AddPerson(Person.New(company.Id, "Mari", "Maasikas", "48712219406"));
            var residency = new FakeResidency { Answers = { ["48712219406"] = LookupResult<ResidencyFinding>.Found(Resident(), null) } };
            var pension = new FakePension { Answers = { ["48712219406"] = LookupResult<PensionFinding>.Found(NotJoined, null) } };
            var runner = Runner(store, residency, pension);
            await runner.RunAsync(period.Id);

            // the registers correct themselves: a run over the month still reuses what it has
            residency.Answers["48712219406"] = LookupResult<ResidencyFinding>.Found(Resident(), null);
            pension.Answers["48712219406"] = LookupResult<PensionFinding>.Found(Member(0.04m), null);
            await runner.RunAsync(period.Id);
            Assert.Equal((1, 1), (residency.Asked.Count, pension.Asked.Count));
            Assert.False(store.FindPerson(company.Id, "48712219406")!.FundedPensionMember);

            // the one person asked afresh: both registers are asked now, and the new answer lands
            var outcome = await runner.LookUpAsync(store.FindPerson(company.Id, "48712219406")!, Control, afresh: true);
            Assert.Equal(["asked residency", "residency resident", "asked pension", "pension member, rate 4 %"], outcome.Steps);
            Assert.Empty(outcome.Disagreements);
            Assert.Equal((2, 2), (residency.Asked.Count, pension.Asked.Count));
            var person = store.FindPerson(company.Id, "48712219406")!;
            Assert.Equal((true, 0.04m, LookupSource.Pensionikeskus), (person.FundedPensionMember, person.PensionRate, person.PensionSource));
            Assert.Equal(2, store.Lookups(added.Id).Count(r => r.Kind == LookupKind.Pension)); // both answers are kept

            // a fresh answer that fails leaves the facts as the earlier answer set them
            pension.Answers.Remove("48712219406");
            var failed = await runner.LookUpAsync(person, Control, afresh: true);
            Assert.Contains(failed.Problems, p => p.StartsWith(LookupRunner.Failed, StringComparison.Ordinal));
            Assert.Equal((true, 0.04m), (store.FindPerson(company.Id, "48712219406")!.FundedPensionMember, store.FindPerson(company.Id, "48712219406")!.PensionRate));
        }
    }

    [Fact]
    public async Task A_fact_set_by_hand_is_given_back_to_the_register_only_on_the_accountants_word()
    {
        var (store, company, period) = Setup();
        using (store)
        {
            store.AddPerson(Person.New(company.Id, "Jaan", "Tamm", "38810140309", Residency.NonResident) with
            {
                ResidencySource = LookupSource.Manual, FundedPensionMember = true, PensionRate = 0.06m, PensionSource = LookupSource.Manual,
            });
            var residency = new FakeResidency { Answers = { ["38810140309"] = LookupResult<ResidencyFinding>.Found(Resident(), null) } };
            var pension = new FakePension { Answers = { ["38810140309"] = LookupResult<PensionFinding>.Found(Member(0.02m), null) } };
            var runner = Runner(store, residency, pension);
            Person Jaan() => store.FindPerson(company.Id, "38810140309")!;

            // asked afresh: both facts are kept, and both disagreements are named — the pension's against what follows from the hand's non-residency
            var asked = await runner.LookUpAsync(Jaan(), Control, afresh: true);
            Assert.Equal(
                [new LookupDisagreement(LookupKind.Residency, true, null, false, false, null), new LookupDisagreement(LookupKind.Pension, false, null, true, true, 0.06m)],
                asked.Disagreements);
            Assert.Equal((Residency.NonResident, LookupSource.Manual, true, LookupSource.Manual), (Jaan().Residency, Jaan().ResidencySource, Jaan().FundedPensionMember, Jaan().PensionSource));

            // the residency given back: the answer just recorded is taken, not asked again; a resident now, Pensionikeskus is
            // asked, and its answer is only compared with the pension still set by hand
            var back = await runner.LookUpAsync(Jaan(), Control, handBack: [LookupKind.Residency]);
            Assert.Equal(["residency answer", "residency given back to the register", "residency resident", "asked pension", "pension set by hand, kept"], back.Steps.Select(s => s.StartsWith("residency answer", StringComparison.Ordinal) ? "residency answer" : s));
            Assert.Single(residency.Asked);
            Assert.Equal([new LookupDisagreement(LookupKind.Pension, true, 0.02m, false, true, 0.06m)], back.Disagreements);
            Assert.Equal((Residency.Resident, LookupSource.Emta, 0.06m, LookupSource.Manual), (Jaan().Residency, Jaan().ResidencySource, Jaan().PensionRate, Jaan().PensionSource));

            // the pension given back too: the recorded answer is the fact, and the register's from now on
            var both = await runner.LookUpAsync(Jaan(), Control, handBack: [LookupKind.Pension]);
            Assert.Empty(both.Disagreements);
            Assert.Contains("pension given back to the register", both.Steps);
            Assert.Equal((1, 1), (residency.Asked.Count, pension.Asked.Count));
            Assert.Equal((true, 0.02m, LookupSource.Pensionikeskus), (Jaan().FundedPensionMember, Jaan().PensionRate, Jaan().PensionSource));
            Assert.Equal(Control, Jaan().FactsControlDate);

            // a run over the month afterwards finds nothing to say
            var run = await runner.RunAsync(period.Id);
            Assert.Empty(run.Persons.Single().Problems);
        }
    }

    [Fact]
    public async Task A_pass_that_takes_the_residency_back_never_takes_the_pension_with_it()
    {
        var (store, company, period) = Setup();
        using (store)
        {
            // by hand a resident and a member at 6 %; EMTA says non-resident, Pensionikeskus member at 4 %
            store.AddPerson(Person.New(company.Id, "Jaan", "Tamm", "38810140309") with
            {
                ResidencySource = LookupSource.Manual, FundedPensionMember = true, PensionRate = 0.06m, PensionSource = LookupSource.Manual,
            });
            var residency = new FakeResidency { Answers = { ["38810140309"] = LookupResult<ResidencyFinding>.Found(NonResidentSince(new DateOnly(2026, 1, 1)), null) } };
            var pension = new FakePension { Answers = { ["38810140309"] = LookupResult<PensionFinding>.Found(Member(0.04m), null) } };
            var runner = Runner(store, residency, pension);
            Person Jaan() => store.FindPerson(company.Id, "38810140309")!;

            var asked = await runner.LookUpAsync(Jaan(), Control, afresh: true);
            Assert.Equal(
                [new LookupDisagreement(LookupKind.Residency, false, null, false, true, null), new LookupDisagreement(LookupKind.Pension, true, 0.04m, false, true, 0.06m)],
                asked.Disagreements);

            // both named at once: the residency is taken; the pension, whose answer is now another one (a non-resident's),
            // is kept and named again against that answer
            var back = await runner.LookUpAsync(Jaan(), Control, handBack: [LookupKind.Residency, LookupKind.Pension]);
            Assert.Equal([new LookupDisagreement(LookupKind.Pension, false, null, true, true, 0.06m)], back.Disagreements);
            Assert.Equal((Residency.NonResident, LookupSource.Emta, true, 0.06m, LookupSource.Manual), (Jaan().Residency, Jaan().ResidencySource, Jaan().FundedPensionMember, Jaan().PensionRate, Jaan().PensionSource));

            // given back on its own, against the answer shown: not a member, as a non-resident
            await runner.LookUpAsync(Jaan(), Control, handBack: [LookupKind.Pension]);
            Assert.Equal((false, (decimal?)null, LookupSource.Derived), (Jaan().FundedPensionMember, Jaan().PensionRate, Jaan().PensionSource));
        }
    }

    [Fact]
    public async Task A_fact_given_back_stays_the_hands_when_the_register_has_no_answer()
    {
        var (store, company, period) = Setup();
        using (store)
        {
            store.AddPerson(Person.New(company.Id, "Jaan", "Tamm", "38810140309", Residency.NonResident) with { ResidencySource = LookupSource.Manual, ResidenceCountry = "FI" });
            var residency = new FakeResidency();
            var pension = new FakePension();

            var outcome = await Runner(store, residency, pension).LookUpAsync(store.FindPerson(company.Id, "38810140309")!, Control, handBack: [LookupKind.Residency]);

            Assert.Contains(outcome.Problems, p => p.StartsWith(LookupRunner.Failed, StringComparison.Ordinal));
            Assert.Contains("residency set by hand, kept", outcome.Steps);
            var person = store.FindPerson(company.Id, "38810140309")!;
            Assert.Equal((Residency.NonResident, LookupSource.Manual, "FI"), (person.Residency, person.ResidencySource, person.ResidenceCountry));
        }
    }

    [Fact]
    public async Task A_reused_answer_keeps_the_day_it_was_given()
    {
        var (store, company, period) = Setup();
        using (store)
        {
            store.AddPerson(Person.New(company.Id, "Mari", "Maasikas", "48712219406"));
            var residency = new FakeResidency { Answers = { ["48712219406"] = LookupResult<ResidencyFinding>.Found(Resident(), null) } };
            var pension = new FakePension { Answers = { ["48712219406"] = LookupResult<PensionFinding>.Found(NotJoined, null) } };
            await Runner(store, residency, pension).RunAsync(period.Id);
            var first = store.FindPerson(company.Id, "48712219406")!;
            var day = store.Lookups(first.Id).Select(r => DateOnly.FromDateTime(r.CheckedAt)).Distinct().Single();
            Assert.Equal((day, day), (first.ResidencyCheckedOn, first.PensionCheckedOn)); // the day the answers were given

            // the answers given weeks ago (the records' stamps moved back), the same month asked again: they are reused, and so is their day
            var given = new DateOnly(2026, 10, 20);
            store.ExecuteRaw("UPDATE lookup SET checked_at = $at", ("$at", "2026-10-20T09:00:00.000Z"));
            var again = await Runner(store, residency, pension).RunAsync(period.Id);
            Assert.Contains(again.Persons.Single().Steps, s => s.Contains("reused", StringComparison.Ordinal));
            Assert.Single(residency.Asked);
            var person = store.FindPerson(company.Id, "48712219406")!;
            Assert.Equal((given, given), (person.ResidencyCheckedOn, person.PensionCheckedOn));
        }
    }
}
