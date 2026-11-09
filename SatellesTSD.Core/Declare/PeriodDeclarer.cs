using System.Text.Json;
using SatellesTSD.Core.Import;
using SatellesTSD.Core.Lookups;
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
    public const string ModeChanged = "MODE_CHANGED";
    public const string RatesMissing = "RATES_MISSING";
    public const string RunStale = "RUN_STALE";
    public const string Excluded = "EXCLUDED";
    public const string Included = "INCLUDED";
    public const string FactsOfAnotherMonth = "FACTS_OF_ANOTHER_MONTH";
    public const string NoResidenceCountry = "NO_RESIDENCE_COUNTRY";

    /// <summary>The source of a line that adds up a person's payments of one type: "totals:&lt;type&gt;:&lt;the payments' references&gt;".</summary>
    public const string TotalsSource = "totals";

    /// <summary>The source text of a zero line carrying the month's minimum.</summary>
    public const string MinimumSource = "minimum";

    private static readonly JsonSerializerOptions Json = LayoutCatalog.CompactJson;

    private readonly IStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly TaxRates _rates = rates ?? TaxRates.Year2026;
    private readonly bool _ratesGiven = rates is not null;
    private readonly ReviewWorkspace _workspace = new(store, rates, clock);

    /// <summary>The part of the month the minimum obligation covers: the employment's days in the month over 30
    /// (määrus nr 17 § 6), 1 for a whole month or an unknown employment.</summary>
    public static decimal MonthShare(TorEntry? entry, int year, int month) => MonthShare(entry, [], year, month);

    private static bool Carries(string? typeId) => EmploymentTypes.Find(typeId) is { SocialTaxMinimum: true };

    /// <summary>The share of the month the minimum is measured by (decision 89): the days covered by the person's register
    /// rows of the month whose employment type carries the minimum — the entry standing for the month among them when its
    /// own type does, else the person's other rows (a board seat or a VÕS row newer than the Tööleping stands for the
    /// month; the Tööleping carries the minimum) — so a contract ended on the 15th and the next begun on the 16th make a
    /// whole month; the standing entry alone when no row carries it. Every row's days of pause (from "Peatamise alguse
    /// aeg" to "Peatamise lõpu aeg", or on) are left out, the standing entry's included; a paused row that does not say
    /// since when adds nothing; the standing entry declared "kaasa" counts every day as if valid. A day two rows cover
    /// counts once.</summary>
    /// <param name="treatPausedAsValid">The person card's "kaasa": the entry standing for the month counts its days as if
    /// valid, its pause included.</param>
    public static decimal MonthShare(TorEntry? entry, IReadOnlyList<TorEntry> others, int year, int month, bool treatPausedAsValid = false)
    {
        ArgumentNullException.ThrowIfNull(others);
        if (entry is null)
        {
            return 1m;
        }

        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var covered = new bool[last.Day];
        (DateOnly Start, DateOnly End) Span(TorEntry e) => (e.StartDate is { } s && s > first ? s : first, e.EndDate is { } x && x < last ? x : last);
        void Mark(TorEntry e, bool skipPause)
        {
            var (start, end) = Span(e);
            for (var day = start; day <= end; day = day.AddDays(1))
            {
                if (skipPause && e.PauseStart is { } from && day >= from && (e.PauseEnd is not { } to || day <= to))
                {
                    continue;
                }

                covered[day.Day - 1] = true;
            }
        }

        var (entryStart, entryEnd) = Span(entry);
        if (entryStart > entryEnd)
        {
            return 1m; // no overlap: not this build's business
        }

        // the rows the minimum is measured by: those whose type carries it — the standing entry among them when its own
        // does, else the person's other rows (a board seat or a VÕS row newer than the Tööleping stands for the month, the
        // Tööleping carries the minimum) — and, where no row carries it, the standing entry alone. A paused row counts
        // its days outside the pause, and nothing when it does not say since when; the standing entry declared "kaasa"
        // counts every day as if valid.
        var rows = new[] { entry }.Concat(others).ToList(); // the entry may be among the others too: a day marked twice is marked once
        var carriers = rows.Where(r => Carries(r.TypeId) && Span(r).Start <= Span(r).End).ToList(); // only rows that cover a day of the month
        foreach (var row in carriers.Count > 0 ? carriers : [entry])
        {
            var asValid = treatPausedAsValid && (ReferenceEquals(row, entry) || row == entry);
            var paused = row.StateId == TorStates.Paused && !asValid;
            if (paused && row.PauseStart is null)
            {
                continue;
            }

            Mark(row, skipPause: paused);
        }

        var days = covered.Count(c => c);
        return days == covered.Length ? 1m : Math.Min(1m, days / 30m); // exact: the due is rounded to cents once, from kuumäär × days / 30
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

    /// <summary>Whether a payment of this type carries the social-tax minimum for this person, their A1 case applied (the
    /// engine's rule); the pension fact, which the engine wants known, has no say in it and is given as "not a member".</summary>
    private static bool CarriesMinimumFor(PaymentType type, Person person, int year) =>
        EffectiveFlags.Of(type, person.FactsFor(year) with { FundedPensionMember = false, PensionRate = null }).SocialTaxMinimum;

    /// <summary>The earlier of two days, either missing.</summary>
    private static DateOnly? Earlier(DateOnly? first, DateOnly? second) => first is null ? second : second is null ? first : first < second ? first : second;

    /// <summary>The cut the period's drafts were built in (the minimum's zero line belongs to both, so it says nothing);
    /// null without a draft that says.</summary>
    public DeclarationMode? DraftsCut(Guid periodId)
    {
        var drafts = _store.Payments(periodId).Where(p => p.Status == PaymentStatus.Draft && p.Action != LineAction.T && p.SourceRef != MinimumSource).ToList();
        return drafts.Count == 0 ? null : drafts.Any(IsTotals) ? DeclarationMode.TotalsPerType : DeclarationMode.PerPayment;
    }

    /// <summary>Whether the period's drafts are what a build would make now: a build run and rolled back, its drafts
    /// compared with the stored ones (by key, action, source, person, net paid and the line itself, its counter aside) — so
    /// the export can say that the review changed after "Koosta read". False too for a stale search (the files
    /// changed after it). Null when it cannot be told (a build the period refuses: another cut on the server, no rates for
    /// its year, not a Draft). Never inside another unit of work: the
    /// rollback is the outermost scope's.</summary>
    public bool? DraftsCurrent(Guid periodId, DeclarationMode mode = DeclarationMode.PerPayment)
    {
        List<string> Drafts() => _store.Payments(periodId)
            .Where(p => p.Status == PaymentStatus.Draft)
            .Select(p => string.Join("|", p.Section, p.LineNumber, p.Action, p.SourceRef, p.PersonId, p.NetPaid, JsonSerializer.Serialize(p.Line with { Counter = 0 }, Json)))
            .Order(StringComparer.Ordinal)
            .ToList();
        var before = Drafts();
        List<string>? after = null;
        try
        {
            _store.InTransaction(() =>
            {
                Build(periodId, mode);
                after = Drafts();
                throw new DryRunDone(); // everything the build wrote goes back: drafts, the line counter, persons bound
            });
        }
        catch (DryRunDone)
        {
        }
        catch (DeclareException e) when (e.Code == RunStale)
        {
            return false; // a search that never saw every linked file: the drafts cannot be what a build would make
        }
        catch (Exception e) when (e is DeclareException or ReviewException)
        {
            return null;
        }

        return after is not null && before.SequenceEqual(after, StringComparer.Ordinal);
    }

    private sealed class DryRunDone : Exception;

    /// <summary>Builds the period's lines from the review (Draft only), cut as the mode says. Once lines of one cut are
    /// on the server, a build in the other cut is refused: it would cancel every line and declare the month anew.</summary>
    public BuildReport Build(Guid periodId, DeclarationMode mode = DeclarationMode.PerPayment)
    {
        var view = _workspace.Load(periodId);
        if (!view.Editable)
        {
            throw new DeclareException(ReviewWorkspace.PeriodNotDraft, $"The period is {view.Period.State}; its lines are built while it is a Draft.");
        }

        if (view.RunIsStale)
        {
            // the run never saw a file linked (or kept one unlinked) since, or there is no run and files to search: the lines
            // would miss or double payments, or retire every bank-row line
            throw new DeclareException(RunStale, view.Run is null
                ? "The period's files have not been searched; search (Otsi maksed) before building the lines."
                : "The period's files changed after the payments were searched; search again (Otsi maksed) before building the lines.");
        }

        var period = view.Period;
        var persons = view.StorePersons.ToDictionary(p => p.Id);
        var standing = _store.Payments(periodId).ToList();
        // the cut the month is declared in on the server: a line there, or the one beneath an M or R draft (a T draft is
        // the accountant's own cancellation, on its way out); the minimum's zero line belongs to both cuts
        var onServer = standing
            .Where(p => p.Action != LineAction.T)
            .Select(p => p.Status == PaymentStatus.Draft ? HistoryStanding(p) : p)
            .Where(p => p is { OnServer: true } && p.Action != LineAction.T && p.SourceRef != MinimumSource)
            .ToList();
        var otherCut = onServer.FirstOrDefault(p => IsTotals(p!) != (mode == DeclarationMode.TotalsPerType));
        if (otherCut is not null)
        {
            throw new DeclareException(ModeChanged, mode == DeclarationMode.TotalsPerType
                ? $"Line {otherCut.LineNumber} on the server stands for one payment; the month was declared payment by payment. Build it that way, or cancel its lines first."
                : $"Line {otherCut.LineNumber} on the server adds up a type; the month was declared as totals per type. Build it that way, or cancel its lines first.");
        }
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
        // the month's own year's rates; a year this build knows no rates for is refused, never computed with another's
        var rates = _ratesGiven ? _rates : TaxRates.For(period.Year)
            ?? throw new DeclareException(RatesMissing, $"This version knows no tax rates for {period.Year}; the month's lines cannot be built until a version that does.");
        var allocator = new MonthAllocator(new TaxEngine(rates));
        _store.InTransaction(() =>
        {
            foreach (var reviewPerson in view.Persons.Where(p => !p.IsUnmatched && p.PersonId is { } id && persons.ContainsKey(id)))
            {
                if (reviewPerson.Register is null && reviewPerson.Payments.Count == 0)
                {
                    // "kaasa" on the card, no register row and no payment this month (decision 137): shown in the review, nothing built
                    problems.Add($"{Included}: {persons[reviewPerson.PersonId!.Value].LastName} {persons[reviewPerson.PersonId!.Value].FirstName}: declared as if employed on the card, but the month's register has no row and no payment was found; no line is built for the person (a line standing from before is undone as usual).");
                    continue;
                }

                builds.Add(BuildPerson(period, reviewPerson, persons[reviewPerson.PersonId!.Value], bySource, claimed, allocator, mode));
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

    private PersonBuild BuildPerson(Period period, ReviewPerson reviewPerson, Person person, Dictionary<string, StoredPayment> bySource, HashSet<string> claimed, MonthAllocator allocator, DeclarationMode mode)
    {
        var name = $"{person.LastName} {person.FirstName}";
        var lines = new List<BuiltLine>();
        var problems = new List<string>();
        if (person.Declaration == DeclarationChoice.Exclude)
        {
            // the accountant's word: nothing is built and nothing claimed, so what stands for the person is undone below
            var left = reviewPerson.Payments.Count(p => p.Declare);
            problems.Add(left == 0
                ? $"{Excluded}: the person is excluded from the declaration."
                : $"{Excluded}: the person is excluded from the declaration; {left} payment(s) marked to declare are left out.");
            return new PersonBuild { PersonId = person.Id, Name = name, Lines = lines, Problems = problems };
        }

        var include = person.Declaration == DeclarationChoice.Include;
        var declared = new List<(ReviewPayment Payment, string Source)>();
        var refusedTypes = new HashSet<string>(StringComparer.Ordinal); // the totals cut: a type with a refused payment is not rebuilt
        var keptMinimum = false; // a refused payment's line left standing carries the minimum: no zero line beside it
        var keptPools = new List<string>(); // refused payments' lines on the server that used the month's exemption or minimum
        foreach (var payment in reviewPerson.Payments.Where(p => p.Declare))
        {
            var source = SourceOf(payment);
            if (payment.Checks.Count > 0)
            {
                problems.Add($"{Refused}: {payment.Reference}: {string.Join(", ", payment.Checks.Select(c => c.Code))}");
                if (payment.TypeCode is { } refusedType)
                {
                    refusedTypes.Add(refusedType);
                }

                if (bySource.TryGetValue(source, out var stale) && stale.Status == PaymentStatus.Draft)
                {
                    _store.DeleteDraft(stale.Id);
                    lines.Add(new BuiltLine { Key = stale.LineNumber, Section = stale.Section, Action = stale.Action, Change = LineChange.Deleted, Source = payment.Reference, Note = Refused });
                    NotePool(HistoryStanding(stale), keptPools);
                }
                else if (bySource.TryGetValue(source, out var kept))
                {
                    lines.Add(new BuiltLine { Key = kept.LineNumber, Section = kept.Section, Action = kept.Action, Change = LineChange.Kept, Source = payment.Reference, Note = Refused });
                    keptMinimum |= CarriesMinimum(kept);
                    NotePool(kept, keptPools);
                }

                claimed.Add(source);
                continue;
            }

            declared.Add((payment, source));
        }

        var monthPayments = new List<MonthPayment>();
        var references = new Dictionary<string, (string Reference, Guid? RowId, Guid? LineId)>(StringComparer.Ordinal);
        if (mode == DeclarationMode.TotalsPerType)
        {
            // one line per type: the payments of the type added up, dated by the latest of them; the line stands for the
            // person and the type, not for a row, so the payments' references travel in its source text
            var byType = declared.GroupBy(d => d.Payment.TypeCode!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Select(d => d.Payment).ToList(), StringComparer.Ordinal);
            foreach (var typeCode in byType.Keys.Concat(refusedTypes).Distinct(StringComparer.Ordinal))
            {
                var source = TotalsKey(person.Id, typeCode);
                if (refusedTypes.Contains(typeCode))
                {
                    // a refused payment in the type: what stands for the type, stands — as a refused payment's own line
                    // does in the other cut — until the payment is put right or taken out of the declaration
                    claimed.Add(source);
                    if (bySource.TryGetValue(source, out var stale) && stale.Status == PaymentStatus.Draft)
                    {
                        _store.DeleteDraft(stale.Id);
                        lines.Add(new BuiltLine { Key = stale.LineNumber, Section = stale.Section, Action = stale.Action, Change = LineChange.Deleted, Source = stale.SourceRef ?? "", Note = Refused });
                        NotePool(HistoryStanding(stale), keptPools);
                    }
                    else if (bySource.TryGetValue(source, out var kept))
                    {
                        lines.Add(new BuiltLine { Key = kept.LineNumber, Section = kept.Section, Action = kept.Action, Change = LineChange.Kept, Source = kept.SourceRef ?? "", Note = Refused });
                        keptMinimum |= CarriesMinimum(kept);
                        NotePool(kept, keptPools);
                    }

                    continue;
                }

                var type = PaymentTypes.Get(typeCode);
                var payments = byType[typeCode].OrderBy(p => p.Date).ThenBy(p => p.Reference, StringComparer.Ordinal).ToList();
                monthPayments.Add(new MonthPayment(source, type, payments.Sum(p => p.Amount), AmountIsNet: true, payments.Max(p => p.Date!.Value)));
                references[source] = (TotalsRef(typeCode, payments.Select(p => p.Reference)), null, null);
            }
        }
        else
        {
            foreach (var (payment, source) in declared)
            {
                var type = PaymentTypes.Get(payment.TypeCode!);
                monthPayments.Add(new MonthPayment(source, type, payment.Amount, AmountIsNet: true, payment.Date!.Value));
                // a bank row's line is known by its row alone; only a hand line's line carries the review line's id
                references[source] = (payment.Reference, payment.RowId, payment.RowId is null ? payment.LineId : null);
            }
        }

        // a month without pay of a minimum-carrying type under a valid employment whose own type carries it: the zero line (decision 3)
        var entry = reviewPerson.Register?.ActiveEntry;
        var inRegister = reviewPerson.Register is { Entries.Count: > 0 };
        // the employment the zero line's type comes from: the one covering the month; for a person declared as if employed
        // (Include) also a paused one, else the latest begun by the month's end, else the type kept on the person
        var lastDay = new DateOnly(period.Year, period.Month, DateTime.DaysInMonth(period.Year, period.Month));
        // the card's type, when the card says it stands for every payment, stands for the zero line too
        var cardType = person is { TypeFromCard: true, TorEmploymentTypeId: { Length: > 0 } card } && EmploymentTypes.Find(card) is not null ? card : null;
        // the valid employment the zero line's type comes from: the standing entry when its own type carries the minimum,
        // else another valid row of the month whose type does (a board seat newer than the Tööleping stands for the
        // month; the Tööleping carries the minimum)
        var carrier = entry is { StateId: TorStates.Valid } && Carries(entry.TypeId) ? entry
            : reviewPerson.Register?.Entries.Where(e => e.Overlaps(period.Year, period.Month) && e.StateId == TorStates.Valid && Carries(e.TypeId)).OrderByDescending(e => e.StartDate ?? DateOnly.MinValue).FirstOrDefault() ?? entry;
        var zeroTypeId = carrier is { StateId: TorStates.Valid } ? cardType ?? carrier.TypeId
            : !include ? null
            : cardType ?? (entry ?? reviewPerson.Register?.Entries.Where(e => e.StartDate is null || e.StartDate <= lastDay).OrderByDescending(e => e.StartDate ?? DateOnly.MinValue).FirstOrDefault())?.TypeId ?? person.TorEmploymentTypeId;
        if (person.SocialTaxMinimumApplies && !keptMinimum && !monthPayments.Any(m => m.Type.SocialTaxMinimum) && zeroTypeId is not null
            && EmploymentTypes.Find(zeroTypeId) is { SocialTaxMinimum: true }
            && EmploymentTypes.DefaultCode(zeroTypeId, person.Residency) is { } zeroCode && PaymentTypes.TryGet(zeroCode, out var zeroType) && zeroType!.SocialTaxMinimum && zeroType.ZeroAllowed
            && CarriesMinimumFor(zeroType, person, period.Year)) // the person's A1 case applied: a foreign A1 carries no Estonian social tax, so no minimum and no zero line
        {
            var zeroSource = MinimumKey(person.Id);
            monthPayments.Add(MonthPayment.Zero(zeroSource, zeroType, lastDay));
            references[zeroSource] = (MinimumSource, null, null);
            problems.Add(carrier is { StateId: TorStates.Valid }
                ? $"{ZeroLine}: a valid employment{(cardType is null ? "" : ", the card's employment type for every payment,")} of type {zeroTypeId} with no pay carrying the minimum this month; a zero line of type {zeroCode} carries it."
                : $"{ZeroLine}: declared as if employed (the person card's choice), type {zeroTypeId}, no pay carrying the minimum this month; a zero line of type {zeroCode} carries it.");
        }

        if (include && entry is not { StateId: TorStates.Valid })
        {
            // the card's choice is said, and so is why it made no zero line where one was looked for
            var unmet = person.SocialTaxMinimumApplies && !keptMinimum && !monthPayments.Any(m => m.Type.SocialTaxMinimum)
                ? zeroTypeId is null ? " No zero line: no employment type is known (neither the register nor the card's TÖR type)."
                : EmploymentTypes.Find(zeroTypeId) is not { SocialTaxMinimum: true } ? $" No zero line: employment type {zeroTypeId} carries no minimum."
                : EmploymentTypes.DefaultCode(zeroTypeId, person.Residency) is { } code && PaymentTypes.TryGet(code, out var type) && !CarriesMinimumFor(type!, person, period.Year) ? " No zero line: the person's A1 case carries no Estonian social tax."
                : ""
                : "";
            problems.Add($"{Included}: declared as if employed (the person card's choice); the register has no valid employment for the month.{unmet}");
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

        var share = MonthShare(entry, reviewPerson.Register?.Entries ?? [], period.Year, period.Month, include);
        var facts = person.FactsFor(period.Year, share);
        var fromRegisters = person.ResidencySource == LookupSource.Emta || person.PensionSource is LookupSource.Pensionikeskus or LookupSource.Derived;
        if (fromRegisters && person.FactsControlDate is { } asked && (asked.Year != period.Year || asked.Month != period.Month))
        {
            // a note, not a refusal: the facts stand as the registers said them for that day; this month may say otherwise
            problems.Add($"{FactsOfAnotherMonth}: the residency or the pension was last looked up for {asked:dd.MM.yyyy}, another month; ask the registers for this month (Päringud) if they may have changed.");
        }
        else if (fromRegisters && person.FactsControlDate is null
            && Earlier(person.ResidencySource == LookupSource.Emta ? person.ResidencyCheckedOn : null, person.PensionSource is LookupSource.Pensionikeskus or LookupSource.Derived ? person.PensionCheckedOn : null) is { } checkedOn
            && checkedOn < new DateOnly(period.Year, period.Month, 1))
        {
            // a person looked up before the control date was kept (schema v9): the day of the check stands in — the registers
            // are asked after the month, so only a check before the month's first day is of another month for sure
            problems.Add($"{FactsOfAnotherMonth}: the residency or the pension was checked on {checkedOn:dd.MM.yyyy}, before this month; ask the registers for this month (Päringud) if they may have changed.");
        }

        if (facts.Residency == Residency.NonResident && string.IsNullOrWhiteSpace(facts.ResidenceCountry))
        {
            // a note, not a refusal: the schema lets the line go without the country, MTA expects it and checks it
            problems.Add($"{NoResidenceCountry}: a non-resident declared without the country of tax residence (RR_MAKSURESIDENTSUSE_RIIK); MTA compares it with its register (KR_ISIK_6, red) — enter it on the person card when known.");
        }

        if (inRegister && entry is null && facts.SocialTaxMinimumApplies && !include)
        {
            // pay after the employment ended (a final settlement): the register covers no day of the month, so no minimum is owed on it
            facts = facts with { SocialTaxMinimumApplies = false };
            problems.Add($"{NoEmploymentInMonth}: no employment of the register covers the month; the minimum is not owed on this pay.");
        }
        else if (share == 0m)
        {
            // the employment is paused for every day of the month (or its pause has no start): no day counts, no minimum
            // is owed — the engine takes a whole share with the minimum switched off, never a share of nothing
            var owed = facts.SocialTaxMinimumApplies;
            facts = person.FactsFor(period.Year, 1m) with { SocialTaxMinimumApplies = false };
            if (owed)
            {
                problems.Add($"{NoEmploymentInMonth}: the employment is paused through the month (or the register does not say since when); the minimum is not owed on this pay.");
            }
        }

        if (keptPools.Count > 0 && monthPayments.Count > 0)
        {
            // a refused payment's line on the server used the month's exemption or minimum: the other payments would be given
            // them again (MTA refuses the month's exemption twice, KR_KOOND_02) — nothing is allocated until it is put right
            problems.Add($"{NotAllocated}: the line(s) {string.Join(", ", keptPools)} of a refused payment stand on the server and use the month's exemption or minimum; put the payment right (or take it out of the declaration) before the person's other payments are built.");
            foreach (var source in monthPayments.Select(m => m.Key))
            {
                // what stands on the server stands, nothing is cancelled; a draft, allocated in an earlier build beside the
                // refused payment's own draft (gone now), would go out with the month's exemption or minimum once more — it
                // goes, to be allocated afresh once the payment is put right
                claimed.Add(source);
                if (!bySource.TryGetValue(source, out var held))
                {
                    continue;
                }

                if (held.Status == PaymentStatus.Draft)
                {
                    _store.DeleteDraft(held.Id);
                    lines.Add(new BuiltLine { Key = held.LineNumber, Section = held.Section, Action = held.Action, Change = LineChange.Deleted, Source = references[source].Reference, Note = NotAllocated });
                }
                else
                {
                    lines.Add(new BuiltLine { Key = held.LineNumber, Section = held.Section, Action = held.Action, Change = LineChange.Kept, Source = references[source].Reference, Note = NotAllocated });
                }
            }

            return new PersonBuild { PersonId = person.Id, Name = name, Lines = lines, Problems = problems, MinimumShare = share };
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
                // the action follows what stands beneath the draft now, not what it was drafted as: an M over a copy
                // the server refused since (settled from the history view) must go out as an R
                var action = HistoryStanding(existing) is { OnServer: true } ? LineAction.M : LineAction.R;
                if (action == existing.Action)
                {
                    _store.ReplaceDraft(existing.Id, Draft(existing.LineNumber, action, null));
                }
                else
                {
                    // the store replaces a draft only under its own action: the draft goes and the line is drafted anew —
                    // checked against the key's rules first, so a refusal never costs the draft (the build's transaction
                    // does not undo a step that failed after another succeeded)
                    LineKeyRules.Check(action, _store.History(period.Id, existing.Section, existing.LineNumber).Where(v => v.Id != existing.Id).ToList());
                    _store.DeleteDraft(existing.Id);
                    _store.AddPayment(Draft(existing.LineNumber, action, null));
                }

                return new BuiltLine { Key = existing.LineNumber, Section = section, Action = action, Change = LineChange.Replaced, Source = reference };
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
                if (HistoryStanding(payment) is not { OnServer: true })
                {
                    // the copy the T was to cancel was refused since (settled from the history view): nothing to cancel
                    _store.DeleteDraft(payment.Id);
                    return new BuiltLine { Key = payment.LineNumber, Section = payment.Section, Action = payment.Action, Change = LineChange.Deleted, Source = source, Note = "nothing on the server to cancel" };
                }

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

    /// <summary>Notes a refused payment's line that stands on the server (not a T) and used the month's exemption or minimum.</summary>
    private static void NotePool(StoredPayment? line, List<string> pools)
    {
        if (line is { OnServer: true, Result: { } result } && line.Action != LineAction.T
            && (result.ExemptionUsed > 0 || result.SocialTaxMinimumApplies))
        {
            pools.Add(line.LineNumber);
        }
    }

    /// <summary>Whether a standing line (not its cancellation) is of a type that carries the minimum — so that the
    /// month's minimum is already on it, topped up there or owed through it.</summary>
    private static bool CarriesMinimum(StoredPayment line) =>
        line.Action != LineAction.T && line.Line.TypeCode is { } code && PaymentTypes.TryGet(code, out var type) && type!.SocialTaxMinimum;

    /// <summary>The source a standing line stands for: its bank row, its hand line, the month's minimum of its person,
    /// or the person's payments of one type added up.</summary>
    public static string? SourceKey(StoredPayment payment) =>
        payment.SourceRowId is { } row ? "row:" + row.ToString("N")
        : payment.ReviewLineId is { } line ? "line:" + line.ToString("N")
        : payment.SourceRef == MinimumSource && payment.PersonId is { } person ? MinimumKey(person)
        : TotalsOf(payment.SourceRef) is { } totals && payment.PersonId is { } owner ? TotalsKey(owner, totals.TypeCode)
        : null;

    /// <summary>Whether a standing line adds up a type (the totals cut) rather than standing for one payment. The
    /// minimum's zero line belongs to both cuts.</summary>
    public static bool IsTotals(StoredPayment payment) => TotalsOf(payment.SourceRef) is not null;

    /// <summary>The source text of a totals line: "totals:&lt;type&gt;:&lt;reference; reference; …&gt;".</summary>
    public static string TotalsRef(string typeCode, IEnumerable<string> references) => $"{TotalsSource}:{typeCode}:{string.Join("; ", references)}";

    /// <summary>The type and the payments' references of a totals line's source text; null for any other source.</summary>
    public static (string TypeCode, string References)? TotalsOf(string? sourceRef)
    {
        if (sourceRef is null || !sourceRef.StartsWith(TotalsSource + ":", StringComparison.Ordinal))
        {
            return null;
        }

        var rest = sourceRef[(TotalsSource.Length + 1)..];
        var colon = rest.IndexOf(':', StringComparison.Ordinal);
        return colon <= 0 ? null : (rest[..colon], rest[(colon + 1)..]);
    }

    private static string TotalsKey(Guid personId, string typeCode) => $"{TotalsSource}:{personId:N}:{typeCode}";

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
