namespace SatellesTSD.Core.Declare;

/// <summary>A refusal on the submission and feedback path (the store's, the feedback reader's, the system's when a file
/// is written or read) by code, carried in the exception's Data so the window words it in Estonian, the Core's English
/// kept as the message for the log, the tests and support; the exception keeps the type it is thrown as today.</summary>
public static class SubmissionRefusal
{
    /// <summary>A line of the new file is no draft of the period any more (another window exported it meanwhile).</summary>
    public const string NotDraft = "SUBMIT_NOT_DRAFT";

    /// <summary>The file has a verdict on it (feedback, a line settled by hand): it was uploaded and cannot be withdrawn.</summary>
    public const string WithdrawAnswered = "SUBMIT_WITHDRAW_ANSWERED";

    /// <summary>A line settled by hand is not Sent or Review; the subject is its status (the enum's name).</summary>
    public const string NotPending = "SUBMIT_NOT_PENDING";

    /// <summary>The export's file could not be written (the system's refusal); the subject is the folder.</summary>
    public const string WriteFailed = "SUBMIT_WRITE_FAILED";

    /// <summary>The report's file could not be opened (in use, no access): the system's refusal.</summary>
    public const string FileUnopened = "FEEDBACK_FILE_UNOPENED";

    /// <summary>The file is neither readable XML nor a ZIP; the detail is the .NET text.</summary>
    public const string Unreadable = "FEEDBACK_UNREADABLE";

    /// <summary>The ZIP holds no XML report.</summary>
    public const string ZipEmpty = "FEEDBACK_ZIP_EMPTY";

    /// <summary>The XML has no root element.</summary>
    public const string NoRoot = "FEEDBACK_NO_ROOT";

    /// <summary>The root is not &lt;processing&gt;; the subject is its name.</summary>
    public const string NotReport = "FEEDBACK_NOT_REPORT";

    /// <summary>The report has no entryHeader; the subject is its raw result.</summary>
    public const string NoEntry = "FEEDBACK_NO_ENTRY";

    /// <summary>A lineReference has no lineInternalId.</summary>
    public const string LineNoId = "FEEDBACK_LINE_NO_ID";

    /// <summary>A confirmation report handed to the store as a submission's feedback.</summary>
    public const string Confirmation = "FEEDBACK_CONFIRMATION";

    /// <summary>The submission has no lines in the store.</summary>
    public const string NoLines = "FEEDBACK_NO_LINES";

    /// <summary>The report names lines the file did not carry; the items are "section/key".</summary>
    public const string OtherFile = "FEEDBACK_OTHER_FILE";

    /// <summary>The report places lines elsewhere than the file did; the items are "section/key|report's counter|file's counter".</summary>
    public const string PlacedElsewhere = "FEEDBACK_PLACED_ELSEWHERE";

    /// <summary>Every code this path gives.</summary>
    public static readonly IReadOnlyList<string> Codes =
        [NotDraft, WithdrawAnswered, NotPending, WriteFailed, FileUnopened, Unreadable, ZipEmpty, NoRoot, NotReport, NoEntry, LineNoId, Confirmation, NoLines, OtherFile, PlacedElsewhere];

    /// <summary>The refusal marked with its code and figures, the exception itself returned (thrown as it is, or rethrown).</summary>
    public static T Of<T>(T refusal, string code, string? subject = null, string? detail = null, IReadOnlyList<string>? items = null) where T : Exception
    {
        ArgumentNullException.ThrowIfNull(refusal);
        refusal.Data["Code"] = code;
        refusal.Data["Subject"] = subject;
        refusal.Data["Detail"] = detail;
        refusal.Data["Items"] = items is null ? null : string.Join("\n", items);
        return refusal;
    }

    public static string? CodeOf(Exception? e) => e?.Data["Code"] as string is { } code
        && (code.StartsWith("SUBMIT_", StringComparison.Ordinal) || code.StartsWith("FEEDBACK_", StringComparison.Ordinal)) ? code : null;

    public static string? SubjectOf(Exception? e) => e?.Data["Subject"] as string;

    public static string? DetailOf(Exception? e) => e?.Data["Detail"] as string;

    public static IReadOnlyList<string> ItemsOf(Exception? e) => e?.Data["Items"] is string items && items.Length > 0 ? items.Split('\n') : [];
}
