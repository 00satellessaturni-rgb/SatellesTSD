using SatellesTSD.Core.Import;
using SatellesTSD.Core.Matching;

namespace SatellesTSD.Tests;

/// <summary>The two passes over one company's month: the Otsingu kontroll in the workbook's order and words, the sums,
/// the sub-tables, the problems.</summary>
public class PeriodMatcherTests
{
    private static readonly PeriodMatcher Matcher = new(MatchCatalog.Default);

    private static TorEntry Entry(int rowNo, string? code, string last, string first, string typeId = "1", string stateId = "2", DateOnly? start = null, DateOnly? end = null) =>
        new(rowNo, rowNo.ToString(), code, first, last, "EE", start ?? new DateOnly(2024, 1, 1), end, typeId, null, stateId, null, null, null, "10235688", "Näidis OÜ", [], null);

    private static StatementRow Row(int rowNo, string? date, string? name, decimal? amount, string? description, Direction direction = Direction.Debit, string? skip = null) =>
        new(rowNo, date is null ? null : DateOnly.ParseExact(date, "yyyy-MM-dd"), date ?? "kolmapäev", "EE49", name, null, amount, amount?.ToString(), "EUR", description, null, null, direction,
            direction == Direction.Debit ? "D" : direction == Direction.Credit ? "C" : null, skip, null, $"hash{rowNo}|{name}|{amount}|{description}", []);

    private static readonly IReadOnlyList<TorEntry> Register =
    [
        Entry(2, "48712219406", "MAASIKAS", "MARI"),
        Entry(3, "38810140309", "TAMM", "JAAN", "600", "4", new DateOnly(2024, 1, 1), new DateOnly(2026, 9, 30)),
        Entry(4, "38810140309", "TAMM", "JAAN", "1", "2", new DateOnly(2026, 10, 15)),
        Entry(5, "39001010000", "KIIASHKO", "SERHII"),
        Entry(6, "37001010001", "TAMM", "JAAN"), // a namesake with another code
        Entry(7, "36001010002", "KASK", "PEETER", "1", "4", new DateOnly(2020, 1, 1), new DateOnly(2026, 8, 31)), // ended before the month
    ];

    private static readonly MatchSource Swedbank = new("Swedbank *2353", "swedbank-et", LayoutKind.Bank,
    [
        Row(2, "2026-10-05", "Mari Maasikas", -905.92m, "Palk"),
        Row(3, "2026-10-06", "Mari Maasikas", -200m, "Dividendid"),
        Row(4, "2026-10-07", "Jaan Tamm", -300m, "Septembrikuu palk"),
        Row(5, "2026-10-08", "Sergei Kiiashko", -50m, "Lähetus"),
        Row(6, "2026-09-30", "Jaan Tamm", -700m, "Augusti palk"),
        Row(7, null, "Mari Maasikas", -10m, "Palk"),
        Row(8, "2026-10-09", "Workform OÜ", 5000m, "Laen 25/09/2026", Direction.Credit),
        Row(9, "2026-10-10", "RAHANDUSMINISTEERIUM", -55m, "ETeenindus isik 37903140300 taotlus"),
        Row(10, "2026-10-11", null, -26.99m, "547882******8124 Tradehouse", skip: SkipReason.NoName),
        Row(11, null, null, null, null, Direction.Unknown, SkipReason.Blank),
        Row(12, "2026-10-12", "Saaja/maksja nimi", null, "Selgitus", Direction.Unknown, SkipReason.Header),
        Row(13, "2026-10-13", "Peeter Kask", -100m, "Lõpparve"),
        Row(14, "2026-09-02", "AN®ELIKA BABITŠEVA", -1000m, "Laen"),
        Row(15, "2026-10-31", null, 12345.67m, "Lõppsaldo", Direction.Unknown, SkipReason.NoName),
    ]);

    private static readonly MatchSource Kassa = new("Kassa", "kassa", LayoutKind.Table,
    [
        Row(2, "2026-10-31", "Mari Maasikas", 123.45m, "2026-10-01"),
        Row(3, "2026-10-31", "Mari Maasikas", 10m, "2026-10-01", Direction.Unknown),
    ]);

    private static MatchResult Run() => Matcher.Run(2026, 10, MatchPerson.FromTor(Register, 2026, 10), MatchSource.Order([Swedbank, Kassa]));

    [Fact]
    public void Persons_are_one_per_code_with_the_employment_of_the_month()
    {
        var persons = MatchPerson.FromTor(Register, 2026, 10);

        Assert.Equal(["48712219406", "38810140309", "39001010000", "37001010001", "36001010002"], persons.Select(p => p.Key));
        var jaan = persons[1];
        Assert.Equal(2, jaan.Entries.Count);
        Assert.Equal(4, jaan.ActiveEntry!.RowNo); // the valid one, not the ended one that also touches the month
        Assert.Equal("1", jaan.Entry.TypeId);
        Assert.Null(persons[4].ActiveEntry);
        Assert.Equal(7, persons[4].Entry.RowNo);
        Assert.Equal([0, 1, 2, 3, 4], persons.Select(p => p.Order));

        var codeless = MatchPerson.FromTor([Entry(2, null, "KASK", "MARI"), Entry(3, null, "Kask", "Mari"), Entry(4, "1", "KASK", "MARI")], 2026, 10);
        Assert.Equal(["KASK|MARI", "1"], codeless.Select(p => p.Key)); // a coded row after a codeless one of the same names was a second person to the workbook
        Assert.Equal(2, codeless[0].Entries.Count);

        // a codeless row after a coded one of the same names was a yellow duplicate to the workbook: it joins that person
        var folded = MatchPerson.FromTor([Entry(2, "1", "TAMM", "JAAN"), Entry(3, null, "Tamm", "Jaan "), Entry(4, "2", "TAMM", "JAAN")], 2026, 10);
        Assert.Equal(["1", "2"], folded.Select(p => p.Key));
        Assert.Equal([2, 3], folded[0].Entries.Select(e => e.RowNo));
    }

    [Fact]
    public void Sources_are_ordered_tables_first_and_numbered_when_a_name_repeats()
    {
        var a = new MatchSource("LHV pank *1757", "lhv-et-short", LayoutKind.Bank, []);
        var b = new MatchSource("Kassa", "kassa", LayoutKind.Table, []);
        var c = new MatchSource("LHV pank *1757", "lhv-et-short", LayoutKind.Bank, []);
        var d = new MatchSource("lhv pank *1757", "lhv-et-long", LayoutKind.Bank, []);

        Assert.Equal(["Kassa", "LHV pank *1757", "LHV pank *1757 #2", "lhv pank *1757 #3"], MatchSource.Order([a, b, c, d]).Select(s => s.Name));
        Assert.Throws<MatchException>(() => Matcher.Run(2026, 10, [], [a, c]));
    }

    [Fact]
    public void The_employee_pass_writes_the_workbooks_rows_in_its_order_and_words()
    {
        var result = Run();

        var mari = result.HitsOf("48712219406");
        Assert.Equal(["Kassa [2]", "Kassa [3]", "Swedbank *2353 [2]", "Swedbank *2353 [3]", "Swedbank *2353 [7]"], mari.Select(h => h.Reference));
        Assert.Equal(["[Palgad]", "viga: [Palgad] D/C kontroll", "[Palgad]", "[Dividendid]", "viga: kehtetu kuupaev [kolmapäev]"], mari.Select(h => h.Text));
        Assert.Equal([true, false, true, true, false], mari.Select(h => h.Counted));
        Assert.All(mari, h => Assert.False(h.MaskUsed));

        var jaan = result.HitsOf("38810140309");
        Assert.Equal(["Swedbank *2353 [4]", "Swedbank *2353 [6]"], jaan.Select(h => h.Reference));
        Assert.Equal(["[Palgad]", "kuupaev ei kuulu perioodi [2026-10]"], jaan.Select(h => h.Text));
        Assert.True(jaan[0].InPeriod);
        Assert.False(jaan[1].InPeriod);

        var serhii = Assert.Single(result.HitsOf("39001010000"));
        Assert.Equal(("Swedbank *2353 [5]", "[Kompensatsioon]", "KIIASHKO SERGEI", true), (serhii.Reference, serhii.Text, serhii.FullMask, serhii.MaskUsed));

        var peeter = Assert.Single(result.HitsOf("36001010002"));
        Assert.Equal("[Palgad]", peeter.Text); // searched although no employment covers the month: a final payment is a payment

        // the namesake found the same rows by his exact name: counted for neither, said as a problem
        var namesake = result.HitsOf("37001010001");
        Assert.Equal(["Swedbank *2353 [4]", "Swedbank *2353 [6]"], namesake.Select(h => h.Reference));
        Assert.All(jaan.Concat(namesake).Where(h => h.Row.RowNo == 4), h =>
        {
            Assert.False(h.Counted);
            Assert.Equal([MatchProblem.SharedRow], h.Flags);
        });
        Assert.Contains(result.Problems, p => p.Code == MatchProblem.SharedRow && p.RowNo == 4 && p.Message.Contains("TAMM JAAN"));

        // the hits of the employee pass come before the second pass, persons in register order
        Assert.Equal(["48712219406", "48712219406", "48712219406", "48712219406", "48712219406", "38810140309", "38810140309", "39001010000", "37001010001", "37001010001", "36001010002"],
            result.Hits.Where(h => h.PersonKey is not null).Select(h => h.PersonKey));
    }

    [Fact]
    public void The_second_pass_lists_every_row_nobody_matched_in_source_order()
    {
        var result = Run();

        var unmatched = result.Unmatched;
        Assert.Equal(["Swedbank *2353 [8]", "Swedbank *2353 [9]", "Swedbank *2353 [10]", "Swedbank *2353 [11]", "Swedbank *2353 [14]"], unmatched.Select(h => h.Reference));
        Assert.Equal(["viga: [Laenud] D/C kontroll", "[Trahvid]", "", "", "[Laenud]"], unmatched.Select(h => h.Text));
        Assert.Equal([false, true, false, false, true], unmatched.Select(h => h.Counted));
        Assert.Equal([true, true, true, false, false], unmatched.Select(h => h.InPeriod)); // the workbook categorised rows of any month; the flag tells
        Assert.Equal(result.Hits.Count - unmatched.Count, result.Hits.ToList().IndexOf(unmatched[0]));
        Assert.Contains(result.Problems, p => p.Code == MatchProblem.HeaderRow && p.RowNo == 12);
        Assert.DoesNotContain(result.Hits, h => h.Row.RowNo == 12); // a pasted header row is left out
        Assert.DoesNotContain(result.Hits, h => h.Row.RowNo == 15); // the balance line after the last named row: the workbook never reached it
    }

    [Fact]
    public void A_row_already_in_an_earlier_source_is_a_duplicate_listed_not_searched_not_counted()
    {
        // two exports of one account that overlap: the first carries the salary and two identical fees without a bank id;
        // the second carries the salary again (the same fingerprint and bank id), one of the fees, and a payment of its own
        var first = new MatchSource("LHV pank *1757", "lhv-et-short", LayoutKind.Bank,
        [
            Row(2, "2026-10-05", "Mari Maasikas", -905.92m, "Palk") with { RowHash = "h-salary", TransactionId = "T1" },
            Row(3, "2026-10-06", "Mari Maasikas", -0.38m, "Teenustasu") with { RowHash = "h-fee" },
            Row(4, "2026-10-06", "Mari Maasikas", -0.38m, "Teenustasu") with { RowHash = "h-fee" },
        ]);
        var second = new MatchSource("LHV pank *1757 #2", "lhv-et-short", LayoutKind.Bank,
        [
            Row(2, "2026-10-05", "Mari Maasikas", -905.92m, "Palk") with { RowHash = "h-salary", TransactionId = "T1" },
            Row(3, "2026-10-06", "Mari Maasikas", -0.38m, "Teenustasu") with { RowHash = "h-fee" },
            Row(4, "2026-10-20", "Mari Maasikas", -100m, "Lisatasu") with { RowHash = "h-bonus", TransactionId = "T2" },
        ]);

        var result = Matcher.Run(2026, 10, MatchPerson.FromTor(Register, 2026, 10), [first, second]);

        // the first source's rows are all the person's; the second's salary and one fee are copies, the bonus is new
        var mari = result.Hits.Where(h => h.PersonKey == "48712219406").Select(h => h.Reference).ToList();
        Assert.Equal(["LHV pank *1757 [2]", "LHV pank *1757 [3]", "LHV pank *1757 [4]", "LHV pank *1757 #2 [4]"], mari);
        var copies = result.Hits.Where(h => h.Flags.Contains(MatchProblem.DuplicateRow)).ToList();
        Assert.Equal(["LHV pank *1757 #2 [2]", "LHV pank *1757 #2 [3]"], copies.Select(h => h.Reference));
        Assert.All(copies, h => Assert.Equal((null, false, "duplikaat: "), (h.PersonKey, h.Counted, h.Text[..11])));
        Assert.Equal("duplikaat: LHV pank *1757 [2]", copies[0].Text);
        Assert.Equal("duplikaat: LHV pank *1757 [3]", copies[1].Text); // the first fee: one copy for one row, never both
        Assert.Equal(2, result.Problems.Count(p => p.Code == MatchProblem.DuplicateRow));
        Assert.Equal(905.92m + 100m, result.SalaryTotal("48712219406")); // the salary once, the bonus once

        // the same fingerprints in the other order: the copies are the ones that come later
        var reversed = Matcher.Run(2026, 10, MatchPerson.FromTor(Register, 2026, 10), [second with { Name = "A" }, first with { Name = "B" }]);
        Assert.Equal(["B [2]", "B [3]"], reversed.Hits.Where(h => h.Flags.Contains(MatchProblem.DuplicateRow)).Select(h => h.Reference));

        // a third export carrying the salary again: a copy too — an original is never used up
        var third = new MatchSource("C", "lhv-et-short", LayoutKind.Bank, [Row(2, "2026-10-05", "Mari Maasikas", -905.92m, "Palk") with { RowHash = "h-salary", TransactionId = "T1" }]);
        var three = Matcher.Run(2026, 10, MatchPerson.FromTor(Register, 2026, 10), [first, second, third]);
        Assert.Equal(["LHV pank *1757 #2 [2]", "LHV pank *1757 #2 [3]", "C [2]"], three.Hits.Where(h => h.Flags.Contains(MatchProblem.DuplicateRow)).Select(h => h.Reference));
        Assert.Equal(905.92m + 100m, three.SalaryTotal("48712219406"));

        // the accountant's hand-made copy: Excel rounded the sixteen-digit bank id, so the ids differ — such an id tells
        // nothing, the fingerprint does; a short id that differs keeps two rows apart
        var bank = new MatchSource("A", "swedbank-et", LayoutKind.Bank, [Row(2, "2026-10-05", "Mari Maasikas", -905.92m, "Palk") with { RowHash = "h-salary", TransactionId = "2026100500000456" }]);
        var copy = new MatchSource("B", "swedbank-et", LayoutKind.Bank, [Row(2, "2026-10-05", "Mari Maasikas", -905.92m, "Palk") with { RowHash = "h-salary", TransactionId = "2026100500000450" }]);
        Assert.Equal(["B [2]"], Matcher.Run(2026, 10, MatchPerson.FromTor(Register, 2026, 10), [bank, copy]).Hits.Where(h => h.Flags.Contains(MatchProblem.DuplicateRow)).Select(h => h.Reference));
        var other = new MatchSource("B", "lhv-et-short", LayoutKind.Bank, [Row(2, "2026-10-05", "Mari Maasikas", -905.92m, "Palk") with { RowHash = "h-salary", TransactionId = "T2" }]);
        Assert.Empty(Matcher.Run(2026, 10, MatchPerson.FromTor(Register, 2026, 10), [first, other]).Hits.Where(h => h.Flags.Contains(MatchProblem.DuplicateRow)));

        // two identical fees in the first export and both again in the second: each copy names its own original
        var fees = new MatchSource("B", "lhv-et-short", LayoutKind.Bank,
        [
            Row(3, "2026-10-06", "Mari Maasikas", -0.38m, "Teenustasu") with { RowHash = "h-fee" },
            Row(4, "2026-10-06", "Mari Maasikas", -0.38m, "Teenustasu") with { RowHash = "h-fee" },
        ]);
        Assert.Equal(["duplikaat: LHV pank *1757 [3]", "duplikaat: LHV pank *1757 [4]"], Matcher.Run(2026, 10, MatchPerson.FromTor(Register, 2026, 10), [first, fees]).Hits.Where(h => h.Flags.Contains(MatchProblem.DuplicateRow)).Select(h => h.Text));
    }

    [Fact]
    public void An_unreadable_date_is_shown_as_the_text_before_its_first_space()
    {
        var source = new MatchSource("LHV pank *1757", "lhv-et-short", LayoutKind.Bank,
            [Row(2, null, "Mari Maasikas", -500m, "palk") with { DateText = "31.13.2026 12:00" }]);

        var result = Matcher.Run(2026, 10, MatchPerson.FromTor([Entry(2, "1", "MAASIKAS", "MARI")], 2026, 10), [source]);

        Assert.Equal("viga: kehtetu kuupaev [31.13.2026]", Assert.Single(result.Hits).Text);
    }

    [Fact]
    public void At_the_alias_level_the_other_persons_exact_row_stays_theirs()
    {
        var register = new[] { Entry(2, "1", "TAMM", "SERHII"), Entry(3, "2", "TAMM", "SERGII") };
        var source = new MatchSource("LHV pank *1757", "lhv-et-short", LayoutKind.Bank,
            [Row(2, "2026-10-05", "Sergei Tamm", -500m, "palk"), Row(3, "2026-10-05", "Sergii Tamm", -700m, "palk")]);

        var result = Matcher.Run(2026, 10, MatchPerson.FromTor(register, 2026, 10), [source]);

        Assert.Equal((500m, 700m), (result.SalaryTotal("1"), result.SalaryTotal("2")));
        Assert.Equal(["TAMM SERGEI", ""], result.Hits.Select(h => h.FullMask));
        Assert.DoesNotContain(result.Problems, p => p.Code == MatchProblem.SharedRow);
        Assert.Contains(result.Problems, p => p.Code == MatchProblem.AmbiguousMask && p.PersonKey == "1" && p.Message.Contains("TAMM SERGII"));
    }

    [Fact]
    public void The_sums_and_sub_tables_are_the_reports()
    {
        var result = Run();

        Assert.Equal(905.92m, result.SalarySum("48712219406", "Swedbank *2353"));
        Assert.Equal(123.45m, result.SalarySum("48712219406", "Kassa"));
        Assert.Equal(1029.37m, result.SalaryTotal("48712219406"));
        Assert.Equal(0m, result.SalarySum("38810140309", "Swedbank *2353")); // shared with the namesake
        Assert.Equal(100m, result.SalaryTotal("36001010002"));

        var laenud = result.SubTable("Laenud");
        var row = Assert.Single(laenud);
        Assert.Equal((null, "AN®ELIKA BABITŠEVA", 1000m), (row.PersonKey, row.Name, row.Total));
        Assert.Equal(1000m, row.BySource["Swedbank *2353"]);

        var dividendid = Assert.Single(result.SubTable("Dividendid"));
        Assert.Equal(("48712219406", "MAASIKAS MARI", 200m), (dividendid.PersonKey, dividendid.Name, dividendid.Total));
        Assert.Empty(result.SubTable("Üür"));
        Assert.Single(result.SubTable("trahvid")); // by name, case-insensitively
    }

    [Fact]
    public void The_problems_name_what_the_accountant_should_look_at()
    {
        var result = Run();

        var codes = result.Problems.Select(p => p.Code).ToList();
        Assert.Contains(MatchProblem.SeveralEntries, codes);
        Assert.Contains(MatchProblem.NoEmploymentInMonth, codes);
        Assert.Contains(MatchProblem.SharedRow, codes);
        Assert.Contains(MatchProblem.HeaderRow, codes);
        Assert.DoesNotContain(MatchProblem.NoFilters, codes);
        Assert.Contains(result.Problems, p => p.Code == MatchProblem.NoEmploymentInMonth && p.PersonKey == "36001010002");
        Assert.Contains(result.Problems, p => p.Code == MatchProblem.SeveralEntries && p.PersonKey == "38810140309" && p.Message.Contains("rows 3, 4"));
    }

    [Fact]
    public void A_masked_hit_the_masks_could_also_mean_is_dropped_and_said_and_the_rows_fall_to_the_second_pass()
    {
        var register = new[] { Entry(2, "1", "TAMM", "JAAN"), Entry(3, "2", "TAMME", "JAAN") };
        var source = new MatchSource("LHV pank *1757", "lhv-et-short", LayoutKind.Bank, [Row(2, "2026-10-05", "Jaan Tamme", -500m, "palk")]);

        var result = Matcher.Run(2026, 10, MatchPerson.FromTor(register, 2026, 10), [source]);

        Assert.Equal(["2"], result.Hits.Where(h => h.PersonKey is not null).Select(h => h.PersonKey)); // the exact namesake has it
        var problem = Assert.Single(result.Problems, p => p.Code == MatchProblem.AmbiguousMask);
        Assert.Equal(("1", "LHV pank *1757"), (problem.PersonKey, problem.Source));
        Assert.Contains("TAM* JAAN", problem.Message);
        Assert.Contains("TAMME JAAN", problem.Message);

        var alone = Matcher.Run(2026, 10, MatchPerson.FromTor([Entry(2, "1", "TAMM", "JAAN")], 2026, 10), [source]);
        Assert.Equal("TAM* JAAN", Assert.Single(alone.Hits).FullMask);
    }

    [Fact]
    public void A_source_whose_layout_has_no_filters_and_a_person_without_a_name_are_said_not_searched()
    {
        var json = MatchCatalog.Default.ToJson().Replace("\"wise\": \"standard\"", "\"wise-x\": \"standard\"");
        var matcher = new PeriodMatcher(MatchCatalog.Parse(json));
        var wise = new MatchSource("TransferWise", "wise", LayoutKind.Bank, [Row(2, "2026-10-05", "Mari Maasikas", -500m, "palk")]);
        var register = new[] { Entry(2, "1", "MAASIKAS", "MARI"), Entry(3, "2", "", "PEETER") };

        var result = matcher.Run(2026, 10, MatchPerson.FromTor(register, 2026, 10), [wise]);

        Assert.Contains(result.Problems, p => p.Code == MatchProblem.NoFilters && p.Source == "TransferWise");
        var hit = Assert.Single(result.Hits, h => h.PersonKey == "1");
        Assert.Equal(("", VerdictKind.None, false), (hit.Text, hit.Verdict.Kind, hit.Counted));
        Assert.Contains(result.Problems, p => p.Code == MatchProblem.NoName && p.PersonKey == "2");
        Assert.Empty(result.HitsOf("2"));
    }

    [Fact]
    public void A_hit_whose_amount_could_not_be_read_counts_as_nothing_and_is_said()
    {
        var source = new MatchSource("LHV pank *1757", "lhv-et-short", LayoutKind.Bank, [Row(2, "2026-10-05", "Mari Maasikas", null, "palk")]);

        var result = Matcher.Run(2026, 10, MatchPerson.FromTor([Entry(2, "1", "MAASIKAS", "MARI")], 2026, 10), [source]);

        var hit = Assert.Single(result.Hits);
        Assert.Equal(("[Palgad]", false, 0m), (hit.Text, hit.Counted, hit.Amount));
        Assert.Contains(MatchProblem.BadAmount, hit.Flags);
        Assert.Contains(result.Problems, p => p.Code == MatchProblem.BadAmount && p.RowNo == 2);
        Assert.Equal(0m, result.SalaryTotal("1"));
    }

    [Fact]
    public void The_employment_types_map_the_registers_ids_to_the_codes()
    {
        Assert.Equal(("10", "120", true), (EmploymentTypes.DefaultCode("1", Core.Model.Residency.Resident), EmploymentTypes.DefaultCode("1", Core.Model.Residency.NonResident), EmploymentTypes.Find("1")!.SocialTaxMinimum));
        Assert.Equal(("17", "123", false), (EmploymentTypes.DefaultCode("600", Core.Model.Residency.Resident), EmploymentTypes.DefaultCode("600", Core.Model.Residency.NonResident), EmploymentTypes.Find("600")!.SocialTaxMinimum));
        Assert.Equal(("21", "156"), (EmploymentTypes.DefaultCode("601", Core.Model.Residency.Resident), EmploymentTypes.DefaultCode("601", Core.Model.Residency.NonResident)));
        Assert.Equal(("10", false), (EmploymentTypes.DefaultCode("501", Core.Model.Residency.Resident), EmploymentTypes.Find("501")!.SocialTaxMinimum));
        Assert.Equal(("16", (string?)null), (EmploymentTypes.DefaultCode("506", Core.Model.Residency.Resident), EmploymentTypes.DefaultCode("506", Core.Model.Residency.NonResident)));
        Assert.Null(EmploymentTypes.DefaultCode("999", Core.Model.Residency.Resident));
        Assert.Null(EmploymentTypes.DefaultCode(null, Core.Model.Residency.Resident));
        Assert.All(EmploymentTypes.All.Where(t => t.ResidentCode is not null), t => Core.Tax.PaymentTypes.Get(t.ResidentCode!));
        Assert.All(EmploymentTypes.All.Where(t => t.NonResidentCode is not null), t => Core.Tax.PaymentTypes.Get(t.NonResidentCode!));
    }
}
