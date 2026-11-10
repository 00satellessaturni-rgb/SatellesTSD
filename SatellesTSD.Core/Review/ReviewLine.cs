namespace SatellesTSD.Core.Review;

/// <summary>The accountant's decision about one payment of a period: a sparse overlay on the stored run, keyed by the
/// statement row the run found (so a re-run over the same files keeps every decision), or a line added by hand with
/// its own figures. A null field of an overlay means "as the run has it"; <see cref="NoPerson"/> and
/// <see cref="NoCategory"/> say "nobody" and "none" where the run found someone or something.</summary>
/// <param name="RowId">The stored statement row the decision is about; null for a line added by hand.</param>
/// <param name="PersonId">The store person the payment belongs to; null = the person the run found (or nobody).</param>
/// <param name="Category">The category by the accountant's word; null = the run's verdict.</param>
/// <param name="TypeCode">The payment type (VALJAMLIIK code) by the accountant's word; null = derived from the category and the person.</param>
/// <param name="Declare">Whether the payment is declared; null = the default (a typed, in-period, counted payment of a known person).</param>
/// <param name="Note">The accountant's note on the payment.</param>
/// <param name="Date">A hand line's payment date.</param>
/// <param name="Amount">A hand line's net amount (what was paid out); on a row, a part split off the row's payment
/// (decision 150): the row's own share is what the parts leave of the statement's amount.</param>
/// <param name="Payee">A hand line's payee as the accountant names them.</param>
/// <param name="Description">A hand line's description.</param>
/// <param name="NoPerson">The payment is nobody's by the accountant's word, though the run found someone (a namesake).</param>
/// <param name="NoCategory">The payment has no category by the accountant's word, though the run gave it one.</param>
public sealed record ReviewLine(
    Guid Id,
    Guid PeriodId,
    Guid? RowId,
    Guid? PersonId,
    string? Category,
    string? TypeCode,
    bool? Declare,
    string? Note,
    DateOnly? Date,
    decimal? Amount,
    string? Payee,
    string? Description,
    DateTime UpdatedAt,
    bool NoPerson = false,
    bool NoCategory = false)
{
    public bool IsByHand => RowId is null;

    /// <summary>A part split off a statement row's payment, carrying its own amount and decisions (decision 150); the row's
    /// own overlay is the one without an amount.</summary>
    public bool IsPart => RowId is not null && Amount is not null;

    /// <summary>An overlay that decides nothing any more; it is deleted rather than kept. A part is never empty: its amount
    /// is a decision.</summary>
    public bool IsEmpty => !IsByHand && !IsPart && PersonId is null && !NoPerson && Category is null && !NoCategory && TypeCode is null && Declare is null && Note is null;

    public static ReviewLine ForRow(Guid periodId, Guid rowId) =>
        new(Guid.Empty, periodId, rowId, null, null, null, null, null, null, null, null, null, default);

    /// <summary>A part split off the row's payment: its amount, and the person the row's own share has by the accountant's word.</summary>
    public static ReviewLine PartOf(Guid periodId, Guid rowId, decimal amount, Guid? personId = null, bool noPerson = false) =>
        new(Guid.Empty, periodId, rowId, personId, null, null, null, null, null, amount, null, null, default, noPerson);

    public static ReviewLine AddedByHand(Guid periodId, Guid? personId, DateOnly date, decimal amount, string payee, string? description = null, string? category = null, string? typeCode = null, bool? declare = null, string? note = null) =>
        new(Guid.Empty, periodId, null, personId, category, typeCode, declare, note, date, amount, payee, description, default);
}

/// <summary>A review refusal with a stable code the window can key on.</summary>
public sealed class ReviewException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}
