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
    private static Box Ready(bool twin = false, bool minimum = false, decimal salary = 905.92m, int mariType = 1, bool mariEnded = false, bool mariPaused = false, bool mariRestarted = false)
    {
        var (store, company, period, _) = ReviewFixtures.Setup(twin: twin, salary: salary, mariType: mariType, mariEnded: mariEnded, mariPaused: mariPaused, mariRestarted: mariRestarted);
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
        Assert.Equal(0.3333m, PeriodDeclarer.MonthShare(Entry(null, new DateOnly(2026, 10, 10)), 2026, 10));
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
        Assert.Equal(0.5333m, PeriodDeclarer.MonthShare(next, 2026, 10));
        Assert.Equal(1m, PeriodDeclarer.MonthShare(next, [Entry(new DateOnly(2024, 1, 1), new DateOnly(2026, 10, 15), "4"), next], 2026, 10));

        // a gap between them: 1–10 and 16–31 make 26 days
        Assert.Equal(0.8667m, PeriodDeclarer.MonthShare(next, [Entry(new DateOnly(2024, 1, 1), new DateOnly(2026, 10, 10), "4")], 2026, 10));

        // a day two rows cover counts once; a row of another month adds nothing
        Assert.Equal(0.5333m, PeriodDeclarer.MonthShare(next, [Entry(new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 25)), Entry(new DateOnly(2025, 1, 1), new DateOnly(2026, 9, 30), "4")], 2026, 10));

        // a paused row that does not say since when, or one whose type carries no minimum (600, VÕS), adds no day
        Assert.Equal(0.5333m, PeriodDeclarer.MonthShare(next, [Entry(new DateOnly(2024, 1, 1), new DateOnly(2026, 10, 15), "3")], 2026, 10));
        Assert.Equal(0.5333m, PeriodDeclarer.MonthShare(next, [Entry(new DateOnly(2024, 1, 1), new DateOnly(2026, 10, 15), "4", "600")], 2026, 10));

        // a paused row counts its days before the pause (1–9 here) and after its planned end, never the pause itself
        var paused = Entry(new DateOnly(2024, 1, 1), null, "3") with { PauseStart = new DateOnly(2026, 10, 10) };
        Assert.Equal(0.8333m, PeriodDeclarer.MonthShare(next, [paused], 2026, 10));
        Assert.Equal(0.8333m, PeriodDeclarer.MonthShare(next, [paused with { PauseEnd = new DateOnly(2026, 10, 20) }], 2026, 10)); // 21–31 already the new contract's
        Assert.Equal(0.7333m, PeriodDeclarer.MonthShare(Entry(new DateOnly(2026, 10, 22), null), [paused with { PauseEnd = new DateOnly(2026, 10, 18) }], 2026, 10)); // 1–9 and 19–31: 22 days

        // the entry standing for the month carries no minimum (a board seat): the others do not stretch its share
        Assert.Equal(0.7333m, PeriodDeclarer.MonthShare(Entry(new DateOnly(2026, 10, 10), null, "2", "600"), [Entry(new DateOnly(2024, 1, 1), new DateOnly(2026, 10, 15), "4")], 2026, 10)); // 22 days of its own
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
