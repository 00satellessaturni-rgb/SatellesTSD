using SatellesTSD.Core.Declare;
using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Review;
using SatellesTSD.Core.Store;
using SatellesTSD.Core.Tax;
using SatellesTSD.Infrastructure;

namespace SatellesTSD.Tests;

/// <summary>The build's notes carry a code, the Core's English and the figures the window words them by; the engine's, the
/// line factory's and the store's refusals carry their code in the exception's Data (the window's Estonian, plan item 66).</summary>
public class BuildNotesTests
{
    private static (SqliteStore Store, Period Period, ReviewWorkspace Workspace, Person Mari) Ready()
    {
        var (store, company, period, _) = ReviewFixtures.Setup();
        new MatchRunner(store).Run(period.Id);
        var workspace = new ReviewWorkspace(store, TaxRates.Year2026);
        var mari = workspace.Load(period.Id).Persons[0].Person!;
        workspace.SavePerson(mari with { FundedPensionMember = false, BasicExemptionApplied = true, SocialTaxMinimumApplies = true }, confirmResidency: true);
        var salary = workspace.Load(period.Id).Persons[0].Payments.Single(p => !p.IsByHand);
        workspace.SetDeclare(salary, false); // no pay this month: the zero line carries the minimum
        return (store, period, workspace, store.FindPerson(company.Id, ReviewFixtures.MariCode)!);
    }

    [Fact]
    public void A_zero_line_the_engine_refuses_carries_the_engines_code_and_figures()
    {
        var (store, period, _, mari) = Ready();
        using (store)
        {
            store.UpdatePerson(mari with { BasicExemptionMonthly = 5000m });
            var person = new PeriodDeclarer(store, TaxRates.Year2026).Build(period.Id).Persons.Single(p => p.PersonId == mari.Id);
            var note = Assert.Single(person.Notes, n => n.Code == PeriodDeclarer.NotAllocated);
            Assert.Equal("engine", note.Variant);
            Assert.Equal(PaymentChecks.ExemptionAboveLegal, TaxRefusal.CodeOf(note.Cause));
            Assert.Equal((5000m, 700m), (TaxRefusal.FigureOf(note.Cause), TaxRefusal.OtherOf(note.Cause)));
            Assert.IsType<InvalidOperationException>(note.Cause); // the exception's type is unchanged
            Assert.Equal(person.Notes.Select(n => n.Text), person.Problems); // the English stays what Problems reads
            Assert.StartsWith($"{PeriodDeclarer.NotAllocated}: The exemption on the application (5000)", note.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_zero_line_the_line_factory_refuses_carries_the_A1_code()
    {
        var (store, period, _, mari) = Ready();
        using (store)
        {
            store.UpdatePerson(mari with { A1 = A1Case.EstonianA1WorkAbroad, A1Country = null, WorkCountry = null });
            var person = new PeriodDeclarer(store, TaxRates.Year2026).Build(period.Id).Persons.Single(p => p.PersonId == mari.Id);
            var note = Assert.Single(person.Notes, n => n.Code == PeriodDeclarer.NotBuilt);
            Assert.Equal(PaymentChecks.A1CountriesMissing, TaxRefusal.CodeOf(note.Cause));
            Assert.Equal(PeriodDeclarer.MinimumSource, note.Reference);
        }
    }

    [Fact]
    public void The_zero_line_note_carries_its_case_and_types()
    {
        var (store, period, _, mari) = Ready();
        using (store)
        {
            var person = new PeriodDeclarer(store, TaxRates.Year2026).Build(period.Id).Persons.Single(p => p.PersonId == mari.Id);
            var note = Assert.Single(person.Notes, n => n.Code == PeriodDeclarer.ZeroLine);
            Assert.Equal(("valid", "1", false), (note.Variant, note.TypeId, note.FromCard));
            Assert.NotNull(note.PaymentType);
        }
    }

    [Fact]
    public void The_builds_refusals_carry_their_figure()
    {
        var (store, period, _, _) = Ready();
        using (store)
        {
            var declarer = new PeriodDeclarer(store); // the year's own rates
            store.SetPeriodState(period.Id, PeriodState.Exported, new DateTime(2026, 11, 5));
            var notDraft = Assert.Throws<DeclareException>(() => declarer.Build(period.Id));
            Assert.Equal((ReviewWorkspace.PeriodNotDraft, nameof(PeriodState.Exported)), (notDraft.Code, notDraft.Subject));
        }
    }

    [Fact]
    public void The_engine_and_the_store_mark_their_refusals()
    {
        Assert.Equal(PaymentChecks.ZeroNotAllowed, TaxRefusal.CodeOf(Assert.Throws<InvalidOperationException>(() => MonthPayment.Zero("k", PaymentTypes.All.First(t => !t.ZeroAllowed), new DateOnly(2026, 10, 31)))));
        Assert.Null(TaxRefusal.CodeOf(new InvalidOperationException("x"))); // nothing coded: none
        var other = new InvalidOperationException("x");
        other.Data["Code"] = "READ_WEB_PAGE";
        Assert.Null(TaxRefusal.CodeOf(other)); // another refusal's code is not taken for one of these
        Assert.Equal(TaxRefusal.NotDraft, TaxRefusal.CodeOf(TaxRefusal.Of(TaxRefusal.NotDraft, "x")));
    }

    [Fact]
    public void Notes_compare_by_their_figures_lists_included()
    {
        var a = new BuildNote("REFUSED", "REFUSED: x: A, B") { Items = ["A", "B"] };
        var b = new BuildNote("REFUSED", "REFUSED: x: A, B") { Items = new List<string> { "A", "B" } };
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, b with { Items = ["A"] });
        Assert.Equal("REFUSED: x: A, B", a.ToString());
    }
}
