using System.Text.Json;
using SatellesTSD.Core.Import;
using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Review;
using SatellesTSD.Core.Store;
using SatellesTSD.Core.Tax;
using SatellesTSD.Core.Xbrl;

namespace SatellesTSD.Core.Declare;

/// <summary>The declared lines of a period, built from the review and never typed: a person's declared payments of the
/// month allocated together (the exemption in date order, the social-tax minimum topped up on the last qualifying
/// payment or on a zero line), each one a line; then reconciled with what the store holds — a new R draft, a draft
/// replaced, an M where the server's figures changed, a T where a declared payment is declared no more, the rest left
/// alone. A payment the review refuses is left out and said.</summary>
public sealed class PeriodDeclarer(IStore store, TaxRates? rates = null, TimeProvider? clock = null)
{
    public const string Refused = "REFUSED";
    public const string ZeroLine = "ZERO_LINE";
    public const string SectionChanged = "SECTION_CHANGED";
    public const string CancellationPending = "CANCELLATION_PENDING";
    public const string NotAllocated = "NOT_ALLOCATED";
    public const string NoPayee = "NO_PAYEE";
    public const string NoEmploymentInMonth = "NO_EMPLOYMENT_IN_MONTH";

    /// <summary>The source text of a zero line carrying the month's minimum.</summary>
    public const string MinimumSource = "minimum";

    private static readonly JsonSerializerOptions Json = LayoutCatalog.CompactJson;

    private readonly IStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly TaxRates _rates = rates ?? TaxRates.Year2026;
    private readonly ReviewWorkspace _workspace = new(store, rates, clock);

    /// <summary>The part of the month the minimum obligation covers: the employment's days in the month over 30
    /// (määrus nr 17 § 6), 1 for a whole month or an unknown employment.</summary>
    public static decimal MonthShare(TorEntry? entry, int year, int month)
    {
        if (entry is null)
        {
            return 1m;
        }

        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var start = entry.StartDate is { } s && s > first ? s : first;
        var end = entry.EndDate is { } e && e < last ? e : last;
        if (start == first && end == last)
        {
            return 1m;
        }

        var days = end.DayNumber - start.DayNumber + 1;
        return days <= 0 ? 1m : Math.Min(1m, Math.Round(days / 30m, 4));
    }

    /// <summary>The standing lines of the period with their person and the feedback's last word.</summary>
    public IReadOnlyList<DeclaredLine> Lines(Guid periodId)
    {
        var period = FindPeriod(periodId);
        var persons = _store.Persons(period.CompanyId).ToDictionary(p => p.Id);
        return _store.Payments(periodId)
            .OrderBy(p => p.Section).ThenBy(p => p.LineNumber, StringComparer.Ordinal)
            .Select(p => new DeclaredLine
            {
                Payment = p,
                Person = p.PersonId is { } id && persons.TryGetValue(id, out var person) ? person : null,
                LastFeedback = _store.Feedback(p.Id).LastOrDefault(),
                Versions = _store.History(periodId, p.Section, p.LineNumber).Count,
            })
            .ToList();
    }

    /// <summary>Builds the period's lines from the review (Draft only).</summary>
    public BuildReport Build(Guid periodId)
    {
        var view = _workspace.Load(periodId);
        if (!view.Editable)
        {
            throw new DeclareException(ReviewWorkspace.PeriodNotDraft, $"The period is {view.Period.State}; its lines are built while it is a Draft.");
        }

        var period = view.Period;
        var persons = view.StorePersons.ToDictionary(p => p.Id);
        var standing = _store.Payments(periodId).ToList();
        // several keys may have stood for one source over time (a dead one, a refused one, the live one): the live one speaks for it
        var bySource = new Dictionary<string, StoredPayment>(StringComparer.Ordinal);
        foreach (var payment in standing)
        {
            if (SourceKey(payment) is { } key && (!bySource.TryGetValue(key, out var held) || Liveness(payment) > Liveness(held)))
            {
                bySource[key] = payment;
            }
        }

        var claimed = new HashSet<string>(StringComparer.Ordinal);
        var builds = new List<PersonBuild>();
        var problems = new List<string>();
        var allocator = new MonthAllocator(new TaxEngine(_rates));
        _store.InTransaction(() =>
        {
            foreach (var reviewPerson in view.Persons.Where(p => !p.IsUnmatched && p.PersonId is { } id && persons.ContainsKey(id)))
            {
                builds.Add(BuildPerson(period, reviewPerson, persons[reviewPerson.PersonId!.Value], bySource, claimed, allocator));
            }

            // what nobody claimed is declared no more: a draft goes, a line on the server gets its T
            var stray = new List<BuiltLine>();
            foreach (var payment in standing)
            {
                var key = SourceKey(payment);
                if (key is not null && claimed.Contains(key))
                {
                    continue;
                }

                if (Retire(payment, problems) is { } line)
                {
                    var owner = payment.PersonId is { } pid ? builds.FirstOrDefault(b => b.PersonId == pid) : null;
                    if (owner is not null)
                    {
                        builds[builds.IndexOf(owner)] = owner.WithLine(line);
                    }
                    else
                    {
                        stray.Add(line);
                    }
                }
            }

            if (stray.Count > 0)
            {
                builds.Add(new PersonBuild { PersonId = null, Name = "", Lines = stray });
            }
        });

        return new BuildReport { Period = period, Persons = builds, Problems = problems };
    }

    private PersonBuild BuildPerson(Period period, ReviewPerson reviewPerson, Person person, Dictionary<string, StoredPayment> bySource, HashSet<string> claimed, MonthAllocator allocator)
    {
        var name = $"{person.LastName} {person.FirstName}";
        var lines = new List<BuiltLine>();
        var problems = new List<string>();
        var declared = new List<(ReviewPayment Payment, string Source)>();
        foreach (var payment in reviewPerson.Payments.Where(p => p.Declare))
        {
            var source = SourceOf(payment);
            if (payment.Checks.Count > 0)
            {
                problems.Add($"{Refused}: {payment.Reference}: {string.Join(", ", payment.Checks.Select(c => c.Code))}");
                if (bySource.TryGetValue(source, out var stale) && stale.Status == PaymentStatus.Draft)
                {
                    _store.DeleteDraft(stale.Id);
                    lines.Add(new BuiltLine { Key = stale.LineNumber, Section = stale.Section, Action = stale.Action, Change = LineChange.Deleted, Source = payment.Reference, Note = Refused });
                }
                else if (bySource.TryGetValue(source, out var kept))
                {
                    lines.Add(new BuiltLine { Key = kept.LineNumber, Section = kept.Section, Action = kept.Action, Change = LineChange.Kept, Source = payment.Reference, Note = Refused });
                }

                claimed.Add(source);
                continue;
            }

            declared.Add((payment, source));
        }

        var monthPayments = new List<MonthPayment>();
        var references = new Dictionary<string, (string Reference, Guid? RowId, Guid? LineId)>(StringComparer.Ordinal);
        foreach (var (payment, source) in declared)
        {
            var type = PaymentTypes.Get(payment.TypeCode!);
            monthPayments.Add(new MonthPayment(source, type, payment.Amount, AmountIsNet: true, payment.Date!.Value));
            // a bank row's line is known by its row alone; only a hand line's line carries the review line's id
            references[source] = (payment.Reference, payment.RowId, payment.RowId is null ? payment.LineId : null);
        }

        // a month without pay of a minimum-carrying type under a valid employment whose own type carries it: the zero line (decision 3)
        var entry = reviewPerson.Register?.ActiveEntry;
        var inRegister = reviewPerson.Register is { Entries.Count: > 0 };
        if (person.SocialTaxMinimumApplies && !monthPayments.Any(m => m.Type.SocialTaxMinimum) && entry is { StateId: TorStates.Valid }
            && EmploymentTypes.Find(entry.TypeId) is { SocialTaxMinimum: true }
            && EmploymentTypes.DefaultCode(entry.TypeId, person.Residency) is { } zeroCode && PaymentTypes.TryGet(zeroCode, out var zeroType) && zeroType!.SocialTaxMinimum && zeroType.ZeroAllowed)
        {
            var zeroSource = MinimumKey(person.Id);
            monthPayments.Add(MonthPayment.Zero(zeroSource, zeroType, new DateOnly(period.Year, period.Month, DateTime.DaysInMonth(period.Year, period.Month))));
            references[zeroSource] = (MinimumSource, null, null);
            problems.Add($"{ZeroLine}: a valid employment of type {entry.TypeId} with no pay carrying the minimum this month; a zero line of type {zeroCode} carries it.");
        }

        if (monthPayments.Count == 0)
        {
            return new PersonBuild { PersonId = person.Id, Name = name, Lines = lines, Problems = problems };
        }

        if (person.PayeeId is not { } payee)
        {
            problems.Add($"{NoPayee}: the person has neither a personal code nor an MTA non-resident code; nothing declared and nothing undone.");
            foreach (var source in monthPayments.Select(m => m.Key))
            {
                claimed.Add(source);
            }

            return new PersonBuild { PersonId = person.Id, Name = name, Lines = lines, Problems = problems };
        }

        var share = MonthShare(entry, period.Year, period.Month);
        var facts = person.FactsFor(period.Year, share);
        if (inRegister && entry is null && facts.SocialTaxMinimumApplies)
        {
            // pay after the employment ended (a final settlement): the register covers no day of the month, so no minimum is owed on it
            facts = facts with { SocialTaxMinimumApplies = false };
            problems.Add($"{NoEmploymentInMonth}: no employment of the register covers the month; the minimum is not owed on this pay.");
        }

        MonthAllocation allocation;
        try
        {
            allocation = allocator.Allocate(monthPayments, facts);
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException)
        {
            problems.Add($"{NotAllocated}: {e.Message}");
            foreach (var source in monthPayments.Select(m => m.Key))
            {
                claimed.Add(source); // what stands, stands; nothing is cancelled over a month that cannot be computed
            }

            return new PersonBuild { PersonId = person.Id, Name = name, Lines = lines, Problems = problems, MinimumShare = share };
        }

        foreach (var allocated in allocation.Payments)
        {
            var source = allocated.Payment.Key;
            claimed.Add(source);
            var (reference, rowId, lineId) = references[source];
            var type = allocated.Payment.Type;
            bySource.TryGetValue(source, out var existing);
            try
            {
                lines.Add(Reconcile(period, person, existing, reference, rowId, lineId, type, type.Section, facts, allocated, problems));
            }
            catch (LineKeyException e)
            {
                problems.Add($"{e.Code}: {reference}: {e.Message}");
            }
            catch (InvalidOperationException e)
            {
                problems.Add($"{reference}: {e.Message}");
            }
        }

        return new PersonBuild
        {
            PersonId = person.Id,
            Name = name,
            Lines = lines,
            Problems = problems,
            MinimumShare = share,
            ExemptionOffered = allocation.ExemptionOffered,
            ExemptionLeft = allocation.ExemptionLeft,
            MinimumDue = allocation.MinimumDue,
            MinimumShortfall = allocation.MinimumShortfall,
            TopUpKey = allocation.TopUpKey is { } top && references.TryGetValue(top, out var topRef) ? topRef.Reference : allocation.TopUpKey,
        };
    }

    private BuiltLine Reconcile(Period period, Person person, StoredPayment? existing, string reference, Guid? rowId, Guid? lineId, PaymentType type, Section section, PayeeTaxFacts facts, AllocatedPayment allocated, List<string> problems)
    {
        StoredPayment Draft(string key, LineAction action, Guid? row)
        {
            var line = DeclarationLineFactory.Create(key, 0, action, type, facts, person.PayeeId!, allocated.Payment.Date, allocated.Result);
            return StoredPayment.New(period.Id, person.Id, section, line, facts, allocated.Result, allocated.Payment.AmountIsNet ? allocated.Result.Net : null, reference, row, lineId);
        }

        BuiltLine Fresh()
        {
            var key = _store.NewLineNumber(period.Id, section);
            _store.AddPayment(Draft(key, LineAction.R, rowId));
            return new BuiltLine { Key = key, Section = section, Action = LineAction.R, Change = LineChange.New, Source = reference };
        }

        if (existing is null)
        {
            return Fresh();
        }

        if (existing.Status == PaymentStatus.Draft)
        {
            if (existing.Section == section && existing.Action != LineAction.T)
            {
                _store.ReplaceDraft(existing.Id, Draft(existing.LineNumber, existing.Action, null));
                return new BuiltLine { Key = existing.LineNumber, Section = section, Action = existing.Action, Change = LineChange.Replaced, Source = reference };
            }

            // another section, or a T draft of a payment declared again: the draft goes, the line is made afresh from what stands beneath
            _store.DeleteDraft(existing.Id);
            return Reconcile(period, person, HistoryStanding(existing), reference, rowId, lineId, type, section, facts, allocated, problems);
        }

        if (existing.Action == LineAction.T && existing.Pending)
        {
            problems.Add($"{CancellationPending}: {reference}: the cancellation of {existing.LineNumber} awaits MTA's verdict; the line follows once it is settled.");
            return new BuiltLine { Key = existing.LineNumber, Section = existing.Section, Action = existing.Action, Change = LineChange.Kept, Source = reference, Note = CancellationPending };
        }

        if (existing.Status == PaymentStatus.Cancelled)
        {
            return Fresh(); // a dead key: the payment is declared again under a new one (the row is free again)
        }

        if (!existing.OnServer)
        {
            // every version refused black: the key never reached the server. In its own section an R under it is right
            // again; in the other section a fresh key, and the row moves with it (a key never on the server holds nothing)
            if (existing.Section != section)
            {
                return Fresh();
            }

            _store.AddPayment(Draft(existing.LineNumber, LineAction.R, null));
            return new BuiltLine { Key = existing.LineNumber, Section = existing.Section, Action = LineAction.R, Change = LineChange.New, Source = reference };
        }

        if (existing.Section != section)
        {
            // a key is bound to its section: the old line is cancelled now, the new one follows once the key is dead
            _store.AddPayment(StoredPayment.New(period.Id, person.Id, existing.Section, PaymentLine.Cancellation(existing.LineNumber, 0), null, null, null, reference, null, lineId));
            problems.Add($"{SectionChanged}: {reference}: the type moved to {section}; line {existing.LineNumber} is cancelled first, the new line follows once MTA accepts the cancellation.");
            return new BuiltLine { Key = existing.LineNumber, Section = existing.Section, Action = LineAction.T, Change = LineChange.Cancelled, Source = reference, Note = SectionChanged };
        }

        var change = Draft(existing.LineNumber, LineAction.M, null);
        if (SameLine(change.Line, existing.Line))
        {
            return new BuiltLine { Key = existing.LineNumber, Section = section, Action = existing.Action, Change = LineChange.Kept, Source = reference };
        }

        _store.AddPayment(change);
        return new BuiltLine { Key = existing.LineNumber, Section = section, Action = LineAction.M, Change = LineChange.Changed, Source = reference };
    }

    /// <summary>What stands under a key once its draft is gone: the latest version that is not Rejected, or none.</summary>
    private StoredPayment? HistoryStanding(StoredPayment draft)
    {
        var history = _store.History(draft.PeriodId, draft.Section, draft.LineNumber).Where(v => v.Id != draft.Id).ToList();
        return history.LastOrDefault(v => v.Status != PaymentStatus.Rejected) ?? history.LastOrDefault();
    }

    /// <summary>A standing line nobody declares any more: a draft is deleted — and what it stood on, if on the server,
    /// gets its T; a T draft stays as it is; a line on the server gets its T.</summary>
    private BuiltLine? Retire(StoredPayment payment, List<string> problems)
    {
        var source = payment.SourceRef ?? "";
        switch (payment.Status)
        {
            case PaymentStatus.Draft when payment.Action == LineAction.T:
                return new BuiltLine { Key = payment.LineNumber, Section = payment.Section, Action = payment.Action, Change = LineChange.Kept, Source = source, Note = "cancellation drafted" };
            case PaymentStatus.Draft:
                _store.DeleteDraft(payment.Id);
                var beneath = HistoryStanding(payment);
                if (beneath is { OnServer: true } && beneath.Action != LineAction.T)
                {
                    return Cancel(beneath, source);
                }

                return new BuiltLine { Key = payment.LineNumber, Section = payment.Section, Action = payment.Action, Change = LineChange.Deleted, Source = source };
            case PaymentStatus.Sent or PaymentStatus.Accepted or PaymentStatus.NeedsChange or PaymentStatus.Review when payment.Action == LineAction.T:
                problems.Add($"{CancellationPending}: {source}: the cancellation of {payment.LineNumber} awaits MTA's verdict.");
                return new BuiltLine { Key = payment.LineNumber, Section = payment.Section, Action = payment.Action, Change = LineChange.Kept, Source = source, Note = CancellationPending };
            case PaymentStatus.Sent or PaymentStatus.Accepted or PaymentStatus.NeedsChange or PaymentStatus.Review:
                return Cancel(payment, source);
            default:
                return null; // dead or refused: nothing to undo
        }
    }

    private BuiltLine Cancel(StoredPayment onServer, string source)
    {
        _store.AddPayment(StoredPayment.New(onServer.PeriodId, onServer.PersonId, onServer.Section, PaymentLine.Cancellation(onServer.LineNumber, 0), null, null, null, onServer.SourceRef, null, onServer.ReviewLineId));
        return new BuiltLine { Key = onServer.LineNumber, Section = onServer.Section, Action = LineAction.T, Change = LineChange.Cancelled, Source = source };
    }

    /// <summary>A draft before a line on the server, before a refused key, before a dead one.</summary>
    private static int Liveness(StoredPayment payment) =>
        payment.Status == PaymentStatus.Draft ? 4 : payment.OnServer ? 3 : payment.Status == PaymentStatus.Rejected ? 2 : 1;

    private static bool SameLine(PaymentLine a, PaymentLine b) =>
        JsonSerializer.Serialize(a with { Counter = 0, Action = LineAction.R }, Json) == JsonSerializer.Serialize(b with { Counter = 0, Action = LineAction.R }, Json);

    /// <summary>The source a standing line stands for: its bank row, its hand line, or the month's minimum of its person.</summary>
    public static string? SourceKey(StoredPayment payment) =>
        payment.SourceRowId is { } row ? "row:" + row.ToString("N")
        : payment.ReviewLineId is { } line ? "line:" + line.ToString("N")
        : payment.SourceRef == MinimumSource && payment.PersonId is { } person ? MinimumKey(person)
        : null;

    private static string SourceOf(ReviewPayment payment) =>
        payment.RowId is { } row ? "row:" + row.ToString("N") : "line:" + payment.LineId!.Value.ToString("N");

    private static string MinimumKey(Guid personId) => "minimum:" + personId.ToString("N");

    private Period FindPeriod(Guid periodId) =>
        _store.Companies().SelectMany(c => _store.Periods(c.Id)).FirstOrDefault(p => p.Id == periodId)
        ?? throw new KeyNotFoundException($"Period {periodId} is not in the store.");
}

file static class PersonBuildExtensions
{
    public static PersonBuild WithLine(this PersonBuild build, BuiltLine line) => new()
    {
        PersonId = build.PersonId,
        Name = build.Name,
        Lines = [.. build.Lines, line],
        Problems = build.Problems,
        MinimumShare = build.MinimumShare,
        ExemptionOffered = build.ExemptionOffered,
        ExemptionLeft = build.ExemptionLeft,
        MinimumDue = build.MinimumDue,
        MinimumShortfall = build.MinimumShortfall,
        TopUpKey = build.TopUpKey,
    };
}
