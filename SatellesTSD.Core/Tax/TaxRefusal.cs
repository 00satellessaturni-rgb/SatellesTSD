namespace SatellesTSD.Core.Tax;

/// <summary>A refusal of the month's computing, of a line's making or of the store's keeping of it, met by a build: a code
/// and the figures carried in the exception's Data, so the window words it in Estonian inside the build's note (NOT_ALLOCATED,
/// NOT_BUILT) or wherever it surfaces; the exception keeps its type and its English message (the same shape as
/// <see cref="Import.ReaderRefusal"/>). The engine's codes are the review's own (<see cref="PaymentChecks"/>).</summary>
public static class TaxRefusal
{
    /// <summary>The calculation date (TSD01) is later than the payment date (KR_YLD_25).</summary>
    public const string CalcDateAfterPayment = "CALC_DATE_AFTER_PAYMENT";

    /// <summary>The store: the bank row already feeds another line; the subject is that line's section and key.</summary>
    public const string RowTaken = "STORE_ROW_TAKEN";

    /// <summary>The store: the key already points at another bank row.</summary>
    public const string KeyOtherRow = "STORE_KEY_OTHER_ROW";

    /// <summary>The store: the key already points at another hand line.</summary>
    public const string KeyOtherLine = "STORE_KEY_OTHER_LINE";

    /// <summary>The store: the bank row belongs to another company's file.</summary>
    public const string RowOtherCompany = "STORE_ROW_OTHER_COMPANY";

    /// <summary>The store: the bank row is not a payment row; the subject is its skip reason.</summary>
    public const string RowNotPayment = "STORE_ROW_NOT_PAYMENT";

    /// <summary>The store: only a draft is replaced or deleted; the subject is the version's status (none for a delete).</summary>
    public const string NotDraft = "STORE_NOT_DRAFT";

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        PaymentChecks.ZeroNotAllowed, PaymentChecks.ExemptionWithoutApplication, PaymentChecks.ExemptionAboveLegal, PaymentChecks.ExemptionSubCent,
        PaymentChecks.MinimumShareInvalid, PaymentChecks.PensionNotChecked, PaymentChecks.PensionRateUnknown, PaymentChecks.PensionRateInvalid,
        PaymentChecks.TypePayeeMismatch, PaymentChecks.A1CountriesMissing, PaymentChecks.A1CountriesInconsistent, PaymentChecks.NegativeAmount,
        CalcDateAfterPayment, RowTaken, KeyOtherRow, KeyOtherLine, RowOtherCompany, RowNotPayment, NotDraft,
    };

    /// <summary>Every code this refusal carries.</summary>
    public static IReadOnlyCollection<string> Codes => Known;

    public static InvalidOperationException Of(string code, string message, string? subject = null, decimal? figure = null, decimal? other = null, string? detail = null) =>
        Mark(new InvalidOperationException(message), code, subject, figure, other, detail);

    /// <summary>The code and figures put on an exception of another type (an argument's refusal).</summary>
    public static T Mark<T>(T refusal, string code, string? subject = null, decimal? figure = null, decimal? other = null, string? detail = null) where T : Exception
    {
        ArgumentNullException.ThrowIfNull(refusal);
        refusal.Data["Code"] = code;
        refusal.Data["Subject"] = subject;
        refusal.Data["Figure"] = figure;
        refusal.Data["Other"] = other;
        refusal.Data["Detail"] = detail;
        return refusal;
    }

    public static string? CodeOf(Exception? e) => e?.Data["Code"] as string is { } code && Known.Contains(code) ? code : null;

    public static string? SubjectOf(Exception? e) => e?.Data["Subject"] as string;

    public static decimal? FigureOf(Exception? e) => e?.Data["Figure"] as decimal?;

    public static decimal? OtherOf(Exception? e) => e?.Data["Other"] as decimal?;

    public static string? DetailOf(Exception? e) => e?.Data["Detail"] as string;
}
