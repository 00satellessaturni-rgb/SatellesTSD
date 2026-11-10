using SatellesTSD.Core.Declare;
using SatellesTSD.Core.Import;
using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Report;
using SatellesTSD.Core.Review;
using SatellesTSD.Core.Store;
using SatellesTSD.Core.Tax;
using SatellesTSD.Infrastructure;

namespace SatellesTSD.Tests;

/// <summary>The report-details workbook: the sheets in the workbook's order with its headers, the numbers from the
/// review (the accountant's word, not the run's), the statement copy with "Vaste leitud" for everyone found, TOR
/// kontroll's texts under the statement they came from, the declared lines, the file written and read back.</summary>
public class ReportTests
{
    private static readonly DateTime At = new(2026, 11, 4, 10, 0, 0);

    private sealed class FixedClock(DateTime at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(at, DateTimeKind.Utc), TimeSpan.Zero);

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class Box : IDisposable
    {
        public required SqliteStore Store { get; init; }
        public required Company Company { get; init; }
        public required Period Period { get; init; }
        public required ReviewWorkspace Workspace { get; init; }
        public required PeriodDeclarer Declarer { get; init; }
        public required ReportBuilder Builder { get; init; }

        public ReviewView View => Workspace.Load(Period.Id);

        public ReviewPayment Salary => View.Persons[0].Payments.Single(p => !p.IsByHand && p.Payee == "Mari Maasikas");

        public ReviewPayment Stranger => View.Unmatched.Payments.Single(p => p.Payee == "Kinnisvara OÜ");

        public ReportBook Build() => Builder.Build(Period.Id);

        public void Dispose() => Store.Dispose();
    }

    /// <summary>The fixture's month run and reviewed: Mari a checked resident outside the II pillar with the exemption applied.</summary>
    private static Box Ready(bool run = true)
    {
        var (store, company, period, _) = ReviewFixtures.Setup();
        if (run)
        {
            new MatchRunner(store).Run(period.Id);
        }

        var clock = new FixedClock(At);
        var workspace = new ReviewWorkspace(store, TaxRates.Year2026, clock);
        if (run)
        {
            var mari = workspace.Load(period.Id).Persons[0].Person!;
            workspace.SavePerson(mari with { FundedPensionMember = false, BasicExemptionApplied = true, BasicExemptionMonthly = 700m, SocialTaxMinimumApplies = false }, confirmResidency: true);
        }

        var declarer = new PeriodDeclarer(store, TaxRates.Year2026, clock);
        return new Box
        {
            Store = store, Company = company, Period = period, Workspace = workspace, Declarer = declarer,
            Builder = new ReportBuilder(store, workspace, declarer, clock),
        };
    }

    private static string[] Texts(ReportSheet sheet, int rowNo) => sheet.Row(rowNo).Cells.Select(c => c.Kind switch
    {
        ReportCellKind.Number => c.Number.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
        ReportCellKind.Date => c.Date!.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        ReportCellKind.Empty => "",
        _ => c.Text ?? "",
    }).ToArray();

    [Fact]
    public void The_check_day_is_the_earlier_of_the_two_and_the_type_falls_back_to_the_card()
    {
        var company = Guid.NewGuid();
        var person = Person.New(company, "Mari", "Maasikas", "48712219406");
        var early = new DateOnly(2026, 11, 3);
        var late = new DateOnly(2026, 11, 25);

        // the day since which every fact has been checked: the earlier one, whichever fact it belongs to
        Assert.Null(ReportBuilder.CheckedOn(null));
        Assert.Null(ReportBuilder.CheckedOn(person));
        Assert.Equal(early, ReportBuilder.CheckedOn(person with { ResidencyCheckedOn = early, PensionCheckedOn = late }));
        Assert.Equal(early, ReportBuilder.CheckedOn(person with { ResidencyCheckedOn = late, PensionCheckedOn = early }));
        Assert.Equal(late, ReportBuilder.CheckedOn(person with { ResidencyCheckedOn = late })); // the one there is
        Assert.Equal(late, ReportBuilder.CheckedOn(person with { PensionCheckedOn = late }));

        // the type shown: the card's when the card says so; else the register entry's; else the card's for a person the register does not list
        var typeId = EmploymentTypes.All[1].TorTypeId;
        var typeName = EmploymentTypes.Find(typeId)!.Name;
        Assert.Null(ReportBuilder.EmploymentTypeShown(person, null));
        Assert.Null(ReportBuilder.EmploymentTypeShown(null, null));
        Assert.Equal(typeName, ReportBuilder.EmploymentTypeShown(person with { TorEmploymentTypeId = typeId }, null));
        Assert.Equal(typeName, ReportBuilder.EmploymentTypeShown(person with { TorEmploymentTypeId = typeId, TypeFromCard = true }, null));
    }

    [Fact]
    public void The_book_has_the_workbooks_sheets_in_its_order_and_its_file_name()
    {
        using var box = Ready();
        var book = box.Build();
        Assert.Equal("report-details-näidis-oü-2026-11-04_10-00-00.xlsx", book.FileName);
        Assert.Equal(
            ["Vahearuanne", "TOR kontroll", "Otsingu kontroll", "Tundmatu kategooria", "Palgad", "Dividendid", "Üür", "Kommunaalkulud", "Kompensatsioon", "Haigusleht", "Laenud", "TÖR-väline palgad", "Trahvid", "LHV pank 1757", "Deklaratsioon"],
            book.Sheets.Select(s => s.Name));
    }

    [Fact]
    public void The_Vahearuanne_carries_the_workbooks_headers_and_the_reviews_numbers()
    {
        using var box = Ready();
        var sheet = box.Build().Sheet(ReportText.InterimSheet)!;
        Assert.Equal(
            ["Töötaja isikukood", "Perekonnanimi", "Eesnimi", "LHV pank *1757", "PALK NETO KOKKU", "Maksuvaba", "Residentsus", "Pension", "Kontroll-kp", "Töötamise alguse aeg", "Töötamise liik", "Kande olek", "Töötamise lõpu aeg", "Peatamise algus", "Peatamise lõpu aeg", "Deklareerimine", "Deklareerimata"],
            Texts(sheet, 1));
        Assert.True(sheet.Row(1).Header);
        Assert.Equal(["48712219406", "MAASIKAS", "MARI", "905.92", "905.92", "700", "EE", "ei ole liitunud", "2026-11-04", "2024-02-15", "Tööleping", "Kehtiv", "", "", "", "", ""], Texts(sheet, 2)); // Kontroll-kp = the day the facts were confirmed
        Assert.Equal(ReportCellKind.Code, sheet.At(2, 1).Kind);
        Assert.Equal(ReportCellKind.Date, sheet.At(2, 9).Kind);
        Assert.Equal(["38810140309", "TAMM", "JAAN", "", "0", "700", "kontrollimata", "kontrollimata", "", "2020-01-01", "VÕS leping", "Lõpetatud", "2026-09-30", "", "", "", ""], Texts(sheet, 3));
        Assert.True(sheet.Row(4).Cells.Count == 0, "the person block ends at a blank row");

        // Maksuvaba: empty when the exemption is not applied; the pension-age allowance once the person has reached it
        var jaan = box.View.Persons[1].Person!;
        box.Workspace.SavePerson(jaan with { BasicExemptionApplied = false });
        Assert.Equal("", Texts(box.Build().Sheet(ReportText.InterimSheet)!, 3)[5]);
        box.Workspace.SavePerson(jaan with { BasicExemptionApplied = true, PensionAgeYear = 2026 });
        Assert.Equal("776", Texts(box.Build().Sheet(ReportText.InterimSheet)!, 3)[5]);
        box.Workspace.SavePerson(jaan with { BasicExemptionApplied = true, PensionAgeYear = 2027 });
        Assert.Equal("700", Texts(box.Build().Sheet(ReportText.InterimSheet)!, 3)[5]);

        // the accountant's word, not the run's: the salary set to no category leaves the Vahearuanne
        box.Workspace.SetCategory(box.Salary, null);
        sheet = box.Build().Sheet(ReportText.InterimSheet)!;
        Assert.Equal(["48712219406", "MAASIKAS", "MARI", "", "0"], Texts(sheet, 2).Take(5));
    }

    [Fact]
    public void The_sub_tables_sit_below_a_gap_and_follow_the_accountants_category()
    {
        using var box = Ready();
        box.Workspace.SetCategory(box.Stranger, "Üür");
        box.Workspace.AddHandLine(box.Period.Id, box.View.Persons[0].PersonId, new DateOnly(2026, 10, 20), 120m, "Mari Maasikas", "haigusleht", "Haigusleht");
        var sheet = box.Build().Sheet(ReportText.InterimSheet)!;
        Assert.Equal(["Töötaja isikukood", "Perekonnanimi", "Eesnimi", "LHV pank *1757", "käsitsi", "PALK NETO KOKKU"], Texts(sheet, 1).Take(6));
        var rows = sheet.Rows;
        var titles = rows.Select((r, i) => (Row: i + 1, Title: r.Cells.Count == 1 ? r.Cells[0].Text : null)).Where(x => x.Title is not null).ToList();
        Assert.Equal(["Dividendid", "Üür", "Kommunaalkulud", "Kompensatsioon", "Haigusleht", "Laenud", "TÖR-väline palgad", "Trahvid"], titles.Select(t => t.Title));
        Assert.Equal(8, titles[0].Row); // persons in rows 2-3, four blank rows, the title in row 8
        foreach (var (rowNo, _) in titles)
        {
            Assert.Equal(["Töötaja isikukood", "Perekonnanimi", "Eesnimi", "LHV pank *1757", "käsitsi", "Kokku"], Texts(sheet, rowNo + 1));
            Assert.True(sheet.Row(rowNo + 1).Header);
        }

        var rent = titles.Single(t => t.Title == "Üür").Row;
        Assert.Equal(["", "Kinnisvara OÜ", "", "450", "", "450"], Texts(sheet, rent + 2));
        Assert.Empty(sheet.Row(rent + 3).Cells);
        var sick = titles.Single(t => t.Title == "Haigusleht").Row;
        Assert.Equal(["48712219406", "MAASIKAS", "MARI", "", "120", "120"], Texts(sheet, sick + 2));
        Assert.Empty(sheet.Row(titles.Single(t => t.Title == "Laenud").Row + 2).Cells);

        // a person's row outside the month counts for nobody's sub-table once it is nobody's (the macro's second pass never asked the date)
        var jaansRow = box.View.Persons[1].Payments.Single(p => p.Payee == "Jaan Tamm");
        box.Workspace.SetPerson(jaansRow, null);
        box.Workspace.SetCategory(box.View.Unmatched.Payments.Single(p => p.Payee == "Jaan Tamm"), "Laenud");
        sheet = box.Build().Sheet(ReportText.InterimSheet)!;
        var loans = sheet.Rows.Select((r, i) => (Row: i + 1, Title: r.Cells.Count == 1 ? r.Cells[0].Text : null)).Single(x => x.Title == "Laenud").Row;
        Assert.Equal(["", "Jaan Tamm", "", "300", "", "300"], Texts(sheet, loans + 2));
    }

    [Fact]
    public void Tundmatu_kategooria_lists_what_the_macro_listed()
    {
        using var box = Ready();
        Assert.Single(box.Build().Sheet(ReportText.UnknownCategorySheet)!.Rows); // the row outside the month and the row no pattern touched are not undecided
        box.Workspace.SetCategory(box.Salary, null);
        var sheet = box.Build().Sheet(ReportText.UnknownCategorySheet)!;
        Assert.Equal(["LHV pank *1757 [2]"], sheet.Rows.Skip(1).Select(r => r.Cells[3].Text));
        Assert.Equal("puudub", sheet.At(2, 12).Text);
    }

    [Fact]
    public void Otsingu_kontroll_lists_every_payment_with_the_accountants_decision_and_the_category_sheets_split_them()
    {
        using var box = Ready();
        box.Workspace.SetCategory(box.Stranger, "Üür");
        box.Workspace.SetNote(box.Salary, "kontrollitud");
        box.Workspace.AddHandLine(box.Period.Id, box.View.Persons[0].PersonId, new DateOnly(2026, 10, 20), 120m, "Mari Maasikas", "haigusleht", "Haigusleht");
        var book = box.Build();
        var search = book.Sheet(ReportText.SearchSheet)!;
        Assert.Equal(["Isikukood", "Perekonnanimi", "Eesnimi", "Dokument", "Kuupaev", "Nimi valjavote lehel", "Otsing*", "D/C", "Summa", "Selgitus", "Kontroll/Kategooria", "Otsus"], Texts(search, 1));
        Assert.Equal(["48712219406", "MAASIKAS", "MARI", "LHV pank *1757 [2]", "2026-10-05", "Mari Maasikas", "", "D", "-905.92", "palk", "[Palgad]", "kontrollitud"], Texts(search, 2));
        Assert.Equal(["48712219406", "MAASIKAS", "MARI", "käsitsi", "2026-10-20", "Mari Maasikas", "", "", "120", "haigusleht", "", "MAASIKAS MARI; [Haigusleht]"], Texts(search, 3)); // a hand line is the accountant's word through and through
        Assert.Equal(["38810140309", "TAMM", "JAAN", "LHV pank *1757 [4]", "2026-11-02", "Jaan Tamm", "", "D", "-300", "töövõtt", "kuupaev ei kuulu perioodi [2026-10]", ""], Texts(search, 4));
        Assert.Equal(["", "", "", "LHV pank *1757 [3]", "2026-10-06", "", "", "D", "-0.38", "Teenustasu", "", ""], Texts(search, 5)); // the fee row without a payee, listed as the macro listed it
        Assert.Equal(["", "", "", "LHV pank *1757 [5]", "2026-10-12", "Kinnisvara OÜ", "", "D", "-450", "ruumide kasutamine oktoober", "", "[Üür]"], Texts(search, 6));
        Assert.Equal(6, search.Rows.Count);

        Assert.Equal(["LHV pank *1757 [2]"], book.Sheet("Palgad")!.Rows.Skip(1).Select(r => r.Cells[3].Text));
        Assert.Equal(["käsitsi"], book.Sheet("Haigusleht")!.Rows.Skip(1).Select(r => r.Cells[3].Text));
        Assert.Equal(["LHV pank *1757 [5]"], book.Sheet("Üür")!.Rows.Skip(1).Select(r => r.Cells[3].Text));
        Assert.Single(book.Sheet(ReportText.UnknownCategorySheet)!.Rows);
        Assert.Single(book.Sheet("Laenud")!.Rows);
    }

    [Fact]
    public void The_statement_copy_keeps_every_row_at_its_number_and_names_everyone_found()
    {
        using var box = Ready();
        box.Workspace.SetCategory(box.Stranger, "Üür");
        var copy = box.Build().Sheet("LHV pank 1757")!;
        var layout = LayoutCatalog.Default.Find("lhv-et-short")!;
        Assert.Equal(layout.Headers.Append("Vaste leitud"), Texts(copy, 1));
        Assert.Equal(5, copy.Rows.Count);
        var dateColumn = layout.ColumnOf(layout.Roles.Date);
        var amountColumn = layout.ColumnOf(layout.Roles.Amount);
        var salary = copy.Row(2);
        Assert.Equal((ReportCellKind.Date, new DateOnly(2026, 10, 5)), (salary.Cells[dateColumn - 1].Kind, salary.Cells[dateColumn - 1].Date));
        Assert.Equal((ReportCellKind.Number, -905.92m), (salary.Cells[amountColumn - 1].Kind, salary.Cells[amountColumn - 1].Number));
        Assert.Equal("EE497700771001131757", salary.Cells[0].Text);
        Assert.Equal("MAASIKAS MARI [Palgad]", salary.Cells[^1].Text);
        Assert.Equal(layout.Headers.Count + 1, salary.Cells.Count);
        Assert.True(copy.Row(3).Cells[^1].IsEmpty, "the fee row was found for nobody");
        Assert.Equal("TAMM JAAN kuupaev ei kuulu perioodi [2026-10]", copy.Row(4).Cells[^1].Text);
        Assert.Equal("[Üür]", copy.Row(5).Cells[^1].Text);

        box.Workspace.SetCategory(box.Salary, null);
        Assert.Equal("MAASIKAS MARI [puudub]", box.Build().Sheet("LHV pank 1757")!.Row(2).Cells[^1].Text); // the run's category taken away is not shown as theirs
    }

    [Fact]
    public void A_shared_row_is_noted_with_its_candidates_and_a_repeated_source_keeps_its_number()
    {
        var (store, company, period, _) = ReviewFixtures.Setup(twin: true);
        using var _ = store;
        var second = Tables.Xlsx("LHV", [LayoutCatalog.Default.Find("lhv-et-short")!.Headers.ToArray<object?>(),
            ["EE497700771001131757", 7, new DateTime(2026, 10, 28), "EE15", "Jaan Tamm", "D", -80d, null, "2026102800000007", "lisatasu", "EUR"]]);
        store.LinkImport(period.Id, store.AddImport(company.Id, ReviewFixtures.Importer.Read("lhv-2.xlsx", second), second).Id);
        new MatchRunner(store).Run(period.Id);
        var clock = new FixedClock(At);
        var workspace = new ReviewWorkspace(store, TaxRates.Year2026, clock);
        var declarer = new PeriodDeclarer(store, TaxRates.Year2026, clock);
        var book = new ReportBuilder(store, workspace, declarer, clock).Build(period.Id);
        Assert.Equal(["LHV pank 1757", "LHV pank 1757 #2"], book.Sheets.Where(s => s.Name.StartsWith("LHV", StringComparison.Ordinal)).Select(s => s.Name));
        Assert.Equal(["Töötaja isikukood", "Perekonnanimi", "Eesnimi", "LHV pank *1757", "LHV pank *1757 #2", "PALK NETO KOKKU"], Texts(book.Sheet(ReportText.InterimSheet)!, 1).Take(6));
        Assert.StartsWith("jagatud: ", book.Sheet("LHV pank 1757")!.Row(2).Cells[^1].Text, StringComparison.Ordinal);
        Assert.Equal("LHV pank *1757 #2 [2]", book.Sheet(ReportText.SearchSheet)!.Rows.Single(r => r.Cells[9].Text == "lisatasu").Cells[3].Text);
        Assert.Equal("[2][Jaan Tamm][Palgad]lisatasu", Texts(book.Sheet(ReportText.TorSheet)!, 3)[^1]); // under the second statement's column
        var shared = book.Sheet(ReportText.SearchSheet)!.Rows.Single(r => r.Cells[3].Text == "LHV pank *1757 [2]");
        Assert.StartsWith("jagatud: ", shared.Cells[11].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void TOR_kontroll_is_the_register_with_the_texts_under_the_statement_they_came_from()
    {
        using var box = Ready();
        box.Workspace.AddHandLine(box.Period.Id, box.View.Persons[0].PersonId, new DateOnly(2026, 10, 20), 120m, "Mari Maasikas", "haigusleht", "Haigusleht");
        var sheet = box.Build().Sheet(ReportText.TorSheet)!;
        Assert.Equal(TorHeaders.All.Concat(["LHV pank *1757", "käsitsi"]), Texts(sheet, 1));
        var mari = Texts(sheet, 2);
        Assert.Equal(("1", "48712219406", "MAASIKAS", "MARI", "2024-02-15"), (mari[0], mari[1], mari[2], mari[3], mari[4]));
        Assert.Equal(ReportCellKind.Date, sheet.At(2, 5).Kind);
        Assert.Equal("[2][Mari Maasikas][Palgad]palk", mari[^2]);
        Assert.Equal("[-][Mari Maasikas][Haigusleht]haigusleht", mari[^1]);
        var jaan = Texts(sheet, 3);
        Assert.Equal(("TAMM", "[4][Jaan Tamm][]töövõtt", ""), (jaan[2], jaan[^2], jaan[^1]));
        Assert.Equal(3, sheet.Rows.Count);
    }

    [Fact]
    public void The_Vahearuanne_gives_the_pause_the_declaration_choice_and_the_undeclared_sum_as_the_grid_does()
    {
        var (store, _, period, _) = ReviewFixtures.Setup(mariPaused: true);
        using (store)
        {
            new MatchRunner(store).Run(period.Id);
            var clock = new FixedClock(At);
            var workspace = new ReviewWorkspace(store, TaxRates.Year2026, clock);
            var declarer = new PeriodDeclarer(store, TaxRates.Year2026, clock);
            var builder = new ReportBuilder(store, workspace, declarer, clock);
            var mari = workspace.Load(period.Id).Persons[0].Person!;
            workspace.SavePerson(mari with { Declaration = DeclarationChoice.Exclude });

            var view = workspace.Load(period.Id);
            Assert.Equal(905.92m, view.Persons[0].UndeclaredSum); // the salary kept out by the card
            var row = Texts(builder.Build(period.Id).Sheet(ReportText.InterimSheet)!, 2);
            Assert.Equal(["2026-08-03", "2027-06-30", "välja", "905.92"], row.Skip(13)); // Peatamise algus, Peatamise lõpu aeg, Deklareerimine, Deklareerimata
        }
    }

    [Fact]
    public void The_Deklaratsioon_sheet_lists_the_standing_lines()
    {
        using var box = Ready();
        var empty = box.Build().Sheet(ReportText.DeclarationSheet)!;
        Assert.Equal(ReportBuilder.DeclarationHeaders, Texts(empty, 1));
        Assert.Single(empty.Rows);

        box.Declarer.Build(box.Period.Id);
        var sheet = box.Build().Sheet(ReportText.DeclarationSheet)!;
        Assert.Equal(4, sheet.Rows.Count);
        var line = Texts(sheet, 2);
        Assert.Equal(("EE0201001", "R uus", "mustand", "MAASIKAS MARI", "EE48712219406", "10", "2026-10-05", "905.92"), (line[0], line[2], line[3], line[4], line[5], line[6], line[7], line[8]));
        Assert.StartsWith("10235688-202610-01-", line[1]);
        Assert.True(sheet.At(2, 10).Number > 905.92m, "the gross");
        Assert.Equal(ReportCellKind.Number, sheet.At(2, 11).Kind);
        Assert.Equal("1", line[^1]);

        // the tab's totals under the lines (decision 104): the net, then the taxes with their parts
        Assert.Equal(("NETO KOKKU", "905.92"), (Texts(sheet, 3)[0], Texts(sheet, 3)[8]));
        Assert.Equal("MAKSUD KOKKU", Texts(sheet, 4)[0]);
        Assert.True(sheet.At(4, 9).Number > 0m, "the taxes to pay");
        Assert.True(sheet.At(4, 11).Number > 0m, "the social tax");
        Assert.True(sheet.Row(3).Header && sheet.Row(4).Header);
    }

    [Fact]
    public void A_period_without_a_run_still_exports_its_hand_lines_and_the_register()
    {
        using var box = Ready(run: false);
        var person = box.Workspace.AddPerson(box.Company.Id, "Kati", "Kask", "47001010000", null);
        box.Workspace.AddHandLine(box.Period.Id, person.Id, new DateOnly(2026, 10, 3), 250m, "Kati Kask", "tasu", "Palgad");
        var book = box.Build();
        Assert.Equal(["Vahearuanne", "TOR kontroll", "Otsingu kontroll", "Tundmatu kategooria", "Palgad", "Dividendid", "Üür", "Kommunaalkulud", "Kompensatsioon", "Haigusleht", "Laenud", "TÖR-väline palgad", "Trahvid", "Deklaratsioon"], book.Sheets.Select(s => s.Name));
        var interim = book.Sheet(ReportText.InterimSheet)!;
        Assert.Equal(["Töötaja isikukood", "Perekonnanimi", "Eesnimi", "käsitsi", "PALK NETO KOKKU"], Texts(interim, 1).Take(5));
        Assert.Equal(["47001010000", "Kask", "Kati", "250", "250"], Texts(interim, 2).Take(5));
        Assert.Equal(["47001010000", "Kask", "Kati", "käsitsi"], Texts(book.Sheet("Palgad")!, 2).Take(4));
        Assert.Equal(3, book.Sheet(ReportText.TorSheet)!.Rows.Count);
    }

    [Fact]
    public void The_written_workbook_reads_back_with_typed_cells_and_the_register_sheet_is_still_a_register()
    {
        using var box = Ready();
        var book = box.Build();
        var bytes = ReportWriter.ToBytes(book);
        var read = Tables.ReadXlsx(bytes, book.FileName);
        Assert.Equal(ReportWriter.SheetNames(book.Sheets.Select(s => s.Name)), read.Sheets.Select(s => s.Name));
        var interim = read.Sheets.Single(s => s.Name == "Vahearuanne");
        Assert.Equal(book.Sheet("Vahearuanne")!.Row(1).Cells.Select(c => c.Text), interim.Rows[0].Select(c => c.Text));
        var mari = interim.Rows[1];
        Assert.Equal((CellKind.Number, "48712219406"), (mari[0].Kind, mari[0].Text));
        Assert.Equal((CellKind.Number, "905.92"), (mari[3].Kind, mari[3].Text));
        Assert.Equal((CellKind.Date, "2026-11-04"), (mari[8].Kind, mari[8].Text));
        Assert.Equal((CellKind.Text, "Tööleping"), (mari[10].Kind, mari[10].Text));
        var copy = read.Sheets.Single(s => s.Name == "LHV pank 1757");
        var layout = LayoutCatalog.Default.Find("lhv-et-short")!;
        Assert.Equal("-905.92", copy.Rows[1][layout.ColumnOf(layout.Roles.Amount) - 1].Text);
        Assert.Equal("2026-10-05", copy.Rows[1][layout.ColumnOf(layout.Roles.Date) - 1].Text);

        // the cells carry the formats Excel shows them with: plain digits for a code, two decimals for money, a day for a date
        using (var workbook = new ClosedXML.Excel.XLWorkbook(new MemoryStream(bytes)))
        {
            var sheet = workbook.Worksheet("Vahearuanne");
            Assert.Equal("0", sheet.Cell(2, 1).Style.NumberFormat.Format);
            Assert.Equal("#,##0.00", sheet.Cell(2, 4).Style.NumberFormat.Format);
            Assert.Equal("dd.MM.yyyy", sheet.Cell(2, 9).Style.DateFormat.Format);
            Assert.True(sheet.Cell(1, 1).Style.Font.Bold);
        }

        // the importer still knows the register copy (with the statement columns as extras), and a statement copy as
        // the nearest bank layout with one header column more — as the workbook's own reports read
        var parsed = ReviewFixtures.Importer.Read(book.FileName, bytes);
        var tor = parsed.Sheets.Single(s => s.Name == "TOR kontroll");
        Assert.Equal(SheetKind.Tor, tor.Kind);
        Assert.Contains(tor.Problems, p => p.Code == TorParser.ExtraColumns);
        Assert.Equal(SheetKind.Unrecognised, parsed.Sheets.Single(s => s.Name == "Vahearuanne").Kind);
        var statementCopy = parsed.Sheets.Single(s => s.Name == "LHV pank 1757");
        Assert.Equal((SheetKind.Unrecognised, "lhv-et-short"), (statementCopy.Kind, statementCopy.NearestLayoutId));
        Assert.Contains(statementCopy.Differences, d => d.Contains("header columns", StringComparison.Ordinal));
    }

    [Fact]
    public void The_writer_puts_the_file_into_the_folder_under_the_books_name()
    {
        using var box = Ready();
        var folder = Path.Combine(Path.GetTempPath(), $"satellestsd-report-{Guid.NewGuid():N}");
        try
        {
            var book = box.Build();
            var result = ReportWriter.Write(book, folder);
            Assert.Equal(Path.Combine(folder, book.FileName), result.Path);
            Assert.Equal(book.Sheets.Count, result.Sheets);
            Assert.True(new FileInfo(result.Path).Length > 0);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }

    [Fact]
    public void Sheet_names_are_fitted_to_Excels_rules()
    {
        var forty = new string('x', 40);
        Assert.Equal(
            ["LHV pank 1757", "LHV pank 1757 (2)", "abcdef", new string('x', 31), new string('x', 27) + " (2)", "Leht", "Palgad", "palgad (2)"],
            ReportWriter.SheetNames(["LHV pank *1757", "LHV pank 1757", "a:b?c[d]/e\\f", forty, forty, "", "Palgad", "palgad"]));
    }

    [Fact]
    public void The_file_name_is_the_workbooks_with_nothing_windows_refuses()
    {
        Assert.Equal("report-details-näidis-oü-2026-11-04_10-00-00.xlsx", ReportBook.FileNameFor("Näidis OÜ", At));
        Assert.Equal("report-details-ab-c-2026-11-04_10-00-00.xlsx", ReportBook.FileNameFor("A/B: C", At));
        Assert.Equal("report-details-ettevote-2026-11-04_10-00-00.xlsx", ReportBook.FileNameFor("  ", At));
        Assert.Equal("report-details-firma-2026-11-04_10-00-00.xlsx", ReportBook.FileNameFor("Firma. ", At));
        Assert.Equal($"report-details-{new string('a', 80)}-2026-11-04_10-00-00.xlsx", ReportBook.FileNameFor(new string('a', 300), At));
    }

    [Fact]
    public void An_identifier_is_a_number_only_when_the_digits_survive_it()
    {
        Assert.Equal((ReportCellKind.Code, 48712219406m, "48712219406"), (ReportCell.Identifier("48712219406").Kind, ReportCell.Identifier("48712219406").Number, ReportCell.Identifier("48712219406").Text));
        Assert.Equal(ReportCellKind.Text, ReportCell.Identifier("0012345").Kind);
        Assert.Equal(ReportCellKind.Text, ReportCell.Identifier("1234567890123456").Kind);
        Assert.Equal(ReportCellKind.Text, ReportCell.Identifier("EE123").Kind);
        Assert.Equal(ReportCellKind.Empty, ReportCell.Identifier(null).Kind);
    }

    [Fact]
    public void A_stored_cell_becomes_a_date_only_when_it_is_one()
    {
        Assert.Equal(ReportCellKind.Date, ReportCell.FromStored("2026-10-05").Kind);
        Assert.Equal(ReportCellKind.Text, ReportCell.FromStored("2026-13-05").Kind);
        Assert.Equal(ReportCellKind.Text, ReportCell.FromStored("48712219406").Kind);
        Assert.Equal(ReportCellKind.Empty, ReportCell.FromStored("").Kind);
        Assert.Equal(905.92m, ReportCell.Amount(905.915m).Number);
    }
}
