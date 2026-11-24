using SatellesTSD.Core.Model;

namespace SatellesTSD.Core.Tax;

/// <summary>What the engine must know about the payee for one month. These are the person's facts as the accountant
/// has established them (residency check, Pensionikeskus check, the person's application for the basic exemption);
/// the engine never looks them up. A fact that was never checked is null, and the engine refuses to compute a tax
/// that depends on it (the workbook refused unchecked residents the same way).</summary>
/// <param name="Residency">Resident or non-resident for tax purposes on the control date.</param>
/// <param name="ResidenceCountry">ISO country code of tax residence of a non-resident (RR_MAKSURESIDENTSUSE_RIIK): expected by MTA and checked against its register (KR_ISIK_6), optional in the schema — a line without it is declared and the build notes it.</param>
/// <param name="FundedPensionMember">II pillar contributions are being withheld (liitunud, maksed ei ole lõppenud); null = not checked.</param>
/// <param name="PensionRate">The person's own II-pillar rate (0.02, 0.04 or 0.06); null = unknown, refused wherever the pension is withheld.</param>
/// <param name="OnOldAgeOrFlexiblePension">On vanaduspension or paindlik vanaduspension as of the month before the payment: no TSD_TK (KR_TK_2).</param>
/// <param name="PensionAgeThisYear">Reaches old-age pension age in the calendar year (or already has): MVT_650 instead of MVT_610 (KR_MVT_04/05).</param>
/// <param name="BasicExemptionApplied">The person has asked this payer to apply the basic exemption (maksuvaba tulu avaldus).
/// For a non-resident this also asserts the EEA residency certificate the rules require (KR_MVT_06/07).</param>
/// <param name="BasicExemptionMonthly">The amount per month the person's application names for this payer (the workbook's
/// "Maksuvaba" column); null = the full legal amount. Never above the legal amount (KR_KOOND_02/03).</param>
/// <param name="SocialTaxMinimumApplies">The monthly minimum social-tax obligation applies to this person at this payer.
/// Off for the SMS § 2 lg 4 cases (state-pension recipient or reduced work capacity, a parent of a child under 3 or of
/// three children under 19, a pupil or student, a long-term unemployed in the first year, shortened working time,
/// a council member …) and for the other employer when the person has several (only the one applying the basic
/// exemption carries it) — the accountant decides.</param>
/// <param name="SocialTaxMinimumShare">The part of the month the obligation covers, 1 for a full month (SMS § 2 lg 3:
/// a person who starts or leaves mid-month, or is on leave, owes the minimum in proportion; määrus nr 17 § 6 counts
/// days / 30). 0 < share ≤ 1.</param>
/// <param name="A1">A1 / E101 situation. Only types with A1 variants react to it (<see cref="PaymentType.A1Allowed"/>).</param>
/// <param name="A1Country">A1_RIIGID_enum code of the issuing country (EL for Greece); required when A1 is not None.</param>
/// <param name="WorkCountry">RTK2T2013ap code of the place of work (RR_TOOKOHA_ASUKOHT); required when A1 is not None.</param>
/// <param name="TreatyIncomeTaxRate">A tax-treaty rate (fraction) that replaces the income-tax rate on types that allow it,
/// only when the non-resident holds a residency certificate.</param>
/// <param name="PensionAgeReached">Reached old-age pension age in a month before the payment's (<see cref="PensionAge"/>):
/// no TSD_TK, whether or not a pension is granted (Töötuskindlustuse seadus § 42 lg 1 p 1); the employer's TSD_TTK stays.</param>
/// <param name="ReducedWorkAbility">A person with partial or no work ability (osaline või puuduv töövõime, established under
/// the töövõimetoetuse seadus): the social-tax minimum does not apply, by the status alone (SMS § 2 lg 4 p 6) — the engine
/// never tops such a person up, whatever <paramref name="SocialTaxMinimumApplies"/> says. A disability alone is not this.</param>
/// <param name="StatePaysSocialTax">The employer has filed the application by which the state (Töötukassa) pays the social
/// tax on up to one kuumäär of the person's wages a month (SMS § 6 lg 1 p 5, lg 3): the part is TSD_OPTV_SM_VAHENDUS, on the
/// types of matrix column O. Only with <paramref name="ReducedWorkAbility"/> — the engine refuses it without
/// (<see cref="PaymentChecks.WorkAbilityApplicationWithoutStatus"/>). MTA accepts the part only when Töötukassa itself declared
/// the person in the same or the previous month (KR_SM_03), which the app cannot see.</param>
public sealed record PayeeTaxFacts(
    Residency Residency,
    string? ResidenceCountry = null,
    bool? FundedPensionMember = null,
    decimal? PensionRate = null,
    bool OnOldAgeOrFlexiblePension = false,
    bool PensionAgeThisYear = false,
    bool BasicExemptionApplied = false,
    decimal? BasicExemptionMonthly = null,
    bool SocialTaxMinimumApplies = true,
    decimal SocialTaxMinimumShare = 1m,
    A1Case A1 = A1Case.None,
    string? A1Country = null,
    string? WorkCountry = null,
    decimal? TreatyIncomeTaxRate = null,
    bool PensionAgeReached = false,
    bool ReducedWorkAbility = false,
    bool StatePaysSocialTax = false)
{
    /// <summary>A resident employee with the usual facts: II pillar checked and active, exemption applied in full, minimum applies.</summary>
    public static PayeeTaxFacts ResidentEmployee(decimal pensionRate = 0.02m, bool exemptionApplied = true) =>
        new(Residency.Resident, FundedPensionMember: true, PensionRate: pensionRate, BasicExemptionApplied: exemptionApplied);
}
