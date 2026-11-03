using SatellesTSD.Core.Xbrl;

namespace SatellesTSD.Core.Confirmation;

/// <summary>declarationType of the confirmation message.</summary>
public enum DeclarationKind
{
    TSD,
    ESD,
    XF11,
    XF12,
}

/// <summary>The TSD sections a confirmation names (confirmations XSD, SectionTypeEnum). PAYMENTS is the data-based part
/// (former lisa 1 / lisa 2); L3…L8 and INF1 are the annexes that stay form-based until 2027–2028.</summary>
public static class TsdSections
{
    public const string Payments = "PAYMENTS";

    public static readonly IReadOnlyList<string> All = ["PAYMENTS", "L3", "L4", "L5", "L6", "L7", "L8", "INF1"];
}

/// <summary>The TSD tax totals of a confirmation (TaxTypeEnum), in the order MTA's sample lists them.</summary>
public static class TaxTotalCodes
{
    public const string IncomeTaxWithheld = "110";
    public const string SpecialCaseIncomeTax = "114";
    public const string SocialTax = "115";
    public const string UnemploymentInsurance = "116";
    public const string FundedPension = "117";
    public const string TotalObligation = "118";
    public const string Refundable = "119";

    public static readonly IReadOnlyList<string> All = [IncomeTaxWithheld, SpecialCaseIncomeTax, SocialTax, UnemploymentInsurance, FundedPension, TotalObligation, Refundable];

    public static readonly IReadOnlyDictionary<string, string> NamesEt = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [IncomeTaxWithheld] = "Kinnipeetud tulumaks",
        [SpecialCaseIncomeTax] = "Erijuhtude tulumaks",
        [SocialTax] = "Sotsiaalmaks",
        [UnemploymentInsurance] = "Töötuskindlustusmakse",
        [FundedPension] = "Kohustusliku kogumispensioni makse",
        [TotalObligation] = "Maksukohustus kokku",
        [Refundable] = "Kuulub tagastamisele",
    };
}

public sealed record SectionItem(string Section, bool HasData);

public sealed record TaxSum(string TaxType, decimal Amount);

/// <summary>The confirmDeclaration message (confirmations-2026-09-23.xsd). It is what goes inside the signed or
/// e-sealed ASiC-E container; the signing itself is not done here.</summary>
/// <param name="PersonCode">The confirmer's 11-digit personal code, used when the container is e-sealed rather than signed; else null.</param>
/// <param name="DeclarationText">"Kinnitan andmete õigsust" (1–500 characters).</param>
/// <param name="TaxablePersonIdentifier">The taxpayer's code (registry code for a company).</param>
/// <param name="TaxablePersonIdentifierType">ARIREGISTRIKOOD, ISIKUKOOD or MRR_KOOD.</param>
public sealed record ConfirmDeclaration(
    string? PersonCode,
    string DeclarationText,
    string TaxablePersonIdentifier,
    string TaxablePersonIdentifierType,
    DeclarationKind Kind,
    int Month,
    int Year,
    IReadOnlyList<SectionItem> Sections,
    IReadOnlyList<TaxSum> TaxSums)
{
    public const string DefaultText = "Kinnitan andmete õigsust";

    /// <summary>A TSD confirmation for a company. The TSD is confirmed as one whole declaration with all its annexes:
    /// which sections hold data (PAYMENTS, and any of L3…L8 / INF1 the accountant filed by hand in e-MTA) and the seven
    /// totals of the declaration being confirmed are facts the accountant supplies — e-MTA's summary form shows them;
    /// the app has no way to know the annexes or MTA's own figures. Every code of <see cref="TaxTotalCodes.All"/> must be
    /// given; nothing is filled in.</summary>
    public static ConfirmDeclaration ForTsd(
        string registryCode,
        int year,
        int month,
        IReadOnlySet<string> sectionsWithData,
        IReadOnlyDictionary<string, decimal> totals,
        string? personCode = null,
        string? declarationText = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registryCode);
        ArgumentNullException.ThrowIfNull(sectionsWithData);
        ArgumentNullException.ThrowIfNull(totals);
        var unknown = sectionsWithData.Where(s => !TsdSections.All.Contains(s, StringComparer.Ordinal)).ToList();
        if (unknown.Count > 0)
        {
            throw new ArgumentException($"Not a TSD section: {string.Join(", ", unknown)}.", nameof(sectionsWithData));
        }

        var missing = TaxTotalCodes.All.Where(code => !totals.ContainsKey(code)).ToList();
        if (missing.Count > 0)
        {
            throw new ArgumentException($"Every TSD total must be given; missing: {string.Join(", ", missing)}.", nameof(totals));
        }

        var sections = TsdSections.All.Select(s => new SectionItem(s, sectionsWithData.Contains(s))).ToList();
        var sums = TaxTotalCodes.All.Select(code => new TaxSum(code, totals[code])).ToList();
        return new ConfirmDeclaration(personCode, declarationText ?? DefaultText, registryCode, PayeeId.Ariregistrikood, DeclarationKind.TSD, month, year, sections, sums);
    }
}

/// <summary>The taxes ONE FILE declares, added up by TSD total code — a cross-check against e-MTA's figures, never the
/// confirmation's input: a month may arrive in several files, an M replaces an earlier version rather than adding to it,
/// MTA leaves blue lines out of its summary, and MTA's own calculation decides. A correction file of refunds adds up
/// to negative figures, which is fine here (the confirmation writer refuses negatives).</summary>
public static class DeclaredTaxTotals
{
    public static IReadOnlyDictionary<string, decimal> OfFile(Declaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        decimal Sum(params string[] codes) =>
            declaration.Sections.SelectMany(s => s.Lines).SelectMany(l => l.Taxes).Where(t => codes.Contains(t.Code, StringComparer.Ordinal)).Sum(t => t.Amount);

        var incomeTax = Sum(TaxLine.IncomeTax);
        var socialTax = Sum(TaxLine.SocialTax);
        var insurance = Sum(TaxLine.UnemploymentEmployee, TaxLine.UnemploymentEmployer);
        var pension = Sum(TaxLine.FundedPension);
        return new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            [TaxTotalCodes.IncomeTaxWithheld] = incomeTax,
            [TaxTotalCodes.SpecialCaseIncomeTax] = 0m,
            [TaxTotalCodes.SocialTax] = socialTax,
            [TaxTotalCodes.UnemploymentInsurance] = insurance,
            [TaxTotalCodes.FundedPension] = pension,
            [TaxTotalCodes.TotalObligation] = incomeTax + socialTax + insurance + pension,
            [TaxTotalCodes.Refundable] = 0m,
        };
    }
}
