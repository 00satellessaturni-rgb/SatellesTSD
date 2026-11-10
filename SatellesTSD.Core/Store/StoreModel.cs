using SatellesTSD.Core.Feedback;
using SatellesTSD.Core.Lookups;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Tax;
using SatellesTSD.Core.Xbrl;

namespace SatellesTSD.Core.Store;

/// <summary>The accountant's word on declaring a person, kept with the person for every month until changed.</summary>
public enum DeclarationChoice
{
    /// <summary>As the rules say: the declared payments, and the minimum's zero line under a valid (Kehtiv) employment.</summary>
    Auto,

    /// <summary>Declared as if the employment were valid: a paused or ended one (or none in the register) still gets the
    /// minimum's zero line, and pay after the employment ended still owes the minimum, the other conditions holding.</summary>
    Include,

    /// <summary>Never declared: no line is built for the person, and what stands for them is undone (a draft goes, a
    /// line on the server gets its T).</summary>
    Exclude,
}

/// <summary>A client company the firm declares for.</summary>
/// <param name="PayerCodeType">How the file labels the company's code (ARIREGISTRIKOOD or MRR_KOOD), when the card says; null for the code's shape to decide (decision 139).</param>
public sealed record Company(Guid Id, string RegistryCode, string Name, string? Notes = null, string? PayerCodeType = null);

/// <summary>A payee of one company with the facts the engine needs, as the accountant last established them. The
/// "checked on" dates say how fresh the two lookups are. <see cref="FactsFor"/> turns the row into the month's
/// <see cref="PayeeTaxFacts"/>.</summary>
/// <param name="PensionAgeYear">The calendar year the person reaches old-age pension age (MVT_650 from 1 January of that year); null = not yet known or never.</param>
/// <param name="TorEmploymentTypeId">The TÖR töötamise liigi ID (1, 600, 601, 507, 602, 501) when the person is in the register.</param>
/// <param name="ResidencySource">Where the residency came from: a lookup, or the accountant's hand (which a later lookup never overwrites).</param>
/// <param name="PensionSource">Where the funded-pension facts came from, likewise.</param>
/// <param name="RegisterKey">The register key (the personal code as the register wrote it, else the names) the person was
/// created from or bound to, so a code or a name corrected by hand never splits the person from their register row.</param>
/// <param name="Declaration">Whether the person is declared as the rules say, always, or never (the person card's "Deklareerimine"; see <see cref="DeclarationChoice"/>).</param>
/// <param name="TypeFromCard">The card's <paramref name="TorEmploymentTypeId"/> stands for every payment of the person (the card's "Töötamise liik kõigile maksetele"), over the register's contract covering each payment's date.</param>
/// <param name="FactsControlDate">The control date the registers were last asked for when a lookup set the residency or the pension (a lookup of another month's period moves it); the build says so when it is outside the month built.</param>
public sealed record Person(
    Guid Id,
    Guid CompanyId,
    string FirstName,
    string LastName,
    string? PersonalCode,
    string? MrrCode,
    Residency Residency,
    string? ResidenceCountry,
    DateOnly? ResidencyCheckedOn,
    bool? FundedPensionMember,
    decimal? PensionRate,
    DateOnly? PensionCheckedOn,
    bool OnOldAgeOrFlexiblePension,
    int? PensionAgeYear,
    bool BasicExemptionApplied,
    decimal? BasicExemptionMonthly,
    bool SocialTaxMinimumApplies,
    A1Case A1,
    string? A1Country,
    string? WorkCountry,
    decimal? TreatyIncomeTaxRate,
    string? TorEmploymentTypeId,
    string? Notes,
    LookupSource ResidencySource = LookupSource.Unknown,
    LookupSource PensionSource = LookupSource.Unknown,
    string? RegisterKey = null,
    DeclarationChoice Declaration = DeclarationChoice.Auto,
    bool TypeFromCard = false,
    DateOnly? FactsControlDate = null)
{
    public static Person New(Guid companyId, string firstName, string lastName, string? personalCode, Residency residency = Residency.Resident) =>
        new(Guid.Empty, companyId, firstName, lastName, personalCode, null, residency, null, null, null, null, null,
            false, null, false, null, true, A1Case.None, null, null, null, null, null);

    /// <summary>The monthly exemption this person's lines would use in a year: the person's own figure, else the
    /// allowance in force (the pension-age one once the person has reached it), as the allocator reckons it.</summary>
    public decimal ExemptionMonthInForce(int year, TaxRates rates) =>
        BasicExemptionMonthly ?? (PensionAgeYear is { } pensionAgeYear && year >= pensionAgeYear ? rates.PensionAgeExemptionMonth : rates.BasicExemptionMonth);

    public PayeeTaxFacts FactsFor(int year, decimal socialTaxMinimumShare = 1m) => new(
        Residency, ResidenceCountry, FundedPensionMember, PensionRate, OnOldAgeOrFlexiblePension,
        PensionAgeYear is { } pensionAgeYear && year >= pensionAgeYear,
        BasicExemptionApplied, BasicExemptionMonthly, SocialTaxMinimumApplies, socialTaxMinimumShare,
        A1, A1Country, WorkCountry, TreatyIncomeTaxRate);

    /// <summary>How the person is named on the declaration: the Estonian personal code, else the MTA non-resident code.</summary>
    public PayeeId? PayeeId =>
        !string.IsNullOrWhiteSpace(PersonalCode) ? Xbrl.PayeeId.Estonian(PersonalCode)
        : !string.IsNullOrWhiteSpace(MrrCode) ? Xbrl.PayeeId.Mrr(MrrCode)
        : null;

    public string FullName => $"{FirstName} {LastName}".Trim();
}

/// <summary>One company's payment month and how far it has got.</summary>
public enum PeriodState
{
    Draft,
    Exported,
    Uploaded,
    Confirmed,
}

public sealed record Period(Guid Id, Guid CompanyId, int Year, int Month, PeriodState State, DateTime? ExportedAt, DateTime? UploadedAt, DateTime? ConfirmedAt);

/// <summary>Where one version of a declared line stands with MTA.</summary>
public enum PaymentStatus
{
    /// <summary>Built, not yet in a sent file.</summary>
    Draft,
    /// <summary>In an exported file; no feedback recorded yet. Presumed on the server.</summary>
    Sent,
    /// <summary>Feedback OK or a warning only.</summary>
    Accepted,
    /// <summary>Feedback blue or red: on the server, must be corrected with an M.</summary>
    NeedsChange,
    /// <summary>Feedback of unstated colour: on the server as far as is known; the accountant decides.</summary>
    Review,
    /// <summary>Feedback black: this version was not saved.</summary>
    Rejected,
    /// <summary>A T accepted: the key is dead for good.</summary>
    Cancelled,
}

/// <summary>One version of one declared line. The key (period, section, lineNumber) is permanent; each R, M or T sent
/// under it is a new version with its own status, so the history of what MTA was told is never lost.</summary>
/// <param name="Line">The line exactly as declared (or to be declared).</param>
/// <param name="Facts">The payee facts the figures were computed from; null for a T.</param>
/// <param name="Result">The engine's figures; null for a T.</param>
/// <param name="NetPaid">What the bank paid out when the line came from a statement.</param>
/// <param name="SourceRef">The bank line or report row the payment came from, for the accountant's eye ("LHV pank *1234 [12]").</param>
/// <param name="SourceRowId">The imported statement row the payment came from (<see cref="StoredStatementRow"/>); null for a line added by hand. Every version of a key keeps it.</param>
/// <param name="ReviewLineId">The review's hand line the payment came from; null for a bank row or a zero line. Every version of a key keeps it.</param>
public sealed record StoredPayment(
    Guid Id,
    Guid PeriodId,
    Guid? PersonId,
    Section Section,
    string LineNumber,
    int Version,
    LineAction Action,
    PaymentStatus Status,
    PaymentLine Line,
    PayeeTaxFacts? Facts,
    TaxResult? Result,
    decimal? NetPaid,
    string? SourceRef,
    Guid? SubmissionId,
    DateTime CreatedAt,
    Guid? SourceRowId = null,
    Guid? ReviewLineId = null)
{
    public static StoredPayment New(Guid periodId, Guid? personId, Section section, PaymentLine line, PayeeTaxFacts? facts, TaxResult? result, decimal? netPaid = null, string? sourceRef = null, Guid? sourceRowId = null, Guid? reviewLineId = null) =>
        new(Guid.Empty, periodId, personId, section, line.LineNumber, 0, line.Action, PaymentStatus.Draft, line, facts, result, netPaid, sourceRef, null, default, sourceRowId, reviewLineId);

    /// <summary>Whether this version is presumed to exist on MTA's side.</summary>
    public bool OnServer => Status is PaymentStatus.Sent or PaymentStatus.Accepted or PaymentStatus.NeedsChange or PaymentStatus.Review;

    /// <summary>Whether a verdict is still awaited: feedback (or the accountant's word) can still decide this version.</summary>
    public bool Pending => Status is PaymentStatus.Sent or PaymentStatus.Review;
}

/// <summary>One file sent (or exported for upload) for a period, and what came back.</summary>
/// <param name="General">The report's validations that pointed at no line (a file refusal, a section-level finding), from the last report recorded.</param>
/// <param name="FilePath">Where the file was written, when the store knows (an export since decision 152; the stored name is the whole path then): what a withdrawal sets aside.</param>
public sealed record Submission(Guid Id, Guid PeriodId, DateTime CreatedAt, string FileName, string UniqueId, string? Uuid, ProcessingResult? Result, string? FeedbackSource, IReadOnlyList<GeneralValidation> General, string? FilePath = null);

/// <summary>What one feedback report said about one stored payment version — everything the report carried for the
/// line, so the reason is never lost. Append-only: a repeated upload or a second report adds a row, it never replaces
/// the earlier verdict.</summary>
public sealed record LineFeedback(
    Guid PaymentId,
    Guid SubmissionId,
    DateTime RecordedAt,
    ValidationLevel Level,
    int? Counter,
    Severity Severity,
    NextAction NextAction,
    IReadOnlyList<ResponseItem> Messages,
    IReadOnlyDictionary<string, decimal> ServerTaxes,
    IReadOnlyList<ResponseItem> Unclassified);
