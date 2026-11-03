using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using SatellesTSD.Core.Lookups;

namespace SatellesTSD.Infrastructure.Lookups;

/// <summary>Reads Pensionikeskus's membership-check page as the workbook read it: the lines of
/// <c>.custom-main-col</c> say whether the person has joined (or is obliged to by birth year), when the withholding
/// began or the contributions ended; the <c>table.table</c> lists the rate per period.</summary>
public static class PensionikeskusParser
{
    public const string NotJoinedPhrase = "isik ei ole liitunud kohustusliku kogumispensioniga";
    public const string JoinedPhrase = "isik on kohustusliku kogumispensioniga liitunud";
    public const string ObligedPhrase = "sünniaasta järgi kohustatud";
    public const string StartPhrase = "maksete kinnipidamise algus";
    public const string EndedPhrase = "sissemaksed lõppenud";
    public const string NoContent = "data not found";

    private static readonly HtmlParser Parser = new();

    public static LookupResult<PensionFinding> Parse(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var document = Parser.ParseDocument(html);
        var main = document.QuerySelector(".custom-main-col");
        if (main is null)
        {
            return LookupResult<PensionFinding>.Failed(NoContent, EmtaResidencyParser.Text(document.Body));
        }

        var page = EmtaResidencyParser.Text(main);
        PensionStatus? status = null;
        DateOnly? start = null;
        DateOnly? ended = null;
        foreach (var line in Lines(main))
        {
            var lower = line.ToLowerInvariant();
            if (lower.Contains(NotJoinedPhrase, StringComparison.Ordinal))
            {
                status ??= PensionStatus.NotJoined;
            }
            else if (lower.Contains(JoinedPhrase, StringComparison.Ordinal))
            {
                status ??= PensionStatus.Joined;
            }
            else if (lower.Contains(ObligedPhrase, StringComparison.Ordinal) && lower.Contains("sambaga liituma", StringComparison.Ordinal))
            {
                status ??= PensionStatus.ObligedByBirthYear;
            }
            else if (lower.Contains(StartPhrase, StringComparison.Ordinal) || lower.Contains(EndedPhrase, StringComparison.Ordinal))
            {
                // a date line without a date the parser can read was the workbook's "content error" too
                var date = LookupDates.FindAll(line).FirstOrDefault();
                if (date == default)
                {
                    return LookupResult<PensionFinding>.Failed($"content error: the date on '{line}' could not be read", page);
                }

                if (lower.Contains(StartPhrase, StringComparison.Ordinal))
                {
                    start ??= date;
                }
                else
                {
                    ended ??= date;
                }
            }
        }

        if (status is null)
        {
            return LookupResult<PensionFinding>.Failed("content error: no known answer on the page", page);
        }

        if (status != PensionStatus.NotJoined && start is null && ended is null)
        {
            // joined, yet no date to say since when or until when: not "not a member" by default
            return LookupResult<PensionFinding>.Failed("content error: the person has joined but the page names no withholding start", page);
        }

        var rates = new List<RatePeriod>();
        var table = main.QuerySelector("table.table") ?? main.QuerySelector("table");
        if (table is not null)
        {
            foreach (var row in table.QuerySelectorAll("tr"))
            {
                var cells = row.QuerySelectorAll("td").Select(EmtaResidencyParser.Text).ToList();
                if (cells.Count < 2)
                {
                    continue; // the header row
                }

                var dates = LookupDates.FindAll(cells[0]);
                var rate = LookupDates.ParseRate(cells[^1]);
                if (dates.Count == 0 || rate is null)
                {
                    return LookupResult<PensionFinding>.Failed($"content error: the rate row '{string.Join(" | ", cells)}' could not be read", page);
                }

                // "Alates d" runs from d; "d1 - d2" is closed; "Kuni d" ran up to d (the workbook would have read it as a start)
                var until = cells[0].TrimStart().StartsWith("kuni", StringComparison.OrdinalIgnoreCase) && dates.Count == 1;
                rates.Add(until
                    ? new RatePeriod(DateOnly.MinValue, dates[0], rate.Value, cells.Count > 2 ? cells[1] : null)
                    : new RatePeriod(dates[0], dates.Count > 1 ? dates[1] : null, rate.Value, cells.Count > 2 ? cells[1] : null));
            }
        }

        // the rows stay in the page's order: the first row covering a day gives its rate, as the workbook walked them
        return LookupResult<PensionFinding>.Found(new PensionFinding(status.Value, start, ended, rates), page);
    }

    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "DIV", "P", "H1", "H2", "H3", "H4", "H5", "H6", "LI", "UL", "OL", "DL", "DT", "DD", "TABLE", "THEAD", "TBODY", "TFOOT", "TR", "TD", "TH",
        "FORM", "SECTION", "ARTICLE", "HEADER", "FOOTER", "NAV", "MAIN", "BLOCKQUOTE", "PRE", "LABEL",
    };

    /// <summary>The page's lines as the workbook's innerText gave them: the text of every block element that holds no
    /// block inside it, with its inline markup (a date in bold) flattened into the line.</summary>
    private static List<string> Lines(IElement root)
    {
        var lines = new List<string>();
        foreach (var element in root.QuerySelectorAll("*").Prepend(root))
        {
            if (!BlockTags.Contains(element.TagName) || element.Children.Any(c => BlockTags.Contains(c.TagName)))
            {
                continue;
            }

            var text = EmtaResidencyParser.Text(element);
            if (text.Length > 0)
            {
                lines.Add(text);
            }
        }

        return lines;
    }
}
