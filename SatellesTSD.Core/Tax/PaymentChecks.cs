using SatellesTSD.Core.Model;

namespace SatellesTSD.Core.Tax;

/// <summary>A reason a payment cannot be computed or declared as it stands, with a stable code the UI can key on.</summary>
public sealed record Refusal(string Code, string Message);

/// <summary>The checks the engine, the allocator, the zero-line helper and the line factory enforce by throwing, as a
/// list a review grid can show per payment. Each refusal mirrors one of those throws; with no refusal, every one of
/// them computes for the facts given. One refusal mirrors no throw: <see cref="ExemptionNotEea"/>, which MTA would
/// refuse and the engine cannot see.</summary>
public static class PaymentChecks
{
    public const string RatesExceedOne = "RATES_EXCEED_ONE";
    public const string NegativeAmount = "NEGATIVE_AMOUNT";
    public const string ZeroNotAllowed = "ZERO_NOT_ALLOWED";
    public const string TypePayeeMismatch = "TYPE_PAYEE_MISMATCH";
    public const string A1CountriesMissing = "A1_COUNTRIES_MISSING";
    public const string PensionNotChecked = "PENSION_NOT_CHECKED";
    public const string PensionRateUnknown = "PENSION_RATE_UNKNOWN";
    public const string PensionRateInvalid = "PENSION_RATE_INVALID";
    public const string ExemptionWithoutApplication = "EXEMPTION_WITHOUT_APPLICATION";
    public const string ExemptionAboveLegal = "EXEMPTION_ABOVE_LEGAL";
    public const string ExemptionSubCent = "EXEMPTION_SUB_CENT";
    public const string MinimumShareInvalid = "MINIMUM_SHARE_INVALID";

    /// <summary>A non-resident's exemption beside a residence country entered on the card that is outside the EEA: the
    /// card contradicts itself, and MTA refuses the exemption (KR_MVT_06). An empty country refuses nothing — it is
    /// optional, and MTA judges by its own register (decision 144).</summary>
    public const string ExemptionNotEea = "EXEMPTION_NOT_EEA";

    /// <summary>The EEA states by ISO 3166 alpha-2 code: the EU's twenty-seven, Iceland, Liechtenstein, Norway. Greece
    /// also as EL, the code MTA's A1 list uses.</summary>
    public static readonly IReadOnlySet<string> EeaCountries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "AT", "BE", "BG", "HR", "CY", "CZ", "DK", "EE", "FI", "FR", "DE", "GR", "EL", "HU", "IE", "IT", "LV", "LT", "LU", "MT",
        "NL", "PL", "PT", "RO", "SK", "SI", "ES", "SE", "IS", "LI", "NO",
    };

    public static IReadOnlyList<Refusal> Check(PaymentType type, PayeeTaxFacts facts, decimal amount, TaxRates rates)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(rates);
        var refusals = new List<Refusal>();

        if (amount < 0m)
        {
            refusals.Add(new Refusal(NegativeAmount, "Refunds (negative amounts) are not supported."));
        }
        else if (amount == 0m && !type.ZeroAllowed)
        {
            refusals.Add(new Refusal(ZeroNotAllowed, $"Payment type {type.Code} does not allow an amount of 0."));
        }

        if ((facts.Residency == Residency.NonResident) != (type.Payee == Residency.NonResident))
        {
            refusals.Add(new Refusal(TypePayeeMismatch, $"Payment type {type.Code} is for a {type.Payee} payee; this payee is a {facts.Residency}."));
        }

        // a non-resident without the residence country is declared: the country is expected by MTA, not required by the
        // schema; the build notes it (PeriodDeclarer.NoResidenceCountry) and MTA's register check (KR_ISIK_6) is the net

        var a1 = type.EffectiveA1(facts.A1);
        if (a1 != A1Case.None && (string.IsNullOrWhiteSpace(facts.A1Country) || string.IsNullOrWhiteSpace(facts.WorkCountry)))
        {
            refusals.Add(new Refusal(A1CountriesMissing, "An A1 case needs both the A1 country and the work country."));
        }

        bool pensionWithheld = type.FundedPension && facts.Residency == Residency.Resident && a1 != A1Case.ForeignA1WorkInEstonia;
        if (pensionWithheld)
        {
            if (facts.FundedPensionMember is null)
            {
                refusals.Add(new Refusal(PensionNotChecked, $"Payment type {type.Code} withholds the funded pension, but the payee's II-pillar status has not been checked."));
            }
            else if (facts.FundedPensionMember == true)
            {
                if (facts.PensionRate is null)
                {
                    refusals.Add(new Refusal(PensionRateUnknown, $"Payment type {type.Code} withholds the funded pension, but the payee's II-pillar rate is unknown."));
                }
                else if (facts.PensionRate <= 0m || facts.PensionRate >= 1m)
                {
                    refusals.Add(new Refusal(PensionRateInvalid, $"The II-pillar rate {facts.PensionRate} is not a fraction between 0 and 1."));
                }
            }
        }

        if (facts.BasicExemptionMonthly is { } applied)
        {
            decimal legal = facts.PensionAgeThisYear ? rates.PensionAgeExemptionMonth : rates.BasicExemptionMonth;
            if (!facts.BasicExemptionApplied)
            {
                refusals.Add(new Refusal(ExemptionWithoutApplication, "An exemption amount is given, but the person has not applied for the exemption at this payer."));
            }

            if (applied < 0m || applied > legal)
            {
                refusals.Add(new Refusal(ExemptionAboveLegal, $"The exemption on the application ({applied}) must be between 0 and the legal {legal} per month."));
            }

            if (applied != Math.Round(applied, 2))
            {
                refusals.Add(new Refusal(ExemptionSubCent, $"The exemption on the application ({applied}) must be a whole number of cents."));
            }
        }

        // outside the amount's block above: the tick alone applies the full legal exemption. Asked of the engine's own
        // flags, so a payment it exempts nothing on (a 0 % treaty rate, an Estonian A1) is not refused; for a
        // non-resident the flags never throw (the funded pension is not theirs)
        if (facts.Residency == Residency.NonResident && facts.BasicExemptionApplied
            && !string.IsNullOrWhiteSpace(facts.ResidenceCountry) && !EeaCountries.Contains(facts.ResidenceCountry.Trim())
            && EffectiveFlags.Of(type, facts).ExemptionKind != ExemptionKind.None)
        {
            refusals.Add(new Refusal(ExemptionNotEea, $"The basic exemption is applied, but the country of tax residence on the card ({facts.ResidenceCountry.Trim().ToUpperInvariant()}) is outside the EEA; a non-resident gets the exemption only as an EEA resident with a residency certificate (MTA refuses it, KR_MVT_06). Untick the exemption or correct the country."));
        }

        if (facts.SocialTaxMinimumShare <= 0m || facts.SocialTaxMinimumShare > 1m)
        {
            refusals.Add(new Refusal(MinimumShareInvalid, $"The month share of the minimum obligation ({facts.SocialTaxMinimumShare}) must be above 0 and at most 1."));
        }

        if (refusals.Count == 0)
        {
            var flags = EffectiveFlags.Of(type, facts);
            decimal p = flags.FundedPension ? facts.PensionRate ?? 0m : 0m;
            decimal u = flags.UnemploymentEmployee ? rates.UnemploymentEmployee : 0m;
            decimal r = flags.IncomeTaxRate ?? 0m;
            if (p + u + r >= 1m)
            {
                refusals.Add(new Refusal(RatesExceedOne, "The withholding rates add up to 100 % or more; a net amount cannot be grossed up."));
            }
        }

        return refusals;
    }
}
