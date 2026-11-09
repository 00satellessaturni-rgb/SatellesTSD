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
public sealed record TaxInput(
    PaymentType Type,
    decimal Amount,
    bool AmountIsNet,
    decimal ExemptionCap,
    decimal SocialTaxMinimumIncrease = 0m);

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
    decimal ExemptionUsed)
{
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
    ExemptionKind ExemptionKind)
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
        bool socialTaxMinimum = socialTax && minimumOnType && payee.SocialTaxMinimumApplies;
        bool employerInsurance = type.UnemploymentInsurance && !foreignA1;
        bool employeeInsurance = employerInsurance && !payee.OnOldAgeOrFlexiblePension;

        bool fundedPension = false;
        if (type.FundedPension && !nonResident && !foreignA1)
        {
            fundedPension = payee.FundedPensionMember
                ?? throw new InvalidOperationException($"Payment type {type.Code} withholds the funded pension, but the payee's II-pillar status has not been checked.");
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

        return new EffectiveFlags(a1, socialTax, socialTaxMinimum, employeeInsurance, employerInsurance, fundedPension, incomeTaxRate, exemption);
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
            throw new ArgumentOutOfRangeException(nameof(input), "Refunds (negative amounts) are not supported.");
        }

        if (input.ExemptionCap < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "The exemption cap cannot be negative.");
        }

        if (input.SocialTaxMinimumIncrease < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "The social-tax minimum increase cannot be negative.");
        }

        if ((payee.Residency == Residency.NonResident) != (input.Type.Payee == Residency.NonResident))
        {
            throw new InvalidOperationException(
                $"Payment type {input.Type.Code} is for a {input.Type.Payee} payee; this payee is a {payee.Residency}.");
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
                ?? throw new InvalidOperationException($"Payment type {input.Type.Code} withholds the funded pension, but the payee's II-pillar rate is unknown.");
            if (p <= 0m || p >= 1m)
            {
                throw new InvalidOperationException($"The II-pillar rate {p} is not a fraction between 0 and 1.");
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
        decimal socialTax = flags.SocialTax ? Round2((socialTaxBase + increase) * _rates.SocialTax) : 0m;

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
            ExemptionUsed: w.Mvt);
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
