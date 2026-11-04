using SatellesTSD.Core.Review;

namespace SatellesTSD.Core.Store;

/// <summary>The review half of the store: the accountant's decisions about a period's payments, kept apart from the
/// run (which a re-run replaces whole) and from the declared lines (which S8 builds from them).</summary>
public interface IReviewStore
{
    /// <summary>Saves a decision: a new one gets an id; an overlay keyed by (period, statement row) replaces the earlier
    /// one under its id. Refused past Draft; a row must be a payment row of a file linked to the period; a person must
    /// be of the period's company; a line added by hand needs a date, an amount of whole cents not below 0 and a payee.</summary>
    ReviewLine SaveReviewLine(ReviewLine line);

    /// <summary>Forgets a decision; refused past Draft.</summary>
    void DeleteReviewLine(Guid lineId);

    /// <summary>The period's decisions: the overlays first, then the lines added by hand, each in order of saving.</summary>
    IReadOnlyList<ReviewLine> ReviewLines(Guid periodId);
}
