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

    /// <summary>The persons and sources a run of the period would see, without running or storing anything.</summary>
    public (IReadOnlyList<MatchPerson> Persons, IReadOnlyList<MatchSource> Sources, Guid? TorSheetId) Prepare(Guid periodId)
    {
        var period = FindPeriod(periodId);
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
        var entries = tor is null ? [] : _store.TorEntries(period.CompanyId, tor.FileId).Where(e => e.SheetId == tor.Id).Select(e => e.Entry).ToList();
        return (MatchPerson.FromTor(entries, period.Year, period.Month), MatchSource.Order(sources), tor?.Id);
    }

    /// <summary>Runs and stores the period's matching.</summary>
    public MatchResult Run(Guid periodId)
    {
        var period = FindPeriod(periodId);
        var (persons, sources, torSheetId) = Prepare(periodId);
        var result = new PeriodMatcher(Catalog()).Run(period.Year, period.Month, persons, sources);
        _store.SaveMatchRun(periodId, torSheetId, result);
        return result;
    }

    private Period FindPeriod(Guid periodId) =>
        _store.Companies().SelectMany(c => _store.Periods(c.Id)).FirstOrDefault(p => p.Id == periodId)
        ?? throw new KeyNotFoundException($"Period {periodId} is not in the store.");
}
