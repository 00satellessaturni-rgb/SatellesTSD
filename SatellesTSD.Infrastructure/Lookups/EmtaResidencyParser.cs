using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using SatellesTSD.Core.Lookups;

namespace SatellesTSD.Infrastructure.Lookups;

/// <summary>Reads the answer page of EMTA's public non-resident query as the workbook read it: the first paragraph
/// of <c>#content-inner</c> says whether anybody was found; when somebody was, the <c>.alt-block</c> table lists
/// the periods of non-residency (a data row has an empty first cell, the start in the second, the end — or nothing —
/// in the third).</summary>
public static class EmtaResidencyParser
{
    public const string NotFoundPhrase = "otsingutulemustele vastavaid isikuid ei leitud";
    public const string FoundPhrase = "mitteresidentide registrist leitud isikud seisuga";
    public const string NoContent = "data not found";

    private static readonly HtmlParser Parser = new();

    /// <summary>Parses the page; <paramref name="queriedOn"/> is the day of the query, the finding's date when the page states none.</summary>
    public static LookupResult<ResidencyFinding> Parse(string html, DateOnly queriedOn)
    {
        ArgumentNullException.ThrowIfNull(html);
        var document = Parser.ParseDocument(html);
        var content = document.QuerySelector("#content-inner");
        if (content is null)
        {
            return LookupResult<ResidencyFinding>.Failed(NoContent, Text(document.Body));
        }

        var page = Text(content);
        var message = Text(content.QuerySelector("p"));
        var lower = message.ToLowerInvariant();
        if (lower.Contains(NotFoundPhrase, StringComparison.Ordinal))
        {
            return LookupResult<ResidencyFinding>.Found(new ResidencyFinding(queriedOn, [], []), page);
        }

        if (!lower.Contains(FoundPhrase, StringComparison.Ordinal))
        {
            return LookupResult<ResidencyFinding>.Failed("content error: no known answer on the page", page);
        }

        var asOf = LookupDates.FindAll(message).FirstOrDefault();
        var blocks = content.QuerySelectorAll(".alt-block");
        if (blocks.Length == 0)
        {
            return LookupResult<ResidencyFinding>.Failed("content error: the period table is missing", page);
        }

        var periods = new List<DatePeriod>();
        foreach (var row in Rows(blocks[0]))
        {
            var cells = Cells(row);
            if (cells.Count < 2 || cells[0].Length > 0)
            {
                continue; // a header row, or something else than a period
            }

            if (cells.Count > 3)
            {
                return LookupResult<ResidencyFinding>.Failed("table size mismatch", page);
            }

            var start = LookupDates.Parse(cells[1]);
            if (start is null)
            {
                return LookupResult<ResidencyFinding>.Failed($"content error: '{cells[1]}' is not a date", page);
            }

            DateOnly? end = null;
            if (cells.Count == 3 && cells[2].Length > 0)
            {
                end = LookupDates.Parse(cells[2]);
                if (end is null)
                {
                    return LookupResult<ResidencyFinding>.Failed($"content error: '{cells[2]}' is not a date", page);
                }
            }

            periods.Add(new DatePeriod(start.Value, end));
        }

        if (periods.Count == 0)
        {
            // the register found the person, yet no row read as a period: the page is not what the parser knows — never "resident" by default
            return LookupResult<ResidencyFinding>.Failed("content error: the register found the person but no period could be read", page);
        }

        var notes = blocks.Skip(1).Select(Text).Where(t => t.Length > 0).ToList();
        return LookupResult<ResidencyFinding>.Found(new ResidencyFinding(asOf == default ? queriedOn : asOf, periods.OrderBy(p => p.Start).ToList(), notes), page);
    }

    /// <summary>The rows of the block: its table rows, else its children.</summary>
    private static IEnumerable<IElement> Rows(IElement block)
    {
        var rows = block.QuerySelectorAll("tr");
        return rows.Length > 0 ? rows : block.Children;
    }

    private static List<string> Cells(IElement row) => row.Children.Select(Text).ToList();

    /// <summary>The element's text with the HTML's whitespace folded.</summary>
    internal static string Text(INode? node) =>
        node is null ? "" : string.Join(' ', node.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
