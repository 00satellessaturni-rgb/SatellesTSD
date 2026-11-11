using SatellesTSD.Core.Declare;
using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Review;
using SatellesTSD.Core.Store;
using SatellesTSD.Core.Tax;
using SatellesTSD.Infrastructure;

namespace SatellesTSD.Tests;

/// <summary>A statement row's payment split into parts by a smaller amount typed on it (decision 150): each part a
/// payment of its own, with its own amount, category, type, person and declaration; the row's own share what the parts
/// leave; a part resized or removed by its amount; and the declaration's lines one per part.</summary>
public class RowSplitTests
{
    private sealed class FixedClock(DateTime at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(at, DateTimeKind.Utc), TimeSpan.Zero);

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static (SqliteStore Store, Period Period, ReviewWorkspace Workspace, PeriodDeclarer Declarer) Ready()
    {
        var (store, _, period, _) = ReviewFixtures.Setup();
        new MatchRunner(store).Run(period.Id);
        var clock = new FixedClock(new DateTime(2026, 11, 4, 10, 0, 0));
        var workspace = new ReviewWorkspace(store, TaxRates.Year2026, clock);
        var mari = workspace.Load(period.Id).Persons[0].Person!;
        workspace.SavePerson(mari with { FundedPensionMember = false, BasicExemptionApplied = true, SocialTaxMinimumApplies = false }, confirmResidency: true);
        return (store, period, workspace, new PeriodDeclarer(store, TaxRates.Year2026, clock));
    }

    private static List<ReviewPayment> Rows(ReviewWorkspace workspace, Period period) =>
        workspace.Load(period.Id).Persons[0].Payments.Where(p => !p.IsByHand).OrderBy(p => p.Part).ToList();

    [Fact]
    public void A_smaller_amount_on_a_row_splits_the_difference_off_as_a_part_of_its_own()
    {
        var (store, period, workspace, _) = Ready();
        using (store)
        {
            var salary = Assert.Single(Rows(workspace, period));
            Assert.Equal((0, 905.92m, 905.92m, false), (salary.Part, salary.Amount, salary.RowAmount, salary.IsPart));
            var reference = salary.Reference;

            // the question's figures before anything is stored, then the split
            var plan = workspace.PlanAmount(salary, 800m)!;
            Assert.Equal((AmountChangeKind.SplitOff, 905.92m, 800m, 800m, 105.92m), (plan.Kind, plan.From, plan.To, plan.RowShare, plan.Difference));
            Assert.Empty(store.ReviewLines(period.Id).Where(l => l.IsPart));
            Assert.Equal(plan, workspace.SetAmount(salary, 800m));

            var parts = Rows(workspace, period);
            Assert.Equal([(1, 800m), (2, 105.92m)], parts.Select(p => (p.Part, p.Amount)));
            Assert.All(parts, p => Assert.Equal((salary.RowId, 905.92m), (p.RowId, p.RowAmount)));
            Assert.Equal(["Palgad", null], parts.Select(p => p.Category)); // the row's own share keeps its category, the part has none
            Assert.Equal((true, false), (parts[0].Declare, parts[1].Declare)); // nor a type: not declared by default
            Assert.Equal(parts[0].PersonId, parts[1].PersonId); // the row's person, all the same
            Assert.Equal([reference + " osa 1", reference + " osa 2"], parts.Select(p => p.Reference));
            Assert.NotNull(parts[1].LineId);
            Assert.True(parts[1].IsPart);
            Assert.Equal(800m, workspace.Load(period.Id).Persons[0].SalaryTotal); // the salary is the row's own share alone

            // the part given its own category
            workspace.SetCategory(parts[1], "Kompensatsioon");
            parts = Rows(workspace, period);
            Assert.Equal(["Palgad", "Kompensatsioon"], parts.Select(p => p.Category));
            Assert.Equal(800m, workspace.Load(period.Id).Persons[0].SalaryTotal);
            Assert.Equal([true, false], parts.Select(p => p.Declare)); // compensation has no type of its own: not declared by default

            // a note on each lands on each
            workspace.SetNote(parts[0], "palk");
            workspace.SetNote(Rows(workspace, period)[1], "hüvitis");
            Assert.Equal(["palk", "hüvitis"], Rows(workspace, period).Select(p => p.Note));
        }
    }

    [Fact]
    public void A_parts_category_is_the_accountants_word_and_never_follows_the_search()
    {
        var (store, period, workspace, _) = Ready();
        using (store)
        {
            // a row the search gave no category to (a fee without a payee): the part split off it is stored as "none", the
            // accountant's word, not as "whatever the search says" — a later search that finds a category leaves it none
            var fee = workspace.Load(period.Id).Unmatched.Payments.Single(p => p.Amount == 0.38m);
            Assert.Null(fee.DefaultCategory);
            workspace.SetAmount(fee, 0.30m);
            var stored = Assert.Single(store.ReviewLines(period.Id), l => l.IsPart);
            Assert.Equal((null, true), (stored.Category, stored.NoCategory));

            // a category given and taken away again: none once more, still the accountant's word
            var part = workspace.Load(period.Id).Unmatched.Payments.Single(p => p.IsPart);
            workspace.SetCategory(part, "Üür");
            part = workspace.Load(period.Id).Unmatched.Payments.Single(p => p.IsPart);
            Assert.Equal("Üür", part.Category);
            workspace.SetCategory(part, null);
            stored = Assert.Single(store.ReviewLines(period.Id), l => l.IsPart);
            Assert.Equal((null, true), (stored.Category, stored.NoCategory));

            // a part given the very category the search gives its row holds it by name: it is the part's own, whatever a later search says
            var salary = Assert.Single(Rows(workspace, period));
            workspace.SetAmount(salary, 800m);
            var split = Rows(workspace, period)[1];
            Assert.Equal("Palgad", split.DefaultCategory);
            workspace.SetCategory(split, "Palgad");
            Assert.Equal(("Palgad", false), store.ReviewLines(period.Id).Where(l => l.IsPart && l.RowId == salary.RowId).Select(l => (l.Category, l.NoCategory)).Single());
            workspace.SetCategory(Rows(workspace, period)[1], null);
            Assert.Equal((null, true), store.ReviewLines(period.Id).Where(l => l.IsPart && l.RowId == salary.RowId).Select(l => (l.Category, l.NoCategory)).Single());
        }
    }

    [Fact]
    public void A_part_saved_by_an_earlier_version_as_following_the_search_is_settled_once_as_it_shows_now()
    {
        var (store, period, workspace, _) = Ready();
        using (store)
        {
            // as 0.26–0.28 saved them: no category of its own and not marked none
            var fee = workspace.Load(period.Id).Unmatched.Payments.Single(p => p.Amount == 0.38m);
            var salary = Assert.Single(Rows(workspace, period));
            store.SaveReviewLine(ReviewLine.PartOf(period.Id, fee.RowId!.Value, 0.08m, noCategory: false));
            store.SaveReviewLine(ReviewLine.PartOf(period.Id, salary.RowId!.Value, 100m, noCategory: false));
            Assert.All(store.ReviewLines(period.Id).Where(l => l.IsPart), l => Assert.Equal((null, false), (l.Category, l.NoCategory)));

            // the first load settles both: none where the search gives the row no category, the search's category by name where it does
            var view = workspace.Load(period.Id);
            Assert.Null(view.Unmatched.Payments.Single(p => p.IsPart).Category);
            Assert.Equal("Palgad", Rows(workspace, period)[1].Category);
            var stored = store.ReviewLines(period.Id).Where(l => l.IsPart).ToDictionary(l => l.RowId!.Value);
            Assert.Equal((null, true), (stored[fee.RowId!.Value].Category, stored[fee.RowId!.Value].NoCategory));
            Assert.Equal(("Palgad", false), (stored[salary.RowId!.Value].Category, stored[salary.RowId!.Value].NoCategory));

            // settled lines are left alone by the next load
            workspace.Load(period.Id);
            Assert.Equal(stored.Values.OrderBy(l => l.Id), store.ReviewLines(period.Id).Where(l => l.IsPart).OrderBy(l => l.Id));
        }
    }

    [Fact]
    public void A_part_takes_from_the_rows_share_or_gives_back_and_goes_at_zero()
    {
        var (store, period, workspace, _) = Ready();
        using (store)
        {
            workspace.SetAmount(Rows(workspace, period)[0], 800m);
            var part = Rows(workspace, period)[1];

            // more on the part comes from the row's own share; less goes back to it
            var more = workspace.SetAmount(part, 300m)!;
            Assert.Equal((AmountChangeKind.PartResized, 105.92m, 300m, 605.92m, 194.08m), (more.Kind, more.From, more.To, more.RowShare, more.Difference));
            Assert.Equal([605.92m, 300m], Rows(workspace, period).Select(p => p.Amount));
            var less = workspace.SetAmount(Rows(workspace, period)[1], 100m)!;
            Assert.Equal((805.92m, -200m), (less.RowShare, less.Difference));

            // the row's own share given less again: a third part
            workspace.SetAmount(Rows(workspace, period)[0], 700m);
            Assert.Equal([(1, 700m), (2, 100m), (3, 105.92m)], Rows(workspace, period).Select(p => (p.Part, p.Amount)));

            // what is refused: the row's share given 0 or more than it has, a part leaving the share nothing, no cents
            var refused = new[]
            {
                () => workspace.PlanAmount(Rows(workspace, period)[0], 0m),
                () => workspace.PlanAmount(Rows(workspace, period)[0], 905.92m),
                () => workspace.PlanAmount(Rows(workspace, period)[1], 800m),
                () => workspace.PlanAmount(Rows(workspace, period)[1], 10.005m),
                () => workspace.PlanAmount(Rows(workspace, period)[1], -1m),
            };
            Assert.Equal(
                [(ReviewWorkspace.SplitOwnShare, 700m), (ReviewWorkspace.SplitOwnShare, 700m), (ReviewWorkspace.SplitPartTooBig, 799.99m), (ReviewWorkspace.AmountInvalid, null), (ReviewWorkspace.AmountInvalid, null)],
                refused.Select(plan => Assert.Throws<ReviewException>(plan)).Select(e => (e.Code, e.Figure)));
            Assert.Null(workspace.PlanAmount(Rows(workspace, period)[1], 100m)); // the amount it has: nothing to do

            // a part given 0 goes, its amount back to the row's share; the last one gone, the row is whole again
            var gone = workspace.SetAmount(Rows(workspace, period)[1], 0m)!;
            Assert.Equal((AmountChangeKind.PartRemoved, 100m, 800m), (gone.Kind, gone.From, gone.RowShare));
            Assert.Equal([(1, 800m), (2, 105.92m)], Rows(workspace, period).Select(p => (p.Part, p.Amount))); // renumbered by place
            workspace.SetAmount(Rows(workspace, period)[1], 0m);
            var whole = Assert.Single(Rows(workspace, period));
            Assert.Equal((0, 905.92m), (whole.Part, whole.Amount));
            Assert.Empty(store.ReviewLines(period.Id)); // nothing decided is left behind
        }
    }

    [Fact]
    public void Each_part_is_a_line_of_its_own_and_the_totals_cut_adds_them()
    {
        var (store, period, workspace, declarer) = Ready();
        using (store)
        {
            workspace.SetAmount(Rows(workspace, period)[0], 800m);
            var part = Rows(workspace, period)[1];
            workspace.SetTypeCode(part, "10"); // the part declared too, as salary of its own

            declarer.Build(period.Id);
            var lines = store.Payments(period.Id).OrderBy(l => l.NetPaid).ToList();
            Assert.Equal([105.92m, 800m], lines.Select(l => l.NetPaid));
            Assert.All(lines, l => Assert.Equal(part.RowId, l.SourceRowId)); // both from the row
            Assert.Equal([part.LineId, null], lines.Select(l => l.ReviewLineId)); // the part by its own line
            Assert.Equal([part.Reference, Rows(workspace, period)[0].Reference], lines.Select(l => l.SourceRef));

            // built again: the same two lines, kept
            var again = declarer.Build(period.Id);
            Assert.Equal(2, store.Payments(period.Id).Count);
            Assert.Equal(0, again.Count(LineChange.New));

            // the part removed: its line goes, the row's line takes the whole amount
            workspace.SetAmount(Rows(workspace, period)[1], 0m);
            declarer.Build(period.Id);
            Assert.Equal(905.92m, Assert.Single(store.Payments(period.Id)).NetPaid);

            // the totals cut: the parts of one type in one line, each named
            workspace.SetAmount(Rows(workspace, period)[0], 800m);
            workspace.SetTypeCode(Rows(workspace, period)[1], "10");
            declarer.Build(period.Id, DeclarationMode.TotalsPerType);
            var total = Assert.Single(store.Payments(period.Id), l => l.Line.TypeCode == "10");
            Assert.Equal(905.92m, total.NetPaid);
            Assert.EndsWith("[2] osa 1; " + ReviewFixtures.Source + " [2] osa 2", PeriodDeclarer.TotalsOf(total.SourceRef)!.Value.References, StringComparison.Ordinal);
        }
    }

    private const string RenterCode = "49001010008";

    private static (SqliteStore Store, Period Period, ReviewWorkspace Workspace, Guid Kati) Renter()
    {
        var (store, _, period, _) = ReviewFixtures.Setup(renter: "KATI KASK", renterCode: RenterCode);
        new MatchRunner(store).Run(period.Id);
        var workspace = new ReviewWorkspace(store, TaxRates.Year2026, new FixedClock(new DateTime(2026, 11, 4, 10, 0, 0)));
        var kati = Assert.Single(workspace.Load(period.Id).Persons, p => p.Person?.LastName == "Kask").PersonId!.Value;
        return (store, period, workspace, kati);
    }

    private static List<ReviewPayment> Rent(ReviewWorkspace workspace, Period period) =>
        workspace.Load(period.Id).Persons.SelectMany(p => p.Payments).Where(p => p.Payee == "KATI KASK" && !p.IsByHand).OrderBy(p => p.Part).ToList();

    [Fact]
    public void Each_share_of_a_rent_row_has_the_payee_its_own_category_calls_for_and_a_hand_lines_twin_leaves_them_all_undeclared()
    {
        var (store, period, workspace, kati) = Renter();
        using (store)
        {
            workspace.SetAmount(Rent(workspace, period)[0], 300m);
            var shares = workspace.Load(period.Id).Persons.SelectMany(p => p.Payments).Where(p => p.Payee == "KATI KASK").OrderBy(p => p.Part).ToList();
            // the part has no category: nobody's by default, the row's own share still hers
            Assert.Equal([((Guid?)kati, (Guid?)kati, "Üür", true), (null, null, null, false)], shares.Select(p => (p.PersonId, p.DefaultPersonId, p.Category, p.Declare)));
            workspace.SetCategory(shares[1], "Üür");
            shares = Rent(workspace, period);
            Assert.All(shares, p => Assert.Equal((kati, kati, "Üür", true), (p.PersonId, p.DefaultPersonId, p.Category, p.Declare)));

            // the part given a category not declared on the payee's code: nobody's by default, the row's own share still hers
            workspace.SetCategory(shares[1], "Kompensatsioon");
            shares = workspace.Load(period.Id).Persons.SelectMany(p => p.Payments).Where(p => p.Payee == "KATI KASK").OrderBy(p => p.Part).ToList();
            Assert.Equal([(Guid?)kati, null], shares.Select(p => p.PersonId));
            workspace.SetCategory(shares[1], "Üür");

            // a hand line of the row's amount declares the rent: none of the row's shares is declared by default
            workspace.AddHandLine(period.Id, kati, new DateOnly(2026, 10, 15), 400m, "KATI KASK", "üür, käsitsi", "Üür");
            Assert.All(Rent(workspace, period), p => Assert.Equal((false, false), (p.DefaultDeclare, p.Declare)));
            var problems = Assert.Single(workspace.Load(period.Id).Persons, p => p.PersonId == kati).Problems;
            Assert.Single(problems, p => p.Code == MatchProblem.PayeeHandLine); // said once for the row
        }
    }

    [Fact]
    public void A_part_left_on_a_copy_is_taken_out_with_zero_and_nothing_is_split_past_draft()
    {
        var (store, period, workspace, _) = Ready();
        using (store)
        {
            workspace.SetAmount(Rows(workspace, period)[0], 800m);
            var part = Rows(workspace, period)[1];
            var onCopy = new ReviewPayment
            {
                PeriodId = part.PeriodId, RowId = part.RowId, LineId = part.LineId, Part = part.Part, Source = part.Source, RowNo = part.RowNo, Payee = part.Payee,
                VerdictText = part.VerdictText, Amount = part.Amount, RowAmount = part.RowAmount, Flags = [MatchProblem.DuplicateRow],
            };
            Assert.Equal(ReviewWorkspace.SplitCopy, Assert.Throws<ReviewException>(() => workspace.PlanAmount(onCopy, 50m)).Code);
            Assert.Equal(AmountChangeKind.PartRemoved, workspace.SetAmount(onCopy, 0m)!.Kind);
            Assert.Single(Rows(workspace, period));

            workspace.SetAmount(Rows(workspace, period)[0], 800m);
            store.SetPeriodState(period.Id, PeriodState.Exported, new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc));
            var refused = Assert.Throws<ReviewException>(() => workspace.PlanAmount(Rows(workspace, period)[1], 50m));
            Assert.Equal(ReviewWorkspace.PeriodNotDraft, refused.Code);
            Assert.Equal([800m, 105.92m], Rows(workspace, period).Select(p => p.Amount)); // read as it stands
        }
    }

    [Fact]
    public void Parts_survive_a_rerun_and_go_with_the_file()
    {
        var (store, period, workspace, _) = Ready();
        using (store)
        {
            workspace.SetAmount(Rows(workspace, period)[0], 800m);
            new MatchRunner(store).Run(period.Id);
            Assert.Equal([800m, 105.92m], Rows(workspace, period).Select(p => p.Amount));

            // the store keeps one overlay of its own per row, whatever its parts
            var rowId = Rows(workspace, period)[0].RowId!.Value;
            var first = store.SaveReviewLine(ReviewLine.ForRow(period.Id, rowId) with { Note = "a" });
            var second = store.SaveReviewLine(ReviewLine.ForRow(period.Id, rowId) with { Note = "b" });
            Assert.Equal(first.Id, second.Id);
            Assert.Throws<ArgumentException>(() => store.SaveReviewLine(ReviewLine.PartOf(period.Id, rowId, 0m)));

            // the file taken out of the period takes the parts with it
            var file = Assert.Single(store.PeriodImports(period.Id));
            store.ClearMatchRun(period.Id);
            store.UnlinkImport(period.Id, file.Id);
            Assert.Empty(store.ReviewLines(period.Id));
        }
    }

    [Fact]
    public void A_hand_lines_amount_is_just_changed_and_a_copy_or_an_unread_amount_is_not_split()
    {
        var (store, period, workspace, _) = Ready();
        using (store)
        {
            var mari = workspace.Load(period.Id).Persons[0].Person!;
            workspace.AddHandLine(period.Id, mari.Id, new DateOnly(2026, 10, 20), 100m, "Mari Maasikas", category: "Palgad");
            var hand = workspace.Load(period.Id).Persons[0].Payments.Single(p => p.IsByHand);
            var change = workspace.SetAmount(hand, 150m)!;
            Assert.Equal((AmountChangeKind.HandLine, 100m, 150m), (change.Kind, change.From, change.To));
            Assert.Equal(150m, workspace.Load(period.Id).Persons[0].Payments.Single(p => p.IsByHand).Amount);

            var row = Rows(workspace, period)[0];
            ReviewPayment Like(IReadOnlyList<string> flags, string? amountText) => new()
            {
                PeriodId = row.PeriodId, RowId = row.RowId, Source = row.Source, RowNo = row.RowNo, Payee = row.Payee, VerdictText = row.VerdictText,
                Amount = row.Amount, RowAmount = row.RowAmount, Flags = flags, AmountText = amountText,
            };
            Assert.Equal(ReviewWorkspace.SplitCopy, Assert.Throws<ReviewException>(() => workspace.PlanAmount(Like([MatchProblem.DuplicateRow], null), 10m)).Code);
            Assert.Equal(ReviewWorkspace.SplitUnread, Assert.Throws<ReviewException>(() => workspace.PlanAmount(Like([], "x"), 10m)).Code);
        }
    }
}
