using SatellesTSD.Core.Model;

namespace SatellesTSD.Core.Matching;

/// <summary>A TÖR "Töötamise liigi ID" and what the workbook's two matrices made of it: the payment-type code a
/// salary under it is declared with, for a resident and for a non-resident, and whether the social-tax monthly
/// minimum applies (the workbook's "SM min" flag, the default for the person's fact).</summary>
public sealed record EmploymentType(string TorTypeId, string Name, string? ResidentCode, string? NonResidentCode, bool SocialTaxMinimum)
{
    public string? CodeFor(Residency residency) => residency == Residency.NonResident ? NonResidentCode : ResidentCode;
}

/// <summary>The workbook's six employment types (CONFIG {tsdl1-tor-cat_matrix} and {tsdl2-tor-cat_matrix}) and
/// the one the real exports carry beyond them.</summary>
public static class EmploymentTypes
{
    public static readonly IReadOnlyList<EmploymentType> All =
    [
        new("1", "Tööleping", "10", "120", true),
        new("600", "VÕS leping", "17", "123", false),
        new("507", "Viisa alusel - tööleping", "10", "120", true),
        new("602", "Viisa alusel - VÕS leping", "17", "123", false),
        new("601", "Juhtimis-, kontrollorgani liige", "21", "156", false),
        new("501", "Tasuta töötamine", "10", "120", false),
        // beyond the workbook: employment taxed with income tax only (no social tax), as the register names it
        new("506", "Töötamine TM kohustusega Eestis ilma SM kohustuseta", "16", null, false),
    ];

    private static readonly Dictionary<string, EmploymentType> ById = All.ToDictionary(t => t.TorTypeId, StringComparer.Ordinal);

    public static EmploymentType? Find(string? torTypeId) => torTypeId is not null && ById.TryGetValue(torTypeId.Trim(), out var type) ? type : null;

    /// <summary>The payment-type code a salary under this employment is declared with; null when the type is unknown
    /// or has no code for the residency — the review must choose.</summary>
    public static string? DefaultCode(string? torTypeId, Residency residency) => Find(torTypeId)?.CodeFor(residency);
}
