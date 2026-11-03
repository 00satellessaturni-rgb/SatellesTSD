using SatellesTSD.Core.Lookups;
using SatellesTSD.Core.Model;
using SatellesTSD.Infrastructure.Lookups;

namespace SatellesTSD.Tests;

/// <summary>The two registers' pages read as the workbook read them, and the date rules of the findings.</summary>
public class LookupParserTests
{
    private static readonly DateOnly Today = new(2026, 11, 4);

    [Fact]
    public void Emta_nobody_found_means_resident_on_every_day()
    {
        var result = EmtaResidencyParser.Parse(LookupFixtures.EmtaNotFound, Today);

        Assert.True(result.Succeeded, result.Failure);
        var finding = result.Finding!;
        Assert.Empty(finding.Periods);
        Assert.Equal(Today, finding.AsOf);
        Assert.True(finding.IsResidentOn(new DateOnly(2026, 10, 31)));
        Assert.Equal(Residency.Resident, finding.ResidencyOn(new DateOnly(2020, 1, 1)));
        Assert.Contains("Otsingutulemustele vastavaid isikuid ei leitud", result.Page);
        Assert.DoesNotContain("<", result.Page); // the page is kept as text
    }

    [Fact]
    public void Emta_found_gives_the_periods_the_as_of_date_and_the_rest_as_notes()
    {
        var result = EmtaResidencyParser.Parse(LookupFixtures.EmtaFoundTwoPeriods, Today);

        Assert.True(result.Succeeded, result.Failure);
        var finding = result.Finding!;
        Assert.Equal(new DateOnly(2026, 11, 4), finding.AsOf);
        Assert.Equal([new DatePeriod(new DateOnly(2024, 1, 1), new DateOnly(2025, 12, 31)), new DatePeriod(new DateOnly(2026, 3, 1), null)], finding.Periods);
        Assert.Equal(["Residentsustõend: Soome, kehtiv 01.01.2026 - 31.12.2026"], finding.Notes);

        // the workbook's rule: non-resident on a day a period covers, the end day included, an open end for good
        Assert.False(finding.IsResidentOn(new DateOnly(2024, 1, 1)));
        Assert.False(finding.IsResidentOn(new DateOnly(2025, 12, 31)));
        Assert.True(finding.IsResidentOn(new DateOnly(2026, 1, 1)));
        Assert.True(finding.IsResidentOn(new DateOnly(2026, 2, 28)));
        Assert.False(finding.IsResidentOn(new DateOnly(2026, 3, 1)));
        Assert.False(finding.IsResidentOn(new DateOnly(2030, 1, 1)));
        Assert.True(finding.IsResidentOn(new DateOnly(2023, 12, 31)));
        Assert.Equal(Residency.NonResident, finding.ResidencyOn(new DateOnly(2026, 10, 31)));
    }

    [Fact]
    public void Emta_found_reads_a_block_of_divs_too_and_refuses_what_it_cannot_read()
    {
        var divs = EmtaResidencyParser.Parse(LookupFixtures.EmtaFoundDivs, Today);
        Assert.Equal([new DatePeriod(new DateOnly(2026, 6, 15), null)], divs.Finding!.Periods);

        Assert.Contains("is not a date", EmtaResidencyParser.Parse(LookupFixtures.EmtaFoundBadDate, Today).Failure);
        Assert.Equal("table size mismatch", EmtaResidencyParser.Parse(LookupFixtures.EmtaFoundWideRow, Today).Failure);

        // the register found the person, yet no row reads as a period (a shape the parser does not know): never "resident" by default
        var noPeriod = EmtaResidencyParser.Parse(LookupFixtures.EmtaFoundTwoPeriods.Replace("<td></td><td> 01.01.2024 </td>", "<td>X</td><td> 01.01.2024 </td>").Replace("<td></td><td>01.03.2026</td>", "<td>Y</td><td>01.03.2026</td>"), Today);
        Assert.False(noPeriod.Succeeded);
        Assert.Contains("no period could be read", noPeriod.Failure);
        Assert.Equal(EmtaResidencyParser.NoContent, EmtaResidencyParser.Parse("<html><body><p>Hooldus</p></body></html>", Today).Failure);
        Assert.Contains("no known answer", EmtaResidencyParser.Parse(LookupFixtures.EmtaCsrfRefused, Today).Failure);
        Assert.Contains("the period table is missing", EmtaResidencyParser.Parse(LookupFixtures.EmtaFoundTwoPeriods.Replace("alt-block", "other"), Today).Failure);
    }

    [Fact]
    public void Pensionikeskus_obliged_by_birth_year_is_a_member_from_the_withholding_start_at_the_default_rate()
    {
        var result = PensionikeskusParser.Parse(LookupFixtures.PensionObliged);

        Assert.True(result.Succeeded, result.Failure);
        var finding = result.Finding!;
        Assert.Equal((PensionStatus.ObligedByBirthYear, (DateOnly?)new DateOnly(2019, 1, 1), (DateOnly?)null), (finding.Status, finding.WithholdingStart, finding.ContributionsEnded));
        Assert.Equal([new RatePeriod(new DateOnly(2019, 1, 1), null, 0.02m, "Vaikimisi määr")], finding.Rates);
        Assert.True(finding.MemberOn(new DateOnly(2026, 10, 31)));
        Assert.False(finding.MemberOn(new DateOnly(2018, 12, 31)));
        Assert.Equal(0.02m, finding.RateOn(new DateOnly(2026, 10, 31)));
        Assert.Null(finding.RateOn(new DateOnly(2018, 6, 1)));
        Assert.Contains("Päring ei kontrolli isiku residentsust", result.Page);
    }

    [Fact]
    public void Pensionikeskus_joined_with_several_rate_rows_gives_the_rate_of_the_day()
    {
        var finding = PensionikeskusParser.Parse(LookupFixtures.PensionJoinedTwoRates).Finding!;

        Assert.Equal(PensionStatus.Joined, finding.Status);
        Assert.Equal(new DateOnly(2010, 9, 1), finding.WithholdingStart);
        Assert.Equal(3, finding.Rates.Count);
        Assert.Equal([new DateOnly(2025, 1, 1), new DateOnly(2026, 1, 1), DateOnly.MinValue], finding.Rates.Select(r => r.Start)); // the page's order; "Kuni 31.12.2024" ran up to that day
        Assert.Equal(new DateOnly(2024, 12, 31), finding.Rates[2].End);
        Assert.Equal(["Avalduse alusel", "Avalduse alusel", "Vaikimisi määr"], finding.Rates.Select(r => r.Explanation));
        Assert.Equal(0.04m, finding.RateOn(new DateOnly(2025, 6, 30)));
        Assert.Equal(0.04m, finding.RateOn(new DateOnly(2025, 12, 31)));
        Assert.Equal(0.06m, finding.RateOn(new DateOnly(2026, 10, 31)));
        Assert.Equal(0.02m, finding.RateOn(new DateOnly(2024, 12, 31)));
        Assert.Equal(0.02m, finding.RateOn(new DateOnly(2011, 5, 5)));
        Assert.True(finding.MemberOn(new DateOnly(2010, 9, 1)));
        Assert.False(finding.MemberOn(new DateOnly(2010, 8, 31)));
    }

    [Fact]
    public void Pensionikeskus_contributions_ended_means_not_a_member_from_that_day_and_not_joined_means_never()
    {
        var ended = PensionikeskusParser.Parse(LookupFixtures.PensionEnded).Finding!;
        Assert.Equal((PensionStatus.Joined, (DateOnly?)null, (DateOnly?)new DateOnly(2026, 8, 1)), (ended.Status, ended.WithholdingStart, ended.ContributionsEnded));
        Assert.False(ended.MemberOn(new DateOnly(2026, 10, 31))); // and no withholding start on the page: never a member here
        var endedWithStart = ended with { WithholdingStart = new DateOnly(2019, 1, 1) };
        Assert.True(endedWithStart.MemberOn(new DateOnly(2026, 7, 31)));
        Assert.False(endedWithStart.MemberOn(new DateOnly(2026, 8, 1))); // the day the contributions ended is no member's day (the workbook's rule)

        var notJoined = PensionikeskusParser.Parse(LookupFixtures.PensionNotJoined).Finding!;
        Assert.Equal(PensionStatus.NotJoined, notJoined.Status);
        Assert.Empty(notJoined.Rates);
        Assert.False(notJoined.MemberOn(new DateOnly(2026, 10, 31)));
        Assert.Null(notJoined.RateOn(new DateOnly(2026, 10, 31)));
    }

    [Fact]
    public void Pensionikeskus_refuses_a_page_it_does_not_know()
    {
        Assert.Contains("no known answer", PensionikeskusParser.Parse(LookupFixtures.PensionUnknownPage).Failure);
        Assert.Equal(PensionikeskusParser.NoContent, PensionikeskusParser.Parse("<html><body><p>Hooldus</p></body></html>").Failure);
        Assert.Contains("rate row", PensionikeskusParser.Parse(LookupFixtures.PensionObliged.Replace("<td>2%</td>", "<td>kaks</td>")).Failure);

        // a date line without a date the parser can read, and a joined person without any date: the workbook's "content error", never "not a member"
        Assert.Contains("could not be read", PensionikeskusParser.Parse(LookupFixtures.PensionObliged.Replace("algus: 01.01.2019", "algus: 1.1.2019")).Failure);
        Assert.Contains("could not be read", PensionikeskusParser.Parse(LookupFixtures.PensionEnded.Replace("Alates 01.08.2026 on", "Alates eile on")).Failure);
        Assert.Contains("names no withholding start", PensionikeskusParser.Parse(LookupFixtures.PensionJoinedTwoRates.Replace("<div class=\"font-weight-bold my-2\">Maksete kinnipidamise algus: 01.09.2010</div>", "")).Failure);
    }

    [Fact]
    public void Pensionikeskus_reads_a_date_written_in_bold_as_part_of_its_line()
    {
        var bold = LookupFixtures.PensionJoinedTwoRates.Replace("algus: 01.09.2010", "algus: <b>01.09.2010</b>");
        var finding = PensionikeskusParser.Parse(bold).Finding!;
        Assert.Equal(new DateOnly(2010, 9, 1), finding.WithholdingStart);

        var boldEnd = LookupFixtures.PensionEnded.Replace("Alates 01.08.2026 on", "Alates <strong>01.08.2026</strong> on");
        Assert.Equal(new DateOnly(2026, 8, 1), PensionikeskusParser.Parse(boldEnd).Finding!.ContributionsEnded);

        // the status phrase inside a bold tag, as a page might set it
        var boldPhrase = LookupFixtures.PensionNotJoined.Replace("<div class=\"my-2\">Isik ei ole liitunud kohustusliku kogumispensioniga.</div>", "<div class=\"my-2\"><b>Isik ei ole liitunud kohustusliku kogumispensioniga.</b></div>");
        Assert.Equal(PensionStatus.NotJoined, PensionikeskusParser.Parse(boldPhrase).Finding!.Status);
    }

    [Theory]
    [InlineData("01.01.2019", 2019, 1, 1)]
    [InlineData(" 31.12.2025 ", 2025, 12, 31)]
    public void Register_dates_are_day_month_year(string text, int year, int month, int day) => Assert.Equal(new DateOnly(year, month, day), LookupDates.Parse(text));

    [Fact]
    public void Register_dates_are_found_inside_text_and_rates_read_as_fractions()
    {
        Assert.Null(LookupDates.Parse("2019-01-01"));
        Assert.Null(LookupDates.Parse("31.13.2025"));
        Assert.Equal([new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)], LookupDates.FindAll("01.01.2025 - 31.12.2025"));
        Assert.Equal([new DateOnly(2026, 11, 4)], LookupDates.FindAll("Mitteresidentide registrist leitud isikud seisuga 04.11.2026"));
        Assert.Empty(LookupDates.FindAll("Alates 101.01.2019")); // a digit stuck to the date is not a date
        Assert.Empty(LookupDates.FindAll(null));
        Assert.Equal(0.02m, LookupDates.ParseRate("2%"));
        Assert.Equal(0.04m, LookupDates.ParseRate(" 4 % "));
        Assert.Equal(0.06m, LookupDates.ParseRate("0.06"));
        Assert.Equal(0.06m, LookupDates.ParseRate("0,06"));
        Assert.Null(LookupDates.ParseRate("6"));
        Assert.Null(LookupDates.ParseRate("kaks"));
        Assert.Null(LookupDates.ParseRate(""));
    }

    [Fact]
    public void A_period_covers_its_days_and_an_open_rate_period_runs_on()
    {
        var closed = new DatePeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        Assert.True(closed.Covers(new DateOnly(2026, 1, 1)));
        Assert.True(closed.Covers(new DateOnly(2026, 1, 31)));
        Assert.False(closed.Covers(new DateOnly(2026, 2, 1)));
        Assert.False(closed.Covers(new DateOnly(2025, 12, 31)));
        Assert.True(new DatePeriod(new DateOnly(2026, 1, 1), null).Covers(new DateOnly(2099, 1, 1)));
        Assert.Equal("01.01.2026 –", new DatePeriod(new DateOnly(2026, 1, 1), null).ToString());
        Assert.True(new RatePeriod(new DateOnly(2026, 1, 1), null, 0.06m, null).Covers(new DateOnly(2027, 5, 5)));
        Assert.False(new RatePeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 0.06m, null).Covers(new DateOnly(2027, 1, 1)));
    }
}
