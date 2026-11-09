using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SatellesTSD.Core.Import;

/// <summary>The headers of the TÖR (töötamise register) export as the workbook's CONFIG lists them.</summary>
public static partial class TorHeaders
{
    public const string TorId = "TÖR ID";
    public const string PersonalCode = "Töötaja isikukood";
    public const string LastName = "Perekonnanimi";
    public const string FirstName = "Eesnimi";
    public const string StartDate = "Töötamise alguse aeg";
    public const string TypeId = "Töötamise liigi ID";
    public const string Type = "Töötamise liik";
    public const string StateId = "Kande oleku ID";
    public const string State = "Kande olek";
    public const string EndDate = "Töötamise lõpu aeg";
    public const string TerminationId = "Lõpetamise aluse ID";
    public const string Termination = "Lõpetamise alus";
    public const string Country = "Riigi kood";
    public const string EmployerCode = "Tööandja registrikood või ID";
    public const string EmployerName = "Tööandja /Tegeliku tööandja nimetus";
    public const string PauseStart = "Peatamise alguse aeg";
    public const string PauseReason = "Peatamise alus";
    public const string PauseEnd = "Peatamise lõpu aeg";

    /// <summary>The 35 headers of the export the workbook knew, in order.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        TorId, PersonalCode, LastName, FirstName, StartDate, TypeId, Type, StateId, State, EndDate, TerminationId, Termination,
        "Leping", "Ameti ID", "Amet", "Aadressi ID", "Aadress", Country, PauseStart, "Peatamise aluse ID", PauseReason, PauseEnd,
        "Otsus alguse aja kohta", "Otsus töö liigi kohta", "Otsus töö lõpetamise aja kohta", "Otsus lõpetamise aluse kohta", EmployerCode, EmployerName,
        "RKA märge", "TKA märge", "Viimase muutmise kuupäev", "Viimase muutja ID", "Viimase muutja nimi", "Tööaja määr", "IMO",
    ];

    /// <summary>The columns the app reads; a sheet without any of them is not a TÖR export.</summary>
    public static readonly IReadOnlyList<string> Required =
    [
        PersonalCode, LastName, FirstName, StartDate, TypeId, Type, StateId, State, EndDate, Country, EmployerName,
    ];

    /// <summary>Read when present.</summary>
    public static readonly IReadOnlyList<string> Optional = [TorId, TerminationId, Termination, EmployerCode];

    /// <summary>How two TÖR headers are compared: composed Unicode, trimmed, inner whitespace collapsed, no spaces
    /// around a slash, case ignored — the register may respace "Tööandja /Tegeliku tööandja nimetus" one day.</summary>
    public static string Key(string? header)
    {
        var cleaned = HeaderText.Clean(header);
        string composed;
        try
        {
            composed = cleaned.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            composed = cleaned; // a broken character in a damaged file; compare it as it is
        }

        return SlashSpacing().Replace(NameText.Normalize(composed), "/").ToUpperInvariant();
    }

    [GeneratedRegex(@"\s*/\s*")]
    private static partial Regex SlashSpacing();
}

/// <summary>Kande oleku ID values the workbook handled.</summary>
public static class TorStates
{
    public const string Valid = "2";
    public const string Paused = "3";
    public const string Ended = "4";
}

/// <summary>One row of the TÖR export: one employment of one person. Several rows per person are normal (an ended
/// contract and a new one, a contract and a board seat); every row is kept.</summary>
/// <param name="Problem">What could not be read on the row (a date, an id), if anything.</param>
/// <param name="PauseStart">"Peatamise alguse aeg": since when a paused employment (kande olek 3) is paused. Read from the
/// row's cells by <see cref="TorPause.Of"/>, so an import stored before the field existed has it too.</param>
public sealed record TorEntry(
    int RowNo,
    string? TorId,
    string? PersonalCode,
    string FirstName,
    string LastName,
    string? Country,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? TypeId,
    string? Type,
    string? StateId,
    string? State,
    string? TerminationId,
    string? Termination,
    string? EmployerCode,
    string? EmployerName,
    IReadOnlyList<string> Cells,
    string? Problem,
    DateOnly? PauseStart = null,
    DateOnly? PauseEnd = null,
    string? PauseReason = null)
{
    /// <summary>Whether the employment covers any day of the month (an open end covers everything after the start).</summary>
    public bool Overlaps(int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        return (StartDate is null || StartDate <= last) && (EndDate is null || EndDate >= first);
    }

    public bool Equals(TorEntry? other) =>
        other is not null && RowNo == other.RowNo && TorId == other.TorId && PersonalCode == other.PersonalCode && FirstName == other.FirstName && LastName == other.LastName
        && Country == other.Country && StartDate == other.StartDate && EndDate == other.EndDate && TypeId == other.TypeId && Type == other.Type && StateId == other.StateId
        && State == other.State && TerminationId == other.TerminationId && Termination == other.Termination && EmployerCode == other.EmployerCode && EmployerName == other.EmployerName
        && Problem == other.Problem && PauseStart == other.PauseStart && PauseEnd == other.PauseEnd && PauseReason == other.PauseReason && Seq.Equal(Cells, other.Cells);

    public override int GetHashCode() => HashCode.Combine(RowNo, TorId, PersonalCode, FirstName, LastName, StartDate, Seq.Hash(Cells));
}

/// <summary>The pause columns of a TÖR row ("Peatamise alguse aeg", "Peatamise lõpu aeg", "Peatamise alus"), read from
/// the row's cell texts by the sheet's header row: the one reading for a fresh import and for a stored one, whose cells
/// and headers are kept. A date that is not understood reads as no date; the pause is only shown, never declared.</summary>
public static class TorPause
{
    public static (DateOnly? Start, DateOnly? End, string? Reason) Of(IReadOnlyList<string> headers, IReadOnlyList<string> cells)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(cells);
        string? At(string header)
        {
            var key = TorHeaders.Key(header);
            for (var i = 0; i < headers.Count && i < cells.Count; i++)
            {
                if (TorHeaders.Key(headers[i]) == key)
                {
                    var text = HeaderText.Clean(cells[i]);
                    return text.Length == 0 ? null : text;
                }
            }

            return null;
        }

        return (DateOf(At(TorHeaders.PauseStart)), DateOf(At(TorHeaders.PauseEnd)), At(TorHeaders.PauseReason));
    }

    private static DateOnly? DateOf(string? text)
    {
        var value = DateText.ParseText(text);
        if (value is null && decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is >= 20000 and < 100000)
        {
            value = DateTime.FromOADate((double)serial); // a typed Excel date that came through as its serial
        }

        return value is null ? null : DateOnly.FromDateTime(value.Value);
    }
}

/// <summary>A sheet recognised as the TÖR export: where its header row is and which column plays which role.</summary>
public sealed record TorDetection(int HeaderRowNo, IReadOnlyList<string> Headers, IReadOnlyDictionary<string, int> Columns, IReadOnlyList<string> MissingOptional, IReadOnlyList<string> Unknown);

public sealed record ParsedTor(int HeaderRowNo, IReadOnlyList<string> Headers, IReadOnlyList<TorEntry> Entries, IReadOnlyList<ImportProblem> Problems)
{
    public IReadOnlyList<string> EmployerNames => Entries.Select(e => e.EmployerName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList()!;

    public IReadOnlyList<string> EmployerCodes => Entries.Select(e => e.EmployerCode).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList()!;
}

/// <summary>Reads the TÖR export. The workbook demanded exactly its 35 headers in row 1; the app asks for the
/// eleven it reads, in any order and any of the first few rows, and tells which are missing otherwise.</summary>
public static class TorParser
{
    public const string EmployerMismatch = "EMPLOYER_MISMATCH";
    public const string SeveralEmployers = "SEVERAL_EMPLOYERS";

    /// <summary>No row of the export carries an employer registry code: the employer checks have nothing to go by (decision 134).</summary>
    public const string NoEmployerCode = "NO_EMPLOYER_CODE";
    public const string NoCode = "NO_CODE";
    public const string NoName = "NO_NAME";
    /// <summary>Columns the register does not write: a copy the workbook made ("TOR kontroll"), or a newer register.</summary>
    public const string ExtraColumns = "EXTRA_COLUMNS";

    /// <summary>The detection, or null with the missing required headers in <paramref name="missing"/> when the
    /// sheet has some TÖR headers but not all; an empty <paramref name="missing"/> means it is not TÖR-like at all.</summary>
    public static TorDetection? Detect(SheetTable sheet, out IReadOnlyList<string> missing, int headerSearchRows = StatementParser.DefaultHeaderSearchRows)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        missing = [];
        var lastRow = Math.Min(sheet.LastRow, headerSearchRows);
        IReadOnlyList<string> bestMissing = [];
        for (var rowNo = 1; rowNo <= lastRow; rowNo++)
        {
            var headers = sheet.TextsOf(rowNo).Select(HeaderText.Clean).ToList();
            var columns = new Dictionary<string, int>(StringComparer.Ordinal);
            var unknown = new List<string>();
            var known = TorHeaders.All.ToDictionary(TorHeaders.Key, h => h, StringComparer.Ordinal);
            for (var i = 0; i < headers.Count; i++)
            {
                if (headers[i].Length == 0)
                {
                    continue;
                }

                if (known.TryGetValue(TorHeaders.Key(headers[i]), out var canonical))
                {
                    columns.TryAdd(canonical, i + 1);
                }
                else
                {
                    unknown.Add(headers[i]);
                }
            }

            var missingHere = TorHeaders.Required.Where(h => !columns.ContainsKey(h)).ToList();
            if (missingHere.Count == 0)
            {
                return new TorDetection(rowNo, headers, columns, TorHeaders.Optional.Where(h => !columns.ContainsKey(h)).ToList(), unknown);
            }

            if (columns.Count > 0 && (bestMissing.Count == 0 || missingHere.Count < bestMissing.Count))
            {
                bestMissing = missingHere;
            }
        }

        missing = bestMissing.Count == TorHeaders.Required.Count ? [] : bestMissing;
        return null;
    }

    public static ParsedTor Parse(SheetTable sheet, TorDetection detection)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(detection);
        var col = detection.Columns;
        var entries = new List<TorEntry>();
        var problems = new List<ImportProblem>();
        var lastRow = sheet.LastRow;
        for (var rowNo = detection.HeaderRowNo + 1; rowNo <= lastRow; rowNo++)
        {
            var cells = sheet.TextsOf(rowNo);
            var code = IdText.Code(CellOf(sheet, rowNo, col, TorHeaders.PersonalCode));
            var firstName = NameText.Normalize(CellOf(sheet, rowNo, col, TorHeaders.FirstName).Text);
            var lastName = NameText.Normalize(CellOf(sheet, rowNo, col, TorHeaders.LastName).Text);
            if (code is null && firstName.Length == 0 && lastName.Length == 0)
            {
                continue; // a blank or summary row
            }

            var problem = new List<string>();
            if (code is null)
            {
                problem.Add("no personal code");
                problems.Add(new ImportProblem(rowNo, NoCode, $"Row {rowNo} ({firstName} {lastName}) has no personal code."));
            }

            if (firstName.Length == 0 || lastName.Length == 0)
            {
                problem.Add("name incomplete");
                problems.Add(new ImportProblem(rowNo, NoName, $"Row {rowNo} ({code}) has no {(firstName.Length == 0 ? "first" : "last")} name."));
            }

            var start = ParseDate(sheet, rowNo, col, TorHeaders.StartDate, problem, problems);
            var end = ParseDate(sheet, rowNo, col, TorHeaders.EndDate, problem, problems);
            var pause = TorPause.Of(detection.Headers, cells);
            entries.Add(new TorEntry(
                rowNo,
                IdText.Code(CellOf(sheet, rowNo, col, TorHeaders.TorId)),
                code,
                firstName,
                lastName,
                Clean(CellOf(sheet, rowNo, col, TorHeaders.Country)),
                start,
                end,
                IdText.IntegerId(CellOf(sheet, rowNo, col, TorHeaders.TypeId)),
                Clean(CellOf(sheet, rowNo, col, TorHeaders.Type)),
                IdText.IntegerId(CellOf(sheet, rowNo, col, TorHeaders.StateId)),
                Clean(CellOf(sheet, rowNo, col, TorHeaders.State)),
                IdText.IntegerId(CellOf(sheet, rowNo, col, TorHeaders.TerminationId)),
                Clean(CellOf(sheet, rowNo, col, TorHeaders.Termination)),
                IdText.Code(CellOf(sheet, rowNo, col, TorHeaders.EmployerCode)),
                Clean(CellOf(sheet, rowNo, col, TorHeaders.EmployerName)),
                cells,
                problem.Count == 0 ? null : string.Join("; ", problem),
                pause.Start,
                pause.End,
                pause.Reason));
        }

        // counted by code; by name only when no row has a code (a row without a code beside coded rows is not another employer)
        var codes = entries.Select(e => e.EmployerCode).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
        var employers = codes.Count > 0 ? codes : entries.Select(e => e.EmployerName).Where(e => !string.IsNullOrWhiteSpace(e)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (employers.Count > 1)
        {
            problems.Add(new ImportProblem(null, SeveralEmployers, "The export names several employers: " + string.Join(", ", employers) + "."));
        }

        // without a code the employer checks (the import's, the search's) have nothing to go by: said, not refused (decision 134)
        var codeless = entries.Count(e => string.IsNullOrWhiteSpace(e.EmployerCode));
        if (codes.Count == 0 && entries.Count > 0)
        {
            problems.Add(new ImportProblem(null, NoEmployerCode, $"The export carries no employer registry code (column '{TorHeaders.EmployerCode}' missing or empty): it cannot be checked against the company, and goes to the company shown."));
        }
        else if (codeless > 0)
        {
            problems.Add(new ImportProblem(null, NoEmployerCode, $"{codeless} row(s) carry no employer registry code: they cannot be checked against the company and are taken as its own."));
        }

        if (detection.Unknown.Count > 0)
        {
            problems.Add(new ImportProblem(null, ExtraColumns,
                "The sheet has columns the register does not write (" + string.Join(", ", detection.Unknown) + "): a copy made by the workbook, or a newer export. Check that it is the register's own file."));
        }

        return new ParsedTor(detection.HeaderRowNo, detection.Headers, entries, problems);
    }

    /// <summary>The export belongs to the company when its employer registry codes (when it has any) are the company's.</summary>
    public static ImportProblem? CheckEmployer(ParsedTor tor, string registryCode)
    {
        ArgumentNullException.ThrowIfNull(tor);
        var foreign = tor.EmployerCodes.Where(c => !string.Equals(c, registryCode.Trim(), StringComparison.Ordinal)).ToList();
        return foreign.Count == 0
            ? null
            : new ImportProblem(null, EmployerMismatch, $"The export is of employer {string.Join(", ", foreign)}, not of {registryCode}.");
    }

    private static Cell CellOf(SheetTable sheet, int rowNo, IReadOnlyDictionary<string, int> columns, string header) =>
        columns.TryGetValue(header, out var colNo) ? sheet.At(rowNo, colNo) : Cell.Empty;

    private static string? Clean(Cell cell) => cell.IsBlank ? null : HeaderText.Clean(cell.Text);

    private static DateOnly? ParseDate(SheetTable sheet, int rowNo, IReadOnlyDictionary<string, int> columns, string header, List<string> problem, List<ImportProblem> problems)
    {
        var cell = CellOf(sheet, rowNo, columns, header);
        if (cell.IsBlank)
        {
            return null;
        }

        var value = DateText.Parse(cell);
        if (value is null)
        {
            problem.Add($"{header} '{cell.Text}' not understood");
            problems.Add(new ImportProblem(rowNo, ImportProblem.BadDate, $"Row {rowNo}: {header} '{cell.Text}' is not understood."));
            return null;
        }

        return DateOnly.FromDateTime(value.Value);
    }
}
