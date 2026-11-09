using SatellesTSD.Core.Import;
using SatellesTSD.Core.Store;

namespace SatellesTSD.Core.Matching;

/// <summary>Runs a period's matching from the store: the register and the statements of the files linked to the
/// period (one register file; the statements sheet by sheet, Table layouts first, then import order), the owner's
/// catalog when saved, else the default; the result is stored for the period and returned.</summary>
public sealed class MatchRunner(IStore store)
{
    private readonly IStore _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>The catalog in force: the owner's saved document, else the embedded default.</summary>
    public MatchCatalog Catalog()
    {
        var saved = _store.SavedMatchCatalog();
        return saved is null ? MatchCatalog.Default : MatchCatalog.Parse(saved);
    }

    /// <summary>The persons and sources a run of the period would see, without running or storing anything, and what the
    /// run says of the register itself: the rows of another employer left out (decision 133).</summary>
    public (IReadOnlyList<MatchPerson> Persons, IReadOnlyList<MatchSource> Sources, Guid? TorSheetId, IReadOnlyList<MatchProblem> Problems) Prepare(Guid periodId)
    {
        var period = FindPeriod(periodId);
        var company = _store.Companies().First(c => c.Id == period.CompanyId);
        var files = _store.PeriodImports(periodId);
        if (files.Count == 0)
        {
            throw new MatchException("No file is linked to the period; import the register and the statements first.");
        }

        var torSheets = new List<ImportSheet>();
        var sources = new List<MatchSource>();
        foreach (var file in files)
        {
            foreach (var sheet in _store.Sheets(file.Id))
            {
                if (sheet.Kind == SheetKind.Tor)
                {
                    torSheets.Add(sheet);
                }
                else if (sheet.Kind == SheetKind.Statement && sheet.Layout is { } layout)
                {
                    sources.Add(new MatchSource(sheet.DisplayName, layout.Id, layout.Kind, _store.StatementRows(sheet.Id).Select(r => r.Row).ToList(), sheet.Id));
                }
            }
        }

        if (torSheets.Count > 1)
        {
            throw new MatchException($"The period's files hold {torSheets.Count} register sheets; a run takes one (unlink the others).");
        }

        var tor = torSheets.SingleOrDefault();
        var all = tor is null ? [] : _store.TorEntries(period.CompanyId, tor.FileId).Where(e => e.SheetId == tor.Id).Select(e => e.Entry).ToList();

        // a shared export may carry another employer's rows: they are not this company's persons — left out, and said once
        // (a row without a code is taken as the company's: the import checks the export's employer by the coded rows)
        var problems = new List<MatchProblem>();
        var strangers = all.Where(e => IsOtherEmployer(e, company)).ToList();
        if (strangers.Count > 0)
        {
            var employers = strangers.GroupBy(e => e.EmployerCode!.Trim(), StringComparer.Ordinal)
                .Select(g => $"{g.Key} {g.Select(e => e.EmployerName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? ""}".Trim()).ToList();
            var rows = strangers.Select(e => e.RowNo).OrderBy(n => n).ToList();
            var rowText = string.Join(", ", rows.Take(10)) + (rows.Count > 10 ? $" and {rows.Count - 10} more" : "");
            problems.Add(new MatchProblem(MatchProblem.OtherEmployer,
                $"Register row{(rows.Count == 1 ? "" : "s")} {rowText} {(rows.Count == 1 ? "is" : "are")} of {(employers.Count == 1 ? "another employer" : "other employers")} ({string.Join("; ", employers)}), not of {company.RegistryCode}: left out, not this company's persons."));
        }

        var entries = all.Except(strangers).ToList();
        return (MatchPerson.FromTor(entries, period.Year, period.Month), MatchSource.Order(sources), tor?.Id, problems);
    }

    /// <summary>Whether a register row is another employer's (decision 133): its employer code written and not the company's.</summary>
    public static bool IsOtherEmployer(TorEntry entry, Company company) =>
        !string.IsNullOrWhiteSpace(entry.EmployerCode) && !string.Equals(entry.EmployerCode.Trim(), company.RegistryCode.Trim(), StringComparison.Ordinal);

    /// <summary>Runs and stores the period's matching.</summary>
    public MatchResult Run(Guid periodId)
    {
        var period = FindPeriod(periodId);
        var (persons, sources, torSheetId, registerProblems) = Prepare(periodId);
        var result = new PeriodMatcher(Catalog()).Run(period.Year, period.Month, persons, sources);
        if (registerProblems.Count > 0)
        {
            result = result with { Problems = [.. registerProblems, .. result.Problems] };
        }

        _store.SaveMatchRun(periodId, torSheetId, result);
        return result;
    }

    private Period FindPeriod(Guid periodId) =>
        _store.Companies().SelectMany(c => _store.Periods(c.Id)).FirstOrDefault(p => p.Id == periodId)
        ?? throw new KeyNotFoundException($"Period {periodId} is not in the store.");
}
