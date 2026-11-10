namespace SatellesTSD.Core.Review;

/// <summary>What a new amount typed on a payment does (decision 150).</summary>
public enum AmountChangeKind
{
    /// <summary>The row's own share given less: the difference is split off as a new part.</summary>
    SplitOff,

    /// <summary>A part given another amount: the difference comes from the row's own share, or goes back to it.</summary>
    PartResized,

    /// <summary>A part given 0: it goes, its amount back to the row's own share.</summary>
    PartRemoved,

    /// <summary>A line added by hand given another amount.</summary>
    HandLine,
}

/// <summary>A new amount's effect, told to the accountant before it is done.</summary>
/// <param name="From">The amount the payment has.</param>
/// <param name="To">The amount typed.</param>
/// <param name="RowShare">The row's own share afterwards; 0 for a line added by hand.</param>
/// <param name="Difference">For a split, the new part's amount; for a part resized, what it takes (above 0) or gives back
/// (below 0); for a part removed, its amount; for a hand line, the change.</param>
public sealed record AmountChange(AmountChangeKind Kind, decimal From, decimal To, decimal RowShare, decimal Difference);
