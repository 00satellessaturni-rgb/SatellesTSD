using SatellesTSD.Core.Feedback;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Store;
using SatellesTSD.Core.Xbrl;

namespace SatellesTSD.Core.Declare;

/// <summary>A declaring refusal with a stable code the window can key on.</summary>
public sealed class DeclareException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
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
    public required string Key { get; init; }

    public Section Section { get; init; }

    public LineAction Action { get; init; }

    public LineChange Change { get; init; }

    /// <summary>The payment the line stands for, as the accountant knows it ("LHV pank *1757 [12]", "by hand", "minimum").</summary>
    public required string Source { get; init; }

    public string? Note { get; init; }
}

/// <summary>One person's month as the build made it: the lines, the month's figures, what was said.</summary>
public sealed class PersonBuild
{
    public Guid? PersonId { get; init; }

    public required string Name { get; init; }

    public IReadOnlyList<BuiltLine> Lines { get; init; } = [];

    public IReadOnlyList<string> Problems { get; init; } = [];

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

    public IReadOnlyList<string> Problems { get; init; } = [];

    public int Count(LineChange change) => Persons.Sum(p => p.Lines.Count(l => l.Change == change));
}

/// <summary>A standing line of the period as the grid shows it: the version, the person, the last word of the feedback.</summary>
public sealed class DeclaredLine
{
    public required StoredPayment Payment { get; init; }

    public Person? Person { get; init; }

    public LineFeedback? LastFeedback { get; init; }

    public int Versions { get; init; }
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

    public IReadOnlyDictionary<PaymentStatus, int> Statuses { get; init; } = new Dictionary<PaymentStatus, int>();
}
