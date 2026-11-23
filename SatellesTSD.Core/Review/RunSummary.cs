using System.Text.Json;

namespace SatellesTSD.Core.Review;

/// <summary>What a search or a month's register check left to look at, as the summary window showed it: the problems
/// counted and grouped by kind, each kind with the persons or the rows it concerns. Kept in the store as it was shown
/// (one per company, the latest), so it can be opened again after the window was closed or the program quit — a record of
/// what was said, already worded; it is not worked out again from the month as it stands later.</summary>
/// <param name="MadeAt">When the summary was made (UTC).</param>
/// <param name="Searched">The month's search ran.</param>
/// <param name="CheckedRegisters">The month's register check ran (not the card's check of one person).</param>
/// <param name="CheckCompleted">The register check ran to its end; false when it was stopped or failed. True when no check ran.</param>
public sealed record RunSummary(
    Guid CompanyId,
    string CompanyName,
    string RegistryCode,
    Guid PeriodId,
    int Year,
    int Month,
    DateTime MadeAt,
    bool Searched,
    bool CheckedRegisters,
    bool CheckCompleted,
    IReadOnlyList<SummarySection> Sections)
{
    /// <summary>The search and the review's own problems.</summary>
    public const string SearchSection = "SEARCH";

    /// <summary>The payments' refusals and notices, as the check cell shows them.</summary>
    public const string PaymentsSection = "PAYMENTS";

    /// <summary>The register check's problems.</summary>
    public const string RegistersSection = "REGISTERS";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    /// <summary>Every occurrence of every kind.</summary>
    public int Total => Sections.Sum(s => s.Groups.Sum(g => g.Count));

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>The summary as it was saved; null for a text that is no summary (a store written by hand, a later shape).</summary>
    public static RunSummary? FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            return JsonSerializer.Deserialize<RunSummary>(json, Json) is { Sections: not null } summary ? summary : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public bool Equals(RunSummary? other) => other is not null && ToJson() == other.ToJson();

    public override int GetHashCode() => HashCode.Combine(CompanyId, PeriodId, MadeAt, Sections.Count);
}

/// <summary>A part of the summary: the search's problems, the payments', the registers'.</summary>
/// <param name="Kind">One of <see cref="RunSummary.SearchSection"/>, <see cref="RunSummary.PaymentsSection"/>, <see cref="RunSummary.RegistersSection"/>.</param>
/// <param name="Title">The section's name as shown.</param>
public sealed record SummarySection(string Kind, string Title, IReadOnlyList<SummaryGroup> Groups);

/// <summary>One kind of problem: its code verbatim (decision 46), its short name as shown (none for a code the window
/// does not know), and where it was found — one occurrence per person and payment or source row.</summary>
public sealed record SummaryGroup(string Code, string? Title, IReadOnlyList<SummaryOccurrence> Occurrences)
{
    public int Count => Occurrences.Count;
}

/// <summary>Where a problem was found: who (a person's names, or the file and row of a problem that belongs to no person),
/// what the window said of it, and — for a payment's — the payment's day and amount and the keys it is found again by.</summary>
/// <param name="PersonId">The store person, when the problem is a person's: the main window goes to them.</param>
/// <param name="RowId">The payment's statement row, when the problem is a payment's.</param>
/// <param name="LineId">The payment's own line (a part's, a line added by hand), when it has one.</param>
/// <param name="IsPart">The payment is a part split off its row.</param>
public sealed record SummaryOccurrence(string Who, string Detail, Guid? PersonId = null, DateOnly? Date = null, decimal? Amount = null, Guid? RowId = null, Guid? LineId = null, bool IsPart = false)
{
    public bool IsPayment => RowId is not null || LineId is not null;
}
