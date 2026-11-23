using System.Reflection;
using SatellesTSD.Core.Lookups;
using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Review;
using SatellesTSD.Core.Tax;

namespace SatellesTSD.WindowTests;

/// <summary>The summary after a search or a month's register check (plan item 67): the problems counted by kind as the grids
/// show them, one summary for the offer's search and check together, a check stopped midway summarised and saying so, the
/// company's latest kept in the store and opened again after a restart, and a double-click going to the person.</summary>
public sealed class RunSummaryWindowTests
{
    /// <summary>What a summary window showed, taken while the seam held it.</summary>
    private sealed record Shown(RunSummary Summary, string Header, int GroupRows, IReadOnlyList<int> PlaceRows);

    /// <summary>Takes every summary window the run opens: what it shows, each kind picked once.</summary>
    private static void Watch(MainForm form, List<Shown> shown, Action<SummaryDialog>? then = null) => form.SummaryOpening = dialog =>
    {
        var places = new List<int>();
        for (var i = 0; i < dialog.Groups.Rows.Count; i++)
        {
            dialog.SelectGroup(i);
            places.Add(dialog.Places.Rows.Count);
        }

        shown.Add(new Shown(dialog.Summary, dialog.HeaderText, dialog.Groups.Rows.Count, places));
        then?.Invoke(dialog);
    };

    private static void Idle(MainForm form, string what)
    {
        Ui.Until(() => !form.IsBusy, what: what);
        Ui.Pump();
    }

    private static string[] CodesIn(object? cell) => (cell as string ?? "").Split([", ", "; "], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>A code, not a cell's words beside the codes (the note of a salary outside TÖR).</summary>
    private static bool IsCode(string text) => text.Length > 0 && text.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_');

    private static RunSummary Kept(MainForm form) => RunSummary.FromJson(form.Services!.Store.RunSummary(form.View!.Company.Id)!.SummaryJson)!;

    /// <summary>The fixture with Mari's II pillar never checked: her salary is refused (PENSION_NOT_CHECKED), so the payments say something.</summary>
    private static WindowFixture Unchecked() => new(prepare: (store, company, _, _) =>
    {
        var mari = store.FindPerson(company.Id, SatellesTSD.Tests.ReviewFixtures.MariCode)!;
        store.UpdatePerson(mari with { FundedPensionMember = null, PensionRate = null });
    });

    [Fact]
    public void A_search_is_summarised_by_kind_once_a_place_counted_as_the_grids_show_it_and_kept()
    {
        using var fixture = Unchecked();
        var shown = new List<Shown>();
        Ui.Run(() => fixture.Drive(form =>
        {
            Watch(form, shown);
            form.MatchButton.PerformClick();
            Idle(form, "the search");
            var one = Assert.Single(shown);
            var summary = one.Summary;
            Assert.True(summary.Searched);
            Assert.False(summary.CheckedRegisters);
            Assert.Equal([RunSummary.SearchSection, RunSummary.PaymentsSection], summary.Sections.Select(s => s.Kind));
            Assert.Equal([UiText.SummarySearch, UiText.SummaryPayments], summary.Sections.Select(s => s.Title));
            Assert.StartsWith("Näidis OÜ (10235688), kuu 10.2026", one.Header, StringComparison.Ordinal);
            Assert.Contains(summary.Total == 0 ? UiText.SummaryNone : $"Probleeme kokku: {summary.Total}", one.Header, StringComparison.Ordinal);
            Assert.True(summary.Total > 0, "the fixture's month has something to say");
            Assert.Equal(summary.Sections.Sum(s => s.Groups.Count), one.GroupRows);
            Assert.Equal(summary.Sections.SelectMany(s => s.Groups).Select(g => g.Count), one.PlaceRows); // picking a kind lists its places

            // one place once: no two places of a kind are the same
            Assert.All(summary.Sections.SelectMany(s => s.Groups), g =>
                Assert.Equal(g.Count, g.Occurrences.Select(o => (o.Who, o.Detail, o.PersonId, o.RowId, o.LineId, o.IsPart)).Distinct().Count()));

            // the payments' kinds are the check cell's: as many payments as the cells of the Kõik tab name the code
            form.ScopeTabs.SelectedIndex = 1;
            Ui.Pump();
            var cells = form.PaymentsGrid.Rows.Cast<DataGridViewRow>().Select(r => CodesIn(r.Cells["check"].Value)).ToList();
            var payments = summary.Sections.Single(s => s.Kind == RunSummary.PaymentsSection);
            Assert.NotEmpty(payments.Groups);
            Assert.All(payments.Groups, g => Assert.Equal(cells.Count(c => c.Contains(g.Code)), g.Count));
            // and the other way: every code a check cell names (its words aside) is a kind, with as many payments
            foreach (var code in cells.SelectMany(c => c).Where(IsCode).Distinct())
            {
                Assert.Contains(payments.Groups, g => g.Code == code && g.Count == cells.Count(c => c.Contains(code)));
            }

            // the search's kinds are the Probleemid column's: each person a kind names shows the code, and each code a person
            // shows is a kind that names them
            var search = summary.Sections.Single(s => s.Kind == RunSummary.SearchSection);
            var persons = form.PersonsGrid.Rows.Cast<DataGridViewRow>().Where(r => r.Tag is ReviewPerson { IsUnmatched: false }).ToList();
            foreach (var group in search.Groups)
            {
                foreach (var place in group.Occurrences.Where(o => o.PersonId is not null))
                {
                    var row = persons.Single(r => ((ReviewPerson)r.Tag!).PersonId == place.PersonId);
                    Assert.Contains(group.Code, CodesIn(row.Cells["problems"].Value));
                }
            }

            foreach (var row in persons)
            {
                var person = (ReviewPerson)row.Tag!;
                foreach (var code in CodesIn(row.Cells["problems"].Value))
                {
                    // a problem, or income undeclared not by choice, is the search's; a refused payment's reason is the
                    // check cell's, counted with the payments; undeclared by choice is no problem
                    static IEnumerable<string> Of(Undeclared u) => u.Codes.Count > 0 ? u.Codes : [u.Reason];
                    var own = person.Problems.Any(p => p.Code == code) || person.Undeclared.Any(u => !u.ByChoice && u.Reason != Undeclared.Refused && Of(u).Contains(code));
                    var refused = person.Undeclared.Any(u => u.Reason == Undeclared.Refused && Of(u).Contains(code));
                    if (own)
                    {
                        Assert.Contains(search.Groups, g => g.Code == code && g.Occurrences.Any(o => o.PersonId == person.PersonId));
                    }
                    else if (refused)
                    {
                        Assert.Contains(payments.Groups, g => g.Code == code && g.Occurrences.Any(o => o.PersonId == person.PersonId));
                    }
                    else
                    {
                        Assert.True(person.Undeclared.Any(u => u.ByChoice && Of(u).Contains(code)), $"{code} of {person.LastName} is in no list");
                    }
                }
            }

            Assert.Equal(summary, Kept(form)); // the company's latest, as shown
        }));
    }

    [Fact]
    public void A_place_said_in_two_lists_is_counted_once()
    {
        var company = new Core.Store.Company(Guid.NewGuid(), "10235688", "Näidis OÜ");
        var period = new Core.Store.Period(Guid.NewGuid(), company.Id, 2026, 10, Core.Store.PeriodState.Draft, null, null, null);
        var mari = Core.Store.Person.New(company.Id, "Mari", "Maasikas", SatellesTSD.Tests.ReviewFixtures.MariCode);
        var refused = new Core.Tax.Refusal(PaymentChecks.PensionNotChecked, "english", ["10"]);
        var salary = new ReviewPayment { PeriodId = period.Id, RowId = Guid.NewGuid(), Source = "LHV", RowNo = 2, Payee = "Mari Maasikas", VerdictText = "[Palgad]", Amount = 900m, Declare = true, Checks = [refused],
            Notices = [ReviewChecks.TypeTaxesDiffer, ReviewChecks.TypeTaxesDiffer] }; // a notice said twice is one place
        var unread = new ReviewPayment { PeriodId = period.Id, RowId = Guid.NewGuid(), Source = "LHV", RowNo = 3, Payee = "Mari Maasikas", VerdictText = "[Palgad]", Amount = 0m, Blockers = [new Core.Tax.Refusal(ReviewChecks.NotCounted, "english")] };
        // the run's problem of Mari is in her own list and in the run's; the source's in the rows nobody matched and in the run's
        var hers = new MatchProblem(MatchProblem.BadAmount, "english", "key-mari", "LHV", 3, Args: ["LHV [3]"]);
        var source = new MatchProblem(MatchProblem.HeaderRow, "english", null, "LHV", 7, Args: ["LHV", "7"]);
        var person = new ReviewPerson
        {
            Key = "key-mari", PersonId = mari.Id, Person = mari, SalaryBySource = new Dictionary<string, decimal>(), Payments = [salary, unread], Problems = [hers, hers],
            Undeclared = [new Undeclared(salary, Undeclared.Refused, [refused.Code], false), new Undeclared(unread, Undeclared.NotDeclared, [ReviewChecks.NotCounted], false)],
        };
        var nobody = new ReviewPerson { IsUnmatched = true, SalaryBySource = new Dictionary<string, decimal>(), Payments = [], Problems = [source] };
        var view = new ReviewView
        {
            Period = period, Company = company, Catalog = MatchCatalog.Default, Files = [], Sources = ["LHV"], Persons = [person, nobody], StorePersons = [mari], Problems = [hers, source],
        };

        var summary = RunSummaryBuilder.Build(view, DateTime.UtcNow, searched: true, checkedRegisters: false, completed: true, null, null);
        var search = summary.Sections.Single(s => s.Kind == RunSummary.SearchSection).Groups;
        var payments = summary.Sections.Single(s => s.Kind == RunSummary.PaymentsSection).Groups;
        Assert.Equal(1, Assert.Single(search, g => g.Code == MatchProblem.BadAmount).Count);
        var header = Assert.Single(search, g => g.Code == MatchProblem.HeaderRow);
        Assert.Equal("LHV [7]", Assert.Single(header.Occurrences).Who); // a problem of no person by its file and row
        Assert.Null(header.Occurrences[0].PersonId);
        var notCounted = Assert.Single(search, g => g.Code == ReviewChecks.NotCounted); // undeclared by no choice: the column's reason
        Assert.Equal(UiText.RefusalLine(unread.Blockers[0]), Assert.Single(notCounted.Occurrences).Detail);
        Assert.DoesNotContain(search, g => g.Code == PaymentChecks.PensionNotChecked); // the refusal is the check cell's, counted there once
        Assert.Equal(2, payments.Count);
        var pension = Assert.Single(payments, g => g.Code == PaymentChecks.PensionNotChecked);
        var place = Assert.Single(pension.Occurrences);
        Assert.Equal(("Maasikas Mari — LHV [2]", mari.Id, (decimal?)900m, salary.RowId), (place.Who, place.PersonId, place.Amount, place.RowId));
        var notice = Assert.Single(payments, g => g.Code == ReviewChecks.TypeTaxesDiffer); // the check cell's notices are counted with its refusals
        Assert.Equal(UiText.ShortTitle(ReviewChecks.TypeTaxesDiffer), notice.Title);
        var noticed = Assert.Single(notice.Occurrences);
        Assert.Equal((UiText.NoticeTip(salary, ReviewChecks.TypeTaxesDiffer), mari.Id, salary.RowId), (noticed.Detail, noticed.PersonId, noticed.RowId));
        Assert.Equal(5, summary.Total);
    }

    [Fact]
    public void The_registers_places_are_named_and_gone_to_as_the_persons_grid_has_them()
    {
        var company = new Core.Store.Company(Guid.NewGuid(), "10235688", "Näidis OÜ");
        var period = new Core.Store.Period(Guid.NewGuid(), company.Id, 2026, 10, Core.Store.PeriodState.Draft, null, null, null);
        var mari = Core.Store.Person.New(company.Id, "Mari", "Maasikas", SatellesTSD.Tests.ReviewFixtures.MariCode) with { Id = Guid.NewGuid() };
        var jaan = Core.Store.Person.New(company.Id, "Jaan", "Tamm", null) with { Id = Guid.NewGuid() }; // paid in the month, outside the register, no code
        var kati = Core.Store.Person.New(company.Id, "Kati", "Kask", null) with { Id = Guid.NewGuid() }; // a register row without a code, made a person by the review
        static ReviewPerson Shown(string? key, Core.Store.Person person) =>
            new() { Key = key, PersonId = person.Id, Person = person, SalaryBySource = new Dictionary<string, decimal>(), Payments = [], Problems = [] };

        // two register rows bound to Mari: the grid shows her once, with the first row's problems
        var second = new MatchProblem(MatchProblem.NoEmploymentInMonth, "english", "key-mari-2", Args: []);
        var run = new MatchResult(2026, 10,
            [new MatchPerson(new SearchPerson("key-mari", "Maasikas", "Mari", mari.PersonalCode), [], null, 0), new MatchPerson(new SearchPerson("key-mari-2", "Maasikas", "Mari", null), [], null, 1),
             new MatchPerson(new SearchPerson("key-kati", "Kask", "Kati", null), [], null, 2)],
            [], [], [second], "{}");
        var view = new ReviewView
        {
            Period = period, Company = company, Catalog = MatchCatalog.Default, Files = [], Sources = [], Run = run, StorePersons = [mari, jaan, kati], Problems = [second],
            Persons = [Shown("key-mari", mari), Shown("key-kati", kati), Shown(null, jaan)],
        };
        var failedNote = new LookupNote(LookupRunner.Failed, LookupNote.FactsStand, LookupKind.Residency, Failure: "HTTP 503");
        var asked = new PersonLookupOutcome(mari.Id, mari.PersonalCode!, mari.FullName, [], ["FAILED: residency"]) { ProblemNotes = [failedNote] };
        var report = new LookupRunReport(new DateOnly(2026, 10, 31), [asked], ["SKIPPED: paid", "SKIPPED: register", "NO_RUN"])
        {
            ProblemNotes =
            [
                new LookupNote(LookupRunner.Skipped, LookupNote.PaidNoCode, Name: jaan.FullName, PersonId: jaan.Id),
                new LookupNote(LookupRunner.Skipped, LookupNote.RegisterNoCode, Name: "Kask Kati", PersonKey: "key-kati"),
                new LookupNote(LookupNote.NoRun),
            ],
        };

        var summary = RunSummaryBuilder.Build(view, DateTime.UtcNow, searched: true, checkedRegisters: true, completed: true, report, null);
        var search = summary.Sections.Single(s => s.Kind == RunSummary.SearchSection).Groups;
        Assert.DoesNotContain(search, g => g.Code == MatchProblem.NoEmploymentInMonth); // the second row's: no person's Probleemid shows it
        var registers = summary.Sections.Single(s => s.Kind == RunSummary.RegistersSection).Groups;
        var failed = Assert.Single(Assert.Single(registers, g => g.Code == LookupRunner.Failed).Occurrences);
        Assert.Equal(("Maasikas Mari", (Guid?)mari.Id), (failed.Who, failed.PersonId)); // „Perekonnanimi Eesnimi“, as the grid
        var skipped = Assert.Single(registers, g => g.Code == LookupRunner.Skipped).Occurrences;
        Assert.Equal([("Tamm Jaan", (Guid?)jaan.Id), ("Kask Kati", kati.Id)], skipped.Select(o => (o.Who, o.PersonId))); // gone to by a double-click
        var whole = Assert.Single(Assert.Single(registers, g => g.Code == LookupNote.NoRun).Occurrences);
        Assert.Equal((UiText.SummaryWholeCheck, (Guid?)null), (whole.Who, whole.PersonId));
    }

    [Fact]
    public void A_register_check_is_summarised_with_the_payments_counted_afresh_and_its_own_problems()
    {
        using var fixture = new WindowFixture { Services = path => new AppServices(path, new FailingResidency(), new NoPension(), new LookupOptions(TimeSpan.Zero)) };
        var shown = new List<Shown>();
        Ui.Run(() => fixture.Drive(form =>
        {
            Watch(form, shown);
            form.LookupButton.PerformClick();
            Idle(form, "the check");
            var summary = Assert.Single(shown).Summary;
            Assert.False(summary.Searched);
            Assert.True(summary.CheckedRegisters && summary.CheckCompleted);
            Assert.Equal([RunSummary.PaymentsSection, RunSummary.RegistersSection], summary.Sections.Select(s => s.Kind));
            var failed = Assert.Single(summary.Sections.Single(s => s.Kind == RunSummary.RegistersSection).Groups, g => g.Code == LookupRunner.Failed);
            Assert.Equal(UiText.ShortTitle(LookupRunner.Failed), failed.Title);
            Assert.Equal(form.View!.Persons.Count(p => p.Person?.PersonalCode is not null && !p.IsUnmatched), failed.Count); // every person asked, once
            Assert.All(failed.Occurrences, o => Assert.StartsWith($"{LookupRunner.Failed}: residentsuse päring ebaõnnestus", o.Detail, StringComparison.Ordinal));
            Assert.DoesNotContain(UiText.SummaryCheckNotCompleted, Assert.Single(shown).Header, StringComparison.Ordinal);
            Assert.Equal(summary, Kept(form));
        }));
    }

    [Fact]
    public void The_search_and_the_check_from_the_offer_show_one_summary_of_all_three_after_the_check()
    {
        var residency = new FailingResidency();
        using var fixture = new WindowFixture { Services = path => new AppServices(path, residency, new NoPension(), new LookupOptions(TimeSpan.Zero)) };
        var shown = new List<Shown>();
        Ui.Run(() => fixture.Drive(form =>
        {
            var checkedFirst = false;
            Watch(form, shown, _ => checkedFirst = residency.Asked > 0);
            form.Choosing = (_, _, choices) => choices.ToList().IndexOf(UiText.ImportOfferSearchAndCheck);
            var work = form.ImportFilesAsync([Statement(fixture)]);
            Ui.Until(() => work.IsCompleted && !form.IsBusy, what: "the import, the search and the check");
            work.GetAwaiter().GetResult();
            Ui.Pump();
            var summary = Assert.Single(shown).Summary; // one, not one after the search and another after the check
            Assert.True(checkedFirst); // shown once the check was over
            Assert.True(summary.Searched && summary.CheckedRegisters && summary.CheckCompleted);
            Assert.Equal([RunSummary.SearchSection, RunSummary.PaymentsSection, RunSummary.RegistersSection], summary.Sections.Select(s => s.Kind));
            Assert.Contains($"„{UiText.RunMatch}“ ja „{UiText.RunLookups}“", Assert.Single(shown).Header, StringComparison.Ordinal);
        }));
    }

    [Fact]
    public void A_check_stopped_midway_is_summarised_by_the_persons_it_got_to_and_says_it_was_not_completed()
    {
        var residency = new FirstFailsThenWaits();
        using var fixture = new WindowFixture { Services = path => new AppServices(path, residency, new NoPension(), new LookupOptions(TimeSpan.Zero)) };
        var shown = new List<Shown>();
        Ui.Run(() => fixture.Drive(form =>
        {
            Watch(form, shown);
            form.LookupButton.PerformClick();
            Ui.Until(() => residency.Waiting && form.ProgressLabel.Text == UiText.LookupProgress(1, 2), what: "the second query, the first one's answer shown");
            form.StopButton.PerformClick();
            Idle(form, "the stop");
            var one = Assert.Single(shown);
            Assert.True(one.Summary.CheckedRegisters);
            Assert.False(one.Summary.CheckCompleted);
            Assert.Contains(UiText.SummaryCheckNotCompleted, one.Header, StringComparison.Ordinal);
            var failed = Assert.Single(one.Summary.Sections.Single(s => s.Kind == RunSummary.RegistersSection).Groups);
            Assert.Equal(LookupRunner.Failed, failed.Code);
            Assert.Single(failed.Occurrences); // the first person's answer; the second was never given
            Assert.Equal(one.Summary, Kept(form));
        }));
    }

    [Fact]
    public void A_check_stopped_midway_still_says_its_own_problems_known_before_the_first_query_in_the_summary_and_the_log()
    {
        var residency = new FirstFailsThenWaits();
        using var fixture = new WindowFixture(prepare: (store, _, period, _) => store.ClearMatchRun(period.Id)) // no search: NO_RUN
            { Services = path => new AppServices(path, residency, new NoPension(), new LookupOptions(TimeSpan.Zero)) };
        var shown = new List<Shown>();
        Ui.Run(() => fixture.Drive(form =>
        {
            Watch(form, shown);
            form.LookupButton.PerformClick();
            Ui.Until(() => residency.Waiting && form.ProgressLabel.Text.StartsWith(UiText.LookupProgress(1, 0)[..^1], StringComparison.Ordinal), what: "the second query, the first one's answer shown");
            form.StopButton.PerformClick();
            Idle(form, "the stop");
            var summary = Assert.Single(shown).Summary;
            Assert.False(summary.CheckCompleted);
            var registers = summary.Sections.Single(s => s.Kind == RunSummary.RegistersSection).Groups;
            var noRun = Assert.Single(Assert.Single(registers, g => g.Code == LookupNote.NoRun).Occurrences);
            Assert.Equal(UiText.SummaryWholeCheck, noRun.Who);
            Assert.Single(Assert.Single(registers, g => g.Code == LookupRunner.Failed).Occurrences); // and the person it got to
            Assert.Equal(1, form.LogList.Items.Cast<object>().Count(i => i.ToString()!.Contains(noRun.Detail, StringComparison.Ordinal)));
        }));
    }

    [Fact]
    public void A_summary_that_cannot_be_kept_is_shown_all_the_same_and_says_it_was_not_kept()
    {
        using var fixture = Unchecked();
        var shown = new List<Shown>();
        Ui.Run(() => fixture.Drive(form =>
        {
            // the store refuses the write (as a locked file or a network drive gone for a moment would)
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={fixture.StorePath};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TRIGGER refuse_summary BEFORE INSERT ON run_summary BEGIN SELECT RAISE(ABORT, 'locked'); END;";
                command.ExecuteNonQuery();
            }

            Watch(form, shown);
            form.MatchButton.PerformClick();
            Idle(form, "the search");
            var one = Assert.Single(shown); // shown, though not kept
            Assert.True(one.Summary.Total > 0);
            Assert.Contains(UiText.SummaryNotKept, one.Header, StringComparison.Ordinal);
            Assert.Null(form.Services!.Store.RunSummary(form.View!.Company.Id));
            Assert.Equal(1, form.LogList.Items.Cast<object>().Count(i => i.ToString()!.Contains(UiText.SummaryNotKept, StringComparison.Ordinal)));
        }));
        Assert.Empty(fixture.Failures); // said in the summary and the log, no error box on top of it
    }

    [Fact]
    public void Every_code_that_can_reach_the_summary_has_a_short_title_and_an_unknown_one_shows_its_code_alone()
    {
        static IEnumerable<string> Codes(Type type) => type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()!)
            .Where(c => c.Length > 0 && c.All(ch => char.IsUpper(ch) || ch == '_'));

        // the search's and the review's problems, the payments' refusals and notices, the register check's problems and the
        // run's own, the reasons income goes undeclared
        var codes = Codes(typeof(MatchProblem)).Concat(Codes(typeof(ReviewChecks))).Concat(Codes(typeof(PaymentChecks))).Concat(Codes(typeof(LookupRunner)))
            .Append(LookupNote.NoRun).Concat(Codes(typeof(Undeclared))).Distinct().ToList();
        Assert.True(codes.Count >= 23 + 15 + 15 + 6, $"only {codes.Count} codes found");
        Assert.All(codes, code =>
        {
            var title = UiText.ShortTitle(code);
            Assert.False(string.IsNullOrWhiteSpace(title), code);
            Assert.True(title!.Length <= 45, $"{code}: „{title}“ is no short title");
            Assert.DoesNotContain(code, title, StringComparison.Ordinal);
        });
        Assert.Null(UiText.ShortTitle("SOMETHING_NEW"));

        var unknown = new RunSummary(Guid.NewGuid(), "Näidis OÜ", "10235688", Guid.NewGuid(), 2026, 10, DateTime.UtcNow, true, false, true,
            [new SummarySection(RunSummary.SearchSection, UiText.SummarySearch, [new SummaryGroup("SOMETHING_NEW", UiText.ShortTitle("SOMETHING_NEW"), [new SummaryOccurrence("LHV [2]", "SOMETHING_NEW: english")])])]);
        Ui.Run(() =>
        {
            using var dialog = new SummaryDialog(unknown);
            var row = dialog.Groups.Rows[0];
            Assert.Equal(("SOMETHING_NEW", "", 1), ((string)row.Cells[1].Value!, (string)row.Cells[2].Value!, (int)row.Cells[3].Value!));

            // nothing found: the window still opens, and says so
            using var none = new SummaryDialog(unknown with { Sections = [new SummarySection(RunSummary.SearchSection, UiText.SummarySearch, [])] });
            Assert.Contains(UiText.SummaryNone, none.HeaderText, StringComparison.Ordinal);
            Assert.Equal(0, none.Groups.Rows.Count);
        });
    }

    [Fact]
    public void The_latest_summary_opens_again_after_a_restart_whatever_month_is_shown_and_the_menu_says_when_there_is_none()
    {
        using var fixture = Unchecked();
        var shown = new List<Shown>();
        var told = new List<string>();
        Ui.Run(() => fixture.Drive(form =>
        {
            form.Informing = told.Add;
            Assert.True(form.SummaryButton.Enabled);
            form.SummaryButton.PerformClick();
            Assert.Equal([UiText.NoSummaryYet], told); // none made yet
            Watch(form, shown);
            form.MatchButton.PerformClick();
            Idle(form, "the search");
        }));

        // the window closed and opened again: the summary comes from the store as it was shown
        var first = Assert.Single(shown).Summary;
        Ui.Run(() => fixture.Drive(form =>
        {
            Watch(form, shown);
            form.SummaryButton.PerformClick();
            Assert.Equal(2, shown.Count);
            Assert.Equal(first, shown[1].Summary);

            // another month shown: the company's latest still, and its header names its own month
            ShowMonth(form, 11);
            Assert.Equal(11, ShownMonth(form));
            form.SummaryButton.PerformClick();
            Assert.Equal(3, shown.Count);
            Assert.Equal(first, shown[2].Summary);
            Assert.Contains("kuu 10.2026", shown[2].Header, StringComparison.Ordinal);
        }));

        // no company shown: the item is off
        using var empty = new WindowFixture(empty: true);
        Ui.Run(() => empty.Drive(form => Assert.False(form.SummaryButton.Enabled)));
    }

    [Fact]
    public void The_summary_opens_next_time_with_the_size_and_splitter_it_was_closed_with()
    {
        using var fixture = Unchecked();
        Ui.Run(() => fixture.Drive(form =>
        {
            form.SummaryOpening = dialog =>
            {
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(-4000, -4000); // no screen shows it
                dialog.Show();
                Ui.Pump();
                dialog.Size = new Size(900, 540);
                Ui.Pump();
                DialogLayout.Split(dialog, "summarySplit").SplitterDistance = 210;
                dialog.Close();
                Ui.Pump();
            };
            form.MatchButton.PerformClick();
            Idle(form, "the search");

            var reopened = false;
            form.SummaryOpening = dialog =>
            {
                Assert.Equal(new Size(900, 540), dialog.Size);
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(-4000, -4000);
                dialog.Show();
                Ui.Pump();
                Assert.Equal(210, DialogLayout.Split(dialog, "summarySplit").SplitterDistance);
                dialog.Close();
                reopened = true;
            };
            form.SummaryButton.PerformClick();
            Ui.Pump();
            Assert.True(reopened);
        }));
        Assert.True(AppSettings.Load(fixture.SettingsPath).Dialogs!.ContainsKey("summary"));
    }

    [Fact]
    public void A_double_click_on_a_place_goes_to_its_person_and_payment_switching_to_the_summarys_month()
    {
        using var fixture = Unchecked();
        Ui.Run(() => fixture.Drive(form =>
        {
            var told = new List<string>();
            form.Informing = told.Add;
            form.SummaryOpening = _ => { };
            form.MatchButton.PerformClick();
            Idle(form, "the search");
            var summary = Kept(form);
            var payment = summary.Sections.SelectMany(s => s.Groups).SelectMany(g => g.Occurrences).First(o => o.PersonId is not null && o.IsPayment);

            // another person selected, and another month shown: the double-click comes back to both
            var other = form.PersonsGrid.Rows.Cast<DataGridViewRow>().First(r => r.Tag is ReviewPerson p && p.PersonId != payment.PersonId);
            form.PersonsGrid.CurrentCell = other.Cells.Cast<DataGridViewCell>().First(c => c.Visible);
            ShowMonth(form, 11);
            Assert.Equal(11, ShownMonth(form));

            form.SummaryOpening = dialog =>
            {
                var (group, place) = dialog.Summary.Sections.SelectMany(s => s.Groups).Select((g, i) => (i, g.Occurrences.ToList().IndexOf(payment))).First(x => x.Item2 >= 0);
                dialog.SelectGroup(group);
                dialog.Pick(place);
                Assert.Equal(payment, dialog.Picked);
            };
            form.SummaryButton.PerformClick();
            Ui.Pump();
            Assert.Equal(10, ShownMonth(form));
            Assert.Equal(payment.PersonId, (form.PersonsGrid.CurrentRow!.Tag as ReviewPerson)!.PersonId);
            var selected = (ReviewPayment)form.PaymentsGrid.CurrentRow!.Tag!;
            Assert.Equal(payment.RowId, selected.RowId);
            Assert.Equal(payment.IsPart, selected.IsPart);

            Assert.Empty(told); // nothing to say: the month was there, and so was the person
        }));
    }

    [Fact]
    public void A_double_click_in_the_month_shown_brings_the_review_to_the_front_and_clears_what_hides_the_place()
    {
        using var fixture = Unchecked();
        Ui.Run(() => fixture.Drive(form =>
        {
            var told = new List<string>();
            form.Informing = told.Add;
            // a second payment to Mari: the place gone to is not the one her payments open on
            var work = form.ImportFilesAsync([SecondToMari(fixture)]);
            Ui.Until(() => work.IsCompleted && !form.IsBusy, what: "the import");
            work.GetAwaiter().GetResult();
            form.MatchButton.PerformClick();
            Idle(form, "the search");
            var summary = Kept(form);
            var mari = form.PersonsGrid.Rows.Cast<DataGridViewRow>().Single(r => r.Tag is ReviewPerson { IsUnmatched: false } p && p.Person?.PersonalCode == SatellesTSD.Tests.ReviewFixtures.MariCode);
            form.PersonsGrid.CurrentCell = mari.Cells.Cast<DataGridViewCell>().First(c => c.Visible);
            form.ScopeTabs.SelectedIndex = 0;
            Ui.Pump();
            var last = (ReviewPayment)form.PaymentsGrid.Rows.Cast<DataGridViewRow>().Last(r => r.Visible).Tag!;
            Assert.NotSame(last, form.PaymentsGrid.Rows.Cast<DataGridViewRow>().First(r => r.Visible).Tag);
            var payment = summary.Sections.SelectMany(s => s.Groups).SelectMany(g => g.Occurrences)
                .First(o => o.PersonId == ((ReviewPerson)mari.Tag!).PersonId && o.RowId == last.RowId && o.LineId == last.LineId && o.IsPart == last.IsPart);

            // the declaration's tab in front, another person selected, the person hidden by the persons' search and the
            // payment by the payments' search under the Kõik tab
            var other = form.PersonsGrid.Rows.Cast<DataGridViewRow>().First(r => r.Tag is ReviewPerson p && p.PersonId != payment.PersonId);
            form.PersonsGrid.CurrentCell = other.Cells.Cast<DataGridViewCell>().First(c => c.Visible);
            form.ScopeTabs.SelectedIndex = 1;
            form.PaymentsSearch.Text = "ei leidu kusagil";
            form.PersonsSearch.Text = ((ReviewPerson)other.Tag!).LastName;
            form.Tabs.SelectedIndex = 1;
            Ui.Pump();
            Assert.DoesNotContain(form.PersonsGrid.Rows.Cast<DataGridViewRow>(), r => r.Visible && r.Tag is ReviewPerson p && p.PersonId == payment.PersonId);

            form.SummaryOpening = dialog =>
            {
                var (group, place) = dialog.Summary.Sections.SelectMany(s => s.Groups).Select((g, i) => (i, g.Occurrences.ToList().IndexOf(payment))).First(x => x.Item2 >= 0);
                dialog.SelectGroup(group);
                dialog.Pick(place);
            };
            form.SummaryButton.PerformClick();
            Ui.Pump();
            Assert.Equal(10, ShownMonth(form)); // no switch: the month was the one shown
            Assert.Equal(0, form.Tabs.SelectedIndex); // the review's grids in front
            Assert.Equal(("", "", 0), (form.PersonsSearch.Text, form.PaymentsSearch.Text, form.ScopeTabs.SelectedIndex));
            Assert.Equal(payment.PersonId, (form.PersonsGrid.CurrentRow!.Tag as ReviewPerson)!.PersonId);
            var selected = (ReviewPayment)form.PaymentsGrid.CurrentRow!.Tag!;
            Assert.Equal((payment.RowId, payment.LineId, payment.IsPart), (selected.RowId, selected.LineId, selected.IsPart));
            Assert.Empty(told);
        }));
    }

    [Fact]
    public void A_double_click_goes_nowhere_from_a_place_of_no_person_and_says_why_for_a_person_gone_a_month_missing_or_another_company()
    {
        using var fixture = Unchecked();
        Ui.Run(() => fixture.Drive(form =>
        {
            var told = new List<string>();
            form.Informing = told.Add;

            // a header row inside a file's data: a problem of no person, a real one
            var work = form.ImportFilesAsync([WithHeaderInside(fixture)]);
            Ui.Until(() => work.IsCompleted && !form.IsBusy, what: "the import");
            work.GetAwaiter().GetResult();
            form.MatchButton.PerformClick();
            Idle(form, "the search");
            var tried = false;
            form.SummaryOpening = dialog =>
            {
                var groups = dialog.Summary.Sections.SelectMany(s => s.Groups).ToList();
                var index = groups.FindIndex(g => g.Occurrences.Any(o => o.PersonId is null));
                Assert.True(index >= 0, "the search left a place of no person");
                dialog.SelectGroup(index);
                dialog.Pick(groups[index].Occurrences.ToList().FindIndex(o => o.PersonId is null));
                Assert.Null(dialog.Picked); // no way to go: the summary stays open
                tried = true;
            };
            form.SummaryButton.PerformClick();
            Assert.True(tried);

            // summaries kept as they were shown, of a person no longer in the month, a month the window cannot pick, another company
            var basis = Kept(form);
            var someone = basis.Sections.SelectMany(s => s.Groups).SelectMany(g => g.Occurrences).First(o => o.PersonId is not null);
            void Open(SummaryOccurrence place, Guid? company = null, int? year = null)
            {
                var summary = basis with
                {
                    CompanyId = company ?? basis.CompanyId, Year = year ?? basis.Year,
                    Sections = [new SummarySection(RunSummary.SearchSection, UiText.SummarySearch, [new SummaryGroup(MatchProblem.NoName, null, [place])])],
                };
                form.Services!.Store.SaveRunSummary(new Core.Store.StoredRunSummary(form.View!.Company.Id, form.View.Period.Id, summary.MadeAt, summary.ToJson()));
                form.SummaryOpening = dialog =>
                {
                    dialog.SelectGroup(0);
                    dialog.Pick(0);
                };
                form.SummaryButton.PerformClick();
                Ui.Pump();
            }

            Assert.Empty(told);
            Open(someone with { PersonId = Guid.NewGuid() });
            Assert.Equal([UiText.SummaryPersonGone], told);
            told.Clear();
            Open(someone, year: 1999);
            Assert.Equal([UiText.SummaryMonthMissing(1999, basis.Month)], told);
            told.Clear();
            Open(someone, company: Guid.NewGuid());
            Assert.Equal([UiText.SummaryOtherCompany], told);
            Assert.Equal(10, ShownMonth(form)); // nothing switched
        }));
    }

    [Fact]
    public void A_click_on_a_kind_lists_its_places_and_a_double_click_or_enter_on_a_persons_place_hands_it_back()
    {
        var mari = new SummaryOccurrence("Maasikas Mari", "PENSION_NOT_CHECKED: …", Guid.NewGuid());
        var jaan = new SummaryOccurrence("Tamm Jaan", "PENSION_NOT_CHECKED: …", Guid.NewGuid());
        var file = new SummaryOccurrence("LHV [7]", "HEADER_ROW: …");
        var summary = new RunSummary(Guid.NewGuid(), "Näidis OÜ", "10235688", Guid.NewGuid(), 2026, 10, DateTime.UtcNow, true, false, true,
        [
            new SummarySection(RunSummary.SearchSection, UiText.SummarySearch, [new SummaryGroup(MatchProblem.HeaderRow, null, [file])]),
            new SummarySection(RunSummary.PaymentsSection, UiText.SummaryPayments, [new SummaryGroup(PaymentChecks.PensionNotChecked, null, [mari, jaan])]),
        ]);
        var onDoubleClick = typeof(DataGridView).GetMethod("OnCellDoubleClick", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var onKeyDown = typeof(DataGridView).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!;
        SummaryDialog Open()
        {
            var dialog = new SummaryDialog(summary) { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000) };
            dialog.Show();
            Ui.Pump();
            return dialog;
        }

        void Click(SummaryDialog dialog, int group)
        {
            dialog.Groups.CurrentCell = dialog.Groups.Rows[group].Cells[0]; // as a click does: the grid's own selection, nothing else
            Ui.Pump();
        }

        Ui.Run(() =>
        {
            using (var dialog = Open())
            {
                Click(dialog, 1);
                Assert.Equal(2, dialog.Places.Rows.Count);
                Click(dialog, 0);
                Assert.Equal(1, dialog.Places.Rows.Count);

                // a place of no person: a double-click goes nowhere, and the summary stays open
                onDoubleClick.Invoke(dialog.Places, [new DataGridViewCellEventArgs(0, 0)]);
                Ui.Pump();
                Assert.Null(dialog.Picked);
                Assert.True(dialog.Visible);

                // Enter on a person's place hands it back and closes
                Click(dialog, 1);
                dialog.Places.CurrentCell = dialog.Places.Rows[1].Cells[0];
                onKeyDown.Invoke(dialog.Places, [new KeyEventArgs(Keys.Enter)]);
                Ui.Pump();
                Assert.Equal(jaan, dialog.Picked);
                Assert.False(dialog.Visible);
            }

            using (var dialog = Open())
            {
                Click(dialog, 1);
                onDoubleClick.Invoke(dialog.Places, [new DataGridViewCellEventArgs(0, 0)]);
                Ui.Pump();
                Assert.Equal(mari, dialog.Picked);
                Assert.False(dialog.Visible);
            }
        });
    }

    private static void ShowMonth(MainForm form, int month)
    {
        form.MonthBox.SelectedIndex = month - 1;
        Ui.Pump();
    }

    private static int ShownMonth(MainForm form) => int.Parse((string)form.MonthBox.SelectedItem!, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A bank statement of made-up rows that names no employer: it goes to the company shown, something new.</summary>
    private static string Statement(WindowFixture fixture)
    {
        var path = Path.Combine(fixture.Folder, "lhv.xlsx");
        File.WriteAllBytes(path, SatellesTSD.Tests.Tables.Xlsx("LHV", [Core.Import.LayoutCatalog.Default.Find("lhv-et-short")!.Headers.ToArray<object?>(),
            ["EE000000000000000001", 1, new DateTime(2026, 10, 7), "EE12", "Kask Kati", "D", -100.0, null, "2026100500000009", "palk", "EUR", null]]));
        return path;
    }

    /// <summary>A bank statement of one made-up row: a second salary to Mari, refused as her first is (her II pillar unchecked).</summary>
    private static string SecondToMari(WindowFixture fixture)
    {
        var path = Path.Combine(fixture.Folder, "lhv-mari.xlsx");
        File.WriteAllBytes(path, SatellesTSD.Tests.Tables.Xlsx("LHV", [Core.Import.LayoutCatalog.Default.Find("lhv-et-short")!.Headers.ToArray<object?>(),
            ["EE000000000000000002", 1, new DateTime(2026, 10, 20), "EE12", "Mari Maasikas", "D", -200.0, null, "2026102000000039", "palk", "EUR", double.Parse(SatellesTSD.Tests.ReviewFixtures.MariCode, System.Globalization.CultureInfo.InvariantCulture)]]));
        return path;
    }

    /// <summary>A bank statement of made-up rows with its header row again among the data: a problem of no person.</summary>
    private static string WithHeaderInside(WindowFixture fixture)
    {
        var path = Path.Combine(fixture.Folder, "lhv-kaks.xlsx");
        var headers = Core.Import.LayoutCatalog.Default.Find("lhv-et-short")!.Headers.ToArray<object?>();
        File.WriteAllBytes(path, SatellesTSD.Tests.Tables.Xlsx("LHV", [headers,
            ["EE000000000000000001", 1, new DateTime(2026, 10, 7), "EE12", "Kask Kati", "D", -100.0, null, "2026100500000019", "palk", "EUR", null],
            headers,
            ["EE000000000000000001", 2, new DateTime(2026, 10, 8), "EE12", "Kask Kati", "D", -50.0, null, "2026100500000029", "palk", "EUR", null]]));
        return path;
    }

    /// <summary>EMTA faked: every query fails, as a site that does not answer.</summary>
    private sealed class FailingResidency : IResidencyLookup
    {
        private int _asked;

        public int Asked => _asked;

        public Task<LookupResult<ResidencyFinding>> QueryAsync(string personalCode, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _asked);
            return Task.FromResult(LookupResult<ResidencyFinding>.Failed("request timeout or access denied: HTTP 503"));
        }
    }

    /// <summary>EMTA faked: the first query fails at once, every later one waits until it is stopped.</summary>
    private sealed class FirstFailsThenWaits : IResidencyLookup
    {
        private int _asked;
        private volatile bool _waiting;

        public bool Waiting => _waiting;

        public async Task<LookupResult<ResidencyFinding>> QueryAsync(string personalCode, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _asked) == 1)
            {
                return LookupResult<ResidencyFinding>.Failed("request timeout or access denied: HTTP 503");
            }

            _waiting = true;
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private sealed class NoPension : IPensionLookup
    {
        public Task<LookupResult<PensionFinding>> QueryAsync(string personalCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(LookupResult<PensionFinding>.Found(new PensionFinding(PensionStatus.NotJoined, null, null, []), null));
    }
}
