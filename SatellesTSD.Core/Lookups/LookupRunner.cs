using System.Text.Json;
using SatellesTSD.Core.Import;
using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Store;

namespace SatellesTSD.Core.Lookups;

/// <summary>How a run behaves.</summary>
/// <param name="MinInterval">The least time between two requests to the same site — the sites are public forms meant for one query at a time.</param>
/// <param name="Force">Ask again even when an answer for the control date is already recorded.</param>
public sealed record LookupOptions(TimeSpan MinInterval, bool Force = false)
{
    public static readonly LookupOptions Default = new(TimeSpan.FromMilliseconds(500));
}

/// <summary>How far a lookup run is: <see cref="Done"/> of <see cref="Total"/> persons asked, with the outcome of the one
/// just done (none for the first report, which tells the total before any query).</summary>
public sealed record LookupProgress(int Done, int Total, PersonLookupOutcome? Outcome);

/// <summary>What happened to one person in a run.</summary>
public sealed record PersonLookupOutcome(Guid PersonId, string PersonalCode, string Name, IReadOnlyList<string> Steps, IReadOnlyList<string> Problems)
{
    public bool Equals(PersonLookupOutcome? other) =>
        other is not null && PersonId == other.PersonId && PersonalCode == other.PersonalCode && Name == other.Name && Steps.SequenceEqual(other.Steps, StringComparer.Ordinal) && Problems.SequenceEqual(other.Problems, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(PersonId, PersonalCode, Name, Steps.Count, Problems.Count);
}

/// <summary>What a run did: the control date it asked about and one outcome per person.</summary>
public sealed record LookupRunReport(DateOnly ControlDate, IReadOnlyList<PersonLookupOutcome> Persons, IReadOnlyList<string> Problems)
{
    public int Queries => Persons.Sum(p => p.Steps.Count(s => s.StartsWith("asked", StringComparison.Ordinal)));

    public bool Equals(LookupRunReport? other) =>
        other is not null && ControlDate == other.ControlDate && Persons.SequenceEqual(other.Persons) && Problems.SequenceEqual(other.Problems, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(ControlDate, Persons.Count, Problems.Count);
}

/// <summary>The lookups of a period, as the workbook ran them for every register row with useHttp on: residency from
/// EMTA for the period's control date (the last day of the month), then — for residents only — the funded-pension
/// membership and rate from Pensionikeskus. Every answer is recorded; the person's facts are set from it unless the
/// accountant set them by hand; a fact set by hand that disagrees with a fresh answer is said, never changed.</summary>
public sealed class LookupRunner(IStore store, IResidencyLookup residency, IPensionLookup pension, LookupOptions? options = null, TimeProvider? clock = null)
{
    public const string Skipped = "SKIPPED";
    public const string Failed = "FAILED";
    public const string Disagrees = "DISAGREES";
    public const string RateUnknown = "RATE_UNKNOWN";
    public const string Reset = "RESET";

    private static readonly JsonSerializerOptions Json = LayoutCatalog.CompactJson;

    private readonly IStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IResidencyLookup _residency = residency ?? throw new ArgumentNullException(nameof(residency));
    private readonly IPensionLookup _pension = pension ?? throw new ArgumentNullException(nameof(pension));
    private readonly LookupOptions _options = options ?? LookupOptions.Default;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private DateTime _lastRequest = DateTime.MinValue;

    /// <summary>The control date of a period: its last day, as the workbook took it.</summary>
    /// <summary>The latest day a person's facts can be checked for in a period: its last day.</summary>
    public static DateOnly ControlDateOf(Period period) => new DateOnly(period.Year, period.Month, 1).AddMonths(1).AddDays(-1);

    /// <summary>The day a person's facts are checked for: the person's latest payout of the month (the facts at the
    /// payout decide the taxes), never later than the period's last day, and the last day itself when the person had
    /// no dated payout in the month. Never the day the check is made.</summary>
    public static DateOnly ControlDateOf(Period period, IEnumerable<DateOnly> payoutDates)
    {
        ArgumentNullException.ThrowIfNull(payoutDates);
        var last = ControlDateOf(period);
        var first = new DateOnly(period.Year, period.Month, 1);
        return payoutDates.Where(d => d >= first && d <= last).DefaultIfEmpty(last).Max();
    }

    /// <summary>The control date of one person in a period: their payouts of the month as the review stands (decision 138).</summary>
    public DateOnly ControlDateFor(Person person, Period period)
    {
        ArgumentNullException.ThrowIfNull(person);
        ArgumentNullException.ThrowIfNull(period);
        var run = _store.LoadMatchResult(period.Id);
        if (run is null)
        {
            return ControlDateOf(period);
        }

        var persons = _store.Persons(person.CompanyId);
        var paid = PaidDates(period, run, persons, RegisterIds(run, persons));
        return ControlDateOf(period, paid.GetValueOrDefault(person.Id) ?? []);
    }

    /// <summary>The store person each register person of the run stands for, when there is one — without making any.</summary>
    private static Dictionary<string, Guid> RegisterIds(MatchResult run, IReadOnlyList<Person> persons)
    {
        var runKeys = run.Persons.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        var ids = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var taken = new HashSet<Guid>(); // as the review hands them out: one row's person is not a namesake row's too
        foreach (var registerPerson in run.Persons)
        {
            if (Review.RegisterPersons.Find(persons, registerPerson.Person, runKeys, taken) is { } found)
            {
                ids[registerPerson.Key] = found.Id;
                taken.Add(found.Id);
            }
        }

        return ids;
    }

    /// <summary>The days every person was paid in the month, as the review stands (decision 138): a line added by hand for
    /// them; a row the accountant gave them; else the row as the search gave it — the register person it found, or a
    /// payee the row makes a person by decision 108 — unless the accountant gave the row to nobody. The search's own word
    /// alone (the hits) when the stored rows do not line up with it.</summary>
    private Dictionary<Guid, List<DateOnly>> PaidDates(Period period, MatchResult run, IReadOnlyList<Person> persons, IReadOnlyDictionary<string, Guid> registerIds)
    {
        var paid = new Dictionary<Guid, List<DateOnly>>();
        var lines = _store.ReviewLines(period.Id);
        foreach (var line in lines.Where(l => l.IsByHand && l.PersonId is not null && l.Date is { } d && d.Year == period.Year && d.Month == period.Month))
        {
            Add(line.PersonId!.Value, line.Date!.Value);
        }

        var storedRun = _store.MatchRun(period.Id);
        var storedHits = storedRun is null ? [] : _store.MatchHits(storedRun.Id);
        var aligned = storedRun is not null && storedHits.Count == run.Hits.Count;
        var catalog = storedRun is { CatalogJson.Length: > 0 } ? MatchCatalog.Parse(storedRun.CatalogJson) : new MatchRunner(_store).Catalog();
        var overlays = aligned ? lines.Where(l => !l.IsByHand && l.RowId is not null).GroupBy(l => l.RowId!.Value).ToDictionary(g => g.Key, g => g.First()) : [];
        // a row the search found for two persons is nobody's until the accountant names one (SHARED_ROW): the hits of one row
        // are told apart by the stored row, else by the reference
        var rowOf = (int i) => aligned ? storedHits[i].RowId.ToString("N") : run.Hits[i].Reference;
        var hitsPerRow = Enumerable.Range(0, run.Hits.Count).GroupBy(rowOf).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        for (var i = 0; i < run.Hits.Count; i++)
        {
            var hit = run.Hits[i];
            if (!hit.InPeriod || hit.Row.Date is not { } date)
            {
                continue;
            }

            Review.ReviewLine? overlay = null;
            if (aligned)
            {
                overlays.TryGetValue(storedHits[i].RowId, out overlay);
            }

            var shared = hitsPerRow[rowOf(i)] > 1;
            var personId = overlay is { NoPerson: true } ? null
                : overlay?.PersonId
                ?? (shared ? null
                    : hit.PersonKey is { } key && registerIds.TryGetValue(key, out var registered) ? registered
                    : aligned && Review.PayeePersons.IsPayeeRow(hit, overlay, catalog) ? Review.PayeePersons.Find(persons, hit.Row.Counterparty!, hit.Row.CounterpartyCode)?.Id : null);
            if (personId is { } id)
            {
                Add(id, date);
            }
        }

        return paid;

        void Add(Guid id, DateOnly date)
        {
            if (!paid.TryGetValue(id, out var list))
            {
                list = [];
                paid[id] = list;
            }

            list.Add(date);
        }
    }

    /// <summary>"2 %", "4 %", "6 %" — the same in every culture; "unknown" for none.</summary>
    public static string Percent(decimal? rate) => rate is { } r ? (r * 100).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " %" : "unknown";

    /// <summary>Runs the lookups for every person of the period's match run; without a run, for every person of the
    /// company. A register person without a store row gets one, named as the register names them.</summary>
    /// <param name="progress">Told the number of persons to ask before the first query, then each person's outcome as it
    /// comes with the count done, for a window that shows the run going.</param>
    public async Task<LookupRunReport> RunAsync(Guid periodId, CancellationToken cancellationToken = default, IProgress<LookupProgress>? progress = null)
    {
        var period = _store.Companies().SelectMany(c => _store.Periods(c.Id)).FirstOrDefault(p => p.Id == periodId)
            ?? throw new KeyNotFoundException($"Period {periodId} is not in the store.");
        var controlDate = ControlDateOf(period);
        var problems = new List<string>();
        var persons = PersonsOf(period, problems);
        progress?.Report(new LookupProgress(0, persons.Count, null));
        var outcomes = new List<PersonLookupOutcome>();
        foreach (var (person, payoutDate) in persons)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = await LookUpAsync(person, payoutDate, cancellationToken).ConfigureAwait(false);
            outcomes.Add(outcome);
            progress?.Report(new LookupProgress(outcomes.Count, persons.Count, outcome));
        }

        return new LookupRunReport(controlDate, outcomes, problems);
    }

    /// <summary>The lookups for one person.</summary>
    public async Task<PersonLookupOutcome> LookUpAsync(Person person, DateOnly controlDate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(person);
        var steps = new List<string>();
        var problems = new List<string>();
        var code = person.PersonalCode?.Trim() ?? "";
        if (!PersonalCode.LooksValid(code))
        {
            problems.Add($"{Skipped}: no valid personal code; nothing to ask.");
            return new PersonLookupOutcome(person.Id, code, person.FullName, steps, problems);
        }

        // residency first: the pension question is only asked about residents
        var current = Fresh(person);
        var residencyRecord = await RecordedOrAskedAsync(current, LookupKind.Residency, controlDate, steps, cancellationToken).ConfigureAwait(false);
        ResidencyFinding? residency = residencyRecord?.FindingJson is { } rj ? JsonSerializer.Deserialize<ResidencyFinding>(rj, Json) : null;
        if (residency is null && current.ResidencySource != LookupSource.Manual)
        {
            problems.Add($"{Failed}: residency — {residencyRecord?.Failure ?? "no answer"}; the facts stand as they were.");
            return new PersonLookupOutcome(person.Id, code, person.FullName, steps, problems);
        }

        current = Fresh(current);
        bool isResident;
        var derivedGone = false;
        if (current.ResidencySource == LookupSource.Manual)
        {
            // the accountant's word stands; the register's answer, when there is one, is only compared with it
            isResident = current.Residency == Residency.Resident;
            if (residency is null)
            {
                problems.Add($"{Failed}: residency — {residencyRecord?.Failure ?? "no answer"}; the accountant's own {current.Residency} is used.");
            }
            else if (residency.IsResidentOn(controlDate) != isResident)
            {
                problems.Add($"{Disagrees}: the register says {(residency.IsResidentOn(controlDate) ? "resident" : "non-resident")} on {controlDate:dd.MM.yyyy}, the accountant set {current.Residency}; the accountant's word stands.");
            }

            steps.Add("residency set by hand, kept");
        }
        else
        {
            isResident = residency!.IsResidentOn(controlDate);
            // the control date stamps the person once both register facts are of it: here when the pension is not the
            // register's to answer (a non-resident's follows, the accountant's is kept), else when Pensionikeskus has answered —
            // so a pension answer that fails leaves the earlier month's date, and a later build says so
            var pensionAsked = isResident && current.PensionSource != LookupSource.Manual;
            // "not a member" that only ever followed from non-residency goes with the residency, in the same write: a cancel
            // or a failure before Pensionikeskus answers leaves the pension unknown, never the stale inference
            derivedGone = isResident && current.PensionSource == LookupSource.Derived;
            current = current with
            {
                Residency = isResident ? Residency.Resident : Residency.NonResident,
                ResidencyCheckedOn = CheckedOn(residencyRecord!), // the day the answer was given, also when an answer of the month is reused
                ResidencySource = LookupSource.Emta,
                FactsControlDate = pensionAsked ? current.FactsControlDate : controlDate,
                FundedPensionMember = derivedGone ? null : current.FundedPensionMember,
                PensionRate = derivedGone ? null : current.PensionRate,
                PensionSource = derivedGone ? LookupSource.Unknown : current.PensionSource,
            };
            _store.UpdatePerson(current);
            steps.Add($"residency {(isResident ? "resident" : "non-resident")}");
        }

        // the pension question: a non-resident is not a member here (the workbook never asked Pensionikeskus about one)
        if (!isResident)
        {
            var derived = new PensionFinding(PensionStatus.NotJoined, null, null, []);
            var existing = _options.Force ? null : _store.LatestLookup(current.Id, LookupKind.Pension, controlDate);
            var derivedRecord = existing is not null && existing.Source == LookupSource.Derived && existing.PersonalCode == code
                ? existing
                : _store.AddLookup(new LookupRecord(Guid.Empty, current.Id, LookupKind.Pension, LookupSource.Derived, default, controlDate, JsonSerializer.Serialize(derived, Json), null, null, "not asked: a non-resident", code));

            steps.Add("pension not asked: a non-resident");
            ApplyPension(current, derived, LookupSource.Derived, controlDate, CheckedOn(derivedRecord), steps, problems);
            return new PersonLookupOutcome(person.Id, code, person.FullName, steps, problems);
        }

        var pensionRecord = await RecordedOrAskedAsync(current, LookupKind.Pension, controlDate, steps, cancellationToken).ConfigureAwait(false);
        PensionFinding? pensionFinding = pensionRecord?.FindingJson is { } pj ? JsonSerializer.Deserialize<PensionFinding>(pj, Json) : null;
        if (pensionFinding is null)
        {
            problems.Add($"{Failed}: pension — {pensionRecord?.Failure ?? "no answer"}; the facts stand as they were.");
            current = Fresh(current);
            if (current.PensionSource == LookupSource.Derived)
            {
                // "not a member" was only ever inferred from non-residency; a resident's membership is unknown until Pensionikeskus answers
                _store.UpdatePerson(current with { FundedPensionMember = null, PensionRate = null, PensionSource = LookupSource.Unknown });
            }

            if (derivedGone || current.PensionSource == LookupSource.Derived)
            {
                // said only now that the answer did not come: with an answer, the facts are known and nothing was reset for long
                problems.Add($"{Reset}: the pension facts had followed from non-residency; now a resident, they are unknown until Pensionikeskus answers.");
            }

            return new PersonLookupOutcome(person.Id, code, person.FullName, steps, problems);
        }

        ApplyPension(Fresh(current), pensionFinding, LookupSource.Pensionikeskus, controlDate, CheckedOn(pensionRecord!), steps, problems);
        return new PersonLookupOutcome(person.Id, code, person.FullName, steps, problems);
    }

    /// <summary>The day a register's answer was given: the record's own stamp, so a reused answer keeps its day rather than today's.</summary>
    private static DateOnly CheckedOn(LookupRecord record) => DateOnly.FromDateTime(record.CheckedAt);

    private void ApplyPension(Person current, PensionFinding finding, LookupSource source, DateOnly controlDate, DateOnly checkedOn, List<string> steps, List<string> problems)
    {
        var member = finding.MemberOn(controlDate);
        var rate = member ? finding.RateOn(controlDate) : null;
        if (member && rate is null)
        {
            problems.Add($"{RateUnknown}: a member on {controlDate:dd.MM.yyyy} but no rate row covers the day; the rate stays unknown (never 2 % by default).");
        }

        if (current.PensionSource == LookupSource.Manual)
        {
            if (current.FundedPensionMember != member || (member && rate is not null && current.PensionRate != rate))
            {
                var said = source == LookupSource.Derived
                    ? "as a non-resident, not a member (Pensionikeskus not asked)"
                    : $"Pensionikeskus says {(member ? $"member at {Percent(rate)}" : "not a member")} on {controlDate:dd.MM.yyyy}";
                problems.Add($"{Disagrees}: {said}, the accountant set {(current.FundedPensionMember == true ? $"member at {Percent(current.PensionRate)}" : "not a member")}; the accountant's word stands.");
            }

            steps.Add("pension set by hand, kept");
            return;
        }

        _store.UpdatePerson(current with
        {
            FundedPensionMember = member,
            PensionRate = member ? rate : null,
            PensionCheckedOn = checkedOn,
            PensionSource = source,
            FactsControlDate = controlDate,
        });
        steps.Add(member ? $"pension member, rate {Percent(rate)}" : "pension not a member");
    }

    /// <summary>The recorded answer of the register for this control date and this very code, or a fresh one asked and
    /// recorded now (the failure too). A failed answer and an answer of another source (a Derived one) are never reused.</summary>
    private async Task<LookupRecord?> RecordedOrAskedAsync(Person person, LookupKind kind, DateOnly controlDate, List<string> steps, CancellationToken cancellationToken)
    {
        var source = kind == LookupKind.Residency ? LookupSource.Emta : LookupSource.Pensionikeskus;
        var code = person.PersonalCode!.Trim();
        if (!_options.Force && _store.LatestLookup(person.Id, kind, controlDate) is { Succeeded: true } recorded && recorded.Source == source && recorded.PersonalCode == code)
        {
            steps.Add($"{kind.ToString().ToLowerInvariant()} answer of {recorded.CheckedAt:dd.MM.yyyy HH:mm} reused");
            return recorded;
        }

        await PaceAsync(cancellationToken).ConfigureAwait(false);
        steps.Add($"asked {kind.ToString().ToLowerInvariant()}");
        LookupRecord record;
        if (kind == LookupKind.Residency)
        {
            var result = await _residency.QueryAsync(code, cancellationToken).ConfigureAwait(false);
            record = new LookupRecord(Guid.Empty, person.Id, kind, source, default, controlDate, result.Finding is { } f ? JsonSerializer.Serialize(f, Json) : null, result.Failure, result.Page, null, code);
        }
        else
        {
            var result = await _pension.QueryAsync(code, cancellationToken).ConfigureAwait(false);
            record = new LookupRecord(Guid.Empty, person.Id, kind, source, default, controlDate, result.Finding is { } f ? JsonSerializer.Serialize(f, Json) : null, result.Failure, result.Page, null, code);
        }

        return _store.AddLookup(record);
    }

    private async Task PaceAsync(CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var wait = _lastRequest + _options.MinInterval - now;
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, _clock, cancellationToken).ConfigureAwait(false);
        }

        _lastRequest = _clock.GetUtcNow().UtcDateTime;
    }

    private DateOnly Today() => DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);

    /// <summary>The person as the store has them now: a lookup changes only its own fields of a fresh row.</summary>
    private Person Fresh(Person person) => _store.Persons(person.CompanyId).FirstOrDefault(p => p.Id == person.Id) ?? person;

    /// <summary>The persons a run looks up: those of the period's match run (created in the store from the register
    /// when missing), else every person of the company.</summary>
    /// <summary>The persons to look up, each with the day their facts are checked for (their latest payout of the
    /// month, else the period's last day).</summary>
    private List<(Person Person, DateOnly ControlDate)> PersonsOf(Period period, List<string> problems)
    {
        var persons = new List<(Person, DateOnly)>();
        var run = _store.LoadMatchResult(period.Id);
        if (run is null)
        {
            problems.Add("The period has no match run; every person of the company is looked up.");
            persons.AddRange(_store.Persons(period.CompanyId).Select(p => (p, ControlDateOf(period))));
            return persons;
        }

        var runKeys = run.Persons.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        var known = _store.Persons(period.CompanyId);
        var asked = new List<Person>();
        var taken = new HashSet<Guid>(); // as the review hands them out: one row's person is not a namesake row's too
        foreach (var matchPerson in run.Persons)
        {
            Person ensured;
            if (matchPerson.Person.PersonalCode is null)
            {
                // listed without a code: asked by the code the card gave the store person the row stands for, when there is
                // one; nobody is made here for a codeless row (the review makes them when the month is opened)
                var card = Review.RegisterPersons.Find(known, matchPerson.Person, runKeys, taken);
                if (card is not null)
                {
                    taken.Add(card.Id);
                }

                if (string.IsNullOrWhiteSpace(card?.PersonalCode))
                {
                    problems.Add($"{Skipped}: {matchPerson.Person.FullName} has no personal code in the register nor on the card; nothing to ask.");
                    continue;
                }

                ensured = card;
            }
            else
            {
                ensured = Review.RegisterPersons.Ensure(_store, period.CompanyId, matchPerson, runKeys: runKeys, taken: taken);
                taken.Add(ensured.Id);
            }

            asked.Add(ensured);
        }

        // every person's payouts as the review stands (decision 138): the register's persons, and the persons the review gives
        // rows of the month though the register does not list them (a rent payee, a row given by hand), with a code to ask by
        var stored = _store.Persons(period.CompanyId);
        var paid = PaidDates(period, run, stored, RegisterIds(run, stored));
        persons.AddRange(asked.Select(p => (p, ControlDateOf(period, paid.GetValueOrDefault(p.Id) ?? []))));
        foreach (var other in stored.Where(p => p.PersonalCode is not null && paid.ContainsKey(p.Id) && persons.All(listed => listed.Item1.Id != p.Id)))
        {
            persons.Add((other, ControlDateOf(period, paid[other.Id])));
        }

        return persons;
    }
}
