using SatellesTSD.Core.Import;

namespace SatellesTSD.Core.Matching;

/// <summary>Which of the two passes a row is categorised in: the workbook's "Töötajapõhine" filter is "Y" while an
/// employee's hits are categorised and "N" over the rows nobody matched.</summary>
public enum MatchPass
{
    Employee,
    Unmatched,
}

public enum VerdictKind
{
    /// <summary>No category's patterns matched; the text is empty.</summary>
    None,
    /// <summary>One category matched and the row has its side: "[Palgad]".</summary>
    Matched,
    /// <summary>Two or more categories matched: "viga: kategooria tuvastamine [Üür] [Kommunaalkulud]".</summary>
    Ambiguous,
    /// <summary>One category matched but the row is on the other side: "viga: [Laenud] D/C kontroll".</summary>
    WrongSide,
    /// <summary>The catalog has no category at all: "viga: pole uhtegi maaratletud kategooriat".</summary>
    NoCategories,
}

/// <summary>What the category engine said about a row — the workbook's "Kontroll/Kategooria" text verbatim.</summary>
/// <param name="Category">The category when the verdict is Matched or WrongSide.</param>
/// <param name="Matched">Every category whose patterns matched, in catalog order.</param>
public sealed record CategoryVerdict(VerdictKind Kind, Category? Category, string Text, IReadOnlyList<string> Matched)
{
    public static readonly CategoryVerdict None = new(VerdictKind.None, null, "", []);

    public bool Equals(CategoryVerdict? other) =>
        other is not null && Kind == other.Kind && Equals(Category, other.Category) && Text == other.Text && Matched.SequenceEqual(other.Matched, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Kind, Category, Text, Matched.Count);
}

/// <summary>The workbook's cCategory.DefineCategory: every category of the catalog is tried in order on the row's
/// description (and, for a category with the payee check, on the payee's name); the patterns of the row's layout
/// decide; a second category matching is an error, a match on the wrong side is an error.</summary>
public sealed class Categorizer(MatchCatalog catalog)
{
    public const string NoCategoriesText = "viga: pole uhtegi maaratletud kategooriat";

    private readonly MatchCatalog _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

    public CategoryVerdict Define(string layoutId, string? description, string? payeeName, Direction direction, MatchPass pass)
    {
        ArgumentNullException.ThrowIfNull(layoutId);
        if (_catalog.Categories.Count == 0)
        {
            return new CategoryVerdict(VerdictKind.NoCategories, null, NoCategoriesText, []);
        }

        var filters = _catalog.FiltersFor(layoutId);
        var descriptions = Forms(description);
        var payees = Forms(payeeName);
        var matched = new List<Category>();
        string? group = null;
        var error = "";
        foreach (var category in _catalog.Categories)
        {
            if (category.EmployeeBased is { } employeeBased && employeeBased != (pass == MatchPass.Employee))
            {
                continue; // "Töötajapõhine" says the other pass
            }

            if (matched.Count > 0 && !string.IsNullOrEmpty(group) && string.Equals(category.Group, group, StringComparison.OrdinalIgnoreCase))
            {
                continue; // its group already matched
            }

            if (filters is null || !filters.TryGetValue(category.Name, out var patterns) || patterns.IsEmpty)
            {
                continue; // no filter cell for this source
            }

            var hit = descriptions.Any(patterns.Includes) || (category.PayeeCheck && payees.Any(patterns.Includes));
            if (hit && descriptions.Any(patterns.Excludes))
            {
                hit = false; // an exclude pattern on the description cancels the category, even one the payee brought
            }

            if (!hit)
            {
                continue;
            }

            if (matched.Count > 0)
            {
                error = (error.Length == 0 ? $"viga: kategooria tuvastamine [{matched[0].Name}]" : error) + $" [{category.Name}]";
            }

            matched.Add(category);
            group = category.Group;
        }

        if (error.Length > 0)
        {
            return new CategoryVerdict(VerdictKind.Ambiguous, null, error, matched.Select(c => c.Name).ToList());
        }

        if (matched.Count == 0)
        {
            return CategoryVerdict.None;
        }

        var category1 = matched[0];
        var rightSide = (direction == Direction.Debit && category1.Side == Direction.Debit) || (direction == Direction.Credit && category1.Side == Direction.Credit);
        return rightSide
            ? new CategoryVerdict(VerdictKind.Matched, category1, $"[{category1.Name}]", [category1.Name])
            : new CategoryVerdict(VerdictKind.WrongSide, category1, $"viga: [{category1.Name}] D/C kontroll", [category1.Name]);
    }

    /// <summary>The text as the file has it and, when Excel's garbling can be undone, the undone text too: a pattern
    /// written for either form finds the row.</summary>
    private static IReadOnlyList<string> Forms(string? text)
    {
        var raw = text ?? "";
        var undone = NameText.Demojibake(raw);
        return string.Equals(raw, undone, StringComparison.Ordinal) ? [raw] : [raw, undone];
    }
}
