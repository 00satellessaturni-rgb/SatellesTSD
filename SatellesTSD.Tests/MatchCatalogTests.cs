using System.Text.RegularExpressions;
using SatellesTSD.Core.Import;
using SatellesTSD.Core.Matching;

namespace SatellesTSD.Tests;

/// <summary>The embedded match catalog: the workbook's nine categories, its filter cells verbatim, its aliases.</summary>
public class MatchCatalogTests
{
    [Fact]
    public void The_default_is_the_workbooks_catalog_with_the_owners_additions_and_no_more()
    {
        var standard = MatchCatalog.Default.FilterSets["standard"];
        var workbook = WorkbookCatalog.Catalog.FilterSets["standard"];
        Assert.Equal(workbook["Palgad"].Replace("*advance payment*;", "*advance payment*,*proovip?ev*;", StringComparison.Ordinal), standard["Palgad"]);
        Assert.Equal(workbook["TÖR-väline palgad"].Replace("*advance payment*;", "*advance payment*,*proovip?ev*;", StringComparison.Ordinal), standard["TÖR-väline palgad"]);
        Assert.All(workbook.Keys.Where(k => k is not ("Palgad" or "TÖR-väline palgad")), k => Assert.Equal(workbook[k], standard[k]));
    }

    /// <summary>The CONFIG sheet as extracted into the repository (docs/reference/workbook-vba/CONFIG-sheet.txt).</summary>
    private static string ConfigCell(string cell)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "SatellesTSD.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        var line = File.ReadLines(Path.Combine(dir, "docs", "reference", "workbook-vba", "CONFIG-sheet.txt")).First(l => l.StartsWith(cell + "\t", StringComparison.Ordinal));
        return line[(cell.Length + 1)..];
    }

    [Fact]
    public void The_default_catalog_carries_the_workbooks_nine_categories_in_order()
    {
        var catalog = MatchCatalog.Default;

        Assert.Equal(["Palgad", "Dividendid", "Üür", "Kommunaalkulud", "Kompensatsioon", "Haigusleht", "Laenud", "TÖR-väline palgad", "Trahvid"], catalog.Categories.Select(c => c.Name));
        Assert.All(catalog.Categories, c => Assert.Equal(Direction.Debit, c.Side));
        Assert.Equal([true, null, null, null, true, true, null, false, null], catalog.Categories.Select(c => c.EmployeeBased));
        Assert.Equal(["Palgad"], catalog.Categories.Where(c => c.IsSalary).Select(c => c.Name));
        Assert.All(catalog.Categories, c => Assert.True(c.ReportSheet));
        Assert.Equal(["Palgad"], catalog.Categories.Where(c => !c.SubTable).Select(c => c.Name));
        Assert.Equal(["Trahvid"], catalog.Categories.Where(c => c.PayeeCheck).Select(c => c.Name));
        Assert.Equal(("uur", "haigus"), (catalog.Find("Üür")!.Code, catalog.Find("Haigusleht")!.Code));
        Assert.All(catalog.Categories.Where(c => c.Name is not ("Üür" or "Haigusleht")), c => Assert.Null(c.Code));
        Assert.Empty(catalog.Validate());
    }

    [Fact]
    public void The_filter_cells_are_the_config_sheets_verbatim()
    {
        var catalog = WorkbookCatalog.Catalog;
        var standard = catalog.FilterSets["standard"];
        var columns = new[] { ("B", "Palgad"), ("C", "Dividendid"), ("D", "Üür"), ("E", "Kommunaalkulud"), ("F", "Kompensatsioon"), ("G", "Haigusleht"), ("H", "Laenud"), ("I", "TÖR-väline palgad"), ("J", "Trahvid") };
        foreach (var (column, category) in columns)
        {
            Assert.Equal(ConfigCell(column + "58"), standard[category]); // Swedbank's row
        }

        Assert.Equal(ConfigCell("B59"), Assert.Single(catalog.FilterSets["kassa"]).Value);
        Assert.Equal((ConfigCell("B72"), ConfigCell("I72")), (catalog.FilterSets["customtable1"]["Palgad"], catalog.FilterSets["customtable1"]["TÖR-väline palgad"]));
        Assert.Equal(3, catalog.FilterSets.Count);
    }

    [Fact]
    public void Every_layout_has_a_filter_set_matching_its_config_row()
    {
        // CONFIG filter row 58 + k belongs to layout k; rows 60–71, 73 and 74 are copies of row 58
        var catalog = WorkbookCatalog.Catalog;
        var layouts = LayoutCatalog.Default.Layouts;
        Assert.Equal(layouts.Select(l => l.Id).OrderBy(i => i), catalog.LayoutFilterSets.Keys.OrderBy(i => i));
        var columns = new[] { ("B", "Palgad"), ("C", "Dividendid"), ("D", "Üür"), ("E", "Kommunaalkulud"), ("F", "Kompensatsioon"), ("G", "Haigusleht"), ("H", "Laenud"), ("I", "TÖR-väline palgad"), ("J", "Trahvid") };
        for (var k = 0; k < layouts.Count; k++)
        {
            var expectedSet = k switch { 1 => "kassa", 14 => "customtable1", _ => "standard" };
            Assert.Equal(expectedSet, catalog.LayoutFilterSets[layouts[k].Id]);
            var row = 58 + k;
            var filters = catalog.FiltersFor(layouts[k].Id)!;
            var cellsInRow = 0;
            foreach (var (column, category) in columns)
            {
                var cell = ConfigCellOrEmpty(column + row);
                cellsInRow += cell.Length > 0 ? 1 : 0;
                Assert.Equal(cell, filters.TryGetValue(category, out var list) ? list.CellText : "");
            }

            Assert.Equal(cellsInRow, filters.Count); // no cell the CONFIG row does not have
        }
    }

    [Fact]
    public void The_aliases_are_the_system_data_groups()
    {
        var expected = ConfigCell("B133").Split(';').Select(g => g.Split(',')).ToList();
        Assert.Equal(expected.Select(g => string.Join(",", g)), MatchCatalog.Default.Aliases.Select(g => string.Join(",", g)));
        Assert.Equal(["SERHII", "SERGII", "SERGEI", "SERGHEI", "SERGIY"], MatchCatalog.Default.AliasGroupOf("sergei")!);
        Assert.Null(MatchCatalog.Default.AliasGroupOf("Jaan"));
    }

    [Fact]
    public void A_document_round_trips_and_an_unusable_one_is_refused_with_every_reason()
    {
        var json = MatchCatalog.Default.ToJson();
        var parsed = MatchCatalog.Parse(json);
        Assert.Equal(MatchCatalog.Default.Categories, parsed.Categories);
        Assert.Equal(json, parsed.ToJson());

        var broken = Regex.Replace(json, "\"Name\": \"Laenud\"", "\"Name\": \"Palgad\"");
        var e = Assert.Throws<MatchException>(() => MatchCatalog.Parse(broken));
        Assert.Contains("'Palgad' is used 2 times", e.Message);

        var badSet = json.Replace("\"lhv-en\": \"standard\"", "\"lhv-en\": \"nowhere\"");
        Assert.Contains("'nowhere'", Assert.Throws<MatchException>(() => MatchCatalog.Parse(badSet)).Message);

        var badPattern = json.Replace("\"Laenud\": \"*laen*\"", "\"Laenud\": \"*laen*,[a-\"");
        Assert.Contains("never closed", Assert.Throws<MatchException>(() => MatchCatalog.Parse(badPattern)).Message);

        var badCategory = json.Replace("\"Laenud\": \"*laen*\"", "\"Laenud\": \"*laen*\", \"Muu\": \"*muu*\"");
        Assert.Contains("'Muu'", Assert.Throws<MatchException>(() => MatchCatalog.Parse(badCategory)).Message);

        Assert.Throws<MatchException>(() => MatchCatalog.Parse("{not json"));
        Assert.Throws<MatchException>(() => MatchCatalog.Parse("""{"Aliases":[["one"]]}"""));
    }

    [Fact]
    public void A_pattern_list_splits_as_the_workbook_did()
    {
        var list = PatternList.Parse("*palk*, *tasu*;*laen*,x;ignored");
        Assert.Equal(["*palk*", " *tasu*"], list.Include); // not trimmed: the workbook did not trim
        Assert.Equal(["*laen*", "x"], list.Exclude);
        Assert.Equal("*palk*, *tasu*;*laen*,x", list.CellText);
        Assert.True(list.Includes("Palk"));
        Assert.True(list.Includes(" tasu"));
        Assert.False(list.Includes("tasu"));
        Assert.True(list.Excludes("laenu tagastus"));
        Assert.True(PatternList.Parse("").IsEmpty);
        Assert.True(PatternList.Parse(null).IsEmpty);
        Assert.False(PatternList.Parse("").Includes(""));
        Assert.Equal(PatternList.Parse("a;b"), PatternList.Parse("a;b"));

        // an empty part holds no pattern (VBA's Split of "" is an empty array); an empty item inside a list stays, as in VBA
        var excludeOnly = PatternList.Parse(";*x*");
        Assert.True(excludeOnly.IsEmpty);
        Assert.Empty(excludeOnly.Include);
        Assert.Equal(["*x*"], excludeOnly.Exclude);
        Assert.False(excludeOnly.Includes(""));
        Assert.Empty(PatternList.Parse("*x*;").Exclude);
        Assert.False(PatternList.Parse("*x*;").Excludes(""));
        Assert.Equal(["a", "", "b"], PatternList.Parse("a,,b").Include);
        Assert.True(PatternList.Parse("a,,b").Includes(""));
    }

    private static string ConfigCellOrEmpty(string cell)
    {
        try
        {
            return ConfigCell(cell);
        }
        catch (InvalidOperationException)
        {
            return "";
        }
    }
}

/// <summary>The workbook's own catalog: the embedded default less the patterns the owner added since (plan item 18:
/// "proovipäev" counts as salary), for the tests that hold the catalog to the CONFIG sheet and the workbook's reports.</summary>
internal static class WorkbookCatalog
{
    public static readonly string[] OwnerAdditions = [",*proovip?ev*"];

    public static MatchCatalog Catalog { get; } = MatchCatalog.Parse(OwnerAdditions.Aggregate(MatchCatalog.Default.ToJson(), (json, added) => json.Replace(added, "", StringComparison.Ordinal)));
}
