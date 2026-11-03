using SatellesTSD.Core.Import;
using SatellesTSD.Core.Matching;

namespace SatellesTSD.Tests;

/// <summary>The workbook's DefineCategory: its verdict texts verbatim, its two passes, its payee check, its exclusions.</summary>
public class CategorizerTests
{
    private static readonly Categorizer Standard = new(MatchCatalog.Default);

    private static CategoryVerdict Define(string description, Direction direction = Direction.Debit, MatchPass pass = MatchPass.Employee, string? payee = "Mari Maasikas", string layout = "swedbank-et") =>
        Standard.Define(layout, description, payee, direction, pass);

    [Theory]
    [InlineData("Palk", "[Palgad]")]
    [InlineData("palk oktoober 2026", "[Palgad]")]
    [InlineData("Septembrikuu palk", "[Palgad]")]
    [InlineData("August 2026 tasu", "[Palgad]")]
    [InlineData("Avanss", "[Palgad]")]
    [InlineData("töötasu", "[Palgad]")]
    [InlineData("tasu", "[Palgad]")]
    [InlineData("Laenu tagastus", "[Laenud]")]
    [InlineData("Dividendid", "[Dividendid]")]
    [InlineData("Majandus", "[Kompensatsioon]")]
    [InlineData("Huvitis", "[Kompensatsioon]")]
    [InlineData("Lähetus", "[Kompensatsioon]")]
    [InlineData("Haigusleht", "[Haigusleht]")]
    [InlineData("TVL 10/2026", "[Haigusleht]")]
    [InlineData("TVL 10-2026", "viga: kategooria tuvastamine [Üür] [Kommunaalkulud] [Haigusleht]")] // "VL 10-2" is an address to the Üür and Kommunaalkulud patterns
    [InlineData("hl", "[Haigusleht]")]
    [InlineData("Suur-patarei uur", "[Üür]")]
    [InlineData("Korteri üür", "[Üür]")]
    [InlineData("Kommunaalkulud okt", "[Kommunaalkulud]")]
    [InlineData("ülekanne", "")]
    [InlineData("kul. aruanne", "")]
    [InlineData("", "")]
    public void Says_the_workbooks_text_for_a_debit_in_the_employee_pass(string description, string expected) => Assert.Equal(expected, Define(description).Text);

    [Fact]
    public void Two_categories_matching_is_the_workbooks_error_with_every_name()
    {
        var verdict = Define("Mahtra tn 24-51 üür", pass: MatchPass.Unmatched);
        Assert.Equal((VerdictKind.Ambiguous, "viga: kategooria tuvastamine [Üür] [Kommunaalkulud]"), (verdict.Kind, verdict.Text));
        Assert.Null(verdict.Category);
        Assert.Equal(["Üür", "Kommunaalkulud"], verdict.Matched);

        var three = Define("Paasiku 28-188 üürilepingu 01012025 alusel tasu", pass: MatchPass.Unmatched);
        Assert.Equal("viga: kategooria tuvastamine [Üür] [Kommunaalkulud] [TÖR-väline palgad]", three.Text);

        // the same text in the employee pass: Palgad takes the place of TÖR-väline palgad
        Assert.Equal("viga: kategooria tuvastamine [Palgad] [Üür] [Kommunaalkulud]", Define("Paasiku 28-188 üürilepingu 01012025 alusel tasu").Text);
    }

    [Fact]
    public void The_wrong_side_is_an_error_and_a_credit_never_a_category()
    {
        var verdict = Define("Laen 25/09/2026", Direction.Credit);
        Assert.Equal((VerdictKind.WrongSide, "viga: [Laenud] D/C kontroll"), (verdict.Kind, verdict.Text));
        Assert.Equal("Laenud", verdict.Category!.Name);
        Assert.Equal("viga: [Laenud] D/C kontroll", Define("Tagasi Laen", Direction.Credit).Text);
        Assert.Equal("viga: [Palgad] D/C kontroll", Define("Palk", Direction.Unknown).Text); // a one-marker layout's other rows, a bad marker
        Assert.Equal("viga: [TÖR-väline palgad] D/C kontroll", Define("PT-26-22348 7-6.1/26/0480 Palgatoetus", Direction.Credit, MatchPass.Unmatched).Text);
    }

    [Fact]
    public void The_passes_choose_between_the_employee_only_and_the_non_employee_categories()
    {
        Assert.Equal("[Palgad]", Define("Septembrikuu palk").Text);
        Assert.Equal("[TÖR-väline palgad]", Define("Septembrikuu palk", pass: MatchPass.Unmatched).Text);
        Assert.Equal("[Kompensatsioon]", Define("Majandus").Text);
        Assert.Equal("", Define("Majandus", pass: MatchPass.Unmatched).Text); // employee-only: nobody else gets it
        Assert.Equal("[Laenud]", Define("Laen", pass: MatchPass.Unmatched).Text); // both passes
        Assert.Equal("[Laenud]", Define("Laen").Text);
    }

    [Fact]
    public void An_exclude_pattern_cancels_its_category_and_the_others_stand()
    {
        Assert.Equal("[Laenud]", Define("Laenu tagastus palk").Text); // Palgad's ";*laen*" cancels Palgad; Laenud stands alone
        Assert.Equal("[Kompensatsioon]", Define("Lähetus").Text);
        Assert.Equal("[Haigusleht]", Define("Lähetus haigus").Text); // Kompensatsioon's ";*haigus*" cancels it; Haigusleht stands
        Assert.Equal("[Laenud]", Define("Laen 25/09/2026", pass: MatchPass.Unmatched, payee: "Workform OÜ").Text); // Laenud has no exclude list
        Assert.Equal("", Define("Mahtra tn 24-51 üür 10/2026", pass: MatchPass.Unmatched).Text); // "*/*" cancels both Üür and Kommunaalkulud
    }

    [Fact]
    public void The_payee_check_of_Trahvid_brings_the_category_and_an_exclude_on_the_description_still_cancels_it()
    {
        Assert.Equal("[Trahvid]", Define("ETeenindus isik 37903140300 taotlus", payee: "RAHANDUSMINISTEERIUM").Text);
        Assert.Equal("", Define("ETeenindus isik 37903140300 taotlus", payee: "Mari Maasikas").Text);
        Assert.Equal("[Trahvid]", Define("Riigilõiv", payee: "Mari Maasikas").Text); // the description alone
        Assert.Equal("[Trahvid]", Define("riigilõiv").Text);

        var json = MatchCatalog.Default.ToJson().Replace("*EUROPARK*ESTONIA*\"", "*EUROPARK*ESTONIA*;*tagastamine*\"");
        var withExclude = new Categorizer(MatchCatalog.Parse(json));
        Assert.Equal("", withExclude.Define("swedbank-et", "Tagatise tagastamine", "RAHANDUSMINISTEERIUM", Direction.Debit, MatchPass.Employee).Text);
        Assert.Equal("[Trahvid]", withExclude.Define("swedbank-et", "Tagatis", "RAHANDUSMINISTEERIUM", Direction.Debit, MatchPass.Employee).Text);
        Assert.Equal("[Trahvid]", Standard.Define("swedbank-et", "Tagatise tagastamine", "RAHANDUSMINISTEERIUM", Direction.Debit, MatchPass.Employee).Text);
    }

    [Fact]
    public void A_layout_without_filters_finds_nothing_and_a_catalog_without_categories_says_so()
    {
        var json = MatchCatalog.Default.ToJson().Replace("\"customtable1\": \"customtable1\"", "\"customtable1-x\": \"customtable1\"");
        var catalog = MatchCatalog.Parse(json);
        Assert.Null(catalog.FiltersFor("customtable1"));
        Assert.Equal(CategoryVerdict.None, new Categorizer(catalog).Define("customtable1", "Palk", "x", Direction.Debit, MatchPass.Employee));

        var kassa = Standard.Define("kassa", "2026-10-01", "Mari Maasikas", Direction.Debit, MatchPass.Employee);
        Assert.Equal("[Palgad]", kassa.Text); // Kassa's "*202*" on the arvestuskuu date
        Assert.Equal("", Standard.Define("kassa", "2026-10-01", "Mari Maasikas", Direction.Debit, MatchPass.Unmatched).Text); // Kassa has Palgad only
        Assert.Equal("[Palgad]", Standard.Define("customtable1", "Custom sheet", "Mari", Direction.Debit, MatchPass.Employee).Text);

        var empty = MatchCatalog.Parse("""{"Categories":[],"FilterSets":{},"LayoutFilterSets":{},"Aliases":[]}""");
        var verdict = new Categorizer(empty).Define("swedbank-et", "Palk", "x", Direction.Debit, MatchPass.Employee);
        Assert.Equal((VerdictKind.NoCategories, Categorizer.NoCategoriesText), (verdict.Kind, verdict.Text));
    }

    [Fact]
    public void Excels_garbling_of_a_description_is_undone_before_the_patterns_see_it()
    {
        // "Üür oktoober" read as Windows-1251 is "Ььr oktoober": no Üür pattern fits the raw text (Ь is not Ü, and "* ььr*" needs a space before it); the undone text fits "üür*"
        Assert.Equal("[Üür]", Define("Ььr oktoober", pass: MatchPass.Unmatched).Text);
        // and an exclude pattern reached only through the undone text still cancels: "Laenu põhiosa" read as Windows-1251 shows х for õ
        var json = MatchCatalog.Default.ToJson().Replace("\"Laenud\": \"*laen*\"", "\"Laenud\": \"*laen*;*põhiosa*\"");
        var withExclude = new Categorizer(MatchCatalog.Parse(json));
        Assert.Equal("", withExclude.Define("swedbank-et", "Laenu pхhiosa", "x", Direction.Debit, MatchPass.Unmatched).Text);
        Assert.Equal("[Laenud]", withExclude.Define("swedbank-et", "Laenu intress", "x", Direction.Debit, MatchPass.Unmatched).Text);
        // the raw text is tried as well: the workbook's own garbled-form pattern still works
        Assert.Equal("[Üür]", Define("Korteri ььr", pass: MatchPass.Unmatched).Text);
        Assert.Equal("[Laenud]", Define("Laenu pхhiosa: 26-014890-JK").Text);
    }

    [Fact]
    public void An_empty_part_of_a_filter_cell_holds_no_pattern_and_a_case_different_key_still_finds_its_category()
    {
        var json = MatchCatalog.Default.ToJson().Replace("\"Laenud\": \"*laen*\"", "\"Laenud\": \";*laen*\"").Replace("\"Dividendid\": \"*dividen*\"", "\"dividendid\": \"*dividen*;\"");
        var catalog = MatchCatalog.Parse(json);
        var categorizer = new Categorizer(catalog);

        Assert.Equal("", categorizer.Define("swedbank-et", "", "x", Direction.Debit, MatchPass.Unmatched).Text); // an exclude-only cell includes nothing, not the empty description
        Assert.Equal("", categorizer.Define("swedbank-et", "Laen", "x", Direction.Debit, MatchPass.Unmatched).Text);
        Assert.Equal("[Dividendid]", categorizer.Define("swedbank-et", "Dividendid", "x", Direction.Debit, MatchPass.Unmatched).Text); // the key's case does not matter, as in VBA; a trailing ";" excludes nothing

        var colliding = MatchCatalog.Default.ToJson().Replace("\"Dividendid\": \"*dividen*\"", "\"Dividendid\": \"*dividen*\", \"dividendid\": \"*div*\"");
        Assert.Contains("2 times", Assert.Throws<MatchException>(() => MatchCatalog.Parse(colliding)).Message);
    }

    [Fact]
    public void A_group_once_matched_is_not_tried_again()
    {
        var json = MatchCatalog.Default.ToJson()
            .Replace("\"Name\": \"Üür\",", "\"Name\": \"Üür\", \"Group\": \"ruum\",")
            .Replace("\"Name\": \"Kommunaalkulud\",", "\"Name\": \"Kommunaalkulud\", \"Group\": \"ruum\",");
        var grouped = new Categorizer(MatchCatalog.Parse(json));
        Assert.Equal("[Üür]", grouped.Define("swedbank-et", "Mahtra tn 24-51 üür", "x", Direction.Debit, MatchPass.Unmatched).Text);
    }
}
