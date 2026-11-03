using SatellesTSD.Core.Lookups;

namespace SatellesTSD.Core.Store;

/// <summary>The lookup half of the store: every answer the registers gave, per person, kept as it came.</summary>
public interface ILookupStore
{
    /// <summary>Appends an answer; the returned record carries the id and the time.</summary>
    LookupRecord AddLookup(LookupRecord record);

    /// <summary>Every answer for the person, oldest first.</summary>
    IReadOnlyList<LookupRecord> Lookups(Guid personId);

    /// <summary>The latest answer of this kind for this control date, if any.</summary>
    LookupRecord? LatestLookup(Guid personId, LookupKind kind, DateOnly controlDate);
}
