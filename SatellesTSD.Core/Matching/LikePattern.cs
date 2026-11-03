namespace SatellesTSD.Core.Matching;

/// <summary>VBA's <c>Like</c> operator as the workbook used it under <c>Option Compare Text</c>: the whole text
/// against the whole pattern, case-insensitively. <c>?</c> is one character, <c>*</c> any run, <c>#</c> one digit,
/// <c>[list]</c> one of the listed characters and ranges, <c>[!list]</c> one character not in the list; <c>[]</c>
/// matches nothing (an empty string, as VBA has it). Hand-written — a pattern from the configuration can never make
/// the matcher backtrack for seconds the way a regex could.</summary>
public static class LikePattern
{
    /// <summary>Whether <paramref name="text"/> matches <paramref name="pattern"/> entirely. A null text is empty.</summary>
    public static bool IsMatch(string? text, string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        text ??= "";
        var t = 0;
        var p = 0;
        var starP = -1;
        var starT = -1;
        while (t < text.Length)
        {
            if (p < pattern.Length)
            {
                if (pattern[p] == '*')
                {
                    starP = p;
                    starT = t;
                    p++;
                    continue;
                }

                if (pattern[p] == '[' && TryList(pattern, p, out var end, out var empty) && empty)
                {
                    p = end; // "[]" stands for nothing and consumes nothing
                    continue;
                }

                if (MatchOne(pattern, p, text[t], out var next))
                {
                    p = next;
                    t++;
                    continue;
                }
            }

            if (starP < 0)
            {
                return false;
            }

            // the last '*' takes one more character and the rest is tried again
            p = starP + 1;
            starT++;
            t = starT;
        }

        // the text is used up: only stars (and empty lists) may remain
        while (p < pattern.Length)
        {
            if (pattern[p] == '*')
            {
                p++;
            }
            else if (pattern[p] == '[' && TryList(pattern, p, out var end, out var empty) && empty)
            {
                p = end;
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>What makes the pattern unusable: a "[" never closed, a range written downwards. An empty pattern is
    /// fine (it matches an empty text only).</summary>
    public static IReadOnlyList<string> Problems(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var problems = new List<string>();
        var p = 0;
        while (p < pattern.Length)
        {
            if (pattern[p] != '[')
            {
                p++;
                continue;
            }

            if (!TryList(pattern, p, out var end, out _))
            {
                problems.Add($"'{pattern}': the \"[\" at position {p + 1} is never closed.");
                return problems;
            }

            var i = p + 1;
            if (i < end - 1 && pattern[i] == '!')
            {
                i++;
            }

            if (i < end - 1 && pattern[i] == ']')
            {
                i++;
            }

            while (i < end - 1)
            {
                if (i + 2 < end - 1 && pattern[i + 1] == '-' && Fold(pattern[i]) > Fold(pattern[i + 2]))
                {
                    problems.Add($"'{pattern}': the range {pattern[i]}-{pattern[i + 2]} is written downwards.");
                }

                i += i + 2 < end - 1 && pattern[i + 1] == '-' ? 3 : 1;
            }

            p = end;
        }

        return problems;
    }

    private static char Fold(char c) => char.ToUpperInvariant(c);

    /// <summary>Finds the "]" that closes the list starting at <paramref name="start"/>. VBA lets "]" stand for
    /// itself only as the first item (after a leading "!"); "[]" is the empty list.</summary>
    private static bool TryList(string pattern, int start, out int end, out bool empty)
    {
        var i = start + 1;
        empty = false;
        if (i < pattern.Length && pattern[i] == ']')
        {
            end = i + 1;
            empty = true;
            return true;
        }

        if (i < pattern.Length && pattern[i] == '!')
        {
            i++;
        }

        if (i < pattern.Length && pattern[i] == ']')
        {
            i++; // a literal "]" as the first item
        }

        while (i < pattern.Length)
        {
            if (pattern[i] == ']')
            {
                end = i + 1;
                return true;
            }

            i++;
        }

        end = -1;
        return false;
    }

    /// <summary>Matches the single-character item of the pattern at <paramref name="p"/> against <paramref name="c"/>;
    /// <paramref name="next"/> is where the pattern continues when it matched.</summary>
    private static bool MatchOne(string pattern, int p, char c, out int next)
    {
        var item = pattern[p];
        next = p + 1;
        switch (item)
        {
            case '?':
                return true;
            case '#':
                return c is >= '0' and <= '9';
            case '[':
                if (!TryList(pattern, p, out var end, out var empty))
                {
                    return Fold(c) == '['; // never closed: a literal bracket
                }

                next = end;
                if (empty)
                {
                    return false;
                }

                return InList(pattern, p + 1, end - 1, c);
            default:
                return Fold(c) == Fold(item);
        }
    }

    private static bool InList(string pattern, int from, int to, char c)
    {
        var i = from;
        var negate = false;
        if (i < to && pattern[i] == '!')
        {
            negate = true;
            i++;
        }

        var found = false;
        while (i < to)
        {
            if (i + 2 < to && pattern[i + 1] == '-')
            {
                if (InRange(pattern[i], pattern[i + 2], c))
                {
                    found = true;
                }

                i += 3;
            }
            else
            {
                // a "]" is only ever the first item, a "-" at either end stands for itself
                if (Fold(pattern[i]) == Fold(c))
                {
                    found = true;
                }

                i++;
            }
        }

        return found != negate;
    }

    /// <summary>A range compares the upper-cased characters by code point: the workbook's own reports show "tn 24-51",
    /// "vi 29-15" and "ku 28-188" matched by "[a-z][a-z] [0-9]-[0-9]" and "äe 1-7" not — an accented letter lies
    /// outside a–z, as it does here.</summary>
    private static bool InRange(char lo, char hi, char c)
    {
        var f = Fold(c);
        return Fold(lo) <= f && f <= Fold(hi);
    }
}
