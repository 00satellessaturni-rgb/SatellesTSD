using SatellesTSD.Core.Feedback;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Store;
using SatellesTSD.Core.Xbrl;

namespace SatellesTSD.Core.Declare;

/// <summary>A declaring refusal with a stable code the window can key on.</summary>
public sealed class DeclareException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;

    /// <summary>The refusal's figure, for the window's own wording: the period's state (PERIOD_NOT_DRAFT, the enum's name),
    /// the year (RATES_MISSING), the line on the server (MODE_CHANGED); null when there is none.</summary>
    public string? Subject { get; init; }

    /// <summary>Which case of the code: RUN_STALE "notSearched" or "changed"; MODE_CHANGED the cut on the server,
    /// "perPayment" or "totals"; null when the code has one.</summary>
    public string? Variant { get; init; }

    /// <summary>The section the refusal speaks of: a stale line's (LINES_STALE), the one without a fitting reason
    /// (CORRECTION_REASON_NEEDED); null when there is none.</summary>
    public Section? Section { get; init; }

    /// <summary>A second figure, for the window's own wording: the key's rule a stale line broke (LINES_STALE, a
    /// <see cref="LineKeyException"/> code), the loader's own English of schemas not found (SCHEMAS_MISSING).</summary>
    public string? Detail { get; init; }

    /// <summary>What the file's own check found (INVALID_FILE, the structural case), each with its code and figures.</summary>
    public IReadOnlyList<DeclarationProblem> Problems { get; init; } = [];

    /// <summary>What MTA's schema found in the file (INVALID_FILE, the schema case).</summary>
    public IReadOnlyList<XbrlSchemaProblem> SchemaProblems { get; init; } = [];

    /// <summary>The keys a confirmation found open (awaiting MTA or needing a change), for the window's own wording.</summary>
    public IReadOnlyList<string> OpenLines { get; init; } = [];

    /// <summary>The keys a confirmation found refused by MTA and not on the server.</summary>
    public IReadOnlyList<string> RefusedLines { get; init; } = [];

    /// <summary>The keys a confirmation found drafted and never sent: a force does not pass them.</summary>
    public IReadOnlyList<string> DraftLines { get; init; } = [];

    /// <summary>The keys a confirmation found with a correction or cancellation MTA refused over a version the server
    /// holds, which still holds the earlier figures (decision 205).</summary>
    public IReadOnlyList<string> RefusedOverLines { get; init; } = [];
}

/// <summary>How the period's lines are cut: one per payment (what the review shows), or one per person and payment
/// type with the month's payments of that type added up and dated by the latest of them (the old TSD's shape).</summary>
public enum DeclarationMode
{
    PerPayment,
    TotalsPerType,
}

/// <summary>What a build did with one line of the period.</summary>
public enum LineChange
{
    /// <summary>A new R draft under a new key.</summary>
    New,
    /// <summary>A draft replaced in place (its figures followed the review).</summary>
    Replaced,
    /// <summary>An M draft on a key the server holds, because the figures changed.</summary>
    Changed,
    /// <summary>A T draft on a key the server holds, because the payment is no longer declared (or its section changed).</summary>
    Cancelled,
    /// <summary>Left as it was: on the server and unchanged, or waiting for a verdict.</summary>
    Kept,
    /// <summary>A draft deleted: the payment is no longer declared, or is refused.</summary>
    Deleted,
}

public sealed class BuiltLine
{
    /// <summary>A retired T draft's note: the copy it was to cancel is not on the server (any more).</summary>
    public const string NothingToCancel = "nothing on the server to cancel";

    /// <summary>A retired T draft's note: the cancellation stands drafted.</summary>
    public const string CancellationDrafted = "cancellation drafted";

    public required string Key { get; init; }

    public Section Section { get; init; }

    public LineAction Action { get; init; }

    public LineChange Change { get; init; }

    /// <summary>The payment the line stands for, as the accountant knows it ("LHV pank *1757 [12]", "by hand", "minimum").</summary>
    public required string Source { get; init; }

    public string? Note { get; init; }
}

/// <summary>A note or problem of the build: its code, the Core's English as the tests, the store and support read it
/// ("CODE: sentence", kept exactly), and the figures the window words it by in Estonian — which case of the code
/// (<see cref="Variant"/>), the payment's reference, the line's key and section, the types, the days, a count, the
/// references, codes or keys it names (<see cref="Items"/>), and the refusal behind it (<see cref="Cause"/>).</summary>
public sealed record BuildNote(string Code, string Text)
{
    /// <summary>Which case of the code, when it has several (ZERO_LINE "valid"/"ended"/"paused"/"asIfEmployed", say).</summary>
    public string? Variant { get; init; }

    /// <summary>The payment's reference as the build named it (a bank row's, "by hand", "minimum", "totals:…").</summary>
    public string? Reference { get; init; }

    /// <summary>The references, codes or keys the note names, each on its own.</summary>
    public IReadOnlyList<string> Items { get; init; } = [];

    /// <summary>The employment type of the register (TÖR) the note speaks of.</summary>
    public string? TypeId { get; init; }

    /// <summary>The payment type (väljamakse liik) the note speaks of: the zero line's.</summary>
    public string? PaymentType { get; init; }

    public DateOnly? Day { get; init; }

    public DateOnly? OtherDay { get; init; }

    public int? Count { get; init; }

    public Model.Section? Section { get; init; }

    /// <summary>The line's key.</summary>
    public string? Key { get; init; }

    /// <summary>A name, a code or a state the note quotes.</summary>
    public string? Subject { get; init; }

    /// <summary>ZERO_LINE: the type is the card's, for every payment.</summary>
    public bool FromCard { get; init; }

    /// <summary>INCLUDED: the card's TÖR type is asked for (no zero line could be typed).</summary>
    public bool AskCardType { get; init; }

    /// <summary>INCLUDED: why no zero line was made where one was looked for — "noTypeNoneDeclared", "noType",
    /// "typeNoMinimum", "a1" — or null.</summary>
    public string? Unmet { get; init; }

    /// <summary>The refusal behind the note (the engine's, the line factory's, the key's rules, the store's).</summary>
    public Exception? Cause { get; init; }

    public override string ToString() => Text;

    public bool Equals(BuildNote? other) =>
        other is not null && Code == other.Code && Text == other.Text && Variant == other.Variant && Reference == other.Reference
        && Items.SequenceEqual(other.Items, StringComparer.Ordinal) && TypeId == other.TypeId && PaymentType == other.PaymentType
        && Day == other.Day && OtherDay == other.OtherDay && Count == other.Count && Section == other.Section && Key == other.Key
        && Subject == other.Subject && FromCard == other.FromCard && AskCardType == other.AskCardType && Unmet == other.Unmet
        && ReferenceEquals(Cause, other.Cause);

    public override int GetHashCode() => HashCode.Combine(Code, Text, Variant, Reference, Key, Day, Count);
}

/// <summary>One person's month as the build made it: the lines, the month's figures, what was said.</summary>
public sealed class PersonBuild
{
    public Guid? PersonId { get; init; }

    public required string Name { get; init; }

    public IReadOnlyList<BuiltLine> Lines { get; init; } = [];

    /// <summary>What the build said of the person, coded, with the figures the window words it by.</summary>
    public IReadOnlyList<BuildNote> Notes { get; init; } = [];

    /// <summary>The notes' English, "CODE: sentence", as the tests and support read them.</summary>
    public IReadOnlyList<string> Problems => Notes.Select(n => n.Text).ToList();

    public decimal MinimumShare { get; init; } = 1m;

    public decimal ExemptionOffered { get; init; }

    public decimal ExemptionLeft { get; init; }

    public decimal MinimumDue { get; init; }

    public decimal MinimumShortfall { get; init; }

    public string? TopUpKey { get; init; }
}

public sealed class BuildReport
{
    public required Period Period { get; init; }

    public required IReadOnlyList<PersonBuild> Persons { get; init; }

    /// <summary>What the build said of the period, beyond a person (a line retired, a person with nothing built).</summary>
    public IReadOnlyList<BuildNote> Notes { get; init; } = [];

    /// <summary>The notes' English, "CODE: sentence".</summary>
    public IReadOnlyList<string> Problems => Notes.Select(n => n.Text).ToList();

    public int Count(LineChange change) => Persons.Sum(p => p.Lines.Count(l => l.Change == change));
}

/// <summary>A standing line of the period as the grid shows it: the version, the person, the last word of the feedback.</summary>
public sealed class DeclaredLine
{
    public required StoredPayment Payment { get; init; }

    public Person? Person { get; init; }

    public LineFeedback? LastFeedback { get; init; }

    public int Versions { get; init; }

    /// <summary>The latest version of the key refused after the one that stands: an M or T MTA refused (black, or settled
    /// as refused by hand) over a version the server holds, which so still holds the earlier figures (decision 205); null
    /// when the standing version is the key's latest, or is itself refused.</summary>
    public StoredPayment? RefusedAttempt { get; init; }

    /// <summary>The refused attempt's own last feedback (its black message).</summary>
    public LineFeedback? RefusedFeedback { get; init; }

    /// <summary>The version refused over the standing one, from the key's history; null when there is none.</summary>
    public static StoredPayment? RefusedOver(StoredPayment standing, IReadOnlyList<StoredPayment> history)
    {
        ArgumentNullException.ThrowIfNull(standing);
        ArgumentNullException.ThrowIfNull(history);
        return standing.Status == PaymentStatus.Rejected ? null
            : history.LastOrDefault(v => v.Version > standing.Version && v.Status == PaymentStatus.Rejected && !v.Settled);
    }
}

/// <summary>What the month's declaration adds up to: the net paid and the taxes the company owes MTA for it — the
/// employer's own (SM with the minimum's increase, TTK) and those it withholds (TM, TK, KP). Counted over the lines
/// that stand for the month: a draft, a line sent or on the server; never a refused attempt (not on the server), a
/// cancelled key, nor a T (it takes a line back, it owes nothing).</summary>
public sealed record DeclarationTotals(int Lines, decimal Net, decimal IncomeTax, decimal SocialTax, decimal FundedPension, decimal UnemploymentEmployee, decimal UnemploymentEmployer)
{
    public static readonly DeclarationTotals None = new(0, 0m, 0m, 0m, 0m, 0m, 0m);

    /// <summary>The taxes to pay: TM + SM + TK + TTK + KP.</summary>
    public decimal Taxes => IncomeTax + SocialTax + FundedPension + UnemploymentEmployee + UnemploymentEmployer;

    /// <summary>Whether the line counts toward the month's totals.</summary>
    public static bool Counts(StoredPayment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);
        return payment.Action != LineAction.T && payment.Result is not null && payment.Status is not (PaymentStatus.Rejected or PaymentStatus.Cancelled);
    }

    public static DeclarationTotals Of(IEnumerable<StoredPayment> payments)
    {
        ArgumentNullException.ThrowIfNull(payments);
        var total = None;
        foreach (var payment in payments.Where(Counts))
        {
            var r = payment.Result!;
            total = new DeclarationTotals(
                total.Lines + 1,
                total.Net + (payment.NetPaid ?? r.Net),
                total.IncomeTax + r.IncomeTax,
                total.SocialTax + r.SocialTax,
                total.FundedPension + r.FundedPension,
                total.UnemploymentEmployee + r.UnemploymentEmployee,
                total.UnemploymentEmployer + r.UnemploymentEmployer);
        }

        return total;
    }
}

public sealed class ExportResult
{
    public required string Path { get; init; }

    public required Submission Submission { get; init; }

    public required Declaration Declaration { get; init; }

    public int Lines { get; init; }
}

/// <summary>What one feedback report did when read.</summary>
public sealed class FeedbackOutcome
{
    public required string Source { get; init; }

    public ReportKind Kind { get; init; }

    /// <summary>A confirmation report's answer; such a report is said, never recorded on the lines.</summary>
    public ConfirmationOutcome? Confirmation { get; init; }

    public int Recorded { get; init; }

    /// <summary>The statuses of the versions the file carried, whatever stands on their keys now.</summary>
    public IReadOnlyDictionary<PaymentStatus, int> Statuses { get; init; } = new Dictionary<PaymentStatus, int>();

    /// <summary>The keys whose correction or cancellation in this file was refused while an earlier version stays on the
    /// server (decision 205).</summary>
    public IReadOnlyList<string> RefusedOverLines { get; init; } = [];

    /// <summary>The report's findings that point at no line (a file refusal, a section-level finding); a confirmation
    /// report's own answer is its <see cref="Confirmation"/>, not one of these.</summary>
    public IReadOnlyList<GeneralValidation> General { get; init; } = [];
}
