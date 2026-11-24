using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Model;

namespace SatellesTSD.Core.Tax;

/// <summary>The person's case the rules matrix is drawn for (decision 203): the facts that change which taxes a payment
/// carries. A1 stands for a posted worker's certificate; the country it names does not change the taxes. The work-ability
/// status takes the minimum off; the application under it allows TSD_OPTV_SM_VAHENDUS on the types of column O (only with
/// the status, as on the card).</summary>
public sealed record RuleCase(
    Residency Residency = Residency.Resident,
    bool FundedPensionMember = true,
    bool OldAgeOrFlexiblePension = false,
    bool PensionAgeThisYear = false,
    bool BasicExemptionApplied = true,
    bool SocialTaxMinimumApplies = true,
    A1Case A1 = A1Case.None,
    decimal? TreatyIncomeTaxRate = null,
    bool ReducedWorkAbility = false,
    bool StatePaysSocialTax = false)
{
    /// <summary>The engine's own facts for the case: the II-pillar rate is the default 2 %, the A1 countries a stand-in.</summary>
    public PayeeTaxFacts Facts => new(
        Residency,
        FundedPensionMember: FundedPensionMember,
        PensionRate: FundedPensionMember ? 0.02m : null,
        OnOldAgeOrFlexiblePension: OldAgeOrFlexiblePension,
        PensionAgeThisYear: PensionAgeThisYear,
        BasicExemptionApplied: BasicExemptionApplied,
        SocialTaxMinimumApplies: SocialTaxMinimumApplies,
        A1: A1,
        A1Country: A1 == A1Case.None ? null : "FI",
        WorkCountry: A1 == A1Case.None ? null : "FI",
        TreatyIncomeTaxRate: TreatyIncomeTaxRate,
        ReducedWorkAbility: ReducedWorkAbility,
        StatePaysSocialTax: ReducedWorkAbility && StatePaysSocialTax);
}

/// <summary>One payment type under one case: what the type carries by MTA's matrix, the employment types of the register
/// that declare a salary with it, and what the engine applies to it for the case (<see cref="EffectiveFlags.Of"/>, the very
/// rule the build computes with). <see cref="PayeeMatches"/> is false for a code of the other residency: the build
/// refuses it for the case (TYPE_PAYEE_MISMATCH). <see cref="TreatyRefused"/>: the case's treaty rate is one the build
/// refuses for the type (TREATY_RATE_INVALID), so no tax is computed for it.</summary>
public sealed record RuleRow(PaymentType Type, IReadOnlyList<EmploymentType> Employments, EffectiveFlags Flags, bool PayeeMatches, bool TreatyRefused = false)
{
    /// <summary>The section as the table shows it: 1 or 2.</summary>
    public string SectionNumber => Type.Section == Section.EE0201001 ? "1" : "2";

    /// <summary>Whether a search text is found in the row, every word of it: a word of digits is a code — the type's, the
    /// section's or an employment type's id, whole; another word is looked for in the code, the name and the employment
    /// types' names.</summary>
    public bool Matches(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        var words = search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var codes = new[] { Type.Code, SectionNumber }.Concat(Employments.Select(e => e.TorTypeId)).ToList();
        var text = string.Join(" ", new[] { Type.Code, Type.NameEt }.Concat(Employments.Select(e => e.Name)));
        return words.All(w => w.All(char.IsAsciiDigit)
            ? codes.Contains(w, StringComparer.Ordinal)
            : text.Contains(w, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>The rules matrix (decision 203): every payment type a private company declares against the taxes it carries,
/// for a case of the person, drawn from the tables and the engine the build uses — nothing of the law is written twice.
/// It stores nothing.</summary>
public static class RulesMatrix
{
    /// <summary>The rows for a case, in the table's own order.</summary>
    public static IReadOnlyList<RuleRow> Rows(RuleCase rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var facts = rule.Facts;
        return PaymentTypes.All.Select(type => new RuleRow(
            type,
            EmploymentTypes.All.Where(e => e.ResidentCode == type.Code || e.NonResidentCode == type.Code).ToList(),
            EffectiveFlags.Of(type, facts),
            type.Payee == rule.Residency,
            PaymentChecks.TreatyRateRefused(type, facts))).ToList();
    }
}
