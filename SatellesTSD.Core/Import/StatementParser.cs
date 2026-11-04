using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SatellesTSD.Core.Import;

/// <summary>Money out or in, as the layout read it.</summary>
public enum Direction
{
    Debit,
    Credit,
    Unknown,
}

/// <summary>Something the parser could not make sense of; the row or sheet is kept, the problem travels with it.</summary>
public sealed record ImportProblem(int? RowNo, string Code, string Message)
{
    public const string BadDate = "BAD_DATE";
    public const string BadAmount = "BAD_AMOUNT";
    public const string NoAmount = "NO_AMOUNT";
    /// <summary>A two-marker layout's direction cell holds neither marker.</summary>
    public const string BadMarker = "BAD_MARKER";
    public const string NoRows = "NO_ROWS";
    /// <summary>A row above the header row that reads as a payment of the layout: not read, since it is not the statement's.</summary>
    public const string AboveHeader = "ABOVE_HEADER";
    /// <summary>What the file reader had to make do with (a quote never closed): kept with the sheet so the stored rows never look complete when they are not.</summary>
    public const string ReaderWarning = "READER_WARNING";
}

/// <summary>Why a sheet row is not a payment row. It is still stored with all its cells (the review copy of the
/// statement needs every row), just never offered as a payment.</summary>
public static class SkipReason
{
    /// <summary>Nothing in the row.</summary>
    public const string Blank = "Blank";
    /// <summary>No counterparty: a balance, total, fee or card line. Read like any other row (date, amount, direction)
    /// and listed for the accountant, never searched for a person by the run.</summary>
    public const string NoName = "NoName";
    /// <summary>A copy of the header row inside the data (exports pasted together).</summary>
    public const string Header = "Header";
}

/// <summary>One row of a statement sheet as the file said it: the parsed payment fields next to every cell as
/// text. Nothing here is a decision — matching and categories come later and live elsewhere.</summary>
/// <param name="RowNo">The 1-based sheet row, header counted — the workbook's "[12]".</param>
/// <param name="Date">The payment date in Estonian time; null when the cell could not be read.</param>
/// <param name="Amount">Signed as the file had it; null when the cell could not be read.</param>
/// <param name="DcText">The raw marker cell, when the layout has one.</param>
/// <param name="TransactionId">The bank's own id of the entry, as the file had it; empty in some exports, and not to be trusted from a number cell of sixteen digits (Excel keeps fifteen).</param>
/// <param name="RowHash">A fingerprint of what the row says (account, date, amount, direction, counterparty, description, payee code — the parsed values, names with Excel's garbling undone), the same whether the row came from the bank's CSV or from an Excel copy of it; for telling overlapping exports apart.</param>
public sealed record StatementRow(
    int RowNo,
    DateOnly? Date,
    string? DateText,
    string? Account,
    string? Counterparty,
    string? CounterpartyCode,
    decimal? Amount,
    string? AmountText,
    string? Currency,
    string? Description,
    string? Reference,
    string? TransactionId,
    Direction Direction,
    string? DcText,
    string? SkipReason,
    string? Problem,
    string RowHash,
    IReadOnlyList<string> Cells)
{
    public bool IsPayment => SkipReason is null;

    public bool Equals(StatementRow? other) =>
        other is not null && RowNo == other.RowNo && Date == other.Date && DateText == other.DateText && Account == other.Account && Counterparty == other.Counterparty
        && CounterpartyCode == other.CounterpartyCode && Amount == other.Amount && AmountText == other.AmountText && Currency == other.Currency && Description == other.Description
        && Reference == other.Reference && TransactionId == other.TransactionId && Direction == other.Direction && DcText == other.DcText && SkipReason == other.SkipReason
        && Problem == other.Problem && RowHash == other.RowHash && Seq.Equal(Cells, other.Cells);

    public override int GetHashCode() => HashCode.Combine(RowNo, Date, Counterparty, Amount, RowHash, Seq.Hash(Cells));
}

/// <summary>A sheet recognised as one layout.</summary>
/// <param name="HeaderRowNo">The 1-based row of the header; 0 when the sheet has none and the data starts at row 1.</param>
public sealed record Detection(StatementLayout Layout, int HeaderRowNo, IReadOnlyList<string> Headers)
{
    public bool Headerless => HeaderRowNo == 0;

    /// <summary>Whether every header is the layout's text to the letter — false when the match went through a
    /// code-page mix-up, which tells the importer that a CSV was decoded with the wrong code page.</summary>
    public bool Exact => Headerless || Headers.Zip(Layout.Headers).All(pair => string.Equals(pair.First, pair.Second, StringComparison.Ordinal));
}

/// <summary>What recognising a sheet gave: the match, or the nearest layout and how the sheet differs from it.</summary>
public sealed record DetectionResult(Detection? Match, string? NearestLayoutId, IReadOnlyList<string> Differences)
{
    public bool Recognised => Match is not null;
}

/// <summary>A statement sheet read through its layout.</summary>
/// <param name="Account">The last non-empty account cell — the workbook named the source "Bank *1234" after it.</param>
/// <param name="ExactHeaders">See <see cref="Detection.Exact"/>.</param>
public sealed record ParsedStatement(
    StatementLayout Layout,
    int HeaderRowNo,
    IReadOnlyList<string> Headers,
    string? Account,
    IReadOnlyList<StatementRow> Rows,
    IReadOnlyList<ImportProblem> Problems,
    bool ExactHeaders = true)
{
    /// <summary>"LHV pank *1234" for a bank export whose account ends in four digits, else the bank's name.</summary>
    public string DisplayName
    {
        get
        {
            if (Layout.Kind == LayoutKind.Bank && Account is { Length: >= 4 } account && account[^4..].All(char.IsAsciiDigit))
            {
                return $"{Layout.Bank} *{account[^4..]}";
            }

            return Layout.Bank;
        }
    }

    public IReadOnlyList<StatementRow> Payments => Rows.Where(r => r.IsPayment).ToList();

    public DateOnly? FirstDate => Rows.Where(r => r.Date is not null).Select(r => r.Date).Min();

    public DateOnly? LastDate => Rows.Where(r => r.Date is not null).Select(r => r.Date).Max();
}

/// <summary>Recognises a sheet by its header row and reads its rows through the layout, the way the workbook did:
/// the header row's width must equal the layout's header count and every header must match in place; the data runs
/// from the next row to the last non-blank row. Unlike the workbook, the header may sit in any of the first few rows,
/// rows without a counterparty are kept (marked), and a bad date or amount is a problem on the row, not a silent 0.</summary>
public static class StatementParser
{
    /// <summary>How far down a sheet the header row is looked for. A bank's export may put a logo, a title and the
    /// account above the table (Paysera's from October 2026 has the header on row 6); thirty rows is far beyond any
    /// such block, and a data row never reads as a layout's header.</summary>
    public const int DefaultHeaderSearchRows = 30;

    public static DetectionResult Detect(SheetTable sheet, LayoutCatalog catalog, int headerSearchRows = DefaultHeaderSearchRows)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(catalog);
        var lastRow = Math.Min(sheet.LastRow, headerSearchRows);
        string? nearestId = null;
        var nearestMatches = 0;
        var nearestDifferenceCount = int.MaxValue;
        IReadOnlyList<string> nearestDifferences = [];

        for (var rowNo = 1; rowNo <= lastRow; rowNo++)
        {
            var width = sheet.LastColumnOf(rowNo);
            if (width == 0)
            {
                continue;
            }

            var headers = new string[width];
            for (var i = 0; i < width; i++)
            {
                headers[i] = HeaderText.Clean(sheet.At(rowNo, i + 1).Text);
            }

            var forms = headers.Select(HeaderText.Forms).ToList();
            foreach (var layout in catalog.Layouts)
            {
                var (differences, matches) = Compare(layout, headers, forms);
                if (differences.Count == 0)
                {
                    return new DetectionResult(new Detection(layout, rowNo, headers), null, []);
                }

                // the nearest layout is the one with the most headers in place; on a tie, the fewest differences
                if (matches > nearestMatches || (matches == nearestMatches && differences.Count < nearestDifferenceCount))
                {
                    nearestMatches = matches;
                    nearestDifferenceCount = differences.Count;
                    nearestId = layout.Id;
                    nearestDifferences = differences.Select(d => $"row {rowNo}: {d}").ToList();
                }
            }
        }

        // no header row anywhere: a layout that allows it is recognised by the shape of the first rows — the first row's
        // account cell must be the bank's (a header row's "Kliendi konto" is not), and one of the first rows must read as a payment
        var candidateRows = Enumerable.Range(1, lastRow).Where(r => sheet.LastColumnOf(r) > 0).Take(3).ToList();
        foreach (var layout in catalog.Layouts.Where(l => l.AllowHeaderless))
        {
            if (candidateRows.Count > 0 && AccountLooksRight(sheet, candidateRows[0], layout) && candidateRows.Any(rowNo => LooksLikeDataRow(sheet, rowNo, layout)))
            {
                return new DetectionResult(new Detection(layout, 0, layout.Headers), null, []);
            }
        }

        return new DetectionResult(null, nearestMatches > 0 ? nearestId : null, nearestMatches > 0 ? nearestDifferences : []);
    }

    /// <summary>A row that reads as a payment row of the layout: no wider than its header row, the account column
    /// as the layout's pattern says (SEB's own IBANs), a date in the date column, an amount in the amount column, a
    /// marker in the marker column.</summary>
    private static bool LooksLikeDataRow(SheetTable sheet, int rowNo, StatementLayout layout)
    {
        var width = sheet.LastColumnOf(rowNo);
        var accountCol = layout.ColumnOf(layout.Roles.Account);
        var dateCol = layout.ColumnOf(layout.Roles.Date);
        var amountCol = layout.ColumnOf(layout.Roles.Amount);
        var dcCol = layout.ColumnOf(layout.Roles.DebitCredit);
        if (width == 0 || width > layout.Headers.Count || width < Math.Max(dateCol, Math.Max(amountCol, dcCol)))
        {
            return false;
        }

        if (!AccountLooksRight(sheet, rowNo, layout))
        {
            return false;
        }

        if (DateText.Parse(sheet.At(rowNo, dateCol), layout.DateFormats) is null)
        {
            return false;
        }

        var amountCell = sheet.At(rowNo, amountCol);
        if (amountCell.Kind == CellKind.Date || (amountCell.Kind != CellKind.Number && AmountText.Parse(amountCell.Text, layout.DecimalSeparator) is null))
        {
            return false;
        }

        return layout.Direction != DirectionRule.Markers || Marker(HeaderText.Clean(sheet.At(rowNo, dcCol).Text), layout) != Direction.Unknown;
    }

    /// <summary>The row's account cell matches the layout's pattern (always true for a layout without one).</summary>
    private static bool AccountLooksRight(SheetTable sheet, int rowNo, StatementLayout layout)
    {
        if (string.IsNullOrEmpty(layout.HeaderlessAccountPattern))
        {
            return true;
        }

        var accountCol = layout.ColumnOf(layout.Roles.Account);
        return accountCol > 0 && Regex.IsMatch(HeaderText.Clean(sheet.At(rowNo, accountCol).Text), layout.HeaderlessAccountPattern);
    }

    /// <summary>How a header row differs from a layout's, and how many headers are in place.</summary>
    private static (List<string> Differences, int Matches) Compare(StatementLayout layout, string[] headers, List<IReadOnlySet<string>> forms)
    {
        var differences = new List<string>();
        if (layout.Headers.Count != headers.Length)
        {
            differences.Add($"{headers.Length} header columns, '{layout.Id}' has {layout.Headers.Count}");
        }

        var common = Math.Min(layout.Headers.Count, headers.Length);
        var matches = 0;
        for (var i = 0; i < common; i++)
        {
            var expected = layout.Headers[i];
            if (string.Equals(expected, headers[i], StringComparison.Ordinal) || forms[i].Overlaps(HeaderText.Forms(expected)) || HeaderText.Match(headers[i], expected))
            {
                matches++;
            }
            else
            {
                differences.Add($"column {i + 1}: '{headers[i]}' is not '{expected}'");
            }
        }

        return (differences, matches);
    }

    public static ParsedStatement Parse(SheetTable sheet, Detection detection)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(detection);
        var layout = detection.Layout;
        var roles = layout.Roles;
        var accountCol = layout.ColumnOf(roles.Account);
        var dateCol = layout.ColumnOf(roles.Date);
        var nameCol = layout.ColumnOf(roles.Counterparty);
        var amountCol = layout.ColumnOf(roles.Amount);
        var descriptionCol = layout.ColumnOf(roles.Description);
        var dcCol = layout.ColumnOf(roles.DebitCredit);
        var currencyCol = layout.ColumnOf(roles.Currency);
        var codeCol = layout.ColumnOf(roles.CounterpartyCode);
        var txCol = layout.ColumnOf(roles.TransactionId);
        var referenceCol = layout.ColumnOf(roles.Reference);
        var zone = string.IsNullOrEmpty(layout.TimeZone) ? null : DateText.Zone(layout.TimeZone);

        var rows = new List<StatementRow>();
        var problems = new List<ImportProblem>();
        var lastRow = sheet.LastRow;
        string? lastAccount = null;

        // what sits above the header row is a title block, never read; a row there that reads as a payment of the
        // layout (exports pasted together, a headerless block first) is said, since it is left out
        for (var rowNo = 1; rowNo < detection.HeaderRowNo; rowNo++)
        {
            if (LooksLikeDataRow(sheet, rowNo, layout))
            {
                problems.Add(new ImportProblem(rowNo, ImportProblem.AboveHeader, $"Row {rowNo} sits above the header row and reads as a payment; rows above the header are not read."));
            }
        }

        for (var rowNo = detection.HeaderRowNo + 1; rowNo <= lastRow; rowNo++)
        {
            var cells = sheet.TextsOf(rowNo);
            if (cells.Count == 0)
            {
                rows.Add(Skipped(rowNo, SkipReason.Blank, cells));
                continue;
            }

            if (!detection.Headerless && cells.Count == detection.Headers.Count
                && cells.Select(HeaderText.Clean).Zip(detection.Headers).All(pair => HeaderText.Match(pair.First, pair.Second)))
            {
                rows.Add(Skipped(rowNo, SkipReason.Header, cells)); // a second header row: exports pasted together
                continue;
            }

            var account = Text(sheet.At(rowNo, accountCol));
            if (account is not null)
            {
                lastAccount = account;
            }

            var counterparty = Text(sheet.At(rowNo, nameCol));
            var dateCell = sheet.At(rowNo, dateCol);
            var amountCell = sheet.At(rowNo, amountCol);
            var dcText = Text(sheet.At(rowNo, dcCol));
            var description = Text(sheet.At(rowNo, descriptionCol));
            var transactionId = Text(sheet.At(rowNo, txCol));
            var amountText = Text(amountCell);
            var dateText = Text(dateCell);

            // a row without a counterparty (a fee, a card payment, a balance) is read like any other — the accountant
            // sees every transaction — but is never a payment row, and what cannot be read of it is no problem of the sheet
            var named = counterparty is not null;
            var problem = new List<string>();
            // a layout in another zone must not lose a time to the bare-date fallback: the hour decides the day
            var dateTime = DateText.Parse(dateCell, layout.DateFormats, dateOnlyFallback: zone is null);
            if (dateTime is { } parsed && (zone is not null || parsed.Kind == DateTimeKind.Utc))
            {
                dateTime = DateText.ToEstonian(parsed, zone);
            }

            if (dateTime is null && named)
            {
                problem.Add(dateText is null ? "no date" : $"date '{dateText}' not understood");
                problems.Add(new ImportProblem(rowNo, ImportProblem.BadDate, dateText is null ? "The row has no date." : $"Date '{dateText}' is not understood."));
            }

            decimal? amount = amountCell.Kind switch
            {
                CellKind.Number => decimal.Parse(amountCell.Text, NumberStyles.Float, CultureInfo.InvariantCulture),
                CellKind.Date => null, // Excel turned "12.05" into 12 May: never read the year as money
                _ => AmountText.Parse(amountText, layout.DecimalSeparator),
            };
            if (amount is null && named)
            {
                problem.Add(amountText is null ? "no amount" : $"amount '{amountText}' not understood");
                problems.Add(amountText is null
                    ? new ImportProblem(rowNo, ImportProblem.NoAmount, "The row has no amount.")
                    : new ImportProblem(rowNo, ImportProblem.BadAmount, $"Amount '{amountText}' is not understood."));
            }

            var direction = layout.Direction switch
            {
                DirectionRule.Sign => amount switch { < 0 => Direction.Debit, > 0 => Direction.Credit, _ => Direction.Unknown },
                _ => Marker(dcText, layout),
            };
            if (named && direction == Direction.Unknown && layout.Direction == DirectionRule.Markers && dcText is not null
                && !string.IsNullOrEmpty(layout.DebitMarker) && !string.IsNullOrEmpty(layout.CreditMarker))
            {
                // a bank's D/C column holds something else: say so (a one-marker layout such as Kassa has rows that are simply not payouts)
                problem.Add($"direction '{dcText}' is neither '{layout.DebitMarker}' nor '{layout.CreditMarker}'");
                problems.Add(new ImportProblem(rowNo, ImportProblem.BadMarker, $"Direction '{dcText}' is neither '{layout.DebitMarker}' nor '{layout.CreditMarker}'."));
            }

            var currency = Text(sheet.At(rowNo, currencyCol)) ?? (layout.AmountHasCurrency ? AmountText.CurrencyOf(amountText) : null);
            if (!named)
            {
                rows.Add(Skipped(rowNo, SkipReason.NoName, cells) with
                {
                    Date = dateTime is null ? null : DateOnly.FromDateTime(dateTime.Value),
                    DateText = dateText,
                    Account = account,
                    Amount = amount,
                    AmountText = amountText,
                    Currency = currency,
                    Description = description,
                    Reference = Text(sheet.At(rowNo, referenceCol)),
                    TransactionId = transactionId,
                    Direction = direction,
                    DcText = dcText,
                });
                continue;
            }

            var row = new StatementRow(
                rowNo,
                dateTime is null ? null : DateOnly.FromDateTime(dateTime.Value),
                dateText,
                account,
                counterparty,
                IdText.Code(sheet.At(rowNo, codeCol)),
                amount,
                amountText,
                currency,
                description,
                Text(sheet.At(rowNo, referenceCol)),
                transactionId,
                direction,
                dcText,
                null,
                problem.Count == 0 ? null : string.Join("; ", problem),
                "",
                cells);
            rows.Add(row with { RowHash = HashOf(row) });
        }

        if (rows.All(r => !r.IsPayment))
        {
            problems.Add(new ImportProblem(null, ImportProblem.NoRows, "The sheet has no rows with a counterparty."));
        }

        return new ParsedStatement(layout, detection.HeaderRowNo, detection.Headers, lastAccount, rows, problems, detection.Exact);
    }

    private static StatementRow Skipped(int rowNo, string reason, IReadOnlyList<string> cells) =>
        new(rowNo, null, null, null, null, null, null, null, null, null, null, null, Direction.Unknown, null, reason, null, "", cells);

    private static string? Text(Cell cell) => cell.IsBlank ? null : HeaderText.Clean(cell.Text);

    private static Direction Marker(string? dcText, StatementLayout layout)
    {
        if (dcText is null)
        {
            return Direction.Unknown;
        }

        if (IsMarker(dcText, layout.DebitMarker))
        {
            return Direction.Debit;
        }

        if (IsMarker(dcText, layout.CreditMarker))
        {
            return Direction.Credit;
        }

        return Direction.Unknown;
    }

    /// <summary>Case-insensitive, and through the same code-page forms that let a garbled header match: a sheet whose
    /// headers Excel garbled carries its markers garbled too ("Р”" for "Д").</summary>
    private static bool IsMarker(string text, string? marker)
    {
        if (string.IsNullOrEmpty(marker))
        {
            return false;
        }

        if (string.Equals(text, marker, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var markerForms = HeaderText.Forms(marker);
        return HeaderText.Forms(text).Any(form => markerForms.Contains(form) || markerForms.Any(m => string.Equals(m, form, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>A fingerprint of what the row says, from the parsed values, so that the same bank row hashes alike
    /// whether it came from the bank's CSV or from an Excel copy of it (a typed date, a double, a garbled name). The
    /// bank's transaction id is kept beside it for the matching to prefer when both rows carry a trustworthy one.</summary>
    public static string HashOf(StatementRow row)
    {
        var fields = new[]
        {
            row.Account ?? "",
            row.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? row.DateText ?? "",
            row.Amount?.ToString("0.############", CultureInfo.InvariantCulture) ?? row.AmountText ?? "",
            row.Direction.ToString(),
            NameText.Canonical(row.Counterparty),
            NameText.Canonical(row.Description),
            NameText.Canonical(row.CounterpartyCode), // a code's leading zero survives the fingerprint's digit rule, not Excel
            // not the reference number: a viitenumber of twenty digits loses its tail in an Excel copy
        };
        var text = string.Concat(fields.Select(f => $"{f.Length}:{f}|")); // length-prefixed, so a '|' inside a field cannot collide
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>The cells of a row as the store keeps them: a compact JSON string array aligned to the headers.</summary>
    public static string CellsJson(IReadOnlyList<string> cells) => JsonSerializer.Serialize(cells, LayoutCatalog.CompactJson);

    public static IReadOnlyList<string> CellsFromJson(string json) => JsonSerializer.Deserialize<List<string>>(json, LayoutCatalog.CompactJson) ?? [];
}
