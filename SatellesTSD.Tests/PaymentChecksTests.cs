using SatellesTSD.Core.Model;
using SatellesTSD.Core.Tax;

namespace SatellesTSD.Tests;

/// <summary>Every refusal the checks list is one the engine would throw for, and a payment with no refusals computes.</summary>
public class PaymentChecksTests
{
    private static readonly TaxRates Rates = TaxRates.Year2026;
    private static readonly TaxEngine Engine = new(Rates);
    private static readonly PaymentType Salary = PaymentTypes.Get("10");

    public static IEnumerable<object[]> RefusedCases()
    {
        yield return Case(PaymentChecks.NegativeAmount, Salary, PayeeTaxFacts.ResidentEmployee(), -1m);
        yield return Case(PaymentChecks.ZeroNotAllowed, PaymentTypes.Get("21"), PayeeTaxFacts.ResidentEmployee(), 0m);
        yield return Case(PaymentChecks.TypePayeeMismatch, PaymentTypes.Get("120"), PayeeTaxFacts.ResidentEmployee(), 100m);
        yield return Case(PaymentChecks.ResidenceCountryMissing, PaymentTypes.Get("120"), new PayeeTaxFacts(Residency.NonResident), 100m);
        yield return Case(PaymentChecks.A1CountriesMissing, Salary, PayeeTaxFacts.ResidentEmployee() with { A1 = A1Case.EstonianA1WorkAbroad }, 100m);
        yield return Case(PaymentChecks.PensionNotChecked, Salary, new PayeeTaxFacts(Residency.Resident), 100m);
        yield return Case(PaymentChecks.PensionRateUnknown, Salary, new PayeeTaxFacts(Residency.Resident, FundedPensionMember: true), 100m);
        yield return Case(PaymentChecks.PensionRateInvalid, Salary, PayeeTaxFacts.ResidentEmployee(pensionRate: 1.5m), 100m);
        yield return Case(PaymentChecks.MinimumShareInvalid, Salary, PayeeTaxFacts.ResidentEmployee() with { SocialTaxMinimumShare = 0m }, 100m);

        static object[] Case(string code, PaymentType type, PayeeTaxFacts facts, decimal amount) => [code, type, facts, amount];
    }

    [Theory]
    [MemberData(nameof(RefusedCases))]
    public void A_refusal_names_what_the_engine_or_the_factory_would_throw_for(string code, PaymentType type, PayeeTaxFacts facts, decimal amount)
    {
        var refusals = PaymentChecks.Check(type, facts, amount, Rates);

        Assert.Contains(refusals, r => r.Code == code);
    }

    [Fact]
    public void The_engine_throws_for_every_refused_case_that_reaches_it()
    {
        foreach (var caseData in RefusedCases())
        {
            var code = (string)caseData[0];
            var type = (PaymentType)caseData[1];
            var facts = (PayeeTaxFacts)caseData[2];
            var amount = (decimal)caseData[3];
            if (code is PaymentChecks.ZeroNotAllowed or PaymentChecks.ResidenceCountryMissing or PaymentChecks.A1CountriesMissing or PaymentChecks.MinimumShareInvalid)
            {
                continue; // refused by the line factory, the zero-line helper or the allocator rather than the engine
            }

            Assert.ThrowsAny<Exception>(() => Engine.Calculate(new TaxInput(type, amount, false, 0m), facts));
        }
    }

    [Fact]
    public void The_exemption_amount_is_checked_against_the_application_and_the_legal_limit()
    {
        var withoutApplication = PayeeTaxFacts.ResidentEmployee(exemptionApplied: false) with { BasicExemptionMonthly = 300m };
        Assert.Contains(PaymentChecks.Check(Salary, withoutApplication, 100m, Rates), r => r.Code == PaymentChecks.ExemptionWithoutApplication);

        var aboveLegal = PayeeTaxFacts.ResidentEmployee() with { BasicExemptionMonthly = 800m };
        Assert.Contains(PaymentChecks.Check(Salary, aboveLegal, 100m, Rates), r => r.Code == PaymentChecks.ExemptionAboveLegal);
        Assert.Empty(PaymentChecks.Check(Salary, aboveLegal with { PensionAgeThisYear = true, BasicExemptionMonthly = 776m }, 100m, Rates));

        var subCent = PayeeTaxFacts.ResidentEmployee() with { BasicExemptionMonthly = 300.005m };
        Assert.Contains(PaymentChecks.Check(Salary, subCent, 100m, Rates), r => r.Code == PaymentChecks.ExemptionSubCent);
    }

    [Fact]
    public void Withholding_rates_that_reach_100_percent_are_refused_like_the_engine_does()
    {
        var absurd = PayeeTaxFacts.ResidentEmployee(pensionRate: 0.8m);

        Assert.Contains(PaymentChecks.Check(Salary, absurd, 100m, Rates), r => r.Code == PaymentChecks.RatesExceedOne);
        Assert.Throws<InvalidOperationException>(() => Engine.Calculate(new TaxInput(Salary, 100m, false, 0m), absurd));
    }

    [Fact]
    public void A_payment_without_refusals_computes()
    {
        var facts = new PayeeTaxFacts(Residency.NonResident, ResidenceCountry: "LV", BasicExemptionApplied: true);
        Assert.Empty(PaymentChecks.Check(PaymentTypes.Get("120"), facts, 1000m, Rates));
        Assert.Equal(62.48m, Engine.Calculate(new TaxInput(PaymentTypes.Get("120"), 1000m, false, 700m), facts).IncomeTax);

        var posted = PayeeTaxFacts.ResidentEmployee() with { A1 = A1Case.EstonianA1WorkAbroad, A1Country = "EE", WorkCountry = "FI" };
        Assert.Empty(PaymentChecks.Check(Salary, posted, 1000m, Rates));
        Assert.Empty(PaymentChecks.Check(PaymentTypes.Get("50"), posted with { A1Country = null, WorkCountry = null }, 100m, Rates)); // A1 ignored on rent
        Assert.Empty(PaymentChecks.Check(Salary, PayeeTaxFacts.ResidentEmployee(), 0m, Rates)); // a zero line on type 10
    }
}
