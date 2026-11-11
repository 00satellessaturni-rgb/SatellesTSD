using System.Text;
using SatellesTSD.Core.Import;
using SatellesTSD.Core.Lookups;
using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Store;

namespace SatellesTSD.Core.Review;

/// <summary>What importing one file gave: the stored file (new, or the earlier import of the same bytes) linked to
/// the period, or the reason nothing was stored; and what the accountant should hear about its sheets.</summary>
public sealed class ImportOutcome
{
    public required string FileName { get; init; }

    public ImportFile? File { get; init; }

    public ImportKind? Kind { get; init; }

    /// <summary>The same bytes were imported before; the earlier file is the one linked.</summary>
    public bool AlreadyImported { get; init; }

    public bool Linked { get; init; }

    /// <summary>Sheet by sheet: what was not recognised and why, a register of another employer, the sheet's problems.</summary>
    public IReadOnlyList<string> Messages { get; init; } = [];

    public string? Error { get; init; }

    public bool Succeeded => File is not null && Error is null;
}

/// <summary>What a re-read of a stored file left (decision 131): the new file in the old one's place, the decisions carried and
/// dropped, the months whose search was cleared — or why it was refused.</summary>
public sealed class RereadOutcome
{
    public required string FileName { get; init; }

    public ImportFile? File { get; init; }

    public IReadOnlyList<string> Messages { get; init; } = [];

    public int Carried { get; init; }

    public int Dropped { get; init; }

    /// <summary>The draft lines built from the file's rows that went (in any Draft month); the next build makes them again.</summary>
    public int DraftsDeleted { get; init; }

    /// <summary>The months ("2026-10") whose search was cleared because it read the old rows.</summary>
    public IReadOnlyList<string> RunsCleared { get; init; } = [];

    public string? Error { get; init; }

    public bool Succeeded => File is not null && Error is null;
}

/// <summary>The employer a TÖR export names: its registry code and name.</summary>
public sealed record Employer(string RegistryCode, string Name);

/// <summary>The period's pipeline as the window runs it — the workbook's "Run main" in three steps: the files imported
/// and linked (the same bytes once per company; a register of another employer said), the match run, the lookups —
/// each refused once the period is past Draft, the lookups reported person by person.</summary>
public sealed class PeriodPipeline(IStore store, Importer importer, IResidencyLookup? residency = null, IPensionLookup? pension = null, LookupOptions? lookupOptions = null, TimeProvider? clock = null)
{
    public const string NothingRecognised = "Nothing in the file was recognised.";

    private readonly IStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private Importer _importer = importer ?? throw new ArgumentNullException(nameof(importer));

    public bool HasLookups => residency is not null && pension is not null;

    /// <summary>The employer the files name: the one registry code every TÖR sheet among them carries, with its name (the
    /// code itself when the export has no name column). Null when no file holds a TÖR sheet; among several employers
    /// the company shown, else refused.</summary>
    public Employer? EmployerOf(IEnumerable<(string FileName, byte[] Content)> files, string? shownRegistryCode = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        var codes = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (fileName, content) in files)
        {
            ParsedImport parsed;
            try
            {
                parsed = _importer.Read(fileName, content);
            }
            catch (ImportException)
            {
                continue; // the import itself says what is wrong with the file
            }

            foreach (var tor in parsed.Sheets.Select(s => s.Tor).OfType<ParsedTor>())
            {
                foreach (var code in tor.EmployerCodes)
                {
                    var name = tor.Entries.FirstOrDefault(e => string.Equals(e.EmployerCode, code, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(e.EmployerName))?.EmployerName?.Trim();
                    codes[code.Trim()] = codes.GetValueOrDefault(code.Trim()) ?? name;
                }
            }
        }

        if (codes.Count == 0)
        {
            return null;
        }

        // several employers: the company shown among them takes the files (the import then says what the stray rows
        // are, as it always did); none shown among them is a selection to split
        var chosen = codes.Count == 1 ? codes.Keys.Single()
            : shownRegistryCode is { } shown && codes.ContainsKey(shown.Trim()) ? shown.Trim()
            : throw new ImportException($"The TÖR sheets name several employers ({string.Join(", ", codes.Keys)}); import one employer's files at a time.");
        return new Employer(chosen, codes[chosen] ?? chosen);
    }

    /// <summary>The company of a registry code, created from the employer's name when the store has none.</summary>
    public (Company Company, bool Created) EnsureCompany(Employer employer)
    {
        ArgumentNullException.ThrowIfNull(employer);
        return _store.FindCompany(employer.RegistryCode) is { } found ? (found, false) : (_store.AddCompany(employer.RegistryCode, employer.Name), true);
    }

    /// <summary>Reads, stores and links one file. A file that cannot be read, or holds nothing the app knows, is
    /// reported, not stored.</summary>
    public ImportOutcome ImportFile(Guid periodId, string fileName, byte[] content, Encoding? csvEncoding = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);
        var period = FindPeriod(periodId);
        RequireDraft(period);
        var company = _store.Companies().First(c => c.Id == period.CompanyId);
        ParsedImport parsed;
        try
        {
            parsed = _importer.Read(fileName, content, csvEncoding);
        }
        catch (ImportException e)
        {
            return new ImportOutcome { FileName = fileName, Error = e.Message };
        }

        var messages = SheetMessages(parsed, company);
        if (!parsed.HasData)
        {
            return new ImportOutcome { FileName = fileName, Messages = messages, Error = NothingRecognised };
        }

        ImportFile? file = null;
        var already = false;
        try
        {
            _store.InTransaction(() =>
            {
                var existing = _store.FindImport(company.Id, parsed.Sha256);
                if (existing is not null)
                {
                    file = existing;
                    already = true;
                }
                else
                {
                    file = _store.AddImport(company.Id, parsed, content);
                }

                _store.LinkImport(periodId, file.Id);
            });
        }
        catch (Exception e) when (e is ImportException or InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            // the store's refusal is this file's outcome, not the batch's end
            return new ImportOutcome { FileName = fileName, Kind = parsed.Kind, Messages = messages, Error = e.Message };
        }

        return new ImportOutcome { FileName = fileName, File = file, Kind = parsed.Kind, AlreadyImported = already, Linked = true, Messages = messages };
    }

    /// <summary>What the import log says of a reading (decision 135): the reader's own warnings first; then sheet by sheet
    /// what was not recognised and why, a hidden sheet, a register of another employer, the sheet's problems, every
    /// payment-looking row above the header by name, and the other row problems counted by kind.</summary>
    private static List<string> SheetMessages(ParsedImport parsed, Company company)
    {
        var messages = new List<string>();
        foreach (var sheet in parsed.Sheets)
        {
            var label = sheet.Name is { Length: > 0 } name ? $"sheet '{name}'" : $"sheet {sheet.Index + 1}";
            if (!sheet.Recognised)
            {
                messages.Add($"{label}: {sheet.Message}");
                continue;
            }

            if (sheet.Hidden)
            {
                messages.Add($"{label}: the sheet is hidden in the workbook; it was read all the same.");
            }

            if (sheet.Tor is { } tor && TorParser.CheckEmployer(tor, company.RegistryCode) is { } mismatch)
            {
                messages.Add($"{label}: {mismatch.Message}");
            }

            foreach (var problem in sheet.Problems.Where(p => p.RowNo is null))
            {
                messages.Add($"{label}: {problem.Message}");
            }

            // a row above the header that reads as a payment is named one by one — it is the one a hand-made export can hide
            // (decision 135); the other row problems show on their rows in the grid, so they are counted by kind
            foreach (var above in sheet.Problems.Where(p => p.RowNo is not null && p.Code == ImportProblem.AboveHeader))
            {
                messages.Add($"{label}: {above.Message}");
            }

            var rowProblems = sheet.Problems.Where(p => p.RowNo is not null && p.Code != ImportProblem.AboveHeader).ToList();
            if (rowProblems.Count > 0)
            {
                var byKind = rowProblems.GroupBy(p => p.Code, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} {g.Count()}");
                messages.Add($"{label}: {rowProblems.Select(p => p.RowNo).Distinct().Count()} row(s) with a problem ({string.Join(", ", byKind)}).");
            }
        }

        // what the reader had to make do with (a quote never closed in a CSV): the file's own word, before any sheet's; the
        // reader names the file, the log does too, so the name is taken off here
        var prefix = parsed.FileName + ": ";
        messages.InsertRange(0, parsed.Warnings.Select(w => w.StartsWith(prefix, StringComparison.Ordinal) ? w[prefix.Length..] : w));
        return messages;
    }

    /// <summary>Reads a stored file again, with the layouts and the parser in force, and puts the new reading in the old
    /// one's place (decision 131): the file keeps its place among the period's files, the review's decisions follow the
    /// rows read again by fingerprint, and a decision whose row is not read again is dropped and counted. Every month
    /// that lists the file must be a Draft; their runs are cleared (the months are searched again) and their drafts
    /// built from the file's rows go (the next build makes them again). A line on the server from the file refuses.</summary>
    public RereadOutcome RereadFile(Guid periodId, Guid fileId)
    {
        var period = FindPeriod(periodId);
        RequireDraft(period);
        var company = _store.Companies().First(c => c.Id == period.CompanyId);
        var file = _store.PeriodImports(periodId).FirstOrDefault(f => f.Id == fileId)
            ?? throw new KeyNotFoundException("The file is not among the period's files.");
        var content = _store.ImportContent(fileId) ?? throw new KeyNotFoundException($"The bytes of {file.FileName} are not in the store.");
        ParsedImport parsed;
        try
        {
            parsed = _importer.Read(file.FileName, content);
        }
        catch (ImportException e)
        {
            return new RereadOutcome { FileName = file.FileName, Error = e.Message };
        }

        var messages = SheetMessages(parsed, company);
        if (!parsed.HasData)
        {
            return new RereadOutcome { FileName = file.FileName, Messages = messages, Error = NothingRecognised };
        }

        var periods = _store.Periods(period.CompanyId);
        var listing = periods.Where(p => _store.PeriodImports(p.Id).Any(f => f.Id == fileId)).ToList();
        if (listing.Where(p => p.State != PeriodState.Draft).ToList() is { Count: > 0 } locked)
        {
            return new RereadOutcome { FileName = file.FileName, Messages = messages, Error = $"The file is among the files of {string.Join(", ", locked.Select(MonthOf))}, past Draft (exported or declared): its reading stays as their record and is not read again." };
        }

        ImportReplacement? replaced = null;
        var cleared = new List<string>();
        var draftsDeleted = 0;
        try
        {
            _store.InTransaction(() =>
            {
                foreach (var month in listing.Where(m => _store.LoadMatchResult(m.Id) is not null))
                {
                    _store.ClearMatchRun(month.Id); // the search read the old rows: made again over the new ones
                    cleared.Add(MonthOf(month));
                }

                draftsDeleted = DeleteDraftsFrom(fileId, periods, listing.Select(m => m.Id).ToHashSet()); // built from the old rows, in any Draft month; the next build makes them again
                replaced = _store.ReplaceImport(fileId, parsed, content);
            });
        }
        catch (Exception e) when (e is ImportException or InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            return new RereadOutcome { FileName = file.FileName, Messages = messages, Error = e.Message };
        }

        return new RereadOutcome { FileName = file.FileName, File = replaced!.File, Messages = messages, Carried = replaced.Carried, Dropped = replaced.Dropped, DraftsDeleted = draftsDeleted, RunsCleared = cleared };
    }

    /// <summary>Takes a file out of the store (decision 132): out of this period's files (the period's decisions on its rows go,
    /// as with an unlink; the run that took it goes only with <paramref name="clearRun"/>, the accountant's second word), its
    /// drafts in any Draft month go, then the file itself, with the store's own refusals (a line on the server). A file no
    /// period lists any more goes straight out. Refused while another period lists the file: a month past Draft keeps it as
    /// its record for good, a Draft month gives it up first.</summary>
    public void RemoveFile(Guid periodId, Guid fileId, bool clearRun = false)
    {
        var period = FindPeriod(periodId);
        RequireDraft(period);
        var periods = _store.Periods(period.CompanyId);
        var others = periods.Where(p => p.Id != periodId && _store.PeriodImports(p.Id).Any(f => f.Id == fileId)).ToList();
        if (others.Where(p => p.State != PeriodState.Draft).ToList() is { Count: > 0 } locked)
        {
            throw new InvalidOperationException($"The file is among the files of {string.Join(", ", locked.Select(MonthOf))}, past Draft (exported or declared): it stays as their record and cannot be taken out.");
        }

        if (others.Count > 0)
        {
            throw new InvalidOperationException($"{others.Count} other Draft period(s) list this file ({string.Join(", ", others.Select(MonthOf))}); take it out of their files first.");
        }

        var listedHere = _store.PeriodImports(periodId).Any(f => f.Id == fileId);
        _store.InTransaction(() =>
        {
            if (listedHere)
            {
                if (clearRun && _store.LoadMatchResult(periodId) is not null)
                {
                    _store.ClearMatchRun(periodId);
                }

                _store.UnlinkImport(periodId, fileId); // refuses while the run holds the file and the word was not given
            }

            DeleteDraftsFrom(fileId, periods, listedHere ? [periodId] : []); // a file no month lists was read by no run: no totals are its
            _store.RemoveImport(fileId);
        });
    }

    /// <summary>The Draft payments built from the file's rows, in every Draft month of the company (a month that no longer
    /// lists the file may still hold them): deleted, the number told. The next build makes them again.</summary>
    /// <param name="listing">The months whose run read the file. A line of the totals cut adds up a person's rows of the
    /// month and names none of them, so in these months every Draft totals line goes with the file's rows (decision 170).</param>
    private int DeleteDraftsFrom(Guid fileId, IReadOnlyList<Period> periods, IReadOnlyCollection<Guid> listing)
    {
        var rows = _store.Sheets(fileId).SelectMany(s => _store.StatementRows(s.Id)).Select(r => r.Id).ToHashSet();
        var deleted = 0;
        foreach (var month in periods.Where(p => p.State == PeriodState.Draft))
        {
            var totals = rows.Count > 0 && listing.Contains(month.Id);
            foreach (var draft in _store.Payments(month.Id).Where(l => l.Status == PaymentStatus.Draft
                         && (l.SourceRowId is { } row ? rows.Contains(row) : totals && Declare.PeriodDeclarer.IsTotals(l))))
            {
                _store.DeleteDraft(draft.Id);
                deleted++;
            }
        }

        return deleted;
    }

    /// <summary>The statement layouts the owner saved over the built-in catalog (decision 136).</summary>
    public IReadOnlyList<StatementLayout> OwnerLayouts() => _store.Layouts();

    /// <summary>The layouts the files are read by now: the built-in catalog with the owner's over it.</summary>
    public LayoutCatalog Catalog => _importer.Catalog;

    /// <summary>Saves the layouts of a JSON text — one layout, or an array of them — through the store, which refuses a layout
    /// that lacks what a layout needs or whose header row another layout already has (none is saved then), and reads the
    /// files by them from now on (decision 136). The ids saved are returned.</summary>
    public IReadOnlyList<string> ImportLayouts(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var text = json.TrimStart('﻿', ' ', '\t', '\r', '\n');
        List<StatementLayout> layouts;
        try
        {
            layouts = text.StartsWith('[') ? LayoutCatalog.ParseLayouts(text).ToList() : [LayoutCatalog.FromJson(text)];
        }
        catch (System.Text.Json.JsonException e)
        {
            throw new ImportException($"The layouts file is not valid JSON: {e.Message}", e);
        }

        if (layouts.Count == 0 || layouts.Any(l => l is null))
        {
            throw new ImportException(layouts.Count == 0 ? "The layouts file holds no layout." : "The layouts file holds an empty entry (null) among its layouts.");
        }

        var problems = layouts.SelectMany(l => l.Problems().Select(p => $"{l.Id}: {p}")).ToList();
        if (problems.Count > 0)
        {
            throw new ImportException("The layouts cannot be used:\n" + string.Join("\n", problems));
        }

        var repeated = layouts.GroupBy(l => l.Id, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => $"'{g.Key}' {g.Count()} times").ToList();
        if (repeated.Count > 0)
        {
            throw new ImportException($"The layouts file repeats an id ({string.Join(", ", repeated)}): one layout per id.");
        }

        _store.SaveLayouts(layouts); // whole or not at all, in any order
        RefreshImporter();
        return layouts.Select(l => l.Id).ToList();
    }

    /// <summary>Every layout in force as a JSON text (the built-in ones with the owner's over them): what the owner starts a
    /// layout of their own from, the shape the import takes back.</summary>
    public string ExportLayouts() => LayoutCatalog.ToJson(_importer.Catalog.Layouts);

    /// <summary>Forgets a layout the owner saved: a built-in one of the same id comes back; refused when the catalog would then collide.</summary>
    public void DeleteLayout(string layoutId)
    {
        _store.DeleteLayout(layoutId);
        RefreshImporter();
    }

    private void RefreshImporter() => _importer = _importer.WithCatalog(LayoutCatalog.Default.WithOverrides(_store.Layouts()));

    /// <summary>The company's files no period lists any more (unlinked everywhere, or left when a month was cleared).</summary>
    public IReadOnlyList<ImportFile> UnlistedFiles(Guid companyId)
    {
        var listed = _store.Periods(companyId).SelectMany(p => _store.PeriodImports(p.Id)).Select(f => f.Id).ToHashSet();
        return _store.Imports(companyId).Where(f => !listed.Contains(f.Id)).ToList();
    }

    private static string MonthOf(Period period) => $"{period.Year}-{period.Month:00}";

    /// <summary>Runs and stores the period's matching (a re-run replaces the run; the review's decisions stand, keyed by row).</summary>
    public MatchResult RunMatch(Guid periodId)
    {
        RequireDraft(FindPeriod(periodId));
        return new MatchRunner(_store).Run(periodId);
    }

    public void ClearRun(Guid periodId)
    {
        RequireDraft(FindPeriod(periodId));
        _store.ClearMatchRun(periodId);
    }

    /// <summary>Forgets that the period's run used a file (the run must be cleared first; the store refuses otherwise).</summary>
    public void UnlinkFile(Guid periodId, Guid fileId)
    {
        RequireDraft(FindPeriod(periodId));
        _store.UnlinkImport(periodId, fileId);
    }

    /// <summary>The lookups for the period's persons, reported one by one as they finish. Only for a Draft: a locked month's
    /// lookups would write its control date's answer over the facts every other month is built with.</summary>
    public Task<LookupRunReport> RunLookupsAsync(Guid periodId, IProgress<LookupProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        RequireDraft(FindPeriod(periodId), LookupsLocked);
        return Runner().RunAsync(periodId, cancellationToken, progress);
    }

    /// <summary>The lookups for one person, for the day their facts are checked for in the period (their latest
    /// payout of the month, else its last day).</summary>
    /// <param name="afresh">Ask the registers now even when an answer for that day is recorded: the card's own check of
    /// one person (decision 159).</param>
    public Task<PersonLookupOutcome> LookUpAsync(Person person, Period period, CancellationToken cancellationToken = default, bool afresh = false)
    {
        ArgumentNullException.ThrowIfNull(period);
        RequireDraft(FindPeriod(period.Id), LookupsLocked);
        var runner = Runner();
        return runner.LookUpAsync(person, runner.ControlDateFor(person, period), cancellationToken, afresh);
    }

    /// <summary>Gives facts the accountant set by hand back to the register: the answers recorded for the person's day
    /// are taken over the hand's word (asked only where none is recorded), and the facts are the register's again.</summary>
    public Task<PersonLookupOutcome> HandBackAsync(Person person, Period period, IReadOnlyCollection<LookupKind> kinds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(kinds);
        RequireDraft(FindPeriod(period.Id), LookupsLocked);
        var runner = Runner();
        return runner.LookUpAsync(person, runner.ControlDateFor(person, period), cancellationToken, afresh: false, handBack: kinds);
    }

    private LookupRunner Runner() =>
        residency is not null && pension is not null
            ? new LookupRunner(_store, residency, pension, lookupOptions, clock)
            : throw new InvalidOperationException("No lookups are configured; the registers cannot be asked.");

    private const string LookupsLocked = "the registers are asked for a month still being prepared";

    private static void RequireDraft(Period period, string? why = null)
    {
        if (period.State != PeriodState.Draft)
        {
            throw new ReviewException(ReviewWorkspace.PeriodNotDraft, $"The period is {period.State}; {why ?? "its files and its run stay as its record"}.");
        }
    }

    private Period FindPeriod(Guid periodId) =>
        _store.Companies().SelectMany(c => _store.Periods(c.Id)).FirstOrDefault(p => p.Id == periodId)
        ?? throw new KeyNotFoundException($"Period {periodId} is not in the store.");
}
