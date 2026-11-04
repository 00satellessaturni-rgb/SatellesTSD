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
    public static DateOnly ControlDateOf(Period period) => new DateOnly(period.Year, period.Month, 1).AddMonths(1).AddDays(-1);

    /// <summary>"2 %", "4 %", "6 %" — the same in every culture; "unknown" for none.</summary>
    public static string Percent(decimal? rate) => rate is { } r ? (r * 100).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " %" : "unknown";

    /// <summary>Runs the lookups for every person of the period's match run; without a run, for every person of the
    /// company. A register person without a store row gets one, named as the register names them.</summary>
    /// <param name="progress">Told each person's outcome as it comes, for a window that shows the run going.</param>
    public async Task<LookupRunReport> RunAsync(Guid periodId, CancellationToken cancellationToken = default, IProgress<PersonLookupOutcome>? progress = null)
    {
        var period = _store.Companies().SelectMany(c => _store.Periods(c.Id)).FirstOrDefault(p => p.Id == periodId)
            ?? throw new KeyNotFoundException($"Period {periodId} is not in the store.");
        var controlDate = ControlDateOf(period);
        var problems = new List<string>();
        var persons = PersonsOf(period, problems);
        var outcomes = new List<PersonLookupOutcome>();
        foreach (var person in persons)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = await LookUpAsync(person, controlDate, cancellationToken).ConfigureAwait(false);
            outcomes.Add(outcome);
            progress?.Report(outcome);
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
            current = current with { Residency = isResident ? Residency.Resident : Residency.NonResident, ResidencyCheckedOn = Today(), ResidencySource = LookupSource.Emta };
            _store.UpdatePerson(current);
            steps.Add($"residency {(isResident ? "resident" : "non-resident")}");
        }

        // the pension question: a non-resident is not a member here (the workbook never asked Pensionikeskus about one)
        if (!isResident)
        {
            var derived = new PensionFinding(PensionStatus.NotJoined, null, null, []);
            var existing = _options.Force ? null : _store.LatestLookup(current.Id, LookupKind.Pension, controlDate);
            if (existing is null || existing.Source != LookupSource.Derived || existing.PersonalCode != code)
            {
                _store.AddLookup(new LookupRecord(Guid.Empty, current.Id, LookupKind.Pension, LookupSource.Derived, default, controlDate, JsonSerializer.Serialize(derived, Json), null, null, "not asked: a non-resident", code));
            }

            steps.Add("pension not asked: a non-resident");
            ApplyPension(current, derived, LookupSource.Derived, controlDate, steps, problems);
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
                problems.Add($"{Reset}: the pension facts had followed from non-residency; now a resident, they are unknown until Pensionikeskus answers.");
            }

            return new PersonLookupOutcome(person.Id, code, person.FullName, steps, problems);
        }

        ApplyPension(Fresh(current), pensionFinding, LookupSource.Pensionikeskus, controlDate, steps, problems);
        return new PersonLookupOutcome(person.Id, code, person.FullName, steps, problems);
    }

    private void ApplyPension(Person current, PensionFinding finding, LookupSource source, DateOnly controlDate, List<string> steps, List<string> problems)
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
            PensionCheckedOn = Today(),
            PensionSource = source,
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
    private List<Person> PersonsOf(Period period, List<string> problems)
    {
        var persons = new List<Person>();
        var run = _store.LoadMatchResult(period.Id);
        if (run is null)
        {
            problems.Add("The period has no match run; every person of the company is looked up.");
            persons.AddRange(_store.Persons(period.CompanyId));
            return persons;
        }

        foreach (var matchPerson in run.Persons)
        {
            if (matchPerson.Person.PersonalCode is null)
            {
                problems.Add($"{Skipped}: {matchPerson.Person.FullName} has no personal code in the register; nothing to ask.");
                continue;
            }

            persons.Add(Review.RegisterPersons.Ensure(_store, period.CompanyId, matchPerson));
        }

        return persons;
    }
}
