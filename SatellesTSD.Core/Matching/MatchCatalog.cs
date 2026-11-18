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
            FilterSets.ToDictionary(s => s.Key, s => (Dictionary<string, string>?)s.Value.ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal), StringComparer.Ordinal),
            LayoutFilterSets.ToDictionary(l => l.Key, l => l.Value, StringComparer.Ordinal),
            Aliases.Select(g => (List<string>?)g.ToList()).ToList()),
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

    /// <summary>What the category phrases' file says it is.</summary>
    public const string PhrasesKind = "satellestsd-category-phrases";

    /// <summary>The words and phrases that find each category — the filter cells of every set, "include,include;exclude"
    /// — as a file of their own (decision 202): the categories, the layouts' sets and the aliases are not in it.</summary>
    public string PhrasesToJson() => JsonSerializer.Serialize(
        new PhrasesDocument(PhrasesKind, FilterSets.ToDictionary(s => s.Key, s => (Dictionary<string, string>?)s.Value.ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal), StringComparer.Ordinal)),
        LayoutCatalog.Json);

    /// <summary>This catalog with the phrases of a file over it (decision 202): each cell the file names replaces the one of
    /// its set and category; a cell, a set or a category the file does not name stays. The file is refused whole — a
    /// refusal's code and subject in its data (<see cref="PhrasesRefusal"/>) — when it is no phrases' file, or names a set
    /// or a category the catalog does not have (the categories and the sets are the built-in ones: the review lines and
    /// the runs keep category names), or a cell that cannot be read. The number of cells it changed is given.</summary>
    public MatchCatalog WithPhrases(string json, out int changed) => Merge(json, lenient: false, out changed);

    /// <summary>This catalog with the owner's saved phrases over it (decision 202): the cells the store keeps, those that
    /// differ from the built-in ones. A cell whose set or category a later release no longer has, or that no longer reads,
    /// is left out rather than refusing the search: the built-in catalog of the release in use stays the base.</summary>
    public MatchCatalog WithSavedPhrases(string json) => Merge(json, lenient: true, out _);

    /// <summary>Whether a saved catalog document is the owner's phrases (laid over the built-in catalog) rather than a
    /// catalog whole.</summary>
    public static bool IsPhrases(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            return JsonSerializer.Deserialize<PhrasesDocument>(json.TrimStart('﻿', ' ', '\t', '\r', '\n'), LayoutCatalog.Json) is { Kind: PhrasesKind };
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>The phrases of this catalog that differ from <paramref name="baseline"/>'s, as a phrases' file; null when
    /// none differs. What the store keeps of an import (decision 202), so a later release's own cells, sets, categories
    /// and layouts still reach the search.</summary>
    public string? PhrasesOver(MatchCatalog baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        var sets = new Dictionary<string, Dictionary<string, string>?>(StringComparer.Ordinal);
        foreach (var (setName, cells) in FilterSets)
        {
            baseline.FilterSets.TryGetValue(setName, out var before);
            var own = cells.Where(c => before is null || !before.TryGetValue(c.Key, out var b) || !string.Equals(b, c.Value, StringComparison.Ordinal))
                .ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal);
            if (own.Count > 0)
            {
                sets[setName] = own;
            }
        }

        return sets.Count == 0 ? null : JsonSerializer.Serialize(new PhrasesDocument(PhrasesKind, sets), LayoutCatalog.Json);
    }

    private MatchCatalog Merge(string json, bool lenient, out int changed)
    {
        ArgumentNullException.ThrowIfNull(json);
        var text = json.TrimStart('﻿', ' ', '\t', '\r', '\n');
        PhrasesDocument? file;
        try
        {
            file = text.Length == 0 ? null : JsonSerializer.Deserialize<PhrasesDocument>(text, LayoutCatalog.Json);
        }
        catch (JsonException e) when (!lenient)
        {
            throw PhrasesRefusal.Of(PhrasesRefusal.NotJson, "The category phrases' file is not valid JSON: " + e.Message, null, e);
        }
        catch (JsonException)
        {
            changed = 0;
            return this;
        }

        if (file is null || !string.Equals(file.Kind, PhrasesKind, StringComparison.Ordinal) || file.Sets is not { Count: > 0 } sets)
        {
            if (lenient)
            {
                changed = 0;
                return this;
            }

            throw PhrasesRefusal.Of(PhrasesRefusal.NotPhrases, $"The file is not a category phrases' file (\"kind\": \"{PhrasesKind}\" with its \"sets\").");
        }

        var merged = FilterSets.ToDictionary(s => s.Key, s => new Dictionary<string, string>(s.Value, StringComparer.Ordinal), StringComparer.Ordinal);
        changed = 0;
        foreach (var (setName, cells) in sets)
        {
            if (!merged.TryGetValue(setName, out var target))
            {
                if (lenient)
                {
                    continue;
                }

                throw PhrasesRefusal.Of(PhrasesRefusal.UnknownSet, $"The file names a filter set '{setName}' the catalog does not have ({string.Join(", ", merged.Keys)}).", setName);
            }

            foreach (var (categoryName, cell) in cells ?? [])
            {
                if (Find(categoryName) is not { } category)
                {
                    if (lenient)
                    {
                        continue;
                    }

                    throw PhrasesRefusal.Of(PhrasesRefusal.UnknownCategory, $"The file names a category '{categoryName}' (set '{setName}') the catalog does not have.", $"{setName} / {categoryName}");
                }

                var value = cell ?? "";
                if (PatternList.Parse(value).Findings() is { Count: > 0 } findings)
                {
                    if (lenient)
                    {
                        continue;
                    }

                    // the first problem's kind goes with the refusal, so the window words it (the fifth audit, item 27)
                    throw PhrasesRefusal.Of(PhrasesRefusal.BadCell, $"Set '{setName}', category '{category.Name}': {string.Join("; ", findings.Select(f => f.Text))}", $"{setName} / {category.Name}",
                        problem: findings[0]);
                }

                // the cell under the catalog's own spelling of the name (names are compared case-insensitively)
                var key = target.Keys.FirstOrDefault(k => string.Equals(k, categoryName, StringComparison.OrdinalIgnoreCase)) ?? category.Name;
                if (!target.TryGetValue(key, out var before) || !string.Equals(before, value, StringComparison.Ordinal))
                {
                    target[key] = value;
                    changed++;
                }
            }
        }

        var catalog = new MatchCatalog(new Document(
            Categories.ToList(),
            merged.ToDictionary(s => s.Key, s => (Dictionary<string, string>?)s.Value, StringComparer.Ordinal),
            LayoutFilterSets.ToDictionary(l => l.Key, l => l.Value, StringComparer.Ordinal),
            Aliases.Select(g => (List<string>?)g.ToList()).ToList()));
        if (catalog.Validate() is { Count: > 0 } left)
        {
            if (lenient)
            {
                changed = 0;
                return this;
            }

            throw PhrasesRefusal.Of(PhrasesRefusal.BadCell, "The phrases cannot be used:\n" + string.Join("\n", left));
        }

        return catalog;
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

    private sealed record PhrasesDocument(string? Kind, Dictionary<string, Dictionary<string, string>?>? Sets);
}

/// <summary>A refusal of the category phrases' file (decision 202): a <see cref="MatchException"/> whose code and subject
/// are in its data, so the window words it in Estonian (decision 183).</summary>
public static class PhrasesRefusal
{
    public const string NotJson = "PHRASES_NOT_JSON";
    public const string NotPhrases = "PHRASES_NOT_PHRASES";
    public const string UnknownSet = "PHRASES_UNKNOWN_SET";
    public const string UnknownCategory = "PHRASES_UNKNOWN_CATEGORY";
    public const string BadCell = "PHRASES_BAD_CELL";

    /// <param name="problem">A bad cell's first pattern problem, kept in the data as plain values (the fifth audit, item 27).</param>
    public static MatchException Of(string code, string message, string? subject = null, Exception? inner = null, PatternProblem? problem = null)
    {
        var refusal = new MatchException(message, inner);
        refusal.Data["Code"] = code;
        refusal.Data["Subject"] = subject;
        if (problem is not null)
        {
            refusal.Data["Problem"] = problem.Kind.ToString();
            refusal.Data["Pattern"] = problem.Pattern;
            refusal.Data["Position"] = problem.Position;
            refusal.Data["Range"] = problem.Range;
        }

        return refusal;
    }

    public static string? CodeOf(Exception e) => e?.Data["Code"] as string;

    public static string? SubjectOf(Exception e) => e?.Data["Subject"] as string;

    /// <summary>The pattern problem a bad cell's refusal carries, or null (a merged catalog that fails its check names none).</summary>
    public static PatternProblem? ProblemOf(Exception e) =>
        e?.Data["Problem"] is string kind && Enum.TryParse<PatternProblemKind>(kind, out var parsed) && e.Data["Pattern"] is string pattern && e.Data["Position"] is int position
            ? new PatternProblem(parsed, pattern, position, e.Data["Range"] as string)
            : null;
}
