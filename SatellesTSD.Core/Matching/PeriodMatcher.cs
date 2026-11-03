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
        var names = sources.ToDictionary(s => s.Name, s => (IReadOnlyList<string?>)s.Rows.Select(r => r.IsPayment ? NameText.Normalize(NameText.Demojibake(r.Counterparty)) : null).ToList(), StringComparer.Ordinal);

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
            foreach (var row in source.Rows)
            {
                if (row.RowNo > lastNamed || matchedRows.Contains((source.Name, row.RowNo)))
                {
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
