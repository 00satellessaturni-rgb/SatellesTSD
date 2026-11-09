using SatellesTSD.Core.Model;
using SatellesTSD.Core.Tax;

namespace SatellesTSD.Tests;

/// <summary>The engine against the worked figures in MTA's business example (docs/reference/apa-xml-reference.md C.6)
/// and against the workbook's own net → gross formulas (cCalc2026).</summary>
public class TaxEngineTests
{
    private static readonly TaxEngine Engine = new(TaxRates.Year2026);
    private static readonly PaymentType Salary = PaymentTypes.Get("10");

    [Fact]
    public void Salary_1000_gross_resident_with_exemption_matches_Tehing_2111()
    {
        var r = Engine.Calculate(new TaxInput(Salary, 1000m, false, 700m), PayeeTaxFacts.ResidentEmployee());

        Assert.Equal(1000m, r.Gross);
        Assert.Equal(700m, r.ExemptionUsed);
        Assert.Equal(ExemptionKind.Mvt610, r.ExemptionKind);
        Assert.Equal(1000m, r.SocialTaxBase);
        Assert.Equal(330m, r.SocialTax);
        Assert.Equal(20m, r.FundedPension);
        Assert.Equal(16m, r.UnemploymentEmployee);
        Assert.Equal(8m, r.UnemploymentEmployer);
        Assert.Equal(58.08m, r.IncomeTax);
        Assert.Equal(1000m - 20m - 16m - 58.08m, r.Net);
        Assert.Equal(1000m + 8m + 330m, r.EmployerCost);
    }

    [Fact]
    public void Salary_900_to_pensioner_in_pension_age_matches_Tehing_21113()
    {
        var facts = new PayeeTaxFacts(Residency.Resident, FundedPensionMember: false, OnOldAgeOrFlexiblePension: true,
            PensionAgeThisYear: true, BasicExemptionApplied: true);
        var r = Engine.Calculate(new TaxInput(Salary, 900m, false, 776m), facts);

        Assert.Equal(ExemptionKind.Mvt650, r.ExemptionKind);
        Assert.Equal(776m, r.ExemptionUsed);
        Assert.Equal(297m, r.SocialTax);
        Assert.False(r.UnemploymentEmployeeApplies);
        Assert.Equal(0m, r.UnemploymentEmployee);
        Assert.True(r.UnemploymentEmployerApplies);
        Assert.Equal(7.2m, r.UnemploymentEmployer);
        Assert.False(r.FundedPensionApplies);
        Assert.Equal(27.28m, r.IncomeTax);
    }

    [Fact]
    public void Estonian_A1_work_abroad_keeps_social_taxes_and_drops_income_tax_matches_Tehing_21114()
    {
        var facts = PayeeTaxFacts.ResidentEmployee() with { A1 = A1Case.EstonianA1WorkAbroad, A1Country = "EE", WorkCountry = "FI" };
        var r = Engine.Calculate(new TaxInput(Salary, 1000m, false, 700m), facts);

        Assert.Equal(330m, r.SocialTax);
        Assert.Equal(20m, r.FundedPension);
        Assert.Equal(16m, r.UnemploymentEmployee);
        Assert.Equal(8m, r.UnemploymentEmployer);
        Assert.False(r.IncomeTaxApplies);
        Assert.Equal(0m, r.IncomeTax);
        Assert.Equal(ExemptionKind.None, r.ExemptionKind);
        Assert.Equal(0m, r.ExemptionUsed);
    }

    [Fact]
    public void Foreign_A1_work_in_Estonia_has_only_income_tax_matches_Tehing_21115()
    {
        var facts = PayeeTaxFacts.ResidentEmployee() with { A1 = A1Case.ForeignA1WorkInEstonia, A1Country = "LV", WorkCountry = "EE" };
        var r = Engine.Calculate(new TaxInput(Salary, 1000m, false, 700m), facts);

        Assert.False(r.SocialTaxApplies);
        Assert.False(r.FundedPensionApplies);
        Assert.False(r.UnemploymentEmployeeApplies);
        Assert.False(r.UnemploymentEmployerApplies);
        Assert.Equal(700m, r.ExemptionUsed);
        Assert.Equal(66m, r.IncomeTax);
    }

    [Fact]
    public void Non_resident_salary_with_EEA_exemption_matches_Tehing_21116()
    {
        var facts = new PayeeTaxFacts(Residency.NonResident, ResidenceCountry: "LV", BasicExemptionApplied: true);
        var r = Engine.Calculate(new TaxInput(PaymentTypes.Get("120"), 1000m, false, 700m), facts);

        Assert.Equal(330m, r.SocialTax);
        Assert.False(r.FundedPensionApplies);
        Assert.Equal(16m, r.UnemploymentEmployee);
        Assert.Equal(8m, r.UnemploymentEmployer);
        Assert.Equal(700m, r.ExemptionUsed);
        Assert.Equal(0.22m, r.IncomeTaxRate);
        Assert.Equal(62.48m, r.IncomeTax);
    }

    [Fact]
    public void Zero_payment_with_minimum_increase_matches_Tehing_21117()
    {
        var engine = new TaxEngine(TaxRates.Year2026 with { SocialTaxMinimumBase = 820m });
        var r = engine.Calculate(new TaxInput(Salary, 0m, false, 0m, SocialTaxMinimumIncrease: 820m), PayeeTaxFacts.ResidentEmployee());

        Assert.Equal(0m, r.Gross);
        Assert.Equal(820m, r.SocialTaxMinimumIncrease);
        Assert.Equal(270.60m, r.SocialTax);
        Assert.Equal(0m, r.FundedPension);
        Assert.Equal(0m, r.IncomeTax);
    }

    [Fact]
    public void Employer_sick_pay_100_is_income_tax_only_matches_Tehing_2112()
    {
        var r = Engine.Calculate(new TaxInput(PaymentTypes.Get("24"), 100m, false, 0m), PayeeTaxFacts.ResidentEmployee());

        Assert.False(r.SocialTaxApplies);
        Assert.False(r.UnemploymentEmployeeApplies);
        Assert.False(r.FundedPensionApplies);
        Assert.Equal(22m, r.IncomeTax);
        Assert.Equal(0m, r.ExemptionUsed);
    }

    [Fact]
    public void Rent_100_in_pension_age_with_50_exemption_left_matches_Tehing_31111()
    {
        var facts = new PayeeTaxFacts(Residency.Resident, PensionAgeThisYear: true, BasicExemptionApplied: true);
        var r = Engine.Calculate(new TaxInput(PaymentTypes.Get("50"), 100m, false, 50m), facts);

        Assert.Equal(ExemptionKind.Mvt650, r.ExemptionKind);
        Assert.Equal(50m, r.ExemptionUsed);
        Assert.Equal(11m, r.IncomeTax);
        Assert.False(r.SocialTaxApplies);
    }

    [Fact]
    public void Net_1000_salary_grosses_up_as_the_workbook_formula()
    {
        // cCalc2026 line 412: gross = (net − r·MVT) / (1 − p − u + r·(p + u − 1)) = 846 / 0.75192 = 1125.12
        var r = Engine.Calculate(new TaxInput(Salary, 1000m, true, 700m), PayeeTaxFacts.ResidentEmployee());

        Assert.Equal(1125.12m, r.Gross);
        Assert.Equal(22.50m, r.FundedPension);
        Assert.Equal(18.00m, r.UnemploymentEmployee);
        Assert.Equal(9.00m, r.UnemploymentEmployer);
        Assert.Equal(700m, r.ExemptionUsed);
        Assert.Equal(84.62m, r.IncomeTax);
        Assert.Equal(1000.00m, r.Net);
        Assert.Equal(371.29m, r.SocialTax);
    }

    [Fact]
    public void Net_within_the_exemption_has_no_income_tax_and_uses_the_whole_net_as_exemption()
    {
        // cCalc2026 tier 0: gross = net / (1 − p − u) = 600 / 0.964 = 622.41
        var r = Engine.Calculate(new TaxInput(Salary, 600m, true, 700m), PayeeTaxFacts.ResidentEmployee());

        Assert.Equal(622.41m, r.Gross);
        Assert.Equal(12.45m, r.FundedPension);
        Assert.Equal(9.96m, r.UnemploymentEmployee);
        Assert.Equal(600m, r.ExemptionUsed);
        Assert.Equal(0m, r.IncomeTax);
        Assert.Equal(600m, r.Net);
    }

    [Fact]
    public void Net_to_gross_re_derives_gross_from_net_plus_rounded_withholdings()
    {
        // Board member, net 1000, no exemption: the closed formula gives 1308.22, the re-derivation 1000 + 26.16 + 282.05 = 1308.21,
        // and the net recomputed from 1308.21 is exactly 1000.00 — the workbook's behaviour, kept on purpose.
        var r = Engine.Calculate(new TaxInput(PaymentTypes.Get("21"), 1000m, true, 0m), PayeeTaxFacts.ResidentEmployee(exemptionApplied: false));

        Assert.Equal(1308.21m, r.Gross);
        Assert.Equal(26.16m, r.FundedPension);
        Assert.Equal(0m, r.UnemploymentEmployee);
        Assert.Equal(282.05m, r.IncomeTax);
        Assert.Equal(1000.00m, r.Net);
        Assert.Equal(431.71m, r.SocialTax);
    }

    [Theory]
    [InlineData(0.02, 20)]
    [InlineData(0.04, 40)]
    [InlineData(0.06, 60)]
    public void Funded_pension_uses_the_persons_own_rate(double rate, double expected)
    {
        var r = Engine.Calculate(new TaxInput(Salary, 1000m, false, 0m), PayeeTaxFacts.ResidentEmployee((decimal)rate, exemptionApplied: false));

        Assert.Equal((decimal)expected, r.FundedPension);
    }

    [Fact]
    public void Treaty_rate_replaces_the_income_tax_rate_only_where_the_type_allows_it()
    {
        var certified = new PayeeTaxFacts(Residency.NonResident, ResidenceCountry: "LT", TreatyIncomeTaxRate: 0.10m);

        var vos = Engine.Calculate(new TaxInput(PaymentTypes.Get("123"), 1000m, false, 0m), certified);
        Assert.Equal(0.10m, vos.IncomeTaxRate);
        Assert.Equal(98.40m, vos.IncomeTax); // (1000 − 16) × 0.10

        var salary = Engine.Calculate(new TaxInput(PaymentTypes.Get("120"), 1000m, false, 0m), certified);
        Assert.Equal(0.22m, salary.IncomeTaxRate);
    }

    [Fact]
    public void Exemption_is_capped_at_amount_minus_KP_and_TK()
    {
        var r = Engine.Calculate(new TaxInput(Salary, 300m, false, 700m), PayeeTaxFacts.ResidentEmployee());

        Assert.Equal(300m - 6m - 4.8m, r.ExemptionUsed);
        Assert.Equal(0m, r.IncomeTax);
    }

    [Fact]
    public void Exemption_is_not_used_when_the_person_has_not_applied_for_it()
    {
        var r = Engine.Calculate(new TaxInput(Salary, 1000m, false, 700m), PayeeTaxFacts.ResidentEmployee(exemptionApplied: false));

        Assert.Equal(ExemptionKind.None, r.ExemptionKind);
        Assert.Equal(0m, r.ExemptionUsed);
        Assert.Equal(212.08m, r.IncomeTax); // (1000 − 20 − 16) × 0.22
    }

    [Fact]
    public void A_refund_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Engine.Calculate(new TaxInput(Salary, -100m, false, 0m), PayeeTaxFacts.ResidentEmployee()));
    }

    [Fact]
    public void A_resident_type_for_a_non_resident_payee_is_refused_and_vice_versa()
    {
        var nonResident = new PayeeTaxFacts(Residency.NonResident, ResidenceCountry: "LV");
        Assert.Throws<InvalidOperationException>(() => Engine.Calculate(new TaxInput(Salary, 100m, false, 0m), nonResident));
        Assert.Throws<InvalidOperationException>(() =>
            Engine.Calculate(new TaxInput(PaymentTypes.Get("120"), 100m, false, 0m), PayeeTaxFacts.ResidentEmployee()));
    }

    [Fact]
    public void A_minimum_increase_on_a_type_without_the_minimum_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Engine.Calculate(new TaxInput(PaymentTypes.Get("17"), 100m, false, 0m, SocialTaxMinimumIncrease: 100m), PayeeTaxFacts.ResidentEmployee()));
    }

    [Fact]
    public void A_minimum_increase_is_refused_when_the_person_is_exempt_from_the_minimum()
    {
        var exempt = PayeeTaxFacts.ResidentEmployee() with { SocialTaxMinimumApplies = false };
        Assert.Throws<InvalidOperationException>(() =>
            Engine.Calculate(new TaxInput(Salary, 100m, false, 0m, SocialTaxMinimumIncrease: 100m), exempt));
    }

    [Fact]
    public void Rounding_is_commercial_half_away_from_zero()
    {
        // 1000.50 × 0.33 = 330.165 → 330.17 (banker's rounding would give 330.16); 1000.50 × 0.016 = 16.008 → 16.01
        var r = Engine.Calculate(new TaxInput(Salary, 1000.50m, false, 0m), PayeeTaxFacts.ResidentEmployee(exemptionApplied: false));

        Assert.Equal(330.17m, r.SocialTax);
        Assert.Equal(20.01m, r.FundedPension);
        Assert.Equal(16.01m, r.UnemploymentEmployee);
        Assert.Equal(212.19m, r.IncomeTax); // (1000.50 − 20.01 − 16.01) × 0.22 = 212.1856
        Assert.Equal(330.16m, new TaxEngine(TaxRates.Year2026, MidpointRounding.ToEven)
            .Calculate(new TaxInput(Salary, 1000.50m, false, 0m), PayeeTaxFacts.ResidentEmployee(exemptionApplied: false)).SocialTax);
    }

    [Fact]
    public void Net_to_gross_recomputes_the_withholdings_after_the_re_derivation()
    {
        // First pass: gross 622.70, exemption 600.29. Re-derived gross 622.69 → the exemption must be recomputed to 600.28,
        // or MVT_610 would exceed amount − KP − TK (KR_MVT_08).
        var r = Engine.Calculate(new TaxInput(Salary, 600.28m, true, 700m), PayeeTaxFacts.ResidentEmployee());

        Assert.Equal(622.69m, r.Gross);
        Assert.Equal(600.28m, r.ExemptionUsed);
        Assert.Equal(r.Gross - r.FundedPension - r.UnemploymentEmployee, r.ExemptionUsed);
        Assert.Equal(600.28m, r.Net);
    }

    [Fact]
    public void An_unchecked_II_pillar_status_is_refused_where_the_pension_is_withheld()
    {
        var notChecked = new PayeeTaxFacts(Residency.Resident, BasicExemptionApplied: true);

        Assert.Throws<InvalidOperationException>(() => Engine.Calculate(new TaxInput(Salary, 1000m, false, 700m), notChecked));
        Assert.Equal(22m, Engine.Calculate(new TaxInput(PaymentTypes.Get("24"), 100m, false, 0m), notChecked).IncomeTax);
    }

    [Fact]
    public void An_unknown_pension_rate_is_refused_where_the_pension_is_withheld()
    {
        // KR_KOP_03 checks TSD_KP against the person's own rate; a silent 2 % default would be a RED error for a 4 % or 6 % person.
        var memberWithoutRate = new PayeeTaxFacts(Residency.Resident, FundedPensionMember: true);

        Assert.Throws<InvalidOperationException>(() => Engine.Calculate(new TaxInput(Salary, 1000m, false, 0m), memberWithoutRate));
        Assert.Equal(0m, Engine.Calculate(new TaxInput(Salary, 1000m, false, 0m), memberWithoutRate with { FundedPensionMember = false }).FundedPension);
        Assert.Equal(22m, Engine.Calculate(new TaxInput(PaymentTypes.Get("24"), 100m, false, 0m), memberWithoutRate).IncomeTax);
    }

    [Fact]
    public void An_Estonian_A1_drops_the_minimum_on_severance_but_not_on_salary()
    {
        // Matrix: the EE-A1 row of 33 has M = EI (no monthly minimum), the EE-A1 row of 10 keeps M = JAH.
        var posted = PayeeTaxFacts.ResidentEmployee() with { A1 = A1Case.EstonianA1WorkAbroad, A1Country = "EE", WorkCountry = "FI" };

        Assert.False(Engine.Calculate(new TaxInput(PaymentTypes.Get("33"), 500m, false, 0m), posted).SocialTaxMinimumApplies);
        Assert.True(Engine.Calculate(new TaxInput(PaymentTypes.Get("33"), 500m, false, 0m), PayeeTaxFacts.ResidentEmployee()).SocialTaxMinimumApplies);
        Assert.True(Engine.Calculate(new TaxInput(Salary, 500m, false, 0m), posted).SocialTaxMinimumApplies);
    }

    [Fact]
    public void A_non_resident_board_member_has_only_the_foreign_A1_variant()
    {
        var foreign = new PayeeTaxFacts(Residency.NonResident, ResidenceCountry: "LV", A1: A1Case.ForeignA1WorkInEstonia, A1Country: "LV", WorkCountry: "EE");
        var estonian = foreign with { A1 = A1Case.EstonianA1WorkAbroad, A1Country = "EE", WorkCountry = "FI" };
        var fee = PaymentTypes.Get("156");

        var f = Engine.Calculate(new TaxInput(fee, 1000m, false, 0m), foreign);
        Assert.Equal(A1Case.ForeignA1WorkInEstonia, f.EffectiveA1);
        Assert.False(f.SocialTaxApplies);
        Assert.Equal(220m, f.IncomeTax);

        var e = Engine.Calculate(new TaxInput(fee, 1000m, false, 0m), estonian);
        Assert.Equal(A1Case.None, e.EffectiveA1); // no EE-A1 row for 156: declared as a plain 156
        Assert.Equal(330m, e.SocialTax);
        Assert.Equal(220m, e.IncomeTax);
    }

    [Fact]
    public void A1_facts_are_ignored_on_a_type_without_A1_variants()
    {
        var posted = PayeeTaxFacts.ResidentEmployee(exemptionApplied: false) with { A1 = A1Case.EstonianA1WorkAbroad, A1Country = "EE", WorkCountry = "FI" };

        var rent = Engine.Calculate(new TaxInput(PaymentTypes.Get("50"), 500m, false, 0m), posted);
        Assert.Equal(A1Case.None, rent.EffectiveA1);
        Assert.Equal(110m, rent.IncomeTax);

        var sickPay = Engine.Calculate(new TaxInput(PaymentTypes.Get("24"), 100m, false, 0m), posted);
        Assert.Equal(A1Case.None, sickPay.EffectiveA1);
        Assert.Equal(22m, sickPay.IncomeTax);

        var salary = Engine.Calculate(new TaxInput(Salary, 1000m, false, 0m), posted);
        Assert.Equal(A1Case.EstonianA1WorkAbroad, salary.EffectiveA1);
        Assert.False(salary.IncomeTaxApplies);
    }

    [Fact]
    public void The_exemption_cap_can_be_the_amount_on_the_persons_application()
    {
        var r = Engine.Calculate(new TaxInput(Salary, 1000m, false, 300m), PayeeTaxFacts.ResidentEmployee());

        Assert.Equal(300m, r.ExemptionUsed);
        Assert.Equal(146.08m, r.IncomeTax); // (1000 − 20 − 16 − 300) × 0.22
    }

    [Fact]
    public void Every_table_row_has_a_consistent_section_and_payee()
    {
        foreach (var type in PaymentTypes.All)
        {
            Assert.False(type.FundedPension && type.Payee == Residency.NonResident, $"{type.Code}: non-residents never have TSD_KP");
            Assert.False(type.SocialTaxMinimum && !type.SocialTax, $"{type.Code}: a minimum without social tax");
            Assert.False(type.BasicExemptionAllowed && type.IncomeTaxRate is null, $"{type.Code}: an exemption without income tax");
            Assert.False(type.SocialTaxMinimumWithEstonianA1 && !type.A1Kinds.HasFlag(A1Kinds.Estonian), $"{type.Code}: a minimum under an A1 variant the type lacks");
            Assert.False(type.SocialTaxMinimumWithEstonianA1 && !type.SocialTax, $"{type.Code}: a minimum without social tax");
        }

        Assert.Equal(A1Kinds.Foreign, PaymentTypes.Get("156").A1Kinds);
        Assert.Equal(A1Kinds.None, PaymentTypes.Get("24").A1Kinds);
        Assert.Equal(["10", "120", "138"], PaymentTypes.All.Where(t => t.SocialTaxMinimumWithEstonianA1).Select(t => t.Code));

        Assert.Equal(PaymentTypes.All.Count, PaymentTypes.All.Select(t => t.Code).Distinct().Count());
    }

    [Fact]
    public void A_payment_taxed_at_zero_by_a_treaty_takes_nothing_from_the_exemption()
    {
        var exempt = new PayeeTaxFacts(Residency.NonResident, ResidenceCountry: "LT", BasicExemptionApplied: true, TreatyIncomeTaxRate: 0m);
        var r = Engine.Calculate(new TaxInput(PaymentTypes.Get("123"), 1000m, false, 700m), exempt);
        Assert.Equal(0m, r.IncomeTax);
        Assert.Equal(0m, r.ExemptionUsed); // nothing to exempt at 0 %: the month's pool is left to the taxed payments
    }
}
