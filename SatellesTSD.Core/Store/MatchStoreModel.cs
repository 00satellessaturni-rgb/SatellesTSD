using SatellesTSD.Core.Matching;

namespace SatellesTSD.Core.Store;

/// <summary>A period's match run as stored: when it ran, the catalog it ran with (so a later edit of the categories
/// never re-reads stored verdicts), and its problems.</summary>
public sealed record StoredMatchRun(Guid Id, Guid PeriodId, DateTime RunAt, string CatalogJson, IReadOnlyList<MatchProblem> Problems)
{
    public bool Equals(StoredMatchRun? other) =>
        other is not null && Id == other.Id && PeriodId == other.PeriodId && RunAt == other.RunAt && CatalogJson == other.CatalogJson && Problems.SequenceEqual(other.Problems);

    public override int GetHashCode() => HashCode.Combine(Id, PeriodId, RunAt, Problems.Count);
}

/// <summary>A person of a stored run, with the register rows behind them.</summary>
public sealed record StoredMatchPerson(Guid Id, Guid RunId, int Order, string Key, string? PersonalCode, string LastName, string FirstName, Guid? ActiveEntryId, IReadOnlyList<Guid> EntryIds)
{
    public bool Equals(StoredMatchPerson? other) =>
        other is not null && Id == other.Id && RunId == other.RunId && Order == other.Order && Key == other.Key && PersonalCode == other.PersonalCode && LastName == other.LastName
        && FirstName == other.FirstName && ActiveEntryId == other.ActiveEntryId && EntryIds.SequenceEqual(other.EntryIds);

    public override int GetHashCode() => HashCode.Combine(Id, RunId, Order, Key, EntryIds.Count);
}

/// <summary>A source of a stored run: a stored statement sheet under the name the run gave it.</summary>
public sealed record StoredMatchSource(Guid Id, Guid RunId, int Order, Guid SheetId, string Name, string LayoutId);

/// <summary>One stored hit: a statement row, the person it was found for (or none), and the verdict as the run said it.</summary>
public sealed record StoredMatchHit(
    Guid Id,
    Guid RunId,
    int Order,
    Guid SourceId,
    Guid RowId,
    Guid? PersonId,
    string LastMask,
    string FirstMask,
    bool MaskUsed,
    VerdictKind VerdictKind,
    string? Category,
    IReadOnlyList<string> Matched,
    string Text,
    bool InPeriod,
    bool Counted,
    IReadOnlyList<string> Flags)
{
    public bool Equals(StoredMatchHit? other) =>
        other is not null && Id == other.Id && RunId == other.RunId && Order == other.Order && SourceId == other.SourceId && RowId == other.RowId && PersonId == other.PersonId
        && LastMask == other.LastMask && FirstMask == other.FirstMask && MaskUsed == other.MaskUsed && VerdictKind == other.VerdictKind && Category == other.Category
        && Matched.SequenceEqual(other.Matched, StringComparer.Ordinal) && Text == other.Text && InPeriod == other.InPeriod && Counted == other.Counted && Flags.SequenceEqual(other.Flags, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Id, RunId, Order, RowId, PersonId, Text);
}

/// <summary>The matching half of the store: the owner's match catalog and each period's run — the persons, the
/// sources and every hit, so that the review grid and the report read what the run said, and a re-run replaces it
/// whole while the period is a draft.</summary>
public interface IMatchStore
{
    /// <summary>The owner's match catalog document, or null when the embedded default is in force.</summary>
    string? SavedMatchCatalog();

    /// <summary>Saves the owner's document; refused when it cannot be used (<see cref="MatchCatalog.Parse"/>).</summary>
    void SaveMatchCatalog(string json);

    /// <summary>Back to the embedded default.</summary>
    void ClearMatchCatalog();

    /// <summary>Stores a run's result for the period, replacing an earlier run. Refused past Draft, when a source is not
    /// a sheet of a file linked to the period, or when a hit's row or a person's entry is not in the store.</summary>
    /// <param name="torSheetId">The register sheet the persons came from; null when the run had no register (every person's entries must then be empty).</param>
    StoredMatchRun SaveMatchRun(Guid periodId, Guid? torSheetId, MatchResult result);

    /// <summary>Forgets the period's run; refused past Draft.</summary>
    void ClearMatchRun(Guid periodId);

    StoredMatchRun? MatchRun(Guid periodId);

    IReadOnlyList<StoredMatchPerson> MatchPersons(Guid runId);

    IReadOnlyList<StoredMatchSource> MatchSources(Guid runId);

    /// <summary>Every hit of the run in the run's order: the employee pass, then the rows nobody matched.</summary>
    IReadOnlyList<StoredMatchHit> MatchHits(Guid runId);

    /// <summary>The period's run rebuilt as the matcher returned it (rows and register entries read back from the store).</summary>
    MatchResult? LoadMatchResult(Guid periodId);
}
