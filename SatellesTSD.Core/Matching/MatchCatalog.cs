using System.Reflection;
using System.Text.Json;
using SatellesTSD.Core.Import;

namespace SatellesTSD.Core.Matching;

/// <summary>One of the workbook's payment categories ({category_config}): what it is called, which side of the
/// statement it lives on, and what the report does with it.</summary>
/// <param name="Side">The direction a row must have for the category to stand ("Deebet/Creedit (D/C)"); every workbook category is a debit.</param>
/// <param name="EmployeeBased">"Töötajapõhine": true = found for employees only (the employee pass), false = for non-employees only (the second pass), null = both.</param>
/// <param name="IsSalary">"Palk": the amount counts in the person's net salary of the month.</param>
/// <param name="ReportSheet">"Aruande leht": the category gets its own sheet in the report workbook.</param>
/// <param name="SubTable">"Lisatabel": the category gets a sub-table under the main table.</param>
/// <param name="PayeeCheck">"Saaja kontroll": the include patterns are also tried on the payee's name (Trahvid).</param>
/// <param name="Group">"Grupp": once a category of a group matched, the others of the group are not tried (empty in the workbook).</param>
/// <param name="Code">"TOR/Kategooria kood": the matrix code the category declares under (uur, haigus), null when found but never declared.</param>
public sealed record Category(
    string Name,
    Direction Side,
    bool? EmployeeBased,
    bool IsSalary,
    bool ReportSheet,
    bool SubTable,
    bool PayeeCheck,
    string? Group = null,
    string? Code = null);

/// <summary>A matching refusal: a catalog that cannot be used, a run that cannot be made.</summary>
public sealed class MatchException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The workbook's matching configuration as one document: the categories in their order, the filter cells
/// per layout (the sets the seventeen CONFIG rows reduce to — fourteen rows are byte-identical copies of Swedbank's),
/// and the first-name aliases of {system_data}. The default is embedded; the owner's own document replaces it whole.
/// Names are compared case-insensitively throughout, as VBA's Option Compare Text compared them.</summary>
public sealed class MatchCatalog
{
    public const string ResourceName = "SatellesTSD.Core.Matching.matching.json";

    private static readonly Lazy<MatchCatalog> DefaultCatalog = new(LoadDefault);

    private readonly Dictionary<string, IReadOnlyDictionary<string, PatternList>> _parsedSets = new(StringComparer.Ordinal);

    private MatchCatalog(Document document)
    {
        Categories = document.Categories ?? [];
        FilterSets = (document.FilterSets ?? []).ToDictionary(s => s.Key, s => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(s.Value ?? [], StringComparer.Ordinal), StringComparer.Ordinal);
        LayoutFilterSets = new Dictionary<string, string>(document.LayoutFilterSets ?? [], StringComparer.Ordinal);
        Aliases = (document.Aliases ?? []).Select(g => (IReadOnlyList<string>)(g ?? []).ToList()).ToList();
    }

    public static MatchCatalog Default => DefaultCatalog.Value;

    public IReadOnlyList<Category> Categories { get; }

    /// <summary>Filter set name → category name → the filter cell as the workbook wrote it ("a,b;c").</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> FilterSets { get; }

    /// <summary>Layout id → the name of its filter set.</summary>
    public IReadOnlyDictionary<string, string> LayoutFilterSets { get; }

    /// <summary>Groups of first names that stand for each other (SERHII, SERGII, SERGEI …).</summary>
    public IReadOnlyList<IReadOnlyList<string>> Aliases { get; }

    /// <summary>Parses a document and refuses one that cannot be used.</summary>
    public static MatchCatalog Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        Document document;
        try
        {
            document = JsonSerializer.Deserialize<Document>(json, LayoutCatalog.Json) ?? throw new MatchException("The match catalog is empty.");
        }
        catch (JsonException e)
        {
            throw new MatchException("The match catalog is not valid JSON: " + e.Message, e);
        }

        var catalog = new MatchCatalog(document);
        var problems = catalog.Validate();
        if (problems.Count > 0)
        {
            throw new MatchException("The match catalog cannot be used:\n" + string.Join("\n", problems));
        }

        return catalog;
    }

    public string ToJson() => JsonSerializer.Serialize(
        new Document(
            Categories.ToList(),
            FilterSets.ToDictionary(s => s.Key, s => s.Value.ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal), StringComparer.Ordinal),
            LayoutFilterSets.ToDictionary(l => l.Key, l => l.Value, StringComparer.Ordinal),
            Aliases.Select(g => g.ToList()).ToList()),
        LayoutCatalog.Json);

    /// <summary>What makes the document unusable; empty when it can be used.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        foreach (var group in Categories.GroupBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            if (group.Key.Length == 0)
            {
                problems.Add("A category has no name.");
            }
            else if (group.Count() > 1)
            {
                problems.Add($"The category name '{group.Key}' is used {group.Count()} times.");
            }
        }

        foreach (var category in Categories)
        {
            if (category.Side is not (Direction.Debit or Direction.Credit))
            {
                problems.Add($"Category '{category.Name}': the side must be Debit or Credit.");
            }
        }

        foreach (var (setName, filters) in FilterSets)
        {
            if (string.IsNullOrWhiteSpace(setName))
            {
                problems.Add("A filter set has no name.");
            }

            foreach (var twice in filters.Keys.GroupBy(k => k, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            {
                problems.Add($"Filter set '{setName}' names the category '{twice.Key}' {twice.Count()} times ({string.Join(", ", twice)}).");
            }

            foreach (var (categoryName, cell) in filters)
            {
                if (Find(categoryName) is null)
                {
                    problems.Add($"Filter set '{setName}' names a category '{categoryName}' the catalog does not have.");
                }

                foreach (var problem in PatternList.Parse(cell).Problems())
                {
                    problems.Add($"Filter set '{setName}', category '{categoryName}': {problem}");
                }
            }
        }

        foreach (var (layoutId, setName) in LayoutFilterSets)
        {
            if (!FilterSets.ContainsKey(setName))
            {
                problems.Add($"Layout '{layoutId}' refers to a filter set '{setName}' the catalog does not have.");
            }
        }

        foreach (var group in Aliases)
        {
            if (group.Count < 2 || group.Any(string.IsNullOrWhiteSpace))
            {
                problems.Add($"An alias group must hold two or more names: [{string.Join(", ", group)}].");
            }
        }

        return problems;
    }

    public Category? Find(string name) => Categories.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The parsed filters of a layout, category by category (names case-insensitive); null when the layout
    /// has no filter set — as the workbook's empty CONFIG rows, nothing is ever found on such a source.</summary>
    public IReadOnlyDictionary<string, PatternList>? FiltersFor(string layoutId)
    {
        if (!LayoutFilterSets.TryGetValue(layoutId, out var setName) || !FilterSets.TryGetValue(setName, out var cells))
        {
            return null;
        }

        lock (_parsedSets)
        {
            if (!_parsedSets.TryGetValue(setName, out var parsed))
            {
                parsed = cells.ToDictionary(c => c.Key, c => PatternList.Parse(c.Value), StringComparer.OrdinalIgnoreCase);
                _parsedSets[setName] = parsed;
            }

            return parsed;
        }
    }

    /// <summary>The alias group the first name belongs to (the whole name equal to one of its names, case-insensitively), or null.</summary>
    public IReadOnlyList<string>? AliasGroupOf(string firstName) =>
        Aliases.FirstOrDefault(g => g.Any(a => string.Equals(a, firstName, StringComparison.OrdinalIgnoreCase)));

    private static MatchCatalog LoadDefault()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    private sealed record Document(
        List<Category>? Categories,
        Dictionary<string, Dictionary<string, string>?>? FilterSets,
        Dictionary<string, string>? LayoutFilterSets,
        List<List<string>?>? Aliases);
}
