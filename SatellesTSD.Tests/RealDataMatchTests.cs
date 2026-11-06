using SatellesTSD.Core.Import;
using SatellesTSD.Core.Matching;
using SatellesTSD.Infrastructure;

namespace SatellesTSD.Tests;

/// <summary>The matcher against the workbook's own reports: for every company folder of the owner's real exports
/// (outside the repository, see <see cref="RealDataTests"/>), the register and the statements the workbook read are
/// matched for the report's month and held to the report's "Otsingu kontroll" row by row (the person found and the
/// verdict text) and to its "Vahearuanne" salary sums. The one difference allowed is the one decided: a row the
/// workbook left unmatched because the person's name was spelt otherwise than on the first row it found.</summary>
public class RealDataMatchTests
{
    private static readonly Importer Importer = new(LayoutCatalog.Default, new ExcelTableReader());

    private sealed record Comparison(string Company, int Rows, int Same, IReadOnlyList<string> Different, IReadOnlyList<string> OnlyInReport, IReadOnlyList<string> Allowed, IReadOnlyList<string> SumDifferences);

    [Fact]
    public void Every_report_of_the_workbook_is_reproduced_row_for_row()
    {
        if (RealDataTests.Folder is not { } folder)
        {
            return;
        }

        var companies = Directory.GetDirectories(folder).Where(d => Directory.GetFiles(d, "report-details-*.xlsx", SearchOption.TopDirectoryOnly).Length > 0).OrderBy(d => d).ToList();
        Assert.NotEmpty(companies);
        var comparisons = companies.Select(Compare).ToList();

        var rows = comparisons.Sum(c => c.Rows);
        Assert.True(rows > 1000, $"only {rows} report rows");
        foreach (var c in comparisons)
        {
            Assert.True(c.OnlyInReport.Count == 0, $"{c.Company}: rows the workbook listed and the app did not: {string.Join("; ", c.OnlyInReport)}");
            Assert.True(c.Different.Count == 0, $"{c.Company}: rows that differ beyond the decided allowance: {string.Join("; ", c.Different)}");
            Assert.True(c.SumDifferences.Count == 0, $"{c.Company}: salary sums that differ beyond the decided allowance: {string.Join("; ", c.SumDifferences)}");
        }

        var allowed = comparisons.Sum(c => c.Allowed.Count);
        Assert.True(allowed * 1000 <= rows * 5, $"{allowed} allowed differences in {rows} rows is more than the known handful"); // 0.5 % over all
        Assert.All(comparisons, c => Assert.True(c.Allowed.Count <= 3, $"{c.Company}: {c.Allowed.Count} allowed differences, more than the known handful: {string.Join("; ", c.Allowed)}"));
        Assert.True(comparisons.Sum(c => c.Same) + allowed == rows);
    }

    private static Comparison Compare(string folder)
    {
        var company = Path.GetFileName(folder);
        var report = Directory.GetFiles(folder, "report-details-*.xlsx", SearchOption.TopDirectoryOnly).OrderBy(f => f).First();
        var book = new ExcelTableReader().Read(Path.GetFileName(report), File.ReadAllBytes(report));
        var otsing = book.Sheets.First(s => s.Name == "Otsingu kontroll");
        var vahe = book.Sheets.First(s => s.Name == "Vahearuanne");
        var (year, month) = PeriodOf(vahe, otsing);

        // the workbook read the accountant's .xlsx copies: take those, and a CSV only where no .xlsx twin exists
        var files = Directory.GetFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f => !Path.GetFileName(f).StartsWith("report-details", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(f).StartsWith("tsd", StringComparison.OrdinalIgnoreCase))
            .Where(f => f.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || (f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) && !File.Exists(Path.ChangeExtension(f, ".xlsx"))))
            .OrderBy(f => f).ToList();
        var tor = new List<TorEntry>();
        var sources = new List<MatchSource>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var parsed = Importer.Read(Path.GetFileName(file), File.ReadAllBytes(file));
            foreach (var sheet in parsed.Sheets)
            {
                if (sheet.Tor is { } t && tor.Count == 0)
                {
                    tor.AddRange(t.Entries);
                }
                else if (sheet.Statement is { } st && seen.Add(string.Join("|", st.Rows.Where(r => r.IsPayment).Select(r => r.RowHash).Order(StringComparer.Ordinal))))
                {
                    // the accountant's copy of a headerless CSV got a header row, so the workbook's row numbers are one higher
                    var rows = st.HeaderRowNo == 0 ? st.Rows.Select(r => r with { RowNo = r.RowNo + 1 }).ToList() : st.Rows;
                    sources.Add(new MatchSource(st.DisplayName, st.Layout.Id, st.Layout.Kind, rows));
                }
            }
        }

        var persons = MatchPerson.FromTor(tor, year, month);
        var result = new PeriodMatcher(WorkbookCatalog.Catalog).Run(year, month, persons, NamedAsTheReport(sources, book, otsing));

        // the report's rows by "Dokument [row]": every (person code, verdict text) written for the row — two persons can share one
        var expected = new Dictionary<string, List<(string Code, string Text)>>(StringComparer.Ordinal);
        foreach (var row in otsing.Rows.Skip(1))
        {
            string Col(int i) => i < row.Count ? row[i].Text : "";
            if (Col(3).Length > 0)
            {
                (expected.TryGetValue(Col(3), out var list) ? list : expected[Col(3)] = []).Add((Col(0), Col(10).Replace("  ", " ")));
            }
        }

        var actual = new Dictionary<string, List<(string Code, string Text, MatchHit Hit)>>(StringComparer.Ordinal);
        foreach (var hit in result.Hits)
        {
            var code = hit.PersonKey is null ? "" : persons.First(p => p.Key == hit.PersonKey).Person.PersonalCode ?? "";
            (actual.TryGetValue(hit.Reference, out var list) ? list : actual[hit.Reference] = []).Add((code, hit.Text.Replace("  ", " "), hit));
        }

        var same = 0;
        var different = new List<string>();
        var allowed = new List<string>();
        var onlyInReport = new List<string>();
        var personsTheReportFound = expected.Values.SelectMany(l => l).Select(e => e.Code).Where(c => c.Length > 0).ToHashSet(StringComparer.Ordinal);
        foreach (var (reference, exp) in expected)
        {
            if (!actual.TryGetValue(reference, out var act))
            {
                onlyInReport.Add(reference);
                continue;
            }

            var expSet = exp.OrderBy(e => e.Code).ThenBy(e => e.Text).ToList();
            var actSet = act.Select(a => (a.Code, a.Text)).OrderBy(a => a.Code).ThenBy(a => a.Text).ToList();
            if (expSet.SequenceEqual(actSet))
            {
                same++;
            }
            else if (exp.Count == 1 && act.Count == 1 && exp[0].Code.Length == 0 && (exp[0].Text.Length == 0 || exp[0].Text == "[TÖR-väline palgad]")
                     && act[0].Code.Length > 0 && personsTheReportFound.Contains(act[0].Code))
            {
                // decision 27: the workbook took only the rows spelt like the first row it found for a person; the app takes every row that
                // qualifies at that level — so a row the workbook left unmatched may go to a person the workbook itself found elsewhere
                allowed.Add(reference);
            }
            else
            {
                different.Add($"{reference}: report [{string.Join(", ", expSet.Select(e => $"'{e.Text}'"))}] app [{string.Join(", ", actSet.Select(a => $"'{a.Text}'"))}]{(expSet.Select(e => e.Code).SequenceEqual(actSet.Select(a => a.Code)) ? "" : " (other persons)")}");
            }
        }

        foreach (var reference in actual.Keys.Where(k => !expected.ContainsKey(k)))
        {
            different.Add($"{reference}: listed here, not in the report");
        }

        // the Vahearuanne: the person × source salary sums, summed over a person's rows (a yellow duplicate row holds 0); the app's sum may
        // exceed the report's by exactly the allowed rows' amounts, nothing else
        var headers = vahe.Rows[0].Select(c => c.Text).ToList();
        var codeCol = headers.IndexOf("Töötaja isikukood");
        var firstSource = headers.IndexOf("Eesnimi") + 1;
        var totalCol = headers.IndexOf("PALK NETO KOKKU");
        var reportSums = new Dictionary<(string Code, string Source), decimal>();
        for (var r = 1; r < vahe.Rows.Count && !vahe.Rows[r].All(c => c.IsBlank); r++)
        {
            var row = vahe.Rows[r];
            var code = codeCol < row.Count ? row[codeCol].Text : "";
            for (var c = firstSource; c < totalCol; c++)
            {
                var text = c < row.Count ? row[c].Text : "";
                reportSums[(code, headers[c])] = reportSums.GetValueOrDefault((code, headers[c])) + (text.Length == 0 ? 0m : decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        var sumDifferences = new List<string>();
        var allowedSet = allowed.ToHashSet(StringComparer.Ordinal);
        foreach (var ((code, source), exp) in reportSums)
        {
            var person = persons.FirstOrDefault(p => p.Person.PersonalCode == code);
            var act = person is null ? 0m : result.SalarySum(person.Key, source);
            var surplus = person is null ? 0m : result.Hits.Where(h => h.PersonKey == person.Key && h.Source == source && allowedSet.Contains(h.Reference) && h.Counted && h.Verdict.Category is { IsSalary: true }).Sum(h => h.Amount);
            if (act != exp + surplus)
            {
                sumDifferences.Add($"{source}: a person's sum is {exp} in the report and {act} here, with {surplus} from the allowed rows");
            }
        }

        return new Comparison(company, expected.Count, same, different, onlyInReport, allowed, sumDifferences);
    }

    /// <summary>The report's month: the Kontroll-kp column, else the commonest month of the hits.</summary>
    private static (int Year, int Month) PeriodOf(SheetTable vahe, SheetTable otsing)
    {
        var headers = vahe.Rows[0].Select(c => c.Text).ToList();
        var kp = headers.IndexOf("Kontroll-kp");
        if (kp >= 0)
        {
            var text = vahe.Rows.Skip(1).Select(r => kp < r.Count ? r[kp].Text : "").FirstOrDefault(t => t.Length >= 7);
            if (text is not null && int.TryParse(text[..4], out var year) && int.TryParse(text.Substring(5, 2), out var month))
            {
                return (year, month);
            }
        }

        var commonest = otsing.Rows.Skip(1).Select(r => r.Count > 4 ? r[4].Text : "").Where(t => t.Length == 10).Select(t => t[^7..]).GroupBy(t => t).OrderByDescending(g => g.Count()).First().Key;
        return (int.Parse(commonest[^4..]), int.Parse(commonest[..2]));
    }

    /// <summary>The workbook named its sources " #k" by its file-pick order, which the folder does not record: each
    /// source is matched to the report's statement copy by its amounts and takes the report's name.</summary>
    private static IReadOnlyList<MatchSource> NamedAsTheReport(IReadOnlyList<MatchSource> sources, SourceWorkbook book, SheetTable otsing)
    {
        var documentNames = otsing.Rows.Skip(1).Select(r => r.Count > 3 ? r[3].Text : "").Where(t => t.EndsWith(']')).Select(t => t[..t.LastIndexOf(" [", StringComparison.Ordinal)]).Distinct().ToList();
        var copies = book.Sheets.Where(s => documentNames.Any(d => d.Replace("*", "") == s.Name)).ToList();
        var used = new HashSet<int>();
        var named = new List<MatchSource>();
        foreach (var source in sources)
        {
            var layout = LayoutCatalog.Default.Find(source.LayoutId)!;
            var amountCol = layout.ColumnOf(layout.Roles.Amount) - 1;
            var nameCol = layout.ColumnOf(layout.Roles.Counterparty) - 1;
            var amounts = source.Rows.Where(r => r.IsPayment).Select(r => r.Amount ?? 0m).Order().ToList();
            var copy = copies.Where(c => !used.Contains(c.Index)).FirstOrDefault(c =>
            {
                var copyAmounts = c.Rows.Skip(1)
                    .Where(row => nameCol < row.Count && row[nameCol].Text.Length > 0 && amountCol < row.Count)
                    .Select(row => AmountText.Parse(row[amountCol].Text, layout.DecimalSeparator))
                    .Where(a => a is not null).Select(a => a!.Value).Order().ToList();
                return copyAmounts.SequenceEqual(amounts);
            });
            if (copy is null)
            {
                continue; // a twin of a statement already taken (a differently named copy the workbook never saw): not a source of the report
            }

            used.Add(copy.Index);
            named.Add(source with { Name = documentNames.First(d => d.Replace("*", "") == copy.Name) });
        }

        Assert.True(used.Count == copies.Count, $"{copies.Count - used.Count} statement copies of the report found no source among the folder's files");
        return named;
    }
}
