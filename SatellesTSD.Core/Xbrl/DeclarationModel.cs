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
    IReadOnlyList<SectionEntries> Sections)
{
    public DateOnly PeriodStart => new(Year, Month, 1);

    public DateOnly PeriodEnd => new(Year, Month, DateTime.DaysInMonth(Year, Month));

    /// <summary>Every section renumbered for this file (<see cref="SectionEntries.WithCounters"/>).</summary>
    public Declaration WithCounters() => this with { Sections = Sections.Select(s => s.WithCounters()).ToList() };

    /// <summary>gl-cor:uniqueID in the ISO 8601 form the taxonomy describes: registry code, date, 'T', time with milliseconds.</summary>
    public static string MakeUniqueId(string registryCode, DateTime createdAt) =>
        registryCode + "-" + createdAt.ToString("yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture);
}
