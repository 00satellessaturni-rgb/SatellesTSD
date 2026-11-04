using System.Globalization;
using SatellesTSD.Core.Feedback;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Store;
using SatellesTSD.Core.Xbrl;

namespace SatellesTSD.Core.Declare;

/// <summary>The file and what comes back: every draft of the period into one XBRL GL file (validated twice, named,
/// written without a BOM), the submission recorded and the period Exported in one transaction; the accountant's
/// uploads and confirmations in e-MTA recorded as the period's state; a feedback report read onto the lines; a
/// pending line settled by the accountant's word; a period reopened for corrections.</summary>
public sealed class PeriodSubmitter(IStore store, string? schemaRoot = null, TimeProvider? clock = null)
{
    public const string NoDrafts = "NO_DRAFTS";
    public const string InvalidFile = "INVALID_FILE";
    public const string ReasonNeeded = "CORRECTION_REASON_NEEDED";
    public const string WrongState = "WRONG_STATE";
    public const string LinesPending = "LINES_PENDING";
    public const string LinesStale = "LINES_STALE";
    public const string SchemasMissing = "SCHEMAS_MISSING";
    public const string SourceApplication = "SatellesTSD";

    public static readonly IReadOnlyList<string> CorrectionReasons = ["PARANDUSPOHJUS_1", "PARANDUSPOHJUS_2", "PARANDUSPOHJUS_3", "PARANDUSPOHJUS_4"];

    private readonly IStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly Lazy<XbrlGlValidator?> _validator = new(() => schemaRoot is null ? null : new XbrlGlValidator(schemaRoot));
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>Whether the next file corrects confirmed data, so the export needs a PARANDUSPOHJUS.</summary>
    public bool NeedsCorrectionReason(Guid periodId) => FindPeriod(periodId).ConfirmedAt is not null;

    public ExportResult Export(Guid periodId, string folder, string? correctionReason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var period = FindPeriod(periodId);
        var company = _store.Companies().First(c => c.Id == period.CompanyId);
        var drafts = _store.Payments(periodId).Where(p => p.Status == PaymentStatus.Draft)
            .OrderBy(p => p.Section).ThenBy(p => p.LineNumber, StringComparer.Ordinal).ToList();
        if (drafts.Count == 0)
        {
            throw new DeclareException(NoDrafts, "The period has no draft lines; build them from the review first.");
        }

        // a draft was legal when it was built; what stands beneath it may have changed since (a hidden version refused
        // from the history view, a feedback report read after a rebuild) — a stale M or T would be refused black
        foreach (var draft in drafts)
        {
            var beneath = _store.History(periodId, draft.Section, draft.LineNumber).Where(v => v.Id != draft.Id).ToList();
            try
            {
                LineKeyRules.Check(draft.Action, beneath);
            }
            catch (LineKeyException e)
            {
                throw new DeclareException(LinesStale, $"Line {draft.LineNumber} ({draft.Action}) no longer fits what stands beneath it ({e.Code}); build the lines again before exporting.");
            }
        }

        if (period.ConfirmedAt is not null && correctionReason is null)
        {
            throw new DeclareException(ReasonNeeded, "The declaration was confirmed; a file that changes it names the reason (PARANDUSPOHJUS_1..4).");
        }

        if (correctionReason is not null && !CorrectionReasons.Contains(correctionReason, StringComparer.Ordinal))
        {
            throw new DeclareException(ReasonNeeded, $"'{correctionReason}' is not a PARANDUSPOHJUS code.");
        }

        var sections = drafts.GroupBy(p => p.Section).OrderBy(g => g.Key)
            .Select(g => new SectionEntries(g.Key, ReasonFor(g.Key, correctionReason), g.Select(p => p.Line).ToList()))
            .ToList();
        if (correctionReason is not null && sections.FirstOrDefault(s => s.CorrectionReason is null) is { } misfit)
        {
            throw new DeclareException(ReasonNeeded, $"{correctionReason} does not fit section {misfit.Section}; the file carries lines of it, so name a reason that fits every section in the file.");
        }
        var now = _clock.GetLocalNow();
        var uniqueId = Declaration.MakeUniqueId(company.RegistryCode, now.DateTime);
        var declaration = new Declaration(company.RegistryCode, period.Year, period.Month, DateOnly.FromDateTime(now.DateTime), uniqueId, company.Name, SourceApplication, null, sections).WithCounters();
        var problems = DeclarationValidator.Validate(declaration);
        if (problems.Count > 0)
        {
            throw new DeclareException(InvalidFile, "The file cannot be written: " + string.Join("; ", problems));
        }

        var xml = XbrlGlWriter.Serialize(declaration);
        XbrlGlValidator? validator;
        try
        {
            validator = _validator.Value;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException or IOException or InvalidOperationException)
        {
            throw new DeclareException(SchemasMissing, $"MTA's schemas are not where the window expects them ({schemaRoot}); the file cannot be checked, so it is not written. {e.Message}");
        }

        if (validator is not null)
        {
            var schemaProblems = validator.Validate(xml);
            if (schemaProblems.Count > 0)
            {
                throw new DeclareException(InvalidFile, "The file fails MTA's schema: " + string.Join("; ", schemaProblems));
            }
        }

        Directory.CreateDirectory(folder);
        var fileName = $"tsd-{company.RegistryCode}-{period.Year:0000}-{period.Month:00}-{now:yyyyMMdd-HHmmss}.xml";
        var path = Path.Combine(folder, fileName);
        XbrlGlWriter.Save(declaration, path);
        Submission submission = null!;
        try
        {
            _store.InTransaction(() =>
            {
                submission = _store.AddSubmission(periodId, fileName, uniqueId, drafts.Select(d => d.Id).ToList(), now.UtcDateTime);
                _store.SetPeriodState(periodId, PeriodState.Exported, now.UtcDateTime);
            });
        }
        catch
        {
            File.Delete(path); // a file the store does not know is no file
            throw;
        }

        return new ExportResult { Path = path, Submission = submission, Declaration = declaration, Lines = drafts.Count };
    }

    /// <summary>An export nothing came of, taken back: only the period's latest file, only while the period is still
    /// Exported (not marked uploaded, not confirmed); the lines are drafts again and the period a Draft.</summary>
    public void Withdraw(Guid submissionId)
    {
        var submission = FindSubmission(submissionId);
        var period = FindPeriod(submission.PeriodId);
        if (period.State != PeriodState.Exported)
        {
            throw new DeclareException(WrongState, $"The period is {period.State}; only a file not yet marked uploaded is taken back.");
        }

        var latest = _store.Submissions(period.Id).OrderBy(s => s.CreatedAt).Last();
        if (latest.Id != submissionId)
        {
            throw new DeclareException(WrongState, $"Only the latest file ({latest.FileName}) is taken back; an earlier one is history.");
        }

        _store.InTransaction(() =>
        {
            _store.WithdrawSubmission(submissionId);
            _store.SetPeriodState(period.Id, PeriodState.Draft, _clock.GetUtcNow().UtcDateTime);
        });
    }

    /// <summary>Whether a file may be taken back: the period's latest, nothing came of it, the period still Exported.</summary>
    public bool CanWithdraw(Submission submission, Period period, IReadOnlyList<Submission> submissions) =>
        period.State == PeriodState.Exported && submission.Result is null && submissions.Count > 0 && submissions.OrderBy(s => s.CreatedAt).Last().Id == submission.Id;

    public void MarkUploaded(Guid periodId)
    {
        var period = FindPeriod(periodId);
        if (period.State != PeriodState.Exported)
        {
            throw new DeclareException(WrongState, $"The period is {period.State}; only an exported file is uploaded.");
        }

        _store.SetPeriodState(periodId, PeriodState.Uploaded, _clock.GetUtcNow().UtcDateTime);
    }

    /// <summary>The accountant confirmed the declaration in e-MTA. Refused while a line still awaits a verdict or
    /// needs a change, unless forced.</summary>
    public void MarkConfirmed(Guid periodId, bool force = false)
    {
        var period = FindPeriod(periodId);
        if (period.State is not (PeriodState.Uploaded or PeriodState.Exported))
        {
            throw new DeclareException(WrongState, $"The period is {period.State}; a confirmation follows an upload.");
        }

        var lines = _store.Payments(periodId);
        var drafts = lines.Where(p => p.Status == PaymentStatus.Draft).ToList();
        if (drafts.Count > 0)
        {
            throw new DeclareException(LinesPending, $"{drafts.Count} draft line(s) were never sent: {string.Join(", ", drafts.Select(p => p.LineNumber))}; export them or build again first.");
        }

        var open = lines.Where(p => p.Pending || p.Status == PaymentStatus.NeedsChange).ToList();
        if (open.Count > 0 && !force)
        {
            throw new DeclareException(LinesPending, $"{open.Count} line(s) still await a verdict or need a change: {string.Join(", ", open.Select(p => p.LineNumber))}.");
        }

        _store.SetPeriodState(periodId, PeriodState.Confirmed, _clock.GetUtcNow().UtcDateTime);
    }

    /// <summary>A period opened for corrections: a Draft again, its history kept; the next export carries the M and T lines.</summary>
    public void Reopen(Guid periodId)
    {
        var period = FindPeriod(periodId);
        if (period.State == PeriodState.Draft)
        {
            throw new DeclareException(WrongState, "The period is a Draft already.");
        }

        _store.SetPeriodState(periodId, PeriodState.Draft, _clock.GetUtcNow().UtcDateTime);
    }

    /// <summary>Reads a feedback report (or a ZIP of them) onto the submission's lines; a confirmation report is answered, not recorded.</summary>
    public IReadOnlyList<FeedbackOutcome> ReadFeedback(Guid submissionId, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var reports = FeedbackReader.LoadAll(path);
        var outcomes = new List<FeedbackOutcome>();
        foreach (var report in reports)
        {
            if (report.Kind == ReportKind.Confirmation)
            {
                outcomes.Add(new FeedbackOutcome { Source = report.Source ?? Path.GetFileName(path), Kind = report.Kind, Confirmation = report.ConfirmationResult() });
                continue;
            }

            if (outcomes.Count > 0)
            {
                Thread.Sleep(2); // the feedback rows are keyed by their millisecond; two reports never share one
            }

            var recorded = _store.RecordFeedback(submissionId, report);
            var statuses = _store.Payments(FindSubmission(submissionId).PeriodId).Where(p => p.SubmissionId == submissionId)
                .GroupBy(p => p.Status).ToDictionary(g => g.Key, g => g.Count());
            outcomes.Add(new FeedbackOutcome { Source = report.Source ?? Path.GetFileName(path), Kind = report.Kind, Recorded = recorded.Count, Statuses = statuses });
        }

        return outcomes;
    }

    /// <summary>The accountant's own verdict on a pending line (decision 12).</summary>
    public void Resolve(Guid paymentId, bool onServer) => _store.ResolveLine(paymentId, onServer);

    /// <summary>A PARANDUSPOHJUS fits its section: _3 only the pay section, _4 only the other.</summary>
    public static string? ReasonFor(Section section, string? reason) => reason switch
    {
        "PARANDUSPOHJUS_3" when section != Section.EE0201001 => null,
        "PARANDUSPOHJUS_4" when section != Section.EE0201002 => null,
        _ => reason,
    };

    private Submission FindSubmission(Guid submissionId) =>
        _store.Companies().SelectMany(c => _store.Periods(c.Id)).SelectMany(p => _store.Submissions(p.Id)).FirstOrDefault(s => s.Id == submissionId)
        ?? throw new KeyNotFoundException($"Submission {submissionId} is not in the store.");

    private Period FindPeriod(Guid periodId) =>
        _store.Companies().SelectMany(c => _store.Periods(c.Id)).FirstOrDefault(p => p.Id == periodId)
        ?? throw new KeyNotFoundException($"Period {periodId} is not in the store.");
}
