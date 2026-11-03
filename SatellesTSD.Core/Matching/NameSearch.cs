using SatellesTSD.Core.Import;

namespace SatellesTSD.Core.Matching;

/// <summary>A person as the search knows them: the two names as the register wrote them (trimmed) and a key that
/// tells persons apart — the personal code, or the names when there is none.</summary>
public sealed record SearchPerson(string Key, string LastName, string FirstName, string? PersonalCode)
{
    public static SearchPerson Of(string? personalCode, string lastName, string firstName)
    {
        var last = NameText.Normalize(lastName);
        var first = NameText.Normalize(firstName);
        var code = string.IsNullOrWhiteSpace(personalCode) ? null : personalCode.Trim();
        return new SearchPerson(KeyOf(code, last, first), last, first, code);
    }

    public static string KeyOf(string? personalCode, string lastName, string firstName) =>
        !string.IsNullOrWhiteSpace(personalCode) ? personalCode.Trim() : NameKey(lastName, firstName);

    /// <summary>The names as a key: the workbook compared them case-insensitively.</summary>
    public static string NameKey(string lastName, string firstName) => (NameText.Normalize(lastName) + "|" + NameText.Normalize(firstName)).ToUpperInvariant();

    public string FullName => $"{LastName} {FirstName}";

    /// <summary>Whether the other person counts as somebody else for the uniqueness check: the workbook compared the
    /// personal codes, so two persons without one were never each other's competitor.</summary>
    public bool Competes(SearchPerson other) => !string.Equals(PersonalCode ?? "", other.PersonalCode ?? "", StringComparison.OrdinalIgnoreCase);
}

/// <summary>What the search of one source gave for one person: the rows hit, the masks that hit them (one first-name
/// mask per hit: at the alias level each row carries the alias it was found through), and whether a mask or an alias
/// was needed — in which case the workbook asked whether the masks could also mean another person of the register,
/// and dropped the hits when they could (<see cref="ConflictsWith"/>, <see cref="Dropped"/>).</summary>
/// <param name="FirstMask">The first-name mask of the first hit (the level's mask when no alias was involved).</param>
/// <param name="HitMasks">The first-name mask of each hit, in the order of <see cref="Hits"/>.</param>
public sealed record SheetSearch(IReadOnlyList<int> Hits, string LastMask, string FirstMask, bool MaskUsed, SearchPerson? ConflictsWith, IReadOnlyList<int> Dropped, IReadOnlyList<string> HitMasks)
{
    public static readonly SheetSearch None = new([], "", "", false, null, [], []);

    public bool Found => Hits.Count > 0;

    /// <summary>Whether some hits of the level were dropped because their masks could mean another person.</summary>
    public bool Ambiguous => Dropped.Count > 0;

    /// <summary>The workbook's "Otsing*" text: "lastMask firstMask" when a mask or alias was used, else empty.</summary>
    public string FullMask => MaskUsed ? $"{LastMask} {FirstMask}" : "";

    public bool Equals(SheetSearch? other) =>
        other is not null && Hits.SequenceEqual(other.Hits) && LastMask == other.LastMask && FirstMask == other.FirstMask && MaskUsed == other.MaskUsed
        && Equals(ConflictsWith, other.ConflictsWith) && Dropped.SequenceEqual(other.Dropped) && HitMasks.SequenceEqual(other.HitMasks, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Hits.Count, LastMask, FirstMask, MaskUsed, ConflictsWith, Dropped.Count, HitMasks.Count);
}

/// <summary>The workbook's name search (cPayOut.SearchData, mdlFunc.NextMaskSD, cEmployee.IsUniqueResult), one
/// source at a time. A name cell is split into tokens; the person's last name must equal a token and the first
/// name another, case-insensitively (VBA Like under Option Compare Text, so a "?" or "*" written in the register's
/// name is a wildcard). When that finds nothing the names are masked one letter at a time ("*AMM", "T*MM", "TA*M",
/// "TAM*"): the last name first, and once a row carries the last name, the first name — through its aliases first
/// (SERHII = SERGEI …), then its masks. A masked or alias hit stands only when the masks could not also mean another
/// person of the register.</summary>
public sealed class NameSearch(MatchCatalog catalog)
{
    /// <summary>The characters the workbook deleted from a name cell before splitting it (RemoveSymbols).</summary>
    public const string StrippedSymbols = ",.;:()";

    private readonly MatchCatalog _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

    /// <summary>The tokens of a name cell: the symbols deleted (not replaced by spaces, so "Tamm,Mari" is one
    /// token), split on single spaces, empties dropped.</summary>
    public static IReadOnlyList<string> Tokens(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var cleaned = text;
        foreach (var symbol in StrippedSymbols)
        {
            cleaned = cleaned.Replace(symbol.ToString(), "", StringComparison.Ordinal);
        }

        return cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>The masks of a name in the workbook's order: one letter at a time replaced by "*", from the first;
    /// a name shorter than two characters has none.</summary>
    public static IReadOnlyList<string> Masks(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length < 2)
        {
            return [];
        }

        var masks = new List<string>(name.Length);
        for (var pos = 0; pos < name.Length; pos++)
        {
            masks.Add(name[..pos] + "*" + name[(pos + 1)..]);
        }

        return masks;
    }

    /// <summary>Searches one source. <paramref name="names"/> are the rows' name cells in row order (null for a row
    /// without one); <paramref name="others"/> the other persons of the register, for the uniqueness check.
    /// Returns the rows hit at the first level that hit any: every row that qualifies at that level, less those
    /// whose masks could also mean another person.</summary>
    public SheetSearch Search(SearchPerson person, IReadOnlyList<string?> names, IReadOnlyList<SearchPerson> others)
    {
        ArgumentNullException.ThrowIfNull(person);
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(others);
        if (person.LastName.Length == 0 || person.FirstName.Length == 0)
        {
            return SheetSearch.None; // the workbook's Find with an empty text gave nothing usable
        }

        var tokens = names.Select(Tokens).ToList();
        var lastMasks = new List<string> { person.LastName };
        lastMasks.AddRange(Masks(person.LastName));
        var aliasGroup = _catalog.AliasGroupOf(person.FirstName);
        var competitors = others.Where(o => o.Key != person.Key && person.Competes(o)).ToList();

        foreach (var lastMask in lastMasks)
        {
            // the rows carrying the last name at this level, each with the tokens left for the first name
            var candidates = new List<(int Row, List<string> Others)>();
            for (var i = 0; i < tokens.Count; i++)
            {
                var index = tokens[i].ToList().FindIndex(t => LikePattern.IsMatch(t, lastMask));
                if (index >= 0)
                {
                    candidates.Add((i, tokens[i].Where((_, j) => j != index).ToList()));
                }
            }

            if (candidates.Count == 0)
            {
                continue; // nobody carries the last name at this level: mask it further
            }

            var lastMasked = !ReferenceEquals(lastMask, lastMasks[0]);
            foreach (var (firstMask, isAlias) in FirstVariants(person.FirstName, aliasGroup))
            {
                // every row that qualifies, each with the first-name mask that hit it (at the alias level: its own alias)
                var hits = new List<(int Row, string Mask)>();
                foreach (var (row, rest) in candidates)
                {
                    if (isAlias)
                    {
                        var alias = rest.Select(t => aliasGroup!.FirstOrDefault(a => string.Equals(a, t, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(a => a is not null);
                        if (alias is not null)
                        {
                            hits.Add((row, alias.ToUpperInvariant()));
                        }
                    }
                    else if (rest.Any(t => LikePattern.IsMatch(t, firstMask)))
                    {
                        hits.Add((row, firstMask));
                    }
                }

                if (hits.Count == 0)
                {
                    continue;
                }

                var maskUsed = lastMasked || isAlias || !ReferenceEquals(firstMask, person.FirstName);
                if (!maskUsed)
                {
                    return new SheetSearch(hits.Select(h => h.Row).ToList(), lastMask, firstMask, false, null, [], hits.Select(h => h.Mask).ToList());
                }

                // the uniqueness check, once per first-name mask that hit: a competitor whose last name contains the
                // last mask and whose first name is Like the first mask takes the rows found through that mask away
                var kept = new List<(int Row, string Mask)>();
                var dropped = new List<int>();
                SearchPerson? conflict = null;
                foreach (var group in hits.GroupBy(h => h.Mask, StringComparer.Ordinal))
                {
                    var competitor = competitors.FirstOrDefault(o => LikePattern.IsMatch(o.LastName, "*" + lastMask + "*") && LikePattern.IsMatch(o.FirstName, group.Key));
                    if (competitor is null)
                    {
                        kept.AddRange(group);
                    }
                    else
                    {
                        conflict ??= competitor;
                        dropped.AddRange(group.Select(h => h.Row));
                    }
                }

                kept.Sort((a, b) => a.Row.CompareTo(b.Row));
                dropped.Sort();
                var shownFirst = kept.Count > 0 ? kept[0].Mask : hits[0].Mask;
                return new SheetSearch(kept.Select(h => h.Row).ToList(), lastMask, shownFirst, true, conflict, dropped, kept.Select(h => h.Mask).ToList());
            }

            // the last name was found but no first-name variant: the workbook went to the next source here
            return SheetSearch.None;
        }

        return SheetSearch.None;
    }

    private static IEnumerable<(string Mask, bool IsAlias)> FirstVariants(string firstName, IReadOnlyList<string>? aliasGroup)
    {
        yield return (firstName, false);
        if (aliasGroup is not null)
        {
            yield return (firstName, true);
        }

        foreach (var mask in Masks(firstName))
        {
            yield return (mask, false);
        }
    }
}
