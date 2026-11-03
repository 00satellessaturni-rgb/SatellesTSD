using SatellesTSD.Core.Import;

namespace SatellesTSD.Tests;

/// <summary>Recognising a statement sheet and reading its rows, layout by layout.</summary>
public class StatementParserTests
{
    private static readonly LayoutCatalog Catalog = LayoutCatalog.Default;

    /// <summary>The header rows exactly as the workbook's CONFIG sheet holds them where Excel had decoded the bank's
    /// file with the wrong code page — what an .xlsx saved from such a sheet still carries.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> WorkbookMojibakeHeaders = new Dictionary<string, string[]>
    {
        ["swedbank-et"] =
        [
            "Kliendi konto", "Dok nr", "Makse kuupдev", "Saaja/maksja konto", "Saaja/maksja nimi", "Saaja/maksja pank", "Tьhi (ei kasutata)",
            "Deebet/Kreedit (D/C)", "Summa", "Viitenumber", "Arhiveerimistunnus", "Selgitus", "Teenustasu", "VALUUTA TДHIS", "Isikukood / Reg. nr",
        ],
        ["luminor-et"] =
        [
            "Konto", "Dokumendi number", "VГ¤Г¤rtuspГ¤ev", "Saaja/maksja konto", "Saaja/maksja nimi", "Deebet/krediit", "Summa", "Viitenumber",
            "Arhiveerimiskood", "Makse andmed", "Valuuta", "Registrikood/isikukood",
        ],
        ["luminor-ru"] =
        [
            "РЎС‡С‘С‚", "РќРѕРјРµСЂ РґРѕРєСѓРјРµРЅС‚Р°", "Р”Р°С‚Р° Р·Р°С‡РёСЃР»РµРЅРёСЏ", "РЎС‡РµС‚ РїРѕР»СѓС‡Р°С‚РµР»СЏ / РїР»Р°С‚РµР»СЊС‰РёРєР°",
            "Р\u0098РјСЏ РїРѕР»СѓС‡Р°С‚РµР»СЏ / РїР»Р°С‚РµР»СЊС‰РёРєР°", "Р”РµР±РµС‚ / РљСЂРµРґРёС‚", "CyРјРјa", "РќoРјeСЂ ccС‹Р»РєРё", "РљРѕРґ Р°СЂС…РёРІР°С†РёРё",
            "Р”РµС‚Р°Р»Рё РїР»Р°С‚РµР¶Р°", "Р’Р°Р»СЋС‚Р°", "Р РµРіРёСЃС‚СЂРѕРІС‹Р№ / Р›РёС‡РЅС‹Р№ РєРѕРґ",
        ],
        ["customtable1"] =
        [
            "Name", "Surname", "Income tax ", "Social tax", "Amount to declare", "Travel", "FirstLastFormat", "SumFormat", "DateFormat", "NameSearch", "Salary", "Date", "D/C", "SheetName",
        ],
    };

    public static IEnumerable<object[]> LayoutIds() => Catalog.Layouts.Select(l => new object[] { l.Id });

    public static IEnumerable<object[]> MojibakeIds() => WorkbookMojibakeHeaders.Keys.Select(id => new object[] { id });

    private static string[] DataRowFor(StatementLayout layout)
    {
        var row = new string[layout.Headers.Count];
        Array.Fill(row, "x");
        row[layout.ColumnOf(layout.Roles.Date) - 1] = "01.10.2026";
        row[layout.ColumnOf(layout.Roles.Counterparty) - 1] = "Mari Maasikas";
        row[layout.ColumnOf(layout.Roles.Amount) - 1] = layout.AmountHasCurrency ? "100,00 EUR" : "-100,00";
        if (layout.Roles.DebitCredit is { } dc)
        {
            row[layout.ColumnOf(dc) - 1] = layout.DebitMarker ?? "";
        }

        return row;
    }

    [Theory]
    [MemberData(nameof(LayoutIds))]
    public void A_sheet_with_a_layouts_own_header_row_is_that_layout(string id)
    {
        var layout = Catalog.Find(id)!;
        var sheet = Tables.Sheet(layout.Headers.ToArray(), DataRowFor(layout));

        var result = StatementParser.Detect(sheet, Catalog);

        Assert.True(result.Recognised, string.Join("; ", result.Differences));
        Assert.Equal(id, result.Match!.Layout.Id);
        Assert.Equal(1, result.Match.HeaderRowNo);

        var parsed = StatementParser.Parse(sheet, result.Match);
        var row = Assert.Single(parsed.Rows);
        Assert.Equal(new DateOnly(2026, 10, 1), row.Date);
        Assert.Equal("Mari Maasikas", row.Counterparty);
        Assert.Equal(layout.AmountHasCurrency ? 100m : -100m, row.Amount);
        Assert.Equal(Direction.Debit, row.Direction);
        Assert.Null(row.Problem);
    }

    [Theory]
    [MemberData(nameof(MojibakeIds))]
    public void A_sheet_with_the_header_row_as_excel_garbled_it_is_still_that_layout(string id)
    {
        var layout = Catalog.Find(id)!;
        var headers = WorkbookMojibakeHeaders[id];
        Assert.Equal(layout.Headers.Count, headers.Length);
        for (var i = 0; i < headers.Length; i++)
        {
            Assert.True(HeaderText.Match(headers[i], layout.Headers[i]), $"{id} column {i + 1}: '{headers[i]}' vs '{layout.Headers[i]}'");
        }

        var result = StatementParser.Detect(Tables.Sheet(headers, DataRowFor(layout)), Catalog);

        Assert.Equal(id, result.Match?.Layout.Id);
    }

    [Fact]
    public void The_column_count_tells_seb_from_the_long_lhv_export()
    {
        var seb = Catalog.Find("seb-et")!;
        var lhv = Catalog.Find("lhv-et-long")!;
        Assert.Equal(seb.Headers, lhv.Headers.Take(15));

        Assert.Equal("seb-et", StatementParser.Detect(Tables.Sheet(seb.Headers.ToArray(), DataRowFor(seb)), Catalog).Match?.Layout.Id);
        Assert.Equal("lhv-et-long", StatementParser.Detect(Tables.Sheet(lhv.Headers.ToArray(), DataRowFor(lhv)), Catalog).Match?.Layout.Id);
    }

    [Fact]
    public void A_header_row_below_a_title_is_found_and_the_row_numbers_stay_the_sheets()
    {
        var lhv = Catalog.Find("lhv-et-short")!;
        var sheet = Tables.Sheet(Tables.Row("Konto väljavõte"), Tables.Row(), lhv.Headers.ToArray(), DataRowFor(lhv));

        var result = StatementParser.Detect(sheet, Catalog);

        Assert.Equal(3, result.Match?.HeaderRowNo);
        Assert.Equal(4, Assert.Single(StatementParser.Parse(sheet, result.Match!).Rows).RowNo);
    }

    [Fact]
    public void An_unknown_header_row_names_the_nearest_layout_and_the_differences()
    {
        var lhv = Catalog.Find("lhv-et-long")!;
        var headers = lhv.Headers.ToList();
        headers[8] = "Summa (EUR)";
        headers.Add("Uus veerg");

        var result = StatementParser.Detect(Tables.Sheet(headers.ToArray(), Tables.Row("x")), Catalog);

        Assert.False(result.Recognised);
        Assert.Equal("lhv-et-long", result.NearestLayoutId);
        Assert.Contains(result.Differences, d => d.Contains("20 header columns") && d.Contains("19"));
        Assert.Contains(result.Differences, d => d.Contains("column 9") && d.Contains("Summa (EUR)"));

        var nothingAlike = StatementParser.Detect(Tables.Sheet(Tables.Row("Foo", "Bar"), Tables.Row("1", "2")), Catalog);
        Assert.False(nothingAlike.Recognised);
        Assert.Null(nothingAlike.NearestLayoutId);
    }

    [Fact]
    public void A_headerless_seb_export_is_recognised_by_its_shape_and_read_from_row_one()
    {
        var sheet = Tables.Sheet(
            Tables.Row("EE591010220215133228", "TM168358936", "01.10.2026", "", "AS SEB PANK", "EEUHEE2X", "H", "C", "1,01", "", "RO4248947827L02", "intresside väljamakse EUR", "0", "EUR", ""),
            Tables.Row("EE591010220215133228", "TT", "02.10.2026", "EE497700771001131757", "StopFire OÜ", "LHVBEE22", "H", "D", "500,00", "", "RO4262101896L02", "Reserv SEB", "0", "EUR", ""));

        var result = StatementParser.Detect(sheet, Catalog);

        Assert.Equal("seb-et", result.Match?.Layout.Id);
        Assert.True(result.Match!.Headerless);
        var parsed = StatementParser.Parse(sheet, result.Match);
        Assert.Equal([1, 2], parsed.Rows.Select(r => r.RowNo));
        Assert.Equal([Direction.Credit, Direction.Debit], parsed.Rows.Select(r => r.Direction));
        Assert.Equal(500m, parsed.Rows[1].Amount);
        Assert.Equal("SEB *3228", parsed.DisplayName);

        // the same shape with no date in the date column is not SEB
        var notSeb = Tables.Sheet(Tables.Row("EE59", "x", "eile", "", "N", "", "", "C", "1,01", "", "", "", "0", "EUR", ""));
        Assert.False(StatementParser.Detect(notSeb, Catalog).Recognised);
    }

    [Fact]
    public void A_bank_sheet_is_read_with_typed_cells_skipped_rows_and_problems()
    {
        var lhv = Catalog.Find("lhv-et-short")!;
        var sheet = Tables.Typed(
            lhv.Headers.Select(Cell.Of).ToArray(),
            [Cell.Of("EE497700771001131757"), Cell.Number(14136m), Cell.Date(new DateTime(2026, 10, 1)), Cell.Of("EE615100005100117393"), Cell.Of("Aktsiaselts Onninen"), Cell.Of("D"), Cell.Number(-201.09m), Cell.Of("638379446"), Cell.Number(2026092863975588m), Cell.Of("Arve 63837944"), Cell.Of("EUR"), Cell.Empty, Cell.Of("OKOYEE2XXXX"), Cell.Empty, Cell.Number(1563975588m)],
            [Cell.Of("EE497700771001131757"), Cell.Empty, Cell.Date(new DateTime(2026, 10, 2)), Cell.Empty, Cell.Empty, Cell.Of("D"), Cell.Number(-0.38m), Cell.Empty, Cell.Empty, Cell.Of("Teenustasu")],
            [],
            [Cell.Of("EE497700771001131757"), Cell.Empty, Cell.Of("eile"), Cell.Of("EE12"), Cell.Of("Mari Maasikas"), Cell.Of("d"), Cell.Of("palju"), Cell.Empty, Cell.Empty, Cell.Of("palk"), Cell.Of("EUR"), Cell.Number(48712219406m)],
            [Cell.Of("EE497700771001131757"), Cell.Empty, Cell.Of("2026-10-15"), Cell.Of("EE12"), Cell.Of("Jaan Tamm"), Cell.Of("C "), Cell.Of("1 000,50"), Cell.Empty, Cell.Empty, Cell.Of("tagasimakse"), Cell.Of("EUR")],
            [Cell.Empty, Cell.Empty, Cell.Empty, Cell.Empty, Cell.Empty, Cell.Empty, Cell.Number(1234.56m), Cell.Empty, Cell.Empty, Cell.Of("Lõppsaldo")]);

        var parsed = StatementParser.Parse(sheet, StatementParser.Detect(sheet, Catalog).Match!);

        Assert.Equal("LHV pank *1757", parsed.DisplayName);
        Assert.Equal("EE497700771001131757", parsed.Account); // the last non-empty account cell, as the workbook took it
        Assert.Equal([2, 3, 4, 5, 6, 7], parsed.Rows.Select(r => r.RowNo));
        Assert.Equal([null, SkipReason.NoName, SkipReason.Blank, null, null, SkipReason.NoName], parsed.Rows.Select(r => r.SkipReason));
        Assert.Equal(3, parsed.Payments.Count);

        var onninen = parsed.Rows[0];
        Assert.Equal(((DateOnly?)new DateOnly(2026, 10, 1), (decimal?)-201.09m, Direction.Debit, "EUR", "638379446", "1563975588"), (onninen.Date, onninen.Amount, onninen.Direction, onninen.Currency, onninen.Reference, onninen.TransactionId)); // LHV's entry reference, present on every row
        Assert.Equal(64, onninen.RowHash.Length);
        Assert.Equal("14136", onninen.Cells[1]);
        Assert.Equal("2026092863975588", onninen.Cells[8]); // the archive id stays in the cells, whole
        Assert.Equal(15, onninen.Cells.Count);

        var fee = parsed.Rows[1];
        Assert.Equal("Teenustasu", fee.Description);
        Assert.Equal("-0.38", fee.AmountText);

        var bad = parsed.Rows[3];
        Assert.Null(bad.Date);
        Assert.Null(bad.Amount);
        Assert.Equal(Direction.Debit, bad.Direction); // 'd' matches 'D', as in the workbook
        Assert.Equal("48712219406", bad.CounterpartyCode);
        Assert.Contains("eile", bad.Problem);
        Assert.Contains("palju", bad.Problem);
        Assert.Equal(2, parsed.Problems.Count(p => p.RowNo == 5));

        var tamm = parsed.Rows[4];
        Assert.Equal(1000.50m, tamm.Amount);
        Assert.Equal(Direction.Credit, tamm.Direction); // 'C ' trimmed
        Assert.NotEqual(onninen.RowHash, tamm.RowHash);
        Assert.Equal(new DateOnly(2026, 10, 1), parsed.FirstDate);
        Assert.Equal(new DateOnly(2026, 10, 15), parsed.LastDate);
    }

    [Fact]
    public void The_row_fingerprint_is_the_same_from_the_banks_csv_text_and_from_an_excel_copy()
    {
        var lhv = Catalog.Find("lhv-et-short")!;
        var fromCsv = Tables.Sheet(lhv.Headers.ToArray(),
            Tables.Row("EE497700771001131757", "14136", "2026-10-01", "EE615100005100117393", "Aktsiaselts Onninen", "D", "-201.09", "638379446", "2026092863975588", "Arve 63837944", "EUR", "", "OKOYEE2XXXX", "", "1563975588"));
        var fromXlsx = Tables.Typed(lhv.Headers.Select(Cell.Of).ToArray(),
            [Cell.Of("EE497700771001131757"), Cell.Number(14136m), Cell.Date(new DateTime(2026, 10, 1)), Cell.Of("EE615100005100117393"), Cell.Of("Aktsiaselts Onninen"), Cell.Of("D"), Cell.Number(-201.09m), Cell.Number(638379446m), Cell.Number(2026092863975590m), Cell.Of("Arve 63837944"), Cell.Of("EUR"), Cell.Empty, Cell.Of("OKOYEE2XXXX"), Cell.Empty, Cell.Number(1563975588m)]);
        var garbled = Tables.Sheet(lhv.Headers.ToArray(),
            Tables.Row("EE497700771001131757", "14136", "01.10.2026", "EE615100005100117393", "Aktsiaselts Onninen", "D", "-201,09", "638379446", "2026092863975588", "Arve 63837944", "EUR", "", "OKOYEE2XXXX", "", "1563975588"));

        var a = StatementParser.Parse(fromCsv, StatementParser.Detect(fromCsv, Catalog).Match!).Rows[0];
        var b = StatementParser.Parse(fromXlsx, StatementParser.Detect(fromXlsx, Catalog).Match!).Rows[0];
        var c = StatementParser.Parse(garbled, StatementParser.Detect(garbled, Catalog).Match!).Rows[0];

        Assert.Equal(a.RowHash, b.RowHash); // a typed date and a double hash like the bank's text; Excel's rounding of the archive id does not matter
        Assert.Equal(a.RowHash, c.RowHash); // nor does the date or decimal convention

        var swedbank = Catalog.Find("swedbank-et")!;
        var csv = Tables.Sheet(swedbank.Headers.ToArray(), Tables.Row("EE162200221054367099", "26034", "08-10-2026", "EE497700771001131757", "StopFire OÜ", "689", "", "D", "-8500,00", "", "2026100802767783", "Firma SM", "0", "EUR", ""));
        var excel = Tables.Sheet(StatementParserTests.WorkbookMojibakeHeaders["swedbank-et"], Tables.Row("EE162200221054367099", "26034", "2026-10-08", "EE497700771001131757", "StopFire OЬ", "689", "", "D", "-8500", "", "2026100802767780", "Firma SM", "0", "EUR", ""));
        var fromBank = StatementParser.Parse(csv, StatementParser.Detect(csv, Catalog).Match!).Rows[0];
        var fromExcel = StatementParser.Parse(excel, StatementParser.Detect(excel, Catalog).Match!).Rows[0];
        Assert.Equal(fromBank.RowHash, fromExcel.RowHash); // the garbled name is undone for the fingerprint, the raw text stays in the row
        Assert.Equal("StopFire OЬ", fromExcel.Counterparty);
    }

    [Fact]
    public void Markers_are_read_through_the_same_code_page_forms_as_the_headers()
    {
        // a Paysera CSV (UTF-8) that Excel opened as Windows-1251: headers and markers alike come out garbled
        var paysera = Catalog.Find("paysera-ru")!;
        static string Garble(string s) => s.All(c => c < 0x80) ? s : HeaderText.Forms(s).First(f => f.Length > s.Length); // the UTF-8 bytes read as Windows-1251: two characters per letter
        var headers = paysera.Headers.Select(Garble).ToArray();
        var sheet = Tables.Sheet(headers,
            Tables.Row(Garble("Перевод"), "1", "ABC123", "2026-10-05 10:15:00", Garble("Иван Петров"), "LT123", "", "123,45 EUR", "EUR", Garble("зарплата"), "", Garble("Д"), "1000,00"),
            Tables.Row(Garble("Перевод"), "1", "ABC124", "2026-10-06 10:15:00", "Klient", "LT124", "", "50,00 EUR", "EUR", "arve", "", Garble("К"), "1050,00"),
            Tables.Row(Garble("Перевод"), "1", "ABC125", "2026-10-07 10:15:00", "Keegi", "LT125", "", "1,00 EUR", "EUR", "?", "", "X", "1051,00"));

        var detection = StatementParser.Detect(sheet, Catalog);
        Assert.Equal("paysera-ru", detection.Match?.Layout.Id);
        Assert.False(detection.Match!.Exact);

        var parsed = StatementParser.Parse(sheet, detection.Match);
        Assert.Equal([Direction.Debit, Direction.Credit, Direction.Unknown], parsed.Rows.Select(r => r.Direction));
        Assert.Contains(parsed.Problems, p => p.Code == ImportProblem.BadMarker && p.RowNo == 4); // a two-marker layout's cell with neither marker is said
        Assert.DoesNotContain(parsed.Problems, p => p.Code == ImportProblem.BadMarker && p.RowNo != 4);
        Assert.Equal("Иван Петров", NameText.Demojibake(parsed.Rows[0].Counterparty));
    }

    [Fact]
    public void A_one_marker_layout_says_nothing_about_rows_that_are_simply_not_payouts()
    {
        var kassa = Catalog.Find("kassa")!;
        var sheet = Tables.Sheet(kassa.Headers.ToArray(),
            Tables.Row("Kassa 1", "05.10.2026", "2026-09", "KVO", "Avansid", "Jaan Tamm", "EE13", "", "", "", "", "", "100"));

        var parsed = StatementParser.Parse(sheet, StatementParser.Detect(sheet, Catalog).Match!);

        Assert.Equal(Direction.Unknown, parsed.Rows[0].Direction);
        Assert.Empty(parsed.Problems);
    }

    [Fact]
    public void A_second_header_row_inside_the_data_is_skipped_and_a_date_in_the_amount_column_is_not_money()
    {
        var lhv = Catalog.Find("lhv-et-short")!;
        var sheet = Tables.Typed(
            lhv.Headers.Select(Cell.Of).ToArray(),
            [Cell.Of("EE497700771001131757"), Cell.Empty, Cell.Date(new DateTime(2026, 10, 5)), Cell.Of("EE12"), Cell.Of("Mari Maasikas"), Cell.Of("D"), Cell.Number(-100m), Cell.Empty, Cell.Empty, Cell.Of("palk")],
            lhv.Headers.Select(Cell.Of).ToArray(),
            [Cell.Of("EE497700771001131757"), Cell.Empty, Cell.Date(new DateTime(2026, 11, 5)), Cell.Of("EE12"), Cell.Of("Jaan Tamm"), Cell.Of("D"), Cell.Date(new DateTime(2026, 5, 12)), Cell.Empty, Cell.Empty, Cell.Of("palk")]);

        var parsed = StatementParser.Parse(sheet, StatementParser.Detect(sheet, Catalog).Match!);

        Assert.Equal([null, SkipReason.Header, null], parsed.Rows.Select(r => r.SkipReason));
        Assert.Equal("EE497700771001131757", parsed.Account);
        var jaan = parsed.Rows[2];
        Assert.Null(jaan.Amount); // Excel turned "12.05" into 12 May; the year is not an amount
        Assert.Contains(parsed.Problems, p => p.Code == ImportProblem.BadAmount && p.RowNo == 4);
    }

    [Fact]
    public void A_header_whose_letters_the_bank_wrote_as_question_marks_still_matches_and_is_never_taken_for_data()
    {
        // one bank's CSV, as the owner's files have it: every non-ASCII letter of the header shown as '?'
        var seb = Catalog.Find("seb-et")!;
        var headers = seb.Headers.Select(h => new string(h.Select(c => c >= 0x80 ? '?' : c).ToArray())).ToArray();
        Assert.Contains("Kuup?ev", headers);
        var sheet = Tables.Sheet(headers,
            Tables.Row("EE591010220215133228", "TT", "02.10.2026", "EE497700771001131757", "StopFire OÜ", "LHVBEE22", "H", "D", "500,00", "", "RO4262101896L02", "Reserv SEB", "0", "EUR", ""));

        var detection = StatementParser.Detect(sheet, Catalog);

        Assert.Equal("seb-et", detection.Match?.Layout.Id);
        Assert.Equal(1, detection.Match!.HeaderRowNo); // the header row, not a headerless read from row 1
        Assert.False(detection.Match.Exact);
        Assert.Equal(["StopFire OÜ"], StatementParser.Parse(sheet, detection.Match).Payments.Select(r => r.Counterparty));

        Assert.True(HeaderText.Match("Kuup?ev", "Kuupäev"));
        Assert.False(HeaderText.Match("Kuup?ev", "Kuupaev")); // a '?' stands for a letter the exporter could not write, never for an ASCII one
        Assert.False(HeaderText.Match("Kuup?ev", "Kuupäeva"));
    }

    [Fact]
    public void A_headerless_file_of_another_bank_with_sebs_shape_is_not_seb()
    {
        var swedbankLike = Tables.Sheet(
            Tables.Row("EE162200221054367099", "26034", "08.10.2026", "EE4977", "StopFire OÜ", "689", "", "D", "-8500,00", "", "2026100802767783", "Firma SM", "0", "EUR", ""));
        Assert.False(StatementParser.Detect(swedbankLike, Catalog).Recognised); // Swedbank's bank code is 22, not SEB's 10

        // SEB's file whose first line is a fee line without a marker is still found through the next lines
        var seb = Tables.Sheet(
            Tables.Row("EE591010220215133228", "", "01.10.2026", "", "AS SEB PANK", "", "", "", "0,50", "", "", "Konto kuutasu", "0", "EUR", ""),
            Tables.Row("EE591010220215133228", "TT", "02.10.2026", "EE497700771001131757", "StopFire OÜ", "LHVBEE22", "H", "D", "500,00", "", "RO4262101896L02", "Reserv SEB", "0", "EUR", ""));
        var detection = StatementParser.Detect(seb, Catalog);
        Assert.Equal("seb-et", detection.Match?.Layout.Id);
        var parsed = StatementParser.Parse(seb, detection.Match!);
        Assert.Equal([Direction.Unknown, Direction.Debit], parsed.Rows.Select(r => r.Direction));
        Assert.Empty(parsed.Problems); // an empty marker cell is not a wrong marker
    }

    [Fact]
    public void Paysera_reads_the_amount_with_its_currency_and_cyrillic_markers()
    {
        var paysera = Catalog.Find("paysera-ru")!;
        var sheet = Tables.Sheet(
            paysera.Headers.ToArray(),
            Tables.Row("Перевод", "1", "ABC123", "2026-10-05 10:15:00", "Mari Maasikas", "LT123", "", "123,45 EUR", "EUR", "palk", "", "Д", "1000,00"),
            Tables.Row("Перевод", "1", "ABC124", "2026-10-06 10:15:00", "Klient OÜ", "LT124", "", "50,00 EUR", "EUR", "arve", "", "К", "1050,00"),
            Tables.Row("Перевод", "1", "ABC125", "2026-10-07 10:15:00", "Keegi", "LT125", "", "1,00 EUR", "", "?", "", "D", "1051,00")); // no currency column: taken from the amount

        var parsed = StatementParser.Parse(sheet, StatementParser.Detect(sheet, Catalog).Match!);

        Assert.Equal("Paysera", parsed.DisplayName);
        Assert.Equal([123.45m, 50m, 1m], parsed.Rows.Select(r => r.Amount));
        Assert.Equal(["EUR", "EUR", "EUR"], parsed.Rows.Select(r => r.Currency));
        Assert.Equal([Direction.Debit, Direction.Credit, Direction.Unknown], parsed.Rows.Select(r => r.Direction)); // Latin D is not Cyrillic Д
        Assert.Equal("ABC123", parsed.Rows[0].TransactionId);
        Assert.Equal(new DateOnly(2026, 10, 5), parsed.Rows[0].Date);
    }

    [Fact]
    public void The_short_paysera_counts_every_eur_row_as_money_out()
    {
        var paysera = Catalog.Find("paysera-ru-short")!;
        var sheet = Tables.Sheet(paysera.Headers.ToArray(),
            Tables.Row("2026-10-05", "Mari Maasikas", "LT1", "palk", "200,00", "EUR"),
            Tables.Row("2026-10-06", "Mari Maasikas", "LT1", "palk", "200,00", "USD"));

        var parsed = StatementParser.Parse(sheet, StatementParser.Detect(sheet, Catalog).Match!);

        Assert.Equal([Direction.Debit, Direction.Unknown], parsed.Rows.Select(r => r.Direction));
        Assert.Equal(["EUR", "USD"], parsed.Rows.Select(r => r.Currency));
        Assert.Null(parsed.Account);
    }

    [Fact]
    public void Kassa_counts_a_row_only_when_the_sheet_type_is_a_payout()
    {
        var kassa = Catalog.Find("kassa")!;
        var sheet = Tables.Sheet(kassa.Headers.ToArray(),
            Tables.Row("Kassa 1", "05.10.2026", "2026-09", "KVO", "Palgaväljamaksed", "Mari Maasikas", "EE12", "1000", "16", "20", "700", "58,08", "905,92"),
            Tables.Row("Kassa 1", "05.10.2026", "2026-09", "KVO", "Avansid", "Jaan Tamm", "EE13", "", "", "", "", "", "100"));

        var parsed = StatementParser.Parse(sheet, StatementParser.Detect(sheet, Catalog).Match!);

        Assert.Equal("Kassa", parsed.DisplayName);
        Assert.Equal([Direction.Debit, Direction.Unknown], parsed.Rows.Select(r => r.Direction));
        Assert.Equal(905.92m, parsed.Rows[0].Amount);
        Assert.Equal("2026-09", parsed.Rows[0].Description);
        Assert.Equal(new DateOnly(2026, 10, 5), parsed.Rows[0].Date);
        Assert.Equal("1000", parsed.Rows[0].Cells[7]); // bruto stays in the cells
    }

    [Fact]
    public void Wise_and_revolut_take_direction_from_the_sign_and_revolut_moves_utc_to_estonian_time()
    {
        var wise = Catalog.Find("wise")!;
        var wiseRow = new string[wise.Headers.Count];
        Array.Fill(wiseRow, "");
        wiseRow[0] = "TRANSFER-1";
        wiseRow[1] = "15-10-2026";
        wiseRow[3] = "-250.00";
        wiseRow[4] = "EUR";
        wiseRow[6] = "palk";
        wiseRow[12] = "Mari Maasikas";
        var wiseIn = (string[])wiseRow.Clone();
        wiseIn[0] = "TRANSFER-2";
        wiseIn[3] = "99.99";
        var wiseSheet = Tables.Sheet(wise.Headers.ToArray(), wiseRow, wiseIn);
        var wiseParsed = StatementParser.Parse(wiseSheet, StatementParser.Detect(wiseSheet, Catalog).Match!);
        Assert.Equal("TransferWise", wiseParsed.DisplayName);
        Assert.Equal([Direction.Debit, Direction.Credit], wiseParsed.Rows.Select(r => r.Direction));
        Assert.Equal(new DateOnly(2026, 10, 15), wiseParsed.Rows[0].Date);
        Assert.Equal("TRANSFER-1", wiseParsed.Rows[0].TransactionId);

        var source1 = Catalog.Find("source1")!;
        var row = new string[source1.Headers.Count];
        Array.Fill(row, "");
        row[1] = "2026-10-31 22:30:00"; // UTC: already 1 November in Tallinn
        row[2] = "ID-1";
        row[5] = "Mari Maasikas";
        row[11] = "EUR";
        row[12] = "-300.00";
        row[21] = "LT601234567890123456";
        var zoned = (string[])row.Clone();
        zoned[1] = "2026-10-31T22:30:00Z"; // the same instant, the zone written into the text: converted once, not twice
        var suffixed = (string[])row.Clone();
        suffixed[1] = "2026-10-31 22:30:00 UTC"; // a time the patterns do not know: a bare-date fallback would lose the hour, so it is a problem
        var sheet = Tables.Sheet(source1.Headers.ToArray(), row, zoned, suffixed);
        var parsed = StatementParser.Parse(sheet, StatementParser.Detect(sheet, Catalog).Match!);
        Assert.Equal("Source1 *3456", parsed.DisplayName);
        Assert.Equal(new DateOnly(2026, 11, 1), parsed.Rows[0].Date);
        Assert.Equal(new DateOnly(2026, 11, 1), parsed.Rows[1].Date);
        Assert.Null(parsed.Rows[2].Date);
        Assert.Contains(parsed.Problems, p => p.Code == ImportProblem.BadDate && p.RowNo == 4);
        Assert.Equal(Direction.Debit, parsed.Rows[0].Direction);
        Assert.Equal("EUR", parsed.Rows[0].Currency);

        // a zone written into the text is honoured by a layout in Estonian time too
        var lhv = Catalog.Find("lhv-et-short")!;
        var lhvSheet = Tables.Sheet(lhv.Headers.ToArray(), Tables.Row("EE49", "", "2026-10-31T22:30:00Z", "", "Mari Maasikas", "D", "-1"));
        Assert.Equal(new DateOnly(2026, 11, 1), StatementParser.Parse(lhvSheet, StatementParser.Detect(lhvSheet, Catalog).Match!).Rows[0].Date);
    }

    [Fact]
    public void A_sheet_with_a_header_row_and_no_counterparty_rows_is_recognised_with_a_problem()
    {
        var lhv = Catalog.Find("lhv-et-short")!;
        var sheet = Tables.Sheet(lhv.Headers.ToArray(), Tables.Row("EE49", "", "01.10.2026", "", "", "D", "-1"));

        var parsed = StatementParser.Parse(sheet, StatementParser.Detect(sheet, Catalog).Match!);

        Assert.Empty(parsed.Payments);
        Assert.Contains(parsed.Problems, p => p.Code == ImportProblem.NoRows);
    }

    [Fact]
    public void Cells_round_trip_through_their_compact_json()
    {
        var cells = new[] { "a", "", "Tühi \"x\"", " ", "Счёт" };

        var json = StatementParser.CellsJson(cells);

        Assert.Equal("[\"a\",\"\",\"Tühi \\\"x\\\"\",\"\\u00A0\",\"Счёт\"]", json); // one line, letters as they are, the no-break space escaped
        Assert.Equal(cells, StatementParser.CellsFromJson(json));
    }
}
