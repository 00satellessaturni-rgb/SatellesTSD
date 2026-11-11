using System.Text;
using SatellesTSD.Core.Declare;
using SatellesTSD.Core.Feedback;
using SatellesTSD.Core.Import;
using SatellesTSD.Core.Lookups;
using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Report;
using SatellesTSD.Core.Review;
using SatellesTSD.Core.Store;
using SatellesTSD.Core.Tax;
using SatellesTSD.Core.Xbrl;
using SatellesTSD.Infrastructure;

namespace SatellesTSD.Tests;

/// <summary>The declaration side: the lines built from the review and reconciled with the store (new, replaced, M, T,
/// kept, deleted, refused), the month's share and the zero line, the file exported and validated, the period's
/// states, the feedback read, a line settled by hand, a correction after a confirmation.</summary>
public class DeclareTests
{
    private static readonly DateOnly Today = new(2026, 11, 4);
    private static readonly string SchemaRoot = Path.Combine(AppContext.BaseDirectory, "schemas", "apa-tsd");

    private sealed class FixedClock(DateTime at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(at, DateTimeKind.Utc), TimeSpan.Zero);

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class Box : IDisposable
    {
        public required SqliteStore Store { get; init; }
        public required Company Company { get; init; }
        public required Period Period { get; init; }
        public required ReviewWorkspace Workspace { get; init; }
        public required PeriodDeclarer Declarer { get; init; }
        public required PeriodSubmitter Submitter { get; init; }
        public required string Folder { get; init; }

        public Person Mari => Store.FindPerson(Company.Id, ReviewFixtures.MariCode)!;

        public ReviewPayment Salary => Workspace.Load(Period.Id).Persons[0].Payments.Single(p => !p.IsByHand);

        public IReadOnlyList<StoredPayment> Lines => Store.Payments(Period.Id);

        public void Dispose()
        {
            Store.Dispose();
            if (Directory.Exists(Folder))
            {
                Directory.Delete(Folder, true);
            }
        }
    }

    /// <summary>A run reviewed and ready: Mari a checked resident, not a II-pillar member, with the exemption applied and,
    /// unless a test wants it, the social-tax minimum switched off (so a month without her salary makes no zero line).</summary>
    private static Box Ready(bool twin = false, bool minimum = false, decimal salary = 905.92m, int mariType = 1, bool mariEnded = false, bool mariPaused = false, bool mariRestarted = false, bool mariSecond = false)
    {
        var (store, company, period, _) = ReviewFixtures.Setup(twin: twin, salary: salary, mariType: mariType, mariEnded: mariEnded, mariPaused: mariPaused, mariRestarted: mariRestarted, mariSecond: mariSecond);
        new MatchRunner(store).Run(period.Id);
        var clock = new FixedClock(new DateTime(2026, 11, 4, 10, 0, 0));
        var workspace = new ReviewWorkspace(store, TaxRates.Year2026, clock);
        var mari = workspace.Load(period.Id).Persons[0].Person!;
        workspace.SavePerson(mari with { FundedPensionMember = false, BasicExemptionApplied = true, SocialTaxMinimumApplies = minimum }, confirmResidency: true);
        return new Box
        {
            Store = store, Company = company, Period = period, Workspace = workspace,
            Declarer = new PeriodDeclarer(store, TaxRates.Year2026, clock),
            Submitter = new PeriodSubmitter(store, SchemaRoot, clock),
            Folder = Path.Combine(Path.GetTempPath(), $"satellestsd-export-{Guid.NewGuid():N}"),
        };
    }

    [Fact]
    public void The_month_share_follows_the_employments_days_in_the_month()
    {
        TorEntry Entry(DateOnly? start, DateOnly? end) => new(1, "1", ReviewFixtures.MariCode, "MARI", "MAASIKAS", "EE", start, end, "1", "Tööleping", "2", "Kehtiv", null, null, null, null, [], null);
        Assert.Equal(1m, PeriodDeclarer.MonthShare(null, 2026, 10));
        Assert.Equal(1m, PeriodDeclarer.MonthShare(Entry(new DateOnly(2024, 1, 1), null), 2026, 10));
        Assert.Equal(1m, PeriodDeclarer.MonthShare(Entry(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)), 2026, 10));
        Assert.Equal(0.5m, PeriodDeclarer.MonthShare(Entry(new DateOnly(2026, 10, 17), null), 2026, 10)); // 15 days / 30
        Assert.Equal(10m / 30m, PeriodDeclarer.MonthShare(Entry(null, new DateOnly(2026, 10, 10)), 2026, 10)); // exact, so the due rounds to the cent once
        Assert.Equal(1m, PeriodDeclarer.MonthShare(Entry(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 30)), 2026, 10)); // 30 days = the whole
        Assert.Equal(1m, PeriodDeclarer.MonthShare(Entry(new DateOnly(2026, 11, 1), null), 2026, 10)); // no overlap: not this build's business
    }

    [Fact]
    public void The_month_share_counts_every_day_any_of_the_persons_minimum_carrying_rows_covers()
    {
        TorEntry Entry(DateOnly? start, DateOnly? end, string state = "2", string type = "1") =>
            new(1, "1", ReviewFixtures.MariCode, "MARI", "MAASIKAS", "EE", start, end, type, "x", state, "x", null, null, null, null, [], null);
        var next = Entry(new DateOnly(2026, 10, 16), null);

        // a contract ended on the 15th and the next begun on the 16th: the whole month, not the new contract's 16 days
        Assert.Equal(16m / 30m, PeriodDeclarer.MonthShare(next, 2026, 10));
        Assert.Equal(1m, PeriodDeclarer.MonthShare(next, [Entry(new DateOnly(2024, 1, 1), new DateOnly(2026, 10, 15), "4"), next], 2026, 10));

        // a gap between them: 1–10 and 16–31 make 26 days
        Assert.Equal(26m / 30m, PeriodDeclarer.MonthShare(next, [Entry(new DateOnly(2024, 1, 1), new DateOnly(2026, 10, 10), "4")], 2026, 10));

        // a day two rows cover counts once; a row of another month adds nothing
        Assert.Equal(16m / 30m, PeriodDeclarer.MonthShare(next, [Entry(new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 25)), Entry(new DateOnly(2025, 1, 1), new DateOnly(2026, 9, 30), "4")], 2026, 10));

        // a paused row that does not say since when, or one whose type carries no minimum (600, VÕS), adds no day
        Assert.Equal(16m / 30m, PeriodDeclarer.MonthShare(next, [Entry(new DateOnly(2024, 1, 1), new DateOnly(2026, 10, 15), "3")], 2026, 10));
        Assert.Equal(16m / 30m, PeriodDeclarer.MonthShare(next, [Entry(new DateOnly(2024, 1, 1), new DateOnly(2026, 10, 15), "4", "600")], 2026, 10));

        // a paused row counts its days before the pause (1–9 here) and after its planned end, never the pause itself
        var paused = Entry(new DateOnly(2024, 1, 1), null, "3") with { PauseStart = new DateOnly(2026, 10, 10) };
        Assert.Equal(25m / 30m, PeriodDeclarer.MonthShare(next, [paused], 2026, 10));
        Assert.Equal(25m / 30m, PeriodDeclarer.MonthShare(next, [paused with { PauseEnd = new DateOnly(2026, 10, 20) }], 2026, 10)); // 21–31 already the new contract's
        Assert.Equal(22m / 30m, PeriodDeclarer.MonthShare(Entry(new DateOnly(2026, 10, 22), null), [paused with { PauseEnd = new DateOnly(2026, 10, 18) }], 2026, 10)); // 1–9 and 19–31: 22 days

        // a row that carries no minimum stands for the month: the share is measured by the Tööleping that carries it (decision 89)
        Assert.Equal(0.5m, PeriodDeclarer.MonthShare(Entry(new DateOnly(2026, 10, 10), null, "2", "600"), [Entry(new DateOnly(2024, 1, 1), new DateOnly(2026, 10, 15), "4")], 2026, 10)); // a VÕS row (no minimum) stands for the month; the minimum is the ended Tööleping's 15 days (decision 89)
    }

    [Fact]
    public void A_build_over_a_contract_ended_mid_month_and_the_next_begun_owes_the_whole_months_minimum()
    {
        using var box = Ready(minimum: true, mariRestarted: true);
        var report = box.Declarer.Build(box.Period.Id);
        var mari = Assert.Single(report.Persons, p => p.PersonId == box.Mari.Id);
        Assert.Equal(1m, mari.MinimumShare); // the new contract alone would be 16/30
    }

    [Fact]
    public void The_month_share_is_measured_by_the_rows_that_carry_the_minimum_and_a_paused_entry_counts_no_pause()
    {
        TorEntry Entry(DateOnly? start, DateOnly? end, string state = "2", string type = "1", int row = 1) =>
            new(row, row.ToString(), ReviewFixtures.MariCode, "MARI", "MAASIKAS", "EE", start, end, type, type == "1" ? "Tööleping" : "Muu", state, state == "2" ? "Kehtiv" : state == "3" ? "Peatatud" : "Lõpetatud", null, null, null, null, [], null);

        // a board seat (601, no minimum) from the 11th stands for the month; the Tööleping ended on the 10th carries the
        // minimum: the share is the Tööleping's 10 days, not the board seat's 21
        var board = Entry(new DateOnly(2026, 10, 11), null, "2", "601", 2);
        var contract = Entry(new DateOnly(2024, 1, 1), new DateOnly(2026, 10, 10), "4", "1", 1);
        Assert.Equal(10m / 30m, PeriodDeclarer.MonthShare(board, [contract, board], 2026, 10));
        // a VÕS row newer than a running Tööleping: the Tööleping's whole month
        Assert.Equal(1m, PeriodDeclarer.MonthShare(Entry(new DateOnly(2026, 10, 6), null, "2", "600", 2), [Entry(new DateOnly(2024, 1, 1), null), Entry(new DateOnly(2026, 10, 6), null, "2", "600", 2)], 2026, 10));
        // no row carries the minimum: the standing entry's own days, as before
        Assert.Equal(25m / 30m, PeriodDeclarer.MonthShare(board with { StartDate = new DateOnly(2026, 10, 7) }, [], 2026, 10));
        // a Tööleping ended years ago beside the board seat covers no day of the month: it measures nothing, the board seat's days stand
        Assert.Equal(25m / 30m, PeriodDeclarer.MonthShare(board with { StartDate = new DateOnly(2026, 10, 7) }, [Entry(new DateOnly(2020, 1, 1), new DateOnly(2025, 6, 30), "4", "1", 3)], 2026, 10));

        // the entry standing for the month is paused: its days before the pause count, the pause does not
        var paused = Entry(new DateOnly(2024, 1, 1), null, "3") with { PauseStart = new DateOnly(2026, 10, 10) };
        Assert.Equal(0.3m, PeriodDeclarer.MonthShare(paused, [paused], 2026, 10));
        Assert.Equal(22m / 30m, PeriodDeclarer.MonthShare(paused with { PauseEnd = new DateOnly(2026, 10, 18) }, [], 2026, 10)); // 1–9 and 19–31: 22 days
        Assert.Equal(0m, PeriodDeclarer.MonthShare(paused with { PauseStart = new DateOnly(2026, 8, 1) }, [], 2026, 10)); // paused through the month
        Assert.Equal(0m, PeriodDeclarer.MonthShare(paused with { PauseStart = null }, [], 2026, 10)); // a pause without a start counts nothing
        // the person card's "kaasa": the paused entry counts every day as if valid
        Assert.Equal(1m, PeriodDeclarer.MonthShare(paused, [], 2026, 10, treatPausedAsValid: true));
    }

    [Fact]
    public void A_paused_employment_owes_no_minimum_on_its_pause_and_a_newer_contract_without_the_minimum_does_not_hide_the_zero_line()
    {
        // paused since August, through the month: the small pay is not topped up, and the build says why
        using (var paused = Ready(minimum: true, mariPaused: true, salary: 100m))
        {
            var report = paused.Declarer.Build(paused.Period.Id);
            var mari = Assert.Single(report.Persons, p => p.PersonId == paused.Mari.Id);
            Assert.Equal((0m, 0m), (mari.MinimumShare, mari.MinimumDue));
            Assert.Contains(mari.Problems, p => p.StartsWith(PeriodDeclarer.NoEmploymentInMonth, StringComparison.Ordinal) && p.Contains("paused", StringComparison.Ordinal));
            Assert.True(paused.Lines.Count == 1, string.Join(" | ", mari.Problems.Concat(report.Problems)));
            Assert.Equal(0m, Assert.Single(paused.Lines).Result!.SocialTaxMinimumIncrease);
        }

        // a VÕS contract (no minimum) begun on the 6th stands for the month beside the running Tööleping: with no pay
        // carrying the minimum, the zero line comes from the Tööleping, over the whole month
        using (var second = Ready(minimum: true, mariSecond: true))
        {
            second.Workspace.SetCategory(second.Salary, "Haigusleht");
            var report = second.Declarer.Build(second.Period.Id);
            var mari = Assert.Single(report.Persons, p => p.PersonId == second.Mari.Id);
            Assert.Contains(mari.Problems, p => p.StartsWith(PeriodDeclarer.ZeroLine, StringComparison.Ordinal));
            Assert.True(second.Lines.Count > 0, string.Join(" | ", mari.Problems.Concat(report.Problems)));
            var zero = Assert.Single(second.Lines, l => l.SourceRef == PeriodDeclarer.MinimumSource);
            Assert.Equal(("10", 1m, 886m), (zero.Line.TypeCode, mari.MinimumShare, mari.MinimumDue));
        }
    }

    [Fact]
    public void A_month_of_a_year_this_build_has_no_rates_for_is_refused_never_computed_with_anothers()
    {
        Assert.Same(TaxRates.Year2026, TaxRates.For(2026));
        Assert.Null(TaxRates.For(2027));
        using var box = Ready();
        var january = box.Store.GetOrCreatePeriod(box.Company.Id, 2027, 1);
        var refusal = Assert.Throws<DeclareException>(() => new PeriodDeclarer(box.Store).Build(january.Id));
        Assert.Equal(PeriodDeclarer.RatesMissing, refusal.Code);
        Assert.Contains("2027", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(box.Store.Payments(january.Id));
    }

    [Fact]
    public void A_build_makes_one_draft_per_declared_payment_from_the_net_and_builds_again_in_place()
    {
        using var box = Ready();
        var report = box.Declarer.Build(box.Period.Id);
        Assert.Equal(1, report.Count(LineChange.New));
        var mari = Assert.Single(report.Persons, p => p.PersonId == box.Mari.Id);
        Assert.Equal((700m, 1m), (mari.ExemptionOffered, mari.MinimumShare));
        Assert.Empty(mari.Problems);

        var line = Assert.Single(box.Lines);
        var row = ReviewFixtures.StatementRows(box.Store, box.Store.Imports(box.Company.Id)[0])[0];
        Assert.Equal((PaymentStatus.Draft, LineAction.R, Section.EE0201001, "10", 905.92m, row.Id, (Guid?)null, 1), (line.Status, line.Action, line.Section, line.Line.TypeCode, line.NetPaid, line.SourceRowId, line.ReviewLineId, line.Version));
        Assert.Equal("10235688-202610-01-001", line.LineNumber);
        Assert.Equal(905.92m, line.Result!.Net);
        Assert.True(line.Result.Gross > 905.92m);
        Assert.Equal(new DateOnly(2026, 10, 5), line.Line.DocumentDate);
        Assert.Equal(ReviewFixtures.Source + " [2]", line.SourceRef);

        // the same review again: the draft is replaced in place, the key stands
        var again = box.Declarer.Build(box.Period.Id);
        Assert.Equal(1, again.Count(LineChange.Replaced));
        var replaced = Assert.Single(box.Lines);
        Assert.Equal((line.Id, line.LineNumber, 1), (replaced.Id, replaced.LineNumber, replaced.Version));

        // the payment is declared no more: the draft goes
        box.Workspace.SetDeclare(box.Salary, false);
        var gone = box.Declarer.Build(box.Period.Id);
        Assert.Equal(1, gone.Count(LineChange.Deleted));
        Assert.Empty(box.Lines);

        // declared again: a new key, the old number burnt
        box.Workspace.SetDeclare(box.Salary, true);
        box.Declarer.Build(box.Period.Id);
        Assert.Equal("10235688-202610-01-002", Assert.Single(box.Lines).LineNumber);
    }

    [Fact]
    public void Totals_per_type_make_one_line_per_person_and_type_dated_by_the_latest_payment_and_the_cuts_never_mix_once_sent()
    {
        using var box = Ready();
        // a second payment of the salary type (10) by hand, later in the month, and one of another type (24)
        box.Workspace.AddHandLine(box.Period.Id, box.Mari.Id, new DateOnly(2026, 10, 20), 100m, "Mari Maasikas", "lisatasu", "Palgad");
        box.Workspace.AddHandLine(box.Period.Id, box.Mari.Id, new DateOnly(2026, 10, 15), 120m, "Mari Maasikas", "haigusleht", "Haigusleht");

        // payment by payment: three lines
        var each = box.Declarer.Build(box.Period.Id);
        Assert.Equal(3, each.Count(LineChange.New));
        Assert.Equal(3, box.Lines.Count);

        // the totals cut: one line per type, the salary type's two payments added up and dated by the later one; the
        // three per-payment drafts go
        var totals = box.Declarer.Build(box.Period.Id, DeclarationMode.TotalsPerType);
        Assert.Equal((2, 3), (totals.Count(LineChange.New), totals.Count(LineChange.Deleted)));
        var lines = box.Lines.OrderBy(l => l.Line.TypeCode).ToList();
        Assert.Equal(2, lines.Count);
        var salary = lines[0];
        Assert.Equal(("10", 1005.92m, new DateOnly(2026, 10, 20), (Guid?)null, (Guid?)null), (salary.Line.TypeCode, salary.NetPaid, salary.Line.DocumentDate, salary.SourceRowId, salary.ReviewLineId));
        Assert.True(PeriodDeclarer.IsTotals(salary));
        var (typeCode, references) = PeriodDeclarer.TotalsOf(salary.SourceRef)!.Value;
        Assert.Equal("10", typeCode);
        Assert.Equal($"{ReviewFixtures.Source} [2]; {ReviewWorkspace.ByHandSource}", references);
        Assert.StartsWith("koond, liik 10: ", ReportText.SourceText(salary.SourceRef), StringComparison.Ordinal);
        Assert.Equal(("24", 120m, new DateOnly(2026, 10, 15)), (lines[1].Line.TypeCode, lines[1].NetPaid, lines[1].Line.DocumentDate));

        // built again the same way: kept in place (replaced drafts, the same keys)
        var again = box.Declarer.Build(box.Period.Id, DeclarationMode.TotalsPerType);
        Assert.Equal((2, 0), (again.Count(LineChange.Replaced), again.Count(LineChange.New)));
        Assert.Equal(lines.Select(l => l.LineNumber), box.Lines.OrderBy(l => l.Line.TypeCode).Select(l => l.LineNumber));

        // sent as totals: the other cut is refused until those lines are gone, the same cut goes on
        box.Submitter.Export(box.Period.Id, box.Folder);
        box.Submitter.MarkUploaded(box.Period.Id);
        foreach (var line in box.Lines)
        {
            box.Submitter.Resolve(line.Id, onServer: true);
        }

        box.Submitter.MarkConfirmed(box.Period.Id);
        box.Submitter.Reopen(box.Period.Id);
        Assert.Equal(PeriodDeclarer.ModeChanged, Assert.Throws<DeclareException>(() => box.Declarer.Build(box.Period.Id)).Code);
        Assert.Equal(2, box.Declarer.Build(box.Period.Id, DeclarationMode.TotalsPerType).Count(LineChange.Kept));

        // drafts over the sent lines hide nothing: the cut is judged by what stands on the server beneath them
        box.Workspace.SavePerson(box.Mari with { FundedPensionMember = true, PensionRate = 0.02m }); // the salary type carries the pension, sick pay does not
        var pension = box.Declarer.Build(box.Period.Id, DeclarationMode.TotalsPerType);
        Assert.Equal((1, 1), (pension.Count(LineChange.Changed), pension.Count(LineChange.Kept)));
        Assert.Equal((LineAction.M, PaymentStatus.Draft), box.Lines.Where(l => l.Line.TypeCode == "10").Select(l => (l.Action, l.Status)).Single());
        Assert.Equal(PeriodDeclarer.ModeChanged, Assert.Throws<DeclareException>(() => box.Declarer.Build(box.Period.Id)).Code);

        // a refused payment in a type: the type's line stands as it is on the server — the M draft goes, nothing is cancelled
        box.Workspace.SavePerson(box.Mari with { FundedPensionMember = true, PensionRate = null }); // PENSION_RATE_UNKNOWN on every payment
        var refused = box.Declarer.Build(box.Period.Id, DeclarationMode.TotalsPerType);
        Assert.Equal((1, 1, 0, 0), (refused.Count(LineChange.Deleted), refused.Count(LineChange.Kept), refused.Count(LineChange.Cancelled), refused.Count(LineChange.Changed)));
        Assert.All(box.Lines, l => Assert.Equal((PaymentStatus.Accepted, LineAction.R), (l.Status, l.Action)));
        Assert.All(box.Lines, l => Assert.True(PeriodDeclarer.IsTotals(l)));

        // the file whose rows a sent totals line adds up stays: it is their source, though no row is named on the line
        box.Workspace.SavePerson(box.Mari with { PensionRate = 0.02m });
        box.Store.ClearMatchRun(box.Period.Id);
        var file = box.Store.Imports(box.Company.Id).Single();
        Assert.Contains("add up", Assert.Throws<InvalidOperationException>(() => box.Store.RemoveImport(file.Id)).Message);
    }

    [Fact]
    public void The_minimums_zero_line_belongs_to_both_cuts_and_never_trips_the_cut_guard()
    {
        using var box = Ready(minimum: true);
        box.Workspace.SetCategory(box.Salary, "Haigusleht"); // sick pay (24) carries no minimum: the zero line of type 10 comes with it
        var report = box.Declarer.Build(box.Period.Id, DeclarationMode.TotalsPerType);
        Assert.Equal(2, report.Count(LineChange.New));
        Assert.Single(box.Lines, l => l.SourceRef == PeriodDeclarer.MinimumSource);
        Assert.Single(box.Lines, PeriodDeclarer.IsTotals);

        box.Submitter.Export(box.Period.Id, box.Folder);
        box.Submitter.MarkUploaded(box.Period.Id);
        foreach (var line in box.Lines)
        {
            box.Submitter.Resolve(line.Id, onServer: true);
        }

        box.Submitter.MarkConfirmed(box.Period.Id);
        box.Submitter.Reopen(box.Period.Id);
        Assert.Equal(2, box.Declarer.Build(box.Period.Id, DeclarationMode.TotalsPerType).Count(LineChange.Kept)); // the zero line is no other cut
        Assert.Equal(PeriodDeclarer.ModeChanged, Assert.Throws<DeclareException>(() => box.Declarer.Build(box.Period.Id)).Code); // the totals line is
    }

    [Fact]
    public void A_refused_payment_is_left_out_and_said_and_a_hand_line_keeps_its_link()
    {
        using var box = Ready();
        // Jaan's November payment declared by the accountant's word: refused by the review (another month, no type)
        var view = box.Workspace.Load(box.Period.Id);
        box.Workspace.SetDeclare(view.Persons[1].Payments.Single(), true);
        var hand = box.Workspace.AddHandLine(box.Period.Id, box.Mari.Id, new DateOnly(2026, 10, 20), 100m, "Mari Maasikas", "lisatasu", "Palgad");

        var report = box.Declarer.Build(box.Period.Id);
        var jaan = Assert.Single(report.Persons, p => p.Name.StartsWith("TAMM", StringComparison.Ordinal));
        Assert.Contains(jaan.Problems, p => p.StartsWith(PeriodDeclarer.Refused, StringComparison.Ordinal) && p.Contains(ReviewChecks.NotInPeriod));
        Assert.Empty(jaan.Lines);
        Assert.Equal(2, box.Lines.Count);
        var handLine = Assert.Single(box.Lines, l => l.ReviewLineId == hand.Id);
        Assert.Equal((100m, (Guid?)null, ReviewWorkspace.ByHandSource), (handLine.NetPaid, handLine.SourceRowId, handLine.SourceRef));

        // the exemption is one pool: the first payment by date takes it, the second gets what is left
        var mari = Assert.Single(report.Persons, p => p.PersonId == box.Mari.Id);
        var first = box.Lines.Single(l => l.SourceRowId is not null).Result!;
        var second = handLine.Result!;
        Assert.Equal(700m, first.ExemptionUsed + second.ExemptionUsed + mari.ExemptionLeft);
        Assert.True(first.ExemptionUsed > 0m);
    }

    [Fact]
    public void A_valid_employment_without_pay_carrying_the_minimum_gets_a_zero_line_unless_the_minimum_is_off()
    {
        using var box = Ready(minimum: true);
        box.Workspace.SetCategory(box.Salary, "Haigusleht"); // sick pay (24) carries no minimum; the contract (1 → 10) does
        var report = box.Declarer.Build(box.Period.Id);
        var mari = Assert.Single(report.Persons, p => p.PersonId == box.Mari.Id);
        Assert.Contains(mari.Problems, p => p.StartsWith(PeriodDeclarer.ZeroLine, StringComparison.Ordinal));
        Assert.Equal(2, box.Lines.Count);
        var zero = Assert.Single(box.Lines, l => l.SourceRef == PeriodDeclarer.MinimumSource);
        Assert.Equal(("10", 0m, new DateOnly(2026, 10, 31), (Guid?)null, (Guid?)null), (zero.Line.TypeCode, zero.Line.Amount, zero.Line.DocumentDate, zero.SourceRowId, zero.ReviewLineId));
        Assert.Equal(886m, zero.Result!.SocialTaxMinimumIncrease);
        Assert.Equal(PeriodDeclarer.MinimumSource, mari.TopUpKey);
        Assert.Equal(886m, mari.MinimumDue);

        // a month with no declared payment at all still owes the minimum under a valid contract: the zero line alone
        box.Workspace.SetDeclare(box.Salary, false);
        var alone = box.Declarer.Build(box.Period.Id);
        Assert.Equal(1, alone.Count(LineChange.Deleted));
        Assert.Equal(PeriodDeclarer.MinimumSource, Assert.Single(box.Lines).SourceRef);

        // the minimum switched off in the panel: the zero line goes
        box.Workspace.SetDeclare(box.Salary, true);
        box.Workspace.SavePerson(box.Mari with { SocialTaxMinimumApplies = false });
        var again = box.Declarer.Build(box.Period.Id);
        Assert.Equal(1, again.Count(LineChange.Deleted));
        Assert.Single(box.Lines);
        Assert.Equal("24", box.Lines[0].Line.TypeCode);
    }

    [Fact]
    public void No_zero_line_is_built_under_a_code_that_is_no_personal_code_and_one_standing_goes()
    {
        // the zero line never passes the review's checks, so the declarer refuses the code itself (decision 142)
        using var box = Ready(minimum: true);
        var mariId = box.Mari.Id;
        Person Mari() => box.Store.Persons(box.Company.Id).Single(p => p.Id == mariId);

        // the zero line drafted while the code was valid (the salary unticked), then the code found wrong: the draft goes
        box.Workspace.SetDeclare(box.Salary, false);
        box.Declarer.Build(box.Period.Id);
        Assert.Equal(PeriodDeclarer.MinimumSource, Assert.Single(box.Lines).SourceRef);
        box.Store.UpdatePerson(Mari() with { PersonalCode = "AB123456" }); // past the card, which would refuse it
        var withdrawn = box.Declarer.Build(box.Period.Id);
        var mari = Assert.Single(withdrawn.Persons, p => p.PersonId == mariId);
        Assert.Equal(1, withdrawn.Count(LineChange.Deleted));
        Assert.Equal((LineChange.Deleted, ReviewChecks.CodeInvalid), (mari.Lines.Single().Change, mari.Lines.Single().Note));
        Assert.Contains(mari.Problems, p => p.StartsWith($"{ReviewChecks.CodeInvalid}:", StringComparison.Ordinal) && p.Contains("'AB123456'", StringComparison.Ordinal));
        Assert.Empty(box.Lines);

        // the salary ticked too: refused, and still no zero line
        box.Workspace.SetDeclare(box.Salary, true);
        var report = box.Declarer.Build(box.Period.Id);
        mari = Assert.Single(report.Persons, p => p.PersonId == mariId);
        Assert.Contains(mari.Problems, p => p.StartsWith($"{PeriodDeclarer.Refused}:", StringComparison.Ordinal) && p.Contains(ReviewChecks.CodeInvalid, StringComparison.Ordinal)); // the salary
        Assert.Contains(mari.Problems, p => p.StartsWith($"{ReviewChecks.CodeInvalid}:", StringComparison.Ordinal)); // the zero line
        Assert.Empty(box.Lines);

        // the code corrected: the salary and no zero line beside it, as before
        box.Store.UpdatePerson(Mari() with { PersonalCode = ReviewFixtures.MariCode });
        box.Declarer.Build(box.Period.Id);
        Assert.Equal("10", Assert.Single(box.Lines).Line.TypeCode);
    }

    [Fact]
    public void The_file_is_written_validated_recorded_and_the_lines_follow_the_period_through_its_states()
    {
        using var box = Ready();
        Assert.Equal(PeriodSubmitter.NoDrafts, Assert.Throws<DeclareException>(() => box.Submitter.Export(box.Period.Id, box.Folder)).Code);
        box.Declarer.Build(box.Period.Id);

        var result = box.Submitter.Export(box.Period.Id, box.Folder);
        Assert.True(File.Exists(result.Path));
        Assert.Equal("tsd-10235688-2026-10-20261104-100000.xml", Path.GetFileName(result.Path));
        Assert.Equal("10235688-2026-11-04T10:00:00.000", result.Submission.UniqueId);
        Assert.Equal(1, result.Lines);
        var bytes = File.ReadAllBytes(result.Path);
        Assert.False(bytes[0] == 0xEF); // no BOM
        Assert.Empty(new XbrlGlValidator(SchemaRoot).ValidateFile(result.Path));
        Assert.Equal(1, result.Declaration.Sections.Single().Lines.Single().Counter);
        var period = box.Store.Periods(box.Company.Id).Single();
        Assert.Equal((PeriodState.Exported, PaymentStatus.Sent), (period.State, box.Lines.Single().Status));
        Assert.Equal(result.Submission.Id, box.Lines.Single().SubmissionId);
        Assert.Equal(PeriodSubmitter.NoDrafts, Assert.Throws<DeclareException>(() => box.Submitter.Export(box.Period.Id, box.Folder)).Code);

        // the review is locked now; the export can be taken back while nothing came of it
        Assert.Equal(ReviewWorkspace.PeriodNotDraft, Assert.Throws<ReviewException>(() => box.Workspace.SetDeclare(box.Salary, false)).Code);
        Assert.Equal(ReviewWorkspace.PeriodNotDraft, Assert.Throws<DeclareException>(() => box.Declarer.Build(box.Period.Id)).Code);
        box.Submitter.Withdraw(result.Submission.Id);
        Assert.Equal((PeriodState.Draft, PaymentStatus.Draft), (box.Store.Periods(box.Company.Id).Single().State, box.Lines.Single().Status));
        Assert.Empty(box.Store.Submissions(box.Period.Id));

        // exported again, uploaded, confirmed — the confirmation asks while the line awaits its verdict
        var second = box.Submitter.Export(box.Period.Id, box.Folder);
        Assert.Equal(PeriodSubmitter.LinesPending, Assert.Throws<DeclareException>(() => box.Submitter.MarkConfirmed(box.Period.Id)).Code);
        box.Submitter.MarkUploaded(box.Period.Id);
        Assert.Equal(PeriodSubmitter.WrongState, Assert.Throws<DeclareException>(() => box.Submitter.MarkUploaded(box.Period.Id)).Code);
        Assert.Equal(PeriodSubmitter.LinesPending, Assert.Throws<DeclareException>(() => box.Submitter.MarkConfirmed(box.Period.Id)).Code);
        box.Submitter.Resolve(box.Lines.Single().Id, onServer: true);
        Assert.Equal(PaymentStatus.Accepted, box.Lines.Single().Status);
        box.Submitter.MarkConfirmed(box.Period.Id);
        period = box.Store.Periods(box.Company.Id).Single();
        Assert.Equal(PeriodState.Confirmed, period.State);
        Assert.NotNull(period.ConfirmedAt);
        Assert.Throws<KeyNotFoundException>(() => box.Submitter.Reopen(Guid.NewGuid()));
        Assert.Equal(PeriodSubmitter.WrongState, Assert.Throws<DeclareException>(() => box.Submitter.MarkUploaded(box.Period.Id)).Code);
        Assert.Equal(PeriodSubmitter.WrongState, Assert.Throws<DeclareException>(() => box.Submitter.Withdraw(second.Submission.Id)).Code);
    }

    [Fact]
    public void A_correction_file_names_a_reason_for_each_section_it_carries()
    {
        using var box = Ready();
        var unmatched = box.Workspace.Load(box.Period.Id).Unmatched.Payments.Single(p => p.Payee == "Kinnisvara OÜ");
        box.Workspace.SetPerson(unmatched, box.Mari.Id);
        box.Workspace.SetCategory(unmatched, "Üür"); // 57: the benefits section, beside the salary's pay section
        box.Declarer.Build(box.Period.Id);
        Assert.Equal([Section.EE0201001, Section.EE0201002], box.Submitter.DraftSections(box.Period.Id));
        var first = box.Submitter.Export(box.Period.Id, box.Folder);
        Assert.All(first.Declaration.Sections, s => Assert.Null(s.CorrectionReason)); // nothing confirmed yet: no reason asked or written
        foreach (var line in box.Lines)
        {
            box.Submitter.Resolve(line.Id, onServer: true);
        }

        box.Submitter.MarkUploaded(box.Period.Id);
        box.Submitter.MarkConfirmed(box.Period.Id);
        box.Submitter.Reopen(box.Period.Id);
        box.Workspace.SavePerson(box.Mari with { FundedPensionMember = true, PensionRate = 0.04m });
        box.Workspace.AddHandLine(box.Period.Id, box.Mari.Id, new DateOnly(2026, 10, 25), 50m, "Mari Maasikas", "lisaüür", "Üür");
        box.Declarer.Build(box.Period.Id);
        Assert.Equal([Section.EE0201001, Section.EE0201002], box.Submitter.DraftSections(box.Period.Id));
        Assert.True(box.Submitter.NeedsCorrectionReason(box.Period.Id)); // a correction of confirmed data
        Assert.Equal([LineAction.M, LineAction.R], box.Lines.Where(l => l.Status == PaymentStatus.Draft).OrderBy(l => l.Section).Select(l => l.Action)); // the salary changed, a rent line added

        // the reasons that fit each section: 3 is the pay section's alone, 4 the benefits section's
        Assert.Equal(["PARANDUSPOHJUS_1", "PARANDUSPOHJUS_2", "PARANDUSPOHJUS_3"], PeriodSubmitter.ReasonsFor(Section.EE0201001));
        Assert.Equal(["PARANDUSPOHJUS_1", "PARANDUSPOHJUS_2", "PARANDUSPOHJUS_4"], PeriodSubmitter.ReasonsFor(Section.EE0201002));

        // one reason for the whole file must fit both sections, as before
        Assert.Equal(PeriodSubmitter.ReasonNeeded, Assert.Throws<DeclareException>(() => box.Submitter.Export(box.Period.Id, box.Folder, "PARANDUSPOHJUS_3")).Code);

        // a reason per section: each section the file carries names one, and one that fits it
        string Refusal(Dictionary<Section, string> reasons) => Assert.Throws<DeclareException>(() => box.Submitter.Export(box.Period.Id, box.Folder, reasons)).Message;
        Assert.Contains("No reason is named for section EE0201002", Refusal(new() { [Section.EE0201001] = "PARANDUSPOHJUS_3" }));
        Assert.Contains("PARANDUSPOHJUS_4 does not fit section EE0201001", Refusal(new() { [Section.EE0201001] = "PARANDUSPOHJUS_4", [Section.EE0201002] = "PARANDUSPOHJUS_4" }));
        Assert.Contains("is not a PARANDUSPOHJUS code", Refusal(new() { [Section.EE0201001] = "PARANDUSPOHJUS_3", [Section.EE0201002] = "x" }));
        Assert.Empty(box.Store.Submissions(box.Period.Id).Where(s => s.Id != first.Submission.Id)); // nothing was written by the refusals

        var correction = box.Submitter.Export(box.Period.Id, box.Folder, new Dictionary<Section, string> { [Section.EE0201001] = "PARANDUSPOHJUS_3", [Section.EE0201002] = "PARANDUSPOHJUS_4" });
        Assert.Equal([(Section.EE0201001, "PARANDUSPOHJUS_3"), (Section.EE0201002, "PARANDUSPOHJUS_4")], correction.Declaration.Sections.Select(s => (s.Section, s.CorrectionReason!)));
        Assert.Empty(new XbrlGlValidator(SchemaRoot).ValidateFile(correction.Path));
        var read = XbrlGlReader.Load(correction.Path);
        Assert.Equal(["PARANDUSPOHJUS_3", "PARANDUSPOHJUS_4"], read.Sections.Select(s => s.CorrectionReason));
    }

    [Fact]
    public void A_withdrawn_exports_file_is_set_aside_under_another_name_and_the_outcome_says_what_became_of_it()
    {
        using var box = Ready();
        box.Declarer.Build(box.Period.Id);

        // the store keeps where the file was written; the name alone is what is shown
        var first = box.Submitter.Export(box.Period.Id, box.Folder);
        var stored = box.Store.Submissions(box.Period.Id).Single();
        Assert.Equal((Path.GetFileName(first.Path), first.Path), (stored.FileName, stored.FilePath));
        Assert.Equal(stored, first.Submission with { General = stored.General });

        // the export tells the store where each line sits in the file: a report placing the line elsewhere is another file's (decision 153)
        var line = box.Lines.Single();
        static FeedbackReport At(string key, int counter) => FeedbackReader.Read(System.Xml.Linq.XDocument.Parse($"<processing result=\"OK\"><entryHeader><entryNumber>EE0201001</entryNumber><validations><validation><errorLevel>OK</errorLevel><errorCode>OK</errorCode><errorDescription>ok</errorDescription><lineReferences><lineReference><lineInternalId>{key}</lineInternalId><lineNumberCounter>{counter}</lineNumberCounter></lineReference></lineReferences></validation></validations></entryHeader></processing>"));
        Assert.Equal(1, first.Declaration.Sections.Single().Lines.Single().Counter);
        Assert.Contains("another submission", Assert.Throws<InvalidOperationException>(() => box.Store.RecordFeedback(first.Submission.Id, At(line.LineNumber, 2))).Message);
        Assert.Equal(PaymentStatus.Sent, box.Lines.Single().Status);

        // taken back: the file is renamed, so the upload dialog no longer offers it, and still there to look at
        var aside = box.Submitter.Withdraw(first.Submission.Id);
        Assert.Equal((WithdrawnFileState.SetAside, first.Path + PeriodSubmitter.WithdrawnSuffix, stored.FileName), (aside.State, aside.Path, aside.FileName));
        Assert.False(File.Exists(first.Path));
        Assert.True(File.Exists(aside.Path));
        Assert.Empty(box.Store.Submissions(box.Period.Id));

        // exported again within the clock's second: the same name, a new file; taken back, it goes beside the first one set aside
        var second = box.Submitter.Export(box.Period.Id, box.Folder);
        Assert.Equal(first.Path, second.Path);
        var asideAgain = box.Submitter.Withdraw(second.Submission.Id);
        Assert.Equal((WithdrawnFileState.SetAside, first.Path + PeriodSubmitter.WithdrawnSuffix + "-2"), (asideAgain.State, asideAgain.Path));
        Assert.Equal(2, Directory.GetFiles(box.Folder, "*" + PeriodSubmitter.WithdrawnSuffix + "*").Length);
        Assert.Empty(Directory.GetFiles(box.Folder, "*.xml"));

        // a file moved or deleted meanwhile: the withdrawal is done all the same, and says the file was not found
        var third = box.Submitter.Export(box.Period.Id, box.Folder);
        File.Delete(third.Path);
        var gone = box.Submitter.Withdraw(third.Submission.Id);
        Assert.Equal((WithdrawnFileState.NotFound, third.Path), (gone.State, gone.Path));
        Assert.Equal(PeriodState.Draft, box.Store.Periods(box.Company.Id).Single().State);

        // a file another program holds: not renamed, said with the reason — and the withdrawal done
        var fourth = box.Submitter.Export(box.Period.Id, box.Folder);
        WithdrawnFile held;
        using (File.Open(fourth.Path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            held = box.Submitter.Withdraw(fourth.Submission.Id);
        }

        Assert.Equal(WithdrawnFileState.Failed, held.State);
        Assert.False(string.IsNullOrWhiteSpace(held.Error));
        Assert.True(File.Exists(fourth.Path));
        Assert.Equal((PeriodState.Draft, PaymentStatus.Draft), (box.Store.Periods(box.Company.Id).Single().State, box.Lines.Single().Status));
        File.Delete(fourth.Path);

        // an export made before the store kept the path (the name alone): looked for in the folder given, else unknown
        var old = box.Store.AddSubmission(box.Period.Id, "tsd-vana.xml", "vana", [box.Lines.Single().Id], DateTime.UtcNow);
        box.Store.SetPeriodState(box.Period.Id, PeriodState.Exported, DateTime.UtcNow);
        Assert.Null(box.Store.Submissions(box.Period.Id).Single().FilePath);
        File.WriteAllText(Path.Combine(box.Folder, "tsd-vana.xml"), "<vana/>");
        var found = box.Submitter.Withdraw(old.Id, box.Folder);
        Assert.Equal((WithdrawnFileState.SetAside, Path.Combine(box.Folder, "tsd-vana.xml") + PeriodSubmitter.WithdrawnSuffix, "tsd-vana.xml"), (found.State, found.Path, found.FileName));
        var older = box.Store.AddSubmission(box.Period.Id, "tsd-vanem.xml", "vanem", [box.Lines.Single().Id], DateTime.UtcNow);
        box.Store.SetPeriodState(box.Period.Id, PeriodState.Exported, DateTime.UtcNow);
        var unknown = box.Submitter.Withdraw(older.Id);
        Assert.Equal((WithdrawnFileState.FolderUnknown, (string?)null, "tsd-vanem.xml"), (unknown.State, unknown.Path, unknown.FileName));

        // the path as stored leads nowhere from here (a folder gone, a share under another letter): the file of that name in
        // the export folder last used is the same export, and is set aside there; an unreachable folder is said as such
        var moved = Path.Combine(box.Folder, "teisal");
        Directory.CreateDirectory(moved);
        var fifth = box.Submitter.Export(box.Period.Id, Path.Combine(box.Folder, "kadunud"));
        File.Move(fifth.Path, Path.Combine(moved, Path.GetFileName(fifth.Path)));
        Directory.Delete(Path.Combine(box.Folder, "kadunud"));
        var elsewhere = box.Submitter.Withdraw(fifth.Submission.Id, moved);
        Assert.Equal((WithdrawnFileState.SetAside, Path.Combine(moved, Path.GetFileName(fifth.Path)) + PeriodSubmitter.WithdrawnSuffix), (elsewhere.State, elsewhere.Path));
        var sixth = box.Submitter.Export(box.Period.Id, Path.Combine(box.Folder, "kadunud"));
        Directory.Delete(Path.Combine(box.Folder, "kadunud"), recursive: true);
        var unreachable = box.Submitter.Withdraw(sixth.Submission.Id, moved);
        Assert.Equal((WithdrawnFileState.Unreachable, sixth.Path), (unreachable.State, unreachable.Path));
    }

    [Fact]
    public void An_M_draft_over_a_sent_R_becomes_an_R_once_the_R_beneath_is_refused()
    {
        using var box = Ready();
        box.Declarer.Build(box.Period.Id);
        box.Submitter.Export(box.Period.Id, box.Folder);
        box.Submitter.MarkUploaded(box.Period.Id);
        var sent = box.Lines.Single();
        Assert.Equal((LineAction.R, PaymentStatus.Sent), (sent.Action, sent.Status));

        // reopened before the feedback came; the figures change: an M draft hides the sent R
        box.Submitter.Reopen(box.Period.Id);
        box.Workspace.SavePerson(box.Mari with { FundedPensionMember = true, PensionRate = 0.02m });
        box.Declarer.Build(box.Period.Id);
        var m = box.Lines.Single();
        Assert.Equal((LineAction.M, PaymentStatus.Draft, 2), (m.Action, m.Status, m.Version));
        var history = box.Store.History(box.Period.Id, sent.Section, sent.LineNumber);
        Assert.Equal([PaymentStatus.Sent, PaymentStatus.Draft], history.Select(v => v.Status));

        // the hidden R settled as refused (the history view's business): the stale M cannot go out, the next build makes the draft an R again
        box.Submitter.Resolve(history[0].Id, onServer: false);
        Assert.Equal(PeriodSubmitter.LinesStale, Assert.Throws<DeclareException>(() => box.Submitter.Export(box.Period.Id, box.Folder)).Code);
        var rebuilt = box.Declarer.Build(box.Period.Id);
        Assert.True(rebuilt.Count(LineChange.Replaced) == 1, string.Join(" | ", rebuilt.Persons.SelectMany(p => p.Lines.Select(l => $"{l.Key} {l.Action} {l.Change} {l.Note}").Concat(p.Problems)).Concat(rebuilt.Problems)));
        var r = box.Lines.Single();
        Assert.Equal((sent.LineNumber, LineAction.R, PaymentStatus.Draft), (r.LineNumber, r.Action, r.Status));

        // a T draft over a refused R has nothing to cancel: the next build deletes it
        box.Submitter.Export(box.Period.Id, box.Folder);
        box.Submitter.MarkUploaded(box.Period.Id);
        box.Submitter.Reopen(box.Period.Id);
        box.Workspace.SetDeclare(box.Salary, false);
        Assert.Equal(1, box.Declarer.Build(box.Period.Id).Count(LineChange.Cancelled));
        var t = box.Lines.Single();
        Assert.Equal((LineAction.T, PaymentStatus.Draft), (t.Action, t.Status));
        box.Submitter.Resolve(box.Store.History(box.Period.Id, t.Section, t.LineNumber).Single(v => v.Pending).Id, onServer: false);
        Assert.Equal(1, box.Declarer.Build(box.Period.Id).Count(LineChange.Deleted));
        Assert.Empty(box.Lines.Where(l => l.Status == PaymentStatus.Draft));
        box.Workspace.SetDeclare(box.Salary, true);
        Assert.Equal(1, box.Declarer.Build(box.Period.Id).Count(LineChange.New));
        Assert.Equal((LineAction.R, PaymentStatus.Draft), (box.Lines.Single().Action, box.Lines.Single().Status));

        // the R accepted and the figures changed again: an M over the accepted copy, the long-standing path
        box.Submitter.Export(box.Period.Id, box.Folder);
        box.Submitter.MarkUploaded(box.Period.Id);
        box.Submitter.Resolve(box.Lines.Single().Id, onServer: true);
        box.Submitter.Reopen(box.Period.Id);
        box.Workspace.SavePerson(box.Mari with { PensionRate = 0.04m });
        Assert.Equal(1, box.Declarer.Build(box.Period.Id).Count(LineChange.Changed));
        Assert.Equal(LineAction.M, box.Lines.Single().Action);
    }

    [Fact]
    public void Refusing_an_earlier_version_by_hand_refuses_the_later_pending_ones_that_presumed_it()
    {
        using var box = Ready();
        box.Declarer.Build(box.Period.Id);
        box.Submitter.Export(box.Period.Id, box.Folder);
        box.Submitter.MarkUploaded(box.Period.Id);
        box.Submitter.Reopen(box.Period.Id);
        box.Workspace.SavePerson(box.Mari with { FundedPensionMember = true, PensionRate = 0.02m });
        box.Declarer.Build(box.Period.Id);
        box.Submitter.Export(box.Period.Id, box.Folder); // the M goes out while the R still awaits its verdict
        box.Submitter.MarkUploaded(box.Period.Id);
        var line = box.Lines.Single();
        var history = box.Store.History(box.Period.Id, line.Section, line.LineNumber);
        Assert.Equal([(LineAction.R, PaymentStatus.Sent), (LineAction.M, PaymentStatus.Sent)], history.Select(v => (v.Action, v.Status)));

        // the R never reached MTA: the M that presumed it is refused with it, and the next build sends an R again
        box.Submitter.Resolve(history[0].Id, onServer: false);
        Assert.Equal([PaymentStatus.Rejected, PaymentStatus.Rejected], box.Store.History(box.Period.Id, line.Section, line.LineNumber).Select(v => v.Status));
        box.Submitter.Reopen(box.Period.Id);
        Assert.Equal(1, box.Declarer.Build(box.Period.Id).Count(LineChange.New));
        Assert.Equal((line.LineNumber, LineAction.R, PaymentStatus.Draft), (box.Lines.Single().LineNumber, box.Lines.Single().Action, box.Lines.Single().Status));

        // an accepted copy beneath keeps a later pending M alive when an in-between version is refused by hand
        using var other = Ready();
        other.Declarer.Build(other.Period.Id);
        other.Submitter.Export(other.Period.Id, other.Folder);
        other.Submitter.MarkUploaded(other.Period.Id);
        other.Submitter.Resolve(other.Lines.Single().Id, onServer: true);
        other.Submitter.Reopen(other.Period.Id);
        other.Workspace.SavePerson(other.Mari with { FundedPensionMember = true, PensionRate = 0.02m });
        other.Declarer.Build(other.Period.Id);
        other.Submitter.Export(other.Period.Id, other.Folder);
        other.Submitter.MarkUploaded(other.Period.Id);
        other.Submitter.Reopen(other.Period.Id);
        other.Workspace.SavePerson(other.Mari with { PensionRate = 0.04m });
        other.Declarer.Build(other.Period.Id);
        other.Submitter.Export(other.Period.Id, other.Folder, "PARANDUSPOHJUS_1");
        other.Submitter.MarkUploaded(other.Period.Id);
        var key = other.Lines.Single();
        var versions = other.Store.History(other.Period.Id, key.Section, key.LineNumber);
        Assert.Equal([PaymentStatus.Accepted, PaymentStatus.Sent, PaymentStatus.Sent], versions.Select(v => v.Status));
        other.Submitter.Resolve(versions[1].Id, onServer: false);
        Assert.Equal([PaymentStatus.Accepted, PaymentStatus.Rejected, PaymentStatus.Sent], other.Store.History(other.Period.Id, key.Section, key.LineNumber).Select(v => v.Status));
    }

    [Fact]
    public void A_correction_after_the_file_went_out_is_an_M_or_a_T_on_the_same_key_and_the_next_file_names_its_reason()
    {
        using var box = Ready();
        box.Declarer.Build(box.Period.Id);
        var export = box.Submitter.Export(box.Period.Id, box.Folder);
        box.Submitter.MarkUploaded(box.Period.Id);
        box.Submitter.Resolve(box.Lines.Single().Id, onServer: true);
        box.Submitter.MarkConfirmed(box.Period.Id);
        var key = box.Lines.Single().LineNumber;

        // the period is reopened; the person's pension facts change the figures: an M under the same key
        box.Submitter.Reopen(box.Period.Id);
        Assert.Equal(PeriodState.Draft, box.Store.Periods(box.Company.Id).Single().State);
        var unchanged = box.Declarer.Build(box.Period.Id);
        Assert.Equal((1, 0), (unchanged.Count(LineChange.Kept), unchanged.Count(LineChange.Changed)));
        box.Workspace.SavePerson(box.Mari with { FundedPensionMember = true, PensionRate = 0.02m });
        var changed = box.Declarer.Build(box.Period.Id);
        Assert.Equal(1, changed.Count(LineChange.Changed));
        var m = Assert.Single(box.Lines);
        Assert.Equal((key, LineAction.M, PaymentStatus.Draft, 2), (m.LineNumber, m.Action, m.Status, m.Version));
        Assert.True(m.Result!.FundedPension > 0m);

        // the confirmed declaration's file names its reason, and the reason fits its section
        Assert.True(box.Submitter.NeedsCorrectionReason(box.Period.Id));
        Assert.Equal(PeriodSubmitter.ReasonNeeded, Assert.Throws<DeclareException>(() => box.Submitter.Export(box.Period.Id, box.Folder)).Code);
        Assert.Equal(PeriodSubmitter.ReasonNeeded, Assert.Throws<DeclareException>(() => box.Submitter.Export(box.Period.Id, box.Folder, "PARANDUSPOHJUS_9")).Code);
        var correction = box.Submitter.Export(box.Period.Id, box.Folder, "PARANDUSPOHJUS_1");
        Assert.Equal("PARANDUSPOHJUS_1", correction.Declaration.Sections.Single().CorrectionReason);
        Assert.Equal(LineAction.M, correction.Declaration.Sections.Single().Lines.Single().Action);
        Assert.Null(PeriodSubmitter.ReasonFor(Section.EE0201002, "PARANDUSPOHJUS_3"));
        Assert.Equal("PARANDUSPOHJUS_4", PeriodSubmitter.ReasonFor(Section.EE0201002, "PARANDUSPOHJUS_4"));
        Assert.Equal(PeriodState.Exported, box.Store.Periods(box.Company.Id).Single().State);
        Assert.Equal(2, box.Store.Submissions(box.Period.Id).Count);

        // the M accepted by hand; the payment declared no more: a T under the key, then a dead key and a new R
        box.Submitter.Resolve(box.Lines.Single().Id, onServer: true);
        box.Submitter.Reopen(box.Period.Id);
        box.Workspace.SetDeclare(box.Salary, false);
        var cancelled = box.Declarer.Build(box.Period.Id);
        Assert.Equal(1, cancelled.Count(LineChange.Cancelled));
        var t = Assert.Single(box.Lines);
        Assert.Equal((key, LineAction.T, PaymentStatus.Draft, 3, (TaxResult?)null), (t.LineNumber, t.Action, t.Status, t.Version, t.Result));
        box.Submitter.Export(box.Period.Id, box.Folder, "PARANDUSPOHJUS_1");
        box.Submitter.Resolve(box.Lines.Single().Id, onServer: true);
        Assert.Equal(PaymentStatus.Cancelled, box.Lines.Single().Status);
        box.Submitter.Reopen(box.Period.Id);
        box.Workspace.SetDeclare(box.Salary, true);
        var revived = box.Declarer.Build(box.Period.Id);
        Assert.Equal(1, revived.Count(LineChange.New));
        Assert.Equal(2, box.Lines.Count);
        Assert.Equal("10235688-202610-01-002", box.Lines.Single(l => l.Status == PaymentStatus.Draft).LineNumber);
    }

    [Fact]
    public void A_type_moving_to_the_other_section_cancels_the_line_first_and_a_refused_key_is_used_again()
    {
        using var box = Ready();
        box.Declarer.Build(box.Period.Id);
        box.Submitter.Export(box.Period.Id, box.Folder);
        box.Submitter.Resolve(box.Lines.Single().Id, onServer: true);
        box.Submitter.Reopen(box.Period.Id);
        box.Workspace.SetCategory(box.Salary, "Üür"); // 57: the other section
        var report = box.Declarer.Build(box.Period.Id);
        var mari = Assert.Single(report.Persons, p => p.PersonId == box.Mari.Id);
        Assert.Contains(mari.Problems, p => p.StartsWith(PeriodDeclarer.SectionChanged, StringComparison.Ordinal));
        var t = Assert.Single(box.Lines);
        Assert.Equal((LineAction.T, Section.EE0201001), (t.Action, t.Section));

        // once the cancellation is accepted, the line follows in its section under a new key
        box.Submitter.Export(box.Period.Id, box.Folder);
        box.Submitter.Resolve(t.Id, onServer: true);
        box.Submitter.Reopen(box.Period.Id);
        box.Declarer.Build(box.Period.Id);
        var rent = Assert.Single(box.Lines, l => l.Status == PaymentStatus.Draft);
        Assert.Equal((Section.EE0201002, "57", "10235688-202610-02-001"), (rent.Section, rent.Line.TypeCode, rent.LineNumber));

        // a line refused black never reached the server: the next build uses the same key with an R again
        var refused = box.Submitter.Export(box.Period.Id, box.Folder);
        box.Submitter.Resolve(rent.Id, onServer: false);
        Assert.Equal(PaymentStatus.Rejected, box.Lines.Single(l => l.Section == Section.EE0201002).Status);
        box.Submitter.Reopen(box.Period.Id);
        box.Declarer.Build(box.Period.Id);
        var again = box.Lines.Single(l => l.Section == Section.EE0201002);
        Assert.Equal((rent.LineNumber, LineAction.R, PaymentStatus.Draft, 2), (again.LineNumber, again.Action, again.Status, again.Version));

        // refused black and then moved back to the pay section: a fresh key there, the bank row moving with it
        box.Workspace.SetCategory(box.Salary, "Palgad");
        var moved = box.Declarer.Build(box.Period.Id);
        Assert.Equal(1, moved.Count(LineChange.New));
        var back = box.Lines.Single(l => l.Status == PaymentStatus.Draft);
        Assert.Equal((Section.EE0201001, "10", "10235688-202610-01-002", rent.SourceRowId), (back.Section, back.Line.TypeCode, back.LineNumber, back.SourceRowId));
        Assert.Equal(1, box.Declarer.Build(box.Period.Id).Count(LineChange.Replaced)); // and it stays put on the next build: the live key speaks for the row
        Assert.Equal([PaymentStatus.Cancelled, PaymentStatus.Draft, PaymentStatus.Rejected], box.Lines.Select(l => l.Status)); // the dead key, the live one, the refused one
        Assert.Equal(1, refused.Lines);
    }

    [Fact]
    public void A_second_build_keeps_a_drafted_cancellation_and_cancels_beneath_a_dropped_change()
    {
        using var box = Ready();
        box.Declarer.Build(box.Period.Id);
        box.Submitter.Export(box.Period.Id, box.Folder);
        box.Submitter.Resolve(box.Lines.Single().Id, onServer: true);
        box.Submitter.Reopen(box.Period.Id);

        // declared no more: the T drafted once, then kept by every later build
        box.Workspace.SetDeclare(box.Salary, false);
        Assert.Equal(1, box.Declarer.Build(box.Period.Id).Count(LineChange.Cancelled));
        var again = box.Declarer.Build(box.Period.Id);
        Assert.Equal((0, 1), (again.Count(LineChange.Deleted), again.Count(LineChange.Kept)));
        var t = Assert.Single(box.Lines);
        Assert.Equal((LineAction.T, PaymentStatus.Draft), (t.Action, t.Status));

        // declared again, the figures changed: the T draft goes, an M stands; then declared no more: the M goes and the T comes
        box.Workspace.SetDeclare(box.Salary, true);
        box.Workspace.SavePerson(box.Mari with { FundedPensionMember = true, PensionRate = 0.02m });
        Assert.Equal(1, box.Declarer.Build(box.Period.Id).Count(LineChange.Changed));
        Assert.Equal(LineAction.M, Assert.Single(box.Lines).Action);
        box.Workspace.SetDeclare(box.Salary, false);
        var dropped = box.Declarer.Build(box.Period.Id);
        Assert.Equal((1, 0), (dropped.Count(LineChange.Cancelled), dropped.Count(LineChange.Deleted)));
        var cancellation = Assert.Single(box.Lines);
        Assert.Equal((LineAction.T, PaymentStatus.Draft, 2), (cancellation.Action, cancellation.Status, cancellation.Version)); // the dropped M draft left no version behind
    }

    [Fact]
    public void A_zero_line_follows_the_employments_own_minimum_flag_and_pay_after_the_employment_ended_owes_no_minimum()
    {
        // 501 Tasuta töötamine maps to type 10 but carries no minimum of its own: no zero line for a month without pay
        using (var unpaid = Ready(minimum: true, mariType: 501))
        {
            unpaid.Workspace.SetDeclare(unpaid.Salary, false);
            var report = unpaid.Declarer.Build(unpaid.Period.Id);
            Assert.Empty(unpaid.Lines);
            Assert.DoesNotContain(report.Persons.Single(p => p.PersonId == unpaid.Mari.Id).Problems, p => p.StartsWith(PeriodDeclarer.ZeroLine, StringComparison.Ordinal));
        }

        // a final settlement paid in October for a contract that ended in September: the register covers no day, no top-up
        using var ended = Ready(minimum: true, salary: 300m, mariEnded: true);
        var build = ended.Declarer.Build(ended.Period.Id);
        var mari = build.Persons.Single(p => p.PersonId == ended.Mari.Id);
        Assert.Contains(mari.Problems, p => p.StartsWith(PeriodDeclarer.NoEmploymentInMonth, StringComparison.Ordinal));
        var line = Assert.Single(ended.Lines);
        Assert.Equal((0m, 0m, 0m), (line.Result!.SocialTaxMinimumIncrease, mari.MinimumDue, mari.MinimumShortfall));
        Assert.True(line.Result.Gross < 886m);
    }

    [Fact]
    public void A_person_excluded_on_the_card_gets_no_line_and_what_stood_for_them_is_undone()
    {
        using var box = Ready();
        box.Declarer.Build(box.Period.Id);
        Assert.Single(box.Lines);

        // excluded while the line is a draft: the draft goes, and the build says why the salary is not declared
        box.Workspace.SavePerson(box.Mari with { Declaration = DeclarationChoice.Exclude });
        Assert.Equal(DeclarationChoice.Exclude, box.Mari.Declaration); // kept in the store
        var excluded = box.Declarer.Build(box.Period.Id);
        Assert.Equal(1, excluded.Count(LineChange.Deleted));
        Assert.Empty(box.Lines);
        Assert.Contains(excluded.Persons.Single(p => p.PersonId == box.Mari.Id).Problems, p => p.StartsWith(PeriodDeclarer.Excluded, StringComparison.Ordinal) && p.Contains("1 payment", StringComparison.Ordinal));

        // back to the rules, the line is declared and sent; excluded again, the line on the server gets its T
        box.Workspace.SavePerson(box.Mari with { Declaration = DeclarationChoice.Auto });
        box.Declarer.Build(box.Period.Id);
        box.Submitter.Export(box.Period.Id, box.Folder);
        box.Submitter.MarkUploaded(box.Period.Id);
        box.Submitter.Resolve(box.Lines.Single().Id, onServer: true);
        box.Submitter.MarkConfirmed(box.Period.Id);
        box.Submitter.Reopen(box.Period.Id);
        box.Workspace.SavePerson(box.Mari with { Declaration = DeclarationChoice.Exclude });
        var cancelled = box.Declarer.Build(box.Period.Id);
        Assert.Equal(1, cancelled.Count(LineChange.Cancelled));
        Assert.Equal(LineAction.T, Assert.Single(box.Lines).Action);
    }

    [Fact]
    public void A_person_included_on_the_card_owes_the_minimum_as_under_a_valid_employment_when_the_other_conditions_hold()
    {
        // a paused employment and no pay: by the rules nothing is declared; included, the zero line carries the minimum
        using (var paused = Ready(minimum: true, mariPaused: true))
        {
            paused.Workspace.SetDeclare(paused.Salary, false);
            paused.Declarer.Build(paused.Period.Id);
            Assert.Empty(paused.Lines);

            paused.Workspace.SavePerson(paused.Mari with { Declaration = DeclarationChoice.Include });
            var report = paused.Declarer.Build(paused.Period.Id);
            var zero = Assert.Single(paused.Lines);
            Assert.Equal((PeriodDeclarer.MinimumSource, "10", 0m, new DateOnly(2026, 10, 31)), (zero.SourceRef, zero.Line.TypeCode, zero.Line.Amount, zero.Line.DocumentDate));
            Assert.Equal(886m, zero.Result!.SocialTaxMinimumIncrease);
            Assert.Contains(report.Persons.Single(p => p.PersonId == paused.Mari.Id).Problems, p => p.StartsWith(PeriodDeclarer.ZeroLine, StringComparison.Ordinal) && p.Contains("as if employed", StringComparison.Ordinal));
            Assert.Contains(report.Persons.Single(p => p.PersonId == paused.Mari.Id).Problems, p => p.StartsWith(PeriodDeclarer.Included, StringComparison.Ordinal));

            // the other conditions still hold the line back: the minimum switched off on the card
            paused.Workspace.SavePerson(paused.Mari with { SocialTaxMinimumApplies = false });
            paused.Declarer.Build(paused.Period.Id);
            Assert.Empty(paused.Lines);
        }

        // an employment ended before the month, no pay: included, the zero line comes from the ended employment's type
        using (var ended = Ready(minimum: true, mariEnded: true))
        {
            ended.Workspace.SetDeclare(ended.Salary, false);
            ended.Workspace.SavePerson(ended.Mari with { Declaration = DeclarationChoice.Include });
            ended.Declarer.Build(ended.Period.Id);
            Assert.Equal(886m, Assert.Single(ended.Lines).Result!.SocialTaxMinimumIncrease);
        }

        // pay after the employment ended: by the rules no minimum; included, the pay is topped up to it
        using (var settled = Ready(minimum: true, salary: 300m, mariEnded: true))
        {
            settled.Workspace.SavePerson(settled.Mari with { Declaration = DeclarationChoice.Include });
            var build = settled.Declarer.Build(settled.Period.Id);
            Assert.DoesNotContain(build.Persons.Single(p => p.PersonId == settled.Mari.Id).Problems, p => p.StartsWith(PeriodDeclarer.NoEmploymentInMonth, StringComparison.Ordinal));
            Assert.True(Assert.Single(settled.Lines).Result!.SocialTaxMinimumIncrease > 0m);
        }

        // an employment type that carries no minimum of its own (501) stays without a zero line, included or not, and the build says why
        using var unpaid = Ready(minimum: true, mariType: 501, mariPaused: true);
        unpaid.Workspace.SetDeclare(unpaid.Salary, false);
        unpaid.Workspace.SavePerson(unpaid.Mari with { Declaration = DeclarationChoice.Include });
        var said = unpaid.Declarer.Build(unpaid.Period.Id);
        Assert.Empty(unpaid.Lines);
        Assert.Contains(said.Persons.Single(p => p.PersonId == unpaid.Mari.Id).Problems, p => p.StartsWith(PeriodDeclarer.Included, StringComparison.Ordinal) && p.Contains("carries no minimum", StringComparison.Ordinal));
    }

    [Fact]
    public void A_kaasa_person_the_register_does_not_list_and_nobody_paid_is_shown_with_the_word_and_nothing_is_built()
    {
        using var box = Ready();
        var kati = box.Workspace.AddPerson(box.Company.Id, "Kati", "Kask", "60001010008", "1");
        box.Workspace.SavePerson(kati with { Declaration = DeclarationChoice.Include, FundedPensionMember = false }, confirmResidency: true, confirmPension: true); // facts checked: a zero line could be built for her

        // listed in the review with the word, no payments; the build makes no line for her and says so (decision 137)
        var shown = Assert.Single(box.Workspace.Load(box.Period.Id).Persons, p => p.PersonId == kati.Id);
        Assert.Empty(shown.Payments);
        Assert.Null(shown.Register);
        Assert.Contains(shown.Problems, p => p.Code == MatchProblem.IncludedWithoutRow);
        var report = box.Declarer.Build(box.Period.Id);
        Assert.DoesNotContain(report.Persons, p => p.PersonId == kati.Id);
        Assert.Contains(report.Problems, p => p.StartsWith(PeriodDeclarer.Included, StringComparison.Ordinal) && p.Contains("Kask Kati", StringComparison.Ordinal) && p.Contains("no line is built", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Persons.SelectMany(p => p.Problems).Concat(report.Problems), p => p.StartsWith(PeriodDeclarer.ZeroLine, StringComparison.Ordinal)); // no zero line, no minimum
        Assert.DoesNotContain(box.Lines, l => l.PersonId == kati.Id);
        Assert.DoesNotContain(new ReportBuilder(box.Store, box.Workspace, box.Declarer).Build(box.Period.Id).Sheet(ReportText.InterimSheet)!.Rows, r => r.Cells.Count > 1 && r.Cells[1].Text == "Kask"); // not a row of the Vahearuanne either

        // a month past Draft, or one not yet searched, lists nobody by the card alone
        box.Store.SetPeriodState(box.Period.Id, PeriodState.Exported, DateTime.UtcNow);
        Assert.DoesNotContain(box.Workspace.Load(box.Period.Id).Persons, p => p.PersonId == kati.Id);
        box.Store.SetPeriodState(box.Period.Id, PeriodState.Draft, DateTime.UtcNow);

        // the card set back to Auto: not listed any more
        box.Workspace.SavePerson(box.Store.Persons(box.Company.Id).Single(p => p.Id == kati.Id) with { Declaration = DeclarationChoice.Auto });
        Assert.DoesNotContain(box.Workspace.Load(box.Period.Id).Persons, p => p.PersonId == kati.Id);

        // a "kaasa" person with a hand line is built as before: the word is for the one nobody paid
        box.Workspace.SavePerson(box.Store.Persons(box.Company.Id).Single(p => p.Id == kati.Id) with { Declaration = DeclarationChoice.Include, FundedPensionMember = false }, confirmResidency: true, confirmPension: true);
        box.Workspace.AddHandLine(box.Period.Id, kati.Id, new DateOnly(2026, 10, 20), 100m, "Kati Kask", "tasu", "Palgad");
        var paid = Assert.Single(box.Workspace.Load(box.Period.Id).Persons, p => p.PersonId == kati.Id);
        Assert.DoesNotContain(paid.Problems, p => p.Code == MatchProblem.IncludedWithoutRow);
        box.Declarer.Build(box.Period.Id);
        Assert.Contains(box.Lines, l => l.PersonId == kati.Id);
    }

    [Fact]
    public void A_refused_salary_whose_sent_line_stands_gets_no_zero_line_beside_it_so_the_minimum_is_never_declared_twice()
    {
        using var box = Ready(minimum: true, salary: 300m);
        box.Declarer.Build(box.Period.Id);
        box.Submitter.Export(box.Period.Id, box.Folder);
        box.Submitter.MarkUploaded(box.Period.Id);
        box.Submitter.Resolve(box.Lines.Single().Id, onServer: true);
        box.Submitter.MarkConfirmed(box.Period.Id);
        box.Submitter.Reopen(box.Period.Id);
        Assert.True(box.Lines.Single().Result!.SocialTaxMinimumIncrease > 0m); // the sent salary carries the minimum

        // the salary refused by the review (a type no table knows): its line stands, and no zero line comes beside it
        box.Workspace.SetTypeCode(box.Salary, "999");
        var report = box.Declarer.Build(box.Period.Id);
        Assert.Equal(1, report.Count(LineChange.Kept));
        Assert.DoesNotContain(box.Lines, l => l.SourceRef == PeriodDeclarer.MinimumSource);
        Assert.Single(box.Lines);
    }

    [Fact]
    public void A_v6_store_upgrades_to_v7_and_every_person_is_declared_by_the_rules_until_the_card_says_otherwise()
    {
        var path = Path.Combine(Path.GetTempPath(), $"satellestsd-{Guid.NewGuid():N}.db");
        try
        {
            Guid personId;
            Guid companyId;
            using (var v6 = SqliteStore.OpenAtVersion(path, 6))
            {
                var company = v6.AddCompany("10235688", "Näidis OÜ");
                companyId = company.Id;
                v6.ExecuteRaw($"INSERT INTO person (id, company_id, first_name, last_name, residency, on_pension, exemption_applied, minimum_applies, a1_case, residency_source, pension_source) VALUES ('{Guid.NewGuid():N}', '{company.Id:N}', 'Mari', 'Maasikas', 'Resident', 0, 1, 1, 'None', 'Unknown', 'Unknown')");
                personId = Guid.Parse(v6.ScalarRaw<string>("SELECT id FROM person"));
            }

            using var v7 = SqliteStore.Open(path);
            Assert.Equal((long)SqliteStore.SchemaVersion, v7.ScalarRaw<long>("SELECT MAX(version) FROM schema_version"));
            var person = Assert.Single(v7.Persons(companyId));
            Assert.Equal((personId, DeclarationChoice.Auto), (person.Id, person.Declaration));
            v7.UpdatePerson(person with { Declaration = DeclarationChoice.Include });
            Assert.Equal(DeclarationChoice.Include, Assert.Single(v7.Persons(companyId)).Declaration);
            var added = v7.AddPerson(Person.New(companyId, "Jaan", "Tamm", null) with { Declaration = DeclarationChoice.Exclude });
            Assert.Equal(DeclarationChoice.Exclude, v7.Persons(companyId).Single(p => p.Id == added.Id).Declaration);
        }
        finally
        {
            File.Delete(path);
            File.Delete(SqliteStore.BackupPathFor(path));
        }
    }

    [Fact]
    public void A_v7_store_upgrades_to_v8_and_every_person_goes_by_the_register_until_the_card_says_otherwise()
    {
        var path = Path.Combine(Path.GetTempPath(), $"satellestsd-{Guid.NewGuid():N}.db");
        try
        {
            Guid companyId;
            using (var v7 = SqliteStore.OpenAtVersion(path, 7))
            {
                var company = v7.AddCompany("10235688", "Näidis OÜ");
                companyId = company.Id;
                v7.ExecuteRaw($"INSERT INTO person (id, company_id, first_name, last_name, residency, on_pension, exemption_applied, minimum_applies, a1_case, residency_source, pension_source, tor_type_id, declaration) VALUES ('{Guid.NewGuid():N}', '{company.Id:N}', 'Mari', 'Maasikas', 'Resident', 0, 1, 1, 'None', 'Unknown', 'Unknown', '600', 'Include')");
            }

            using var v8 = SqliteStore.Open(path);
            Assert.Equal((long)SqliteStore.SchemaVersion, v8.ScalarRaw<long>("SELECT MAX(version) FROM schema_version"));
            var person = Assert.Single(v8.Persons(companyId));
            Assert.Equal(("600", DeclarationChoice.Include, false), (person.TorEmploymentTypeId, person.Declaration, person.TypeFromCard));
            v8.UpdatePerson(person with { TypeFromCard = true });
            Assert.True(Assert.Single(v8.Persons(companyId)).TypeFromCard);
        }
        finally
        {
            File.Delete(path);
            File.Delete(SqliteStore.BackupPathFor(path));
        }
    }

    [Fact]
    public void A_file_is_taken_back_only_while_exported_and_only_the_latest_and_a_report_names_its_section()
    {
        using var box = Ready();
        box.Declarer.Build(box.Period.Id);
        var first = box.Submitter.Export(box.Period.Id, box.Folder);
        box.Submitter.MarkUploaded(box.Period.Id);
        Assert.Equal(PeriodSubmitter.WrongState, Assert.Throws<DeclareException>(() => box.Submitter.Withdraw(first.Submission.Id)).Code);
        box.Submitter.Resolve(box.Lines.Single().Id, onServer: true);
        box.Submitter.MarkConfirmed(box.Period.Id);

        // a correction round: the second file is taken back, the period is a Draft again; the first file is history
        box.Submitter.Reopen(box.Period.Id);
        box.Workspace.SavePerson(box.Mari with { FundedPensionMember = true, PensionRate = 0.04m });
        box.Declarer.Build(box.Period.Id);
        var second = box.Submitter.Export(box.Period.Id, box.Folder, "PARANDUSPOHJUS_1");
        Assert.Equal(PeriodSubmitter.WrongState, Assert.Throws<DeclareException>(() => box.Submitter.Withdraw(first.Submission.Id)).Code);
        var period = box.Store.Periods(box.Company.Id).Single();
        Assert.True(box.Submitter.CanWithdraw(second.Submission, period, box.Store.Submissions(box.Period.Id)));
        Assert.False(box.Submitter.CanWithdraw(first.Submission, period, box.Store.Submissions(box.Period.Id)));
        box.Submitter.Withdraw(second.Submission.Id);
        period = box.Store.Periods(box.Company.Id).Single();
        Assert.Equal((PeriodState.Draft, 1), (period.State, box.Store.Submissions(box.Period.Id).Count));
        Assert.Equal(PaymentStatus.Draft, box.Lines.Single().Status);
        Assert.Equal(1, box.Declarer.Build(box.Period.Id).Count(LineChange.Replaced)); // the review and the build are open again

        // a confirmation never swallows a draft, forced or not; a reason that fits no section of the file is refused
        box.Submitter.Export(box.Period.Id, box.Folder, "PARANDUSPOHJUS_1");
        box.Submitter.Reopen(box.Period.Id);
        box.Workspace.SavePerson(box.Mari with { PensionRate = 0.06m });
        box.Declarer.Build(box.Period.Id);
        Assert.Equal(PeriodSubmitter.ReasonNeeded, Assert.Throws<DeclareException>(() => box.Submitter.Export(box.Period.Id, box.Folder, "PARANDUSPOHJUS_4")).Code);
        box.Submitter.Export(box.Period.Id, box.Folder, "PARANDUSPOHJUS_3");
        box.Submitter.Reopen(box.Period.Id);
        box.Workspace.SavePerson(box.Mari with { PensionRate = 0.02m });
        box.Declarer.Build(box.Period.Id);
        box.Store.SetPeriodState(box.Period.Id, PeriodState.Uploaded, DateTime.UtcNow);
        Assert.Contains("never sent", Assert.Throws<DeclareException>(() => box.Submitter.MarkConfirmed(box.Period.Id, force: true)).Message);
    }

    [Fact]
    public void A_bank_rows_line_is_known_by_its_row_so_a_re_made_overlay_still_corrects_it()
    {
        using var box = Ready();
        box.Workspace.SetNote(box.Salary, "esimene"); // an overlay with its own id
        box.Declarer.Build(box.Period.Id);
        Assert.Null(box.Lines.Single().ReviewLineId);
        box.Submitter.Export(box.Period.Id, box.Folder);
        box.Submitter.Resolve(box.Lines.Single().Id, onServer: true);
        box.Submitter.Reopen(box.Period.Id);

        box.Workspace.SetNote(box.Salary, null); // the overlay deciding nothing is deleted
        box.Workspace.SetTypeCode(box.Salary, "17"); // a new overlay, a new id, changed figures
        var report = box.Declarer.Build(box.Period.Id);
        Assert.Equal(1, report.Count(LineChange.Changed));
        Assert.Empty(report.Persons.SelectMany(p => p.Problems));
        Assert.Equal((LineAction.M, "17"), (box.Lines.Single().Action, box.Lines.Single().Line.TypeCode));
    }

    [Fact]
    public void A_line_refused_by_MTA_is_not_on_the_server_so_the_month_is_confirmed_only_by_force()
    {
        using var box = Ready();
        box.Declarer.Build(box.Period.Id);
        box.Submitter.Export(box.Period.Id, box.Folder);
        box.Submitter.MarkUploaded(box.Period.Id);
        box.Submitter.Resolve(box.Lines.Single().Id, onServer: false);
        Assert.Equal(PaymentStatus.Rejected, box.Lines.Single().Status);

        var refused = Assert.Throws<DeclareException>(() => box.Submitter.MarkConfirmed(box.Period.Id));
        Assert.Equal(PeriodSubmitter.LinesPending, refused.Code);
        Assert.Contains("refused", refused.Message);
        box.Submitter.MarkConfirmed(box.Period.Id, force: true);
        Assert.Equal(PeriodState.Confirmed, box.Store.Periods(box.Company.Id).Single().State);
    }

    [Fact]
    public void The_drafts_are_checked_against_a_build_rolled_back_so_an_export_knows_the_review_changed()
    {
        using var box = Ready();
        Assert.False(box.Declarer.DraftsCurrent(box.Period.Id)); // nothing built yet: a build would make a line
        Assert.Empty(box.Lines);

        box.Declarer.Build(box.Period.Id);
        var built = box.Lines.Single();
        Assert.True(box.Declarer.DraftsCurrent(box.Period.Id));
        Assert.Equal((built.Id, built.Version, built.LineNumber, built.Result), (box.Lines.Single().Id, box.Lines.Single().Version, box.Lines.Single().LineNumber, box.Lines.Single().Result)); // the dry run left the draft as it was, its id and counter too

        // the card changed after the build: the drafts no longer say it, and the dry run changed nothing
        box.Workspace.SavePerson(box.Mari with { FundedPensionMember = true, PensionRate = 0.02m });
        Assert.False(box.Declarer.DraftsCurrent(box.Period.Id));
        Assert.Equal((built.Id, built.Result), (box.Lines.Single().Id, box.Lines.Single().Result));
        Assert.False(box.Declarer.DraftsCurrent(box.Period.Id, DeclarationMode.TotalsPerType)); // the other cut is another set of lines
        box.Declarer.Build(box.Period.Id);
        Assert.True(box.Declarer.DraftsCurrent(box.Period.Id));

        box.Submitter.Export(box.Period.Id, box.Folder);
        Assert.Null(box.Declarer.DraftsCurrent(box.Period.Id)); // past Draft: a build is refused, nothing can be told
    }

    [Fact]
    public void A_company_in_MTAs_non_resident_register_is_labelled_MRR_KOOD_in_the_file_unless_its_card_says_otherwise()
    {
        using var box = Ready();
        box.Declarer.Build(box.Period.Id);
        var result = box.Submitter.Export(box.Period.Id, box.Folder);
        Assert.Equal("ARIREGISTRIKOOD", OrganizationDescription(result.Path)); // an eight-digit code beginning with 1: the Äriregister's
        Assert.Equal(PayeeId.Ariregistrikood, result.Declaration.RegistryCodeType);

        // the card says the company is in MTA's non-resident register (a Lithuanian MB, say): the file labels the code MRR_KOOD, and the schema takes it
        box.Submitter.Withdraw(result.Submission.Id);
        box.Store.UpdateCompany(box.Company with { PayerCodeType = PayeeId.MrrKood });
        var mrr = box.Submitter.Export(box.Period.Id, box.Folder);
        Assert.Empty(new XbrlGlValidator(SchemaRoot).ValidateFile(mrr.Path));
        Assert.Equal("MRR_KOOD", OrganizationDescription(mrr.Path));
        Assert.Equal(PayeeId.MrrKood, mrr.Declaration.RegistryCodeType);

        // the case e-MTA refused: a company whose code is MTA's non-resident register code, nothing said on the card — the shape decides
        using var foreign = SqliteStore.InMemory();
        var company = foreign.AddCompany("65758008", "MB Pavyzdys");
        var period = foreign.GetOrCreatePeriod(company.Id, 2026, 10);
        var clock = new FixedClock(new DateTime(2026, 11, 4, 10, 0, 0));
        var workspace = new ReviewWorkspace(foreign, TaxRates.Year2026, clock);
        var kati = workspace.AddPerson(company.Id, "Kati", "Kask", "60001010008", "1");
        workspace.SavePerson(kati with { FundedPensionMember = false }, confirmResidency: true, confirmPension: true);
        workspace.AddHandLine(period.Id, kati.Id, new DateOnly(2026, 10, 20), 1000m, "Kati Kask", "palk", "Palgad");
        new PeriodDeclarer(foreign, TaxRates.Year2026, clock).Build(period.Id);
        var exported = new PeriodSubmitter(foreign, SchemaRoot, clock).Export(period.Id, box.Folder);
        Assert.Empty(new XbrlGlValidator(SchemaRoot).ValidateFile(exported.Path));
        Assert.Equal("MRR_KOOD", OrganizationDescription(exported.Path));
        Assert.Equal("65758008", System.Xml.Linq.XDocument.Load(exported.Path).Root!.Element(XbrlGlWriter.GlCor + "accountingEntries")!.Element(XbrlGlWriter.GlCor + "entityInformation")!.Element(XbrlGlWriter.GlBus + "organizationIdentifiers")!.Element(XbrlGlWriter.GlBus + "organizationIdentifier")!.Value);

        static string OrganizationDescription(string path) =>
            System.Xml.Linq.XDocument.Load(path).Root!.Element(XbrlGlWriter.GlCor + "accountingEntries")!.Element(XbrlGlWriter.GlCor + "entityInformation")!
                .Element(XbrlGlWriter.GlBus + "organizationIdentifiers")!.Element(XbrlGlWriter.GlBus + "organizationDescription")!.Value;
    }

    [Fact]
    public void The_cut_the_drafts_were_built_with_is_read_from_them()
    {
        using var box = Ready();
        Assert.Null(box.Declarer.DraftsCut(box.Period.Id)); // nothing built: no cut to speak of
        box.Declarer.Build(box.Period.Id);
        Assert.Equal(DeclarationMode.PerPayment, box.Declarer.DraftsCut(box.Period.Id));
        box.Declarer.Build(box.Period.Id, DeclarationMode.TotalsPerType);
        Assert.Equal(DeclarationMode.TotalsPerType, box.Declarer.DraftsCut(box.Period.Id)); // the export compares it with the switch, and asks when they differ
        box.Declarer.Build(box.Period.Id);
        Assert.Equal(DeclarationMode.PerPayment, box.Declarer.DraftsCut(box.Period.Id));
    }

    [Fact]
    public void The_confirmation_names_the_lines_awaiting_a_verdict_and_the_refused_ones_in_one_question()
    {
        using var box = Ready();
        box.Workspace.AddHandLine(box.Period.Id, box.Mari.Id, new DateOnly(2026, 10, 20), 100m, "Mari Maasikas", "lisatasu", "Palgad");
        box.Declarer.Build(box.Period.Id);
        box.Submitter.Export(box.Period.Id, box.Folder);
        box.Submitter.MarkUploaded(box.Period.Id);
        var lines = box.Lines;
        Assert.Equal(2, lines.Count);

        // one line settled as refused by hand, the other still awaiting its verdict
        box.Submitter.Resolve(lines[0].Id, onServer: false);
        Assert.Equal([PaymentStatus.Rejected, PaymentStatus.Sent], box.Lines.Select(l => l.Status));

        var refused = Assert.Throws<DeclareException>(() => box.Submitter.MarkConfirmed(box.Period.Id));
        Assert.Equal(PeriodSubmitter.LinesPending, refused.Code);
        Assert.Contains("await a verdict", refused.Message);
        Assert.Contains("refused by MTA", refused.Message); // both said in the one question, not one after the other
        Assert.EndsWith("or confirm anyway.", refused.Message);

        box.Submitter.MarkConfirmed(box.Period.Id, force: true);
        Assert.Equal(PeriodState.Confirmed, box.Store.Periods(box.Company.Id).Single().State);
    }

    [Fact]
    public void A_refused_payments_line_that_used_the_months_exemption_holds_the_persons_other_payments_back()
    {
        using var box = Ready();
        box.Declarer.Build(box.Period.Id);
        box.Submitter.Export(box.Period.Id, box.Folder);
        box.Submitter.MarkUploaded(box.Period.Id);
        box.Submitter.Resolve(box.Lines.Single().Id, onServer: true);
        box.Submitter.MarkConfirmed(box.Period.Id);
        box.Submitter.Reopen(box.Period.Id);
        Assert.True(box.Lines.Single().Result!.ExemptionUsed > 0m);

        // a second payment by hand: the salary's line stands (it takes the whole exemption), the hand line gets an R draft
        box.Workspace.AddHandLine(box.Period.Id, box.Mari.Id, new DateOnly(2026, 10, 20), 100m, "Mari Maasikas", "lisatasu", "Palgad");
        var both = box.Declarer.Build(box.Period.Id);
        Assert.Equal((1, 1), (both.Count(LineChange.Kept), both.Count(LineChange.New)));
        Assert.Equal(1, box.Lines.Count(l => l.Status == PaymentStatus.Draft));

        // the salary refused by the review (a type no table knows), its line standing on the server with the exemption: the
        // hand line's draft goes — it was allocated beside the salary, and would go out with the month's exemption once more;
        // nothing on the server is cancelled, nothing is allocated
        box.Workspace.SetTypeCode(box.Salary, "999");
        Assert.False(box.Declarer.DraftsCurrent(box.Period.Id)); // straight to the export: the check sees the draft a build would drop, and asks
        Assert.Equal(1, box.Lines.Count(l => l.Status == PaymentStatus.Draft)); // the dry run undone
        var report = box.Declarer.Build(box.Period.Id);
        var mari = Assert.Single(report.Persons, p => p.PersonId == box.Mari.Id);
        Assert.Contains(mari.Problems, p => p.StartsWith(PeriodDeclarer.NotAllocated, StringComparison.Ordinal));
        Assert.Equal((0, 1, 0), (report.Count(LineChange.New), report.Count(LineChange.Deleted), report.Count(LineChange.Cancelled)));
        Assert.Equal(PaymentStatus.Accepted, Assert.Single(box.Lines).Status);
        Assert.True(box.Declarer.DraftsCurrent(box.Period.Id)); // nothing to export that the review does not say

        // the salary put right: both payments are built again, the exemption shared between them
        box.Workspace.SetTypeCode(box.Salary, null);
        report = box.Declarer.Build(box.Period.Id);
        Assert.DoesNotContain(report.Persons.SelectMany(p => p.Problems), p => p.StartsWith(PeriodDeclarer.NotAllocated, StringComparison.Ordinal));
        Assert.Equal(2, box.Lines.Count);
    }

    [Fact]
    public void Facts_the_registers_gave_for_another_month_are_noted_on_the_build()
    {
        using var box = Ready();
        box.Store.UpdatePerson(box.Mari with { ResidencySource = LookupSource.Emta, FactsControlDate = new DateOnly(2026, 9, 15) });
        var mari = Assert.Single(box.Declarer.Build(box.Period.Id).Persons, p => p.PersonId == box.Mari.Id);
        Assert.Contains(mari.Problems, p => p.StartsWith(PeriodDeclarer.FactsOfAnotherMonth, StringComparison.Ordinal));
        Assert.Single(box.Lines); // a note, not a refusal

        box.Store.UpdatePerson(box.Mari with { FactsControlDate = new DateOnly(2026, 10, 31) });
        mari = Assert.Single(box.Declarer.Build(box.Period.Id).Persons, p => p.PersonId == box.Mari.Id);
        Assert.DoesNotContain(mari.Problems, p => p.StartsWith(PeriodDeclarer.FactsOfAnotherMonth, StringComparison.Ordinal));
    }

    [Fact]
    public void Facts_the_registers_gave_for_another_day_of_the_month_than_the_reviews_are_noted_on_the_build()
    {
        using var box = Ready();
        var due = LookupRunner.ControlDatesOf(box.Store, box.Period)![box.Mari.Id]; // her latest payout of the month, as the review stands
        Assert.NotEqual(new DateOnly(2026, 10, 31), due);

        // checked for the month's last day (a check made before the search), paid earlier: noted, not refused
        box.Store.UpdatePerson(box.Mari with { ResidencySource = LookupSource.Emta, FactsControlDate = new DateOnly(2026, 10, 31) });
        var mari = Assert.Single(box.Declarer.Build(box.Period.Id).Persons, p => p.PersonId == box.Mari.Id);
        var note = Assert.Single(mari.Problems, p => p.StartsWith(PeriodDeclarer.FactsOfAnotherDay, StringComparison.Ordinal));
        Assert.Contains("31.10.2026", note);
        Assert.Contains(due.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture), note);
        Assert.Single(box.Lines);

        // checked for her day: nothing to say; and the day follows the review at the next build (a line added later in the month)
        box.Store.UpdatePerson(box.Mari with { ResidencySource = LookupSource.Emta, FactsControlDate = due });
        mari = Assert.Single(box.Declarer.Build(box.Period.Id).Persons, p => p.PersonId == box.Mari.Id);
        Assert.DoesNotContain(mari.Problems, p => p.StartsWith(PeriodDeclarer.FactsOfAnotherDay, StringComparison.Ordinal));
        box.Workspace.AddHandLine(box.Period.Id, box.Mari.Id, due.AddDays(3), 50m, "x", "käsitsi", "Palgad");
        mari = Assert.Single(box.Declarer.Build(box.Period.Id).Persons, p => p.PersonId == box.Mari.Id);
        Assert.Contains(mari.Problems, p => p.StartsWith(PeriodDeclarer.FactsOfAnotherDay, StringComparison.Ordinal));

        // facts set by hand are nobody's day: nothing is said
        box.Store.UpdatePerson(box.Mari with { ResidencySource = LookupSource.Manual, PensionSource = LookupSource.Manual, FactsControlDate = new DateOnly(2026, 10, 31) });
        mari = Assert.Single(box.Declarer.Build(box.Period.Id).Persons, p => p.PersonId == box.Mari.Id);
        Assert.DoesNotContain(mari.Problems, p => p.StartsWith(PeriodDeclarer.FactsOfAnotherDay, StringComparison.Ordinal));
    }

    [Fact]
    public void A_person_looked_up_before_the_control_date_was_kept_is_noted_by_the_day_of_the_check()
    {
        using var box = Ready();
        box.Store.UpdatePerson(box.Mari with { ResidencySource = LookupSource.Emta, ResidencyCheckedOn = new DateOnly(2026, 9, 15), FactsControlDate = null });
        Assert.Contains(Assert.Single(box.Declarer.Build(box.Period.Id).Persons, p => p.PersonId == box.Mari.Id).Problems, p => p.StartsWith(PeriodDeclarer.FactsOfAnotherMonth, StringComparison.Ordinal) && p.Contains("15.09.2026", StringComparison.Ordinal));

        // checked in the month, or after it (the registers are asked when the month is declared): the month's own, no note
        foreach (var checkedOn in new[] { new DateOnly(2026, 10, 2), new DateOnly(2026, 11, 3) })
        {
            box.Store.UpdatePerson(box.Mari with { ResidencySource = LookupSource.Emta, ResidencyCheckedOn = checkedOn, FactsControlDate = null });
            Assert.DoesNotContain(Assert.Single(box.Declarer.Build(box.Period.Id).Persons, p => p.PersonId == box.Mari.Id).Problems, p => p.StartsWith(PeriodDeclarer.FactsOfAnotherMonth, StringComparison.Ordinal));
        }

        // the earlier of the two register facts' days counts: a pension checked before the month is noted
        box.Store.UpdatePerson(box.Mari with { ResidencySource = LookupSource.Emta, ResidencyCheckedOn = new DateOnly(2026, 11, 3), PensionSource = LookupSource.Pensionikeskus, PensionCheckedOn = new DateOnly(2026, 8, 30), FactsControlDate = null });
        Assert.Contains(Assert.Single(box.Declarer.Build(box.Period.Id).Persons, p => p.PersonId == box.Mari.Id).Problems, p => p.StartsWith(PeriodDeclarer.FactsOfAnotherMonth, StringComparison.Ordinal) && p.Contains("30.08.2026", StringComparison.Ordinal));
    }

    [Fact]
    public void An_included_foreign_A1_person_without_an_employment_is_told_why_no_zero_line_is_made()
    {
        using var box = Ready(minimum: true, mariEnded: true);
        box.Workspace.SetDeclare(box.Salary, false);
        box.Store.UpdatePerson(box.Mari with { Declaration = DeclarationChoice.Include, A1 = A1Case.ForeignA1WorkInEstonia, A1Country = "FI" });
        var mari = Assert.Single(box.Declarer.Build(box.Period.Id).Persons, p => p.PersonId == box.Mari.Id);
        Assert.Contains(mari.Problems, p => p.StartsWith(PeriodDeclarer.Included, StringComparison.Ordinal) && p.Contains("A1 case carries no Estonian social tax", StringComparison.Ordinal));
        Assert.DoesNotContain(mari.Problems, p => p.StartsWith(PeriodDeclarer.ZeroLine, StringComparison.Ordinal));
        Assert.DoesNotContain(box.Lines, l => l.Status == PaymentStatus.Draft);
    }

    [Fact]
    public void A_foreign_A1_person_with_no_pay_gets_no_zero_line_for_a_minimum_they_do_not_owe()
    {
        using var box = Ready(minimum: true);
        box.Workspace.SetDeclare(box.Salary, false);
        Assert.Contains(box.Declarer.Build(box.Period.Id).Persons.Single(p => p.PersonId == box.Mari.Id).Problems, p => p.StartsWith(PeriodDeclarer.ZeroLine, StringComparison.Ordinal));

        box.Store.UpdatePerson(box.Mari with { A1 = A1Case.ForeignA1WorkInEstonia, A1Country = "FI" });
        var report = box.Declarer.Build(box.Period.Id);
        Assert.DoesNotContain(report.Persons.Single(p => p.PersonId == box.Mari.Id).Problems, p => p.StartsWith(PeriodDeclarer.ZeroLine, StringComparison.Ordinal));
        Assert.DoesNotContain(box.Lines, l => l.Status == PaymentStatus.Draft); // no Estonian social tax under a foreign A1: no minimum, no zero line
    }

    [Fact]
    public void A_non_resident_without_the_country_of_residence_is_declared_and_the_build_notes_it()
    {
        using var box = Ready();
        box.Workspace.SavePerson(box.Mari with { Residency = Residency.NonResident, ResidenceCountry = null }, confirmResidency: true);
        Assert.Empty(box.Salary.Checks); // the country is no refusal

        var mari = Assert.Single(box.Declarer.Build(box.Period.Id).Persons, p => p.PersonId == box.Mari.Id);
        Assert.Contains(mari.Problems, p => p.StartsWith(PeriodDeclarer.NoResidenceCountry, StringComparison.Ordinal));
        Assert.Single(box.Lines);

        box.Workspace.SavePerson(box.Mari with { ResidenceCountry = "LV" });
        mari = Assert.Single(box.Declarer.Build(box.Period.Id).Persons, p => p.PersonId == box.Mari.Id);
        Assert.DoesNotContain(mari.Problems, p => p.StartsWith(PeriodDeclarer.NoResidenceCountry, StringComparison.Ordinal));
        Assert.Single(box.Lines); // the draft built again in place
    }

    [Fact]
    public void Income_the_month_does_not_declare_is_listed_on_the_person_with_why_and_whether_it_was_chosen()
    {
        using var box = Ready();
        ReviewPerson Person(int i) => box.Workspace.Load(box.Period.Id).Persons[i];
        Assert.Empty(Person(0).Undeclared); // Mari's salary is declared
        Assert.Empty(Person(1).Undeclared); // Jaan's payment is of another month: not this month's income

        // ticked by the accountant all the same: the build refuses it, and the person says so
        box.Workspace.SetDeclare(Person(1).Payments.Single(), true);
        var refused = Assert.Single(Person(1).Undeclared);
        Assert.Equal((Undeclared.Refused, false), (refused.Reason, refused.ByChoice));
        Assert.Contains(ReviewChecks.NotInPeriod, refused.Codes);

        // unticked, or the person left out on the card: the accountant's own choice
        box.Workspace.SetDeclare(box.Salary, false);
        var unticked = Assert.Single(Person(0).Undeclared);
        Assert.Equal((Undeclared.Unticked, true, box.Salary.Amount), (unticked.Reason, unticked.ByChoice, unticked.Payment.Amount));
        box.Workspace.SetDeclare(box.Salary, null);
        box.Workspace.SavePerson(box.Mari with { Declaration = DeclarationChoice.Exclude });
        Assert.Equal((Undeclared.Excluded, true), (Person(0).Undeclared.Single().Reason, Person(0).Undeclared.Single().ByChoice));

        // the residency not checked: declared by default, refused, not chosen
        box.Workspace.SavePerson(box.Mari with { Declaration = DeclarationChoice.Auto });
        box.Store.UpdatePerson(box.Mari with { ResidencySource = LookupSource.Unknown, ResidencyCheckedOn = null });
        var notChecked = Assert.Single(Person(0).Undeclared);
        Assert.False(notChecked.ByChoice);
        Assert.Contains(ReviewChecks.ResidencyNotChecked, notChecked.Codes);
    }

    [Fact]
    public void Under_the_totals_cut_a_refused_payment_holds_its_types_other_payments_and_the_person_lists_them()
    {
        using var box = Ready();
        // a salary of November added by hand and ticked: refused (another month), of the salary's own type 10
        box.Workspace.AddHandLine(box.Period.Id, box.Mari.Id, new DateOnly(2026, 11, 2), 100m, "Mari Maasikas", "lisatasu", "Palgad");
        box.Workspace.SetDeclare(box.Workspace.Load(box.Period.Id).Persons[0].Payments.Single(p => p.IsByHand), true);

        // payment by payment the bank salary goes on its own
        Assert.Equal([Undeclared.Refused], box.Workspace.Load(box.Period.Id).Persons[0].Undeclared.Select(u => u.Reason));

        // the totals cut holds the type: the bank salary is listed as held, and the build declares nothing of type 10
        box.Workspace.TotalsPerType = true;
        var undeclared = box.Workspace.Load(box.Period.Id).Persons[0].Undeclared;
        var held = Assert.Single(undeclared, u => u.Reason == Undeclared.Held);
        Assert.Equal((box.Salary.RowId, false, Undeclared.TypeHeld), (held.Payment.RowId, held.ByChoice, Assert.Single(held.Codes)));
        box.Declarer.Build(box.Period.Id, DeclarationMode.TotalsPerType);
        Assert.Empty(box.Lines);
    }

    [Fact]
    public void The_declarations_totals_add_up_the_standing_lines_net_and_taxes_and_leave_out_a_T_and_a_refused_line()
    {
        using var box = Ready();
        box.Workspace.AddHandLine(box.Period.Id, box.Mari.Id, new DateOnly(2026, 10, 15), 120m, "Mari Maasikas", "haigusleht", "Haigusleht");
        box.Declarer.Build(box.Period.Id);
        Assert.Equal(2, box.Lines.Count);

        var totals = DeclarationTotals.Of(box.Lines);
        var results = box.Lines.Select(l => l.Result!).ToList();
        Assert.Equal((2, 905.92m + 120m), (totals.Lines, totals.Net));
        Assert.Equal(results.Sum(r => r.IncomeTax), totals.IncomeTax);
        Assert.Equal(results.Sum(r => r.SocialTax), totals.SocialTax);
        Assert.Equal(results.Sum(r => r.UnemploymentEmployee + r.UnemploymentEmployer + r.FundedPension), totals.UnemploymentEmployee + totals.UnemploymentEmployer + totals.FundedPension);
        Assert.Equal(results.Sum(r => r.IncomeTax + r.SocialTax + r.UnemploymentEmployee + r.UnemploymentEmployer + r.FundedPension), totals.Taxes);
        Assert.True(totals.Taxes > 0m);

        // a line refused by MTA, and a T taking a line back, owe nothing
        var salary = box.Lines.Single(l => l.SourceRowId is not null);
        Assert.False(DeclarationTotals.Counts(salary with { Status = PaymentStatus.Rejected }));
        Assert.False(DeclarationTotals.Counts(salary with { Status = PaymentStatus.Cancelled }));
        Assert.False(DeclarationTotals.Counts(salary with { Action = LineAction.T, Result = null }));
        Assert.True(DeclarationTotals.Counts(salary with { Status = PaymentStatus.Accepted }));
        Assert.Equal(DeclarationTotals.None, DeclarationTotals.Of([]));

        // the salary taken out after the file went out: its T stands, the totals are the hand line's alone
        box.Submitter.Export(box.Period.Id, box.Folder);
        box.Submitter.MarkUploaded(box.Period.Id);
        foreach (var line in box.Lines)
        {
            box.Submitter.Resolve(line.Id, onServer: true);
        }

        box.Submitter.MarkConfirmed(box.Period.Id);
        box.Submitter.Reopen(box.Period.Id);
        box.Workspace.SetDeclare(box.Salary, false);
        box.Declarer.Build(box.Period.Id);
        Assert.Contains(box.Lines, l => l.Action == LineAction.T);
        var after = DeclarationTotals.Of(box.Lines);
        Assert.Equal((1, 120m), (after.Lines, after.Net));
    }

    [Fact]
    public void A_non_resident_added_by_hand_counts_as_checked()
    {
        using var box = Ready();
        var added = box.Workspace.AddPerson(box.Company.Id, "Anna", "Kask", null, null, Residency.NonResident);
        Assert.Equal((LookupSource.Manual, (DateOnly?)new DateOnly(2026, 11, 4)), (added.ResidencySource, added.ResidencyCheckedOn));
        var resident = box.Workspace.AddPerson(box.Company.Id, "Peeter", "Kask", "50001010006", null);
        Assert.Equal((LookupSource.Unknown, (DateOnly?)null), (resident.ResidencySource, resident.ResidencyCheckedOn)); // a resident is asked of EMTA
    }

    [Fact]
    public void A_v8_store_upgrades_to_v9_and_a_persons_facts_have_no_control_date_until_the_registers_are_asked()
    {
        var path = Path.Combine(Path.GetTempPath(), $"satellestsd-{Guid.NewGuid():N}.db");
        try
        {
            Guid companyId;
            using (var v8 = SqliteStore.OpenAtVersion(path, 8))
            {
                var company = v8.AddCompany("10235688", "Näidis OÜ");
                companyId = company.Id;
                v8.ExecuteRaw($"INSERT INTO person (id, company_id, first_name, last_name, residency, on_pension, exemption_applied, minimum_applies, a1_case, residency_source, pension_source, declaration, card_type) VALUES ('{Guid.NewGuid():N}', '{company.Id:N}', 'Mari', 'Maasikas', 'Resident', 0, 1, 1, 'None', 'Emta', 'Unknown', 'Auto', 0)");
            }

            using var v9 = SqliteStore.Open(path);
            Assert.Equal((long)SqliteStore.SchemaVersion, v9.ScalarRaw<long>("SELECT MAX(version) FROM schema_version"));
            var person = Assert.Single(v9.Persons(companyId));
            Assert.Null(person.FactsControlDate);
            v9.UpdatePerson(person with { FactsControlDate = new DateOnly(2026, 10, 31) });
            Assert.Equal(new DateOnly(2026, 10, 31), Assert.Single(v9.Persons(companyId)).FactsControlDate);
        }
        finally
        {
            File.Delete(path);
            File.Delete(SqliteStore.BackupPathFor(path));
        }
    }

    [Fact]
    public void A_reports_findings_on_no_line_come_back_with_what_it_did()
    {
        using var box = Ready();
        box.Declarer.Build(box.Period.Id);
        var export = box.Submitter.Export(box.Period.Id, box.Folder);
        var outcome = Assert.Single(box.Submitter.ReadFeedback(export.Submission.Id, Path.Combine(AppContext.BaseDirectory, "Fixtures", "feedback", "APA_TSD_20260330_tagasisideraport_faili_enda_veaga.xml")));
        var general = Assert.Single(outcome.General);
        Assert.Equal("KR_VALIDATE_XBRL_FILE_7", general.Validation.Code);
    }

    [Fact]
    public void A_feedback_report_is_read_onto_the_submissions_lines_and_a_confirmation_report_is_only_answered()
    {
        using var box = Ready();
        box.Declarer.Build(box.Period.Id);
        var export = box.Submitter.Export(box.Period.Id, box.Folder);
        var key = box.Lines.Single().LineNumber;
        var reportPath = Path.Combine(box.Folder, "tagasiside.xml");
        File.WriteAllText(reportPath, $"""
            <processing result="OK">
              <entryHeader>
                <entryNumber>EE0201001</entryNumber>
                <validations/>
                <responseData>
                  <entryDetail>
                    <lineReferences><lineReference><lineInternalId>{key}</lineInternalId><lineNumberCounter>1</lineNumberCounter></lineReference></lineReferences>
                    <data><code>TSD_SM</code><description>Sotsiaalmaks</description><info>330,00</info></data>
                  </entryDetail>
                </responseData>
              </entryHeader>
            </processing>
            """, Encoding.UTF8);

        var outcomes = box.Submitter.ReadFeedback(export.Submission.Id, reportPath);
        var outcome = Assert.Single(outcomes);
        Assert.Equal((ReportKind.Submission, 1), (outcome.Kind, outcome.Recorded));
        Assert.Equal(1, outcome.Statuses[PaymentStatus.Accepted]);
        var line = box.Declarer.Lines(box.Period.Id).Single();
        Assert.Equal((PaymentStatus.Accepted, 1), (line.Payment.Status, line.Versions));
        Assert.Equal(330m, line.LastFeedback!.ServerTaxes["TSD_SM"]);
        Assert.Equal(ProcessingResult.Ok, box.Store.Submissions(box.Period.Id).Single().Result);

        var confirmPath = Path.Combine(box.Folder, "kinnitamine.xml");
        File.WriteAllText(confirmPath, """
            <processing result="OK">
              <entryHeader>
                <validations><validation><errorLevel>INFO</errorLevel><errorCode>OK</errorCode><errorDescription>kinnitatud</errorDescription></validation></validations>
                <responseData><entryDetail><data><code>110</code><description>Tulumaks</description><info>58,08</info></data></entryDetail></responseData>
              </entryHeader>
            </processing>
            """, Encoding.UTF8);
        var answered = Assert.Single(box.Submitter.ReadFeedback(export.Submission.Id, confirmPath));
        Assert.Equal((ReportKind.Confirmation, 0), (answered.Kind, answered.Recorded));
        Assert.NotNull(answered.Confirmation);
    }

    [Fact]
    public void A_v5_store_upgrades_to_v6_and_a_hand_lines_link_survives_every_version()
    {
        var path = Path.Combine(Path.GetTempPath(), $"satellestsd-{Guid.NewGuid():N}.db");
        try
        {
            Guid periodId;
            using (var v5 = SqliteStore.OpenAtVersion(path, 5))
            {
                var company = v5.AddCompany("10235688", "Näidis OÜ");
                periodId = v5.GetOrCreatePeriod(company.Id, 2026, 10).Id;
                Assert.Equal(5L, v5.ScalarRaw<long>("SELECT MAX(version) FROM schema_version"));
            }

            using var v6 = SqliteStore.Open(path);
            Assert.Equal((long)SqliteStore.SchemaVersion, v6.ScalarRaw<long>("SELECT MAX(version) FROM schema_version"));
            var company6 = v6.FindCompany("10235688")!;
            var person = v6.AddPerson(Person.New(company6.Id, "Mari", "Maasikas", ReviewFixtures.MariCode) with { FundedPensionMember = false, ResidencyCheckedOn = Today, BasicExemptionApplied = true });
            var handLineId = Guid.NewGuid();
            var facts = person.FactsFor(2026);
            var engine = new TaxEngine(TaxRates.Year2026);
            var type = PaymentTypes.Get("10");
            var result = engine.Calculate(new TaxInput(type, 1000m, false, 700m), facts);
            var key = v6.NewLineNumber(periodId, Section.EE0201001);
            var line = DeclarationLineFactory.Create(key, 0, LineAction.R, type, facts, person.PayeeId!, new DateOnly(2026, 10, 10), result);
            var draft = v6.AddPayment(StoredPayment.New(periodId, person.Id, Section.EE0201001, line, facts, result, 1000m, "by hand", null, handLineId));
            Assert.Equal(handLineId, draft.ReviewLineId);
            Assert.Equal(handLineId, v6.Payments(periodId).Single().ReviewLineId);
            var replaced = v6.ReplaceDraft(draft.Id, StoredPayment.New(periodId, person.Id, Section.EE0201001, line, facts, result, 1000m, "by hand"));
            Assert.Equal(handLineId, replaced.ReviewLineId); // kept through a replacement without one
            v6.AddSubmission(periodId, "f.xml", "u", [draft.Id], DateTime.UtcNow);
            Assert.Contains("another hand line", Assert.Throws<InvalidOperationException>(() => v6.AddPayment(StoredPayment.New(periodId, person.Id, Section.EE0201001, PaymentLine.Cancellation(key, 0), null, null, null, null, null, Guid.NewGuid()))).Message);
            var m = v6.AddPayment(StoredPayment.New(periodId, person.Id, Section.EE0201001, DeclarationLineFactory.Create(key, 0, LineAction.M, type, facts, person.PayeeId!, new DateOnly(2026, 10, 10), result), facts, result, 1000m, "by hand"));
            Assert.Equal(handLineId, m.ReviewLineId); // inherited by the next version
        }
        finally
        {
            File.Delete(path);
        }
    }
}
