using SatellesTSD.Core.Model;

namespace SatellesTSD.Core.Tax;

/// <summary>One payment to compute.</summary>
/// <param name="Type">The payment type (decides which taxes exist).</param>
/// <param name="Amount">The amount, gross or net per <paramref name="AmountIsNet"/>. Never negative (refunds are not supported).</param>
/// <param name="AmountIsNet">True when the amount is what the bank paid out (the workbook's case); the engine grosses up.</param>
/// <param name="ExemptionCap">The basic exemption still available to this payment (the allocator spreads the month's amount
/// over the person's payments). 0 when none is left or none applies.</param>
/// <param name="SocialTaxMinimumIncrease">TSD_KUUMSUURENDUS: the increase of the social-tax base so that the month reaches the
/// minimum obligation. Only on a type that carries the minimum. 0 normally.</param>
/// <param name="WorkAbilityReductionCap">The part of the month's kuumäär still available to this payment as
/// TSD_OPTV_SM_VAHENDUS (the allocator spreads one kuumäär over the person's payments in date order), like
/// <paramref name="ExemptionCap"/>: the engine takes what the payment's social-tax base holds of it, on a type of matrix column
/// O for a person whose employer has applied (<see cref="PayeeTaxFacts.StatePaysSocialTax"/>), else nothing. Never above one
/// kuumäär.</param>
public sealed record TaxInput(
    PaymentType Type,
    decimal Amount,
    bool AmountIsNet,
    decimal ExemptionCap,
    decimal SocialTaxMinimumIncrease = 0m,
    decimal WorkAbilityReductionCap = 0m);

/// <summary>The computed figures of one payment. "Applies" flags say whether a tax exists for this type and payee at
/// all; <see cref="Xbrl.DeclarationLineFactory"/> decides which lines to write from them (a tax that applies is written
/// even at 0 when the payment itself is above 0; on a zero-amount minimum-only line only TSD_SM and TSD_SMVM appear).</summary>
public sealed record TaxResult(
    decimal Gross,
    decimal Net,
    A1Case EffectiveA1,
    bool SocialTaxApplies,
    bool SocialTaxMinimumApplies,
    decimal SocialTaxBase,
    decimal SocialTaxMinimumIncrease,
    decimal SocialTax,
    bool FundedPensionApplies,
    decimal FundedPensionRate,
    decimal FundedPension,
    bool UnemploymentEmployeeApplies,
    decimal UnemploymentEmployee,
    bool UnemploymentEmployerApplies,
    decimal UnemploymentEmployer,
    bool IncomeTaxApplies,
    decimal? IncomeTaxRate,
    decimal IncomeTaxBase,
    decimal IncomeTax,
    ExemptionKind ExemptionKind,
    decimal ExemptionUsed,
    bool WorkAbilityReductionApplies = false,
    decimal WorkAbilityReduction = 0m)
{
    // WorkAbilityReductionApplies: the type and the person allow TSD_OPTV_SM_VAHENDUS; WorkAbilityReduction: the part of the
    // social-tax base the state pays the tax on (SMS § 6 lg 3), taken off before the rate (KR_SM_04). Both last, with
    // defaults, so a stored result of a build before them reads as none.

    /// <summary>The social-tax base the employer's own TSD_SM is computed on: TSD_SMVM − TSD_OPTV_SM_VAHENDUS +
    /// TSD_KUUMSUURENDUS (KR_SM_04; the app declares no TSD_KUUMVAHENDUS or TSD_SMVMSK).</summary>
    public decimal SocialTaxComputedBase => SocialTaxBase - WorkAbilityReduction + SocialTaxMinimumIncrease;

    /// <summary>Gross plus the employer's own costs (TSD_TTK and TSD_SM), the workbook's "company expense".</summary>
    public decimal EmployerCost => Gross + UnemploymentEmployer + SocialTax;
}

/// <summary>Which taxes a payment of a given type to a given payee carries, after the A1, residency and pension
/// modifiers. Pure function of the two inputs; throws when a fact the type needs was never checked.</summary>
public readonly record struct EffectiveFlags(
    A1Case A1,
    bool SocialTax,
    bool SocialTaxMinimum,
    bool UnemploymentEmployee,
    bool UnemploymentEmployer,
    bool FundedPension,
    decimal? IncomeTaxRate,
    ExemptionKind ExemptionKind,
    bool WorkAbilityReduction = false)
{
    public static EffectiveFlags Of(PaymentType type, PayeeTaxFacts payee)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(payee);

        // A1 facts belong to the person; they change the taxes only on the types that have that A1 variant (matrix J/K;
        // KR_YLD2_02/03 refuse A1 entries on the others), e.g. rent paid to a posted worker is taxed as any rent.
        var a1 = type.EffectiveA1(payee.A1);
        bool foreignA1 = a1 == A1Case.ForeignA1WorkInEstonia;
        bool estonianA1 = a1 == A1Case.EstonianA1WorkAbroad;
        bool nonResident = payee.Residency == Residency.NonResident;

        bool socialTax = type.SocialTax && !foreignA1;
        bool minimumOnType = estonianA1 ? type.SocialTaxMinimumWithEstonianA1 : type.SocialTaxMinimum;
        // a person with partial or no work ability owes no minimum, by the status alone (SMS § 2 lg 4 p 6): the card's tick
        // is taken off with the status, and the engine holds the rule whatever the tick says
        bool socialTaxMinimum = socialTax && minimumOnType && payee.SocialTaxMinimumApplies && !payee.ReducedWorkAbility;
        // the state pays the social tax on up to one kuumäär of wages (SMS § 6 lg 1 p 5, lg 3) on the types of matrix column
        // O, for a person with the status whose employer has applied; the other social-taxed pay (a board fee, a VÕS fee)
        // is taxed in full (KR_SM_05)
        bool workAbilityReduction = socialTax && type.WorkAbilityReductionOn(a1) && payee.ReducedWorkAbility && payee.StatePaysSocialTax;
        bool employerInsurance = type.UnemploymentInsurance && !foreignA1;
        // the employee's 1.6 % ends with an old-age or flexible pension, and with the month the pension age is reached
        // (TKindlS § 42 lg 1 p 1, whether or not a pension is granted); the employer's 0.8 % stays
        bool employeeInsurance = employerInsurance && !payee.OnOldAgeOrFlexiblePension && !payee.PensionAgeReached;

        bool fundedPension = false;
        if (type.FundedPension && !nonResident && !foreignA1)
        {
            fundedPension = payee.FundedPensionMember
                ?? throw TaxRefusal.Of(PaymentChecks.PensionNotChecked, $"Payment type {type.Code} withholds the funded pension, but the payee's II-pillar status has not been checked.", type.Code);
        }

        decimal? incomeTaxRate = null;
        if (!estonianA1 && type.IncomeTaxRate is { } rate)
        {
            incomeTaxRate = nonResident && type.TreatyRateAllowed && payee.TreatyIncomeTaxRate is { } treaty ? treaty : rate;
        }

        var exemption = ExemptionKind.None;
        if (incomeTaxRate is > 0m && type.BasicExemptionAllowed && payee.BasicExemptionApplied) // a payment taxed at 0 % (a treaty rate) has nothing to exempt: the month's pool is left to the taxed ones
        {
            exemption = payee.PensionAgeThisYear ? ExemptionKind.Mvt650 : ExemptionKind.Mvt610;
        }

        return new EffectiveFlags(a1, socialTax, socialTaxMinimum, employeeInsurance, employerInsurance, fundedPension, incomeTaxRate, exemption, workAbilityReduction);
    }
}

/// <summary>Per-payment tax arithmetic for 2026. A port of the workbook's <c>cCalc2026</c>: the net → gross tiers
/// (no income tax while the net is within the exemption, else the closed formula), the re-derivation of gross as
/// net + withholdings after rounding, the exemption capped at amount − KP − TK (KR_MVT_08) — widened to MTA's matrix
/// flags, the A1 cases and non-residents. Rounding is commercial (half away from zero) by default: the only rounding
/// rule MTA ever wrote for the TSD (rahandusministri määrus nr 60, lisa 1, märkus 8, 2012 wording: a third decimal of
/// 5–9 rounds the cent up) is half-up, and the workbook's VBA <c>Round</c> on doubles followed no mode reliably
/// (1000.50 × 0.33 gave 330.16, 2000.50 × 0.33 gave 660.17). MTA's tolerance on tax figures is unpublished; the mode
/// is a constructor parameter.</summary>
public sealed class TaxEngine
{
    private readonly TaxRates _rates;
    private readonly MidpointRounding _rounding;

    public TaxEngine(TaxRates rates, MidpointRounding rounding = MidpointRounding.AwayFromZero)
    {
        _rates = rates ?? throw new ArgumentNullException(nameof(rates));
        _rounding = rounding;
    }

    public TaxRates Rates => _rates;

    public TaxResult Calculate(TaxInput input, PayeeTaxFacts payee)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(payee);
        if (input.Amount < 0)
        {
            throw TaxRefusal.Mark(new ArgumentOutOfRangeException(nameof(input), "Refunds (negative amounts) are not supported."), PaymentChecks.NegativeAmount);
        }

        if (input.ExemptionCap < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "The exemption cap cannot be negative.");
        }

        if (input.SocialTaxMinimumIncrease < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "The social-tax minimum increase cannot be negative.");
        }

        if (input.WorkAbilityReductionCap < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "The work-ability deduction's cap cannot be negative.");
        }

        if (input.WorkAbilityReductionCap > WorkAbilityMonth.MostRates * _rates.SocialTaxMinimumBase)
        {
            // one kuumäär for each month the pay is for (SMS § 6 lg 3 as MTA applies it); a declared month carries two at
            // most — holiday pay or a final pay beside the month's wages — which is MTA's own bound (KR_SM_07; decision 267)
            throw new ArgumentOutOfRangeException(nameof(input), $"The work-ability deduction's cap ({input.WorkAbilityReductionCap}) is above {WorkAbilityMonth.MostRates} × the kuumäär ({_rates.SocialTaxMinimumBase}).");
        }

        if (payee.StatePaysSocialTax && !payee.ReducedWorkAbility)
        {
            throw TaxRefusal.Of(PaymentChecks.WorkAbilityApplicationWithoutStatus,
                "The application for Töötukassa to pay the social tax is ticked, but the person's partial or no work ability is not; the state pays only for such a person (SMS § 6 lg 1 p 5).");
        }

        if (input.Type.ThirdPillarExemptionAllowed)
        {
            // the tax-free part MVT_640 is not computed: without it the income tax — and from a net amount the gross and
            // every tax on it — would be too high, so nothing is computed at all (decision 255)
            throw TaxRefusal.Of(PaymentChecks.ThirdPillarNotComputed,
                $"Payment type {input.Type.Code} has a tax-free part (MVT_640) the app does not compute; declare this payment by hand in e-MTA.", input.Type.Code);
        }

        if ((payee.Residency == Residency.NonResident) != (input.Type.Payee == Residency.NonResident))
        {
            throw TaxRefusal.Of(PaymentChecks.TypePayeeMismatch,
                $"Payment type {input.Type.Code} is for a {input.Type.Payee} payee; this payee is a {payee.Residency}.", input.Type.Code, detail: payee.Residency.ToString());
        }

        var flags = EffectiveFlags.Of(input.Type, payee);
        if (input.SocialTaxMinimumIncrease > 0 && !flags.SocialTaxMinimum)
        {
            throw new InvalidOperationException(
                $"Payment type {input.Type.Code} carries no monthly social-tax minimum for this payee; TSD_KUUMSUURENDUS is not allowed.");
        }

        decimal p = 0m;
        if (flags.FundedPension)
        {
            // KR_KOP_03 checks TSD_KP against the person's own rate (2, 4 or 6 %); a silent default would be a RED error.
            p = payee.PensionRate
                ?? throw TaxRefusal.Of(PaymentChecks.PensionRateUnknown, $"Payment type {input.Type.Code} withholds the funded pension, but the payee's II-pillar rate is unknown.", input.Type.Code);
            if (p <= 0m || p >= 1m)
            {
                throw TaxRefusal.Of(PaymentChecks.PensionRateInvalid, $"The II-pillar rate {p} is not a fraction between 0 and 1.", figure: p);
            }
        }

        decimal u = flags.UnemploymentEmployee ? _rates.UnemploymentEmployee : 0m;
        decimal ue = flags.UnemploymentEmployer ? _rates.UnemploymentEmployer : 0m;
        decimal r = flags.IncomeTaxRate ?? 0m;
        if (p + u + r >= 1m)
        {
            throw new InvalidOperationException("The withholding rates add up to 100 % or more; a net amount cannot be grossed up.");
        }

        decimal cap = flags.ExemptionKind == ExemptionKind.None ? 0m : Round2(input.ExemptionCap);

        decimal gross = input.AmountIsNet
            ? Round2(GrossFromNet(input.Amount, p, u, r, cap))
            : Round2(input.Amount);

        var w = Withholdings(gross, p, u, ue, r, cap, flags);
        if (input.AmountIsNet)
        {
            // cCalc2026: the gross is re-derived as net + the rounded withholdings, then everything is recomputed from it.
            gross = Round2(input.Amount) + w.Kp + w.Tk + w.Tm;
            w = Withholdings(gross, p, u, ue, r, cap, flags);
        }

        decimal net = gross - w.Kp - w.Tk - w.Tm;

        decimal socialTaxBase = flags.SocialTax ? gross : 0m;
        decimal increase = flags.SocialTax ? input.SocialTaxMinimumIncrease : 0m;
        // the state's part: what is left of the month's kuumäär, never more than the payment's own base (KR_SM_06; with no
        // TSD_KUUMVAHENDUS declared, KR_SM_10's OPTV + KUUMVAHENDUS ≤ SMVM is the same bound)
        decimal reduction = flags.WorkAbilityReduction ? Math.Max(0m, Math.Min(Round2(input.WorkAbilityReductionCap), socialTaxBase)) : 0m;
        decimal socialTax = flags.SocialTax ? Round2((socialTaxBase - reduction + increase) * _rates.SocialTax) : 0m;

        return new TaxResult(
            Gross: gross,
            Net: net,
            EffectiveA1: flags.A1,
            SocialTaxApplies: flags.SocialTax,
            SocialTaxMinimumApplies: flags.SocialTaxMinimum,
            SocialTaxBase: socialTaxBase,
            SocialTaxMinimumIncrease: increase,
            SocialTax: socialTax,
            FundedPensionApplies: flags.FundedPension,
            FundedPensionRate: p,
            FundedPension: w.Kp,
            UnemploymentEmployeeApplies: flags.UnemploymentEmployee,
            UnemploymentEmployee: w.Tk,
            UnemploymentEmployerApplies: flags.UnemploymentEmployer,
            UnemploymentEmployer: w.Ttk,
            IncomeTaxApplies: flags.IncomeTaxRate is not null,
            IncomeTaxRate: flags.IncomeTaxRate,
            IncomeTaxBase: w.TmBase,
            IncomeTax: w.Tm,
            ExemptionKind: flags.ExemptionKind,
            ExemptionUsed: w.Mvt,
            WorkAbilityReductionApplies: flags.WorkAbilityReduction,
            WorkAbilityReduction: reduction);
    }

    /// <summary>cCalc2026 tiers: no income tax while the net is within the exemption (gross = net / (1 − p − u)),
    /// otherwise the closed formula with the exemption; a type without income tax uses the first form.</summary>
    private static decimal GrossFromNet(decimal net, decimal p, decimal u, decimal r, decimal cap)
    {
        if (r == 0m || net <= cap)
        {
            return net / (1m - p - u);
        }

        return (net - r * cap) / (1m - p - u + r * (p + u - 1m));
    }

    private (decimal Kp, decimal Tk, decimal Ttk, decimal Mvt, decimal TmBase, decimal Tm) Withholdings(
        decimal gross, decimal p, decimal u, decimal ue, decimal r, decimal cap, EffectiveFlags flags)
    {
        decimal kp = Round2(gross * p);
        decimal tk = Round2(gross * u);
        decimal ttk = Round2(gross * ue);

        decimal mvt = 0m;
        if (flags.ExemptionKind != ExemptionKind.None)
        {
            // KR_MVT_08: the exemption may not exceed amount − TSD_KP − TSD_TK.
            mvt = Math.Min(cap, gross - kp - tk);
            if (mvt < 0m)
            {
                mvt = 0m;
            }

            mvt = Round2(mvt);
        }

        decimal tmBase = 0m;
        decimal tm = 0m;
        if (flags.IncomeTaxRate is not null)
        {
            tmBase = gross - kp - tk - mvt;
            if (tmBase < 0m)
            {
                tmBase = 0m;
            }

            tm = Round2(tmBase * r);
        }

        return (kp, tk, ttk, mvt, tmBase, tm);
    }

    private decimal Round2(decimal value) => Math.Round(value, 2, _rounding);
}
