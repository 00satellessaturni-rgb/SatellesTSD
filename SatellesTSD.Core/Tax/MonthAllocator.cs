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
            throw TaxRefusal.Of(PaymentChecks.ZeroNotAllowed, $"Payment type {type.Code} does not allow an amount of 0.", type.Code);
        }

        return new MonthPayment(key, type, 0m, AmountIsNet: false, date);
    }
}

/// <param name="ExemptionCap">The exemption that was available to this payment when it was computed.</param>
/// <param name="WorkAbilityReductionCap">The part of the month's kuumäär that was available to it as TSD_OPTV_SM_VAHENDUS.</param>
public sealed record AllocatedPayment(MonthPayment Payment, TaxResult Result, decimal ExemptionCap, decimal WorkAbilityReductionCap = 0m);

/// <summary>A person's month: every payment computed, plus the month-level figures a review needs to explain them.</summary>
/// <param name="ExemptionOffered">The exemption the month started with (the application's amount or the legal amount, or 0).</param>
/// <param name="ExemptionLeft">What was not used by any payment.</param>
/// <param name="MinimumBaseTotal">The social-tax bases of the payments that carry the minimum obligation, added up (a zero line counts as 0).</param>
/// <param name="MinimumDue">The minimum base owed this month: kuumäär × the month share; 0 when the minimum does not apply to any payment.</param>
/// <param name="MinimumShortfall">How far the total fell short of what is due; 0 when the minimum does not apply or is met.</param>
/// <param name="TopUpKey">The payment that carries TSD_KUUMSUURENDUS, or null.</param>
/// <param name="WorkAbilityReductionOffered">The part of the base the state pays the social tax on this month for a person
/// with partial or no work ability whose employer has applied (SMS § 6 lg 3): the month's amount the caller reckoned
/// (<see cref="WorkAbilityMonth"/>), one kuumäär where it gave none; else 0.</param>
/// <param name="WorkAbilityReductionLeft">What no payment took of it (the month's pay of column-O types was less, or none).</param>
public sealed record MonthAllocation(
    IReadOnlyList<AllocatedPayment> Payments,
    decimal ExemptionOffered,
    decimal ExemptionLeft,
    decimal MinimumBaseTotal,
    decimal MinimumDue,
    decimal MinimumShortfall,
    string? TopUpKey,
    decimal WorkAbilityReductionOffered = 0m,
    decimal WorkAbilityReductionLeft = 0m)
{
    /// <summary>TSD_OPTV_SM_VAHENDUS over the month's payments: never above <see cref="WorkAbilityReductionOffered"/>.</summary>
    public decimal WorkAbilityReductionTotal => Payments.Sum(p => p.Result.WorkAbilityReduction);
}

/// <summary>Spreads the month-level facts over a person's individual payments: the basic exemption (the application's
/// monthly amount or the legal 700/776, consumed in date order, each payment capped by its own amount − KP − TK) and the
/// social-tax minimum (if the bases of the payments that carry the obligation add up to less than the kuumäär owed for
/// the month, the shortfall becomes TSD_KUUMSUURENDUS on the last such payment). The workbook did both once per
/// person-month on the summed net; the new regime declares every payment on its own, so the spreading is explicit here.
/// The minimum compares pay received as an employee or official (SMS § 2 lg 2 with lg 1 p 1; types with matrix M = JAH:
/// 10, 33, 37, 120, 138, 167); a board fee (21/156) or a VÕS fee (17/123) never fills it, and MTA forbids the top-up on
/// those types (KR_SM_11). A month without such pay needs a <see cref="MonthPayment.Zero"/> line to carry the top-up.
/// For a person with partial or no work ability whose employer has applied, the month's part of the kuumäär is the state's
/// to pay the social tax on (SMS § 6 lg 1 p 5, lg 3): TSD_OPTV_SM_VAHENDUS, spread in date order over the payments of matrix
/// column O like the exemption, each capped at its own base. The part is the caller's to reckon (the month the pay is for,
/// its days under the application, decision 267); without one it is a whole kuumäär. Such a person owes no minimum
/// (SMS § 2 lg 4 p 6), so the two never meet.</summary>
public sealed class MonthAllocator
{
    private readonly TaxEngine _engine;

    public MonthAllocator(TaxEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    /// <summary>Computes every payment of one person in one month. Payments come back in date order (stable for equal dates).</summary>
    /// <param name="workAbilityBase">The part of the month's wages the state pays the social tax on, as the caller reckoned
    /// it for a person with both work-ability ticks (0 to two kuumäär, <see cref="WorkAbilityMonth.MostRates"/>); null = one
    /// whole kuumäär. Ignored for anyone else.</param>
    public MonthAllocation Allocate(IReadOnlyList<MonthPayment> payments, PayeeTaxFacts payee, decimal? workAbilityBase = null)
    {
        ArgumentNullException.ThrowIfNull(payments);
        ArgumentNullException.ThrowIfNull(payee);
        var rates = _engine.Rates;
        if (workAbilityBase is { } given && (given < 0m || given > WorkAbilityMonth.MostRates * rates.SocialTaxMinimumBase))
        {
            throw new ArgumentOutOfRangeException(nameof(workAbilityBase), $"The month's part of the kuumäär ({given}) must be between 0 and {WorkAbilityMonth.MostRates} × {rates.SocialTaxMinimumBase}.");
        }

        decimal legal = payee.PensionAgeThisYear ? rates.PensionAgeExemptionMonth : rates.BasicExemptionMonth;
        decimal offered = 0m;
        if (payee.BasicExemptionMonthly is { } applied)
        {
            if (!payee.BasicExemptionApplied)
            {
                throw TaxRefusal.Of(PaymentChecks.ExemptionWithoutApplication, "An exemption amount is given, but the person has not applied for the exemption at this payer; drop one of the two.");
            }

            if (applied < 0m || applied > legal)
            {
                throw TaxRefusal.Of(PaymentChecks.ExemptionAboveLegal, $"The exemption on the application ({applied}) must be between 0 and the legal {legal} per month (KR_KOOND_02/03).", figure: applied, other: legal);
            }

            if (applied != Math.Round(applied, 2))
            {
                throw TaxRefusal.Of(PaymentChecks.ExemptionSubCent, $"The exemption on the application ({applied}) must be a whole number of cents.", figure: applied);
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
            throw TaxRefusal.Of(PaymentChecks.MinimumShareInvalid, $"The month share of the minimum obligation ({payee.SocialTaxMinimumShare}) must be above 0 and at most 1; switch the minimum off for a whole month away.", figure: payee.SocialTaxMinimumShare);
        }

        if (payee.StatePaysSocialTax && !payee.ReducedWorkAbility)
        {
            throw TaxRefusal.Of(PaymentChecks.WorkAbilityApplicationWithoutStatus,
                "The application for Töötukassa to pay the social tax is ticked, but the person's partial or no work ability is not; the state pays only for such a person (SMS § 6 lg 1 p 5).");
        }

        // the month's part as the caller reckoned it (decision 267), else one whole kuumäär; the minimum's month share is the
        // minimum's own, and the minimum does not apply to the person anyway
        decimal reductionOffered = payee.ReducedWorkAbility && payee.StatePaysSocialTax ? workAbilityBase ?? rates.SocialTaxMinimumBase : 0m;

        if (payments.Count == 0)
        {
            return new MonthAllocation([], offered, offered, 0m, 0m, 0m, null, reductionOffered, reductionOffered);
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
        // the state's part goes like the exemption: the earliest payment first, each taking what its own base holds of what
        // is left; a payment of a type outside column O takes none and leaves it to the next (the owner's answer 3)
        decimal reductionLeft = reductionOffered;
        var results = new List<AllocatedPayment>(ordered.Count);
        foreach (var payment in ordered)
        {
            var facts = payment.Facts ?? payee;
            var result = _engine.Calculate(new TaxInput(payment.Type, payment.Amount, payment.AmountIsNet, remaining, WorkAbilityReductionCap: reductionLeft), facts);
            results.Add(new AllocatedPayment(payment, result, remaining, reductionLeft));
            remaining -= result.ExemptionUsed;
            reductionLeft -= result.WorkAbilityReduction;
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
                        SocialTaxMinimumIncrease: shortfall, WorkAbilityReductionCap: target.WorkAbilityReductionCap),
                    target.Payment.Facts ?? payee);
                results[last] = target with { Result = withIncrease };
                topUpKey = target.Payment.Key;
            }
        }

        return new MonthAllocation(results, offered, remaining, minimumBase, due, shortfall, topUpKey, reductionOffered, reductionLeft);
    }

    /// <summary>A payment's own facts may differ from the month's only in what MTA evaluates per payment: residency and
    /// its country, the A1 case and its countries, the treaty rate. Pension status (month), pension age (year), the
    /// exemption application, the minimum (per payer-month) and the work-ability status with its application define the
    /// month's pools and must agree.</summary>
    private static void RequireMonthFactsAgree(string key, PayeeTaxFacts month, PayeeTaxFacts own)
    {
        var differences = new List<string>();
        if (own.FundedPensionMember != month.FundedPensionMember) differences.Add(nameof(PayeeTaxFacts.FundedPensionMember));
        if (own.PensionRate != month.PensionRate) differences.Add(nameof(PayeeTaxFacts.PensionRate));
        if (own.OnOldAgeOrFlexiblePension != month.OnOldAgeOrFlexiblePension) differences.Add(nameof(PayeeTaxFacts.OnOldAgeOrFlexiblePension));
        if (own.PensionAgeThisYear != month.PensionAgeThisYear) differences.Add(nameof(PayeeTaxFacts.PensionAgeThisYear));
        if (own.PensionAgeReached != month.PensionAgeReached) differences.Add(nameof(PayeeTaxFacts.PensionAgeReached));
        if (own.BasicExemptionApplied != month.BasicExemptionApplied) differences.Add(nameof(PayeeTaxFacts.BasicExemptionApplied));
        if (own.BasicExemptionMonthly != month.BasicExemptionMonthly) differences.Add(nameof(PayeeTaxFacts.BasicExemptionMonthly));
        if (own.SocialTaxMinimumApplies != month.SocialTaxMinimumApplies) differences.Add(nameof(PayeeTaxFacts.SocialTaxMinimumApplies));
        if (own.SocialTaxMinimumShare != month.SocialTaxMinimumShare) differences.Add(nameof(PayeeTaxFacts.SocialTaxMinimumShare));
        if (own.ReducedWorkAbility != month.ReducedWorkAbility) differences.Add(nameof(PayeeTaxFacts.ReducedWorkAbility));
        if (own.StatePaysSocialTax != month.StatePaysSocialTax) differences.Add(nameof(PayeeTaxFacts.StatePaysSocialTax));
        if (differences.Count > 0)
        {
            throw new InvalidOperationException(
                $"Payment {key}: its own facts differ from the month's facts on {string.Join(", ", differences)}, which define the month's pools; only residency, A1 and treaty facts may differ per payment.");
        }
    }
}
