using SatellesTSD.Core.Import;

namespace SatellesTSD.Tests;

/// <summary>The embedded layouts: the workbook's seventeen, their JSON shape, their overrides and their validation.</summary>
public class LayoutCatalogTests
{
    private static readonly string[] WorkbookIds =
    [
        "swedbank-et", "kassa", "lhv-et-long", "lhv-et-short", "lhv-ru-long", "lhv-ru-short", "lhv-en", "seb-et", "coop-ru", "coop-et",
        "luminor-et", "luminor-ru", "paysera-ru", "source1", "customtable1", "wise", "paysera-ru-short",
    ];

    [Fact]
    public void The_default_catalog_holds_the_workbooks_seventeen_layouts_in_its_order()
    {
        var catalog = LayoutCatalog.Default;

        Assert.Equal(WorkbookIds, catalog.Layouts.Select(l => l.Id));
        Assert.Empty(LayoutCatalog.Validate(catalog.Layouts));
        Assert.Equal(3, catalog.Layouts.Count(l => l.Kind == LayoutKind.Table)); // the workbook's type-0 sources
        Assert.Equal([15, 13, 19, 16, 19, 16, 19, 15, 14, 14, 12, 12, 13, 33, 14, 23, 6], catalog.Layouts.Select(l => l.Headers.Count));
    }

    [Fact]
    public void Every_role_header_is_one_of_its_layouts_headers()
    {
        foreach (var layout in LayoutCatalog.Default.Layouts)
        {
            var roles = layout.Roles;
            foreach (var header in new[] { roles.Account, roles.Date, roles.Counterparty, roles.Amount, roles.Description, roles.DebitCredit, roles.Currency, roles.CounterpartyCode, roles.TransactionId, roles.Reference })
            {
                if (header is not null)
                {
                    Assert.True(layout.ColumnOf(header) > 0, $"{layout.Id}: '{header}'");
                }
            }

            Assert.True(layout.ColumnOf(roles.Date) > 0 && layout.ColumnOf(roles.Counterparty) > 0 && layout.ColumnOf(roles.Amount) > 0, layout.Id);
        }
    }

    [Fact]
    public void The_workbooks_particulars_are_carried()
    {
        var catalog = LayoutCatalog.Default;

        var kassa = catalog.Find("kassa")!;
        Assert.Equal(("lehe_kuup", "töötaja", "neto", "arvestuskuu", "lehe_tüüp"), (kassa.Roles.Date, kassa.Roles.Counterparty, kassa.Roles.Amount, kassa.Roles.Description, kassa.Roles.DebitCredit));
        Assert.Equal("Palgaväljamaksed", kassa.DebitMarker);
        Assert.Null(kassa.CreditMarker);

        var paysera = catalog.Find("paysera-ru")!;
        Assert.Equal(("Д", "К"), (paysera.DebitMarker, paysera.CreditMarker));
        Assert.True(paysera.AmountHasCurrency);

        var custom = catalog.Find("customtable1")!;
        Assert.Equal("Income tax ", custom.Headers[2]);
        Assert.Equal(custom.Roles.Account, custom.Roles.Description); // SheetName plays both

        Assert.Equal(DirectionRule.Sign, catalog.Find("source1")!.Direction);
        Assert.Equal("UTC", catalog.Find("source1")!.TimeZone);
        Assert.Equal(("Amount", "Payment currency"), (catalog.Find("source1")!.Roles.Amount, catalog.Find("source1")!.Roles.Currency)); // the signed amount in the account's currency, not "Orig amount"
        // only the short Paysera export assumes its direction (its "marker" is the currency column)
        Assert.Equal(["paysera-ru-short"], catalog.Layouts.Where(l => l.DirectionIsAssumed()).Select(l => l.Id));
        Assert.Equal(DirectionRule.Sign, catalog.Find("wise")!.Direction);
        Assert.Null(catalog.Find("wise")!.Roles.Account);
        Assert.Equal("EUR", catalog.Find("paysera-ru-short")!.DebitMarker);
        Assert.Equal("Валюта", catalog.Find("paysera-ru-short")!.Roles.DebitCredit);
        Assert.True(catalog.Find("seb-et")!.AllowHeaderless);
        Assert.Equal(@"^EE\d{2}10", catalog.Find("seb-et")!.HeaderlessAccountPattern);
        Assert.All(catalog.Layouts.Where(l => l.Id != "seb-et"), l => Assert.False(l.AllowHeaderless));

        // LHV's archive id is empty on card payments and sixteen digits long; its entry reference is on every row
        Assert.Equal("Kande viide", catalog.Find("lhv-et-short")!.Roles.TransactionId);
        Assert.Equal("Kande viide", catalog.Find("lhv-et-long")!.Roles.TransactionId);
        Assert.Equal("Ссылка проводки", catalog.Find("lhv-ru-long")!.Roles.TransactionId);
        Assert.Equal("Transaction reference", catalog.Find("lhv-en")!.Roles.TransactionId);
        Assert.Equal("Arhiveerimistunnus", catalog.Find("seb-et")!.Roles.TransactionId);
    }

    [Fact]
    public void An_older_stored_snapshot_without_the_newer_fields_still_reads_and_a_snapshot_without_headers_is_refused()
    {
        // the v2 shape as first written, before AllowHeaderless and HeaderlessAccountPattern existed: every later field has a default
        var older = """
            {"Id":"seb-et","Bank":"SEB","Kind":"Bank","Headers":["Kliendi konto","Dokumendi number","Kuupäev","Saaja/maksja konto","Saaja/maksja nimi","Saaja panga kood","Tühi","Deebet/Kreedit (D/C)","Summa","Viitenumber","Arhiveerimistunnus","Selgitus","Teenustasu","Valuuta","Isikukood või registrikood"],
             "Roles":{"Account":"Kliendi konto","Date":"Kuupäev","Counterparty":"Saaja/maksja nimi","Amount":"Summa","Description":"Selgitus","DebitCredit":"Deebet/Kreedit (D/C)","Currency":"Valuuta","CounterpartyCode":"Isikukood või registrikood","TransactionId":"Arhiveerimistunnus","Reference":"Viitenumber"},
             "Direction":"Markers","DebitMarker":"D","CreditMarker":"C","AmountHasCurrency":false}
            """;

        var layout = LayoutCatalog.FromJson(older);

        Assert.Equal("seb-et", layout.Id);
        Assert.False(layout.AllowHeaderless);
        Assert.Null(layout.HeaderlessAccountPattern);
        Assert.Null(layout.DateFormats);
        Assert.Equal(15, layout.ColumnOf(layout.Roles.CounterpartyCode));

        var noHeaders = """{"Id":"x","Bank":"X","Kind":"Bank","Roles":{"Date":"a","Counterparty":"b","Amount":"c"}}""";
        Assert.Contains("no Headers", Assert.Throws<ImportException>(() => LayoutCatalog.FromJson(noHeaders)).Message);
        Assert.Throws<ImportException>(() => LayoutCatalog.FromJson("""{"Id":"x","Bank":"X","Kind":"Bank","Headers":["a"]}"""));
    }

    [Fact]
    public void The_json_shape_of_a_layout_is_pinned()
    {
        var mini = new StatementLayout("mini", "Mini", LayoutKind.Bank, ["A", "B", "C"], new LayoutRoles("A", "B", "C", "C", null, null), DirectionRule.Sign);

        var json = LayoutCatalog.ToJson(mini).Replace("\r\n", "\n");

        Assert.Equal("""
            {
              "Id": "mini",
              "Bank": "Mini",
              "Kind": "Bank",
              "Headers": [
                "A",
                "B",
                "C"
              ],
              "Roles": {
                "Account": "A",
                "Date": "B",
                "Counterparty": "C",
                "Amount": "C"
              },
              "Direction": "Sign",
              "AmountHasCurrency": false,
              "AllowHeaderless": false
            }
            """.Replace("\r\n", "\n"), json);
        Assert.Equal(mini, LayoutCatalog.FromJson(json));
    }

    [Fact]
    public void Every_default_layout_survives_a_json_round_trip_and_unknown_properties_are_tolerated()
    {
        foreach (var layout in LayoutCatalog.Default.Layouts)
        {
            Assert.Equal(layout, LayoutCatalog.FromJson(LayoutCatalog.ToJson(layout)));
        }

        var withExtra = LayoutCatalog.FromJson("""{"id":"x","bank":"X","kind":"Table","headers":["Nimi","Summa","Kuupäev"],"roles":{"date":"Kuupäev","counterparty":"Nimi","amount":"Summa"},"direction":"Sign","futureField":1}""");
        Assert.Equal("x", withExtra.Id);
        Assert.Equal(LayoutKind.Table, withExtra.Kind);
        Assert.Empty(withExtra.Problems());
    }

    [Fact]
    public void An_override_replaces_the_default_with_its_id_and_a_new_id_adds()
    {
        var swedbank = LayoutCatalog.Default.Find("swedbank-et")!;
        var edited = swedbank with { DateFormats = ["dd-MM-yyyy"], Notes = "edited" };
        var added = new StatementLayout("my-bank", "My Bank", LayoutKind.Bank, ["Nimi", "Summa", "Kuupäev", "DC"], new LayoutRoles(null, "Kuupäev", "Nimi", "Summa", null, "DC"), DebitMarker: "D", CreditMarker: "K");

        var merged = LayoutCatalog.Default.WithOverrides([edited, added]);

        Assert.Equal(18, merged.Layouts.Count);
        Assert.Equal("edited", merged.Find("swedbank-et")!.Notes);
        Assert.Equal(0, merged.Layouts.ToList().FindIndex(l => l.Id == "swedbank-et")); // keeps its place
        Assert.Equal(added, merged.Find("my-bank"));
    }

    [Fact]
    public void A_catalog_that_cannot_be_used_is_refused_with_every_reason()
    {
        var lhv = LayoutCatalog.Default.Find("lhv-et-long")!;
        var sameHeaders = lhv with { Id = "lhv-copy" };
        var badRole = lhv with { Id = "bad-role", Roles = lhv.Roles with { Amount = "Puudub" } };
        var noMarkers = lhv with { Id = "no-markers", DebitMarker = null, CreditMarker = null };
        var badZone = lhv with { Id = "bad-zone", TimeZone = "Mars/Olympus" };

        var problems = LayoutCatalog.Validate([lhv, badRole, noMarkers, badZone]);
        Assert.Contains(problems, p => p.Contains("'bad-role'") && p.Contains("Puudub"));
        Assert.Contains(problems, p => p.Contains("'no-markers'") && p.Contains("neither marker"));
        Assert.Contains(problems, p => p.Contains("'bad-zone'") && p.Contains("Mars/Olympus"));

        var collision = LayoutCatalog.Validate([lhv, sameHeaders]);
        Assert.Contains(collision, p => p.Contains("same header row"));

        // two layouts that differ only in letters an exporter may write as '?' could not be told apart by such a file
        var coopLike = lhv with { Id = "coop-like", Headers = lhv.Headers.Select(h => h.Replace("Kuupäev", "Kuupöev")).ToList(), Roles = lhv.Roles with { Date = "Kuupöev" } };
        Assert.Contains(LayoutCatalog.Validate([lhv, coopLike]), p => p.Contains("'?'"));
        Assert.Empty(LayoutCatalog.Validate(LayoutCatalog.Default.Layouts)); // the seventeen defaults differ in an ASCII letter somewhere
        Assert.Contains(LayoutCatalog.Validate([lhv, lhv]), p => p.Contains("used 2 times"));
        Assert.Throws<ImportException>(() => LayoutCatalog.Default.WithOverrides([sameHeaders]));
    }

    [Fact]
    public void A_layouts_file_parses_into_a_catalog()
    {
        var json = LayoutCatalog.ToJson(LayoutCatalog.Default.Layouts.Take(2));

        var catalog = LayoutCatalog.Parse(json);

        Assert.Equal(["swedbank-et", "kassa"], catalog.Layouts.Select(l => l.Id));
    }
}
