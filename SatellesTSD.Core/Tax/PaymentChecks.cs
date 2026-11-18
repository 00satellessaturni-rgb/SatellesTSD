using SatellesTSD.Core.Model;

namespace SatellesTSD.Core.Tax;

/// <summary>A reason a payment cannot be computed or declared as it stands, with a stable code the UI can key on.</summary>
/// <param name="Message">The Core's English (the tests and support read it).</param>
/// <param name="Args">The figures the window words the refusal with in Estonian, invariant text (a type code, a currency, an
/// amount "0.00", a rate as a fraction, an enum's name); null for a refusal that has none.</param>
public sealed record Refusal(string Code, string Message, IReadOnlyList<string>? Args = null)
{
    public bool Equals(Refusal? other) =>
        other is not null && Code == other.Code && Message == other.Message
        && (Args ?? []).SequenceEqual(other.Args ?? [], StringComparer.Ordinal) && (Args is null) == (other.Args is null);

    public override int GetHashCode() => HashCode.Combine(Code, Message, Args?.Count ?? -1);
}

/// <summary>The checks the engine, the allocator, the zero-line helper and the line factory enforce by throwing, as a
/// list a review grid can show per payment. Each refusal mirrors one of those throws; with no refusal, every one of
/// them computes for the facts given (<see cref="A1CountriesInconsistent"/> mirrors the line factory's). Two refusals mirror no throw: <see cref="ExemptionNotEea"/>, which MTA would
/// refuse and the engine cannot see, and <see cref="TreatyRateInvalid"/>, a rate the file cannot carry or no treaty gives.</summary>
public static class PaymentChecks
{
    public const string RatesExceedOne = "RATES_EXCEED_ONE";
    public const string NegativeAmount = "NEGATIVE_AMOUNT";
    public const string ZeroNotAllowed = "ZERO_NOT_ALLOWED";
    public const string TypePayeeMismatch = "TYPE_PAYEE_MISMATCH";
    public const string A1CountriesMissing = "A1_COUNTRIES_MISSING";

    /// <summary>An A1 case whose countries say the other case (MTA's KR_YLD2_07/09): an Estonian A1 has the A1 country EE
    /// and work abroad, a foreign A1 another A1 country and work in Estonia — else the file's classifiers and the taxes
    /// worked out for the case disagree (the fifth audit, items 14 and 21).</summary>
    public const string A1CountriesInconsistent = "A1_COUNTRIES_INCONSISTENT";
    public const string PensionNotChecked = "PENSION_NOT_CHECKED";
    public const string PensionRateUnknown = "PENSION_RATE_UNKNOWN";
    public const string PensionRateInvalid = "PENSION_RATE_INVALID";
    public const string ExemptionWithoutApplication = "EXEMPTION_WITHOUT_APPLICATION";
    public const string ExemptionAboveLegal = "EXEMPTION_ABOVE_LEGAL";
    public const string ExemptionSubCent = "EXEMPTION_SUB_CENT";
    public const string MinimumShareInvalid = "MINIMUM_SHARE_INVALID";

    /// <summary>A treaty income-tax rate the line would use that is below 0, above the type's own rate (a treaty never asks
    /// for more), or not a whole percent — the file carries the rate in hundredths (taxPercentageRate, decimals 2), so a
    /// rate of 12.5 % would be written as 13 % beside a tax computed at 12.5 %. Mirrors no throw (decision 195).</summary>
    public const string TreatyRateInvalid = "TREATY_RATE_INVALID";

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

    /// <summary>Whether a country of tax residence entered on the card is outside the EEA; an empty one is not (it is
    /// optional, decision 144).</summary>
    public static bool OutsideEea(string? country) => !string.IsNullOrWhiteSpace(country) && !EeaCountries.Contains(country.Trim());

    /// <summary>Whether the A1 case and its two countries agree (<see cref="A1CountriesInconsistent"/>); with no A1 case,
    /// or a country missing (<see cref="A1CountriesMissing"/> says that), they do.</summary>
    public static bool A1CountriesAgree(A1Case a1, string? a1Country, string? workCountry)
    {
        static bool Ee(string? c) => string.Equals(c?.Trim(), "EE", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(a1Country) || string.IsNullOrWhiteSpace(workCountry))
        {
            return true;
        }

        return a1 switch
        {
            A1Case.EstonianA1WorkAbroad => Ee(a1Country) && !Ee(workCountry),
            A1Case.ForeignA1WorkInEstonia => !Ee(a1Country) && Ee(workCountry),
            _ => true,
        };
    }

    /// <summary>An A1 country as the file writes it: trimmed, upper case, Greece's ISO code GR as EL, the spelling of
    /// MTA's A1 list (KR_YLD2_01).</summary>
    public static string A1Code(string country)
    {
        ArgumentNullException.ThrowIfNull(country);
        var code = country.Trim().ToUpperInvariant();
        return code == "GR" ? "EL" : code;
    }

    /// <summary>Whether the card's treaty rate is one the build refuses for the type (<see cref="TreatyRateInvalid"/>): a
    /// non-resident's rate on a type that takes one, below 0, above the type's own rate or not a whole percent.</summary>
    public static bool TreatyRateRefused(PaymentType type, PayeeTaxFacts facts) =>
        facts.Residency == Residency.NonResident && type.TreatyRateAllowed && type.IncomeTaxRate is { } ownRate
        && facts.TreatyIncomeTaxRate is { } treaty && (treaty < 0m || treaty > ownRate || treaty * 100m != Math.Round(treaty * 100m));

    /// <summary>A figure as a refusal carries it for the window (<see cref="Refusal.Args"/>): invariant text.</summary>
    public static string Invariant(decimal figure) => figure.ToString(System.Globalization.CultureInfo.InvariantCulture);

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
            refusals.Add(new Refusal(ZeroNotAllowed, $"Payment type {type.Code} does not allow an amount of 0.", [type.Code]));
        }

        if ((facts.Residency == Residency.NonResident) != (type.Payee == Residency.NonResident))
        {
            refusals.Add(new Refusal(TypePayeeMismatch, $"Payment type {type.Code} is for a {type.Payee} payee; this payee is a {facts.Residency}.", [type.Code, type.Payee.ToString(), facts.Residency.ToString()]));
        }

        // a non-resident without the residence country is declared: the country is expected by MTA, not required by the
        // schema; the build notes it (PeriodDeclarer.NoResidenceCountry) and MTA's register check (KR_ISIK_6) is the net

        var a1 = type.EffectiveA1(facts.A1);
        if (a1 != A1Case.None && (string.IsNullOrWhiteSpace(facts.A1Country) || string.IsNullOrWhiteSpace(facts.WorkCountry)))
        {
            refusals.Add(new Refusal(A1CountriesMissing, "An A1 case needs both the A1 country and the work country."));
        }
        else if (!A1CountriesAgree(a1, facts.A1Country, facts.WorkCountry))
        {
            refusals.Add(new Refusal(A1CountriesInconsistent, a1 == A1Case.EstonianA1WorkAbroad
                ? "An Estonian A1 needs the A1 country EE and a work country other than EE (KR_YLD2_09, KR_YLD2_07)."
                : "A foreign A1 needs an A1 country other than EE and the work country EE (KR_YLD2_07).", [a1.ToString()]));
        }

        bool pensionWithheld = type.FundedPension && facts.Residency == Residency.Resident && a1 != A1Case.ForeignA1WorkInEstonia;
        if (pensionWithheld)
        {
            if (facts.FundedPensionMember is null)
            {
                refusals.Add(new Refusal(PensionNotChecked, $"Payment type {type.Code} withholds the funded pension, but the payee's II-pillar status has not been checked.", [type.Code]));
            }
            else if (facts.FundedPensionMember == true)
            {
                if (facts.PensionRate is null)
                {
                    refusals.Add(new Refusal(PensionRateUnknown, $"Payment type {type.Code} withholds the funded pension, but the payee's II-pillar rate is unknown.", [type.Code]));
                }
                else if (facts.PensionRate <= 0m || facts.PensionRate >= 1m)
                {
                    refusals.Add(new Refusal(PensionRateInvalid, $"The II-pillar rate {facts.PensionRate} is not a fraction between 0 and 1.", [Invariant(facts.PensionRate.Value)]));
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
                refusals.Add(new Refusal(ExemptionAboveLegal, $"The exemption on the application ({applied}) must be between 0 and the legal {legal} per month.", [Invariant(applied), Invariant(legal)]));
            }

            if (applied != Math.Round(applied, 2))
            {
                refusals.Add(new Refusal(ExemptionSubCent, $"The exemption on the application ({applied}) must be a whole number of cents.", [Invariant(applied)]));
            }
        }

        // outside the amount's block above: the tick alone applies the full legal exemption. Asked of the engine's own
        // flags, so a payment it exempts nothing on (a 0 % treaty rate, an Estonian A1) is not refused; for a
        // non-resident the flags never throw (the funded pension is not theirs)
        if (facts.Residency == Residency.NonResident && facts.BasicExemptionApplied
            && OutsideEea(facts.ResidenceCountry)
            && EffectiveFlags.Of(type, facts).ExemptionKind != ExemptionKind.None)
        {
            refusals.Add(new Refusal(ExemptionNotEea, $"The basic exemption is applied, but the country of tax residence on the card ({facts.ResidenceCountry.Trim().ToUpperInvariant()}) is outside the EEA; a non-resident gets the exemption only as an EEA resident with a residency certificate (MTA refuses it, KR_MVT_06). Untick the exemption or correct the country.", [facts.ResidenceCountry.Trim().ToUpperInvariant()]));
        }

        if (TreatyRateRefused(type, facts) && type.IncomeTaxRate is { } ownRate && facts.TreatyIncomeTaxRate is { } treaty)
        {
            refusals.Add(new Refusal(TreatyRateInvalid, $"The treaty income-tax rate on the card ({treaty * 100m:0.####} %) must be a whole percent between 0 and the type's own {ownRate * 100m:0.##} %.", [Invariant(treaty * 100m), Invariant(ownRate * 100m)]));
        }

        if (facts.SocialTaxMinimumShare <= 0m || facts.SocialTaxMinimumShare > 1m)
        {
            refusals.Add(new Refusal(MinimumShareInvalid, $"The month share of the minimum obligation ({facts.SocialTaxMinimumShare}) must be above 0 and at most 1.", [Invariant(facts.SocialTaxMinimumShare)]));
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
