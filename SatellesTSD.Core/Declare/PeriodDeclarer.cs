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
    public const string NotBuilt = "NOT_BUILT";

    /// <summary>The person was paid this month in minimum-carrying pay the review refused: no zero line, and no top-up on
    /// the other payments; the minimum waits for the payment to be put right (decision 207).</summary>
    public const string MinimumHeld = "MINIMUM_HELD";

    /// <summary>The rule for a salary paid on a day two employments of different types are in force (decision 209): the
    /// build reads the review through its own workspace, so the window sets it here too.</summary>
    public TwoContractsRule TwoContracts
    {
        get => _workspace.TwoContracts;
        set => _workspace.TwoContracts = value;
    }
    public const string NoPayee = "NO_PAYEE";
    public const string NoEmploymentInMonth = "NO_EMPLOYMENT_IN_MONTH";
    public const string UnpaidLeave = "UNPAID_LEAVE";
    public const string ModeChanged = "MODE_CHANGED";
    public const string RatesMissing = "RATES_MISSING";
    public const string RunStale = "RUN_STALE";
    public const string Excluded = "EXCLUDED";
    public const string Included = "INCLUDED";
    public const string FactsOfAnotherMonth = "FACTS_OF_ANOTHER_MONTH";
    public const string FactsOfAnotherDay = "FACTS_OF_ANOTHER_DAY";
    public const string NoResidenceCountry = "NO_RESIDENCE_COUNTRY";

    /// <summary>A note, not a refusal: the card has "Vanaduspensionil" ticked and the minimum on (decision 237).</summary>
    public const string MinimumWithPension = "MINIMUM_WITH_PENSION";

    /// <summary>A note, not a refusal: the card says Töötukassa pays the social tax (SMS § 6 lg 1 p 5), but pay of the month
    /// is of a type the deduction is not allowed on (matrix column O, KR_SM_05) while part of the kuumäär is left; the
    /// types are its <see cref="BuildNote.Items"/>.</summary>
    public const string WorkAbilityNotDeducted = "WORK_ABILITY_NOT_DEDUCTED";

    /// <summary>A note on the month's part of the kuumäär Töötukassa pays the social tax on (decision 267): "none" — the month
    /// the pay is for is covered on no day (<see cref="BuildNote.Day"/> its first day), nothing deducted; "lastMonth" — the
    /// employment ends in the declared month (<see cref="BuildNote.Day"/>), and a final pay paid in it would bring that
    /// month's share (<see cref="BuildNote.Figure"/>) to a total of <see cref="BuildNote.Other"/>, typed by hand;
    /// "notListed" — the month's register does not list the person, so an end of the employment is not seen; "endUnknown" —
    /// the register's rows of the person are ended without an end date, so their days are not known.</summary>
    public const string WorkAbilityMonthNote = "WORK_ABILITY_MONTH";

    /// <summary>What the person's build needs of the month for Töötukassa's part: the company's pay month, the amounts the
    /// accountant typed for the declared month, the month's rates.</summary>
    private sealed record WorkAbilityContext(bool WagesPaidFollowingMonth, IReadOnlyDictionary<Guid, decimal> ByHand, TaxRates Rates);

    /// <summary>The spans of employment Töötukassa's part is measured by: the register rows whose type is an employee's (the
    /// rows carrying the minimum — the same types the deduction is allowed on), else every row; a row ended without an
    /// end date says nothing of its days and is left out. Empty = the register says nothing.</summary>
    public static IReadOnlyList<(DateOnly? Start, DateOnly? End)> EmploymentSpans(IReadOnlyList<TorEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var carriers = entries.Where(e => Carries(e.TypeId)).ToList();
        return (carriers.Count > 0 ? carriers : entries)
            .Where(e => !(e.StateId == TorStates.Ended && e.EndDate is null))
            .Select(e => (e.StartDate, e.EndDate)).Distinct().ToList();
    }

    /// <summary>The part of the kuumäär Töötukassa pays the social tax on for the pay declared in a period, as reckoned
    /// (decision 267): the month the pay is for by the company's setting, its kuumäär, the days from the application's day
    /// (never before the employment began) to the employment's end.</summary>
    public static WorkAbilityMonth WorkAbilityMonthOf(Period period, Person person, IReadOnlyList<TorEntry> entries, bool wagesPaidFollowingMonth, TaxRates rates)
    {
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(person);
        ArgumentNullException.ThrowIfNull(rates);
        var payMonth = WorkAbilityMonth.PayMonthOf(period.Year, period.Month, wagesPaidFollowingMonth);
        // another year's kuumäär for pay of December declared in January; a year the app knows no figure for takes the month's
        var rate = payMonth.Year == period.Year ? rates.SocialTaxMinimumBase : WorkAbilityMonth.RateOf(payMonth.Year) ?? rates.SocialTaxMinimumBase;
        return WorkAbilityMonth.Reckon(payMonth, rate, person.WorkAbilityApplicationDate, EmploymentSpans(entries));
    }

    /// <summary>The source of a line that adds up a person's payments of one type: "totals:&lt;type&gt;:&lt;the payments' references&gt;".</summary>
    public const string TotalsSource = "totals";

    /// <summary>The source text of a zero line carrying the month's minimum.</summary>
    public const string MinimumSource = "minimum";

    private static readonly JsonSerializerOptions Json = LayoutCatalog.CompactJson;

    private readonly IStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly TaxRates _rates = rates ?? TaxRates.Year2026;
    private readonly bool _ratesGiven = rates is not null;
    private readonly ReviewWorkspace _workspace = new(store, rates, clock);

    /// <summary>The day each paid person is checked for as the review stands, worked out once per build of a period.</summary>
    private (Guid PeriodId, IReadOnlyDictionary<Guid, DateOnly>? Days)? _controlDays;

    /// <summary>The day the person's facts are checked for as the review stands: their latest payout of the month, else
    /// its last day — the lookups' own rule. None without a search.</summary>
    private DateOnly? ControlDayOf(Period period, Guid personId)
    {
        if (_controlDays is not { } known || known.PeriodId != period.Id)
        {
            known = (period.Id, LookupRunner.ControlDatesOf(_store, period));
            _controlDays = known;
        }

        return known.Days is null ? null : known.Days.TryGetValue(personId, out var day) ? day : LookupRunner.ControlDateOf(period);
    }

    /// <summary>The part of the month the minimum obligation covers: the employment's days in the month over 30
    /// (määrus nr 17 § 6), 1 for a whole month or an unknown employment.</summary>
    public static decimal MonthShare(TorEntry? entry, int year, int month) => MonthShare(entry, [], year, month);

    private static bool Carries(string? typeId) => EmploymentTypes.Find(typeId) is { SocialTaxMinimum: true };

    /// <summary>The codes that say a refused row is no pay of this person in this month: it holds no minimum (decision 207).</summary>
    private static readonly HashSet<string> NotThisMonthsPay = new(StringComparer.Ordinal)
        { ReviewChecks.NotInPeriod, ReviewChecks.MoneyIn, ReviewChecks.DuplicateRow, ReviewChecks.NotCounted };

    /// <summary>Whether a payment the review refused is still this month's pay of a type that carries the minimum: the
    /// person was paid, so the month is not one without pay (decision 207). A payment of no type or an unknown one cannot
    /// be told to be such pay.</summary>
    private static bool IsMonthsMinimumPay(ReviewPayment payment, Person person, int year) =>
        payment.Amount > 0m && !payment.Checks.Any(c => NotThisMonthsPay.Contains(c.Code))
        && (payment.TypeCode is { } code ? PaymentTypes.TryGet(code, out var type) && CarriesMinimumFor(type!, person, year) // under the person's A1 case (the fifth audit, item 15)
            : payment.Checks.Any(c => c.Code == ReviewChecks.TwoContracts)); // a salary asked for its type is pay all the same (decision 209)

    /// <summary>The share of the month the minimum is measured by (decision 89): the days covered by the person's register
    /// rows of the month whose employment type carries the minimum — the entry standing for the month among them when its
    /// own type does, else the person's other rows (a board seat or a VÕS row newer than the Tööleping stands for the
    /// month; the Tööleping carries the minimum) — so a contract ended on the 15th and the next begun on the 16th make a
    /// whole month; the standing entry alone when no row carries it. Each row counts the days it is in force by decision
    /// 181's rule (<see cref="TorEntry.InForceOn"/>): its days of pause (from "Peatamise alguse aeg" to "Peatamise lõpu
    /// aeg", or on) are left out, a paused row that does not say since when, an ended row that names no end and a row in a
    /// state the app does not know add nothing; the standing entry declared "kaasa" counts every day as if valid. A pause
    /// for unpaid leave is no such pause: its days count, the minimum is owed for them (decision 254). A day two rows
    /// cover counts once.</summary>
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
        void Mark(TorEntry e, bool asValid)
        {
            var (start, end) = Span(e);
            for (var day = start; day <= end; day = day.AddDays(1))
            {
                // by its dates and its state, as the zero line asks it (decision 181): an ended row with no end date, or a
                // state the app does not know, counts no day (the fifth audit, item 16); "kaasa" counts every day
                if (asValid || e.OwesMinimumOn(day)) // a pause for unpaid leave counts its days too (decision 254)
                {
                    covered[day.Day - 1] = true;
                }
            }
        }

        var (entryStart, entryEnd) = Span(entry);
        if (entryStart > entryEnd)
        {
            return 1m; // no overlap: not this build's business
        }

        // the rows the minimum is measured by: those whose type carries it — the standing entry among them when its own
        // does, else the person's other rows (a board seat or a VÕS row newer than the Tööleping stands for the month, the
        // Tööleping carries the minimum) — and, where no row carries it, the standing entry alone. Each counts the days it
        // is in force (a paused row its days outside the pause, an ended row its days up to its end, none without one); the
        // standing entry declared "kaasa" counts every day as if valid.
        var rows = new[] { entry }.Concat(others).ToList(); // the entry may be among the others too: a day marked twice is marked once
        var carriers = rows.Where(r => Carries(r.TypeId) && Span(r).Start <= Span(r).End).ToList(); // only rows that cover a day of the month
        foreach (var row in carriers.Count > 0 ? carriers : [entry])
        {
            Mark(row, asValid: treatPausedAsValid && (ReferenceEquals(row, entry) || row == entry));
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
            .Select(p =>
            {
                var history = _store.History(periodId, p.Section, p.LineNumber);
                var refused = DeclaredLine.RefusedOver(p, history); // a correction refused over what the server holds (decision 205)
                return new DeclaredLine
                {
                    Payment = p,
                    Person = p.PersonId is { } id && persons.TryGetValue(id, out var person) ? person : null,
                    LastFeedback = _store.Feedback(p.Id).LastOrDefault(),
                    Versions = history.Count,
                    RefusedAttempt = refused,
                    RefusedFeedback = refused is null ? null : _store.Feedback(refused.Id).LastOrDefault(),
                };
            })
            .ToList();
    }

    /// <summary>Whether a payment of this type carries the social-tax minimum for this person, their A1 case applied (the
    /// engine's rule); the pension fact, which the engine wants known, has no say in it and is given as "not a member".</summary>
    private static bool CarriesMinimumFor(PaymentType type, Person person, int year) =>
        EffectiveFlags.Of(type, person.FactsFor(year, 1) with { FundedPensionMember = false, PensionRate = null }).SocialTaxMinimum; // the month has no say in the minimum

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
        _controlDays = null; // the review may have changed since the last build: the persons' days are worked out afresh
        var view = _workspace.Load(periodId);
        if (!view.Editable)
        {
            throw new DeclareException(ReviewWorkspace.PeriodNotDraft, $"The period is {view.Period.State}; its lines are built while it is a Draft.") { Subject = view.Period.State.ToString() };
        }

        if (view.RunIsStale)
        {
            // the run never saw a file linked (or kept one unlinked) since, or there is no run and files to search: the lines
            // would miss or double payments, or retire every bank-row line
            throw new DeclareException(RunStale, view.Run is null
                ? "The period's files have not been searched; search (Otsi maksed) before building the lines."
                : "The period's files changed after the payments were searched; search again (Otsi maksed) before building the lines.")
                { Variant = view.Run is null ? "notSearched" : "changed" };
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
                : $"Line {otherCut.LineNumber} on the server adds up a type; the month was declared as totals per type. Build it that way, or cancel its lines first.")
            { Subject = otherCut.LineNumber, Variant = mode == DeclarationMode.TotalsPerType ? "perPayment" : "totals" };
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
        var problems = new List<BuildNote>();
        // the month's own year's rates; a year this build knows no rates for is refused, never computed with another's
        var rates = _ratesGiven ? _rates : TaxRates.For(period.Year)
            ?? throw new DeclareException(RatesMissing, $"This version knows no tax rates for {period.Year}; the month's lines cannot be built until a version that does.")
            { Subject = period.Year.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        var allocator = new MonthAllocator(new TaxEngine(rates));
        var work = new WorkAbilityContext(_store.Companies().First(c => c.Id == period.CompanyId).WagesPaidFollowingMonth, _store.WorkAbilityAmounts(period.Id), rates);
        // whether the month was searched against a register: only then does a person it does not list have no employment in it
        var monthHasRegister = view.StoredRun?.TorSheetId is not null || view.Persons.Any(p => p.Register is not null);
        _store.InTransaction(() =>
        {
            foreach (var reviewPerson in view.Persons.Where(p => !p.IsUnmatched && p.PersonId is { } id && persons.ContainsKey(id)))
            {
                if (reviewPerson.Register is null && reviewPerson.Payments.Count == 0)
                {
                    // "kaasa" on the card, no register row and no payment this month (decision 137): shown in the review, nothing built
                    var nobody = $"{persons[reviewPerson.PersonId!.Value].LastName} {persons[reviewPerson.PersonId!.Value].FirstName}";
                    problems.Add(new BuildNote(Included, $"{Included}: {nobody}: declared as if employed on the card, but the month's register has no row and no payment was found; no line is built for the person (a line standing from before is undone as usual).")
                    { Variant = "noRowNoPay", Subject = nobody });
                    continue;
                }

                builds.Add(BuildPerson(period, reviewPerson, persons[reviewPerson.PersonId!.Value], bySource, claimed, allocator, mode, monthHasRegister, work));
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

        return new BuildReport { Period = period, Persons = builds, Notes = problems };
    }

    private PersonBuild BuildPerson(Period period, ReviewPerson reviewPerson, Person person, Dictionary<string, StoredPayment> bySource, HashSet<string> claimed, MonthAllocator allocator, DeclarationMode mode, bool monthHasRegister, WorkAbilityContext work)
    {
        var name = $"{person.LastName} {person.FirstName}";
        var lines = new List<BuiltLine>();
        var problems = new List<BuildNote>();
        if (person.Declaration == DeclarationChoice.Exclude)
        {
            // the accountant's word: nothing is built and nothing claimed, so what stands for the person is undone below
            var left = reviewPerson.Payments.Count(p => p.Declare);
            problems.Add(new BuildNote(Excluded, left == 0
                ? $"{Excluded}: the person is excluded from the declaration."
                : $"{Excluded}: the person is excluded from the declaration; {left} payment(s) marked to declare are left out.") { Count = left });
            return new PersonBuild { PersonId = person.Id, Name = name, Lines = lines, Notes = problems };
        }

        var include = person.Declaration == DeclarationChoice.Include;
        var declared = new List<(ReviewPayment Payment, string Source)>();
        var refusedTypes = new HashSet<string>(StringComparer.Ordinal); // the totals cut: a type with a refused payment is not rebuilt
        var keptMinimum = false; // a refused payment's line left standing carries the minimum: no zero line beside it
        var keptPools = new List<string>(); // refused payments' lines on the server that used the month's exemption, minimum or Töötukassa deduction
        var minimumHeldBy = new List<string>(); // refused pay of the month that carries the minimum: the minimum waits for it (decision 207)
        foreach (var payment in reviewPerson.Payments.Where(p => p.Declare))
        {
            var source = SourceOf(payment);
            if (payment.Checks.Count > 0)
            {
                problems.Add(new BuildNote(Refused, $"{Refused}: {payment.Reference}: {string.Join(", ", payment.Checks.Select(c => c.Code))}")
                { Reference = payment.Reference, Items = payment.Checks.Select(c => c.Code).ToList() });
                if (IsMonthsMinimumPay(payment, person, period.Year))
                {
                    minimumHeldBy.Add(payment.Reference);
                }
                if (payment.TypeCode is { } refusedType)
                {
                    refusedTypes.Add(refusedType);
                }

                if (mode == DeclarationMode.TotalsPerType)
                {
                    // the type whose standing totals line adds the payment up is held as well, whatever type the payment
                    // has now (moved to another, or none): that line stands as it is until the payment is put right or
                    // taken out of the declaration (decision 79, amended by decision 199)
                    refusedTypes.UnionWith(TypesAddingUp(person.Id, payment, bySource));
                }

                if (bySource.TryGetValue(source, out var stale) && stale.Status == PaymentStatus.Draft)
                {
                    _store.DeleteDraft(stale.Id);
                    lines.Add(new BuiltLine { Key = stale.LineNumber, Section = stale.Section, Action = stale.Action, Change = LineChange.Deleted, Source = payment.Reference, Note = Refused });
                    NotePool(HistoryStanding(stale), keptPools);
                }
                else if (bySource.TryGetValue(source, out var kept) && kept.OnServer)
                {
                    // only a line on the server stands for the refused payment: a key refused black holds nothing, so it
                    // neither counts as kept nor carries the month's minimum (decision 200)
                    lines.Add(new BuiltLine { Key = kept.LineNumber, Section = kept.Section, Action = kept.Action, Change = LineChange.Kept, Source = payment.Reference, Note = Refused });
                    keptMinimum |= CarriesMinimum(kept, person, period.Year);
                    NotePool(kept, keptPools);
                }

                claimed.Add(source);
                continue;
            }

            declared.Add((payment, source));
        }

        var monthPayments = new List<MonthPayment>();
        var references = new Dictionary<string, (string Reference, Guid? RowId, Guid? LineId)>(StringComparer.Ordinal);
        void Withdraw(IEnumerable<string> sources, string note) => WithdrawSources(sources, note, bySource, claimed, references, lines);
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
                    if (byType.TryGetValue(typeCode, out var heldPay) && PaymentTypes.TryGet(typeCode, out var heldType) && CarriesMinimumFor(heldType!, person, period.Year))
                    {
                        minimumHeldBy.AddRange(heldPay.Where(p => p.Amount > 0m).Select(p => p.Reference)); // held, not unpaid (decisions 199, 207)
                    }

                    if (bySource.TryGetValue(source, out var stale) && stale.Status == PaymentStatus.Draft)
                    {
                        _store.DeleteDraft(stale.Id);
                        lines.Add(new BuiltLine { Key = stale.LineNumber, Section = stale.Section, Action = stale.Action, Change = LineChange.Deleted, Source = stale.SourceRef ?? "", Note = Refused });
                        NotePool(HistoryStanding(stale), keptPools);
                    }
                    else if (bySource.TryGetValue(source, out var kept) && kept.OnServer)
                    {
                        lines.Add(new BuiltLine { Key = kept.LineNumber, Section = kept.Section, Action = kept.Action, Change = LineChange.Kept, Source = kept.SourceRef ?? "", Note = Refused });
                        keptMinimum |= CarriesMinimum(kept, person, period.Year);
                        NotePool(kept, keptPools);
                    }

                    continue;
                }

                var type = PaymentTypes.Get(typeCode);
                var payments = byType[typeCode].OrderBy(p => p.Date).ThenBy(p => p.Reference, StringComparer.Ordinal).ToList();
                monthPayments.Add(new MonthPayment(source, type, payments.Sum(p => p.Amount), AmountIsNet: true, payments.Max(p => p.Date!.Value)));
                references[source] = (TotalsRef(typeCode, payments.Select(TotalsToken)), null, null);
            }
        }
        else
        {
            foreach (var (payment, source) in declared)
            {
                var type = PaymentTypes.Get(payment.TypeCode!);
                monthPayments.Add(new MonthPayment(source, type, payment.Amount, AmountIsNet: true, payment.Date!.Value));
                // a bank row's line is known by its row alone; a hand line's line, and a part's of a split row, carry the
                // review line's id as well (decision 150)
                references[source] = (payment.Reference, payment.RowId, payment.RowId is null || payment.IsPart ? payment.LineId : null);
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
        // "valid" is the month's, by the row's dates, not the state the export was taken in: a contract that ran through the
        // month and was ended or paused after it (as an export downloaded the next month shows it) carries the month's
        // minimum like a valid one (decision 181)
        // a row paused for unpaid leave carries the minimum through its pause, with or without pay (decision 254)
        bool InForce(TorEntry? e) => e is not null && e.OwesMinimumIn(period.Year, period.Month);
        var carrier = InForce(entry) && Carries(entry!.TypeId) ? entry
            : reviewPerson.Register?.Entries.Where(e => InForce(e) && Carries(e.TypeId)).OrderByDescending(e => e.StartDate ?? DateOnly.MinValue).FirstOrDefault() ?? entry;
        var zeroTypeId = InForce(carrier) ? cardType ?? carrier!.TypeId
            : !include ? null
            : cardType ?? (entry ?? reviewPerson.Register?.Entries.Where(e => e.StartDate is null || e.StartDate <= lastDay).OrderByDescending(e => e.StartDate ?? DateOnly.MinValue).FirstOrDefault())?.TypeId ?? person.TorEmploymentTypeId;
        // what the zero line's build says, said once the line is built: a build that stops later (no payee, a month that
        // cannot be computed, the line factory's refusal) withdraws it instead, and says that (the fifth audit, item 2)
        BuildNote? zeroNote = null;
        var zeroNoteAt = 0;
        if (person.MinimumOwed && !keptMinimum && !monthPayments.Any(m => CarriesMinimumFor(m.Type, person, period.Year)) && zeroTypeId is not null
            && EmploymentTypes.Find(zeroTypeId) is { SocialTaxMinimum: true }
            && EmploymentTypes.DefaultCode(zeroTypeId, person.Residency) is { } zeroCode && PaymentTypes.TryGet(zeroCode, out var zeroType) && zeroType!.SocialTaxMinimum && zeroType.ZeroAllowed
            && CarriesMinimumFor(zeroType, person, period.Year)) // the person's A1 case applied: a foreign A1 carries no Estonian social tax, so no minimum and no zero line
        {
            var zeroSource = MinimumKey(person.Id);
            if (person.ResidencyCheckedOn is null)
            {
                // the zero line never passes the review's checks, and its code follows the residency: one never checked
                // refuses it as it refuses every other line of the person (decision 191). A draft under it goes, a line
                // on the server stands, nothing is cancelled.
                problems.Add(new BuildNote(ReviewChecks.ResidencyNotChecked, $"{ReviewChecks.ResidencyNotChecked}: the minimum's zero line (type {zeroCode}) is not built: the person's residency has not been checked — by the register or by hand; a draft standing under it is withdrawn, a line on the server stands.")
                { Variant = "zeroLine", PaymentType = zeroCode });
                claimed.Add(zeroSource);
                if (bySource.TryGetValue(zeroSource, out var held))
                {
                    if (held.Status == PaymentStatus.Draft)
                    {
                        _store.DeleteDraft(held.Id);
                        lines.Add(new BuiltLine { Key = held.LineNumber, Section = held.Section, Action = held.Action, Change = LineChange.Deleted, Source = MinimumSource, Note = ReviewChecks.ResidencyNotChecked });
                    }
                    else if (held.OnServer)
                    {
                        lines.Add(new BuiltLine { Key = held.LineNumber, Section = held.Section, Action = held.Action, Change = LineChange.Kept, Source = MinimumSource, Note = ReviewChecks.ResidencyNotChecked });
                    }
                }
            }
            else if (minimumHeldBy.Count > 0)
            {
                // the person was paid this month in pay the review refused: not a month without pay, so no zero line; the
                // minimum waits for the payment to be put right or taken out of the declaration. A draft zero line goes,
                // one on the server stands, nothing is cancelled (decision 207, after 92 and 191)
                problems.Add(new BuildNote(MinimumHeld, $"{MinimumHeld}: no zero line (type {zeroCode}): the month's pay {string.Join(", ", minimumHeldBy.Distinct(StringComparer.Ordinal))} carries the minimum but was refused by the review; the minimum waits for it to be put right (or taken out of the declaration) — a draft zero line standing is withdrawn, a line on the server stands.")
                { Variant = "zeroLine", PaymentType = zeroCode, Items = minimumHeldBy.Distinct(StringComparer.Ordinal).ToList() });
                references[zeroSource] = (MinimumSource, null, null);
                Withdraw([zeroSource], MinimumHeld);
            }
            else if (PaymentChecks.Check(zeroType, person.FactsFor(period.Year, period.Month, 1m), 0m, _rates)
                .FirstOrDefault(r => r.Code is PaymentChecks.PensionNotChecked or PaymentChecks.PensionRateUnknown or PaymentChecks.PensionRateInvalid) is { } pension)
            {
                // the zero line's type withholds the funded pension where the person is a resident with no foreign A1: a status
                // (or rate) never checked refuses it up front under the review's own code, as the person's other lines are
                // refused, never by the engine's throw (NOT_ALLOCATED). Residency first (191), held pay next (207), then this.
                // A draft under it goes, a line on the server stands, nothing is cancelled (decision 238, refining 206)
                problems.Add(new BuildNote(pension.Code, $"{pension.Code}: the minimum's zero line (type {zeroCode}) is not built: the person's II-pillar status (or rate) has not been checked — ask the registers (Päringud) or confirm it on the card; a draft standing under it is withdrawn, a line on the server stands.")
                { Variant = "zeroLine", PaymentType = zeroCode });
                references[zeroSource] = (MinimumSource, null, null);
                Withdraw([zeroSource], pension.Code);
            }
            else
            {
                monthPayments.Add(MonthPayment.Zero(zeroSource, zeroType, lastDay));
                references[zeroSource] = (MinimumSource, null, null);
                zeroNoteAt = problems.Count;
                zeroNote = InForce(carrier)
                    ? new BuildNote(ZeroLine, $"{ZeroLine}: a valid employment{(carrier!.StateId == TorStates.Valid ? "" : carrier.StateId == TorStates.Ended ? $" in the month (the register has it ended on {carrier.EndDate:dd.MM.yyyy})" : $" in the month (the register has it paused from {carrier.PauseStart:dd.MM.yyyy})")}{(cardType is null ? "" : ", the card's employment type for every payment,")} of type {zeroTypeId} with no pay carrying the minimum this month; a zero line of type {zeroCode} carries it.")
                    {
                        Variant = carrier.StateId == TorStates.Valid ? "valid" : carrier.StateId == TorStates.Ended ? "ended" : carrier.PausedForUnpaidLeave ? "unpaidLeave" : "paused",
                        Day = carrier.StateId == TorStates.Valid ? null : carrier.StateId == TorStates.Ended ? carrier.EndDate : carrier.PauseStart,
                        FromCard = cardType is not null, TypeId = zeroTypeId, PaymentType = zeroCode,
                    }
                    : new BuildNote(ZeroLine, $"{ZeroLine}: declared as if employed (the person card's choice), type {zeroTypeId}, no pay carrying the minimum this month; a zero line of type {zeroCode} carries it.")
                    { Variant = "asIfEmployed", TypeId = zeroTypeId, PaymentType = zeroCode };
            }
        }

        if (include && !InForce(entry))
        {
            // the card's choice is said, and so is why it made no zero line where one was looked for; a month with payments
            // of the person none of which is declared says so too, with their refusals (OUTSIDE_TOR, say), not as a month
            // without pay. Only pay refused counts: salary-pattern money outside the register or a typed payment, in euros,
            // with a refusal; not money that is no income, nor a row the accountant left out by choice. The card's TÖR type
            // is asked for only when no zero line could be typed
            var inMonth = reviewPerson.Payments.Where(p => p.InPeriod && p.Direction != Direction.Credit && !p.Flags.Contains(MatchProblem.DuplicateRow, StringComparer.Ordinal)).ToList();
            var refusedPay = inMonth.Where(p => !p.Declare && p.Blockers.Count > 0 && ReviewChecks.IsEuro(p.Currency)
                && (p.OutsideTor is not null || p.TypeCode is not null || p.DefaultTypeCode is not null)).ToList();
            var noneDeclared = inMonth.Count > 0 && inMonth.All(p => !p.Declare) && refusedPay.Count > 0;
            var paid = noneDeclared
                ? $" The month has {refusedPay.Count} payment(s) for the person, not declared ({string.Join(", ", refusedPay.SelectMany(p => p.Blockers).Select(b => b.Code).Distinct(StringComparer.Ordinal))}): give the row a category or a payment type{(zeroTypeId is null ? ", or put the TÖR type on the card ('Kaardi töötamise liik kõigile maksetele') for the zero line" : "")}."
                : "";
            var unmetCase = person.MinimumOwed && !keptMinimum && minimumHeldBy.Count == 0 && !monthPayments.Any(m => CarriesMinimumFor(m.Type, person, period.Year))
                ? zeroTypeId is null ? noneDeclared ? "noTypeNoneDeclared" : "noType"
                : EmploymentTypes.Find(zeroTypeId) is not { SocialTaxMinimum: true } ? "typeNoMinimum"
                : EmploymentTypes.DefaultCode(zeroTypeId, person.Residency) is { } code && PaymentTypes.TryGet(code, out var type) && !CarriesMinimumFor(type!, person, period.Year) ? "a1"
                : null
                : null;
            var unmet = unmetCase switch
            {
                "noTypeNoneDeclared" => " No zero line and no line for the payment: no employment type is known (neither the register nor the card's TÖR type).",
                "noType" => " No zero line: no employment type is known (neither the register nor the card's TÖR type).",
                "typeNoMinimum" => $" No zero line: employment type {zeroTypeId} carries no minimum.",
                "a1" => " No zero line: the person's A1 case carries no Estonian social tax.",
                _ => "",
            };
            problems.Add(new BuildNote(Included, $"{Included}: declared as if employed (the person card's choice); the register has no valid employment for the month.{paid}{unmet}")
            {
                Variant = "noValid",
                Count = noneDeclared ? refusedPay.Count : null,
                Items = noneDeclared ? refusedPay.SelectMany(p => p.Blockers).Select(b => b.Code).Distinct(StringComparer.Ordinal).ToList() : [],
                AskCardType = noneDeclared && zeroTypeId is null,
                Unmet = unmetCase,
                TypeId = zeroTypeId,
            });
        }
        else if (include && person.MinimumOwed && entry is { StateId: TorStates.Paused, PauseStart: { } pausedFrom, PausedForUnpaidLeave: false } && Carries(entry.TypeId) // an unpaid leave owes the minimum without the card's word (254)
            && pausedFrom <= lastDay && (entry.PauseEnd is not { } pausedTo || pausedTo >= new DateOnly(period.Year, period.Month, 1))
            && (entry.StartDate is not { } begun || begun <= lastDay))
        {
            // in force on days of the month, paused on others: the card's choice counts the pause's days too, and says so
            problems.Add(new BuildNote(Included, $"{Included}: declared as if employed (the person card's choice); the register has the employment paused from {entry.PauseStart:dd.MM.yyyy}, and the minimum counts the pause's days as well.")
            { Variant = "paused", Day = entry.PauseStart });
        }

        if (monthPayments.Count == 0)
        {
            return new PersonBuild { PersonId = person.Id, Name = name, Lines = lines, Notes = problems };
        }

        if (person.PayeeId is not { } payee)
        {
            problems.Add(new BuildNote(NoPayee, $"{NoPayee}: the person has neither a personal code nor an MTA non-resident code; nothing built — a draft standing for the month's pay is withdrawn, a line on the server stands."));
            Withdraw(monthPayments.Select(m => m.Key), NoPayee);
            return new PersonBuild { PersonId = person.Id, Name = name, Lines = lines, Notes = problems };
        }

        if (ReviewChecks.InvalidCode(person) is { } invalidCode)
        {
            // the zero line never passes the review's checks: the same refusal here, so no line is built under a code
            // e-MTA cannot know (decision 142). A draft standing under it (built before the code was found wrong) goes,
            // since the export takes every draft; a line on the server stands, nothing is cancelled.
            problems.Add(new BuildNote(ReviewChecks.CodeInvalid, $"{ReviewChecks.CodeInvalid}: the person's code '{invalidCode}' is not an Estonian personal code; nothing built under it — a draft standing under it is withdrawn, a line on the server stands. Correct the code on the card.")
            { Subject = invalidCode });
            Withdraw(monthPayments.Select(m => m.Key), ReviewChecks.CodeInvalid);
            return new PersonBuild { PersonId = person.Id, Name = name, Lines = lines, Notes = problems };
        }

        var share = MonthShare(entry, reviewPerson.Register?.Entries ?? [], period.Year, period.Month, include);
        var facts = person.FactsFor(period.Year, period.Month, share);
        var fromRegisters = person.ResidencySource == LookupSource.Emta || person.PensionSource is LookupSource.Pensionikeskus or LookupSource.Derived;
        if (fromRegisters && person.FactsControlDate is { } asked && (asked.Year != period.Year || asked.Month != period.Month))
        {
            // a note, not a refusal: the facts stand as the registers said them for that day; this month may say otherwise
            problems.Add(new BuildNote(FactsOfAnotherMonth, $"{FactsOfAnotherMonth}: the residency or the pension was last looked up for {asked:dd.MM.yyyy}, another month; ask the registers for this month (Päringud) if they may have changed.")
            { Variant = "lookedUpFor", Day = asked });
        }
        else if (fromRegisters && person.FactsControlDate is { } checkedFor && ControlDayOf(period, person.Id) is { } due && due != checkedFor)
        {
            // a note, not a refusal: the registers answered for one day of the month, and the review now pays the person on
            // another (a row given to them or taken away, a line added, or a check made before the search) — the residency or
            // the membership may differ between the two days (decision 166)
            problems.Add(new BuildNote(FactsOfAnotherDay, $"{FactsOfAnotherDay}: the residency and the pension were looked up for {checkedFor:dd.MM.yyyy}; as the review stands the person's day is {due:dd.MM.yyyy}; ask the registers again (Päringud).")
            { Day = checkedFor, OtherDay = due });
        }
        else if (fromRegisters && person.FactsControlDate is null
            && Earlier(person.ResidencySource == LookupSource.Emta ? person.ResidencyCheckedOn : null, person.PensionSource is LookupSource.Pensionikeskus or LookupSource.Derived ? person.PensionCheckedOn : null) is { } checkedOn
            && checkedOn < new DateOnly(period.Year, period.Month, 1))
        {
            // a person looked up before the control date was kept (schema v9): the day of the check stands in — the registers
            // are asked after the month, so only a check before the month's first day is of another month for sure
            problems.Add(new BuildNote(FactsOfAnotherMonth, $"{FactsOfAnotherMonth}: the residency or the pension was checked on {checkedOn:dd.MM.yyyy}, before this month; ask the registers for this month (Päringud) if they may have changed.")
            { Variant = "checkedOn", Day = checkedOn });
        }

        if (facts.Residency == Residency.NonResident && string.IsNullOrWhiteSpace(facts.ResidenceCountry))
        {
            // a note, not a refusal: the schema lets the line go without the country, MTA expects it and checks it
            problems.Add(new BuildNote(NoResidenceCountry, $"{NoResidenceCountry}: a non-resident declared without the country of tax residence (RR_MAKSURESIDENTSUSE_RIIK); MTA compares it with its register (KR_ISIK_6, red) — enter it on the person card when known."));
        }

        if (inRegister && entry is null && facts.SocialTaxMinimumApplies && !include)
        {
            // pay after the employment ended (a final settlement): the register covers no day of the month, so no minimum is owed on it
            facts = facts with { SocialTaxMinimumApplies = false };
            problems.Add(new BuildNote(NoEmploymentInMonth, $"{NoEmploymentInMonth}: no employment of the register covers the month; the minimum is not owed on this pay.") { Variant = "notCovered" });
        }
        else if (monthHasRegister && reviewPerson.Register is null && facts.SocialTaxMinimumApplies && !include)
        {
            // a store person the month's register does not list at all (a contract ended in an earlier month drops out of a
            // month's export), paid by hand or by a row given to them: no employment of the month, no minimum — the same as
            // pay after an ended row the export still lists; "kaasa" on the card declares them as if employed (decision 176)
            facts = facts with { SocialTaxMinimumApplies = false };
            problems.Add(new BuildNote(NoEmploymentInMonth, $"{NoEmploymentInMonth}: the month's register does not list the person; the minimum is not owed on this pay (set the card's Deklareerimine to \"kaasa\" if the person was employed in the month).")
            { Variant = "notListed" });
        }
        else if (person.MinimumOwed && share > 0m
            // said only where the month's pay carries the minimum and nothing holds it back: with a zero line the zero
            // line's own note says it; under a foreign A1, or with the minimum held, nothing is counted to speak of
            && zeroNote is null && minimumHeldBy.Count == 0 && (keptMinimum || monthPayments.Any(m => CarriesMinimumFor(m.Type, person, period.Year)))
            && (reviewPerson.Register?.Entries ?? []).Concat(entry is null ? [] : [entry]).FirstOrDefault(e => e.PausedForUnpaidLeave && Carries(e.TypeId)
                && (e.PauseStart is not { } from || from <= lastDay) && (e.PauseEnd is not { } to || to >= new DateOnly(period.Year, period.Month, 1))
                && e.OwesMinimumIn(period.Year, period.Month)) is { } leave)
        {
            // paused for unpaid leave on days of the month: the minimum is owed for them all the same, and it is said
            problems.Add(new BuildNote(UnpaidLeave, $"{UnpaidLeave}: the register has the employment paused for unpaid leave{(leave.PauseStart is { } since ? $" from {since:dd.MM.yyyy}" : "")}; the minimum is owed for the pause's days as well.")
            { Day = leave.PauseStart });
        }
        else if (share == 0m)
        {
            // the employment is paused for every day of the month (or its pause has no start), or in force on no day of it
            // (an ended row that names no end, a state the app does not know — the fifth audit, item 16): no day counts, no
            // minimum is owed — the engine takes a whole share with the minimum switched off, never a share of nothing
            var owed = facts.SocialTaxMinimumApplies;
            facts = person.FactsFor(period.Year, period.Month, 1m) with { SocialTaxMinimumApplies = false };
            if (owed)
            {
                // why no day counts, told from the rows the share was measured by (the rows carrying the minimum, else the
                // standing entry alone), not from the standing entry's own state: a valid board seat beside a Tööleping
                // paused or ended without a date is not "a state the app does not know" (decision 257)
                var known = new[] { entry }.OfType<TorEntry>().Concat(reviewPerson.Register?.Entries ?? []).ToList();
                var carrying = known.Where(e => Carries(e.TypeId) && e.Overlaps(period.Year, period.Month)).ToList();
                var measured = carrying.Count > 0 ? carrying : known.Take(1).ToList();
                var unknownState = measured.FirstOrDefault(e => e.StateId is not (TorStates.Valid or TorStates.Paused or TorStates.Ended));
                problems.Add(measured.Any(e => e.StateId == TorStates.Paused)
                    ? new BuildNote(NoEmploymentInMonth, $"{NoEmploymentInMonth}: the employment is paused through the month (or the register does not say since when); the minimum is not owed on this pay. If the pause is an unpaid leave the register does not name as one, the minimum is owed: set the card's Deklareerimine to \"kaasa\".") { Variant = "paused" }
                    : measured.Any(e => e is { StateId: TorStates.Ended, EndDate: null })
                        ? new BuildNote(NoEmploymentInMonth, $"{NoEmploymentInMonth}: the register row carrying the minimum is ended (kande olek {TorStates.Ended}) and names no end date, so no day of the month counts; the minimum is not owed on this pay.")
                        { Variant = "endedNoEnd" }
                        : new BuildNote(NoEmploymentInMonth, $"{NoEmploymentInMonth}: no register row carrying the minimum is in force on a day of the month (kande olek '{unknownState?.StateId ?? entry?.StateId}' the app does not know); the minimum is not owed on this pay.")
                        { Variant = "notInForce", Subject = unknownState?.StateId ?? entry?.StateId });
            }
        }

        if (minimumHeldBy.Count > 0 && !keptMinimum && facts.SocialTaxMinimumApplies && monthPayments.Any(m => CarriesMinimumFor(m.Type, person, period.Year)))
        {
            // refused pay carries the month's minimum beside these payments: topped up here, the minimum would be declared on
            // them as if the refused pay did not exist, and again on it once it is put right — it waits (decision 207)
            facts = facts with { SocialTaxMinimumApplies = false };
            problems.Add(new BuildNote(MinimumHeld, $"{MinimumHeld}: the month's pay {string.Join(", ", minimumHeldBy.Distinct(StringComparer.Ordinal))} carries the minimum but was refused by the review; the person's other payments are built without the minimum's top-up until it is put right (or taken out of the declaration).")
            { Variant = "topUp", Items = minimumHeldBy.Distinct(StringComparer.Ordinal).ToList() });
        }

        if (facts.SocialTaxMinimumApplies && person.OnOldAgeOrFlexiblePension && monthPayments.Any(m => CarriesMinimumFor(m.Type, person, period.Year)))
        {
            // a note, not a refusal: the figures follow the card's switch, the accountant decides (decision 3) — said after
            // the switches above, so a month that owes no minimum anyway is not noted; a zero line of the minimum is pay
            // carrying it, so a pensioner with no pay is told too (decision 237)
            problems.Add(new BuildNote(MinimumWithPension, $"{MinimumWithPension}: the card says the person is on an old-age or flexible pension and the social tax minimum applies; a state pension's recipient owes no minimum (SMS § 2(4) p 6) — untick the minimum on the person card if the pension is paid."));
        }

        if (keptPools.Count > 0 && monthPayments.Count > 0)
        {
            // a refused payment's line on the server used the month's exemption, minimum or Töötukassa deduction: the other
            // payments would be given them again (MTA refuses the month's exemption twice, KR_KOOND_02; a second kuumäär of
            // TSD_OPTV_SM_VAHENDUS passes the server's 2 × kuumäär bound, KR_SM_07, yet breaks SMS § 6 lg 3) — nothing is
            // allocated until it is put right
            problems.Add(new BuildNote(NotAllocated, $"{NotAllocated}: the line(s) {string.Join(", ", keptPools)} of a refused payment stand on the server and use the month's exemption, minimum or Töötukassa deduction; put the payment right (or take it out of the declaration) before the person's other payments are built.")
            { Variant = "keptPools", Items = keptPools.ToList() });
            // what stands on the server stands, nothing is cancelled; a draft, allocated in an earlier build beside the
            // refused payment's own draft (gone now), would go out with the month's pool once more — it
            // goes, to be allocated afresh once the payment is put right
            Withdraw(monthPayments.Select(m => m.Key), NotAllocated);
            return new PersonBuild { PersonId = person.Id, Name = name, Lines = lines, Notes = problems, MinimumShare = share };
        }

        // Töötukassa's part of the month (decision 267): the kuumäär of the month the pay is for, by the days the application
        // and the employment cover — or the accountant's own amount for the declared month (two months' pay paid in one)
        WorkAbilityMonth? stateMonth = null;
        if (person is { ReducedWorkAbility: true, StatePaysSocialTax: true })
        {
            var rows = (reviewPerson.Register?.Entries ?? []).Concat(entry is null ? [] : [entry]).Distinct().ToList();
            var reckoned = WorkAbilityMonthOf(period, person, rows, work.WagesPaidFollowingMonth, work.Rates);
            stateMonth = work.ByHand.TryGetValue(person.Id, out var typed)
                ? reckoned.WithAmountByHand(Math.Min(typed, WorkAbilityMonth.MostRates * work.Rates.SocialTaxMinimumBase))
                : reckoned;
            // said only where the month has pay the deduction can be made on — by the type under the person's A1 case, as
            // the engine allows it: a foreign A1 carries no Estonian social tax, an Estonian A1 none on 33 and 138
            if (!stateMonth.ByHand && monthPayments.Any(m => m.Amount > 0m && EffectiveFlags.Of(m.Type, m.Facts ?? facts).WorkAbilityReduction))
            {
                // notes, not refusals: each says what the accountant may have to type by hand for the month
                var spans = EmploymentSpans(rows);
                var monthFirst = new DateOnly(period.Year, period.Month, 1);
                if (stateMonth.Amount == 0m)
                {
                    problems.Add(new BuildNote(WorkAbilityMonthNote, $"{WorkAbilityMonthNote}: the pay is taken to be for {stateMonth.PayMonth:MM.yyyy}, and no day of that month is covered by both the application and the employment; nothing is deducted. If this pay is for another month, type the month's amount by hand.")
                    { Variant = "none", Day = stateMonth.PayMonth });
                }
                else if (monthHasRegister && reviewPerson.Register is null)
                {
                    problems.Add(new BuildNote(WorkAbilityMonthNote, $"{WorkAbilityMonthNote}: the month's register does not list the person, so an end of the employment is not seen; {stateMonth.Amount} is taken for {stateMonth.PayMonth:MM.yyyy}. If the employment ended in that month, type the month's amount by hand.")
                    { Variant = "notListed", Day = stateMonth.PayMonth, Figure = stateMonth.Amount });
                }
                else if (rows.Count > 0 && spans.Count == 0)
                {
                    // rows there are, none with days that can be counted (ended, no end date): the whole month was taken
                    problems.Add(new BuildNote(WorkAbilityMonthNote, $"{WorkAbilityMonthNote}: the register's rows of the person are ended without an end date, so the employment's days are not known; {stateMonth.Amount} is taken for {stateMonth.PayMonth:MM.yyyy}. If the employment ended in or before that month, type the month's amount by hand.")
                    { Variant = "endUnknown", Day = stateMonth.PayMonth, Figure = stateMonth.Amount });
                }
                else if (work.WagesPaidFollowingMonth
                    && spans.Where(s => s.End is { } e && e >= monthFirst && e <= lastDay).Select(s => s.End!.Value).DefaultIfEmpty().Max() is var ended && ended != default
                    && !spans.Any(s => (s.Start is not { } from || from <= lastDay) && (s.End is not { } to || to > lastDay))
                    && WorkAbilityMonth.Reckon(monthFirst, work.Rates.SocialTaxMinimumBase, person.WorkAbilityApplicationDate, spans) is { Amount: > 0m } ending)
                {
                    // the employment ends in the declared month: a final pay paid now is pay for this month too, a second
                    // month's share the app cannot tell from the rows (MTA's example of a final pay)
                    problems.Add(new BuildNote(WorkAbilityMonthNote, $"{WorkAbilityMonthNote}: the employment ends on {ended:dd.MM.yyyy}, in the declared month; {stateMonth.Amount} is taken for {stateMonth.PayMonth:MM.yyyy}. If the final pay for the ending month is paid in this month too, its share is {ending.Amount}: type {Math.Min(stateMonth.Amount + ending.Amount, WorkAbilityMonth.MostRates * work.Rates.SocialTaxMinimumBase)} by hand.")
                    { Variant = "lastMonth", Day = ended, Figure = ending.Amount, Other = Math.Min(stateMonth.Amount + ending.Amount, WorkAbilityMonth.MostRates * work.Rates.SocialTaxMinimumBase) });
                }
            }
        }

        MonthAllocation allocation;
        try
        {
            allocation = allocator.Allocate(monthPayments, facts, stateMonth?.Amount);
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException)
        {
            // what stands on the server stands; nothing is cancelled over a month that cannot be computed — but a draft of
            // an earlier build goes, since the export takes every draft and its figures are no longer the month's
            problems.Add(new BuildNote(NotAllocated, $"{NotAllocated}: {e.Message} Nothing built — a draft standing for the month's pay is withdrawn, a line on the server stands.")
            { Variant = "engine", Cause = e });
            Withdraw(monthPayments.Select(m => m.Key), NotAllocated);
            return new PersonBuild { PersonId = person.Id, Name = name, Lines = lines, Notes = problems, MinimumShare = share };
        }

        if (allocation.WorkAbilityReductionLeft > 0m
            && allocation.Payments.Where(a => a.Result is { SocialTaxApplies: true, WorkAbilityReductionApplies: false } && a.Result.Gross > 0m).ToList() is { Count: > 0 } fullTax)
        {
            // a note, not a refusal: the state pays the social tax only on wages (matrix column O, KR_SM_05); pay of the other
            // types — a board fee, a VÕS fee — is taxed in full while part of the month's kuumäär is left unused. A type that
            // allows the deduction but not under the person's Estonian A1 (33, 138: the Estonian-A1 row's column O is not
            // known, decision 263) gets a note of its own, so the type is not named as both allowed and not
            var notAllowed = fullTax.Where(a => !a.Payment.Type.WorkAbilityReductionAllowed).Select(a => a.Payment.Type.Code).Distinct(StringComparer.Ordinal).ToList();
            var notUnderA1 = fullTax.Where(a => a.Payment.Type.WorkAbilityReductionAllowed).Select(a => a.Payment.Type.Code).Distinct(StringComparer.Ordinal).ToList();
            if (notAllowed.Count > 0)
            {
                problems.Add(new BuildNote(WorkAbilityNotDeducted, $"{WorkAbilityNotDeducted}: the card says Töötukassa pays the social tax, but the month's pay of type {string.Join(", ", notAllowed)} allows no deduction (TSD_OPTV_SM_VAHENDUS only on 10, 33, 37, 120, 138, 167; KR_SM_05): its social tax is the employer's in full.")
                { Items = notAllowed });
            }
            if (notUnderA1.Count > 0)
            {
                problems.Add(new BuildNote(WorkAbilityNotDeducted, $"{WorkAbilityNotDeducted}: the card says Töötukassa pays the social tax, but under the person's Estonian A1 the month's pay of type {string.Join(", ", notUnderA1)} gets no deduction (MTA's matrix does not show the Estonian A1's row for the type, so the app makes none): its social tax is the employer's in full.")
                { Variant = "a1", Items = notUnderA1 });
            }
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
                if (zeroNote is not null && source == MinimumKey(person.Id))
                {
                    problems.Insert(Math.Min(zeroNoteAt, problems.Count), zeroNote);
                }
            }
            catch (LineKeyException e)
            {
                problems.Add(new BuildNote(e.Code, $"{e.Code}: {reference}: {e.Message}") { Variant = "keyRule", Reference = reference, Cause = e });
            }
            catch (InvalidOperationException e)
            {
                // the line factory refused the line (an A1 case without its countries, say): a draft of an earlier build
                // goes with it, its figures or facts no longer the review's; a line on the server stands
                problems.Add(new BuildNote(NotBuilt, $"{NotBuilt}: {reference}: {e.Message} A draft standing for it is withdrawn, a line on the server stands.")
                { Reference = reference, Cause = e });
                Withdraw([source], NotBuilt);
            }
        }

        return new PersonBuild
        {
            PersonId = person.Id,
            Name = name,
            Lines = lines,
            Notes = problems,
            MinimumShare = share,
            ExemptionOffered = allocation.ExemptionOffered,
            ExemptionLeft = allocation.ExemptionLeft,
            MinimumDue = allocation.MinimumDue,
            MinimumShortfall = allocation.MinimumShortfall,
            TopUpKey = allocation.TopUpKey is { } top && references.TryGetValue(top, out var topRef) ? topRef.Reference : allocation.TopUpKey,
            WorkAbilityReductionOffered = allocation.WorkAbilityReductionOffered,
            WorkAbilityReduction = allocation.WorkAbilityReductionTotal,
            WorkAbilityMonth = stateMonth,
        };
    }

    /// <summary>The month's sources withdrawn from a build that stops (the fifth audit, item 2; decisions 142, 191): each
    /// is claimed, so nothing standing for it is retired or cancelled; a draft under it goes — the export takes every
    /// draft, and one of an earlier build carries figures or facts that are no longer the review's — and a line on the
    /// server stands.</summary>
    private void WithdrawSources(IEnumerable<string> sources, string note, Dictionary<string, StoredPayment> bySource, HashSet<string> claimed, Dictionary<string, (string Reference, Guid? RowId, Guid? LineId)> references, List<BuiltLine> lines)
    {
        foreach (var source in sources)
        {
            claimed.Add(source);
            if (!bySource.TryGetValue(source, out var held))
            {
                continue;
            }

            var reference = references.TryGetValue(source, out var known) ? known.Reference : source;
            if (held.Status == PaymentStatus.Draft)
            {
                // the line's own reconciling may have taken the draft away before it was refused (a draft of another action
                // or section): deleted once, said once
                if (_store.History(held.PeriodId, held.Section, held.LineNumber).Any(v => v.Id == held.Id && v.Status == PaymentStatus.Draft))
                {
                    _store.DeleteDraft(held.Id);
                }

                lines.Add(new BuiltLine { Key = held.LineNumber, Section = held.Section, Action = held.Action, Change = LineChange.Deleted, Source = reference, Note = note });
            }
            else if (held.OnServer)
            {
                // a key refused black or cancelled holds nothing, so it is not said to stand (decision 200)
                lines.Add(new BuiltLine { Key = held.LineNumber, Section = held.Section, Action = held.Action, Change = LineChange.Kept, Source = reference, Note = note });
            }
        }
    }

    private BuiltLine Reconcile(Period period, Person person, StoredPayment? existing, string reference, Guid? rowId, Guid? lineId, PaymentType type, Section section, PayeeTaxFacts facts, AllocatedPayment allocated, List<BuildNote> problems)
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
                var beneath = HistoryStanding(existing);
                var action = beneath is { OnServer: true } ? LineAction.M : LineAction.R;
                if (action == LineAction.M && beneath!.Action != LineAction.T && SameLine(Draft(existing.LineNumber, action, null).Line, beneath.Line))
                {
                    // the review is back to the figures on the server: an M would change nothing, so the draft goes and
                    // the line beneath stands (decision 198)
                    _store.DeleteDraft(existing.Id);
                    _store.SettleRefusals(period.Id, beneath.Section, beneath.LineNumber, beneath.Version); // a refusal over it is moot (decision 205)
                    return new BuiltLine { Key = existing.LineNumber, Section = section, Action = beneath.Action, Change = LineChange.Kept, Source = reference };
                }

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
            problems.Add(new BuildNote(CancellationPending, $"{CancellationPending}: {reference}: the cancellation of {existing.LineNumber} awaits MTA's verdict; the line follows once it is settled.")
            { Variant = "reconcile", Reference = reference, Key = existing.LineNumber });
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
            problems.Add(new BuildNote(SectionChanged, $"{SectionChanged}: {reference}: the type moved to {section}; line {existing.LineNumber} is cancelled first, the new line follows once MTA accepts the cancellation.")
            { Reference = reference, Section = section, Key = existing.LineNumber });
            return new BuiltLine { Key = existing.LineNumber, Section = existing.Section, Action = LineAction.T, Change = LineChange.Cancelled, Source = reference, Note = SectionChanged };
        }

        var change = Draft(existing.LineNumber, LineAction.M, null);
        if (SameLine(change.Line, existing.Line))
        {
            // the review wants what the server holds: a correction or cancellation refused over it is moot (decision 205)
            _store.SettleRefusals(period.Id, existing.Section, existing.LineNumber, existing.Version);
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
    private BuiltLine? Retire(StoredPayment payment, List<BuildNote> problems)
    {
        var source = payment.SourceRef ?? "";
        switch (payment.Status)
        {
            case PaymentStatus.Draft when payment.Action == LineAction.T:
                if (HistoryStanding(payment) is not { OnServer: true })
                {
                    // the copy the T was to cancel was refused since (settled from the history view): nothing to cancel
                    _store.DeleteDraft(payment.Id);
                    return new BuiltLine { Key = payment.LineNumber, Section = payment.Section, Action = payment.Action, Change = LineChange.Deleted, Source = source, Note = BuiltLine.NothingToCancel };
                }

                return new BuiltLine { Key = payment.LineNumber, Section = payment.Section, Action = payment.Action, Change = LineChange.Kept, Source = source, Note = BuiltLine.CancellationDrafted };
            case PaymentStatus.Draft:
                _store.DeleteDraft(payment.Id);
                var beneath = HistoryStanding(payment);
                if (beneath is { OnServer: true } && beneath.Action != LineAction.T)
                {
                    return Cancel(beneath, source);
                }

                return new BuiltLine { Key = payment.LineNumber, Section = payment.Section, Action = payment.Action, Change = LineChange.Deleted, Source = source };
            case PaymentStatus.Sent or PaymentStatus.Accepted or PaymentStatus.NeedsChange or PaymentStatus.Review when payment.Action == LineAction.T:
                problems.Add(new BuildNote(CancellationPending, $"{CancellationPending}: {source}: the cancellation of {payment.LineNumber} awaits MTA's verdict.")
                { Variant = "retire", Reference = source, Key = payment.LineNumber });
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

    /// <summary>Notes a refused payment's line that stands on the server (not a T) and used one of the month's pools: the
    /// exemption, the minimum or the Töötukassa deduction (TSD_OPTV_SM_VAHENDUS, one kuumäär a month, SMS § 6 lg 3) — the
    /// allocator starts each month's pools afresh, so the other payments would be given the pool once more.</summary>
    private static void NotePool(StoredPayment? line, List<string> pools)
    {
        if (line is { OnServer: true, Result: { } result } && line.Action != LineAction.T
            && (result.ExemptionUsed > 0 || result.SocialTaxMinimumApplies || result.WorkAbilityReduction > 0))
        {
            pools.Add(line.LineNumber);
        }
    }

    /// <summary>Whether a standing line (not its cancellation) is of a type that carries the minimum — so that the
    /// month's minimum is already on it, topped up there or owed through it — under the person's A1 case as the card says
    /// it now, as the zero line asks it (the fifth audit, item 15).</summary>
    private static bool CarriesMinimum(StoredPayment line, Person person, int year) =>
        line.Action != LineAction.T && line.Line.TypeCode is { } code && PaymentTypes.TryGet(code, out var type) && CarriesMinimumFor(type!, person, year);

    /// <summary>The source a standing line stands for: its bank row, its hand line (or its part of a split row, known by
    /// its own line with the row beside it, decision 150), the month's minimum of its person, or the person's payments of
    /// one type added up.</summary>
    public static string? SourceKey(StoredPayment payment) =>
        payment.ReviewLineId is { } line ? "line:" + line.ToString("N")
        : payment.SourceRowId is { } row ? "row:" + row.ToString("N")
        : payment.SourceRef == MinimumSource && payment.PersonId is { } person ? MinimumKey(person)
        : TotalsOf(payment.SourceRef) is { } totals && payment.PersonId is { } owner ? TotalsKey(owner, totals.TypeCode)
        : null;

    /// <summary>Whether a standing line adds up a type (the totals cut) rather than standing for one payment. The
    /// minimum's zero line belongs to both cuts.</summary>
    public static bool IsTotals(StoredPayment payment) => TotalsOf(payment.SourceRef) is not null;

    /// <summary>The source text of a totals line: "totals:&lt;type&gt;:&lt;reference; reference; …&gt;".</summary>
    public static string TotalsRef(string typeCode, IEnumerable<string> references) => $"{TotalsSource}:{typeCode}:{string.Join("; ", references)}";

    /// <summary>How a payment is named in a totals line's source text: by its reference, and a hand line by its own review
    /// line as well ("by hand #&lt;id&gt;"), since every hand line's reference is the same — else a refused hand line would
    /// hold every type with some hand line in it (the fifth audit, item 5).</summary>
    public static string TotalsToken(ReviewPayment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);
        return payment.IsByHand && payment.LineId is { } id ? $"{ReviewWorkspace.ByHandSource} #{id:N}" : payment.Reference;
    }

    /// <summary>Whether a totals line's references add the payment up: by its token; a line written before hand lines had
    /// one (a bare "by hand") holds every hand line of the person, as it did.</summary>
    public static bool AddsUp(IReadOnlyCollection<string> references, ReviewPayment payment)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(payment);
        return references.Contains(TotalsToken(payment), StringComparer.Ordinal)
            || (payment.IsByHand && references.Contains(ReviewWorkspace.ByHandSource, StringComparer.Ordinal));
    }

    /// <summary>A token as the accountant reads it: a hand line's id left out.</summary>
    public static string ShownToken(string token) =>
        token.StartsWith(ReviewWorkspace.ByHandSource + " #", StringComparison.Ordinal) ? ReviewWorkspace.ByHandSource : token;

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

    /// <summary>The types of a person whose totals line on the server adds up the payment — the stored line or, beneath a
    /// draft, the version on the server — named by the payment's reference in its source text. A draft alone, or a line
    /// refused black, holds nothing: nothing of it stands at MTA, and the build would otherwise drop the draft at one run and
    /// draft it anew at the next. A hand line is named by its own token (<see cref="AddsUp"/>).</summary>
    private IEnumerable<string> TypesAddingUp(Guid personId, ReviewPayment payment, Dictionary<string, StoredPayment> bySource)
    {
        var prefix = $"{TotalsSource}:{personId:N}:";
        foreach (var (key, line) in bySource)
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var standing = line.Status == PaymentStatus.Draft ? HistoryStanding(line) : line;
            if (standing is { OnServer: true } s && s.Action != LineAction.T && TotalsOf(s.SourceRef) is { } totals
                && AddsUp(totals.References.Split("; "), payment))
            {
                yield return key[prefix.Length..];
            }
        }
    }

    private static string SourceOf(ReviewPayment payment) =>
        payment.RowId is { } row && !payment.IsPart ? "row:" + row.ToString("N") : "line:" + payment.LineId!.Value.ToString("N");

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
        Notes = build.Notes,
        MinimumShare = build.MinimumShare,
        ExemptionOffered = build.ExemptionOffered,
        ExemptionLeft = build.ExemptionLeft,
        MinimumDue = build.MinimumDue,
        MinimumShortfall = build.MinimumShortfall,
        TopUpKey = build.TopUpKey,
        // every figure of the month goes along: a line retired beside the person's new ones must not silence the log
        WorkAbilityReductionOffered = build.WorkAbilityReductionOffered,
        WorkAbilityReduction = build.WorkAbilityReduction,
        WorkAbilityMonth = build.WorkAbilityMonth,
    };
}
