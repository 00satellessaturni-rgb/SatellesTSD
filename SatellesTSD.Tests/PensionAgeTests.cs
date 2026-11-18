using SatellesTSD.Core.Model;
using SatellesTSD.Core.Review;
using SatellesTSD.Core.Tax;
using SatellesTSD.Infrastructure;

namespace SatellesTSD.Tests;

/// <summary>The employee's unemployment insurance ends with the month the pension age is reached (TKindlS § 42 lg 1 p 1,
/// the fifth audit's critical item): the age by year of birth (RPKS § 7), the card's year, the code's month.</summary>
public class PensionAgeTests
{
    private const string BornJuly1961 = "46107150018"; // 65 on 15.07.2026
    private const string BornOctober1961 = "46110200013"; // 65 on 20.10.2026
    private const string Born1950 = "35001010014";
    private const string Born1962 = "36205050015"; // the age is set later from life expectancy

    [Fact]
    public void The_pension_age_follows_the_laws_table_by_year_of_birth()
    {
        Assert.Equal(new DateOnly(2026, 7, 15), PensionAge.ReachedOn(BornJuly1961));
        Assert.Equal(new DateOnly(2025, 4, 1), PensionAge.ReachedOn("36007010008")); // 64 y 9 m for 1960
        Assert.Equal(new DateOnly(2013, 1, 1), PensionAge.ReachedOn(Born1950));
        Assert.Null(PensionAge.ReachedOn(Born1962));
        Assert.Null(PensionAge.ReachedOn(null));
        Assert.Null(PensionAge.ReachedOn("12345"));
    }

    [Fact]
    public void A_card_without_the_pension_year_reads_the_age_from_the_code_for_the_exemption_as_for_the_premium()
    {
        // the review of decision 208: both rules read one age, so the line never drops TK while it still offers MVT_610
        var rates = TaxRates.Year2026;
        var past = SatellesTSD.Core.Store.Person.New(Guid.NewGuid(), "A", "B", Born1950);
        var facts = past.FactsFor(2026, 10);
        Assert.True(facts.PensionAgeThisYear);
        Assert.True(facts.PensionAgeReached);
        Assert.Equal(rates.PensionAgeExemptionMonth, past.ExemptionMonthInForce(2026, rates));

        // reached in October of this year: the pension-age exemption from 1 January, the premium withheld until October's end
        var turning = SatellesTSD.Core.Store.Person.New(Guid.NewGuid(), "A", "B", BornOctober1961);
        Assert.True(turning.FactsFor(2026, 3).PensionAgeThisYear);
        Assert.False(turning.FactsFor(2026, 10).PensionAgeReached);
        Assert.True(turning.FactsFor(2026, 11).PensionAgeReached);
        Assert.Equal(rates.PensionAgeExemptionMonth, turning.ExemptionMonthInForce(2026, rates));

        // a code whose age the law leaves to a later regulation: the card decides, and without its year, not of age
        var later = SatellesTSD.Core.Store.Person.New(Guid.NewGuid(), "A", "B", Born1962);
        Assert.False(later.FactsFor(2026, 10).PensionAgeThisYear);
        Assert.Equal(rates.BasicExemptionMonth, later.ExemptionMonthInForce(2026, rates));
        Assert.True((later with { PensionAgeYear = 2026 }).FactsFor(2026, 10).PensionAgeThisYear);
    }

    [Fact]
    public void The_shared_reading_takes_the_cards_year_over_the_code_and_the_code_without_one()
    {
        // one reading for the store and the card's exemption box, so the two cannot drift apart (decision 208)
        Assert.Equal(2030, SatellesTSD.Core.Store.Person.PensionAgeYearOf(2030, Born1950));
        Assert.Equal(2013, SatellesTSD.Core.Store.Person.PensionAgeYearOf(null, Born1950));
        Assert.Equal(2026, SatellesTSD.Core.Store.Person.PensionAgeYearOf(null, BornJuly1961));
        Assert.Null(SatellesTSD.Core.Store.Person.PensionAgeYearOf(null, Born1962));
        Assert.Null(SatellesTSD.Core.Store.Person.PensionAgeYearOf(null, null));
        Assert.Equal(2028, SatellesTSD.Core.Store.Person.PensionAgeYearOf(2028, null));
    }

    [Theory]
    [InlineData(2026, BornJuly1961, 7, false)] // the month the age is reached still withholds
    [InlineData(2026, BornJuly1961, 8, true)] // from the next month on, not
    [InlineData(2026, BornOctober1961, 10, false)]
    [InlineData(2026, BornOctober1961, 11, true)]
    [InlineData(2024, BornJuly1961, 1, true)] // the card's earlier year: already of pension age
    [InlineData(2027, Born1950, 12, false)] // the card's later year stands over the code
    [InlineData(2026, null, 12, false)] // the year without a code: the month is not known, withheld
    [InlineData(2026, Born1962, 12, false)]
    public void The_month_after_the_age_is_reached_is_the_first_without_the_premium(int? cardYear, string? code, int month, bool reached) =>
        Assert.Equal(reached, PensionAge.ReachedBefore(cardYear, code, 2026, month));

    [Fact]
    public void With_no_year_on_the_card_the_personal_code_decides()
    {
        Assert.True(PensionAge.ReachedBefore(null, Born1950, 2026, 1));
        Assert.True(PensionAge.ReachedBefore(null, BornJuly1961, 2026, 8));
        Assert.False(PensionAge.ReachedBefore(null, BornJuly1961, 2026, 7));
        Assert.False(PensionAge.ReachedBefore(null, Born1962, 2026, 12));
    }

    [Fact]
    public void A_person_past_pension_age_has_no_employee_unemployment_insurance_and_the_employers_stays()
    {
        var facts = PayeeTaxFacts.ResidentEmployee() with { PensionAgeReached = true };
        var flags = EffectiveFlags.Of(PaymentTypes.Get("10"), facts);
        Assert.False(flags.UnemploymentEmployee);
        Assert.True(flags.UnemploymentEmployer);
        Assert.True(EffectiveFlags.Of(PaymentTypes.Get("10"), PayeeTaxFacts.ResidentEmployee()).UnemploymentEmployee);
    }

    [Fact]
    public void A_salary_in_the_months_after_the_age_goes_out_without_the_premium_and_in_the_month_itself_with_it()
    {
        SimScenario At(int month, int? cardYear, string code) => new(2026, month,
            new SimPerson("Vaike", "Vanem", code, FundedPensionMember: false, PensionRate: null, PensionAgeYear: cardYear),
            SimRegister.Listed,
            [new SimEmployment("1", new DateOnly(2020, 1, 1))],
            [new SimRow(new DateOnly(2026, month, 5), "Vaike Vanem", 500m, "palk")]);

        var after = Assert.Single(Simulator.Run(At(10, 2026, BornJuly1961)).Lines).Result!;
        Assert.Equal((500.00m, 0m, 4.00m), (after.Gross, after.UnemploymentEmployee, after.UnemploymentEmployer)); // 776 covers the net: the gross is the net
        Assert.True(Assert.Single(Simulator.Run(At(10, 2026, BornJuly1961)).Lines).Facts!.PensionAgeReached);

        var same = Assert.Single(Simulator.Run(At(7, 2026, BornJuly1961)).Lines).Result!;
        Assert.True(same.UnemploymentEmployee > 0m);

        var earlier = Assert.Single(Simulator.Run(At(10, 2024, BornJuly1961)).Lines).Result!;
        Assert.Equal(0m, earlier.UnemploymentEmployee);
    }
}
