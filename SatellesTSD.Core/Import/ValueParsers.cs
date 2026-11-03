using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SatellesTSD.Core.Import;

/// <summary>Amounts as banks write them: "1234.56", "-1 234,56", "1.234,56", "123,45 EUR", "−12.00", "(100,00)",
/// "100,00-". The whole cell must be one amount: a sign (before, after, or as parentheses — one of them), one
/// currency sign or code, digits in groups of three with one grouping character, at most one decimal part. "Arve 12",
/// a date ("31.10.2026") or two numbers in one cell are not amounts. Hand-written and linear: no regular expression
/// to backtrack on a run of spaces. The sign is kept; whether the row is money out is the layout's business.</summary>
public static partial class AmountText
{
    public static decimal? Parse(string? text, string? decimalSeparator = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var s = text.Replace('−', '-').Replace('–', '-');
        var n = s.Length;
        var i = 0;
        SkipSpace(s, ref i);

        var open = false;
        var negative = false;
        var signs = 0;
        var currencySeen = false;
        while (i < n)
        {
            if (s[i] == '(' && !open)
            {
                open = true;
                i++;
            }
            else if (s[i] is '-' or '+' && signs == 0)
            {
                negative = s[i] == '-';
                signs++;
                i++;
            }
            else if (!currencySeen && TryCurrency(s, ref i))
            {
                currencySeen = true;
            }
            else
            {
                break;
            }

            SkipSpace(s, ref i);
        }

        if (i >= n || !char.IsAsciiDigit(s[i]))
        {
            return null;
        }

        // the body runs from the first digit to the last digit of the run of digits and separators
        var start = i;
        var lastDigit = i;
        while (i < n && (char.IsAsciiDigit(s[i]) || s[i] is '.' or ',' or '\'' || IsSpace(s[i])))
        {
            if (char.IsAsciiDigit(s[i]))
            {
                lastDigit = i;
            }

            i++;
        }

        var body = s[start..(lastDigit + 1)];
        i = lastDigit + 1;
        SkipSpace(s, ref i);
        if (i < n && s[i] == '-')
        {
            if (signs > 0)
            {
                return null;
            }

            negative = true;
            signs++;
            i++;
            SkipSpace(s, ref i);
        }

        var close = false;
        if (i < n && s[i] == ')')
        {
            close = true;
            i++;
            SkipSpace(s, ref i);
        }

        if (i < n && !currencySeen && TryCurrency(s, ref i))
        {
            currencySeen = true;
            SkipSpace(s, ref i);
        }

        if (i < n || open != close)
        {
            return null;
        }

        if (open)
        {
            if (signs > 0)
            {
                return null;
            }

            negative = true;
        }

        var value = Number(body, decimalSeparator);
        return value is null ? null : negative ? -value : value;
    }

    /// <summary>The digits of the body with their separators checked: either no separator, or one grouping
    /// character with three digits after each, with at most one decimal part after the last '.' or ',' — the
    /// decimal one being the last separator (or the configured one).</summary>
    private static decimal? Number(string body, string? decimalSeparator)
    {
        var groups = new List<string>();
        var separators = new List<char>();
        var current = new StringBuilder();
        foreach (var c in body)
        {
            if (char.IsAsciiDigit(c))
            {
                current.Append(c);
                continue;
            }

            if (current.Length == 0)
            {
                return null; // two separators in a row
            }

            groups.Add(current.ToString());
            current.Clear();
            separators.Add(IsSpace(c) ? ' ' : c);
        }

        groups.Add(current.ToString());
        char? decimalPoint = decimalSeparator switch
        {
            "." => '.',
            "," => ',',
            _ when separators.Count > 0 && separators[^1] is '.' or ',' => separators.Take(separators.Count - 1).Contains(separators[^1]) ? null : separators[^1],
            _ => null,
        };

        if (decimalPoint is { } d)
        {
            var count = separators.Count(c => c == d);
            if (count > 1 || (count == 1 && separators[^1] != d))
            {
                return null; // a decimal part that is not the last part
            }

            if (count == 0)
            {
                decimalPoint = null;
            }
        }

        var grouping = separators.Where(c => c != decimalPoint).ToList();
        var integerGroups = decimalPoint is null ? groups : groups.Take(groups.Count - 1).ToList();
        if (grouping.Count > 0)
        {
            if (grouping.Any(c => c != grouping[0]) || integerGroups[0].Length > 3 || integerGroups.Skip(1).Any(g => g.Length != 3))
            {
                return null; // mixed grouping characters, or groups that are not thousands
            }
        }

        var integerPart = string.Concat(integerGroups);
        var fraction = decimalPoint is null ? "" : groups[^1];
        var normalized = fraction.Length == 0 ? integerPart : integerPart + "." + fraction;
        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static bool IsSpace(char c) => c is ' ' or ' ' or '\t';

    private static void SkipSpace(string s, ref int i)
    {
        while (i < s.Length && IsSpace(s[i]))
        {
            i++;
        }
    }

    /// <summary>A currency at the position: "€", "$", or a three-letter code not followed by another letter.</summary>
    private static bool TryCurrency(string s, ref int i)
    {
        if (i < s.Length && s[i] is '€' or '$')
        {
            i++;
            return true;
        }

        if (i + 3 <= s.Length && char.IsAsciiLetter(s[i]) && char.IsAsciiLetter(s[i + 1]) && char.IsAsciiLetter(s[i + 2]) && (i + 3 == s.Length || !char.IsAsciiLetter(s[i + 3])))
        {
            i += 3;
            return true;
        }

        return false;
    }

    /// <summary>The three-letter currency code in an amount cell that carries one ("123,45 EUR"), else null.</summary>
    public static string? CurrencyOf(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = CurrencyCode().Match(text);
        return match.Success ? match.Groups["code"].Value : null;
    }

    [GeneratedRegex(@"(?<![A-Za-z])(?<code>[A-Z]{3})(?![A-Za-z])")]
    private static partial Regex CurrencyCode();
}

/// <summary>Dates as the sources write them: typed ISO from Excel, "31.10.2026", "2026-10-31 14:22:05",
/// "31-10-2026", "2026-10-31T22:30:00Z", a General-formatted serial number. The workbook cut the text at the first
/// space and let the locale decide; here every accepted pattern is explicit, and a zone written into the text
/// ('Z', '+02:00') is honoured.</summary>
public static class DateText
{
    public static readonly IReadOnlyList<string> DefaultFormats =
    [
        "yyyy-MM-dd", "dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "yyyy.MM.dd", "yyyyMMdd",
    ];

    private static readonly string[] TimeTails =
    [
        "", " HH:mm:ss", " H:mm:ss", " HH:mm", " H:mm", " HH:mm:ss.FFFFFFF", "'T'HH:mm:ss", "'T'HH:mm:ss.FFFFFFF", "'T'HH:mm",
        "'T'HH:mm:ssK", "'T'HH:mm:ss.FFFFFFFK", "'T'HH:mmK", " HH:mm:ssK", " HH:mm:ss.FFFFFFFK",
    ];

    private static readonly Lazy<TimeZoneInfo> Estonia = new(() => Zone("Europe/Tallinn"));

    /// <summary>The value of a cell in the date role, or null when it is not a date. A typed date comes through as
    /// it is; a number is an Excel serial; text is tried against the layout's formats, then the defaults, each with
    /// and without a time part. A text with a zone comes back as UTC (<see cref="DateTimeKind.Utc"/>); any other
    /// value is wall time of unstated zone (<see cref="DateTimeKind.Unspecified"/>).</summary>
    /// <param name="dateOnlyFallback">Whether the text before the first space may stand alone as a bare date when the rest is not a time ("31.10.2026 kell 10"); never when the rest holds digits, and a layout in another zone must not lose its time this way.</param>
    public static DateTime? Parse(Cell cell, IReadOnlyList<string>? formats = null, bool dateOnlyFallback = true)
    {
        switch (cell.Kind)
        {
            case CellKind.Empty:
                return null;
            case CellKind.Date:
                return ParseText(cell.Text, ["yyyy-MM-dd"], dateOnlyFallback: false);
            case CellKind.Number:
                if (decimal.TryParse(cell.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is >= 20000 and < 100000)
                {
                    return DateTime.FromOADate((double)serial);
                }

                return null;
            default:
                return ParseText(cell.Text, formats, dateOnlyFallback);
        }
    }

    public static DateTime? ParseText(string? text, IReadOnlyList<string>? formats = null, bool dateOnlyFallback = true)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var cleaned = text.Trim().Replace(' ', ' ');
        var patterns = (formats ?? []).Concat(DefaultFormats).Distinct().ToList();
        foreach (var pattern in patterns)
        {
            foreach (var tail in TimeTails)
            {
                // AdjustToUniversal: a text that names its zone comes back as that instant in UTC (Kind Utc), never as the machine's local time
                if (DateTime.TryParseExact(cleaned, pattern + tail, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AdjustToUniversal, out var value))
                {
                    return value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
                }
            }
        }

        var space = cleaned.IndexOf(' ');
        if (dateOnlyFallback && space > 0 && !cleaned[space..].Any(char.IsAsciiDigit))
        {
            var head = cleaned[..space];
            foreach (var pattern in patterns)
            {
                if (DateTime.TryParseExact(head, pattern, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
                {
                    return DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
                }
            }
        }

        return null;
    }

    /// <summary>The value in Estonian time: a UTC value (a text that named its zone) is converted from UTC; wall
    /// time of a layout in another zone is converted from that zone when it carries a time (a bare date says nothing
    /// about the hour and stays); anything else is already Estonian. A time that falls in the source zone's spring
    /// gap is moved past it.</summary>
    public static DateTime ToEstonian(DateTime value, TimeZoneInfo? source)
    {
        if (value.Kind == DateTimeKind.Utc)
        {
            return TimeZoneInfo.ConvertTimeFromUtc(value, Estonia.Value);
        }

        if (source is null || value.TimeOfDay == TimeSpan.Zero)
        {
            return value;
        }

        var unspecified = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        if (source.IsInvalidTime(unspecified))
        {
            unspecified = unspecified.AddHours(1);
        }

        return TimeZoneInfo.ConvertTime(unspecified, source, Estonia.Value);
    }

    /// <summary>A time zone by IANA or Windows id; throws <see cref="TimeZoneNotFoundException"/>.</summary>
    public static TimeZoneInfo Zone(string id)
    {
        if (string.Equals(id, "UTC", StringComparison.OrdinalIgnoreCase))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException) when (id == "Europe/Tallinn")
        {
            return TimeZoneInfo.FindSystemTimeZoneById("FLE Standard Time");
        }
    }
}

/// <summary>Identifiers as the registers write them, whatever Excel made of them.</summary>
public static class IdText
{
    /// <summary>A code that may have come through Excel as a number: "38001010000" stays, 38001010000.0 becomes
    /// "38001010000", text is trimmed. Letters and leading zeros of a foreign identifier survive because text is
    /// never reinterpreted.</summary>
    public static string? Code(Cell cell)
    {
        if (cell.IsBlank)
        {
            return null;
        }

        if (cell.Kind == CellKind.Number && decimal.TryParse(cell.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return decimal.Truncate(number) == number ? decimal.Truncate(number).ToString("0", CultureInfo.InvariantCulture) : cell.Text;
        }

        return cell.Text.Trim();
    }

    /// <summary>A small integer id ("2", "2.0", 2) as its canonical text, or the trimmed text when it is not one.</summary>
    public static string? IntegerId(Cell cell)
    {
        if (cell.IsBlank)
        {
            return null;
        }

        var text = cell.Text.Trim();
        if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && decimal.Truncate(number) == number)
        {
            return decimal.Truncate(number).ToString("0", CultureInfo.InvariantCulture);
        }

        return text;
    }
}

/// <summary>The Estonian personal code (isikukood).</summary>
public static class PersonalCode
{
    /// <summary>The workbook's IsPersonCode: eleven digits, the first 1–6, digits 2–7 a real date in the century the
    /// first digit names. The check digit is not verified here, as the workbook never did.</summary>
    public static bool LooksValid(string? code)
    {
        if (code is null || code.Length != 11 || !code.All(char.IsAsciiDigit) || code[0] is < '1' or > '6')
        {
            return false;
        }

        var century = code[0] switch { '1' or '2' => 1800, '3' or '4' => 1900, _ => 2000 };
        var year = century + int.Parse(code.AsSpan(1, 2), CultureInfo.InvariantCulture);
        var month = int.Parse(code.AsSpan(3, 2), CultureInfo.InvariantCulture);
        var day = int.Parse(code.AsSpan(5, 2), CultureInfo.InvariantCulture);
        return month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month);
    }

    /// <summary>The check digit (modulo 11, two weight rounds) — a stricter test than the workbook's, for a warning.</summary>
    public static bool ChecksumValid(string? code)
    {
        if (!LooksValid(code))
        {
            return false;
        }

        int[] first = [1, 2, 3, 4, 5, 6, 7, 8, 9, 1];
        int[] second = [3, 4, 5, 6, 7, 8, 9, 1, 2, 3];
        var check = Sum(code!, first) % 11;
        if (check == 10)
        {
            check = Sum(code!, second) % 11;
            if (check == 10)
            {
                check = 0;
            }
        }

        return check == code![10] - '0';

        static int Sum(string code, int[] weights)
        {
            var sum = 0;
            for (var i = 0; i < 10; i++)
            {
                sum += (code[i] - '0') * weights[i];
            }

            return sum;
        }
    }

    public static DateOnly? BirthDate(string? code)
    {
        if (!LooksValid(code))
        {
            return null;
        }

        var century = code![0] switch { '1' or '2' => 1800, '3' or '4' => 1900, _ => 2000 };
        return new DateOnly(century + int.Parse(code.AsSpan(1, 2), CultureInfo.InvariantCulture), int.Parse(code.AsSpan(3, 2), CultureInfo.InvariantCulture), int.Parse(code.AsSpan(5, 2), CultureInfo.InvariantCulture));
    }
}
