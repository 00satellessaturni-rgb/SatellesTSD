using System.Text;

namespace SatellesTSD.Core.Import;

/// <summary>A Windows single-byte code page as a round-trippable table. The bank statements' mojibake comes from
/// Excel decoding CSV bytes with the wrong code page; undoing it needs every byte to map back to the very character
/// the decoder produced, including the C1 controls that Windows-1251 leaves undefined — so the table is built from the
/// decoder itself and patched where .NET would substitute.</summary>
internal sealed class SingleByteCodec
{
    public static readonly SingleByteCodec Cp1251 = new(1251);
    public static readonly SingleByteCodec Cp1257 = new(1257);

    /// <summary>Windows-1257 for decoding a bank's file, with the four bytes that ISO-8859-4 uses for Š, Ž, š, ž
    /// read as those letters: Swedbank's CSV writes "ŠVÕRJOV" with the ISO-8859-4 byte A9, which Windows-1257 shows as
    /// "©" — the reason the workbook turned © into Š. No bank statement holds ©, ®, ¹ or ¾. Encoding stays pure
    /// Windows-1257.</summary>
    public static readonly SingleByteCodec Baltic = new(1257, new Dictionary<byte, char> { [0xA9] = 'Š', [0xAE] = 'Ž', [0xB9] = 'š', [0xBE] = 'ž' });

    public const string BalticName = "windows-1257 (Š Ž as ISO-8859-4)";

    private readonly char[] _chars = new char[256];
    private readonly Dictionary<char, byte> _bytes = new();

    private SingleByteCodec(int codePage, IReadOnlyDictionary<byte, char>? decodeOverrides = null)
    {
        // registered here, not in a static constructor: the static fields above are built before a static constructor's body runs
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding(codePage);
        for (var b = 0; b < 256; b++)
        {
            var decoded = encoding.GetString([(byte)b]);
            var c = decoded.Length == 1 ? decoded[0] : '�';
            if (c == '�' || (c == '?' && b != '?'))
            {
                c = (char)b; // an undefined byte passes through as the control of the same value, as Windows does
            }

            _chars[b] = c;
            _bytes.TryAdd(c, (byte)b);
        }

        if (decodeOverrides is not null)
        {
            foreach (var (b, c) in decodeOverrides)
            {
                _chars[b] = c;
            }
        }
    }

    public string Decode(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            chars[i] = _chars[bytes[i]];
        }

        return new string(chars);
    }

    /// <summary>Encodes only when every character is in the code page; otherwise there is no byte string the text
    /// could have come from.</summary>
    public bool TryEncode(string text, out byte[] bytes)
    {
        bytes = new byte[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            if (!_bytes.TryGetValue(text[i], out bytes[i]))
            {
                bytes = [];
                return false;
            }
        }

        return true;
    }
}

/// <summary>Header text as a statement layout names it and as a sheet carries it. Two headers match when they are
/// the same text, or the same text seen through a wrong code page: the workbook's layouts were written from sheets
/// Excel had decoded wrongly ("Makse kuupдev" for "Makse kuupäev", "РЎС‡С‘С‚" for "Счёт"), and the app reads the same
/// banks' files both ways — correctly from a CSV, and as Excel saved them.</summary>
public static class HeaderText
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Trims ASCII whitespace only — a no-break space is part of a header ("Income tax" + U+00A0 in the
    /// workbook's own table) and must survive — and replaces a lone surrogate (a broken character in a damaged
    /// file) so that no later step throws on it.</summary>
    public static string Clean(string? text)
    {
        if (text is null)
        {
            return "";
        }

        var trimmed = text.Trim(' ', '\t', '\r', '\n');
        return HasLoneSurrogate(trimmed) ? ReplaceLoneSurrogates(trimmed) : trimmed;
    }

    /// <summary>Every spelling the text could have under a code-page mix-up: itself, itself with the mix-up undone
    /// (its Windows-1251 bytes read as UTF-8 or as Windows-1257), and itself with the mix-up applied (its UTF-8 or
    /// Windows-1257 bytes read as Windows-1251). Pure ASCII has only itself.</summary>
    public static IReadOnlySet<string> Forms(string text)
    {
        var forms = new HashSet<string>(StringComparer.Ordinal) { text };
        if (text.All(c => c < 0x80))
        {
            return forms;
        }

        var safe = HasLoneSurrogate(text) ? ReplaceLoneSurrogates(text) : text;
        if (SingleByteCodec.Cp1251.TryEncode(safe, out var bytes1251))
        {
            if (TryUtf8(bytes1251, out var utf8))
            {
                forms.Add(utf8);
            }

            forms.Add(SingleByteCodec.Baltic.Decode(bytes1251));
        }

        forms.Add(SingleByteCodec.Cp1251.Decode(Encoding.UTF8.GetBytes(safe)));
        if (SingleByteCodec.Cp1257.TryEncode(safe, out var bytes1257))
        {
            forms.Add(SingleByteCodec.Cp1251.Decode(bytes1257));
        }

        return forms;
    }

    /// <summary>The same text; or the same text seen through a code-page mix-up; or the same text with every
    /// letter the exporter could not write shown as '?' ("Kuup?ev" for "Kuupäev", as one bank's CSV has it).</summary>
    public static bool Match(string a, string b) =>
        string.Equals(a, b, StringComparison.Ordinal) || Forms(a).Overlaps(Forms(b)) || MatchWithQuestionMarks(a, b) || MatchWithQuestionMarks(b, a);

    private static bool MatchWithQuestionMarks(string written, string meant)
    {
        if (written.Length != meant.Length || !written.Contains('?') || written == meant)
        {
            return false;
        }

        for (var i = 0; i < written.Length; i++)
        {
            if (written[i] != meant[i] && !(written[i] == '?' && meant[i] >= 0x80))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool TryUtf8(byte[] bytes, out string text)
    {
        try
        {
            text = StrictUtf8.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = "";
            return false;
        }
    }

    private static bool HasLoneSurrogate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
                continue;
            }

            if (char.IsSurrogate(text[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static string ReplaceLoneSurrogates(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (char.IsHighSurrogate(chars[i]) && i + 1 < chars.Length && char.IsLowSurrogate(chars[i + 1]))
            {
                i++;
                continue;
            }

            if (char.IsSurrogate(chars[i]))
            {
                chars[i] = '�';
            }
        }

        return new string(chars);
    }
}

/// <summary>Names as the statements carry them.</summary>
public static class NameText
{
    /// <summary>The workbook's twelve letters, plus the four signs that Š Ž š ž become when Swedbank's ISO-8859-4
    /// bytes are shown as Windows-1257 (© ® ¹ ¾; the workbook knew ©).</summary>
    private static readonly (char From, char To)[] CyrillicLookalikes =
    [
        ('Д', 'Ä'), ('Р', 'Š'), ('Х', 'Õ'), ('Ц', 'Ö'), ('Ь', 'Ü'), ('Ю', 'Ž'),
        ('д', 'ä'), ('р', 'š'), ('х', 'õ'), ('ц', 'ö'), ('ь', 'ü'), ('ю', 'ž'),
        ('©', 'Š'), ('®', 'Ž'), ('¹', 'š'), ('¾', 'ž'),
    ];

    /// <summary>The workbook's CorrectUtf8SymbolCyrBalt: Estonian letters that Excel decoded as Cyrillic
    /// (Windows-1257 bytes read as Windows-1251: Д→Ä Р→Š Х→Õ Ц→Ö Ь→Ü Ю→Ž and lower case, ©→Š) put back. The
    /// workbook applied it to every name; here it applies only to text that is Latin with nothing but those twelve
    /// Cyrillic letters in it — that is what the mix-up produces, and genuine Cyrillic text always carries other
    /// letters. "Tхnu Mдgi" is fixed, "Петров" and "Иван Smith" are left alone.</summary>
    public static string FixCyrillicLookalikes(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "";
        }

        var hasLatin = false;
        var hasLookalike = false;
        foreach (var c in name)
        {
            if (IsLatinLetter(c))
            {
                hasLatin = true;
            }
            else if (c is >= 'Ѐ' and <= 'ӿ')
            {
                if (!CyrillicLookalikes.Any(m => m.From == c))
                {
                    return name; // a Cyrillic letter the mix-up never produces: genuine Cyrillic text
                }

                hasLookalike = true;
            }
            else if (c is '©' or '®' or '¹' or '¾')
            {
                hasLookalike = true;
            }
        }

        if (!hasLatin || !hasLookalike)
        {
            return name;
        }

        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            foreach (var (from, to) in CyrillicLookalikes)
            {
                if (chars[i] == from)
                {
                    chars[i] = to;
                    break;
                }
            }
        }

        return new string(chars);
    }

    /// <summary>Text that Excel decoded with the wrong code page, put back. First UTF-8 that was read as Windows-1251
    /// or Windows-1257 ("StopFire OГњ" for "StopFire OÜ", "РџРµС‚СЂРѕРІ" for "Петров"), when the text's bytes under that
    /// code page are valid UTF-8 with something non-ASCII in it — which genuine Cyrillic or Estonian text practically
    /// never is. Then Windows-1257 that was read as Windows-1251 ("OЬ DEPILЙ" for "OÜ DEPILÉ"): Latin text with
    /// Cyrillic letters in it whose bytes read as Windows-1257 give a Western or Baltic-Estonian text and nothing
    /// Lithuanian, Latvian or Polish — which is what genuine Cyrillic gives ("Иван" → "Čâŕí"). Last the workbook's
    /// own look-alike map (<see cref="FixCyrillicLookalikes"/>).</summary>
    public static string Demojibake(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        if (text.All(c => c < 0x80))
        {
            return text;
        }

        foreach (var codec in new[] { SingleByteCodec.Cp1251, SingleByteCodec.Cp1257 })
        {
            if (codec.TryEncode(text, out var bytes) && bytes.Any(b => b >= 0x80) && HeaderText.TryUtf8(bytes, out var decoded))
            {
                return decoded;
            }
        }

        if (text.Any(IsLatinLetter) && HasBalticGarbling(text) && SingleByteCodec.Cp1251.TryEncode(text, out var bytes1251))
        {
            var western = SingleByteCodec.Baltic.Decode(bytes1251);
            if (western.All(c => c < 0x80 || IsWesternOrEstonianLetter(c) || c is ' ' or '©' or '«' or '»' or '°' or '·' or '–' or '—' or '’' or '“' or '”' or '„' or '€'))
            {
                return western;
            }
        }

        return FixCyrillicLookalikes(text);
    }

    /// <summary>A Cyrillic letter, or one of the four signs (© ® ¹ ¾) that Š Ž š ž become when a Baltic file is read
    /// as Windows-1251 or Windows-1257.</summary>
    private static bool LooksGarbledFromBaltic(char c) => c is (>= 'Ѐ' and <= 'ӿ') or '©' or '®' or '¹' or '¾';

    /// <summary>Whether the text shows signs of a Baltic file read with the wrong code page: a garbled letter, or
    /// a "№" between letters — what Swedbank's š (byte B9) becomes in a Windows-1251 view ("Babit№eva"), as against
    /// a numero sign before a number ("Arve №5").</summary>
    private static bool HasBalticGarbling(string text)
    {
        if (text.Any(LooksGarbledFromBaltic))
        {
            return true;
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '№' && i > 0 && char.IsLetter(text[i - 1]) && i + 1 < text.Length && char.IsLetter(text[i + 1]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A letter of the Latin-1 block, or Š/Ž: what Estonian and Western names are made of. The letters of
    /// Latin Extended-A that Lithuanian, Latvian and Polish use (Č, Ę, Ģ, Ķ, Ļ, Ņ, Ł, Ų, …) are what genuine
    /// Cyrillic becomes when read as Windows-1257, so they say "not a mix-up".</summary>
    private static bool IsWesternOrEstonianLetter(char c) =>
        c is (>= 'À' and <= 'ÿ' and not '×' and not '÷') or 'Š' or 'š' or 'Ž' or 'ž';

    /// <summary>The text as a fingerprint sees it: Excel's garbling undone first (a no-break space may be part of
    /// it), then whitespace normalised; a text of digits and one separator read as the number Excel would make of it
    /// ("1365,1370" and "1365.137" alike, "019242216802" without its zero); and then — when signs of a Baltic
    /// mix-up remain — read once more the Baltic way, which turns every other letter a Windows-1257 file lost in
    /// Excel ("€" shown as "Ђ") back and makes genuine Cyrillic text the same (meaningless) string whichever way it
    /// arrived. For hashing only; never shown.</summary>
    public static string Canonical(string? text)
    {
        var fixedText = Normalize(Demojibake(HeaderText.Clean(text)));
        if (fixedText.Length > 0 && fixedText.Any(char.IsAsciiDigit) && fixedText.All(c => char.IsAsciiDigit(c) || c is '.' or ',' or ' '))
        {
            var compact = fixedText.Replace(" ", "");
            var separators = compact.Count(c => c is '.' or ',');
            if (separators == 1)
            {
                var at = compact.IndexOfAny(['.', ',']);
                var integerPart = compact[..at].TrimStart('0');
                var fraction = compact[(at + 1)..].TrimEnd('0');
                return (integerPart.Length > 0 ? integerPart : "0") + (fraction.Length > 0 ? "." + fraction : "");
            }

            var digits = new string(compact.Where(char.IsAsciiDigit).ToArray()).TrimStart('0');
            return digits.Length > 0 ? digits : "0";
        }

        if (HasBalticGarbling(fixedText) && SingleByteCodec.Cp1251.TryEncode(fixedText, out var bytes))
        {
            return SingleByteCodec.Baltic.Decode(bytes);
        }

        return fixedText;
    }

    /// <summary>Trimmed, no-break spaces made ordinary, runs of whitespace collapsed — what a name is compared by.
    /// The raw text stays in the row for display.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>A Latin letter: ASCII, Latin-1 or Latin Extended-A — not the micro sign or the ordinal indicators,
    /// which a code-page mix-up produces from Cyrillic bytes.</summary>
    private static bool IsLatinLetter(char c) =>
        c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= 'À' and <= 'ɏ' and not '×' and not '÷');
}
