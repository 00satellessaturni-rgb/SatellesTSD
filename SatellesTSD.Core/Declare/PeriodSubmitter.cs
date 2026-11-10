using System.Globalization;
using SatellesTSD.Core.Feedback;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Store;
using SatellesTSD.Core.Xbrl;

namespace SatellesTSD.Core.Declare;

/// <summary>What became of a withdrawn export's file.</summary>
public enum WithdrawnFileState
{
    /// <summary>Renamed with <see cref="PeriodSubmitter.WithdrawnSuffix"/>; <see cref="WithdrawnFile.Path"/> is the new name.</summary>
    SetAside,

    /// <summary>Not where it was written (moved or deleted already); <see cref="WithdrawnFile.Path"/> is where it was looked for.</summary>
    NotFound,

    /// <summary>The folder it was written to cannot be reached now (a share offline, rights lost): the file may still be there, under its own name.</summary>
    Unreachable,

    /// <summary>An export made before the store kept the path, and no folder to look in.</summary>
    FolderUnknown,

    /// <summary>Found and not renamed (held by another program, no rights); <see cref="WithdrawnFile.Error"/> says why.</summary>
    Failed,
}

/// <summary>A withdrawn export's file and what became of it (decision 152).</summary>
public sealed record WithdrawnFile(string FileName, WithdrawnFileState State, string? Path, string? Error);

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
        var declaration = new Declaration(company.RegistryCode, period.Year, period.Month, DateOnly.FromDateTime(now.DateTime), uniqueId, company.Name, SourceApplication, null, sections,
            PayerCodes.DescriptionOf(company.RegistryCode, company.PayerCodeType)).WithCounters();
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
                submission = _store.AddSubmission(periodId, path, uniqueId, drafts.Select(d => d.Id).ToList(), now.UtcDateTime);
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
    /// <param name="fallbackFolder">Where to look for the file of an export made before the store kept its path (the export folder last used).</param>
    public WithdrawnFile Withdraw(Guid submissionId, string? fallbackFolder = null)
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

        // the file written is set aside under another name, so it is not uploaded by mistake for the export made next: the
        // report of it would name the same lines (decision 152). Never a reason to refuse the withdrawal: it is done.
        var fallback = string.IsNullOrWhiteSpace(fallbackFolder) ? null : Path.Combine(fallbackFolder, submission.FileName);
        var aside = SetAside(submission.FileName, submission.FilePath ?? fallback);
        if (aside.State is WithdrawnFileState.NotFound or WithdrawnFileState.Unreachable && submission.FilePath is not null && fallback is not null
            && !string.Equals(Path.GetFullPath(fallback), Path.GetFullPath(submission.FilePath), StringComparison.OrdinalIgnoreCase)
            && SetAside(submission.FileName, fallback) is { State: WithdrawnFileState.SetAside } elsewhere)
        {
            // the path as stored leads nowhere from here (the share under another drive letter, a store moved to another
            // computer): the file of that very name in the export folder last used is the same export — the name carries
            // the company, the month and the second
            return elsewhere;
        }

        return aside;
    }

    /// <summary>The suffix a withdrawn export's file gets: no longer an .xml the upload offers, still there to look at.</summary>
    public const string WithdrawnSuffix = ".tagasi-voetud";

    private static WithdrawnFile SetAside(string fileName, string? path)
    {
        if (path is null)
        {
            return new WithdrawnFile(fileName, WithdrawnFileState.FolderUnknown, null, null);
        }

        try
        {
            if (!File.Exists(path))
            {
                // a folder that cannot be reached (a share offline, rights lost) says nothing of the file: it may well be there
                return new WithdrawnFile(fileName, Directory.Exists(Path.GetDirectoryName(path)) ? WithdrawnFileState.NotFound : WithdrawnFileState.Unreachable, path, null);
            }

            var aside = path + WithdrawnSuffix;
            for (var n = 2; File.Exists(aside); n++)
            {
                aside = $"{path}{WithdrawnSuffix}-{n}";
            }

            File.Move(path, aside);
            return new WithdrawnFile(fileName, WithdrawnFileState.SetAside, aside, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return new WithdrawnFile(fileName, WithdrawnFileState.Failed, path, e.Message);
        }
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

    /// <summary>The accountant confirmed the declaration in e-MTA. Refused while a line still awaits a verdict, needs a
    /// change, or was refused by MTA and so is not on the server (black, or settled as refused by hand), unless forced:
    /// a payment the review no longer declares may stay refused.</summary>
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

        // the one question names everything the force would pass: the lines still open, and the keys whose every version
        // MTA refused (nothing of them is on the server) — a refused T aside, it took nothing back and nothing resends it
        var open = lines.Where(p => p.Pending || p.Status == PaymentStatus.NeedsChange).ToList();
        var refused = lines.Where(p => p.Status == PaymentStatus.Rejected && p.Action != LineAction.T).ToList();
        if (!force && (open.Count > 0 || refused.Count > 0))
        {
            var parts = new List<string>();
            if (open.Count > 0)
            {
                parts.Add($"{open.Count} line(s) still await a verdict or need a change: {string.Join(", ", open.Select(p => p.LineNumber))}");
            }

            if (refused.Count > 0)
            {
                parts.Add($"{refused.Count} line(s) were refused by MTA and are not on the server: {string.Join(", ", refused.Select(p => p.LineNumber))} (reopen the period and build again to resend what the review still declares)");
            }

            throw new DeclareException(LinesPending, string.Join("; ", parts) + "; or confirm anyway.");
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
            outcomes.Add(new FeedbackOutcome { Source = report.Source ?? Path.GetFileName(path), Kind = report.Kind, Recorded = recorded.Count, Statuses = statuses, General = report.GeneralValidations });
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
