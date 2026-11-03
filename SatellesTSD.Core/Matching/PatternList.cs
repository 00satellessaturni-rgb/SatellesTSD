namespace SatellesTSD.Core.Matching;

/// <summary>One cell of the workbook's category filters: the patterns that make a category, a ";" and the patterns
/// that unmake it ("*palk*,*tasu*;*laen*"). Patterns are kept as written — not trimmed, since VBA's Like did not trim
/// them either ("* üür*" begins with a space on purpose) — and anything after a second ";" is ignored as the
/// workbook ignored it.</summary>
public sealed record PatternList(IReadOnlyList<string> Include, IReadOnlyList<string> Exclude)
{
    public static readonly PatternList Empty = new([], []);

    public static PatternList Parse(string? cellText)
    {
        if (string.IsNullOrEmpty(cellText))
        {
            return Empty;
        }

        // VBA's Split("", ",") is an empty array: a part with nothing in it holds no pattern, while "a,,b" keeps its empty item
        var parts = cellText.Split(';');
        return new PatternList(parts[0].Length == 0 ? [] : parts[0].Split(','), parts.Length > 1 && parts[1].Length > 0 ? parts[1].Split(',') : []);
    }

    /// <summary>The cell as the workbook wrote it.</summary>
    public string CellText => Exclude.Count == 0 ? string.Join(",", Include) : string.Join(",", Include) + ";" + string.Join(",", Exclude);

    public bool IsEmpty => Include.Count == 0;

    /// <summary>Whether any of the include patterns matches the text (never when the list is empty).</summary>
    public bool Includes(string? text) => !IsEmpty && Include.Any(p => LikePattern.IsMatch(text, p));

    public bool Excludes(string? text) => Exclude.Any(p => LikePattern.IsMatch(text, p));

    public IReadOnlyList<string> Problems() =>
        Include.Concat(Exclude).SelectMany(LikePattern.Problems).ToList();

    public bool Equals(PatternList? other) => other is not null && Include.SequenceEqual(other.Include, StringComparer.Ordinal) && Exclude.SequenceEqual(other.Exclude, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Include.Count, Exclude.Count, Include.Count > 0 ? Include[0] : "");
}
