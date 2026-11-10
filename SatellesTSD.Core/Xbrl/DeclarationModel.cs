using System.Globalization;
using SatellesTSD.Core.Model;

namespace SatellesTSD.Core.Xbrl;

/// <summary>One gl-cor:accountSub entry other than the payment type and the action: a compound classifier value
/// (RTK2T2013ap country under a RIIGIROLL2022ap role, A1_RIIGID_enum country under RR_A1, or a TL2015ap ship).</summary>
/// <param name="Id">accountSubID, e.g. "FI".</param>
/// <param name="Type">accountSubType, e.g. "RTK2T2013ap".</param>
/// <param name="ParentCode">segmentParentTuple/parentSubaccountCode, e.g. "RR_TOOKOHA_ASUKOHT" (or an IMO number).</param>
/// <param name="ParentType">segmentParentTuple/parentSubaccountType, "RIIGIROLL2022ap"; absent for TL2015ap.</param>
public sealed record AccountSubEntry(string Id, string Type, string? ParentCode = null, string? ParentType = null)
{
    public const string CountryClassifier = "RTK2T2013ap";
    public const string A1Classifier = "A1_RIIGID_enum";
    public const string RoleClassifier = "RIIGIROLL2022ap";
    public const string RoleA1 = "RR_A1";
    public const string RoleWorkLocation = "RR_TOOKOHA_ASUKOHT";
    public const string RoleTaxResidence = "RR_MAKSURESIDENTSUSE_RIIK";

    public static AccountSubEntry A1Country(string country) => new(country, A1Classifier, RoleA1, RoleClassifier);
    public static AccountSubEntry WorkCountry(string country) => new(country, CountryClassifier, RoleWorkLocation, RoleClassifier);
    public static AccountSubEntry ResidenceCountry(string country) => new(country, CountryClassifier, RoleTaxResidence, RoleClassifier);
}

/// <summary>gl-cor:identifierReference: who received the payment.</summary>
public sealed record PayeeId(string Code, string Description)
{
    public const string Isikukood = "ISIKUKOOD";
    public const string MrrKood = "MRR_KOOD";
    public const string Ariregistrikood = "ARIREGISTRIKOOD";

    /// <summary>An Estonian personal code: the mandatory two-letter country prefix plus the code.</summary>
    public static PayeeId Estonian(string isikukood) => Personal("EE", isikukood);

    public static PayeeId Personal(string countryCode, string personalCode) => new(countryCode + personalCode, Isikukood);

    /// <summary>MTA non-resident register code, without a country prefix.</summary>
    public static PayeeId Mrr(string code) => new(code, MrrKood);

    public static PayeeId Registry(string registryCode) => new(registryCode, Ariregistrikood);
}

/// <summary>The payer's code as the file labels it (gl-bus:organizationDescription, decision 139): an Äriregister code is
/// eight digits beginning with 1, 7, 8 or 9; an Estonian personal code (a FIE's) eleven digits beginning with 1 to 6; any
/// other code is MTA's non-resident register code (a Lithuanian MB's, say) — unless the company card says which.</summary>
public static class PayerCodes
{
    public static readonly IReadOnlyList<string> Labels = [PayeeId.Ariregistrikood, PayeeId.Isikukood, PayeeId.MrrKood];

    public static bool LooksEstonianRegistry(string registryCode) =>
        registryCode?.Trim() is { Length: 8 } code && code.All(char.IsAsciiDigit) && code[0] is '1' or '7' or '8' or '9';

    public static bool LooksEstonianPersonal(string registryCode) =>
        registryCode?.Trim() is { Length: 11 } code && code.All(char.IsAsciiDigit) && code[0] is >= '1' and <= '6';

    /// <param name="chosen">The company card's own word (one of <see cref="Labels"/>), which wins over the code's shape; null for the shape.</param>
    public static string DescriptionOf(string registryCode, string? chosen = null) =>
        chosen is not null && Labels.Contains(chosen) ? chosen
        : LooksEstonianRegistry(registryCode) ? PayeeId.Ariregistrikood
        : LooksEstonianPersonal(registryCode) ? PayeeId.Isikukood
        : PayeeId.MrrKood;
}

/// <summary>The MTA non-resident code's shape, as far as it is certain (decision 155): letters and digits only — MTA's
/// example is A0003689779 — and no country prefix before it. The length is not known and not checked.</summary>
public static class MrrCodes
{
    private static readonly Lazy<HashSet<string>> Countries = new(() =>
    {
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "EL", "UK", "XK" };
        foreach (var culture in System.Globalization.CultureInfo.GetCultures(System.Globalization.CultureTypes.SpecificCultures))
        {
            try
            {
                codes.Add(new System.Globalization.RegionInfo(culture.Name).TwoLetterISORegionName);
            }
            catch (ArgumentException)
            {
                // a culture without a region of its own
            }
        }

        return codes;
    });

    /// <summary>What is wrong with the code for certain, or null: a space, a hyphen or any character that is no letter or
    /// digit. A blank code has no problem — it is no code.</summary>
    public static string? Problem(string? code)
    {
        var text = code?.Trim();
        if (string.IsNullOrEmpty(text) || text.All(char.IsAsciiLetterOrDigit))
        {
            return null;
        }

        return $"'{text}' is not an MTA non-resident code: it holds Latin letters A–Z and digits only, without spaces or other signs.";
    }

    /// <summary>The country code the text begins with, when it looks like one before the code proper (two letters that
    /// are a country's, then more): MTA takes the code without a prefix. A hint for a question, not a certainty — a
    /// code may begin with a letter of its own.</summary>
    public static string? CountryPrefix(string? code)
    {
        var text = code?.Trim();
        return text is { Length: > 2 } && char.IsAsciiLetter(text[0]) && char.IsAsciiLetter(text[1]) && Countries.Value.Contains(text[..2])
            ? text[..2].ToUpperInvariant()
            : null;
    }
}

/// <summary>A SYNDMUS2017ap event block: TSD01 (arvestuse kuupäev) on every payment, TSD02 on refunds.</summary>
public sealed record MeasurableEvent(string Id, DateOnly Start, DateOnly? End = null)
{
    public const string Schema = "SYNDMUS2017ap";
    public const string CalculationDate = "TSD01";
    public const string RefundOriginalDate = "TSD02";
}

/// <summary>A MEASURABLEDESCRIPTION_enum amount block (TSD_SMVM, MVT_610, MVT_650, MVT_640, TSD_KUUMSUURENDUS, …).</summary>
public sealed record MeasurableAmount(string Description, decimal Quantity, bool Money = true)
{
    public const string SocialTaxBase = "TSD_SMVM";
    public const string SocialTaxMinimumIncrease = "TSD_KUUMSUURENDUS";
    public const string Mvt610 = "MVT_610";
    public const string Mvt640 = "MVT_640";
    public const string Mvt650 = "MVT_650";
}

/// <summary>A gl-cor:taxes block. The rate is a fraction and is required for TSD_KTM on a non-resident.</summary>
public sealed record TaxLine(string Code, decimal Amount, decimal? Rate = null)
{
    public const string SocialTax = "TSD_SM";
    public const string FundedPension = "TSD_KP";
    public const string UnemploymentEmployee = "TSD_TK";
    public const string UnemploymentEmployer = "TSD_TTK";
    public const string IncomeTax = "TSD_KTM";
}

/// <summary>One gl-cor:entryDetail. For a cancellation (T) only the key, the counter and the action are set.</summary>
public sealed record PaymentLine(
    string LineNumber,
    int Counter,
    LineAction Action,
    string? TypeCode,
    IReadOnlyList<AccountSubEntry> ExtraAccountSubs,
    decimal? Amount,
    string? AllocationCode,
    PayeeId? Payee,
    DateOnly? DocumentDate,
    IReadOnlyList<MeasurableEvent> Events,
    IReadOnlyList<MeasurableAmount> Amounts,
    IReadOnlyList<TaxLine> Taxes)
{
    public static PaymentLine Cancellation(string lineNumber, int counter) =>
        new(lineNumber, counter, LineAction.T, null, [], null, null, null, null, [], [], []);
}

/// <summary>One gl-cor:entryHeader: a data section with its payments. An empty section is sent as the header alone.</summary>
/// <param name="CorrectionReason">ENTRYCOMMENT_enum value (PARANDUSPOHJUS_1..4) when correcting confirmed data, else null.</param>
public sealed record SectionEntries(Section Section, string? CorrectionReason, IReadOnlyList<PaymentLine> Lines)
{
    /// <summary>The same section with lineNumberCounter set to each line's position (1..n) in this file. The counter is
    /// per file, not a property of the payment: MTA's feedback points back by it.</summary>
    public SectionEntries WithCounters() =>
        this with { Lines = Lines.Select((line, i) => line with { Counter = i + 1 }).ToList() };
}

/// <summary>One file: one company, one payment month.</summary>
public sealed record Declaration(
    string RegistryCode,
    int Year,
    int Month,
    DateOnly CreationDate,
    string UniqueId,
    string Creator,
    string SourceApplication,
    string? EntriesComment,
    IReadOnlyList<SectionEntries> Sections,
    string RegistryCodeType = PayeeId.Ariregistrikood)
{
    public DateOnly PeriodStart => new(Year, Month, 1);

    public DateOnly PeriodEnd => new(Year, Month, DateTime.DaysInMonth(Year, Month));

    /// <summary>Every section renumbered for this file (<see cref="SectionEntries.WithCounters"/>).</summary>
    public Declaration WithCounters() => this with { Sections = Sections.Select(s => s.WithCounters()).ToList() };

    /// <summary>gl-cor:uniqueID in the ISO 8601 form the taxonomy describes: registry code, date, 'T', time with milliseconds.</summary>
    public static string MakeUniqueId(string registryCode, DateTime createdAt) =>
        registryCode + "-" + createdAt.ToString("yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture);
}
