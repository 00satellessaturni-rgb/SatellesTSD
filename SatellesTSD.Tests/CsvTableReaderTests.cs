using System.Text;
using SatellesTSD.Core.Import;

namespace SatellesTSD.Tests;

/// <summary>Bank CSV files as the banks write them: code pages, delimiters, quoting, line ends.</summary>
public class CsvTableReaderTests
{
    [Fact]
    public void A_swedbank_file_in_windows_1257_decodes_and_its_header_reads_correctly()
    {
        var text = "\"Kliendi konto\";\"Dok nr\";\"Makse kuupäev\";\"Saaja/maksja konto\";\"Saaja/maksja nimi\";\"Saaja/maksja pank\";\"Tühi (ei kasutata)\";\"Deebet/Kreedit (D/C)\";\"Summa\";\"Viitenumber\";\"Arhiveerimistunnus\";\"Selgitus\";\"Teenustasu\";\"VALUUTA TÄHIS\";\"Isikukood / Reg. nr\";\r\n"
                   + "\"EE162200221054367099\";\"26034\";\"08-10-2026\";\"EE497700771001131757\";\"StopFire OÜ\";\"689\";\"\";\"D\";\"-8500,00\";\"\";\"2026100802767783\";\"Firma SM\";\"0\";\"EUR\";\"\"\r\n";
        var bytes = CsvTableReader.Windows1257.GetBytes(text);
        Assert.Throws<DecoderFallbackException>(() => new UTF8Encoding(false, true).GetString(bytes)); // really not UTF-8

        var workbook = new CsvTableReader().Read("statement (43).csv", bytes);

        Assert.Equal(CsvTableReader.BalticEncodingName, workbook.Encoding);
        var sheet = Assert.Single(workbook.Sheets);
        Assert.Equal("statement (43)", sheet.Name);
        Assert.Equal(15, sheet.LastColumnOf(1)); // the trailing ';' adds no column
        Assert.Equal("Makse kuupäev", sheet.At(1, 3).Text);
        Assert.Equal("StopFire OÜ", sheet.At(2, 5).Text);
        Assert.Equal("-8500,00", sheet.At(2, 9).Text);
        Assert.Equal(CellKind.Text, sheet.At(2, 9).Kind);

        var result = StatementParser.Detect(sheet, LayoutCatalog.Default);
        Assert.Equal("swedbank-et", result.Match?.Layout.Id);
        var parsed = StatementParser.Parse(sheet, result.Match!);
        Assert.Equal("Swedbank *7099", parsed.DisplayName);
        Assert.Equal(((DateOnly?)new DateOnly(2026, 10, 8), (decimal?)-8500m, Direction.Debit), (parsed.Rows[0].Date, parsed.Rows[0].Amount, parsed.Rows[0].Direction));
    }

    [Fact]
    public void Swedbanks_s_and_z_with_carons_come_as_iso_8859_4_writes_them()
    {
        // the bank's file: Ä Ö Ü Õ as in Windows-1257, but Š Ž š ž at the ISO-8859-4 bytes A9 AE B9 BE (Windows-1257 shows © ® ¹ ¾ there)
        var bytes = CsvTableReader.Windows1257.GetBytes("\"Saaja/maksja nimi\"\r\n\"KIRILL ")
            .Concat(new byte[] { 0xA9 }).Concat(CsvTableReader.Windows1257.GetBytes("VÕRJOV\"\r\n\""))
            .Concat(new byte[] { 0xAE, 0xB9, 0xBE }).Concat(CsvTableReader.Windows1257.GetBytes("\"\r\n")).ToArray();

        var sheet = new CsvTableReader().Read("s.csv", bytes).Sheets[0];

        Assert.Equal("KIRILL ŠVÕRJOV", sheet.At(2, 1).Text);
        Assert.Equal("Žšž", sheet.At(3, 1).Text);
        Assert.Equal("Š", new CsvTableReader().Read("s.csv", CsvTableReader.Windows1257.GetBytes("Š")).Sheets[0].At(1, 1).Text); // Windows-1257's own byte for Š still reads as Š
        Assert.Equal("KIRILL ŠVÕRJOV", NameText.Demojibake("KIRILL ©VХRJOV")); // and the Excel copy of such a file, © and all, is put back
        Assert.Equal("ANŽELIKA BABITŠEVA", NameText.Demojibake("AN®ELIKA BABIT©EVA")); // even with no Cyrillic letter in it
    }

    [Fact]
    public void An_lhv_file_with_a_bom_commas_and_quoted_commas_reads_field_by_field()
    {
        var text = "\uFEFF\"Kliendi konto\",\"Dokumendi number\",\"Kuupäev\",\"Saaja/maksja konto\",\"Saaja/maksja nimi\",\"Deebet/Kreedit (D/C)\",\"Summa\",\"Viitenumber\",\"Arhiveerimistunnus\",\"Selgitus\",\"Valuuta\",\"Isikukood või registrikood\",\"Saaja/maksja panga BIC\",\"Makse algataja nimi\",\"Kande viide\",\"Konto teenusepakkuja viide\"\n"
                   + "\"EE037700771003091015\",\"227,1071\",2026-10-01,\"EE644204278613688414\",\"Dmitri Burov\",\"D\",-1354.00,\"\",\"2026092158069415\",\"septembrikuu palk\",\"EUR\",\"\",\"EKRDEE22XXX\",\"\",\"1558069415\",\"A78D7E9F8EB5F111BB4E0627B324E395\"\n"
                   + "\"EE037700771003091015\",\"\",2026-10-02,\"\",\"PARKL\",\"D\",-2.20,\"\",\"\",\"(..6431) \\KAARLI PST.2B \\TALLINN\",\"EUR\",\"\",\"\",\"\",\"1565649254\",\"13AC\"\n";

        var sheet = new CsvTableReader().Read("EE03.csv", Encoding.UTF8.GetBytes(text)).Sheets[0];

        Assert.Equal("Kliendi konto", sheet.At(1, 1).Text); // the BOM is gone
        Assert.Equal("227,1071", sheet.At(2, 2).Text);
        Assert.Equal("-1354.00", sheet.At(2, 7).Text);
        Assert.Equal("(..6431) \\KAARLI PST.2B \\TALLINN", sheet.At(3, 10).Text);
        var parsed = StatementParser.Parse(sheet, StatementParser.Detect(sheet, LayoutCatalog.Default).Match!);
        Assert.Equal("lhv-et-short", parsed.Layout.Id);
        Assert.Equal(-1354m, parsed.Rows[0].Amount);
        Assert.Equal("LHV pank *1015", parsed.DisplayName);
    }

    [Fact]
    public void A_luminor_file_with_bare_carriage_returns_is_split_into_records()
    {
        var text = "Konto;Dokumendi number;Väärtuspäev;Saaja/maksja konto;Saaja/maksja nimi;Deebet/krediit;Summa;Viitenumber;Arhiveerimiskood;Makse andmed;Valuuta;Registrikood/isikukood\r"
                   + "EE651700017003166439;RMZB27JC0L;01.10.2026;EE611700017000019152;Luminor Liising AS;D;\"320,21\";2026020444;B06456BT;\"Arve nr: 17521240\";EUR;12223047\r"
                   + "EE651700017003166439;RMZB27ZPK5;12.10.2026;;Luminor Bank AS;D;\"0,38\";;B065T5W9;\"Teenustasu\";EUR;12223047\r";

        var sheet = new CsvTableReader().Read("AccountStatement (21).csv", Encoding.UTF8.GetBytes(text)).Sheets[0];

        Assert.Equal(3, sheet.Rows.Count);
        Assert.Equal("luminor-et", StatementParser.Detect(sheet, LayoutCatalog.Default).Match?.Layout.Id);
        Assert.Equal("320,21", sheet.At(2, 7).Text);
    }

    [Fact]
    public void Quoted_fields_may_hold_the_delimiter_doubled_quotes_and_line_breaks_and_the_record_number_is_the_row()
    {
        var text = "a;b;c\r\n\"x;y\";\"say \"\"hi\"\"\";\"two\r\nlines\"\r\n\r\n1;2;3\r\n\r\n\r\n";

        var rows = CsvTableReader.ParseRecords(text, ';');

        Assert.Equal(4, rows.Count); // the trailing blank records are dropped, the inner one stays
        Assert.Equal(["x;y", "say \"hi\"", "two\r\nlines"], rows[1].Select(c => c.Text));
        Assert.Empty(rows[2]);
        Assert.Equal(["1", "2", "3"], rows[3].Select(c => c.Text));
    }

    [Theory]
    [InlineData("a;b;c", ';')]
    [InlineData("a,b,c", ',')]
    [InlineData("a\tb\tc", '\t')]
    [InlineData("\"a;b\",c,d", ',')]
    [InlineData("a|b|c", '|')]
    [InlineData("abc", ';')]
    public void The_delimiter_is_the_one_that_splits_the_header_most(string header, char expected)
    {
        Assert.Equal(expected, CsvTableReader.SniffDelimiter(header));
    }

    [Fact]
    public void The_delimiter_is_sniffed_past_a_title_line_and_blank_lines()
    {
        var headers = string.Join(";", LayoutCatalog.Default.Find("lhv-et-short")!.Headers);
        var text = "\n\nKonto väljavõte, oktoober 2026\n\n" + headers + "\nEE497700771001131757;1;2026-10-01;EE12;Mari Maasikas;D;-100;;;palk;EUR\n";

        var import = new Importer(LayoutCatalog.Default).Read("lhv.csv", Encoding.UTF8.GetBytes(text));

        Assert.Equal("lhv-et-short", import.Sheets[0].Statement?.Layout.Id); // the comma in the title did not decide
        Assert.Equal(5, import.Sheets[0].Statement!.HeaderRowNo);
        Assert.Equal(6, Assert.Single(import.Sheets[0].Statement!.Payments).RowNo);
        Assert.Equal(';', CsvTableReader.SniffDelimiter(CsvTableReader.FirstLines(text, 5)));
        Assert.Equal(["Konto väljavõte, oktoober 2026", headers], CsvTableReader.FirstLines(text, 2));
    }

    [Fact]
    public void A_quote_that_is_never_closed_is_said_not_hidden()
    {
        var text = "Дата;Плательщик / Получатель;Номер счета;Назначение платежа;Сумма;Валюта\n2026-10-01;\"Mari Maasikas;LT1;palk;100,00;EUR\n2026-10-02;Jaan Tamm;LT2;palk;5,00;EUR\n";

        var import = new Importer(LayoutCatalog.Default).Read("p.csv", Encoding.UTF8.GetBytes(text));

        var warning = Assert.Single(import.Warnings);
        Assert.Contains("record 2", warning);
        Assert.Contains("never closed", warning);
        Assert.Single(import.Sheets[0].Statement!.Rows); // the rest of the file became that one field of the one data row
    }

    [Fact]
    public void One_data_line_rich_in_commas_does_not_change_the_delimiter()
    {
        var headers = string.Join(";", LayoutCatalog.Default.Find("paysera-ru-short")!.Headers);
        var text = headers + "\n2026-10-01;Mari Maasikas;LT1;Arve 1, 2, 3, 4, 5, 6, 7, 8 ja 9;100,00;EUR\n2026-10-02;Jaan Tamm;LT2;palk;5,00;EUR\n";

        Assert.Equal(';', CsvTableReader.SniffDelimiter(CsvTableReader.FirstLines(text, 5)));
        Assert.Equal("paysera-ru-short", new Importer(LayoutCatalog.Default).Read("p.csv", Encoding.UTF8.GetBytes(text)).Sheets[0].Statement?.Layout.Id);
    }

    [Fact]
    public void A_headerless_seb_file_reads_as_seb()
    {
        var text = "\uFEFF\"EE591010220215133228\";\"TM168358936\";\"01.10.2026\";\"\";\"AS SEB PANK\";\"EEUHEE2X\";\"H\";\"C\";1,01;\"\";\"RO4248947827L02\";\"intresside väljamakse EUR\";0;\"EUR\";\"\"\n"
                   + "\"EE591010220215133228\";\"TT\";\"02.10.2026\";\"EE497700771001131757\";\"StopFire OÜ\";\"LHVBEE22\";\"H\";\"D\";500,00;\"\";\"RO4262101896L02\";\"Reserv SEB\";0;\"EUR\";\"\"\n\n";

        var import = new Importer(LayoutCatalog.Default).Read("kontovv (1) (9).csv", Encoding.UTF8.GetBytes(text));

        var sheet = Assert.Single(import.Sheets);
        Assert.Equal(SheetKind.Statement, sheet.Kind);
        Assert.Equal("seb-et", sheet.Statement!.Layout.Id);
        Assert.Equal(0, sheet.Statement.HeaderRowNo);
        Assert.Equal(2, sheet.Statement.Rows.Count);
        Assert.Equal(ImportKind.Statement, import.Kind);
        Assert.Equal("utf-8", import.Encoding);
    }

    [Fact]
    public void A_russian_file_in_windows_1251_is_decoded_right_by_the_layout_that_matches_to_the_letter()
    {
        var text = "Дата;Плательщик / Получатель;Номер счета;Назначение платежа;Сумма;Валюта\n2026-10-01;Иван Петров;LT1;зарплата;100,00;EUR\n";
        var bytes = CsvTableReader.Windows1251.GetBytes(text);

        // read as Windows-1257 the header still matches (the code-page mix-up is tolerated), but not to the letter
        var guessed = new CsvTableReader().Read("p.csv", bytes).Sheets[0];
        var detection = StatementParser.Detect(guessed, LayoutCatalog.Default);
        Assert.Equal("paysera-ru-short", detection.Match?.Layout.Id);
        Assert.False(detection.Match!.Exact);
        Assert.NotEqual("Иван Петров", guessed.At(2, 2).Text);

        // so the importer tries Windows-1251 and keeps it: the names come out right
        var import = new Importer(LayoutCatalog.Default).Read("p.csv", bytes);
        Assert.Equal("windows-1251", import.Encoding);
        Assert.True(import.Sheets[0].Statement!.ExactHeaders);
        Assert.Equal("Иван Петров", import.Sheets[0].Statement!.Rows[0].Counterparty);

        // a caller who names the encoding is not second-guessed
        var named = new Importer(LayoutCatalog.Default).Read("p.csv", bytes, CsvTableReader.Windows1257);
        Assert.Equal("windows-1257", named.Encoding);
        Assert.False(named.Sheets[0].Statement!.ExactHeaders);
    }
}
