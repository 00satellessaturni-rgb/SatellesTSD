namespace SatellesTSD.Core.Tax;

/// <summary>The rates and monthly parameters the engine needs. Rates are fractions (0.22), amounts are euros.
/// The II-pillar rate is a fact of the person (2, 4 or 6 %), not a rate here.</summary>
/// <param name="IncomeTax">Tulumaks (standard rate; a payment type may carry its own, e.g. 10 % on code 180).</param>
/// <param name="UnemploymentEmployee">Kindlustatu töötuskindlustusmakse (TSD_TK).</param>
/// <param name="UnemploymentEmployer">Tööandja töötuskindlustusmakse (TSD_TTK).</param>
/// <param name="SocialTax">Sotsiaalmaks (TSD_SM).</param>
/// <param name="SocialTaxMinimumBase">Sotsiaalmaksu minimaalse kohustuse aluseks olev kuumäär (SM_KUUMAAR), per month.</param>
/// <param name="BasicExemptionMonth">MVT_610 per month (8400/yr in 2026 → 700).</param>
/// <param name="PensionAgeExemptionMonth">MVT_650 per month (9312/yr → 776).</param>
public sealed record TaxRates(
    decimal IncomeTax,
    decimal UnemploymentEmployee,
    decimal UnemploymentEmployer,
    decimal SocialTax,
    decimal SocialTaxMinimumBase,
    decimal BasicExemptionMonth,
    decimal PensionAgeExemptionMonth)
{
    /// <summary>The workbook's {taxes_config} for 2026 (CONFIG!B106:L106), which also matches MTA's calculator examples
    /// (886) — MTA's business example uses 820 for the minimum base; that is a known open question.</summary>
    public static readonly TaxRates Year2026 = new(
        IncomeTax: 0.22m,
        UnemploymentEmployee: 0.016m,
        UnemploymentEmployer: 0.008m,
        SocialTax: 0.33m,
        SocialTaxMinimumBase: 886m,
        BasicExemptionMonth: 700m,
        PensionAgeExemptionMonth: 776m);
}
