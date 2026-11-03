using SatellesTSD.Core.Model;
using SatellesTSD.Core.Tax;

namespace SatellesTSD.Tests;

public class MonthAllocatorTests
{
    private static readonly TaxEngine Engine = new(TaxRates.Year2026);
    private static readonly MonthAllocator Allocator = new(Engine);
    private static readonly PaymentType Salary = PaymentTypes.Get("10");
    private static readonly PaymentType BoardFee = PaymentTypes.Get("21");
    private static readonly PaymentType SickPay = PaymentTypes.Get("24");

    private static readonly PayeeTaxFacts Pensioner = new(Residency.Resident, FundedPensionMember: false,
        OnOldAgeOrFlexiblePension: true, PensionAgeThisYear: true, BasicExemptionApplied: true);

    private static MonthPayment Pay(string key, PaymentType type, decimal amount, int day, bool net = false, PayeeTaxFacts? facts = null) =>
        new(key, type, amount, net, new DateOnly(2026, 10, day), facts);

    [Fact]
    public void The_months_exemption_is_spent_in_date_order_and_capped_per_payment()
    {
        var month = Allocator.Allocate([Pay("b", Salary, 500m, 20), Pay("a", Salary, 500m, 5)], PayeeTaxFacts.ResidentEmployee());

        var result = month.Payments;
        Assert.Equal(["a", "b"], result.Select(r => r.Payment.Key));
        Assert.Equal(700m, result[0].ExemptionCap);
        Assert.Equal(482m, result[0].Result.ExemptionUsed); // 500 − 10 − 8
        Assert.Equal(0m, result[0].Result.IncomeTax);
        Assert.Equal(218m, result[1].ExemptionCap);
        Assert.Equal(218m, result[1].Result.ExemptionUsed); // the rest of 700
        Assert.Equal(58.08m, result[1].Result.IncomeTax);  // (500 − 10 − 8 − 218) × 0.22
        Assert.Equal(700m, month.ExemptionOffered);
        Assert.Equal(0m, month.ExemptionLeft);
        Assert.Equal(1000m, month.MinimumBaseTotal);
        Assert.Equal(886m, month.MinimumDue);
        Assert.Equal(0m, month.MinimumShortfall); // 1000 ≥ 886
        Assert.Null(month.TopUpKey);
    }

    [Fact]
    public void A_person_in_pension_age_gets_776_not_700()
    {
        var month = Allocator.Allocate([Pay("x", Salary, 1000m, 10)], Pensioner);

        var only = Assert.Single(month.Payments).Result;
        Assert.Equal(776m, month.ExemptionOffered);
        Assert.Equal(ExemptionKind.Mvt650, only.ExemptionKind);
        Assert.Equal(776m, only.ExemptionUsed);
        Assert.Equal(49.28m, only.IncomeTax); // (1000 − 776) × 0.22
    }

    [Fact]
    public void The_amount_on_the_persons_application_caps_the_month()
    {
        var facts = PayeeTaxFacts.ResidentEmployee() with { BasicExemptionMonthly = 300m };

        var gross = Assert.Single(Allocator.Allocate([Pay("g", Salary, 1000m, 10)], facts).Payments).Result;
        Assert.Equal(300m, gross.ExemptionUsed);
        Assert.Equal(146.08m, gross.IncomeTax); // (1000 − 20 − 16 − 300) × 0.22

        var net = Assert.Single(Allocator.Allocate([Pay("n", Salary, 1000m, 10, net: true)], facts).Payments).Result;
        Assert.Equal(1242.15m, net.Gross);      // (1000 − 0.22 × 300) / 0.75192
        Assert.Equal(300m, net.ExemptionUsed);
        Assert.Equal(197.44m, net.IncomeTax);
        Assert.Equal(409.91m, net.SocialTax);
        Assert.Equal(1000m, net.Net);
    }

    [Fact]
    public void An_application_amount_of_zero_means_no_exemption_and_one_above_the_legal_amount_is_refused()
    {
        var none = Allocator.Allocate([Pay("x", Salary, 1000m, 10)], PayeeTaxFacts.ResidentEmployee() with { BasicExemptionMonthly = 0m });
        Assert.Equal(0m, Assert.Single(none.Payments).Result.ExemptionUsed);
        Assert.Equal(212.08m, Assert.Single(none.Payments).Result.IncomeTax);

        Assert.Throws<InvalidOperationException>(() =>
            Allocator.Allocate([Pay("x", Salary, 1000m, 10)], PayeeTaxFacts.ResidentEmployee() with { BasicExemptionMonthly = 800m }));
    }

    [Fact]
    public void A_short_month_gets_the_minimum_increase_on_the_last_qualifying_payment()
    {
        var month = Allocator.Allocate([Pay("a", Salary, 200m, 5), Pay("b", Salary, 300m, 20)], PayeeTaxFacts.ResidentEmployee());

        Assert.Equal(0m, month.Payments[0].Result.SocialTaxMinimumIncrease);
        Assert.Equal(386m, month.Payments[1].Result.SocialTaxMinimumIncrease); // 886 − 500
        Assert.Equal((300m + 386m) * 0.33m, month.Payments[1].Result.SocialTax);
        Assert.Equal(500m, month.MinimumBaseTotal);
        Assert.Equal(386m, month.MinimumShortfall);
        Assert.Equal("b", month.TopUpKey);
        Assert.Equal(month.Payments[1].ExemptionCap, 700m - month.Payments[0].Result.ExemptionUsed); // the cap is unchanged by the second pass
    }

    [Fact]
    public void A_board_fee_next_to_a_small_salary_does_not_fill_the_minimum()
    {
        // SMS § 2 lg 2 compares the pay received as an employee (lg 1 p 1); a board fee (lg 1 p 4, matrix M = EI on 21) never counts.
        var month = Allocator.Allocate([Pay("salary", Salary, 400m, 5), Pay("fee", BoardFee, 500m, 10)], PayeeTaxFacts.ResidentEmployee());

        Assert.Equal(400m, month.MinimumBaseTotal);
        Assert.Equal(486m, month.MinimumShortfall);
        Assert.Equal(486m, month.Payments[0].Result.SocialTaxMinimumIncrease);
        Assert.Equal(0m, month.Payments[1].Result.SocialTaxMinimumIncrease);
        Assert.Equal("salary", month.TopUpKey);
    }

    [Fact]
    public void A_part_month_owes_the_minimum_in_proportion()
    {
        // Started mid-month: 15 of 30 days → 443 owed (SMS § 2 lg 3, määrus nr 17 § 6).
        var halfMonth = PayeeTaxFacts.ResidentEmployee() with { SocialTaxMinimumShare = 0.5m };

        var month = Allocator.Allocate([Pay("x", Salary, 300m, 20)], halfMonth);

        Assert.Equal(443m, month.MinimumDue);
        Assert.Equal(143m, month.MinimumShortfall);
        Assert.Equal(143m, Assert.Single(month.Payments).Result.SocialTaxMinimumIncrease);
        Assert.Throws<InvalidOperationException>(() => Allocator.Allocate([Pay("x", Salary, 300m, 20)], halfMonth with { SocialTaxMinimumShare = 0m }));
    }

    [Fact]
    public void The_increase_lands_on_the_last_payment_that_carries_the_minimum_not_on_a_later_other_type()
    {
        var month = Allocator.Allocate([Pay("salary", Salary, 400m, 5), Pay("sick", SickPay, 100m, 25)], PayeeTaxFacts.ResidentEmployee());

        Assert.Equal(486m, month.Payments[0].Result.SocialTaxMinimumIncrease); // 886 − 400; sick pay has no social tax
        Assert.Equal(0m, month.Payments[1].Result.SocialTaxMinimumIncrease);
        Assert.Equal("salary", month.TopUpKey);
    }

    [Fact]
    public void A_type_without_the_minimum_alone_gets_no_increase()
    {
        var month = Allocator.Allocate([Pay("fee", BoardFee, 500m, 10)], PayeeTaxFacts.ResidentEmployee());

        Assert.Equal(0m, Assert.Single(month.Payments).Result.SocialTaxMinimumIncrease);
        Assert.Equal(0m, month.MinimumShortfall);
        Assert.Null(month.TopUpKey);
    }

    [Fact]
    public void A_person_exempt_from_the_minimum_gets_no_increase()
    {
        var month = Allocator.Allocate([Pay("x", Salary, 500m, 10)], Pensioner with { SocialTaxMinimumApplies = false });

        var only = Assert.Single(month.Payments).Result;
        Assert.Equal(0m, only.SocialTaxMinimumIncrease);
        Assert.False(only.SocialTaxMinimumApplies);
        Assert.Equal(0m, month.MinimumShortfall);
    }

    [Fact]
    public void A_month_without_pay_is_a_zero_line_carrying_the_whole_minimum()
    {
        var month = Allocator.Allocate([MonthPayment.Zero("min", Salary, new DateOnly(2026, 10, 31))], PayeeTaxFacts.ResidentEmployee());

        var line = Assert.Single(month.Payments).Result;
        Assert.Equal(0m, line.Gross);
        Assert.Equal(886m, line.SocialTaxMinimumIncrease);
        Assert.Equal(292.38m, line.SocialTax);
        Assert.Equal("min", month.TopUpKey);
    }

    [Fact]
    public void A_zero_line_next_to_a_board_fee_carries_the_whole_minimum()
    {
        var month = Allocator.Allocate([Pay("fee", BoardFee, 500m, 10), MonthPayment.Zero("min", Salary, new DateOnly(2026, 10, 31))], PayeeTaxFacts.ResidentEmployee());

        Assert.Equal(886m, month.Payments[1].Result.SocialTaxMinimumIncrease);
        Assert.Equal(292.38m, month.Payments[1].Result.SocialTax);
        Assert.Equal(0m, month.Payments[0].Result.SocialTaxMinimumIncrease);
        Assert.Equal(0m, month.MinimumBaseTotal);
        Assert.Equal(886m, month.MinimumDue);
    }

    [Fact]
    public void A_zero_line_is_refused_on_a_type_that_does_not_allow_zero()
    {
        Assert.Throws<InvalidOperationException>(() => MonthPayment.Zero("min", BoardFee, new DateOnly(2026, 10, 31)));
    }

    [Fact]
    public void Net_payments_are_grossed_up_before_the_exemption_is_spread()
    {
        var month = Allocator.Allocate([Pay("a", Salary, 1000m, 5, net: true), Pay("b", Salary, 1000m, 25, net: true)], PayeeTaxFacts.ResidentEmployee());

        Assert.Equal(1125.12m, month.Payments[0].Result.Gross);
        Assert.Equal(700m, month.Payments[0].Result.ExemptionUsed);
        Assert.Equal(0m, month.Payments[1].Result.ExemptionUsed);
        Assert.Equal(1000m, month.Payments[1].Result.Net);
    }

    [Fact]
    public void A_payment_with_its_own_facts_still_shares_the_months_pools()
    {
        // An A1 certificate from mid-month: the second salary is A1 (no income tax, no exemption), yet one 700 and one minimum.
        var a1 = PayeeTaxFacts.ResidentEmployee() with { A1 = A1Case.EstonianA1WorkAbroad, A1Country = "EE", WorkCountry = "FI" };
        var month = Allocator.Allocate([Pay("plain", Salary, 500m, 5), Pay("posted", Salary, 500m, 25, facts: a1)], PayeeTaxFacts.ResidentEmployee());

        Assert.Equal(482m, month.Payments[0].Result.ExemptionUsed);
        Assert.Equal(A1Case.EstonianA1WorkAbroad, month.Payments[1].Result.EffectiveA1);
        Assert.False(month.Payments[1].Result.IncomeTaxApplies);
        Assert.Equal(0m, month.Payments[1].Result.ExemptionUsed);
        Assert.Equal(218m, month.ExemptionLeft);
        Assert.Equal(0m, month.MinimumShortfall); // bases 500 + 500
    }

    [Fact]
    public void A_payment_whose_own_facts_change_the_months_pools_is_refused()
    {
        var pensionAge = PayeeTaxFacts.ResidentEmployee() with { PensionAgeThisYear = true };
        Assert.Throws<InvalidOperationException>(() =>
            Allocator.Allocate([Pay("x", Salary, 1000m, 10, facts: pensionAge)], PayeeTaxFacts.ResidentEmployee()));

        var appliedLater = PayeeTaxFacts.ResidentEmployee(exemptionApplied: true);
        Assert.Throws<InvalidOperationException>(() =>
            Allocator.Allocate([Pay("x", Salary, 1000m, 10, facts: appliedLater)], PayeeTaxFacts.ResidentEmployee(exemptionApplied: false)));

        var sixPercent = PayeeTaxFacts.ResidentEmployee(pensionRate: 0.06m);
        Assert.Throws<InvalidOperationException>(() =>
            Allocator.Allocate([Pay("x", Salary, 1000m, 10, facts: sixPercent)], PayeeTaxFacts.ResidentEmployee()));
    }

    [Fact]
    public void An_exemption_amount_without_an_application_or_with_sub_cents_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Allocator.Allocate([Pay("x", Salary, 1000m, 10)], PayeeTaxFacts.ResidentEmployee(exemptionApplied: false) with { BasicExemptionMonthly = 300m }));
        Assert.Throws<InvalidOperationException>(() =>
            Allocator.Allocate([Pay("x", Salary, 1000m, 10)], PayeeTaxFacts.ResidentEmployee() with { BasicExemptionMonthly = 333.333m }));
    }

    [Fact]
    public void An_Estonian_A1_severance_carries_no_minimum_top_up()
    {
        var posted = PayeeTaxFacts.ResidentEmployee() with { A1 = A1Case.EstonianA1WorkAbroad, A1Country = "EE", WorkCountry = "FI" };

        var month = Allocator.Allocate([Pay("sev", PaymentTypes.Get("33"), 500m, 10)], posted);

        Assert.Equal(0m, Assert.Single(month.Payments).Result.SocialTaxMinimumIncrease);
        Assert.Null(month.TopUpKey);
        Assert.Equal(386m, Allocator.Allocate([Pay("sal", Salary, 500m, 10)], posted).MinimumShortfall);
    }

    [Fact]
    public void Payments_from_two_months_are_refused()
    {
        Assert.Throws<ArgumentException>(() =>
            Allocator.Allocate([Pay("a", Salary, 100m, 5), new MonthPayment("b", Salary, 100m, false, new DateOnly(2026, 11, 5))], PayeeTaxFacts.ResidentEmployee()));
    }

    [Fact]
    public void No_payments_give_no_lines_but_still_the_months_figures()
    {
        var month = Allocator.Allocate([], PayeeTaxFacts.ResidentEmployee());

        Assert.Empty(month.Payments);
        Assert.Equal(700m, month.ExemptionOffered);
        Assert.Equal(700m, month.ExemptionLeft);
        Assert.Null(month.TopUpKey);
    }
}
