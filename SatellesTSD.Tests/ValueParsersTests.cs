using System.Text;
using SatellesTSD.Core.Import;

namespace SatellesTSD.Tests;

/// <summary>Amounts, dates, identifiers and names as the sources write them.</summary>
public class ValueParsersTests
{
    [Theory]
    [InlineData("1234.56", null, "1234.56")]
    [InlineData("1234,56", null, "1234.56")]
    [InlineData("-2081,84", null, "-2081.84")]
    [InlineData("-201.09", null, "-201.09")]
    [InlineData("1 234,56", null, "1234.56")]
    [InlineData("1 234,56", null, "1234.56")]
    [InlineData("1.234,56", null, "1234.56")]
    [InlineData("1,234.56", null, "1234.56")]
    [InlineData("123,45 EUR", null, "123.45")]
    [InlineData("EUR 123.45", null, "123.45")]
    [InlineData("−12.00", null, "-12.00")]
    [InlineData("+50", null, "50")]
    [InlineData("1,234", ",", "1.234")]
    [InlineData("1,234", ".", "1234")]
    [InlineData("1.234", null, "1.234")]
    [InlineData("1.234.567", null, "1234567")]
    [InlineData("0", null, "0")]
    [InlineData("(100,00)", null, "-100.00")]
    [InlineData("100,00-", null, "-100.00")]
    [InlineData("€ 12,50", null, "12.50")]
    [InlineData("12,50 €", null, "12.50")]
    [InlineData("-€12.50", null, "-12.50")]
    [InlineData("eur 12", null, "12")]
    [InlineData("1'234.56", null, "1234.56")]
    [InlineData("1 234 567,89", null, "1234567.89")]
    [InlineData("1,234,567.89", null, "1234567.89")]
    [InlineData("1.234", ",", "1234")]
    public void Amounts_are_read_whatever_the_separators(string text, string? separator, string expected)
    {
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), AmountText.Parse(text, separator));
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("abc", null)]
    [InlineData("EUR", null)]
    [InlineData("-", null)]
    [InlineData("Arve 12", null)]
    [InlineData("2026-05-12", null)]
    [InlineData("31.10.2026", null)]
    [InlineData("1.10.2026", null)]
    [InlineData("12 EUR 34", null)]
    [InlineData("1.234,56 EUR (kokku)", null)]
    [InlineData("12,50 3,00", null)]
    [InlineData("12 34", null)]
    [InlineData("1,5,10", null)]
    [InlineData("1,,2", null)]
    [InlineData("1234 567", null)]
    [InlineData("-12-", null)]
    [InlineData("(100", null)]
    [InlineData("EUR 12 USD", null)]
    [InlineData("1,5", ".")]
    [InlineData("1.234.567,89", ".")]
    public void Text_that_is_not_wholly_one_amount_is_not_an_amount(string text, string? separator)
    {
        Assert.Null(AmountText.Parse(text, separator));
    }

    [Fact]
    public void A_cell_made_to_make_a_parser_backtrack_is_refused_at_once()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();

        Assert.Null(AmountText.Parse("1" + new string(' ', 400) + "x"));
        Assert.Null(AmountText.Parse(new string(' ', 400) + "x"));
        Assert.Null(AmountText.Parse(string.Concat(Enumerable.Repeat("1 ", 200)) + "x"));
        Assert.Null(AmountText.Parse("100.00" + new string('\t', 300) + "x"));

        Assert.True(watch.ElapsedMilliseconds < 1000, $"{watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void The_currency_in_an_amount_cell_is_found()
    {
        Assert.Equal("EUR", AmountText.CurrencyOf("123,45 EUR"));
        Assert.Equal("USD", AmountText.CurrencyOf("USD -1,00"));
        Assert.Null(AmountText.CurrencyOf("123,45"));
        Assert.Null(AmountText.CurrencyOf("ARVE 123"));
    }

    [Theory]
    [InlineData("2026-10-31", "2026-10-31T00:00:00")]
    [InlineData("31.10.2026", "2026-10-31T00:00:00")]
    [InlineData("1.3.2026", "2026-03-01T00:00:00")]
    [InlineData("31-10-2026", "2026-10-31T00:00:00")]
    [InlineData("31/10/2026", "2026-10-31T00:00:00")]
    [InlineData("2026-10-31 14:22:05", "2026-10-31T14:22:05")]
    [InlineData("2026-10-31T14:22:05", "2026-10-31T14:22:05")]
    [InlineData("31.10.2026 7:05", "2026-10-31T07:05:00")]
    [InlineData("20.09.2021 07:46", "2021-09-20T07:46:00")]
    [InlineData("31.10.2026 (makse)", "2026-10-31T00:00:00")]
    [InlineData(" 31.10.2026 ", "2026-10-31T00:00:00")]
    public void Dates_are_read_in_every_shape_the_sources_use(string text, string expected)
    {
        Assert.Equal(DateTime.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), DateText.ParseText(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("31.13.2026")]
    [InlineData("2026")]
    [InlineData("eile")]
    [InlineData("45322")]
    [InlineData("31.10.2026 kell 10")]
    [InlineData("31.10.2026 10:15 PM")]
    public void Text_that_is_not_a_date_is_not_a_date(string text)
    {
        Assert.Null(DateText.ParseText(text)); // a tail with digits is a time the patterns do not know, never dropped
    }

    [Fact]
    public void A_zone_written_into_the_text_comes_back_as_utc_and_is_converted_once()
    {
        var zoned = DateText.ParseText("2026-10-31T22:30:00Z");
        Assert.Equal(DateTimeKind.Utc, zoned!.Value.Kind);
        Assert.Equal(new DateTime(2026, 10, 31, 22, 30, 0, DateTimeKind.Utc), zoned);
        Assert.Equal(new DateTime(2026, 11, 1, 0, 30, 0), DateText.ToEstonian(zoned.Value, null));
        Assert.Equal(new DateTime(2026, 11, 1, 0, 30, 0), DateText.ToEstonian(zoned.Value, TimeZoneInfo.Utc)); // not shifted twice

        var offset = DateText.ParseText("2026-10-31T23:30:00+02:00");
        Assert.Equal(new DateTime(2026, 10, 31, 21, 30, 0, DateTimeKind.Utc), offset);
        Assert.Equal(new DateTime(2026, 10, 31, 23, 30, 0), DateText.ToEstonian(offset!.Value, null));
        Assert.Equal(new DateTime(2026, 10, 31, 22, 30, 0, DateTimeKind.Utc), DateText.ParseText("2026-10-31T22:30Z"));
        Assert.Equal(new DateTime(2026, 10, 31, 22, 30, 0, DateTimeKind.Utc).AddTicks(1234567), DateText.ParseText("2026-10-31T22:30:00.1234567Z"));

        var plain = DateText.ParseText("2026-10-31 22:30:00");
        Assert.Equal(DateTimeKind.Unspecified, plain!.Value.Kind);
        Assert.Equal(plain, DateText.ToEstonian(plain.Value, null)); // wall time of unstated zone: already Estonian
    }

    [Fact]
    public void A_time_in_the_source_zones_spring_gap_is_moved_past_it()
    {
        var berlin = DateText.Zone("Europe/Berlin");

        Assert.Equal(new DateTime(2026, 3, 29, 4, 30, 0), DateText.ToEstonian(new DateTime(2026, 3, 29, 2, 30, 0), berlin)); // 02:30 does not exist in Berlin that night
    }

    [Fact]
    public void Typed_cells_and_excel_serials_become_dates_and_a_layouts_own_formats_come_first()
    {
        Assert.Equal(new DateTime(2026, 10, 1), DateText.Parse(Cell.Date(new DateTime(2026, 10, 1))));
        Assert.Equal(new DateTime(2026, 10, 1, 9, 30, 0), DateText.Parse(Cell.Date(new DateTime(2026, 10, 1, 9, 30, 0))));
        Assert.Equal(new DateTime(2026, 10, 1), DateText.Parse(Cell.Number(46296m)));
        Assert.Null(DateText.Parse(Cell.Number(12m)));
        Assert.Null(DateText.Parse(Cell.Empty));
        Assert.Equal(new DateTime(2026, 3, 10), DateText.Parse(Cell.Of("10-03-2026"), ["dd-MM-yyyy"]));
        Assert.Equal(new DateTime(2026, 3, 10), DateText.Parse(Cell.Of("03/10/2026"), ["MM/dd/yyyy"])); // a layout may say the other order
    }

    [Fact]
    public void A_utc_time_moves_to_estonian_time_but_a_bare_date_stays()
    {
        var utc = DateText.Zone("UTC");

        Assert.Equal(new DateTime(2026, 11, 1, 0, 30, 0), DateText.ToEstonian(new DateTime(2026, 10, 31, 22, 30, 0), utc)); // winter: +2
        Assert.Equal(new DateTime(2026, 7, 1, 1, 30, 0), DateText.ToEstonian(new DateTime(2026, 6, 30, 22, 30, 0), utc)); // summer: +3
        Assert.Equal(new DateTime(2026, 10, 31), DateText.ToEstonian(new DateTime(2026, 10, 31), utc));
        Assert.Throws<TimeZoneNotFoundException>(() => DateText.Zone("Mars/Olympus"));
    }

    [Fact]
    public void Garbled_names_are_put_back_in_the_right_order_and_genuine_cyrillic_is_left_alone()
    {
        Assert.Equal("Петров", NameText.Demojibake("РџРµС‚СЂРѕРІ")); // UTF-8 Cyrillic read as Windows-1251 ("РџРµС‚СЂРѕРІ"): the µ is not a Latin letter here
        Assert.Equal("Иван Smith", NameText.Demojibake("Иван Smith")); // genuine Cyrillic beside Latin: И is no look-alike
        Assert.Equal("Иван Smith", NameText.FixCyrillicLookalikes("Иван Smith"));
        Assert.Equal("Tõnu Mägi", NameText.FixCyrillicLookalikes("Tхnu Mдgi"));
        Assert.Equal("Pедро", NameText.FixCyrillicLookalikes("Pедро")); // a Latin P before Cyrillic letters that are not all look-alikes
        Assert.Equal("OÜ DEPILÉ", NameText.Demojibake("OЬ DEPILЙ")); // a letter outside the workbook's twelve: the whole text re-read as Windows-1257
        Assert.Equal("Иван Smith", NameText.Demojibake("Иван Smith")); // re-read it would be "Čâŕí Smith": Lithuanian letters say it is genuine Cyrillic
        Assert.Equal("Arve 12 €", NameText.Demojibake("Arve 12 Ђ"));
    }

    [Fact]
    public void The_fingerprint_text_is_the_same_whichever_way_the_text_arrived()
    {
        Assert.Equal(NameText.Canonical("Tõnu Mägi"), NameText.Canonical("Tхnu Mдgi")); // Windows-1257 read as Windows-1251
        Assert.Equal(NameText.Canonical("Arve 12 €"), NameText.Canonical("Arve 12 Ђ")); // a letter outside the look-alike twelve
        Assert.Equal(NameText.Canonical("зарплата"), NameText.Canonical("Р·Р°СЂРїР»Р°С‚Р°")); // UTF-8 read as Windows-1251
        Assert.Equal(NameText.Canonical("19242216802"), NameText.Canonical("019242216802")); // Excel drops a leading zero
        Assert.Equal(NameText.Canonical("1365,1368"), NameText.Canonical("1365.1368")); // and reads two invoice numbers as one decimal
        Assert.Equal(NameText.Canonical("1365,1370"), NameText.Canonical("1365.137")); // and drops the decimal's trailing zero
        Assert.Equal(NameText.Canonical("100,00"), NameText.Canonical("100"));
        Assert.NotEqual(NameText.Canonical("1,5"), NameText.Canonical("15"));
        Assert.Equal("0", NameText.Canonical("000"));
        Assert.NotEqual(NameText.Canonical("Arve 12"), NameText.Canonical("Arve 13"));
        Assert.Equal(NameText.Canonical("KIRILL ŠVÕRJOV"), NameText.Canonical("KIRILL Е VГ•RJOV")); // UTF-8 read as Windows-1251: Š became "Е" + a no-break space
        Assert.Equal(NameText.Canonical("Tšekk 15"), NameText.Canonical("T№ekk 15")); // Swedbank's š in a Windows-1251 view
        Assert.Equal(NameText.Canonical("ŽANNA TÕNISSON"), NameText.Canonical("®ANNA TÕNISSON")); // and Ž in a Windows-1257 view
    }

    [Fact]
    public void Swedbanks_s_and_z_with_carons_are_put_back_from_either_excel_view()
    {
        Assert.Equal("Babitševa", NameText.Demojibake("Babit№eva")); // Windows-1251 view of byte B9
        Assert.Equal("Arve №5", NameText.Demojibake("Arve №5")); // a numero sign before a number is left alone
        Assert.Equal("ŽANNA TÕNISSON", NameText.Demojibake("®ANNA TÕNISSON")); // Windows-1257 view of byte AE, Estonian letters beside it
        Assert.Equal("Ševtšenko", NameText.Demojibake("Ševt¹enko"));
    }

    [Fact]
    public void A_broken_character_in_a_header_does_not_throw()
    {
        var lone = "Kuup\uD83Dev"; // a high surrogate with nothing after it

        Assert.Equal("Kuup�ev", HeaderText.Clean(lone));
        Assert.DoesNotContain(lone, HeaderText.Forms(lone).Where(f => f != lone));
        Assert.NotEmpty(TorHeaders.Key(lone));
    }

    [Fact]
    public void Codes_that_came_through_excel_as_numbers_are_whole_again()
    {
        Assert.Equal("38001010000", IdText.Code(Cell.Number(38001010000m)));
        Assert.Equal("38001010000", IdText.Code(Cell.Of(" 38001010000 ")));
        Assert.Equal("AB0012", IdText.Code(Cell.Of("AB0012")));
        Assert.Equal("0012", IdText.Code(Cell.Of("0012")));
        Assert.Null(IdText.Code(Cell.Of("  ")));
        Assert.Equal("2", IdText.IntegerId(Cell.Number(2m)));
        Assert.Equal("2", IdText.IntegerId(Cell.Of("2.0")));
        Assert.Equal("600", IdText.IntegerId(Cell.Of("600")));
        Assert.Equal("a1t", IdText.IntegerId(Cell.Of("a1t")));
    }

    [Fact]
    public void The_personal_code_check_is_the_workbooks_and_the_checksum_is_stricter()
    {
        Assert.True(PersonalCode.LooksValid("48712219406"));
        Assert.True(PersonalCode.LooksValid("38810140309"));
        Assert.False(PersonalCode.LooksValid("78810140309")); // first digit 7
        Assert.False(PersonalCode.LooksValid("38813140309")); // month 13
        Assert.False(PersonalCode.LooksValid("3881014030"));
        Assert.False(PersonalCode.LooksValid("3881014030X"));
        Assert.False(PersonalCode.LooksValid(null));
        Assert.Equal(new DateOnly(1988, 10, 14), PersonalCode.BirthDate("38810140309"));

        Assert.True(PersonalCode.ChecksumValid("38810140309"));
        Assert.True(PersonalCode.ChecksumValid("48712219406"));
        Assert.False(PersonalCode.ChecksumValid("38810140300"));
    }

    [Fact]
    public void Names_decoded_as_cyrillic_are_put_back_only_when_the_scripts_mix()
    {
        Assert.Equal("Tõnu Mägi", NameText.FixCyrillicLookalikes("Tхnu Mдgi"));
        Assert.Equal("StopFire OÜ", NameText.FixCyrillicLookalikes("StopFire OЬ"));
        Assert.Equal("Šarlote Žagars", NameText.FixCyrillicLookalikes("Рarlote Юagars"));
        Assert.Equal("Петров Иван", NameText.FixCyrillicLookalikes("Петров Иван")); // genuinely Cyrillic, untouched
        Assert.Equal("Mari Maasikas", NameText.FixCyrillicLookalikes("Mari Maasikas"));
        Assert.Equal("", NameText.FixCyrillicLookalikes(null));
    }

    [Fact]
    public void Names_excel_read_with_the_wrong_code_page_are_put_back()
    {
        Assert.Equal("StopFire OÜ", NameText.Demojibake("StopFire OГњ")); // UTF-8 (C3 9C) read as Windows-1251: Гњ
        Assert.Equal("Tõnu Mägi", NameText.Demojibake("Tхnu Mдgi")); // Windows-1257 read as Windows-1251
        Assert.Equal("Петров Иван", NameText.Demojibake("Петров Иван")); // genuine Cyrillic: its bytes are not UTF-8
        Assert.Equal("Tõnu Mägi", NameText.Demojibake("Tõnu Mägi"));
        Assert.Equal("Mari Maasikas", NameText.Demojibake("Mari Maasikas"));
        Assert.Equal("", NameText.Demojibake(null));
    }

    [Fact]
    public void A_name_is_compared_without_its_spacing_quirks()
    {
        Assert.Equal("Anna Maria Kask", NameText.Normalize("  Anna Maria   Kask "));
        Assert.Equal("", NameText.Normalize("   "));
    }

    [Fact]
    public void A_header_matches_itself_seen_through_a_wrong_code_page()
    {
        Assert.True(HeaderText.Match("Makse kuupäev", "Makse kuupдev"));
        Assert.True(HeaderText.Match("Makse kuupдev", "Makse kuupäev"));
        Assert.True(HeaderText.Match("Väärtuspäev", "VГ¤Г¤rtuspГ¤ev"));
        Assert.True(HeaderText.Match("Счёт", "РЎС‡С‘С‚"));
        Assert.True(HeaderText.Match("Имя", "Р\u0098РјСЏ"));
        Assert.True(HeaderText.Match("Регистровый", "Р РµРіРёСЃС‚СЂРѕРІС‹Р№"));
        Assert.False(HeaderText.Match("Kuupäev", "Makse kuupäev"));
        Assert.False(HeaderText.Match("Summa", "summa")); // case matters, as in the workbook
        Assert.Equal("Income tax ", HeaderText.Clean(" Income tax \t")); // the no-break space survives
    }

    [Theory]
    [InlineData(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0, 0 }, SourceFormat.Xlsx)]
    [InlineData(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0 }, SourceFormat.Xls)]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'K', (byte)';' }, SourceFormat.Csv)]
    [InlineData(new byte[] { (byte)'"', (byte)'K', (byte)'l', 0xE4 }, SourceFormat.Csv)]
    [InlineData(new byte[] { (byte)'<', (byte)'h', (byte)'t', (byte)'m', (byte)'l' }, SourceFormat.Html)]
    [InlineData(new byte[] { 0x0D, 0x0A, (byte)' ', (byte)'<', (byte)'t' }, SourceFormat.Html)]
    [InlineData(new byte[] { 0x00, 0x01, 0x02, 0x03 }, SourceFormat.Unknown)]
    [InlineData(new byte[] { (byte)'a', 0x00, (byte)'b' }, SourceFormat.Unknown)]
    [InlineData(new byte[] { }, SourceFormat.Unknown)]
    public void A_files_format_is_told_by_its_bytes(byte[] bytes, SourceFormat expected)
    {
        Assert.Equal(expected, FileSniffer.Sniff(bytes));
    }

    [Fact]
    public void A_utf16_text_file_with_cyrillic_or_baltic_letters_is_text()
    {
        var text = "Kuupäev;Saaja nimi;Summa\r\n01.10.2026;Šarlote Žagars Иван;1,00";

        Assert.Equal(SourceFormat.Csv, FileSniffer.Sniff(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray()));
        Assert.Equal(SourceFormat.Csv, FileSniffer.Sniff(Encoding.BigEndianUnicode.GetPreamble().Concat(Encoding.BigEndianUnicode.GetBytes(text)).ToArray()));
        Assert.Equal(SourceFormat.Html, FileSniffer.Sniff(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("<html>")).ToArray()));

        var sheet = new CsvTableReader().Read("u.csv", Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray()).Sheets[0];
        Assert.Equal("Šarlote Žagars Иван", sheet.At(2, 2).Text);
    }
}
