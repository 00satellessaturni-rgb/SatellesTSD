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

/// <summary>The employer a TÖR export names: its registry code and name.</summary>
public sealed record Employer(string RegistryCode, string Name);

/// <summary>The period's pipeline as the window runs it — the workbook's "Run main" in three steps: the files imported
/// and linked (the same bytes once per company; a register of another employer said), the match run, the lookups —
/// each refused once the period is past Draft, the lookups reported person by person.</summary>
public sealed class PeriodPipeline(IStore store, Importer importer, IResidencyLookup? residency = null, IPensionLookup? pension = null, LookupOptions? lookupOptions = null, TimeProvider? clock = null)
{
    public const string NothingRecognised = "Nothing in the file was recognised.";

    private readonly IStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly Importer _importer = importer ?? throw new ArgumentNullException(nameof(importer));

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

        var messages = new List<string>();
        foreach (var sheet in parsed.Sheets)
        {
            var label = sheet.Name is { Length: > 0 } name ? $"sheet '{name}'" : $"sheet {sheet.Index + 1}";
            if (!sheet.Recognised)
            {
                messages.Add($"{label}: {sheet.Message}");
                continue;
            }

            if (sheet.Tor is { } tor && TorParser.CheckEmployer(tor, company.RegistryCode) is { } mismatch)
            {
                messages.Add($"{label}: {mismatch.Message}");
            }

            foreach (var problem in sheet.Problems.Where(p => p.RowNo is null))
            {
                messages.Add($"{label}: {problem.Message}");
            }

            var rowProblems = sheet.Problems.Count(p => p.RowNo is not null);
            if (rowProblems > 0)
            {
                messages.Add($"{label}: {rowProblems} row(s) with a problem.");
            }
        }

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
    public Task<PersonLookupOutcome> LookUpAsync(Person person, Period period, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(period);
        RequireDraft(FindPeriod(period.Id), LookupsLocked);
        var runner = Runner();
        return runner.LookUpAsync(person, runner.ControlDateFor(person, period), cancellationToken);
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
