using SatellesTSD.Core.Feedback;
using SatellesTSD.Core.Model;

namespace SatellesTSD.Core.Store;

/// <summary>The firm's store: companies, their people, their payment months, every declared line with its permanent
/// key and version history, the files sent and the feedback received. One implementation per database engine; the
/// key rules live in <see cref="LineKeyRules"/> so that they are the same everywhere. The imported files and their
/// rows are the <see cref="IImportStore"/> half.</summary>
public interface IStore : IImportStore, IMatchStore
{
    Company AddCompany(string registryCode, string name, string? notes = null);

    Company? FindCompany(string registryCode);

    IReadOnlyList<Company> Companies();

    void UpdateCompany(Company company);

    /// <summary>Adds a person; the returned record carries the assigned id.</summary>
    Person AddPerson(Person person);

    void UpdatePerson(Person person);

    IReadOnlyList<Person> Persons(Guid companyId);

    Person? FindPerson(Guid companyId, string personalCode);

    Period GetOrCreatePeriod(Guid companyId, int year, int month);

    void SetPeriodState(Guid periodId, PeriodState state, DateTime at);

    IReadOnlyList<Period> Periods(Guid companyId);

    /// <summary>A new permanent key for a line of this period and section: never reused, even after a delete.</summary>
    string NewLineNumber(Guid periodId, Section section);

    /// <summary>Stores a new version of a line after checking the key rules (<see cref="LineKeyRules"/>); the
    /// returned record carries the id and version. Throws <see cref="LineKeyException"/> when the action is not allowed.
    /// A caller-supplied key in the counter's own format moves the counter past it, so the two can never collide.</summary>
    StoredPayment AddPayment(StoredPayment payment);

    /// <summary>Replaces a Draft version in place (same key, same version, new figures) — a grid edit or a
    /// re-allocation of the person's month. Anything already sent is history and cannot be replaced.</summary>
    StoredPayment ReplaceDraft(Guid paymentId, StoredPayment replacement);

    /// <summary>The standing version of every line of the period: per key, the latest version that is not Rejected
    /// (a draft or a pending version on top of what MTA holds counts, a refused attempt does not); when every version
    /// was refused, the latest. The refused attempts stay visible through <see cref="History"/>.</summary>
    IReadOnlyList<StoredPayment> Payments(Guid periodId);

    /// <summary>Every version of one line, oldest first.</summary>
    IReadOnlyList<StoredPayment> History(Guid periodId, Section section, string lineNumber);

    /// <summary>Deletes a Draft version that was never sent. Anything else is history and stays.</summary>
    void DeleteDraft(Guid paymentId);

    /// <summary>Records that a file with these lines was produced: the lines become Sent.</summary>
    Submission AddSubmission(Guid periodId, string fileName, string uniqueId, IReadOnlyCollection<Guid> paymentIds, DateTime at);

    /// <summary>A file that was exported but never uploaded: the submission is removed and its lines go back to Draft.
    /// Refused once any feedback was recorded.</summary>
    void WithdrawSubmission(Guid submissionId);

    void SetSubmissionUuid(Guid submissionId, string uuid);

    /// <summary>Applies a feedback report to the submission's lines. Only versions still awaiting a verdict (Sent or
    /// Review) change status; a version MTA already answered keeps its state, so a repeated upload or a stray report can
    /// never undo what MTA saved. Feedback rows are appended. A black KR_XBRL_VALJAMAKSED_4 (R on a known key) proves
    /// the key is on the server (the version becomes Review); a black _5 (M/T on an unknown key) proves it is not (the
    /// key's pending versions become Rejected). A refused file rejects the submission's pending versions. A report that
    /// names a line this submission did not carry, or a confirmation report, is refused: it belongs elsewhere.</summary>
    IReadOnlyList<LineFeedback> RecordFeedback(Guid submissionId, FeedbackReport report);

    /// <summary>The accountant's own verdict on a pending version (Sent or Review) when the feedback is only on
    /// e-MTA's screen: on the server (Accepted; a T becomes Cancelled) or not (Rejected).</summary>
    void ResolveLine(Guid paymentId, bool onServer);

    IReadOnlyList<Submission> Submissions(Guid periodId);

    IReadOnlyList<LineFeedback> Feedback(Guid paymentId);
}

public sealed class LineKeyException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

/// <summary>The permanent-key rules of andmepõhine TSD, decided from a line's version history:
/// R only while the key was never on the server (KR_XBRL_VALJAMAKSED_4 refuses an R on a known key);
/// M and T only while it is (…_5 refuses them on an unknown key); nothing after a T was accepted (the key is dead),
/// and nothing while a T awaits its verdict (it may be about to kill the key).</summary>
public static class LineKeyRules
{
    public const string KeyExists = "KEY_EXISTS";
    public const string KeyUnknown = "KEY_UNKNOWN";
    public const string KeyDead = "KEY_DEAD";
    public const string DraftPending = "DRAFT_PENDING";
    public const string CancellationPending = "CANCELLATION_PENDING";

    public static void Check(LineAction action, IReadOnlyList<StoredPayment> history)
    {
        ArgumentNullException.ThrowIfNull(history);
        bool dead = history.Any(v => v.Action == LineAction.T && v.Status == PaymentStatus.Cancelled);
        bool cancelling = history.Any(v => v.Action == LineAction.T && v.Pending);
        bool onServer = history.Any(v => v.OnServer);
        bool draftPending = history.Any(v => v.Status == PaymentStatus.Draft);

        if (dead)
        {
            throw new LineKeyException(KeyDead, "The key was cancelled (T accepted); it can never be used again — give the payment a new key and send it as R.");
        }

        if (cancelling)
        {
            throw new LineKeyException(CancellationPending, "A cancellation of this key is awaiting MTA's verdict; record it first.");
        }

        if (draftPending)
        {
            throw new LineKeyException(DraftPending, "A draft version of this key is waiting to be sent; edit or delete it instead of adding another.");
        }

        switch (action)
        {
            case LineAction.R when onServer:
                throw new LineKeyException(KeyExists, "The key is already on the server; a new R would be refused (KR_XBRL_VALJAMAKSED_4) — send an M.");
            case LineAction.M or LineAction.T when !onServer:
                throw new LineKeyException(KeyUnknown, "The key is not on the server; an M or T would be refused (KR_XBRL_VALJAMAKSED_5) — send an R.");
        }
    }
}
