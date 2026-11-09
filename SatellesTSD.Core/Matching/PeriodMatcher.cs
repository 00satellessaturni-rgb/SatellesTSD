using SatellesTSD.Core.Import;

namespace SatellesTSD.Core.Matching;

/// <summary>The workbook's first pass (mdlTables.GenerateReport) over one company's month: every person of the
/// register is searched for in every source, each hit is checked against the month and categorised, then every row
/// nobody matched is categorised on its own. Pure: the store and the report are elsewhere.</summary>
public sealed class PeriodMatcher
{
    private readonly MatchCatalog _catalog;
    private readonly NameSearch _search;
    private readonly Categorizer _categorizer;

    public PeriodMatcher(MatchCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _search = new NameSearch(catalog);
        _categorizer = new Categorizer(catalog);
    }

    public MatchCatalog Catalog => _catalog;

    /// <summary>What the search says of a source's rows below its last payee row (decision 140): they stay out (the workbook's cut,
    /// 29) but are counted and said, since a fee or the closing balance may lie there and the parser cannot tell the one from
    /// the other; a header row among them is said as such; a source with no payee row at all is said as that. Computed from
    /// the rows, so a month searched before this rule is told the same when opened.</summary>
    public static IReadOnlyList<MatchProblem> RowsBelowLastPayee(MatchSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var lastNamed = source.Rows.LastOrDefault(r => !string.IsNullOrEmpty(r.Counterparty))?.RowNo ?? 0;
        var below = source.Rows.Where(r => r.RowNo > lastNamed && r.SkipReason != SkipReason.Blank).ToList();
        var problems = new List<MatchProblem>();
        foreach (var header in below.Where(r => r.SkipReason == SkipReason.Header))
        {
            problems.Add(new MatchProblem(MatchProblem.HeaderRow, $"{source.Name} [{header.RowNo}]: a header row inside the data, left out.", Source: source.Name, RowNo: header.RowNo));
        }

        var rows = below.Where(r => r.SkipReason != SkipReason.Header).ToList();
        if (rows.Count == 0)
        {
            return problems;
        }

        var span = rows.Count == 1 ? $"row {rows[0].RowNo}" : $"rows {rows[0].RowNo}–{rows[^1].RowNo}";
        problems.Add(lastNamed == 0
            ? new MatchProblem(MatchProblem.RowsBelowLastPayee, $"{source.Name} [{span}]: no row names a payee, so none of its {rows.Count} row(s) were listed — check the layout's payee column.", Source: source.Name, RowNo: rows[0].RowNo)
            : new MatchProblem(MatchProblem.RowsBelowLastPayee, $"{source.Name} [{span}]: {rows.Count} row(s) below the last payee row were not listed — a fee or the closing balance may be among them; see the statement.", Source: source.Name, RowNo: rows[0].RowNo));
        return problems;
    }

    public MatchResult Run(int year, int month, IReadOnlyList<MatchPerson> persons, IReadOnlyList<MatchSource> sources)
    {
        ArgumentNullException.ThrowIfNull(persons);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);
        if (sources.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sources.Count)
        {
            throw new MatchException("Two sources carry the same name; order them with MatchSource.Order first.");
        }

        var problems = new List<MatchProblem>();
        var hits = new List<MatchHit>();
        var searchPersons = persons.Select(p => p.Person).ToList();
        // a payment row already seen in an earlier source — the same fingerprint and the same bank id (or none on both),
        // one for one, so two identical payments inside one export are not each other's copy — is a duplicate: told, left
        // out of the search and of the sums, listed as nobody's row (decision 23: overlapping exports told apart)
        // The originals: per fingerprint, the rows of the earlier sources with the bank id each carries. A later source's
        // row is a copy of the first original of its fingerprint not yet copied by that source whose id does not say
        // otherwise — ids are compared only when both are trustworthy (a run of sixteen or more digits is not: Excel
        // rounds it in a hand-made copy, decision 20). An original is never used up: a third export's copy is a copy too.
        var duplicates = new Dictionary<(string Source, int RowNo), string>();
        var originals = new Dictionary<string, List<(string Reference, string? Id)>>(StringComparer.Ordinal);
        static bool Trusted(string? id) => !string.IsNullOrWhiteSpace(id) && !(id.Length >= 16 && id.All(char.IsDigit));
        static bool Compatible(string? a, string? b) => !Trusted(a) || !Trusted(b) || string.Equals(a!.Trim(), b!.Trim(), StringComparison.Ordinal);
        foreach (var source in sources)
        {
            var own = new List<(string Hash, string Reference, string? Id)>();
            var copied = new HashSet<(string Hash, int Index)>();
            foreach (var row in source.Rows.Where(r => r.IsPayment && r.RowHash.Length > 0))
            {
                var reference = $"{source.Name} [{row.RowNo}]";
                var earlier = originals.GetValueOrDefault(row.RowHash);
                var index = earlier is null ? -1 : earlier.FindIndex(o => !copied.Contains((row.RowHash, earlier.IndexOf(o))) && Compatible(o.Id, row.TransactionId));
                if (index >= 0)
                {
                    copied.Add((row.RowHash, index));
                    duplicates[(source.Name, row.RowNo)] = earlier![index].Reference;
                    problems.Add(new MatchProblem(MatchProblem.DuplicateRow, $"{reference} is the same payment as {earlier[index].Reference}; listed, not counted.", Source: source.Name, RowNo: row.RowNo));
                    continue;
                }

                own.Add((row.RowHash, reference, row.TransactionId));
            }

            foreach (var (hash, reference, id) in own)
            {
                if (!originals.TryGetValue(hash, out var list))
                {
                    list = [];
                    originals[hash] = list;
                }

                list.Add((reference, id));
            }
        }

        var names = sources.ToDictionary(s => s.Name, s => (IReadOnlyList<string?>)s.Rows.Select(r => r.IsPayment && !duplicates.ContainsKey((s.Name, r.RowNo)) ? NameText.Normalize(NameText.Demojibake(r.Counterparty)) : null).ToList(), StringComparer.Ordinal);

        foreach (var source in sources)
        {
            if (_catalog.FiltersFor(source.LayoutId) is null)
            {
                problems.Add(new MatchProblem(MatchProblem.NoFilters, $"{source.Name}: the layout '{source.LayoutId}' has no category filters; nothing is categorised on it.", Source: source.Name));
            }
        }

        // the employee pass: persons in register order, sources in the workbook's order, rows in sheet order
        foreach (var person in persons)
        {
            if (person.Entries.Count > 1)
            {
                problems.Add(new MatchProblem(MatchProblem.SeveralEntries, $"{person.Person.FullName}: the register lists the person {person.Entries.Count} times (rows {string.Join(", ", person.Entries.Select(e => e.RowNo))}).", person.Key));
            }

            if (person.ActiveEntry is null)
            {
                problems.Add(new MatchProblem(MatchProblem.NoEmploymentInMonth, $"{person.Person.FullName}: no employment of the register covers {year}-{month:00}.", person.Key));
            }

            if (person.Person.LastName.Length == 0 || person.Person.FirstName.Length == 0)
            {
                problems.Add(new MatchProblem(MatchProblem.NoName, $"Register row {person.Entries[0].RowNo}: no first or last name; not searched.", person.Key));
                continue;
            }

            var others = searchPersons.Where(p => p.Key != person.Key).ToList();
            foreach (var source in sources)
            {
                var found = _search.Search(person.Person, names[source.Name], others);
                if (found.Ambiguous)
                {
                    problems.Add(new MatchProblem(MatchProblem.AmbiguousMask,
                        $"{person.Person.FullName}: the masks \"{found.LastMask} {found.FirstMask}\" could also mean {found.ConflictsWith!.FullName}; {found.Dropped.Count} row(s) of {source.Name} left unmatched ({string.Join(", ", found.Dropped.Select(i => source.Rows[i].RowNo))}).",
                        person.Key, source.Name));
                }

                for (var i = 0; i < found.Hits.Count; i++)
                {
                    hits.Add(EmployeeHit(year, month, person, source, source.Rows[found.Hits[i]], found.LastMask, found.HitMasks[i], found.MaskUsed));
                }
            }
        }

        // a row two persons found by their exact names: the workbook summed it for both
        foreach (var group in hits.GroupBy(h => (h.Source, h.Row.RowNo)).Where(g => g.Count() > 1))
        {
            var persons2 = string.Join(", ", group.Select(h => persons.First(p => p.Key == h.PersonKey).Person.FullName));
            problems.Add(new MatchProblem(MatchProblem.SharedRow, $"{group.Key.Source} [{group.Key.RowNo}] was found for {persons2}; counted for none of them.", Source: group.Key.Source, RowNo: group.Key.RowNo));
            foreach (var hit in group)
            {
                hits[hits.IndexOf(hit)] = hit with { Counted = false, Flags = [.. hit.Flags, MatchProblem.SharedRow] };
            }
        }

        // the second pass: every row nobody matched, in source order, down to the last row with a name — the workbook
        // read the name column to its last non-empty cell, listing the nameless and the blank rows on the way
        var matchedRows = hits.Select(h => (h.Source, h.Row.RowNo)).ToHashSet();
        foreach (var source in sources)
        {
            var lastNamed = source.Rows.LastOrDefault(r => !string.IsNullOrEmpty(r.Counterparty))?.RowNo ?? 0;
            problems.AddRange(RowsBelowLastPayee(source));
            foreach (var row in source.Rows)
            {
                if (row.RowNo > lastNamed || matchedRows.Contains((source.Name, row.RowNo)))
                {
                    continue;
                }

                if (duplicates.TryGetValue((source.Name, row.RowNo), out var first))
                {
                    hits.Add(new MatchHit(source.Name, row, null, "", "", false, CategoryVerdict.None, $"duplikaat: {first}", InMonth(row, year, month), false, [MatchProblem.DuplicateRow]));
                    continue;
                }

                switch (row.SkipReason)
                {
                    case SkipReason.Header:
                        problems.Add(new MatchProblem(MatchProblem.HeaderRow, $"{source.Name} [{row.RowNo}]: a header row inside the data, left out.", Source: source.Name, RowNo: row.RowNo));
                        continue;
                    case SkipReason.Blank:
                    case SkipReason.NoName:
                        hits.Add(new MatchHit(source.Name, row, null, "", "", false, CategoryVerdict.None, "", InMonth(row, year, month), false, []));
                        continue;
                }

                var verdict = _categorizer.Define(source.LayoutId, row.Description, row.Counterparty, row.Direction, MatchPass.Unmatched);
                var flags = new List<string>();
                if (row.Amount is null)
                {
                    flags.Add(MatchProblem.BadAmount);
                }

                hits.Add(new MatchHit(source.Name, row, null, "", "", false, verdict, verdict.Text, InMonth(row, year, month), verdict.Kind == VerdictKind.Matched && row.Amount is not null, flags));
            }
        }

        foreach (var hit in hits.Where(h => h.Flags.Contains(MatchProblem.BadAmount) && h.Verdict.Kind == VerdictKind.Matched))
        {
            problems.Add(new MatchProblem(MatchProblem.BadAmount, $"{hit.Reference}: the amount could not be read; counted as nothing.", hit.PersonKey, hit.Source, hit.Row.RowNo));
        }

        return new MatchResult(year, month, persons, sources, hits, problems, _catalog.ToJson());
    }

    private MatchHit EmployeeHit(int year, int month, MatchPerson person, MatchSource source, StatementRow row, string lastMask, string firstMask, bool maskUsed)
    {
        var flags = new List<string>();
        if (row.Date is null)
        {
            // the workbook's TransDate was the text before the first space of the date cell
            var shown = (row.DateText ?? "").Split(' ')[0];
            return new MatchHit(source.Name, row, person.Key, lastMask, firstMask, maskUsed, CategoryVerdict.None, $"viga: kehtetu kuupaev [{shown}]", false, false, flags);
        }

        if (!InMonth(row, year, month))
        {
            return new MatchHit(source.Name, row, person.Key, lastMask, firstMask, maskUsed, CategoryVerdict.None, $"kuupaev ei kuulu perioodi [{year}-{month}]", false, false, flags);
        }

        var verdict = _categorizer.Define(source.LayoutId, row.Description, row.Counterparty, row.Direction, MatchPass.Employee);
        if (row.Amount is null)
        {
            flags.Add(MatchProblem.BadAmount);
        }

        return new MatchHit(source.Name, row, person.Key, lastMask, firstMask, maskUsed, verdict, verdict.Text, true, verdict.Kind == VerdictKind.Matched && row.Amount is not null, flags);
    }

    private static bool InMonth(StatementRow row, int year, int month) => row.Date is { } d && d.Year == year && d.Month == month;
}
