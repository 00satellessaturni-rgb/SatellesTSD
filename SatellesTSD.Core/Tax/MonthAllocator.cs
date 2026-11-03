namespace SatellesTSD.Core.Tax;

/// <summary>One of a person's payments in a month, before taxes.</summary>
/// <param name="Key">The caller's handle (e.g. the bank line id); carried through unchanged.</param>
/// <param name="Facts">Facts that differ for this payment from the month's facts (an A1 certificate that started
/// mid-month, a treaty certificate). The month-level pools — exemption, minimum — stay one.</param>
public sealed record MonthPayment(string Key, PaymentType Type, decimal Amount, bool AmountIsNet, DateOnly Date, PayeeTaxFacts? Facts = null)
{
    /// <summary>The line for a month in which nothing was paid under a type that carries the minimum social tax:
    /// amount 0, so that the allocator puts the whole shortfall on it (MTA's example Tehing_21117).</summary>
    public static MonthPayment Zero(string key, PaymentType type, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!type.ZeroAllowed)
        {
            throw new InvalidOperationException($"Payment type {type.Code} does not allow an amount of 0.");
        }

        return new MonthPayment(key, type, 0m, AmountIsNet: false, date);
    }
}

/// <param name="ExemptionCap">The exemption that was available to this payment when it was computed.</param>
public sealed record AllocatedPayment(MonthPayment Payment, TaxResult Result, decimal ExemptionCap);

/// <summary>A person's month: every payment computed, plus the month-level figures a review needs to explain them.</summary>
/// <param name="ExemptionOffered">The exemption the month started with (the application's amount or the legal amount, or 0).</param>
/// <param name="ExemptionLeft">What was not used by any payment.</param>
/// <param name="MinimumBaseTotal">The social-tax bases of the payments that carry the minimum obligation, added up (a zero line counts as 0).</param>
/// <param name="MinimumDue">The minimum base owed this month: kuumäär × the month share; 0 when the minimum does not apply to any payment.</param>
/// <param name="MinimumShortfall">How far the total fell short of what is due; 0 when the minimum does not apply or is met.</param>
/// <param name="TopUpKey">The payment that carries TSD_KUUMSUURENDUS, or null.</param>
public sealed record MonthAllocation(
    IReadOnlyList<AllocatedPayment> Payments,
    decimal ExemptionOffered,
    decimal ExemptionLeft,
    decimal MinimumBaseTotal,
    decimal MinimumDue,
    decimal MinimumShortfall,
    string? TopUpKey);

/// <summary>Spreads the month-level facts over a person's individual payments: the basic exemption (the application's
/// monthly amount or the legal 700/776, consumed in date order, each payment capped by its own amount − KP − TK) and the
/// social-tax minimum (if the bases of the payments that carry the obligation add up to less than the kuumäär owed for
/// the month, the shortfall becomes TSD_KUUMSUURENDUS on the last such payment). The workbook did both once per
/// person-month on the summed net; the new regime declares every payment on its own, so the spreading is explicit here.
/// The minimum compares pay received as an employee or official (SMS § 2 lg 2 with lg 1 p 1; types with matrix M = JAH:
/// 10, 33, 37, 120, 138, 167); a board fee (21/156) or a VÕS fee (17/123) never fills it, and MTA forbids the top-up on
/// those types (KR_SM_11). A month without such pay needs a <see cref="MonthPayment.Zero"/> line to carry the top-up.</summary>
public sealed class MonthAllocator
{
    private readonly TaxEngine _engine;

    public MonthAllocator(TaxEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    /// <summary>Computes every payment of one person in one month. Payments come back in date order (stable for equal dates).</summary>
    public MonthAllocation Allocate(IReadOnlyList<MonthPayment> payments, PayeeTaxFacts payee)
    {
        ArgumentNullException.ThrowIfNull(payments);
        ArgumentNullException.ThrowIfNull(payee);
        var rates = _engine.Rates;

        decimal legal = payee.PensionAgeThisYear ? rates.PensionAgeExemptionMonth : rates.BasicExemptionMonth;
        decimal offered = 0m;
        if (payee.BasicExemptionMonthly is { } applied)
        {
            if (!payee.BasicExemptionApplied)
            {
                throw new InvalidOperationException("An exemption amount is given, but the person has not applied for the exemption at this payer; drop one of the two.");
            }

            if (applied < 0m || applied > legal)
            {
                throw new InvalidOperationException($"The exemption on the application ({applied}) must be between 0 and the legal {legal} per month (KR_KOOND_02/03).");
            }

            if (applied != Math.Round(applied, 2))
            {
                throw new InvalidOperationException($"The exemption on the application ({applied}) must be a whole number of cents.");
            }
        }

        if (payee.BasicExemptionApplied)
        {
            offered = payee.BasicExemptionMonthly ?? legal;
        }

        foreach (var payment in payments.Where(p => p.Facts is not null))
        {
            RequireMonthFactsAgree(payment.Key, payee, payment.Facts!);
        }

        if (payee.SocialTaxMinimumShare <= 0m || payee.SocialTaxMinimumShare > 1m)
        {
            throw new InvalidOperationException($"The month share of the minimum obligation ({payee.SocialTaxMinimumShare}) must be above 0 and at most 1; switch the minimum off for a whole month away.");
        }

        if (payments.Count == 0)
        {
            return new MonthAllocation([], offered, offered, 0m, 0m, 0m, null);
        }

        var first = payments[0].Date;
        if (payments.Any(p => p.Date.Year != first.Year || p.Date.Month != first.Month))
        {
            throw new ArgumentException("All payments must fall in the same calendar month.", nameof(payments));
        }

        var ordered = payments
            .Select((p, i) => (Payment: p, Index: i))
            .OrderBy(x => x.Payment.Date)
            .ThenBy(x => x.Index)
            .Select(x => x.Payment)
            .ToList();

        decimal remaining = offered;
        var results = new List<AllocatedPayment>(ordered.Count);
        foreach (var payment in ordered)
        {
            var facts = payment.Facts ?? payee;
            var result = _engine.Calculate(new TaxInput(payment.Type, payment.Amount, payment.AmountIsNet, remaining), facts);
            results.Add(new AllocatedPayment(payment, result, remaining));
            remaining -= result.ExemptionUsed;
        }

        decimal minimumBase = results.Where(a => a.Result.SocialTaxMinimumApplies).Sum(a => a.Result.SocialTaxBase);
        decimal due = 0m;
        decimal shortfall = 0m;
        string? topUpKey = null;
        int last = results.FindLastIndex(a => a.Result.SocialTaxMinimumApplies);
        if (last >= 0)
        {
            due = Math.Round(rates.SocialTaxMinimumBase * payee.SocialTaxMinimumShare, 2, MidpointRounding.AwayFromZero);
            if (minimumBase < due)
            {
                shortfall = due - minimumBase;
                var target = results[last];
                var withIncrease = _engine.Calculate(
                    new TaxInput(target.Payment.Type, target.Payment.Amount, target.Payment.AmountIsNet, target.ExemptionCap,
                        SocialTaxMinimumIncrease: shortfall),
                    target.Payment.Facts ?? payee);
                results[last] = target with { Result = withIncrease };
                topUpKey = target.Payment.Key;
            }
        }

        return new MonthAllocation(results, offered, remaining, minimumBase, due, shortfall, topUpKey);
    }

    /// <summary>A payment's own facts may differ from the month's only in what MTA evaluates per payment: residency and
    /// its country, the A1 case and its countries, the treaty rate. Pension status (month), pension age (year), the
    /// exemption application and the minimum (per payer-month) define the month's pools and must agree.</summary>
    private static void RequireMonthFactsAgree(string key, PayeeTaxFacts month, PayeeTaxFacts own)
    {
        var differences = new List<string>();
        if (own.FundedPensionMember != month.FundedPensionMember) differences.Add(nameof(PayeeTaxFacts.FundedPensionMember));
        if (own.PensionRate != month.PensionRate) differences.Add(nameof(PayeeTaxFacts.PensionRate));
        if (own.OnOldAgeOrFlexiblePension != month.OnOldAgeOrFlexiblePension) differences.Add(nameof(PayeeTaxFacts.OnOldAgeOrFlexiblePension));
        if (own.PensionAgeThisYear != month.PensionAgeThisYear) differences.Add(nameof(PayeeTaxFacts.PensionAgeThisYear));
        if (own.BasicExemptionApplied != month.BasicExemptionApplied) differences.Add(nameof(PayeeTaxFacts.BasicExemptionApplied));
        if (own.BasicExemptionMonthly != month.BasicExemptionMonthly) differences.Add(nameof(PayeeTaxFacts.BasicExemptionMonthly));
        if (own.SocialTaxMinimumApplies != month.SocialTaxMinimumApplies) differences.Add(nameof(PayeeTaxFacts.SocialTaxMinimumApplies));
        if (own.SocialTaxMinimumShare != month.SocialTaxMinimumShare) differences.Add(nameof(PayeeTaxFacts.SocialTaxMinimumShare));
        if (differences.Count > 0)
        {
            throw new InvalidOperationException(
                $"Payment {key}: its own facts differ from the month's facts on {string.Join(", ", differences)}, which define the month's pools; only residency, A1 and treaty facts may differ per payment.");
        }
    }
}
