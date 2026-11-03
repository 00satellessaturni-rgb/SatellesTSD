namespace SatellesTSD.Core.Import;

/// <summary>Value equality for the string lists the import records carry (a record compares a list by reference).</summary>
internal static class Seq
{
    public static bool Equal(IReadOnlyList<string>? a, IReadOnlyList<string>? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null || a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public static int Hash(IReadOnlyList<string>? list)
    {
        if (list is null)
        {
            return 0;
        }

        var hash = new HashCode();
        hash.Add(list.Count);
        foreach (var item in list)
        {
            hash.Add(item, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}
