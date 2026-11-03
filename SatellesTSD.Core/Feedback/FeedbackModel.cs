using System.Globalization;
using SatellesTSD.Core.Model;

namespace SatellesTSD.Core.Feedback;

/// <summary>processing/@result of a tagasisideraport.</summary>
public enum ProcessingResult
{
    Unknown,
    Ok,
    Warning,
    Error,
    PartlyError,
}

/// <summary>validation/errorLevel.</summary>
public enum ValidationLevel
{
    Unknown,
    Ok,
    Warning,
    Error,
}

/// <summary>The colour of a per-payment message (responseData/entryDetail/data/description), ordered from harmless to
/// worst as this app ranks them: a red blocks the confirmation, a blue hides the line's taxes from the totals (the
/// sources do not rank blue against red; red is put above because it stops the month). <see cref="Error"/> is an error
/// whose colour the report does not say (MTA's older samples write plain ERROR).</summary>
public enum Severity
{
    None = 0,
    /// <summary>KOLLANE: advisory, the payment counts and the TSD can be confirmed.</summary>
    Warning = 1,
    /// <summary>SININE: saved, but its taxes are left out of the declaration totals; correct with M.</summary>
    Blue = 2,
    /// <summary>PUNANE: saved and counted, but the TSD cannot be confirmed until fixed; correct with M.</summary>
    Red = 3,
    /// <summary>An error of unstated colour; treat as blocking and look at the message.</summary>
    Error = 4,
    /// <summary>MUST: this transaction was not saved; what to resend depends on what was sent (<see cref="NextAction"/>).</summary>
    Black = 5,
}

/// <summary>What the submitter does with a payment after reading its feedback. A black error says only that THIS
/// transaction was not saved (Juhis B37); whether the key still exists on the server depends on what was sent before,
/// so the advice needs the submitted action (<see cref="FeedbackReport.Outcomes"/>).</summary>
public enum NextAction
{
    None,
    ReviewWarning,
    /// <summary>The payment exists on the server with a defect (blue, red, or a black refusal of an M): resend the whole
    /// payment under the same key with action M.</summary>
    CorrectWithChange,
    /// <summary>The payment does not exist on the server (a black refusal of an R): fix it and send it again as R.</summary>
    FixAndResendAsNew,
    /// <summary>Decide from the message: an error without a colour, a refused T, a black on a line whose submitted action
    /// is unknown, or KR_VM_05 (wrong type OR wrong section — a wrong section needs T in the old section plus R in the right one).</summary>
    Review,
}

/// <summary>The result of a confirmation (kinnitamine) report, from its file-level validation code.</summary>
public enum ConfirmationOutcome
{
    Unknown,
    Confirmed,
    /// <summary>DECLARATION_PROCESSING: the data is still being processed; start a new confirmation later (XTEE §7.5), do not re-poll the old one.</summary>
    StillProcessing,
    /// <summary>DECLARATION_WITH_ERRORS: red errors remain on the declaration.</summary>
    WithErrors,
    /// <summary>DECLARATION_WITH_DISCREPANCIES: the message contradicts what MTA holds (sections, totals).</summary>
    WithDiscrepancies,
    /// <summary>CANNOT_CONFIRM_DECLARATION.</summary>
    CannotConfirm,
}

/// <summary>Which call a report answers. The XML does not say; it is read off the content.</summary>
public enum ReportKind
{
    Submission,
    Confirmation,
}

/// <summary>Identifies one of our lines in a report: the data section's entry number and our lineNumber. A lineNumber
/// is unique only per taxpayer, period and section, so the caller binds the report to its submission first.</summary>
public readonly record struct LineKey(string EntryNumber, string LineNumber);

/// <summary>A reference back to one of our lines: our gl-cor:lineNumber and, when the report gives it, the counter.
/// Black errors come back without the counter although the schema requires it.</summary>
public sealed record LineRef(string LineNumber, int? Counter);

public sealed record Validation(
    ValidationLevel Level,
    string Code,
    string Description,
    string? DescriptionEn,
    string? DescriptionRu,
    IReadOnlyList<LineRef> Lines);

/// <summary>One data block under responseData/entryDetail: either a tax the server computed (code = TAXCODE_enum,
/// description = the tax's name, info = the amount) or a message (code = rule id, description = the colour, info = text),
/// or, in a confirmation report, a declaration total (code 110…119).</summary>
public sealed record ResponseItem(string Code, string? Description, string Info, string? InfoEn, string? InfoRu)
{
    private static readonly HashSet<string> TaxCodes = new(StringComparer.Ordinal) { "TSD_SM", "TSD_KP", "TSD_TK", "TSD_TTK", "TSD_KTM" };
    private static readonly HashSet<string> TotalCodes = new(StringComparer.Ordinal) { "110", "114", "115", "116", "117", "118", "119", "ESM_SM_KOKKU", "ESM_KP_KOKKU" };

    public Severity Severity => (Description ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        "WARNING" => Severity.Warning,
        "ERROR_BLUE" => Severity.Blue,
        "ERROR_RED" => Severity.Red,
        "ERROR_BLACK" => Severity.Black,
        "ERROR" => Severity.Error,
        _ => Severity.None,
    };

    public bool IsMessage => Severity != Severity.None;

    public bool IsTax => TaxCodes.Contains(Code);

    /// <summary>A confirmation total: 110 kinnipeetud tulumaks … 119 kuulub tagastamisele (or the two ESD totals).</summary>
    public bool IsTotal => TotalCodes.Contains(Code);

    /// <summary>The amount in info, written by MTA with a decimal comma ("241,45") or a dot ("1433.64").</summary>
    public decimal? Amount => FeedbackNumbers.TryParse(Info, out var value) ? value : null;
}

public sealed record ResponseLine(IReadOnlyList<LineRef> Lines, IReadOnlyList<ResponseItem> Items);

/// <summary>One entryHeader of the report. <see cref="EntryNumber"/> is null for the file-level block (XML structure
/// errors, or the confirmation result), and <see cref="Section"/> is null when the entry number is not one of ours.</summary>
public sealed record FeedbackSection(
    string? EntryNumber,
    Section? Section,
    IReadOnlyList<Validation> Validations,
    IReadOnlyList<ResponseLine> Lines)
{
    public bool IsFileLevel => EntryNumber is null;

    /// <summary>Validations that point at no line: they concern the file or the section as a whole.</summary>
    public IReadOnlyList<Validation> GeneralValidations => Validations.Where(v => v.Lines.Count == 0).ToList();
}

/// <summary>The outcome for one of our payment lines, assembled from the validations that reference it and the
/// response data block that carries its messages and the server's tax figures.</summary>
/// <param name="SubmittedAction">The action our file carried for this line, when the caller knows it; null otherwise.</param>
/// <param name="Unclassified">Data items that are neither a colour-tagged message nor a parsable known tax: shown as they
/// came, never dropped (an unknown description, a sixth tax code, an amount that would not parse).</param>
public sealed record LineOutcome(
    string EntryNumber,
    Section? Section,
    string LineNumber,
    int? Counter,
    LineAction? SubmittedAction,
    ValidationLevel Level,
    Severity Severity,
    IReadOnlyList<ResponseItem> Messages,
    IReadOnlyDictionary<string, decimal> ServerTaxes,
    IReadOnlyList<ResponseItem> Unclassified,
    NextAction NextAction);

/// <param name="Source">Where the report came from (the file, or the ZIP entry name); null when read from memory.</param>
public sealed record FeedbackReport(ProcessingResult Result, string ResultRaw, IReadOnlyList<FeedbackSection> Sections, string? Source = null)
{
    private static readonly HashSet<string> ConfirmationCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "DECLARATION_PROCESSING", "DECLARATION_WITH_ERRORS", "DECLARATION_WITH_DISCREPANCIES", "CANNOT_CONFIRM_DECLARATION",
    };

    /// <summary>Confirmation when the report carries declaration totals or a confirmation result code; else a submission report.</summary>
    public ReportKind Kind =>
        Sections.SelectMany(s => s.Lines).SelectMany(l => l.Items).Any(i => i.IsTotal)
        || FileValidations.Any(v => ConfirmationCodes.Contains(v.Code))
            ? ReportKind.Confirmation
            : ReportKind.Submission;

    /// <summary>Validations of the entryHeader without an entryNumber: an XML structure error, or a confirmation's
    /// result code such as DECLARATION_WITH_ERRORS.</summary>
    public IReadOnlyList<Validation> FileValidations =>
        Sections.Where(s => s.IsFileLevel).SelectMany(s => s.Validations).ToList();

    /// <summary>Every validation that points at no line, with the section it sits in (null = file level). Nothing a
    /// section says about itself is lost here even though <see cref="Outcomes"/> cannot place it on a line.</summary>
    public IReadOnlyList<(string? EntryNumber, Validation Validation)> GeneralValidations =>
        Sections.SelectMany(s => s.GeneralValidations.Select(v => (s.EntryNumber, v))).ToList();

    /// <summary>True when the report holds only a file-level block that carries an error: a submission refused as a
    /// whole before its payments were looked at, or a confirmation that was not accepted.</summary>
    public bool FileRefused => Sections.All(s => s.IsFileLevel) && FileValidations.Any(v => v.Level == ValidationLevel.Error);

    /// <summary>True when any general (line-less) validation reports an error, in any section.</summary>
    public bool HasGeneralError => GeneralValidations.Any(g => g.Validation.Level == ValidationLevel.Error);

    /// <summary>The declaration totals a confirmation report echoes (110 … 119), by code.</summary>
    public IReadOnlyDictionary<string, decimal> Totals()
    {
        var totals = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var item in Sections.SelectMany(s => s.Lines).SelectMany(l => l.Items).Where(i => i.IsTotal))
        {
            if (item.Amount is { } amount)
            {
                totals[item.Code] = amount;
            }
        }

        return totals;
    }

    /// <summary>The confirmation result when this is a confirmation report (the file-level validation code).</summary>
    public ConfirmationOutcome ConfirmationResult()
    {
        var code = FileValidations.FirstOrDefault()?.Code.ToUpperInvariant();
        return code switch
        {
            "OK" => ConfirmationOutcome.Confirmed,
            "DECLARATION_PROCESSING" => ConfirmationOutcome.StillProcessing,
            "DECLARATION_WITH_ERRORS" => ConfirmationOutcome.WithErrors,
            "DECLARATION_WITH_DISCREPANCIES" => ConfirmationOutcome.WithDiscrepancies,
            "CANNOT_CONFIRM_DECLARATION" => ConfirmationOutcome.CannotConfirm,
            _ => ConfirmationOutcome.Unknown,
        };
    }

    /// <summary>One outcome per referenced payment line, in first-reference order within each section, sections in
    /// report order; each line's messages worst first. <paramref name="submittedActions"/> is what our file carried per
    /// line (the store knows it); without it a black error can only be marked for review, because the right resend
    /// action depends on it.</summary>
    public IReadOnlyList<LineOutcome> Outcomes(IReadOnlyDictionary<LineKey, LineAction>? submittedActions = null)
    {
        var outcomes = new List<LineOutcome>();
        foreach (var section in Sections.Where(s => !s.IsFileLevel))
        {
            var byLine = new Dictionary<string, LineAccumulator>(StringComparer.Ordinal);
            var order = new List<string>();

            LineAccumulator Entry(LineRef reference)
            {
                if (!byLine.TryGetValue(reference.LineNumber, out var entry))
                {
                    entry = new LineAccumulator { Counter = reference.Counter };
                    byLine[reference.LineNumber] = entry;
                    order.Add(reference.LineNumber);
                }
                else if (entry.Counter is null && reference.Counter is not null)
                {
                    entry.Counter = reference.Counter;
                }

                return entry;
            }

            foreach (var validation in section.Validations)
            {
                foreach (var reference in validation.Lines)
                {
                    var entry = Entry(reference);
                    if (validation.Level > entry.Level)
                    {
                        entry.Level = validation.Level;
                    }
                }
            }

            foreach (var line in section.Lines)
            {
                foreach (var reference in line.Lines)
                {
                    var entry = Entry(reference);
                    foreach (var item in line.Items)
                    {
                        if (item.IsMessage)
                        {
                            entry.Messages.Add(item);
                        }
                        else if (item.IsTax && item.Amount is { } amount)
                        {
                            entry.Taxes[item.Code] = amount;
                        }
                        else
                        {
                            entry.Unclassified.Add(item);
                        }
                    }
                }
            }

            foreach (var lineNumber in order)
            {
                var entry = byLine[lineNumber];
                var severity = entry.Messages.Count > 0 ? entry.Messages.Max(m => m.Severity) : Severity.None;

                // The validation level is the server's verdict on the line; a message of a lesser colour (or none at
                // all — blacks before the KR_XBRL_VALJAMAKSED group are not echoed) must not soften it.
                if (entry.Level == ValidationLevel.Error && severity < Severity.Blue)
                {
                    severity = Severity.Error;
                }
                else if (entry.Level == ValidationLevel.Warning && severity == Severity.None)
                {
                    severity = Severity.Warning;
                }

                LineAction? submitted = null;
                if (submittedActions is not null && submittedActions.TryGetValue(new LineKey(section.EntryNumber!, lineNumber), out var action))
                {
                    submitted = action;
                }

                var messages = entry.Messages.OrderByDescending(m => m.Severity).ToList();
                outcomes.Add(new LineOutcome(
                    section.EntryNumber!,
                    section.Section,
                    lineNumber,
                    entry.Counter,
                    submitted,
                    entry.Level,
                    severity,
                    messages,
                    entry.Taxes,
                    entry.Unclassified,
                    ActionFor(severity, submitted, messages.Select(m => m.Code))));
            }
        }

        return outcomes;
    }

    /// <summary>KR_XBRL_VALJAMAKSED_4: an R on a key that already exists — the payment is on the server, so the fix is an M.</summary>
    public const string RuleNewOnExistingKey = "KR_XBRL_VALJAMAKSED_4";

    /// <summary>KR_XBRL_VALJAMAKSED_5: an M or T on a key the server does not know — nothing is there, so send an R.</summary>
    public const string RuleChangeOnUnknownKey = "KR_XBRL_VALJAMAKSED_5";

    /// <summary>KR_VM_05: wrong payment type or wrong data section; a wrong section cannot be fixed with an M.</summary>
    public const string RuleWrongTypeOrSection = "KR_VM_05";

    public static NextAction ActionFor(Severity severity, LineAction? submitted, IEnumerable<string> ruleCodes)
    {
        ArgumentNullException.ThrowIfNull(ruleCodes);
        var codes = ruleCodes.ToHashSet(StringComparer.Ordinal);
        if (codes.Contains(RuleWrongTypeOrSection))
        {
            return NextAction.Review;
        }

        switch (severity)
        {
            case Severity.None:
                return NextAction.None;
            case Severity.Warning:
                return NextAction.ReviewWarning;
            case Severity.Blue:
            case Severity.Red:
                return submitted == LineAction.T ? NextAction.Review : NextAction.CorrectWithChange;
            case Severity.Black:
                if (codes.Contains(RuleNewOnExistingKey))
                {
                    return NextAction.CorrectWithChange;
                }

                if (codes.Contains(RuleChangeOnUnknownKey))
                {
                    return NextAction.FixAndResendAsNew;
                }

                return submitted switch
                {
                    LineAction.R => NextAction.FixAndResendAsNew,
                    LineAction.M => NextAction.CorrectWithChange,
                    _ => NextAction.Review,
                };
            default:
                return NextAction.Review;
        }
    }

    private sealed class LineAccumulator
    {
        public int? Counter { get; set; }

        public ValidationLevel Level { get; set; } = ValidationLevel.Unknown;

        public List<ResponseItem> Messages { get; } = [];

        public Dictionary<string, decimal> Taxes { get; } = new(StringComparer.Ordinal);

        public List<ResponseItem> Unclassified { get; } = [];
    }
}

/// <summary>Numbers as MTA's reports write them: "241,45", "1433.64", "0.00". One separator, comma or dot, at most two
/// decimals (so "1,433" is not mistaken for 1.433).</summary>
public static class FeedbackNumbers
{
    public static bool TryParse(string? text, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Trim();
        if (normalized.Contains(',') && normalized.Contains('.'))
        {
            return false;
        }

        normalized = normalized.Replace(',', '.');
        var dot = normalized.IndexOf('.');
        if (dot >= 0 && normalized.Length - dot - 1 > 2)
        {
            return false;
        }

        return decimal.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }
}
