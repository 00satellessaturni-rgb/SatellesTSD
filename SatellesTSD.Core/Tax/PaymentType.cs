using SatellesTSD.Core.Model;

namespace SatellesTSD.Core.Tax;

/// <summary>One row of MTA's "väljamakseliikide maatrikstabel" (18.11.2025) as it applies to a private company:
/// the VALJAMLIIK2023ap code and which taxes the type carries, plus what its A1 variant rows change. The A1 and
/// non-resident modifiers are applied by <see cref="TaxEngine"/>, not stored here.</summary>
/// <param name="Code">VALJAMLIIK2023ap code, e.g. "10".</param>
/// <param name="NameEt">name_ET of the classifier element.</param>
/// <param name="Section">The data section the payment is declared in.</param>
/// <param name="Payee">Whether the code is for a resident or a non-resident payee (the matrix has separate codes).</param>
/// <param name="SocialTax">Matrix column L: sotsiaalmaks (33 %).</param>
/// <param name="SocialTaxMinimum">Matrix column M: the monthly minimum social-tax obligation (sotsiaalmaksu kuumäär) applies.</param>
/// <param name="ZeroAllowed">Matrix column N: an amount of 0 is allowed (used for a minimum-only line).</param>
/// <param name="UnemploymentInsurance">Matrix column Q: töötuskindlustusmakse, both TSD_TK and TSD_TTK.</param>
/// <param name="FundedPension">Matrix column R: kohustuslik kogumispension (TSD_KP); never for non-residents.</param>
/// <param name="IncomeTaxRate">Matrix column S: the income-tax rate as a fraction, or null when the type carries no income tax.</param>
/// <param name="BasicExemptionAllowed">Matrix column V: MVT_610 / MVT_650 may be applied.</param>
/// <param name="ThirdPillarExemptionAllowed">Matrix column W: MVT_640 may be applied.</param>
/// <param name="TreatyRateAllowed">Matrix column T: a tax-treaty rate may replace the income-tax rate (with a residency certificate).</param>
/// <param name="A1Kinds">Which A1 variant rows the type has (columns J/K). A person's A1 facts are ignored on a kind the type lacks.</param>
/// <param name="SocialTaxMinimumWithEstonianA1">Column M of the Estonian-A1 row (10, 120 and 138 keep the minimum; 33 loses it).</param>
public sealed record PaymentType(
    string Code,
    string NameEt,
    Section Section,
    Residency Payee,
    bool SocialTax,
    bool SocialTaxMinimum,
    bool ZeroAllowed,
    bool UnemploymentInsurance,
    bool FundedPension,
    decimal? IncomeTaxRate,
    bool BasicExemptionAllowed,
    bool ThirdPillarExemptionAllowed,
    bool TreatyRateAllowed,
    A1Kinds A1Kinds,
    bool SocialTaxMinimumWithEstonianA1)
{
    public bool IncomeTaxApplies => IncomeTaxRate is not null;

    /// <summary>The A1 case the type reacts to for this person: the person's own when the type has that variant, else none.</summary>
    public A1Case EffectiveA1(A1Case personal) => personal switch
    {
        A1Case.EstonianA1WorkAbroad when A1Kinds.HasFlag(A1Kinds.Estonian) => personal,
        A1Case.ForeignA1WorkInEstonia when A1Kinds.HasFlag(A1Kinds.Foreign) => personal,
        _ => A1Case.None,
    };

    public override string ToString() => $"{Code} {NameEt}";
}
