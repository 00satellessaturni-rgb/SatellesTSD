using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SatellesTSD.Core.Import;
using SatellesTSD.Core.Store;

namespace SatellesTSD.Infrastructure;

/// <summary>The import half of the store: files, sheets, rows, the files a period used, the owner's layouts.</summary>
public sealed partial class SqliteStore
{
    public ImportFile? FindImport(Guid companyId, string sha256) =>
        Query(ImportFileSelect + " WHERE company_id = $company AND sha256 = $sha", ReadImportFile, ("$company", G(companyId)), ("$sha", sha256.Trim().ToLowerInvariant())).SingleOrDefault();

    public ImportFile AddImport(Guid companyId, ParsedImport import, byte[] content, string? notes = null)
    {
        ArgumentNullException.ThrowIfNull(import);
        ArgumentNullException.ThrowIfNull(content);
        if (import.Kind is not { } kind)
        {
            throw new ImportException($"{import.FileName} has no recognised sheet; nothing to store.");
        }

        // the hash is the stored bytes' own: a parse that came from other bytes would make the blob a false witness
        var sha256 = Importer.Sha256Of(content);
        if (content.LongLength != import.Size || !string.Equals(sha256, import.Sha256?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"{import.FileName}: the parsed import does not belong to these bytes (size or hash differ).", nameof(content));
        }

        using var tx = Tx();
        var earlier = FindImport(companyId, sha256);
        if (earlier is not null)
        {
            throw new ImportException($"{import.FileName} was already imported on {earlier.ImportedAt:yyyy-MM-dd HH:mm} as {earlier.FileName}.");
        }

        var file = InsertImport(companyId, import, content, notes, sha256);
        tx.Commit();
        return file;
    }

    /// <summary>The file row, its recognised sheets and their rows, within the caller's transaction.</summary>
    private ImportFile InsertImport(Guid companyId, ParsedImport import, byte[] content, string? notes, string sha256, DateTime? importedAt = null)
    {
        var kind = import.Kind!.Value;
        var file = new ImportFile(Guid.NewGuid(), companyId, kind, import.FileName, import.Format, sha256, content.LongLength, importedAt ?? Now(), Clean(notes), import.Encoding);
        Exec(
            """
            INSERT INTO import_file (id, company_id, kind, file_name, format, sha256, size, imported_at, encoding, notes, content)
            VALUES ($id, $company, $kind, $name, $format, $sha, $size, $at, $encoding, $notes, $content)
            """,
            ("$id", G(file.Id)), ("$company", G(companyId)), ("$kind", kind.ToString()), ("$name", file.FileName), ("$format", file.Format.ToString()),
            ("$sha", file.Sha256), ("$size", file.Size), ("$at", T(file.ImportedAt)), ("$encoding", file.Encoding), ("$notes", file.Notes), ("$content", content));

        // what the reader had to make do with (a quote never closed) travels with the sheet: a CSV has exactly one
        var readerProblems = import.Warnings.Select(w => new ImportProblem(null, ImportProblem.ReaderWarning, w)).ToList();
        foreach (var sheet in import.Recognised)
        {
            var sheetId = Guid.NewGuid();
            if (sheet.Statement is { } statement)
            {
                InsertSheet(sheetId, file.Id, sheet, SheetKind.Statement, statement.Layout.Id, LayoutCatalog.ToJson(statement.Layout), statement.HeaderRowNo, statement.Headers,
                    statement.Account, statement.DisplayName, statement.Rows.Count, statement.FirstDate, statement.LastDate, [.. statement.Problems, .. readerProblems]);
                foreach (var row in statement.Rows)
                {
                    InsertStatementRow(Guid.NewGuid(), sheetId, row);
                }
            }
            else if (sheet.Tor is { } tor)
            {
                InsertSheet(sheetId, file.Id, sheet, SheetKind.Tor, null, null, tor.HeaderRowNo, tor.Headers, null, "TÖR", tor.Entries.Count, null, null, [.. tor.Problems, .. readerProblems]);
                foreach (var entry in tor.Entries)
                {
                    InsertTorEntry(Guid.NewGuid(), sheetId, companyId, entry);
                }
            }
        }

        return file;
    }

    public ImportReplacement ReplaceImport(Guid fileId, ParsedImport import, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(import);
        ArgumentNullException.ThrowIfNull(content);
        if (import.Kind is null)
        {
            throw new ImportException($"{import.FileName} has no recognised sheet; nothing to store.");
        }

        var sha256 = Importer.Sha256Of(content);
        if (content.LongLength != import.Size || !string.Equals(sha256, import.Sha256?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"{import.FileName}: the parsed import does not belong to these bytes (size or hash differ).", nameof(content));
        }

        using var tx = Tx();
        var old = Query(ImportFileSelect + " WHERE import_file.id = $id", ReadImportFile, ("$id", G(fileId))).SingleOrDefault()
            ?? throw new KeyNotFoundException($"Import {fileId} is not in the store.");
        if (!string.Equals(old.Sha256, sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"{import.FileName}: the bytes are not the stored file's.", nameof(content));
        }

        // the guards of a removal: what stands on the server, or was drafted, or was searched, from the old reading
        var declared = Scalar<long>(
            """
            SELECT COUNT(*) FROM payment p
              JOIN statement_row r ON r.id = p.source_row_id
              JOIN import_sheet s ON s.id = r.sheet_id
            WHERE s.file_id = $file AND p.status <> $draft
            """,
            ("$file", G(fileId)), ("$draft", PaymentStatus.Draft.ToString()));
        if (declared > 0)
        {
            throw new InvalidOperationException($"{declared} declared line(s) came from this file; it stays as their source.");
        }

        var drafts = Scalar<long>(
            "SELECT COUNT(*) FROM payment p JOIN statement_row r ON r.id = p.source_row_id JOIN import_sheet s ON s.id = r.sheet_id WHERE s.file_id = $file",
            ("$file", G(fileId)));
        if (drafts > 0)
        {
            throw new InvalidOperationException($"{drafts} draft line(s) point at this file's rows; they go before the file is read again.");
        }

        var totals = Scalar<long>(
            """
            SELECT COUNT(*) FROM payment p
              JOIN period_import pi ON pi.period_id = p.period_id
            WHERE pi.file_id = $file AND p.source_ref LIKE 'totals:%' AND p.status IN ('Sent', 'Accepted', 'NeedsChange', 'Review')
            """,
            ("$file", G(fileId)));
        if (totals > 0)
        {
            throw new InvalidOperationException($"{totals} declared line(s) on the server add up this file's rows among others; it stays as their source.");
        }

        var sentPeriods = Scalar<long>(
            "SELECT COUNT(*) FROM period_import pi JOIN period p ON p.id = pi.period_id WHERE pi.file_id = $file AND p.state <> $draft",
            ("$file", G(fileId)), ("$draft", PeriodState.Draft.ToString()));
        if (sentPeriods > 0)
        {
            throw new InvalidOperationException($"{sentPeriods} period(s) past Draft list this file among the files of their run; it stays as their record.");
        }

        var runs = RunsUsing(fileId);
        if (runs > 0)
        {
            throw new InvalidOperationException($"{runs} match run(s) took this file; clear them first.");
        }

        // the old rows by sheet and fingerprint (the n-th row of a fingerprint within the sheet), and the decisions on them
        var oldKeys = RowKeys(fileId);
        var decisions = Query(
            """
            SELECT id, row_id FROM review_line WHERE row_id IN (
                SELECT r.id FROM statement_row r JOIN import_sheet s ON s.id = r.sheet_id WHERE s.file_id = $file)
            """,
            r => (LineId: Guid.Parse(r.GetString(0)), RowId: Guid.Parse(r.GetString(1))), ("$file", G(fileId)));

        // the old file steps aside (its hash freed), the new one takes its place in every period's list — the link order kept
        Exec("UPDATE import_file SET sha256 = $gone WHERE id = $id", ("$gone", "replaced:" + G(fileId)), ("$id", G(fileId)));
        var file = InsertImport(old.CompanyId, import, content, old.Notes, sha256, old.ImportedAt); // the day the file came in is its record, not the day of the re-read
        Exec("UPDATE period_import SET file_id = $new WHERE file_id = $old", ("$new", G(file.Id)), ("$old", G(fileId)));

        // the decisions follow their rows; one whose row is not read again goes
        var newRows = RowKeys(file.Id).ToDictionary(k => k.Value, k => k.Key);
        var carried = 0;
        var dropped = 0;
        foreach (var (lineId, rowId) in decisions)
        {
            if (oldKeys.TryGetValue(rowId, out var key) && newRows.TryGetValue(key, out var target))
            {
                Exec("UPDATE review_line SET row_id = $row WHERE id = $id", ("$row", G(target)), ("$id", G(lineId)));
                carried++;
            }
            else
            {
                Exec("DELETE FROM review_line WHERE id = $id", ("$id", G(lineId)));
                dropped++;
            }
        }

        // the old reading goes
        Exec("DELETE FROM statement_row WHERE sheet_id IN (SELECT id FROM import_sheet WHERE file_id = $file)", ("$file", G(fileId)));
        Exec("DELETE FROM tor_entry WHERE sheet_id IN (SELECT id FROM import_sheet WHERE file_id = $file)", ("$file", G(fileId)));
        Exec("DELETE FROM import_sheet WHERE file_id = $file", ("$file", G(fileId)));
        Exec("DELETE FROM import_file WHERE id = $file", ("$file", G(fileId)));
        tx.Commit();
        return new ImportReplacement(file, carried, dropped);
    }

    /// <summary>Every statement row of a file by the key a re-read finds it again under: the sheet's index, the row's
    /// fingerprint, and its ordinal among the sheet's rows of that fingerprint (two identical fees stay two).</summary>
    private Dictionary<Guid, (int Sheet, string Hash, int Ordinal)> RowKeys(Guid fileId)
    {
        var keys = new Dictionary<Guid, (int, string, int)>();
        foreach (var sheet in Sheets(fileId).Where(s => s.Kind == SheetKind.Statement))
        {
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var row in StatementRows(sheet.Id).OrderBy(r => r.Row.RowNo))
            {
                var ordinal = seen.GetValueOrDefault(row.Row.RowHash);
                seen[row.Row.RowHash] = ordinal + 1;
                keys[row.Id] = (sheet.SheetIndex, row.Row.RowHash, ordinal);
            }
        }

        return keys;
    }

    private void InsertSheet(Guid id, Guid fileId, ImportedSheet sheet, SheetKind kind, string? layoutId, string? layoutJson, int headerRowNo, IReadOnlyList<string> headers,
        string? account, string displayName, int rowCount, DateOnly? firstDate, DateOnly? lastDate, IReadOnlyList<ImportProblem> problems) =>
        Exec(
            """
            INSERT INTO import_sheet (id, file_id, sheet_index, sheet_name, kind, layout_id, layout_json, header_row_no, headers_json, account, display_name, row_count, first_date, last_date, problems_json)
            VALUES ($id, $file, $index, $name, $kind, $layout, $layoutJson, $headerRow, $headers, $account, $display, $count, $first, $last, $problems)
            """,
            ("$id", G(id)), ("$file", G(fileId)), ("$index", sheet.Index), ("$name", Clean(sheet.Name)), ("$kind", kind.ToString()), ("$layout", layoutId), ("$layoutJson", layoutJson),
            ("$headerRow", headerRowNo), ("$headers", JsonSerializer.Serialize(headers, LayoutCatalog.CompactJson)), ("$account", Clean(account)), ("$display", displayName),
            ("$count", rowCount), ("$first", D(firstDate)), ("$last", D(lastDate)), ("$problems", JsonSerializer.Serialize(problems, LayoutCatalog.CompactJson)));

    private void InsertStatementRow(Guid id, Guid sheetId, StatementRow row) =>
        Exec(
            """
            INSERT INTO statement_row (id, sheet_id, row_no, date, date_text, account, counterparty, counterparty_code, amount, amount_text, currency, description, reference,
                                       transaction_id, direction, dc_text, skip_reason, problem, row_hash, cells_json)
            VALUES ($id, $sheet, $row, $date, $dateText, $account, $name, $code, $amount, $amountText, $currency, $description, $reference,
                    $tx, $direction, $dc, $skip, $problem, $hash, $cells)
            """,
            ("$id", G(id)), ("$sheet", G(sheetId)), ("$row", row.RowNo), ("$date", D(row.Date)), ("$dateText", row.DateText), ("$account", row.Account), ("$name", row.Counterparty),
            ("$code", row.CounterpartyCode), ("$amount", N(row.Amount)), ("$amountText", row.AmountText), ("$currency", row.Currency), ("$description", row.Description),
            ("$reference", row.Reference), ("$tx", row.TransactionId), ("$direction", row.Direction.ToString()), ("$dc", row.DcText), ("$skip", row.SkipReason), ("$problem", row.Problem),
            ("$hash", row.RowHash), ("$cells", StatementParser.CellsJson(row.Cells)));

    private void InsertTorEntry(Guid id, Guid sheetId, Guid companyId, TorEntry entry) =>
        Exec(
            """
            INSERT INTO tor_entry (id, sheet_id, row_no, company_id, tor_id, personal_code, first_name, last_name, country, start_date, end_date, type_id, type, state_id, state,
                                   termination_id, termination, employer_code, employer_name, problem, cells_json)
            VALUES ($id, $sheet, $row, $company, $tor, $code, $first, $last, $country, $start, $end, $typeId, $type, $stateId, $state,
                    $terminationId, $termination, $employerCode, $employerName, $problem, $cells)
            """,
            ("$id", G(id)), ("$sheet", G(sheetId)), ("$row", entry.RowNo), ("$company", G(companyId)), ("$tor", entry.TorId), ("$code", entry.PersonalCode),
            ("$first", entry.FirstName), ("$last", entry.LastName), ("$country", entry.Country), ("$start", D(entry.StartDate)), ("$end", D(entry.EndDate)),
            ("$typeId", entry.TypeId), ("$type", entry.Type), ("$stateId", entry.StateId), ("$state", entry.State), ("$terminationId", entry.TerminationId), ("$termination", entry.Termination),
            ("$employerCode", entry.EmployerCode), ("$employerName", entry.EmployerName), ("$problem", entry.Problem), ("$cells", StatementParser.CellsJson(entry.Cells)));

    public IReadOnlyList<ImportFile> Imports(Guid companyId) =>
        Query(ImportFileSelect + " WHERE company_id = $company ORDER BY imported_at, import_file.rowid", ReadImportFile, ("$company", G(companyId)));

    public byte[]? ImportContent(Guid fileId) => Scalar<byte[]?>("SELECT content FROM import_file WHERE id = $id", ("$id", G(fileId)));

    public IReadOnlyList<ImportSheet> Sheets(Guid fileId) =>
        Query(ImportSheetSelect + " WHERE file_id = $file ORDER BY sheet_index", ReadImportSheet, ("$file", G(fileId)));

    public ImportSheet? Sheet(Guid sheetId) => Query(ImportSheetSelect + " WHERE id = $id", ReadImportSheet, ("$id", G(sheetId))).SingleOrDefault();

    public IReadOnlyList<StoredStatementRow> StatementRows(Guid sheetId) =>
        Query(StatementRowSelect + " WHERE sheet_id = $sheet ORDER BY row_no", ReadStatementRow, ("$sheet", G(sheetId)));

    public IReadOnlyList<StoredStatementRow> StatementRows(Guid companyId, DateOnly from, DateOnly to) =>
        Query(
            StatementRowSelect + """
             JOIN import_sheet s ON s.id = statement_row.sheet_id
             JOIN import_file f ON f.id = s.file_id
            WHERE f.company_id = $company AND statement_row.skip_reason IS NULL AND statement_row.date >= $from AND statement_row.date <= $to
            ORDER BY statement_row.date, f.imported_at, f.rowid, s.sheet_index, statement_row.row_no
            """,
            ReadStatementRow, ("$company", G(companyId)), ("$from", D(from)), ("$to", D(to)));

    public StoredStatementRow? StatementRow(Guid rowId) =>
        Query(StatementRowSelect + " WHERE statement_row.id = $id", ReadStatementRow, ("$id", G(rowId))).SingleOrDefault();

    public IReadOnlyList<StoredTorEntry> TorEntries(Guid companyId, Guid? fileId = null) =>
        fileId is { } file
            ? Query(TorEntrySelect + " JOIN import_sheet s ON s.id = tor_entry.sheet_id WHERE tor_entry.company_id = $company AND s.file_id = $file ORDER BY s.sheet_index, tor_entry.row_no",
                ReadTorEntry, ("$company", G(companyId)), ("$file", G(file)))
            : Query(TorEntrySelect + " JOIN import_sheet s ON s.id = tor_entry.sheet_id JOIN import_file f ON f.id = s.file_id WHERE tor_entry.company_id = $company ORDER BY f.imported_at, f.rowid, s.sheet_index, tor_entry.row_no",
                ReadTorEntry, ("$company", G(companyId)));

    public void LinkImport(Guid periodId, Guid fileId)
    {
        using var tx = Tx();
        var period = RequirePeriod(periodId);
        var file = Query(ImportFileSelect + " WHERE id = $id", ReadImportFile, ("$id", G(fileId))).SingleOrDefault()
            ?? throw new KeyNotFoundException($"Import {fileId} is not in the store.");
        if (file.CompanyId != period.CompanyId)
        {
            throw new InvalidOperationException("The file belongs to another company than the period.");
        }

        Exec("INSERT OR IGNORE INTO period_import (period_id, file_id) VALUES ($period, $file)", ("$period", G(periodId)), ("$file", G(fileId)));
        tx.Commit();
    }

    public void UnlinkImport(Guid periodId, Guid fileId)
    {
        using var tx = Tx();
        var period = RequirePeriod(periodId);
        if (period.State != PeriodState.Draft)
        {
            throw new InvalidOperationException($"The period is {period.State}; the files of its run stay as its record.");
        }

        if (RunsUsing(fileId, periodId) > 0)
        {
            throw new InvalidOperationException("The period's match run took this file; clear the run (or run again without the file) first.");
        }

        // the period's decisions about this file's rows go with the link: they were about rows the period no longer reads
        Exec(
            """
            DELETE FROM review_line WHERE period_id = $period AND row_id IN (
                SELECT r.id FROM statement_row r JOIN import_sheet s ON s.id = r.sheet_id WHERE s.file_id = $file)
            """,
            ("$period", G(periodId)), ("$file", G(fileId)));
        Exec("DELETE FROM period_import WHERE period_id = $period AND file_id = $file", ("$period", G(periodId)), ("$file", G(fileId)));
        tx.Commit();
    }

    public IReadOnlyList<ImportFile> PeriodImports(Guid periodId) =>
        Query(ImportFileSelect + " JOIN period_import p ON p.file_id = import_file.id WHERE p.period_id = $period ORDER BY p.rowid", ReadImportFile, ("$period", G(periodId)));

    public void RemoveImport(Guid fileId)
    {
        using var tx = Tx();
        var declared = Scalar<long>(
            """
            SELECT COUNT(*) FROM payment p
              JOIN statement_row r ON r.id = p.source_row_id
              JOIN import_sheet s ON s.id = r.sheet_id
            WHERE s.file_id = $file AND p.status <> $draft
            """,
            ("$file", G(fileId)), ("$draft", PaymentStatus.Draft.ToString()));
        if (declared > 0)
        {
            throw new InvalidOperationException($"{declared} declared line(s) came from this file; it stays as their source.");
        }

        var drafts = Scalar<long>(
            "SELECT COUNT(*) FROM payment p JOIN statement_row r ON r.id = p.source_row_id JOIN import_sheet s ON s.id = r.sheet_id WHERE s.file_id = $file",
            ("$file", G(fileId)));
        if (drafts > 0)
        {
            throw new InvalidOperationException($"{drafts} draft line(s) point at this file's rows; they go before the file does.");
        }

        // a totals line names no row, yet the rows it adds up are this file's when a period on the server lists the file
        var totals = Scalar<long>(
            """
            SELECT COUNT(*) FROM payment p
              JOIN period_import pi ON pi.period_id = p.period_id
            WHERE pi.file_id = $file AND p.source_ref LIKE 'totals:%' AND p.status IN ('Sent', 'Accepted', 'NeedsChange', 'Review')
            """,
            ("$file", G(fileId)));
        if (totals > 0)
        {
            throw new InvalidOperationException($"{totals} declared line(s) on the server add up this file's rows among others; it stays as their source.");
        }

        var sentPeriods = Scalar<long>(
            "SELECT COUNT(*) FROM period_import pi JOIN period p ON p.id = pi.period_id WHERE pi.file_id = $file AND p.state <> $draft",
            ("$file", G(fileId)), ("$draft", PeriodState.Draft.ToString()));
        if (sentPeriods > 0)
        {
            throw new InvalidOperationException($"{sentPeriods} period(s) past Draft list this file among the files of their run; it stays as their record.");
        }

        var runs = RunsUsing(fileId);
        if (runs > 0)
        {
            throw new InvalidOperationException($"{runs} match run(s) took this file; clear them (or run again without the file) first.");
        }

        var decisions = DecisionsOn(fileId);
        if (decisions > 0)
        {
            throw new InvalidOperationException($"{decisions} review decision(s) point at this file's rows; unlink the file from its periods first.");
        }

        Exec("DELETE FROM period_import WHERE file_id = $file", ("$file", G(fileId)));
        Exec("DELETE FROM statement_row WHERE sheet_id IN (SELECT id FROM import_sheet WHERE file_id = $file)", ("$file", G(fileId)));
        Exec("DELETE FROM tor_entry WHERE sheet_id IN (SELECT id FROM import_sheet WHERE file_id = $file)", ("$file", G(fileId)));
        Exec("DELETE FROM import_sheet WHERE file_id = $file", ("$file", G(fileId)));
        if (Exec("DELETE FROM import_file WHERE id = $file", ("$file", G(fileId))) == 0)
        {
            throw new KeyNotFoundException($"Import {fileId} is not in the store.");
        }

        tx.Commit();
    }

    public IReadOnlyList<StatementLayout> Layouts() =>
        Query("SELECT json FROM statement_layout ORDER BY id", r => LayoutCatalog.FromJson(r.GetString(0)));

    public void SaveLayout(StatementLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var problems = layout.Problems();
        if (problems.Count > 0)
        {
            throw new ImportException("The layout cannot be saved:\n" + string.Join("\n", problems));
        }

        using var tx = Tx();
        try
        {
            // the catalog it will be merged into must stay usable: a header row another layout already has would break every later import
            LayoutCatalog.Default.WithOverrides(Layouts().Where(l => l.Id != layout.Id).Append(layout));
        }
        catch (ImportException e)
        {
            throw new ImportException("The layout cannot be saved:\n" + e.Message, e);
        }

        Exec("INSERT INTO statement_layout (id, json, updated_at) VALUES ($id, $json, $at) ON CONFLICT(id) DO UPDATE SET json = $json, updated_at = $at",
            ("$id", layout.Id), ("$json", LayoutCatalog.ToJson(layout)), ("$at", T(Now())));
        tx.Commit();
    }

    public void SaveLayouts(IReadOnlyList<StatementLayout> layouts)
    {
        ArgumentNullException.ThrowIfNull(layouts);
        var problems = layouts.SelectMany(l => l.Problems().Select(p => $"{l.Id}: {p}")).ToList();
        if (problems.Count > 0)
        {
            throw new ImportException("The layouts cannot be saved:\n" + string.Join("\n", problems));
        }

        var ids = layouts.Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
        using var tx = Tx();
        try
        {
            // the whole batch over what stays: checked once, so the order within the file does not matter
            LayoutCatalog.Default.WithOverrides(Layouts().Where(l => !ids.Contains(l.Id)).Concat(layouts));
        }
        catch (ImportException e)
        {
            throw new ImportException("The layouts cannot be saved:\n" + e.Message, e);
        }

        var at = T(Now());
        foreach (var layout in layouts)
        {
            Exec("INSERT INTO statement_layout (id, json, updated_at) VALUES ($id, $json, $at) ON CONFLICT(id) DO UPDATE SET json = $json, updated_at = $at",
                ("$id", layout.Id), ("$json", LayoutCatalog.ToJson(layout)), ("$at", at));
        }

        tx.Commit();
    }

    public void DeleteLayout(string layoutId)
    {
        using var tx = Tx();
        try
        {
            // removing an override of a default brings the default's header row back; it must not collide with another saved layout
            LayoutCatalog.Default.WithOverrides(Layouts().Where(l => l.Id != layoutId));
        }
        catch (ImportException e)
        {
            throw new ImportException("The layout cannot be deleted:\n" + e.Message, e);
        }

        Exec("DELETE FROM statement_layout WHERE id = $id", ("$id", layoutId));
        tx.Commit();
    }

    private const string ImportFileSelect =
        "SELECT import_file.id, import_file.company_id, import_file.kind, import_file.file_name, import_file.format, import_file.sha256, import_file.size, import_file.imported_at, import_file.notes, import_file.encoding FROM import_file";

    private static ImportFile ReadImportFile(SqliteDataReader r) => new(
        Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), Enum.Parse<ImportKind>(r.GetString(2)), r.GetString(3), Enum.Parse<SourceFormat>(r.GetString(4)),
        r.GetString(5), r.GetInt64(6), Time(r, 7) ?? default, Str(r, 8), Str(r, 9));

    private const string ImportSheetSelect =
        "SELECT id, file_id, sheet_index, sheet_name, kind, layout_id, layout_json, header_row_no, headers_json, account, display_name, row_count, first_date, last_date, problems_json FROM import_sheet";

    private static ImportSheet ReadImportSheet(SqliteDataReader r) => new(
        Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), (int)r.GetInt64(2), Str(r, 3), Enum.Parse<SheetKind>(r.GetString(4)), Str(r, 5), Str(r, 6),
        (int)r.GetInt64(7), StatementParser.CellsFromJson(r.GetString(8)), Str(r, 9), r.GetString(10), (int)r.GetInt64(11), Date(r, 12), Date(r, 13),
        JsonSerializer.Deserialize<List<ImportProblem>>(r.GetString(14), LayoutCatalog.CompactJson) ?? []);

    private const string StatementRowSelect =
        """
        SELECT statement_row.id, statement_row.sheet_id, statement_row.row_no, statement_row.date, statement_row.date_text, statement_row.account, statement_row.counterparty,
               statement_row.counterparty_code, statement_row.amount, statement_row.amount_text, statement_row.currency, statement_row.description, statement_row.reference,
               statement_row.transaction_id, statement_row.direction, statement_row.dc_text, statement_row.skip_reason, statement_row.problem, statement_row.row_hash, statement_row.cells_json
        FROM statement_row
        """;

    private static StoredStatementRow ReadStatementRow(SqliteDataReader r) => new(
        Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)),
        new StatementRow((int)r.GetInt64(2), Date(r, 3), Str(r, 4), Str(r, 5), Str(r, 6), Str(r, 7), Dec(r, 8), Str(r, 9), Str(r, 10), Str(r, 11), Str(r, 12), Str(r, 13),
            Enum.Parse<Direction>(r.GetString(14)), Str(r, 15), Str(r, 16), Str(r, 17), r.GetString(18), StatementParser.CellsFromJson(r.GetString(19))));

    private const string TorEntrySelect =
        """
        SELECT tor_entry.id, tor_entry.sheet_id, tor_entry.company_id, tor_entry.row_no, tor_entry.tor_id, tor_entry.personal_code, tor_entry.first_name, tor_entry.last_name,
               tor_entry.country, tor_entry.start_date, tor_entry.end_date, tor_entry.type_id, tor_entry.type, tor_entry.state_id, tor_entry.state, tor_entry.termination_id,
               tor_entry.termination, tor_entry.employer_code, tor_entry.employer_name, tor_entry.problem, tor_entry.cells_json,
               (SELECT headers_json FROM import_sheet WHERE import_sheet.id = tor_entry.sheet_id)
        FROM tor_entry
        """;

    /// <summary>The pause dates and reason are not columns of their own: they are read from the stored cells by the
    /// sheet's stored header row, as the parser reads them, so an import stored before they were read has them too.</summary>
    private static StoredTorEntry ReadTorEntry(SqliteDataReader r)
    {
        var cells = StatementParser.CellsFromJson(r.GetString(20));
        var pause = r.IsDBNull(21) ? default : TorPause.Of(StatementParser.CellsFromJson(r.GetString(21)), cells);
        return new(
            Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), Guid.Parse(r.GetString(2)),
            new TorEntry((int)r.GetInt64(3), Str(r, 4), Str(r, 5), r.GetString(6), r.GetString(7), Str(r, 8), Date(r, 9), Date(r, 10), Str(r, 11), Str(r, 12), Str(r, 13), Str(r, 14),
                Str(r, 15), Str(r, 16), Str(r, 17), Str(r, 18), cells, Str(r, 19), pause.Start, pause.End, pause.Reason));
    }
}
