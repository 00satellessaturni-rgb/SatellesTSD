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

/// <summary>The review of a period: the run read with its defaults, the decisions as sparse overlays that survive a
/// re-run, the rows nobody matched given to a person, the lines added by hand, the person's facts edited by hand,
/// the checks, and what a period past Draft still allows.</summary>
public class ReviewWorkspaceTests
{
    private static readonly DateOnly Today = new(2026, 11, 4);

    private sealed class FixedClock(DateOnly day) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);
    }

    private static ReviewWorkspace Workspace(SqliteStore store) => new(store, TaxRates.Year2026, new FixedClock(Today));

    private static (SqliteStore Store, Company Company, Period Period, ReviewWorkspace Workspace) Run()
    {
        var (store, company, period, _) = ReviewFixtures.Setup();
        new MatchRunner(store).Run(period.Id);
        return (store, company, period, Workspace(store));
    }

    private static ReviewPayment Salary(ReviewView view) => view.Persons[0].Payments.Single(p => !p.IsByHand);

    private static ReviewPayment Stranger(ReviewView view) => view.Unmatched.Payments.Single(p => p.Payee == "Kinnisvara OÜ");

    [Fact]
    public void Without_a_run_the_view_holds_the_files_and_the_rows_nobody_matched_is_empty()
    {
        var (store, _, period, _) = ReviewFixtures.Setup();
        using (store)
        {
            var view = Workspace(store).Load(period.Id);
            Assert.Null(view.Run);
            Assert.True(view.Editable);
            Assert.Single(view.Files);
            Assert.Empty(view.Sources);
            var only = Assert.Single(view.Persons);
            Assert.True(only.IsUnmatched);
            Assert.Empty(only.Payments);
            Assert.Equal(MatchCatalog.Default.ToJson(), view.Catalog.ToJson());
            Assert.Throws<KeyNotFoundException>(() => Workspace(store).Load(Guid.NewGuid()));
        }
    }

    [Fact]
    public void With_two_contracts_at_once_each_payment_goes_by_the_contract_of_its_day_unless_the_card_says_otherwise()
    {
        var (store, _, period, _) = ReviewFixtures.Setup(mariSecond: true);
        using (store)
        {
            new MatchRunner(store).Run(period.Id);
            var workspace = new ReviewWorkspace(store);
            var mari = workspace.Load(period.Id).Persons[0];
            Assert.Equal("600", mari.Register!.Entry.TypeId); // the month's entry: the later start of two valid ones
            workspace.AddHandLine(period.Id, mari.PersonId, new DateOnly(2026, 10, 20), 200m, "Mari Maasikas", "töövõtt", "Palgad");

            // the salary of 5 October goes by the Tööleping (1 → 10), the pay of the 20th by the VÕS leping begun on the 6th (600 → 17)
            var payments = workspace.Load(period.Id).Persons[0].Payments.OrderBy(p => p.Date).ToList();
            Assert.Equal(["10", "17"], payments.Select(p => p.DefaultTypeCode));

            // the card's type for every payment: both by the card, the register's contracts aside
            var person = workspace.Load(period.Id).Persons[0].Person!;
            workspace.SavePerson(person with { TorEmploymentTypeId = "1", TypeFromCard = true });
            Assert.Equal(["10", "10"], workspace.Load(period.Id).Persons[0].Payments.OrderBy(p => p.Date).Select(p => p.DefaultTypeCode));

            // the switch on with no type on the card changes nothing
            workspace.SavePerson(workspace.Load(period.Id).Persons[0].Person! with { TorEmploymentTypeId = null });
            Assert.Equal(["10", "17"], workspace.Load(period.Id).Persons[0].Payments.OrderBy(p => p.Date).Select(p => p.DefaultTypeCode));
        }
    }

    [Fact]
    public void A_day_between_two_contracts_goes_by_the_one_ended_last_and_an_unknown_card_type_by_the_register()
    {
        TorEntry Entry(int row, DateOnly start, DateOnly? end, string type, string state) =>
            new(row, null, ReviewFixtures.MariCode, "MARI", "MAASIKAS", "EE", start, end, type, "x", state, "x", null, null, null, null, [], null);
        var ended = Entry(1, new DateOnly(2024, 1, 1), new DateOnly(2026, 10, 15), "1", "4");
        var next = Entry(2, new DateOnly(2026, 10, 25), null, "600", "2");
        var register = new MatchPerson(SearchPerson.Of(ReviewFixtures.MariCode, "MAASIKAS", "MARI"), [ended, next], next, 0);

        // the final salary of the ended Tööleping paid on the 20th, before the VÕS leping began: the Tööleping's
        Assert.Equal("1", ReviewDefaults.EmploymentTypeFor(register, null, new DateOnly(2026, 10, 20)));
        Assert.Equal("1", ReviewDefaults.EmploymentTypeFor(register, null, new DateOnly(2026, 10, 10)));
        Assert.Equal("600", ReviewDefaults.EmploymentTypeFor(register, null, new DateOnly(2026, 10, 26))); // the payment's own day: the new contract's (decision 85)
        Assert.Equal("600", ReviewDefaults.EmploymentTypeFor(register, null, null)); // no day: the month's entry

        // the card's switch: its type when the app knows it; an unknown one leaves the register's
        var person = Person.New(Guid.NewGuid(), "Mari", "Maasikas", ReviewFixtures.MariCode) with { TorEmploymentTypeId = "601", TypeFromCard = true };
        Assert.Equal("601", ReviewDefaults.EmploymentTypeFor(register, person, new DateOnly(2026, 10, 20)));
        Assert.Equal("1", ReviewDefaults.EmploymentTypeFor(register, person with { TorEmploymentTypeId = "999" }, new DateOnly(2026, 10, 20)));
    }

    [Fact]
    public void A_run_is_read_with_its_defaults_and_the_register_persons_get_store_rows()
    {
        var (store, company, period, workspace) = Run();
        using (store)
        {
            var view = workspace.Load(period.Id);
            Assert.NotNull(view.Run);
            Assert.Equal([ReviewFixtures.Source], view.Sources);
            Assert.Equal(3, view.Persons.Count); // Mari, Jaan, the rows nobody matched
            Assert.Equal(2, store.Persons(company.Id).Count);

            var mari = view.Persons[0];
            Assert.Equal(("MAASIKAS", "MARI", ReviewFixtures.MariCode, "1"), (mari.LastName, mari.FirstName, mari.PersonalCode, mari.Person!.TorEmploymentTypeId));
            var salary = Assert.Single(mari.Payments);
            Assert.Equal($"{ReviewFixtures.Source} [2]", salary.Reference);
            Assert.Equal(("[Palgad]", "Palgad", "Palgad", "10", "10", true, true), (salary.VerdictText, salary.DefaultCategory, salary.Category, salary.DefaultTypeCode, salary.TypeCode, salary.DefaultDeclare, salary.Declare));
            Assert.Equal((905.92m, true, true, mari.PersonId, mari.PersonId, false), (salary.Amount, salary.InPeriod, salary.AmountOk, salary.DefaultPersonId, salary.PersonId, salary.HasOverride));
            Assert.Equal([ReviewChecks.ResidencyNotChecked, PaymentChecks.PensionNotChecked], salary.Checks.Select(c => c.Code));
            Assert.Equal(905.92m, mari.SalaryTotal);
            Assert.Equal(905.92m, mari.SalaryBySource[ReviewFixtures.Source]);

            var jaan = view.Persons[1];
            Assert.Equal("600", jaan.Person!.TorEmploymentTypeId);
            var next = Assert.Single(jaan.Payments);
            Assert.Equal(("kuupaev ei kuulu perioodi [2026-10]", (string?)null, (string?)null, false, false), (next.VerdictText, next.Category, next.TypeCode, next.InPeriod, next.Declare));
            Assert.Empty(next.Checks); // not declared: nothing to refuse
            Assert.Contains(jaan.Problems, p => p.Code == MatchProblem.NoEmploymentInMonth);
            Assert.Equal(0m, jaan.SalaryTotal);

            var unmatched = view.Unmatched;
            Assert.True(unmatched.IsUnmatched);
            Assert.Equal(2, unmatched.Payments.Count);
            // the fee line without a payee is listed as the workbook listed it: nobody's, no category, the amount and the date read
            var fee = unmatched.Payments[0];
            Assert.Equal(("", (DateOnly?)new DateOnly(2026, 10, 6), 0.38m, (string?)null, "", (string?)null, false, true), (fee.Payee, fee.Date, fee.Amount, fee.AmountText, fee.VerdictText, fee.Category, fee.Declare, fee.InPeriod));
            var stranger = unmatched.Payments[1];
            Assert.Equal(("Kinnisvara OÜ", (Guid?)null, false), (stranger.Payee, stranger.PersonId, stranger.Declare));
            // the direction and the currency travel from the statement, a payee-less row's too
            Assert.Equal((Direction.Debit, "EUR"), (fee.Direction, fee.Currency));
            Assert.Equal((Direction.Debit, "EUR"), (stranger.Direction, stranger.Currency));
            Assert.Equal(0, view.RowsLeftOut); // only a blank row inside the data is left out

            // a decision on a payee-less row stands like any other: a category, then the person it went to — and then
            // it is declared and built into a line from that row
            workspace.SetCategory(fee, "Üür");
            Assert.Equal("Üür", workspace.Load(period.Id).Unmatched.Payments[0].Category);
            workspace.SavePerson(mari.Person! with { FundedPensionMember = false }, confirmResidency: true);
            workspace.SetPerson(fee, mari.PersonId);
            var named = workspace.Load(period.Id).Persons[0].Payments.Single(p => p.RowId == fee.RowId);
            Assert.Equal(("Üür", "57", true), (named.Category, named.TypeCode, named.Declare));
            Assert.Empty(named.Checks);
            new PeriodDeclarer(store).Build(period.Id);
            Assert.Contains(store.Payments(period.Id), p => p.SourceRowId == fee.RowId && p.Line.TypeCode == "57" && p.NetPaid == 0.38m);
        }
    }

    [Fact]
    public void A_decision_equal_to_the_default_is_not_stored_and_an_overlay_deciding_nothing_is_deleted()
    {
        var (store, _, period, workspace) = Run();
        using (store)
        {
            var salary = Salary(workspace.Load(period.Id));
            Assert.Null(workspace.SetDeclare(salary, true));
            Assert.Empty(store.ReviewLines(period.Id));

            var line = workspace.SetDeclare(salary, false)!;
            Assert.Equal((salary.RowId, false), (line.RowId, line.Declare));
            salary = Salary(workspace.Load(period.Id));
            Assert.Equal((false, true, line.Id, true), (salary.Declare, salary.DefaultDeclare, salary.LineId, salary.HasOverride));
            Assert.Empty(salary.Checks);

            Assert.Null(workspace.SetDeclare(salary, null));
            Assert.Empty(store.ReviewLines(period.Id));

            var noted = workspace.SetNote(Salary(workspace.Load(period.Id)), "  kontrollitud ")!;
            Assert.Equal("kontrollitud", noted.Note);
            Assert.Equal("kontrollitud", Salary(workspace.Load(period.Id)).Note);
            Assert.Null(workspace.SetNote(Salary(workspace.Load(period.Id)), " "));
            Assert.Empty(store.ReviewLines(period.Id));
        }
    }

    [Fact]
    public void A_category_set_by_hand_derives_the_type_and_a_type_set_by_hand_stands_until_the_category_changes()
    {
        var (store, _, period, workspace) = Run();
        using (store)
        {
            var salary = Salary(workspace.Load(period.Id));
            var line = workspace.SetCategory(salary, "Üür")!;
            Assert.Equal(("Üür", (string?)null), (line.Category, line.TypeCode));

            var view = workspace.Load(period.Id);
            salary = Salary(view);
            Assert.Equal(("Palgad", "Üür", "57", "57", true), (salary.DefaultCategory, salary.Category, salary.DefaultTypeCode, salary.TypeCode, salary.Declare));
            Assert.Equal(0m, view.Persons[0].SalaryTotal); // no longer a salary

            Assert.Null(workspace.SetTypeCode(salary, "57")!.TypeCode); // equal to the derived type: not stored
            var typed = workspace.SetTypeCode(salary, "24")!;
            Assert.Equal("24", typed.TypeCode);
            salary = Salary(workspace.Load(period.Id));
            Assert.Equal(("24", "57"), (salary.TypeCode, salary.DefaultTypeCode));

            // back to the run's category: the type set by hand goes with it, and the overlay, deciding nothing, is gone
            Assert.Null(workspace.SetCategory(salary, "Palgad"));
            Assert.Empty(store.ReviewLines(period.Id));
            salary = Salary(workspace.Load(period.Id));
            Assert.Equal(("Palgad", "10", false), (salary.Category, salary.TypeCode, salary.HasOverride));
        }
    }

    [Fact]
    public void A_row_nobody_matched_can_be_given_to_a_person_and_a_category()
    {
        var (store, _, period, workspace) = Run();
        using (store)
        {
            var view = workspace.Load(period.Id);
            var mariId = view.Persons[0].PersonId!.Value;
            workspace.SetPerson(Stranger(view), mariId);

            view = workspace.Load(period.Id);
            Assert.DoesNotContain(view.Unmatched.Payments, p => p.Payee == "Kinnisvara OÜ");
            var moved = view.Persons[0].Payments.Single(p => p.Payee == "Kinnisvara OÜ");
            Assert.Equal((mariId, (Guid?)null), (moved.PersonId, moved.DefaultPersonId));

            workspace.SetCategory(moved, "Haigusleht");
            moved = workspace.Load(period.Id).Persons[0].Payments.Single(p => p.Payee == "Kinnisvara OÜ");
            Assert.Equal(("Haigusleht", "24", true), (moved.Category, moved.TypeCode, moved.Declare));

            // given back to nobody: the category stands, the person is the run's again (nobody), and so it is not declared
            workspace.SetPerson(moved, null);
            view = workspace.Load(period.Id);
            var back = Assert.Single(view.Unmatched.Payments, p => p.Payee == "Kinnisvara OÜ");
            Assert.Equal(("Haigusleht", (Guid?)null, false, true), (back.Category, back.PersonId, back.Declare, back.HasOverride));
        }
    }

    [Fact]
    public void A_corrected_code_or_name_keeps_the_register_person_one()
    {
        var (store, company, period, _) = ReviewFixtures.Setup(jaanCodeless: true);
        using (store)
        {
            new MatchRunner(store).Run(period.Id);
            var workspace = Workspace(store);
            var view = workspace.Load(period.Id);
            var mari = view.Persons[0].Person!;
            var jaan = view.Persons[1].Person!;
            Assert.Equal((ReviewFixtures.MariCode, "TAMM|JAAN", (string?)null), (mari.RegisterKey, jaan.RegisterKey, jaan.PersonalCode));

            // the register's code was mistyped; the accountant corrects it — the person stays one, with the run's payments
            var corrected = workspace.SavePerson(mari with { PersonalCode = "48712219417" }, confirmResidency: true);
            view = workspace.Load(period.Id);
            Assert.Equal(2, store.Persons(company.Id).Count);
            Assert.Equal((corrected.Id, "48712219417", LookupSource.Manual), (view.Persons[0].PersonId, view.Persons[0].PersonalCode, view.Persons[0].Person!.ResidencySource));
            Assert.Equal(corrected.Id, Salary(view).PersonId);

            // the codeless register person gets a code and a corrected name: still one person, still the register's
            workspace.SavePerson(jaan with { PersonalCode = ReviewFixtures.JaanCode, FirstName = "Jaan" });
            view = workspace.Load(period.Id);
            Assert.Equal(2, store.Persons(company.Id).Count);
            Assert.Equal((jaan.Id, ReviewFixtures.JaanCode, "Jaan", "TAMM|JAAN"), (view.Persons[1].PersonId, view.Persons[1].PersonalCode, view.Persons[1].FirstName, view.Persons[1].Person!.RegisterKey));
            Assert.Single(view.Persons[1].Payments);

            // a person the lookups created before the review (no key yet) is bound, not doubled
            store.UpdatePerson(corrected with { RegisterKey = null, PersonalCode = ReviewFixtures.MariCode });
            view = workspace.Load(period.Id);
            Assert.Equal(2, store.Persons(company.Id).Count);
            Assert.Equal((corrected.Id, ReviewFixtures.MariCode), (view.Persons[0].PersonId, view.Persons[0].Person!.RegisterKey));
        }
    }

    [Fact]
    public void A_row_two_persons_found_is_shown_once_and_settled_by_the_accountants_word()
    {
        var (store, _, period, _) = ReviewFixtures.Setup(twin: true);
        using (store)
        {
            new MatchRunner(store).Run(period.Id);
            var workspace = Workspace(store);
            var view = workspace.Load(period.Id);
            Assert.Equal(4, view.Persons.Count); // Mari, Jaan, the twin, the rows nobody matched
            Assert.Empty(view.Persons[0].Payments);
            Assert.Empty(view.Persons[2].Payments);
            var shared = Assert.Single(view.Unmatched.Payments, p => p.IsShared);
            Assert.Equal(((Guid?)null, (Guid?)null, false, false, "Palgad"), (shared.PersonId, shared.DefaultPersonId, shared.AmountOk, shared.Declare, shared.Category));
            Assert.Contains(MatchProblem.SharedRow, shared.Flags);
            Assert.Equal(2, shared.Candidates.Count);
            Assert.Equal(0m, view.Persons[0].SalaryTotal);

            var mariId = view.Persons[0].PersonId!.Value;
            workspace.SetPerson(shared, mariId);
            view = workspace.Load(period.Id);
            Assert.DoesNotContain(view.Unmatched.Payments, p => p.IsShared);
            var settled = Assert.Single(view.Persons[0].Payments);
            Assert.Equal((mariId, true, true, "Palgad", "10", true), (settled.PersonId, settled.AmountOk, settled.Declare, settled.Category, settled.TypeCode, settled.IsShared));
            Assert.Empty(view.Persons[2].Payments);
            Assert.Equal(905.92m, view.Persons[0].SalaryTotal);
            Assert.DoesNotContain(settled.Checks, c => c.Code == ReviewChecks.NotCounted);

            // given back to nobody: shared again, counted for nobody, and no decision stored
            workspace.SetPerson(settled, null);
            view = workspace.Load(period.Id);
            Assert.Single(view.Unmatched.Payments, p => p.IsShared);
            Assert.Empty(store.ReviewLines(period.Id));
        }
    }

    [Fact]
    public void A_matched_row_can_be_given_to_nobody_and_to_no_category()
    {
        var (store, _, period, workspace) = Run();
        using (store)
        {
            var view = workspace.Load(period.Id);
            var mariId = view.Persons[0].PersonId!.Value;
            var line = workspace.SetPerson(Salary(view), null)!;
            Assert.Equal((true, (Guid?)null), (line.NoPerson, line.PersonId));

            view = workspace.Load(period.Id);
            Assert.Empty(view.Persons[0].Payments);
            Assert.Equal(0m, view.Persons[0].SalaryTotal);
            var stray = Assert.Single(view.Unmatched.Payments, p => p.Payee == "Mari Maasikas");
            Assert.Equal(((Guid?)null, mariId, false, true), (stray.PersonId, stray.DefaultPersonId, stray.Declare, stray.HasOverride));

            Assert.Null(workspace.SetPerson(stray, mariId)); // the run's person again: nothing to store
            Assert.Empty(store.ReviewLines(period.Id));

            var salary = Salary(workspace.Load(period.Id));
            line = workspace.SetCategory(salary, null)!;
            Assert.Equal((true, (string?)null), (line.NoCategory, line.Category));
            view = workspace.Load(period.Id);
            salary = Salary(view);
            Assert.Equal(((string?)null, (string?)null, false, "Palgad"), (salary.Category, salary.TypeCode, salary.Declare, salary.DefaultCategory));
            Assert.Equal(0m, view.Persons[0].SalaryTotal);
            Assert.Null(workspace.SetCategory(salary, "Palgad"));
            Assert.Empty(store.ReviewLines(period.Id));
        }
    }

    [Fact]
    public void A_hand_line_keeps_its_place_when_edited()
    {
        var (store, _, period, workspace) = Run();
        using (store)
        {
            var mariId = workspace.Load(period.Id).Persons[0].PersonId!.Value;
            var first = workspace.AddHandLine(period.Id, mariId, new DateOnly(2026, 10, 1), 10m, "A");
            var second = workspace.AddHandLine(period.Id, mariId, new DateOnly(2026, 10, 2), 20m, "B");
            var hands = workspace.Load(period.Id).Persons[0].Payments.Where(p => p.IsByHand).ToList();
            Assert.Equal([first.Id, second.Id], hands.Select(p => p.LineId));

            workspace.SetNote(hands[0], "x");
            workspace.UpdateHandLine(store.ReviewLines(period.Id).Single(l => l.Id == first.Id) with { Amount = 11m });
            hands = workspace.Load(period.Id).Persons[0].Payments.Where(p => p.IsByHand).ToList();
            Assert.Equal([first.Id, second.Id], hands.Select(p => p.LineId));
            Assert.Equal(("x", 11m), (hands[0].Note, hands[0].Amount));
        }
    }

    [Fact]
    public void The_salary_type_follows_the_months_register_entry_not_the_stored_type()
    {
        var (store, _, period, workspace) = Run();
        using (store)
        {
            var mari = workspace.Load(period.Id).Persons[0].Person!;
            // the person's stored type is stale (a VÕS contract of an earlier month); the register of this month says Tööleping
            store.UpdatePerson(mari with { TorEmploymentTypeId = "600" });
            Assert.Equal("10", Salary(workspace.Load(period.Id)).TypeCode);
        }
    }

    [Fact]
    public void Decisions_survive_a_re_run_over_the_same_files()
    {
        var (store, _, period, workspace) = Run();
        using (store)
        {
            workspace.SetDeclare(Salary(workspace.Load(period.Id)), false);
            workspace.SetPerson(Stranger(workspace.Load(period.Id)), workspace.Load(period.Id).Persons[1].PersonId);

            new MatchRunner(store).Run(period.Id); // the run replaced whole

            var view = workspace.Load(period.Id);
            Assert.False(Salary(view).Declare);
            Assert.DoesNotContain(view.Unmatched.Payments, p => p.Payee == "Kinnisvara OÜ");
            Assert.Contains(view.Persons[1].Payments, p => p.Payee == "Kinnisvara OÜ");
        }
    }

    [Fact]
    public void A_line_added_by_hand_joins_its_person_and_the_sums_or_waits_with_the_rows_nobody_matched()
    {
        var (store, _, period, workspace) = Run();
        using (store)
        {
            var mariId = workspace.Load(period.Id).Persons[0].PersonId!.Value;
            var line = workspace.AddHandLine(period.Id, mariId, new DateOnly(2026, 10, 20), 100m, "Mari Maasikas", "lisatasu", "Palgad");

            var view = workspace.Load(period.Id);
            Assert.Equal([ReviewFixtures.Source, ReviewWorkspace.ByHandSource], view.Sources);
            var mari = view.Persons[0];
            var hand = Assert.Single(mari.Payments, p => p.IsByHand);
            Assert.Equal((ReviewWorkspace.ByHandSource, "10", true, 100m, line.Id, "lisatasu"), (hand.Source, hand.TypeCode, hand.Declare, hand.Amount, hand.LineId, hand.Description));
            Assert.Equal(1005.92m, mari.SalaryTotal);
            Assert.Equal(100m, mari.SalaryBySource[ReviewWorkspace.ByHandSource]);

            // the hand line's own category and type are edited like a row's
            workspace.SetCategory(hand, "Haigusleht");
            hand = workspace.Load(period.Id).Persons[0].Payments.Single(p => p.IsByHand);
            Assert.Equal(("Haigusleht", "24"), (hand.Category, hand.TypeCode));
            workspace.SetTypeCode(hand, "16");
            Assert.Equal("16", workspace.Load(period.Id).Persons[0].Payments.Single(p => p.IsByHand).TypeCode);

            // without a person it waits with the rows nobody matched; dated outside the month it is not declared
            var stray = workspace.AddHandLine(period.Id, null, new DateOnly(2026, 9, 30), 50m, "Keegi", category: "Palgad");
            var waiting = workspace.Load(period.Id).Unmatched.Payments.Single(p => p.IsByHand);
            Assert.Equal((false, false, stray.Id), (waiting.Declare, waiting.InPeriod, waiting.LineId));
            workspace.SetDeclare(waiting, true);
            waiting = workspace.Load(period.Id).Unmatched.Payments.Single(p => p.IsByHand);
            Assert.True(waiting.Declare);
            Assert.Contains(waiting.Checks, c => c.Code == ReviewChecks.NoPerson);
            Assert.Contains(waiting.Checks, c => c.Code == ReviewChecks.NotInPeriod);

            workspace.RemoveHandLine(period.Id, stray.Id);
            Assert.Single(store.ReviewLines(period.Id));
            Assert.Equal(ReviewWorkspace.LineMissing, Assert.Throws<ReviewException>(() => workspace.RemoveHandLine(period.Id, stray.Id)).Code);
            var overlay = workspace.SetNote(Salary(workspace.Load(period.Id)), "x")!;
            Assert.Throws<ArgumentException>(() => workspace.RemoveHandLine(period.Id, overlay.Id));
            Assert.Throws<ArgumentException>(() => workspace.UpdateHandLine(overlay));
        }
    }

    [Fact]
    public void A_payment_in_another_currency_never_counts_and_is_refused_when_declared()
    {
        var (store, _, period, _) = ReviewFixtures.Setup(strangerCurrency: "USD");
        using (store)
        {
            new MatchRunner(store).Run(period.Id);
            var workspace = Workspace(store);
            var view = workspace.Load(period.Id);
            var stranger = Stranger(view);
            Assert.Equal(("USD", false, false), (stranger.Currency, stranger.AmountOk, stranger.Declare));

            // given to the first person as salary: not in her sums, not declared by default, refused when declared all the same
            var mari = view.Persons[0];
            workspace.SetPerson(stranger, mari.PersonId);
            workspace.SetCategory(stranger, "Palgad");
            view = workspace.Load(period.Id);
            Assert.Equal(905.92m, view.Persons[0].SalaryTotal);
            var given = view.Persons[0].Payments.Single(p => p.Payee == "Kinnisvara OÜ");
            Assert.Equal(("Palgad", "10", false), (given.Category, given.TypeCode, given.Declare));
            workspace.SetDeclare(given, true);
            var declared = workspace.Load(period.Id).Persons[0].Payments.Single(p => p.Payee == "Kinnisvara OÜ");
            Assert.Contains(declared.Checks, c => c.Code == ReviewChecks.ForeignCurrency);
            Assert.Equal(905.92m, workspace.Load(period.Id).Persons[0].SalaryTotal);
        }
    }

    [Fact]
    public void A_save_measured_against_the_card_shown_keeps_a_fact_the_registers_wrote_meanwhile()
    {
        var (store, _, period, workspace) = Run();
        using (store)
        {
            var shown = workspace.Load(period.Id).Persons[0].Person!; // the card filled from this copy
            Assert.Equal((Residency.Resident, LookupSource.Unknown), (shown.Residency, shown.ResidencySource));

            // the registers answer meanwhile (a lookup run): the store's copy moves on
            store.UpdatePerson(shown with { Residency = Residency.NonResident, ResidenceCountry = "LV", ResidencySource = LookupSource.Emta, ResidencyCheckedOn = Today, FundedPensionMember = true, PensionRate = 0.04m, PensionSource = LookupSource.Pensionikeskus, PensionCheckedOn = Today });

            // the card, still showing the old facts, saves a note: only the note is the accountant's, the registers' word stands
            var saved = workspace.SavePerson(shown with { Notes = "helistada" }, presented: shown);
            Assert.Equal((Residency.NonResident, "LV", LookupSource.Emta, true, 0.04m, LookupSource.Pensionikeskus, "helistada"),
                (saved.Residency, saved.ResidenceCountry, saved.ResidencySource, saved.FundedPensionMember, saved.PensionRate, saved.PensionSource, saved.Notes));

            // a fact the accountant did change on the card is theirs, and Manual
            var changed = workspace.SavePerson(shown with { FundedPensionMember = false, PensionRate = null }, presented: shown);
            Assert.Equal((Residency.NonResident, LookupSource.Emta, false, LookupSource.Manual), (changed.Residency, changed.ResidencySource, changed.FundedPensionMember, changed.PensionSource));

            // without the card's copy to measure against, every field of the edit stands (the old way)
            var overwritten = workspace.SavePerson(shown with { Notes = "x" });
            Assert.Equal((Residency.Resident, LookupSource.Manual), (overwritten.Residency, overwritten.ResidencySource));
        }
    }

    [Fact]
    public void A_persons_facts_edited_by_hand_become_manual_and_the_salary_type_follows_the_residency()
    {
        var (store, _, period, workspace) = Run();
        using (store)
        {
            var mari = workspace.Load(period.Id).Persons[0].Person!;
            Assert.Equal((LookupSource.Unknown, (DateOnly?)null), (mari.ResidencySource, mari.ResidencyCheckedOn));

            var saved = workspace.SavePerson(mari with { Residency = Residency.NonResident, ResidenceCountry = " LV " });
            Assert.Equal((Residency.NonResident, "LV", LookupSource.Manual, Today, LookupSource.Unknown, (DateOnly?)null),
                (saved.Residency, saved.ResidenceCountry, saved.ResidencySource, saved.ResidencyCheckedOn, saved.PensionSource, saved.PensionCheckedOn));
            Assert.Equal("120", Salary(workspace.Load(period.Id)).TypeCode);

            // an unchanged fact keeps its source; a confirmation marks it by hand without a change
            var again = workspace.SavePerson(saved with { Notes = "x" });
            Assert.Equal((LookupSource.Manual, Today, LookupSource.Unknown, "x"), (again.ResidencySource, again.ResidencyCheckedOn, again.PensionSource, again.Notes));
            var confirmed = workspace.SavePerson(again, confirmPension: true);
            Assert.Equal((LookupSource.Manual, Today, (bool?)null), (confirmed.PensionSource, confirmed.PensionCheckedOn, confirmed.FundedPensionMember));
            var member = workspace.SavePerson(confirmed with { FundedPensionMember = true, PensionRate = 0.04m });
            Assert.Equal((true, 0.04m, LookupSource.Manual), (member.FundedPensionMember, member.PensionRate, member.PensionSource));

            // what a lookup wrote meanwhile stands when the edit starts from a fresh copy
            store.UpdatePerson(member with { Residency = Residency.Resident, ResidenceCountry = null, ResidencySource = LookupSource.Emta, ResidencyCheckedOn = new DateOnly(2026, 11, 1) });
            var fresh = store.Persons(member.CompanyId).Single(p => p.Id == member.Id);
            var late = workspace.SavePerson(fresh with { Notes = "late" });
            Assert.Equal((Residency.Resident, LookupSource.Emta, new DateOnly(2026, 11, 1)), (late.Residency, late.ResidencySource, late.ResidencyCheckedOn));

            Assert.Equal(ReviewWorkspace.NameMissing, Assert.Throws<ReviewException>(() => workspace.SavePerson(late with { FirstName = " " })).Code);
            Assert.Equal(ReviewWorkspace.PersonExists, Assert.Throws<ReviewException>(() => workspace.SavePerson(late with { PersonalCode = ReviewFixtures.JaanCode })).Code);
            Assert.Equal(ReviewWorkspace.CodeInvalid, Assert.Throws<ReviewException>(() => workspace.SavePerson(late with { PersonalCode = "12345" })).Code);
            Assert.Throws<KeyNotFoundException>(() => workspace.SavePerson(late with { Id = Guid.NewGuid() }));
            Assert.Null(workspace.SavePerson(late with { PersonalCode = " " }).PersonalCode); // a code taken away is allowed: the MRR code may stand instead
        }
    }

    [Fact]
    public void A_person_added_by_hand_needs_names_and_an_unused_valid_code_and_shows_once_they_have_a_line()
    {
        var (store, company, period, workspace) = Run();
        using (store)
        {
            workspace.Load(period.Id);
            var added = workspace.AddPerson(company.Id, " Peeter ", "Kask", "50001010006", "600");
            Assert.Equal(("Peeter", "Kask", "50001010006", "600", Residency.Resident), (added.FirstName, added.LastName, added.PersonalCode, added.TorEmploymentTypeId, added.Residency));
            Assert.True(added.BasicExemptionApplied, "a resident added by hand starts with the exemption applied");
            Assert.Equal(ReviewWorkspace.PersonExists, Assert.Throws<ReviewException>(() => workspace.AddPerson(company.Id, "Peeter", "Kask", "50001010006", null)).Code);
            Assert.Equal(ReviewWorkspace.NameMissing, Assert.Throws<ReviewException>(() => workspace.AddPerson(company.Id, "Peeter", "", null, null)).Code);
            Assert.Equal(ReviewWorkspace.CodeInvalid, Assert.Throws<ReviewException>(() => workspace.AddPerson(company.Id, "Peeter", "Kask", "abc", null)).Code);
            var codeless = workspace.AddPerson(company.Id, "Anna", "Kask", null, null, Residency.NonResident);
            Assert.Null(codeless.PersonalCode);
            Assert.False(codeless.BasicExemptionApplied, "a non-resident gets the exemption only by the application the accountant enters");
            Assert.All(workspace.Load(period.Id).Persons.Where(p => p.Register is not null), p => Assert.True(p.Person!.BasicExemptionApplied, "a person taken from the register starts with the exemption applied"));

            var view = workspace.Load(period.Id);
            Assert.Equal(3, view.Persons.Count); // a person without lines is in the pickers, not in the grid
            Assert.Equal(4, view.StorePersons.Count);

            workspace.AddHandLine(period.Id, added.Id, new DateOnly(2026, 10, 3), 200m, "Peeter Kask", category: "Palgad");
            view = workspace.Load(period.Id);
            Assert.Equal(4, view.Persons.Count);
            var peeter = view.Persons[2]; // after the register's persons, before the rows nobody matched
            Assert.Equal((added.Id, (string?)null, "Kask"), (peeter.PersonId, peeter.Key, peeter.LastName));
            Assert.Equal("17", Assert.Single(peeter.Payments).TypeCode); // VÕS, resident
            Assert.Equal(200m, peeter.SalaryTotal);
        }
    }

    [Fact]
    public void Past_draft_the_review_is_read_but_not_edited()
    {
        var (store, _, period, workspace) = Run();
        using (store)
        {
            workspace.SetDeclare(Salary(workspace.Load(period.Id)), false);
            store.SetPeriodState(period.Id, PeriodState.Exported, DateTime.UtcNow);

            var view = workspace.Load(period.Id);
            Assert.False(view.Editable);
            Assert.False(Salary(view).Declare);
            Assert.Equal(ReviewWorkspace.PeriodNotDraft, Assert.Throws<ReviewException>(() => workspace.SetDeclare(Salary(view), true)).Code);
            Assert.Equal(ReviewWorkspace.PeriodNotDraft, Assert.Throws<ReviewException>(() => workspace.AddHandLine(period.Id, null, new DateOnly(2026, 10, 1), 1m, "x")).Code);
            Assert.Equal(ReviewWorkspace.PeriodNotDraft, Assert.Throws<ReviewException>(() => workspace.RemoveHandLine(period.Id, Guid.NewGuid())).Code);
            Assert.Single(store.ReviewLines(period.Id));
        }
    }

    [Fact]
    public void The_checks_say_what_the_engine_would_refuse_for_a_declared_payment()
    {
        var (store, _, period, workspace) = Run();
        using (store)
        {
            var mari = workspace.Load(period.Id).Persons[0].Person!;
            workspace.SavePerson(mari with { FundedPensionMember = false }, confirmResidency: true);
            Assert.Empty(Salary(workspace.Load(period.Id)).Checks);

            var fresh = () => store.Persons(mari.CompanyId).Single(p => p.Id == mari.Id);
            workspace.SavePerson(fresh() with { BasicExemptionApplied = false, BasicExemptionMonthly = 300m }); // a new person has the exemption applied by default
            Assert.Equal([PaymentChecks.ExemptionWithoutApplication], Salary(workspace.Load(period.Id)).Checks.Select(c => c.Code));

            workspace.SavePerson(fresh() with { BasicExemptionApplied = true, FundedPensionMember = true, PensionRate = null });
            Assert.Equal([PaymentChecks.PensionRateUnknown], Salary(workspace.Load(period.Id)).Checks.Select(c => c.Code));

            workspace.SavePerson(fresh() with { PensionRate = 0.02m });
            Assert.Empty(Salary(workspace.Load(period.Id)).Checks);
            workspace.SetTypeCode(Salary(workspace.Load(period.Id)), "120");
            Assert.Equal([PaymentChecks.TypePayeeMismatch], Salary(workspace.Load(period.Id)).Checks.Select(c => c.Code));
            workspace.SetTypeCode(Salary(workspace.Load(period.Id)), "999");
            Assert.Equal([ReviewChecks.UnknownType], Salary(workspace.Load(period.Id)).Checks.Select(c => c.Code));

            // the next month's payment, declared by the accountant's word: the month and the missing type are said
            var next = workspace.Load(period.Id).Persons[1].Payments.Single();
            workspace.SetDeclare(next, true);
            next = workspace.Load(period.Id).Persons[1].Payments.Single();
            Assert.Equal([ReviewChecks.ResidencyNotChecked, ReviewChecks.NotInPeriod, ReviewChecks.NoType], next.Checks.Select(c => c.Code));
        }
    }
}
