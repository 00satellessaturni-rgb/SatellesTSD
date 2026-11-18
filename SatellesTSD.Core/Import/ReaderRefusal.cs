namespace SatellesTSD.Core.Import;

/// <summary>A refusal of the reading of a file (the reader, the employer's check, the store's twin bytes) or a reading that
/// recognised nothing, by code, carried in the exception's Data so the window words it in Estonian, the Core's English kept
/// beside it for the log (decision 229, the owner's option A); the exception stays an <see cref="ImportException"/>.</summary>
public static class ReaderRefusal
{
    /// <summary>An Excel 2003 XML spreadsheet under an Excel name.</summary>
    public const string SpreadsheetXml = "READ_SPREADSHEET_XML";

    /// <summary>A web page saved under an Excel name.</summary>
    public const string WebPage = "READ_WEB_PAGE";

    /// <summary>Neither an Excel file nor text.</summary>
    public const string UnknownFormat = "READ_UNKNOWN_FORMAT";

    /// <summary>An Excel file and no Excel reader configured (tests only).</summary>
    public const string NoExcelReader = "READ_NO_EXCEL_READER";

    /// <summary>The Excel reader was handed something else; the subject is the format sniffed.</summary>
    public const string NotExcel = "READ_NOT_EXCEL";

    /// <summary>The workbook could not be opened (damaged or password-protected); the detail is the library's own text.</summary>
    public const string ExcelUnopened = "READ_EXCEL_UNOPENED";

    /// <summary>A sheet could not be read; the subject is its number from 1, the detail the library's own text.</summary>
    public const string SheetUnreadable = "READ_SHEET_UNREADABLE";

    /// <summary>Nothing in the file was recognised.</summary>
    public const string NothingRecognised = "READ_NOTHING_RECOGNISED";

    /// <summary>The TÖR sheets of a selection name several employers; the subject is their codes joined by ", ".</summary>
    public const string SeveralEmployers = "READ_SEVERAL_EMPLOYERS";

    /// <summary>The same bytes are stored already; the subject is when ("yyyy-MM-dd HH:mm"), the detail the earlier name.</summary>
    public const string AlreadyImported = "READ_ALREADY_IMPORTED";

    public static ImportException Of(string code, string message, string? subject = null, string? detail = null, Exception? inner = null)
    {
        var refusal = new ImportException(message, inner);
        refusal.Data["Code"] = code;
        refusal.Data["Subject"] = subject;
        refusal.Data["Detail"] = detail;
        return refusal;
    }

    public static string? CodeOf(Exception? e) => e?.Data["Code"] as string is { } code && code.StartsWith("READ_", StringComparison.Ordinal) ? code : null;

    public static string? SubjectOf(Exception? e) => e?.Data["Subject"] as string;

    public static string? DetailOf(Exception? e) => e?.Data["Detail"] as string;
}
