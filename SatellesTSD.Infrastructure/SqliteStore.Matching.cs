using System.Text.Json;
using Microsoft.Data.Sqlite;
using SatellesTSD.Core.Import;
using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Store;

namespace SatellesTSD.Infrastructure;

/// <summary>The matching half of the store: the owner's match catalog and each period's run.</summary>
public sealed partial class SqliteStore
{
    public string? SavedMatchCatalog() => Scalar<string?>("SELECT json FROM match_catalog WHERE id = 1");

    public void SaveMatchCatalog(string json)
    {
        MatchCatalog.Parse(json); // refuses a document that cannot be used
        Exec("INSERT INTO match_catalog (id, json, updated_at) VALUES (1, $json, $at) ON CONFLICT(id) DO UPDATE SET json = $json, updated_at = $at", ("$json", json), ("$at", T(Now())));
    }

    public void ClearMatchCatalog() => Exec("DELETE FROM match_catalog");

    public StoredMatchRun SaveMatchRun(Guid periodId, Guid? torSheetId, MatchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        using var tx = Tx();
        var period = RequirePeriod(periodId);
        if (period.State != PeriodState.Draft)
        {
            throw new InvalidOperationException($"The period is {period.State}; its run stays as its record.");
        }

        if (period.Year != result.Year || period.Month != result.Month)
        {
            throw new ArgumentException($"The run is for {result.Year}-{result.Month:00}, the period is {period.Year}-{period.Month:00}.", nameof(result));
        }

        var linkedSheets = PeriodImports(periodId).SelectMany(f => Sheets(f.Id)).ToDictionary(s => s.Id);
        if (torSheetId is { } tor && (!linkedSheets.TryGetValue(tor, out var torSheet) || torSheet.Kind != SheetKind.Tor))
        {
            throw new InvalidOperationException("The register sheet is not a sheet of the period's files.");
        }

        var entryIds = torSheetId is { } torId
            ? Query(TorEntrySelect + " WHERE tor_entry.sheet_id = $sheet", ReadTorEntry, ("$sheet", G(torId))).ToDictionary(e => e.Entry.RowNo, e => e.Id)
            : new Dictionary<int, Guid>();
        var sourceSheets = new Dictionary<string, (Guid SheetId, Dictionary<int, Guid> Rows)>(StringComparer.Ordinal);
        foreach (var source in result.Sources)
        {
            if (source.SheetId is not { } sheetId)
            {
                throw new ArgumentException($"Source '{source.Name}' names no stored sheet.", nameof(result));
            }

            if (!linkedSheets.TryGetValue(sheetId, out var sheet) || sheet.Kind != SheetKind.Statement)
            {
                throw new InvalidOperationException($"Source '{source.Name}' is not a statement sheet of the period's files.");
            }

            sourceSheets[source.Name] = (sheetId, StatementRows(sheetId).ToDictionary(r => r.Row.RowNo, r => r.Id));
        }

        DeleteRun(periodId);
        var run = new StoredMatchRun(Guid.NewGuid(), periodId, Now(), result.CatalogJson, result.Problems, torSheetId); // the catalog the run was made with, whatever is in force now
        Exec("INSERT INTO match_run (id, period_id, tor_sheet_id, run_at, catalog_json, problems_json) VALUES ($id, $period, $tor, $at, $catalog, $problems)",
            ("$id", G(run.Id)), ("$period", G(periodId)), ("$tor", torSheetId is { } t2 ? G(t2) : null), ("$at", T(run.RunAt)), ("$catalog", run.CatalogJson),
            ("$problems", JsonSerializer.Serialize(result.Problems, LayoutCatalog.CompactJson)));

        var personIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var person in result.Persons)
        {
            var ids = new List<Guid>();
            foreach (var entry in person.Entries)
            {
                if (!entryIds.TryGetValue(entry.RowNo, out var entryId))
                {
                    throw new InvalidOperationException($"{person.Person.FullName}: register row {entry.RowNo} is not in the stored register sheet.");
                }

                ids.Add(entryId);
            }

            var id = Guid.NewGuid();
            personIds[person.Key] = id;
            Exec(
                """
                INSERT INTO match_person (id, run_id, ord, person_key, personal_code, last_name, first_name, active_entry_id, entry_ids_json)
                VALUES ($id, $run, $ord, $key, $code, $last, $first, $active, $entries)
                """,
                ("$id", G(id)), ("$run", G(run.Id)), ("$ord", person.Order), ("$key", person.Key), ("$code", person.Person.PersonalCode), ("$last", person.Person.LastName),
                ("$first", person.Person.FirstName), ("$active", person.ActiveEntry is { } active ? G(entryIds[active.RowNo]) : null),
                ("$entries", JsonSerializer.Serialize(ids.Select(G).ToList(), LayoutCatalog.CompactJson)));
        }

        var sourceIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        for (var i = 0; i < result.Sources.Count; i++)
        {
            var source = result.Sources[i];
            var id = Guid.NewGuid();
            sourceIds[source.Name] = id;
            Exec("INSERT INTO match_source (id, run_id, ord, sheet_id, name, layout_id) VALUES ($id, $run, $ord, $sheet, $name, $layout)",
                ("$id", G(id)), ("$run", G(run.Id)), ("$ord", i), ("$sheet", G(sourceSheets[source.Name].SheetId)), ("$name", source.Name), ("$layout", source.LayoutId));
        }

        for (var i = 0; i < result.Hits.Count; i++)
        {
            var hit = result.Hits[i];
            if (!sourceSheets.TryGetValue(hit.Source, out var sheet) || !sheet.Rows.TryGetValue(hit.Row.RowNo, out var rowId))
            {
                throw new InvalidOperationException($"{hit.Reference} is not a stored row of the run's sources.");
            }

            Exec(
                """
                INSERT INTO match_hit (id, run_id, ord, source_id, row_id, person_id, last_mask, first_mask, mask_used, verdict_kind, category, matched_json, text, in_period, counted, flags_json)
                VALUES ($id, $run, $ord, $source, $row, $person, $last, $first, $mask, $kind, $category, $matched, $text, $inPeriod, $counted, $flags)
                """,
                ("$id", G(Guid.NewGuid())), ("$run", G(run.Id)), ("$ord", i), ("$source", G(sourceIds[hit.Source])), ("$row", G(rowId)),
                ("$person", hit.PersonKey is { } key ? G(personIds[key]) : null), ("$last", hit.LastMask), ("$first", hit.FirstMask), ("$mask", hit.MaskUsed ? 1 : 0),
                ("$kind", hit.Verdict.Kind.ToString()), ("$category", hit.Verdict.Category?.Name), ("$matched", JsonSerializer.Serialize(hit.Verdict.Matched, LayoutCatalog.CompactJson)),
                ("$text", hit.Text), ("$inPeriod", hit.InPeriod ? 1 : 0), ("$counted", hit.Counted ? 1 : 0), ("$flags", JsonSerializer.Serialize(hit.Flags, LayoutCatalog.CompactJson)));
        }

        tx.Commit();
        return run;
    }

    public void ClearMatchRun(Guid periodId)
    {
        using var tx = Tx();
        var period = RequirePeriod(periodId);
        if (period.State != PeriodState.Draft)
        {
            throw new InvalidOperationException($"The period is {period.State}; its run stays as its record.");
        }

        DeleteRun(periodId);
        tx.Commit();
    }

    public StoredMatchRun? MatchRun(Guid periodId) =>
        Query(MatchRunSelect + " WHERE period_id = $period", ReadMatchRun, ("$period", G(periodId))).SingleOrDefault();

    public IReadOnlyList<StoredMatchPerson> MatchPersons(Guid runId) =>
        Query(MatchPersonSelect + " WHERE run_id = $run ORDER BY ord", ReadMatchPerson, ("$run", G(runId)));

    public IReadOnlyList<StoredMatchSource> MatchSources(Guid runId) =>
        Query("SELECT id, run_id, ord, sheet_id, name, layout_id FROM match_source WHERE run_id = $run ORDER BY ord",
            r => new StoredMatchSource(Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), (int)r.GetInt64(2), Guid.Parse(r.GetString(3)), r.GetString(4), r.GetString(5)), ("$run", G(runId)));

    public IReadOnlyList<StoredMatchHit> MatchHits(Guid runId) =>
        Query(MatchHitSelect + " WHERE run_id = $run ORDER BY ord", ReadMatchHit, ("$run", G(runId)));

    public MatchResult? LoadMatchResult(Guid periodId)
    {
        var run = MatchRun(periodId);
        if (run is null)
        {
            return null;
        }

        var period = RequirePeriod(periodId);
        var catalog = run.CatalogJson.Length > 0 ? MatchCatalog.Parse(run.CatalogJson) : MatchCatalog.Default;
        var persons = new List<MatchPerson>();
        var personKeys = new Dictionary<Guid, string>();
        foreach (var stored in MatchPersons(run.Id))
        {
            var entries = stored.EntryIds.Select(id => Query(TorEntrySelect + " WHERE tor_entry.id = $id", ReadTorEntry, ("$id", G(id))).Single().Entry).ToList();
            var active = stored.ActiveEntryId is { } activeId ? Query(TorEntrySelect + " WHERE tor_entry.id = $id", ReadTorEntry, ("$id", G(activeId))).Single().Entry : null;
            persons.Add(new MatchPerson(new SearchPerson(stored.Key, stored.LastName, stored.FirstName, stored.PersonalCode), entries, active, stored.Order));
            personKeys[stored.Id] = stored.Key;
        }

        var sources = new List<MatchSource>();
        var sourceNames = new Dictionary<Guid, string>();
        var rowsById = new Dictionary<Guid, StatementRow>();
        foreach (var stored in MatchSources(run.Id))
        {
            var sheet = Sheet(stored.SheetId) ?? throw new InvalidOperationException($"Sheet {stored.SheetId} of the run is gone.");
            var rows = StatementRows(stored.SheetId);
            foreach (var row in rows)
            {
                rowsById[row.Id] = row.Row;
            }

            sources.Add(new MatchSource(stored.Name, stored.LayoutId, sheet.Layout?.Kind ?? LayoutKind.Bank, rows.Select(r => r.Row).ToList(), stored.SheetId));
            sourceNames[stored.Id] = stored.Name;
        }

        var hits = new List<MatchHit>();
        foreach (var stored in MatchHits(run.Id))
        {
            var category = stored.Category is { } name
                ? catalog.Find(name) ?? throw new InvalidOperationException($"The run's catalog has no category '{name}'; the stored run cannot be read back as it was.")
                : null;
            var verdict = new CategoryVerdict(stored.VerdictKind, category, stored.VerdictKind == VerdictKind.None ? "" : stored.Text, stored.Matched);
            if (stored.VerdictKind is VerdictKind.None && stored.PersonId is not null)
            {
                verdict = CategoryVerdict.None; // a date problem carries its text on the hit, not on the verdict
            }

            hits.Add(new MatchHit(sourceNames[stored.SourceId], rowsById[stored.RowId], stored.PersonId is { } p ? personKeys[p] : null, stored.LastMask, stored.FirstMask, stored.MaskUsed,
                verdict, stored.Text, stored.InPeriod, stored.Counted, stored.Flags));
        }

        return new MatchResult(period.Year, period.Month, persons, sources, hits, run.Problems, run.CatalogJson);
    }

    /// <summary>Whether a run of any period took a sheet of the file as a source or as its register.</summary>
    private long RunsUsing(Guid fileId, Guid? periodId = null) =>
        Scalar<long>(
            """
            SELECT COUNT(*) FROM match_run r
            WHERE ($period IS NULL OR r.period_id = $period)
              AND (r.tor_sheet_id IN (SELECT id FROM import_sheet WHERE file_id = $file)
                   OR EXISTS (SELECT 1 FROM match_source ms JOIN import_sheet s ON s.id = ms.sheet_id WHERE ms.run_id = r.id AND s.file_id = $file))
            """,
            ("$file", G(fileId)), ("$period", periodId is { } p ? G(p) : null));

    private void DeleteRun(Guid periodId)
    {
        Exec("DELETE FROM match_hit WHERE run_id IN (SELECT id FROM match_run WHERE period_id = $period)", ("$period", G(periodId)));
        Exec("DELETE FROM match_source WHERE run_id IN (SELECT id FROM match_run WHERE period_id = $period)", ("$period", G(periodId)));
        Exec("DELETE FROM match_person WHERE run_id IN (SELECT id FROM match_run WHERE period_id = $period)", ("$period", G(periodId)));
        Exec("DELETE FROM match_run WHERE period_id = $period", ("$period", G(periodId)));
    }

    private const string MatchRunSelect = "SELECT id, period_id, run_at, catalog_json, problems_json, tor_sheet_id FROM match_run";

    private static StoredMatchRun ReadMatchRun(SqliteDataReader r) => new(
        Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), Time(r, 2) ?? default, r.GetString(3),
        JsonSerializer.Deserialize<List<MatchProblem>>(r.GetString(4), LayoutCatalog.CompactJson) ?? [], Str(r, 5) is { } tor ? Guid.Parse(tor) : null);

    private const string MatchPersonSelect = "SELECT id, run_id, ord, person_key, personal_code, last_name, first_name, active_entry_id, entry_ids_json FROM match_person";

    private static StoredMatchPerson ReadMatchPerson(SqliteDataReader r) => new(
        Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), (int)r.GetInt64(2), r.GetString(3), Str(r, 4), r.GetString(5), r.GetString(6),
        Str(r, 7) is { } active ? Guid.Parse(active) : null, (JsonSerializer.Deserialize<List<string>>(r.GetString(8), LayoutCatalog.CompactJson) ?? []).Select(Guid.Parse).ToList());

    private const string MatchHitSelect =
        "SELECT id, run_id, ord, source_id, row_id, person_id, last_mask, first_mask, mask_used, verdict_kind, category, matched_json, text, in_period, counted, flags_json FROM match_hit";

    private static StoredMatchHit ReadMatchHit(SqliteDataReader r) => new(
        Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), (int)r.GetInt64(2), Guid.Parse(r.GetString(3)), Guid.Parse(r.GetString(4)),
        Str(r, 5) is { } person ? Guid.Parse(person) : null, r.GetString(6), r.GetString(7), r.GetInt64(8) != 0, Enum.Parse<VerdictKind>(r.GetString(9)), Str(r, 10),
        JsonSerializer.Deserialize<List<string>>(r.GetString(11), LayoutCatalog.CompactJson) ?? [], r.GetString(12), r.GetInt64(13) != 0, r.GetInt64(14) != 0,
        JsonSerializer.Deserialize<List<string>>(r.GetString(15), LayoutCatalog.CompactJson) ?? []);
}
