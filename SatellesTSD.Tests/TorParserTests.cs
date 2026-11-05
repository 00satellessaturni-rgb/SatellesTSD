using SatellesTSD.Core.Import;

namespace SatellesTSD.Tests;

/// <summary>The TÖR export: recognised by its headers in any order, read row by row, several rows per person kept.</summary>
public class TorParserTests
{
    private static string[] Full(string torId, string code, string last, string first, string start, string typeId, string type, string stateId, string state, string end, string employerCode, string employer)
    {
        var row = new string[35];
        Array.Fill(row, "");
        row[0] = torId;
        row[1] = code;
        row[2] = last;
        row[3] = first;
        row[4] = start;
        row[5] = typeId;
        row[6] = type;
        row[7] = stateId;
        row[8] = state;
        row[9] = end;
        row[17] = "EE";
        row[26] = employerCode;
        row[27] = employer;
        row[33] = "1";
        return row;
    }

    [Fact]
    public void The_export_as_the_register_writes_it_is_read()
    {
        var sheet = Tables.Sheet(
            TorHeaders.All.ToArray(),
            Full("414389", "38810140309", "BAVÕKIN", "DMITRI", "01.03.2012", "1", "Tööleping", "2", "Kehtiv", "", "12223047", "STOPFIRE OÜ"),
            Full("414390", "48712219406", "MAASIKAS", "MARI", "15.02.2024", "600", "VÕS leping", "4", "Lõpetatud", "30.09.2026", "12223047", "STOPFIRE OÜ"),
            Full("414391", "48712219406", "MAASIKAS", "MARI", "01.10.2026", "1", "Tööleping", "2", "Kehtiv", "", "12223047", "STOPFIRE OÜ"),
            Full("414392", "", "SMITH", "JOHN", "01.10.2026", "600", "VÕS leping", "2", "Kehtiv", "", "12223047", "STOPFIRE OÜ"));

        var detection = TorParser.Detect(sheet, out var missing);

        Assert.NotNull(detection);
        Assert.Empty(missing);
        Assert.Equal(1, detection.HeaderRowNo);
        Assert.Empty(detection.MissingOptional);
        Assert.Empty(detection.Unknown);

        var tor = TorParser.Parse(sheet, detection);
        Assert.Equal(4, tor.Entries.Count);
        var dmitri = tor.Entries[0];
        Assert.Equal(("414389", "38810140309", "DMITRI", "BAVÕKIN", "EE", (DateOnly?)new DateOnly(2012, 3, 1), (DateOnly?)null, "1", "Tööleping", "2", "Kehtiv", "12223047", "STOPFIRE OÜ"),
            (dmitri.TorId, dmitri.PersonalCode, dmitri.FirstName, dmitri.LastName, dmitri.Country, dmitri.StartDate, dmitri.EndDate, dmitri.TypeId, dmitri.Type, dmitri.StateId, dmitri.State, dmitri.EmployerCode, dmitri.EmployerName));
        Assert.Equal(2, dmitri.RowNo);
        Assert.Null(dmitri.Problem);
        Assert.Equal(34, dmitri.Cells.Count); // up to the last non-blank cell (Tööaja määr); the empty IMO is not kept

        // the same person twice stays twice, with the ended contract's end date
        Assert.Equal(2, tor.Entries.Count(e => e.PersonalCode == "48712219406"));
        Assert.Equal(new DateOnly(2026, 9, 30), tor.Entries[1].EndDate);
        Assert.True(tor.Entries[1].Overlaps(2026, 9));
        Assert.False(tor.Entries[1].Overlaps(2026, 10));
        Assert.True(tor.Entries[2].Overlaps(2026, 10));
        Assert.True(tor.Entries[2].Overlaps(2027, 1)); // open-ended

        // a foreign worker without a code is kept and flagged
        Assert.Null(tor.Entries[3].PersonalCode);
        Assert.Contains("no personal code", tor.Entries[3].Problem);
        Assert.Contains(tor.Problems, p => p.Code == TorParser.NoCode && p.RowNo == 5);

        Assert.Equal(["12223047"], tor.EmployerCodes);
        Assert.Equal(["STOPFIRE OÜ"], tor.EmployerNames);
        Assert.Null(TorParser.CheckEmployer(tor, "12223047"));
        Assert.Equal(TorParser.EmployerMismatch, TorParser.CheckEmployer(tor, "10235688")?.Code);
    }

    [Fact]
    public void Values_that_came_through_excel_as_numbers_and_dates_are_read_whole()
    {
        var xlsx = Tables.Xlsx("Töötajate loend",
            TorHeaders.All.ToArray<object?>(),
            [414389, 38810140309d, "BAVÕKIN", "DMITRI", new DateTime(2012, 3, 1), 1, "Tööleping", 2, "Kehtiv", null, null, null, null, 13490011, "Turvaosakonna juhataja", null, null, "EE", null, null, null, null, null, null, null, null, 12223047, "STOPFIRE OÜ", null, null, new DateTime(2019, 7, 19, 10, 27, 0), 748012897, "Jana Rudnik", 1]);

        var sheet = Tables.FirstSheet(xlsx);
        var tor = TorParser.Parse(sheet, TorParser.Detect(sheet, out _)!);

        var entry = Assert.Single(tor.Entries);
        Assert.Equal("38810140309", entry.PersonalCode);
        Assert.Equal("414389", entry.TorId);
        Assert.Equal(new DateOnly(2012, 3, 1), entry.StartDate);
        Assert.Equal(("1", "2"), (entry.TypeId, entry.StateId));
        Assert.Equal("12223047", entry.EmployerCode);
        Assert.Null(entry.Problem);
    }

    [Fact]
    public void A_paused_employment_says_since_when_why_and_until_when()
    {
        var paused = Full("414390", "48712219406", "MAASIKAS", "MARI", "15.02.2024", "1", "Tööleping", "3", "Peatatud", "", "12223047", "STOPFIRE OÜ");
        paused[18] = "2026-08-03";
        paused[19] = "2";
        paused[20] = "Lapsehoolduspuhkus";
        var sheet = Tables.Sheet(TorHeaders.All.ToArray(), paused, Full("414389", "38810140309", "BAVÕKIN", "DMITRI", "01.03.2012", "1", "Tööleping", "2", "Kehtiv", "", "12223047", "STOPFIRE OÜ"));

        var tor = TorParser.Parse(sheet, TorParser.Detect(sheet, out _)!);

        Assert.Equal(((DateOnly?)new DateOnly(2026, 8, 3), (DateOnly?)null, "Lapsehoolduspuhkus"), (tor.Entries[0].PauseStart, tor.Entries[0].PauseEnd, tor.Entries[0].PauseReason));
        Assert.Equal(((DateOnly?)null, (DateOnly?)null, (string?)null), (tor.Entries[1].PauseStart, tor.Entries[1].PauseEnd, tor.Entries[1].PauseReason));
        Assert.Null(tor.Entries[0].Problem);

        // typed dates through Excel, the planned end too; a pause date not understood reads as none, not as a problem
        var xlsx = Tables.Xlsx("Töötajate loend",
            TorHeaders.All.ToArray<object?>(),
            ["414390", 48712219406d, "MAASIKAS", "MARI", new DateTime(2024, 2, 15), 1, "Tööleping", 3, "Peatatud", null, null, null, null, null, null, null, null, "EE", new DateTime(2026, 8, 3), 2, "Vanemapuhkus", new DateTime(2027, 6, 30), null, null, null, null, 12223047, "STOPFIRE OÜ"],
            ["414391", 38810140309d, "TAMM", "JAAN", new DateTime(2020, 1, 1), 1, "Tööleping", 3, "Peatatud", null, null, null, null, null, null, null, null, "EE", "pole teada", null, null, null, null, null, null, null, 12223047, "STOPFIRE OÜ"]);
        var excel = Tables.FirstSheet(xlsx);
        var read = TorParser.Parse(excel, TorParser.Detect(excel, out _)!);
        Assert.Equal(((DateOnly?)new DateOnly(2026, 8, 3), (DateOnly?)new DateOnly(2027, 6, 30), "Vanemapuhkus"), (read.Entries[0].PauseStart, read.Entries[0].PauseEnd, read.Entries[0].PauseReason));
        Assert.Null(read.Entries[1].PauseStart);
        Assert.Empty(read.Problems);

        // the same reading from the cells and the header row alone: what the store does for an import it already holds
        Assert.Equal((read.Entries[0].PauseStart, read.Entries[0].PauseEnd, read.Entries[0].PauseReason), TorPause.Of(read.Headers, read.Entries[0].Cells));
    }

    [Fact]
    public void Columns_may_come_in_any_order_with_extras_and_the_header_may_sit_lower()
    {
        var headers = new[] { "Eesnimi", "Perekonnanimi", "Töötaja isikukood", "Uus veerg", "Töötamise alguse aeg", "Töötamise lõpu aeg", "Töötamise liigi ID", "Töötamise liik", "Kande oleku ID", "Kande olek", "Riigi kood", "tööandja /tegeliku  tööandja nimetus" };
        var sheet = Tables.Sheet(
            Tables.Row("Töötajate loend 02.11.2026"),
            headers,
            Tables.Row("MARI", "MAASIKAS", "48712219406", "x", "2026-10-01", "", "1", "Tööleping", "2", "Kehtiv", "EE", "Näidis OÜ"));

        var detection = TorParser.Detect(sheet, out var missing);

        Assert.NotNull(detection);
        Assert.Equal(2, detection.HeaderRowNo);
        Assert.Equal(["Uus veerg"], detection.Unknown);
        Assert.Equal([TorHeaders.TorId, TorHeaders.TerminationId, TorHeaders.Termination, TorHeaders.EmployerCode], detection.MissingOptional);
        var entry = Assert.Single(TorParser.Parse(sheet, detection).Entries);
        Assert.Equal(("MARI", "MAASIKAS", "48712219406", "Näidis OÜ"), (entry.FirstName, entry.LastName, entry.PersonalCode, entry.EmployerName));
        Assert.Null(entry.TorId);
    }

    [Fact]
    public void A_sheet_with_some_but_not_all_required_headers_says_which_are_missing()
    {
        var sheet = Tables.Sheet(Tables.Row("Töötaja isikukood", "Perekonnanimi", "Eesnimi", "Summa"), Tables.Row("1", "2", "3", "4"));

        Assert.Null(TorParser.Detect(sheet, out var missing));
        Assert.Contains(TorHeaders.StartDate, missing);
        Assert.Contains(TorHeaders.EmployerName, missing);
        Assert.DoesNotContain(TorHeaders.FirstName, missing);

        Assert.Null(TorParser.Detect(Tables.Sheet(Tables.Row("Nimi", "Summa"), Tables.Row("a", "1")), out var none));
        Assert.Empty(none);
    }

    [Fact]
    public void Bad_dates_names_and_several_employers_are_problems_not_exceptions()
    {
        var sheet = Tables.Sheet(
            TorHeaders.All.ToArray(),
            Full("1", "48712219406", "MAASIKAS", "", "eile", "1", "Tööleping", "2", "Kehtiv", "", "12223047", "A OÜ"),
            Full("2", "38810140309", "TAMM", "JAAN", "01.01.2020", "1", "Tööleping", "2", "Kehtiv", "", "10235688", "B OÜ"),
            Tables.Row("", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "")); // blank, dropped

        var tor = TorParser.Parse(sheet, TorParser.Detect(sheet, out _)!);

        Assert.Equal(2, tor.Entries.Count);
        Assert.Contains("name incomplete", tor.Entries[0].Problem);
        Assert.Contains("not understood", tor.Entries[0].Problem);
        Assert.Null(tor.Entries[0].StartDate);
        Assert.Contains(tor.Problems, p => p.Code == TorParser.NoName && p.RowNo == 2);
        Assert.Contains(tor.Problems, p => p.Code == ImportProblem.BadDate && p.RowNo == 2);
        Assert.Contains(tor.Problems, p => p.Code == TorParser.SeveralEmployers);
        Assert.Equal(["12223047", "10235688"], tor.EmployerCodes);
    }

    [Fact]
    public void A_tor_header_key_forgives_spacing_and_case_but_the_statement_layouts_do_not()
    {
        Assert.Equal(TorHeaders.Key("Tööandja /Tegeliku tööandja nimetus"), TorHeaders.Key("  TÖÖANDJA / TEGELIKU  TÖÖANDJA NIMETUS "));
        Assert.Equal(TorHeaders.Key("Töötaja isikukood"), TorHeaders.Key("Töötaja isikukood")); // decomposed ö
    }
}
