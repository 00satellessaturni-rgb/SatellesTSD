using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SatellesTSD.Core.Import;

/// <summary>What kind of table a layout describes — the workbook's Type column.</summary>
public enum LayoutKind
{
    /// <summary>A bank export (the workbook's type 1): named after the bank plus the account's last four digits.</summary>
    Bank,
    /// <summary>Any other table — the payroll sheet, the firm's own helper table, a simple export (type 0).</summary>
    Table,
}

/// <summary>How a row's direction (money out or in) is read.</summary>
public enum DirectionRule
{
    /// <summary>A column holds a marker compared with <see cref="StatementLayout.DebitMarker"/> and <see cref="StatementLayout.CreditMarker"/>.</summary>
    Markers,
    /// <summary>The amount's sign: negative is money out (debit), positive money in (credit).</summary>
    Sign,
}

/// <summary>The header text of each role column; null when the layout has no such column.</summary>
public sealed record LayoutRoles(
    string? Account,
    string? Date,
    string? Counterparty,
    string? Amount,
    string? Description,
    string? DebitCredit,
    string? Currency = null,
    string? CounterpartyCode = null,
    string? TransactionId = null,
    string? Reference = null);

/// <summary>One statement layout: how a sheet is recognised (its exact header row) and read (which column plays which
/// role, how direction, dates and amounts are written). The defaults are the workbook's seventeen; the owner can
/// override or add layouts as JSON of this very shape.</summary>
/// <param name="Id">Stable identity, the key of category patterns and of stored sheets.</param>
/// <param name="Bank">The display name — "LHV pank", "Swedbank" — not unique.</param>
/// <param name="Headers">The header row as the bank writes it, in order; a sheet matches when its header row has exactly these texts (a code-page mix-up either way is tolerated, see <see cref="HeaderText"/>).</param>
/// <param name="DateFormats">Date patterns tried on text cells before the defaults (<see cref="DateText.DefaultFormats"/>).</param>
/// <param name="DecimalSeparator">"." or "," when the bank is known to use one; null lets the amount decide.</param>
/// <param name="TimeZone">The zone a date-time text is in when it is not local ("UTC" for Revolut); converted to Estonian time.</param>
/// <param name="AmountHasCurrency">The amount cell carries the currency code too ("123,45 EUR").</param>
/// <param name="AllowHeaderless">The bank's CSV has no header row (SEB): a sheet whose first rows have this layout's width and a date, an amount and a marker in the role columns is read as data from row 1.</param>
/// <param name="HeaderlessAccountPattern">With <paramref name="AllowHeaderless"/>: a regular expression the account cell must match ("^EE\d{2}10" — SEB's own IBANs), so that another bank's file of the same shape is not taken for this one.</param>
public sealed record StatementLayout(
    string Id,
    string Bank,
    LayoutKind Kind,
    IReadOnlyList<string> Headers,
    LayoutRoles Roles,
    DirectionRule Direction = DirectionRule.Markers,
    string? DebitMarker = null,
    string? CreditMarker = null,
    IReadOnlyList<string>? DateFormats = null,
    string? DecimalSeparator = null,
    string? TimeZone = null,
    bool AmountHasCurrency = false,
    bool AllowHeaderless = false,
    string? HeaderlessAccountPattern = null,
    string? Notes = null)
{
    public bool Equals(StatementLayout? other) =>
        other is not null && Id == other.Id && Bank == other.Bank && Kind == other.Kind && Seq.Equal(Headers, other.Headers) && Equals(Roles, other.Roles)
        && Direction == other.Direction && DebitMarker == other.DebitMarker && CreditMarker == other.CreditMarker && Seq.Equal(DateFormats, other.DateFormats)
        && DecimalSeparator == other.DecimalSeparator && TimeZone == other.TimeZone && AmountHasCurrency == other.AmountHasCurrency && AllowHeaderless == other.AllowHeaderless
        && HeaderlessAccountPattern == other.HeaderlessAccountPattern && Notes == other.Notes;

    public override int GetHashCode() => HashCode.Combine(Id, Bank, Kind, Seq.Hash(Headers), Roles, Direction);

    /// <summary>Whether the direction is only assumed: the "marker" is the currency column (Paysera's short export, where
    /// every EUR row counts as money out, as the workbook had it) — the statement itself never says debit or credit.
    /// A method, not a property, so that it never travels in the layout's JSON.</summary>
    public bool DirectionIsAssumed() =>
        Direction == DirectionRule.Markers && Roles.DebitCredit is { } marker && string.Equals(marker, Roles.Currency, StringComparison.OrdinalIgnoreCase);

    /// <summary>The 1-based column of a role header within <see cref="Headers"/>, or 0 when the role has no column.</summary>
    public int ColumnOf(string? roleHeader)
    {
        if (string.IsNullOrEmpty(roleHeader))
        {
            return 0;
        }

        for (var i = 0; i < Headers.Count; i++)
        {
            if (HeaderText.Match(Headers[i], roleHeader))
            {
                return i + 1;
            }
        }

        return 0;
    }

    /// <summary>What is wrong with the layout; empty when it can be used.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(Id))
        {
            problems.Add("The layout has no Id.");
        }

        if (string.IsNullOrWhiteSpace(Bank))
        {
            problems.Add($"Layout '{Id}' has no Bank name.");
        }

        if (Headers is null || Headers.Count == 0)
        {
            problems.Add($"Layout '{Id}' has no Headers.");
            return problems;
        }

        if (Headers.Any(string.IsNullOrWhiteSpace))
        {
            problems.Add($"Layout '{Id}' has a blank header; the header row must be contiguous.");
        }

        if (Roles is null)
        {
            problems.Add($"Layout '{Id}' has no Roles.");
            return problems;
        }

        foreach (var (role, header) in new[]
                 {
                     ("Date", Roles.Date), ("Counterparty", Roles.Counterparty), ("Amount", Roles.Amount),
                 })
        {
            if (string.IsNullOrEmpty(header))
            {
                problems.Add($"Layout '{Id}' names no {role} column.");
            }
        }

        foreach (var (role, header) in new[]
                 {
                     ("Account", Roles.Account), ("Date", Roles.Date), ("Counterparty", Roles.Counterparty), ("Amount", Roles.Amount),
                     ("Description", Roles.Description), ("DebitCredit", Roles.DebitCredit), ("Currency", Roles.Currency),
                     ("CounterpartyCode", Roles.CounterpartyCode), ("TransactionId", Roles.TransactionId), ("Reference", Roles.Reference),
                 })
        {
            if (!string.IsNullOrEmpty(header) && ColumnOf(header) == 0)
            {
                problems.Add($"Layout '{Id}': the {role} header '{header}' is not in its Headers.");
            }
        }

        if (Direction == DirectionRule.Markers)
        {
            if (string.IsNullOrEmpty(Roles.DebitCredit))
            {
                problems.Add($"Layout '{Id}' reads direction from markers but names no DebitCredit column.");
            }

            if (string.IsNullOrEmpty(DebitMarker) && string.IsNullOrEmpty(CreditMarker))
            {
                problems.Add($"Layout '{Id}' reads direction from markers but has neither marker.");
            }
        }

        if (!string.IsNullOrEmpty(DecimalSeparator) && DecimalSeparator is not ("." or ","))
        {
            problems.Add($"Layout '{Id}': DecimalSeparator must be '.' or ','.");
        }

        if (!string.IsNullOrEmpty(TimeZone))
        {
            try
            {
                DateText.Zone(TimeZone);
            }
            catch (TimeZoneNotFoundException)
            {
                problems.Add($"Layout '{Id}': time zone '{TimeZone}' is unknown on this machine.");
            }
        }

        if (!string.IsNullOrEmpty(HeaderlessAccountPattern))
        {
            if (!AllowHeaderless)
            {
                problems.Add($"Layout '{Id}' has a HeaderlessAccountPattern but does not allow a headerless sheet.");
            }

            if (string.IsNullOrEmpty(Roles.Account))
            {
                problems.Add($"Layout '{Id}' has a HeaderlessAccountPattern but no Account column.");
            }

            try
            {
                _ = new System.Text.RegularExpressions.Regex(HeaderlessAccountPattern);
            }
            catch (ArgumentException)
            {
                problems.Add($"Layout '{Id}': HeaderlessAccountPattern '{HeaderlessAccountPattern}' is not a valid regular expression.");
            }
        }

        return problems;
    }
}

/// <summary>The layouts the parser knows: the embedded defaults, with the owner's overrides merged by Id.</summary>
public sealed class LayoutCatalog
{
    public const string ResourceName = "SatellesTSD.Core.Import.layouts.json";

    /// <summary>The JSON shape of a layout; pinned by a golden test because the owner's files and the stored
    /// snapshots depend on it.</summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The one-line shape of the stored string arrays (a row's cells, a sheet's headers): no indentation,
    /// non-ASCII as it is.</summary>
    public static readonly JsonSerializerOptions CompactJson = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly Lazy<LayoutCatalog> DefaultCatalog = new(LoadEmbedded);

    /// <summary>The workbook's seventeen layouts, embedded in the assembly.</summary>
    public static LayoutCatalog Default => DefaultCatalog.Value;

    public IReadOnlyList<StatementLayout> Layouts { get; }

    private LayoutCatalog(IReadOnlyList<StatementLayout> layouts)
    {
        Layouts = layouts;
    }

    public StatementLayout? Find(string id) => Layouts.FirstOrDefault(l => l.Id == id);

    /// <summary>Builds a catalog from layouts, refusing a set that cannot be used (duplicate ids, a role header not in
    /// the headers, two layouts that would match the same header row).</summary>
    public static LayoutCatalog Of(IEnumerable<StatementLayout> layouts)
    {
        var list = layouts.ToList();
        var problems = Validate(list);
        if (problems.Count > 0)
        {
            throw new ImportException("The statement layouts cannot be used:\n" + string.Join("\n", problems));
        }

        return new LayoutCatalog(list);
    }

    public static LayoutCatalog Parse(string json)
    {
        var layouts = JsonSerializer.Deserialize<List<StatementLayout>>(json, Json)
            ?? throw new ImportException("The layouts file is empty.");
        return Of(layouts);
    }

    public static LayoutCatalog Load(Stream json)
    {
        using var reader = new StreamReader(json, System.Text.Encoding.UTF8);
        return Parse(reader.ReadToEnd());
    }

    public static IReadOnlyList<StatementLayout> ParseLayouts(string json) =>
        JsonSerializer.Deserialize<List<StatementLayout>>(json, Json) ?? [];

    public static string ToJson(IEnumerable<StatementLayout> layouts) => JsonSerializer.Serialize(layouts.ToList(), Json);

    public static string ToJson(StatementLayout layout) => JsonSerializer.Serialize(layout, Json);

    /// <summary>One layout from its JSON, checked: a snapshot or an override that lacks what a layout needs (its
    /// headers, its roles) is refused here, not where a null would surface later.</summary>
    public static StatementLayout FromJson(string json)
    {
        var layout = JsonSerializer.Deserialize<StatementLayout>(json, Json) ?? throw new ImportException("The layout JSON is empty.");
        var problems = layout.Problems();
        if (problems.Count > 0)
        {
            throw new ImportException("The layout JSON cannot be used:\n" + string.Join("\n", problems));
        }

        return layout;
    }

    /// <summary>These layouts over this catalog: an override with a known Id replaces the default, a new Id adds a
    /// layout. The result is validated like any catalog.</summary>
    public LayoutCatalog WithOverrides(IEnumerable<StatementLayout> overrides)
    {
        var merged = Layouts.ToList();
        foreach (var layout in overrides)
        {
            var at = merged.FindIndex(l => l.Id == layout.Id);
            if (at >= 0)
            {
                merged[at] = layout;
            }
            else
            {
                merged.Add(layout);
            }
        }

        return Of(merged);
    }

    public static IReadOnlyList<string> Validate(IReadOnlyList<StatementLayout> layouts)
    {
        var problems = new List<string>();
        foreach (var layout in layouts)
        {
            problems.AddRange(layout.Problems());
        }

        if (problems.Count > 0)
        {
            return problems;
        }

        foreach (var group in layouts.GroupBy(l => l.Id).Where(g => g.Count() > 1))
        {
            problems.Add($"Layout id '{group.Key}' is used {group.Count()} times.");
        }

        for (var i = 0; i < layouts.Count; i++)
        {
            for (var j = i + 1; j < layouts.Count; j++)
            {
                if (SameHeaderRow(layouts[i], layouts[j]))
                {
                    problems.Add($"Layouts '{layouts[i].Id}' and '{layouts[j].Id}' have the same header row; a sheet could not tell them apart.");
                }
                else if (ShareQuestionMarkRow(layouts[i], layouts[j]))
                {
                    problems.Add($"Layouts '{layouts[i].Id}' and '{layouts[j].Id}' differ only in letters an exporter may write as '?'; such a sheet could not tell them apart.");
                }
            }
        }

        return problems;
    }

    private static bool SameHeaderRow(StatementLayout a, StatementLayout b) =>
        a.Headers.Count == b.Headers.Count && a.Headers.Zip(b.Headers).All(pair => HeaderText.Match(pair.First, pair.Second));

    /// <summary>Whether one header row with '?' for every non-ASCII letter would match both layouts: the headers
    /// have the same length and differ nowhere in an ASCII character.</summary>
    private static bool ShareQuestionMarkRow(StatementLayout a, StatementLayout b) =>
        a.Headers.Count == b.Headers.Count && a.Headers.Zip(b.Headers).All(pair =>
            pair.First.Length == pair.Second.Length
            && pair.First.Zip(pair.Second).All(c => c.First == c.Second || (c.First >= 0x80 && c.Second >= 0x80) || c.First == '?' || c.Second == '?'));

    private static LayoutCatalog LoadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        return Load(stream);
    }
}

public sealed class ImportException(string message, Exception? inner = null) : Exception(message, inner);
