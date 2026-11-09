using System.Text;

namespace SatellesTSD.Core.Import;

/// <summary>Reads a delimited text file (a bank's CSV) into one sheet. The encoding is the BOM's, else UTF-8 when
/// the bytes are valid UTF-8, else the fallback: Windows-1257, the Estonian Windows code page, with Š and Ž read as
/// ISO-8859-4 writes them (Swedbank's file) — which is where the workbook's mojibake headers came from, Excel having
/// guessed differently. The delimiter is sniffed on the first lines. Fields may be quoted and span lines; the record
/// number is the row number.</summary>
public sealed class CsvTableReader : ITableReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    static CsvTableReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static Encoding Windows1257 => Encoding.GetEncoding(1257);

    public static Encoding Windows1251 => Encoding.GetEncoding(1251);

    private readonly Encoding? _fallback;
    private readonly char? _delimiter;

    /// <param name="fallback">The encoding of bytes that are not UTF-8; by default Windows-1257 with Š and Ž as ISO-8859-4 writes them.</param>
    /// <param name="delimiter">A fixed delimiter; sniffed from the header line by default.</param>
    public CsvTableReader(Encoding? fallback = null, char? delimiter = null)
    {
        _fallback = fallback;
        _delimiter = delimiter;
    }

    public SourceWorkbook Read(string fileName, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var (text, encodingName) = Decode(content, _fallback);
        var delimiter = _delimiter ?? SniffDelimiter(FirstLines(text, StatementParser.DefaultHeaderSearchRows)); // as deep as the header is looked for: a title block does not outvote the table
        var rows = ParseRecords(text, delimiter, out var unterminated);
        var sheet = new SheetTable(0, Path.GetFileNameWithoutExtension(fileName), rows);
        var warnings = unterminated
            ? new[] { $"{fileName}: a quote opened at record {rows.Count} is never closed; everything after it was read as that one field." }
            : [];
        return new SourceWorkbook(fileName, SourceFormat.Csv, [sheet], encodingName, warnings);
    }

    /// <summary>The text and the name of the encoding it was decoded with.</summary>
    public static (string Text, string Encoding) Decode(byte[] content, Encoding? fallback = null)
    {
        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
        {
            return (Encoding.UTF8.GetString(content, 3, content.Length - 3), "utf-8");
        }

        if (content.Length >= 2 && content[0] == 0xFF && content[1] == 0xFE)
        {
            return (Encoding.Unicode.GetString(content, 2, content.Length - 2), "utf-16");
        }

        if (content.Length >= 2 && content[0] == 0xFE && content[1] == 0xFF)
        {
            return (Encoding.BigEndianUnicode.GetString(content, 2, content.Length - 2), "utf-16BE");
        }

        try
        {
            return (StrictUtf8.GetString(content), "utf-8");
        }
        catch (DecoderFallbackException)
        {
            return fallback is null
                ? (SingleByteCodec.Baltic.Decode(content), SingleByteCodec.BalticName)
                : (fallback.GetString(content), fallback.WebName);
        }
    }

    /// <summary>The name of the default fallback decoding, as a stored file reports it.</summary>
    public static string BalticEncodingName => SingleByteCodec.BalticName;

    /// <summary>The delimiter that splits the header line most, among ';', ',', tab and '|' (quotes respected);
    /// ';' when nothing splits it.</summary>
    public static char SniffDelimiter(string headerLine) => SniffDelimiter([headerLine]);

    /// <summary>The delimiter that splits the first lines the same way: per candidate, the count most lines agree
    /// on (a title line above the header, a blank first line, or one data line rich in commas must not decide);
    /// the candidate with the most agreeing lines wins, then the one splitting more. ';' when nothing splits.</summary>
    public static char SniffDelimiter(IReadOnlyList<string> firstLines)
    {
        char[] candidates = [';', ',', '\t', '|'];
        var countsPerLine = new List<int[]>();
        foreach (var line in firstLines)
        {
            var counts = new int[candidates.Length];
            var quoted = false;
            foreach (var c in line)
            {
                if (c == '"')
                {
                    quoted = !quoted;
                    continue;
                }

                if (quoted)
                {
                    continue;
                }

                var at = Array.IndexOf(candidates, c);
                if (at >= 0)
                {
                    counts[at]++;
                }
            }

            countsPerLine.Add(counts);
        }

        var winner = -1;
        var winnerAgreeing = 0;
        var winnerCount = 0;
        for (var i = 0; i < candidates.Length; i++)
        {
            var nonZero = countsPerLine.Select(c => c[i]).Where(c => c > 0).ToList();
            if (nonZero.Count == 0)
            {
                continue;
            }

            var mode = nonZero.GroupBy(c => c).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First();
            if (mode.Count() > winnerAgreeing || (mode.Count() == winnerAgreeing && mode.Key > winnerCount))
            {
                winner = i;
                winnerAgreeing = mode.Count();
                winnerCount = mode.Key;
            }
        }

        return winner < 0 ? ';' : candidates[winner];
    }

    /// <summary>The first non-blank physical lines of the text.</summary>
    public static IReadOnlyList<string> FirstLines(string text, int count)
    {
        var lines = new List<string>();
        var start = 0;
        while (start < text.Length && lines.Count < count)
        {
            var end = text.IndexOfAny(['\r', '\n'], start);
            var line = end < 0 ? text[start..] : text[start..end];
            if (!string.IsNullOrWhiteSpace(line))
            {
                lines.Add(line);
            }

            if (end < 0)
            {
                break;
            }

            start = end + 1;
            if (text[end] == '\r' && start < text.Length && text[start] == '\n')
            {
                start++;
            }
        }

        return lines;
    }

    /// <summary>RFC 4180 records: a quoted field may hold the delimiter, quotes doubled, and line breaks. Trailing
    /// empty records are dropped; an empty record inside the data stays as an empty row.</summary>
    public static IReadOnlyList<IReadOnlyList<Cell>> ParseRecords(string text, char delimiter) => ParseRecords(text, delimiter, out _);

    /// <param name="unterminatedQuote">Whether the text ended inside a quoted field: the rest of the file was read as that field, which the caller should say.</param>
    public static IReadOnlyList<IReadOnlyList<Cell>> ParseRecords(string text, char delimiter, out bool unterminatedQuote)
    {
        var rows = new List<IReadOnlyList<Cell>>();
        var fields = new List<Cell>();
        var field = new StringBuilder();
        var quoted = false;
        var fieldStarted = false;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i += 2;
                        continue;
                    }

                    quoted = false;
                    i++;
                    continue;
                }

                field.Append(c);
                i++;
                continue;
            }

            if (c == '"' && !fieldStarted)
            {
                quoted = true;
                fieldStarted = true;
                i++;
                continue;
            }

            if (c == delimiter)
            {
                fields.Add(Cell.Of(field.ToString()));
                field.Clear();
                fieldStarted = false;
                i++;
                continue;
            }

            if (c == '\r' || c == '\n')
            {
                fields.Add(Cell.Of(field.ToString()));
                field.Clear();
                fieldStarted = false;
                rows.Add(Finish(fields));
                fields = [];
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                i++;
                continue;
            }

            field.Append(c);
            fieldStarted = true;
            i++;
        }

        unterminatedQuote = quoted;
        if (fieldStarted || fields.Count > 0 || field.Length > 0)
        {
            fields.Add(Cell.Of(field.ToString()));
            rows.Add(Finish(fields));
        }

        while (rows.Count > 0 && rows[^1].Count == 0)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        return rows;
    }

    private static IReadOnlyList<Cell> Finish(List<Cell> fields)
    {
        while (fields.Count > 0 && fields[^1].IsBlank)
        {
            fields.RemoveAt(fields.Count - 1);
        }

        return fields;
    }
}
