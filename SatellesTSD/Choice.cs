namespace SatellesTSD;

/// <summary>An item of a combo box: what is stored and what is shown.</summary>
public sealed class Choice(string value, string text)
{
    public string Value { get; } = value;

    public string Text { get; } = text;

    public override string ToString() => Text;

    public static int IndexOf(IEnumerable<Choice> choices, string? value)
    {
        var i = 0;
        foreach (var choice in choices)
        {
            if (string.Equals(choice.Value, value ?? "", StringComparison.Ordinal))
            {
                return i;
            }

            i++;
        }

        return -1;
    }
}
