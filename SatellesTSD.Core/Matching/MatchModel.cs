using SatellesTSD.Core.Import;

namespace SatellesTSD.Core.Matching;

/// <summary>One statement (or payroll sheet) a run searches: the workbook's "document". Named as the workbook
/// named it — "LHV pank *1757", and " #2", " #3" for a repeated name — and ordered as the workbook ordered them:
/// Table layouts first, then the banks, each in import order (<see cref="Order"/>).</summary>
/// <param name="SheetId">The stored sheet the rows came from, when they came from the store.</param>
public sealed record MatchSource(string Name, string LayoutId, LayoutKind Kind, IReadOnlyList<StatementRow> Rows, Guid? SheetId = null)
{
    /// <summary>The workbook's order and names: a stable sort with Table layouts first, then " #k" on the k-th source
    /// of an equal name (case-insensitively).</summary>
    public static IReadOnlyList<MatchSource> Order(IEnumerable<MatchSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var sorted = sources.Select((s, i) => (Source: s, Index: i)).OrderBy(x => x.Source.Kind == LayoutKind.Table ? 0 : 1).ThenBy(x => x.Index).Select(x => x.Source).ToList();
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var named = new List<MatchSource>(sorted.Count);
        foreach (var source in sorted)
        {
            var k = seen.GetValueOrDefault(source.Name) + 1;
            seen[source.Name] = k;
            named.Add(k == 1 ? source : source with { Name = $"{source.Name} #{k}" });
        }

        return named;
    }

    public bool Equals(MatchSource? other) =>
        other is not null && Name == other.Name && LayoutId == other.LayoutId && Kind == other.Kind && SheetId == other.SheetId && Rows.SequenceEqual(other.Rows);

    public override int GetHashCode() => HashCode.Combine(Name, LayoutId, Kind, SheetId, Rows.Count);
}

/// <summary>One person of the register as the run sees them: one per personal code (the workbook painted a second
/// row of the same code yellow and skipped it; here every row is kept), with the employment that covers the month.</summary>
/// <param name="Entries">The TÖR rows of the person, in register order.</param>
/// <param name="ActiveEntry">The entry covering the month — a valid one (kande olek 2) before a suspended or ended one, the latest start first; null when none covers it.</param>
/// <param name="Order">The person's place in the register (0-based), which orders the report.</param>
public sealed record MatchPerson(SearchPerson Person, IReadOnlyList<TorEntry> Entries, TorEntry? ActiveEntry, int Order)
{
    public string Key => Person.Key;

    /// <summary>The entry whose facts stand for the person: the active one, else the latest.</summary>
    public TorEntry Entry => ActiveEntry ?? Entries.OrderByDescending(e => e.StartDate ?? DateOnly.MinValue).First();

    /// <summary>The entry covering the day, by the month's rule (a valid one first, then the latest start); null when none does.</summary>
    public TorEntry? EntryOn(DateOnly day) => Entries
        .Where(e => (e.StartDate is null || e.StartDate <= day) && (e.EndDate is null || e.EndDate >= day))
        .OrderBy(e => e.StateId == TorStates.Valid ? 0 : 1)
        .ThenByDescending(e => e.StartDate ?? DateOnly.MinValue)
        .FirstOrDefault();

    /// <summary>Groups the register's rows into persons, in order of first appearance: a row with a personal code
    /// joins the person of that code; a row without one joins the earliest person of the same names, coded or not
    /// (the workbook's IsDuplicateEntry compared a codeless row by its names against every earlier row), else is a
    /// person of its own by its names.</summary>
    public static IReadOnlyList<MatchPerson> FromTor(IEnumerable<TorEntry> entries, int year, int month)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var persons = new List<MatchPerson>();
        var byKey = new Dictionary<string, List<TorEntry>>(StringComparer.Ordinal);
        var byNames = new Dictionary<string, string>(StringComparer.Ordinal); // name key → the key of the earliest person with those names
        var order = new List<string>();
        foreach (var entry in entries)
        {
            var nameKey = SearchPerson.NameKey(entry.LastName, entry.FirstName);
            var key = string.IsNullOrWhiteSpace(entry.PersonalCode) && byNames.TryGetValue(nameKey, out var sameNames)
                ? sameNames
                : SearchPerson.KeyOf(entry.PersonalCode, entry.LastName, entry.FirstName);
            if (!byKey.TryGetValue(key, out var list))
            {
                list = [];
                byKey[key] = list;
                order.Add(key);
            }

            byNames.TryAdd(nameKey, key);
            list.Add(entry);
        }

        foreach (var key in order)
        {
            var list = byKey[key];
            var first = list[0];
            var active = list.Where(e => e.Overlaps(year, month))
                .OrderBy(e => e.StateId == TorStates.Valid ? 0 : 1)
                .ThenByDescending(e => e.StartDate ?? DateOnly.MinValue)
                .FirstOrDefault();
            persons.Add(new MatchPerson(SearchPerson.Of(first.PersonalCode, first.LastName, first.FirstName), list, active, persons.Count));
        }

        return persons;
    }

    public bool Equals(MatchPerson? other) =>
        other is not null && Person == other.Person && Entries.SequenceEqual(other.Entries) && Equals(ActiveEntry, other.ActiveEntry) && Order == other.Order;

    public override int GetHashCode() => HashCode.Combine(Person, Entries.Count, ActiveEntry, Order);
}

/// <summary>Something the run wants the accountant to see; never a reason to stop.</summary>
public sealed record MatchProblem(string Code, string Message, string? PersonKey = null, string? Source = null, int? RowNo = null)
{
    /// <summary>A person's masked hits in a source were dropped because the masks could also mean another person.</summary>
    public const string AmbiguousMask = "AMBIGUOUS_MASK";
    /// <summary>Two persons of the register hit the same row by their exact names; it is counted for neither.</summary>
    public const string SharedRow = "SHARED_ROW";
    /// <summary>The source's layout has no category filters: nothing is ever found on it.</summary>
    public const string NoFilters = "NO_FILTERS";
    /// <summary>No employment of the person covers the month.</summary>
    public const string NoEmploymentInMonth = "NO_EMPLOYMENT_IN_MONTH";
    /// <summary>The register lists the person more than once.</summary>
    public const string SeveralEntries = "SEVERAL_ENTRIES";
    /// <summary>A person of the register without a first or last name was not searched.</summary>
    public const string NoName = "NO_NAME";
    /// <summary>A pasted header row inside a statement was left out.</summary>
    public const string HeaderRow = "HEADER_ROW";
    /// <summary>A hit whose amount could not be read counts as nothing.</summary>
    public const string BadAmount = "BAD_AMOUNT";
    /// <summary>A payment row whose fingerprint (and the bank's transaction id, when it has one) already stood in an
    /// earlier source of the run: two exports that overlap, or a bank's file beside its copy. Listed, never searched
    /// for a person, never counted.</summary>
    public const string DuplicateRow = "DUPLICATE_ROW";
    /// <summary>Two persons of the company have a rent payee's names and neither is bound or coded: the accountant names the one.</summary>
    public const string PayeeAmbiguous = "PAYEE_AMBIGUOUS";
    /// <summary>A rent row whose payee already has a line added by hand of the same amount this month: not declared by default, lest the rent go twice.</summary>
    public const string PayeeHandLine = "PAYEE_HAND_LINE";

    /// <summary>The payee row's personal code belongs to another person of the company than the one bound to the payee's names: the code is not taken, the cards want a look.</summary>
    public const string PayeeCodeElsewhere = "PAYEE_CODE_ELSEWHERE";
    /// <summary>The accountant's decision on a row a later file turned into a copy was moved to the original row.</summary>
    public const string DecisionMoved = "DECISION_MOVED";
    /// <summary>The accountant's decision sits on a row that is a copy, and could not be moved (the month is past Draft, or the original has its own).</summary>
    public const string DecisionOnCopy = "DECISION_ON_COPY";
}

/// <summary>One row of the workbook's "Otsingu kontroll": a statement row with the person it was found for (or none),
/// how the name was found, and what the category engine said — the texts verbatim.</summary>
/// <param name="PersonKey">The person the row was found for; null for a row nobody matched (the second pass).</param>
/// <param name="Text">The "Kontroll/Kategooria" text: the date problem, else the verdict's text.</param>
/// <param name="InPeriod">Whether the row's date falls in the run's month (the second pass categorised rows of any month; the flag says which).</param>
/// <param name="Counted">Whether the amount is summed: a Matched verdict, a readable amount, a row not shared with another person.</param>
public sealed record MatchHit(
    string Source,
    StatementRow Row,
    string? PersonKey,
    string LastMask,
    string FirstMask,
    bool MaskUsed,
    CategoryVerdict Verdict,
    string Text,
    bool InPeriod,
    bool Counted,
    IReadOnlyList<string> Flags)
{
    /// <summary>The workbook's "Dokument" cell: "LHV pank *1757 [12]".</summary>
    public string Reference => $"{Source} [{Row.RowNo}]";

    /// <summary>The workbook's "Otsing*" cell.</summary>
    public string FullMask => MaskUsed ? $"{LastMask} {FirstMask}" : "";

    /// <summary>The amount as the sums take it: its absolute value, 0 when unreadable.</summary>
    public decimal Amount => Math.Abs(Row.Amount ?? 0m);

    public bool Equals(MatchHit? other) =>
        other is not null && Source == other.Source && Row == other.Row && PersonKey == other.PersonKey && LastMask == other.LastMask && FirstMask == other.FirstMask
        && MaskUsed == other.MaskUsed && Verdict == other.Verdict && Text == other.Text && InPeriod == other.InPeriod && Counted == other.Counted
        && Flags.SequenceEqual(other.Flags, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Source, Row.RowNo, PersonKey, Text, Counted, Flags.Count);
}

/// <summary>A row of a sub-table of the report: a person (by key) or a raw payee name, with the summed amounts per source.</summary>
public sealed record SubTableRow(string? PersonKey, string Name, IReadOnlyDictionary<string, decimal> BySource)
{
    public decimal Total => BySource.Values.Sum();

    public bool Equals(SubTableRow? other) =>
        other is not null && PersonKey == other.PersonKey && Name == other.Name && BySource.Count == other.BySource.Count
        && BySource.All(kv => other.BySource.TryGetValue(kv.Key, out var v) && v == kv.Value);

    public override int GetHashCode() => HashCode.Combine(PersonKey, Name, BySource.Count);
}

/// <summary>What one run over a company's month gave: the persons in register order, the sources in the workbook's
/// order, every hit (the employee pass in person-then-source-then-row order, then the rows nobody matched in source
/// order — the Otsingu kontroll sheet), the problems worth a look, and the catalog the run was made with (its JSON),
/// so that what is stored and read back is what the run saw. The sums the report shows are derived here.</summary>
public sealed record MatchResult(int Year, int Month, IReadOnlyList<MatchPerson> Persons, IReadOnlyList<MatchSource> Sources, IReadOnlyList<MatchHit> Hits, IReadOnlyList<MatchProblem> Problems, string CatalogJson)
{
    public IReadOnlyList<MatchHit> HitsOf(string personKey) => Hits.Where(h => h.PersonKey == personKey).ToList();

    /// <summary>The rows nobody matched, in source order.</summary>
    public IReadOnlyList<MatchHit> Unmatched => Hits.Where(h => h.PersonKey is null).ToList();

    /// <summary>The person's net salary found on one source — the Vahearuanne cell: the counted salary hits' amounts, 2 dp.</summary>
    public decimal SalarySum(string personKey, string source) =>
        Round(Hits.Where(h => h.PersonKey == personKey && h.Source == source && h.Counted && h.Verdict.Category is { IsSalary: true }).Sum(h => h.Amount));

    /// <summary>"PALK NETO KOKKU": the person's salary over every source.</summary>
    public decimal SalaryTotal(string personKey) => Round(Sources.Sum(s => SalarySum(personKey, s.Name)));

    /// <summary>The sub-table of a category: persons first in register order, then the raw payee names in order of
    /// first appearance, each with its sums per source (only counted hits).</summary>
    public IReadOnlyList<SubTableRow> SubTable(string categoryName)
    {
        var rows = new List<SubTableRow>();
        var hits = Hits.Where(h => h.Counted && h.Verdict.Category is { } c && string.Equals(c.Name, categoryName, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var person in Persons)
        {
            var own = hits.Where(h => h.PersonKey == person.Key).ToList();
            if (own.Count > 0)
            {
                rows.Add(new SubTableRow(person.Key, person.Person.FullName, Sums(own)));
            }
        }

        foreach (var group in hits.Where(h => h.PersonKey is null).GroupBy(h => h.Row.Counterparty ?? "", StringComparer.OrdinalIgnoreCase))
        {
            rows.Add(new SubTableRow(null, group.Key, Sums(group.ToList())));
        }

        return rows;
    }

    private IReadOnlyDictionary<string, decimal> Sums(IReadOnlyList<MatchHit> hits)
    {
        var sums = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var source in Sources)
        {
            var sum = Round(hits.Where(h => h.Source == source.Name).Sum(h => h.Amount));
            if (sum != 0)
            {
                sums[source.Name] = sum;
            }
        }

        return sums;
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.ToEven);

    public bool Equals(MatchResult? other) =>
        other is not null && Year == other.Year && Month == other.Month && Persons.SequenceEqual(other.Persons) && Sources.SequenceEqual(other.Sources)
        && Hits.SequenceEqual(other.Hits) && Problems.SequenceEqual(other.Problems) && CatalogJson == other.CatalogJson;

    public override int GetHashCode() => HashCode.Combine(Year, Month, Persons.Count, Sources.Count, Hits.Count, Problems.Count, CatalogJson);
}
