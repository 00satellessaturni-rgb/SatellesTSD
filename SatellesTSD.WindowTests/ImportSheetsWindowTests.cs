using System.Reflection;
using System.Text;
using SatellesTSD.Core.Import;
using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Review;
using SatellesTSD.Core.Store;
using SatellesTSD.Infrastructure;
using SatellesTSD.Tests;

namespace SatellesTSD.WindowTests;

/// <summary>What the import says of a file's sheets, the re-read's own refusals, the kind of a file and a statement read before
/// its state was read: said in Estonian by their codes in the import log, the files dialog, the persons grid's tooltip and the
/// simulation, the code verbatim before it; the Core's English stays for one the window does not word.</summary>
public class ImportSheetsWindowTests
{
    private static readonly string[] English = ["sheet", "row(s)", "layout", "Nearest", "header", " is not ", "hidden", "never closed", "employer", "export", "column", "understood", " has no ", "was read", "Read the file", "store", "period"];

    private static void NoEnglish(string? said, string what)
    {
        Assert.False(string.IsNullOrWhiteSpace(said), what);
        Assert.All(English, english => Assert.DoesNotContain(english, said!, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Every_note_of_the_import_is_worded_in_Estonian_by_its_code_with_its_figures()
    {
        ImportNote Note(string code, string? sheet, int no, IReadOnlyList<string> args, int? count = null, int? rowNo = null) => new(code, "english", sheet, no, args, count, rowNo);
        var said = new (ImportNote Note, string Estonian)[]
        {
            (Note(SheetNotes.Empty, "Tühi", 1, []), "SHEET_EMPTY: Leht „Tühi“: tühi leht, sealt ei loetud midagi."),
            (Note(SheetNotes.NoLayout, null, 2, []), "SHEET_NO_LAYOUT: 2. leht: päiserida ei vasta ühelegi väljavõtte paigutusele; lehte ei loetud."),
            (Note(SheetNotes.TorLacks, "Pooleli", 3, ["Töötaja isikukood", "Eesnimi"]), "SHEET_TOR_LACKS: Leht „Pooleli“: sarnaneb TÖR väljavõttega, kuid sellel puuduvad veerud „Töötaja isikukood“, „Eesnimi“; lehte ei loetud."),
            (Note(SheetNotes.NearestLayout, "Võõras", 4, ["lhv-et-short"]) with { Differences = [new(1, "lhv-et-short", Width: 12, LayoutWidth: 11), new(1, "lhv-et-short", 7, "Summa EUR", "Summa")] },
                "SHEET_NEAREST_LAYOUT: Leht „Võõras“: lehte ei tuntud ära. Lähim paigutus on „lhv-et-short“: 1. real on päiseveerge 12, paigutusel „lhv-et-short“ 11; 1. rea 7. veerg on „Summa EUR“, paigutuses „Summa“."),
            (Note(SheetNotes.Hidden, "SEB", 5, []), "SHEET_HIDDEN: Leht „SEB“: töövihikus peidetud, loeti siiski."),
            (Note(SheetNotes.RowProblems, "SEB", 5, ["BAD_AMOUNT 1", "BAD_DATE 2"], count: 2), "SHEET_ROW_PROBLEMS: Leht „SEB“: probleemiga ridu 2 (BAD_AMOUNT 1, BAD_DATE 2)."),
            (Note(SheetNotes.UnclosedQuote, null, 0, ["3"]), "READ_UNCLOSED_QUOTE: 3. kirjes avatud jutumärk jääb sulgemata: kõik sellele järgnev loeti üheks väljaks."),
            (Note(ImportProblem.AboveHeader, "SEB", 5, [], rowNo: 1), "ABOVE_HEADER: Leht „SEB“: rida 1 on päiserea kohal ja näeb välja nagu makse; päiserea kohal olevaid ridu ei loeta."),
            (Note(ImportProblem.NoRows, "LHV", 1, []), "NO_ROWS: Leht „LHV“: lehel ei ole ühtegi rida, kus oleks saaja nimi."),
            (Note(TorParser.EmployerMismatch, "Töötajate loend", 1, ["12345678", "10235688"]), "EMPLOYER_MISMATCH: Leht „Töötajate loend“: TÖR väljavõte nimetab teist tööandjat (12345678), mitte seda ettevõtet (10235688). Kontrolli, et fail kuulub sellele ettevõttele."),
            (Note(TorParser.SeveralEmployers, "Töötajate loend", 1, ["12345678, 10235688"]), "SEVERAL_EMPLOYERS: Leht „Töötajate loend“: TÖR väljavõte nimetab mitut tööandjat: 12345678, 10235688."),
            (Note(TorParser.NoEmployerCode, "Töötajate loend", 1, []), $"NO_EMPLOYER_CODE: Leht „Töötajate loend“: TÖR väljavõttes ei ole tööandja registrikoodi (veerg „{TorHeaders.EmployerCode}“ puudub või on tühi): väljavõtet ei saa ettevõttega võrrelda ja see imporditakse valitud ettevõttele."),
            (Note(TorParser.NoEmployerCode, "Töötajate loend", 1, ["2"]), "NO_EMPLOYER_CODE: Leht „Töötajate loend“: tööandja registrikoodita ridu 2: neid ei saa ettevõttega võrrelda ja need loetakse ettevõtte omaks."),
            (Note(TorParser.ExtraColumns, "Töötajate loend", 1, ["Kontroll, Märkus"]), "EXTRA_COLUMNS: Leht „Töötajate loend“: lehel on veerge, mida töötamise register ei kirjuta (Kontroll, Märkus): see võib olla töövihiku tehtud koopia või uuem väljavõte. Kontrolli, et fail on registrist alla laaditud."),
        };
        Assert.All(said, pair =>
        {
            Assert.Equal(pair.Estonian, UiText.SheetNoteText(pair.Note));
            Assert.Equal(pair.Estonian, UiText.SheetNoteLine(pair.Note));
            NoEnglish(pair.Estonian[(pair.Note.Code.Length + 2)..], pair.Note.Code);
        });

        // every code the import log has of its own is in the table: a code added later without its Estonian fails here
        var codes = typeof(SheetNotes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!);
        Assert.All(codes, code => Assert.Contains(said, pair => pair.Note.Code == code));
    }

    [Fact]
    public void Every_row_problem_is_worded_in_Estonian_with_its_figures_and_one_without_them_keeps_its_English()
    {
        var said = new (ImportProblem Problem, string Estonian)[]
        {
            (new(4, ImportProblem.BadDate, "x", []), "Real 4 ei ole kuupäeva."),
            (new(4, ImportProblem.BadDate, "x", ["eile"]), "Rea 4 kuupäeva „eile“ ei saanud lugeda."),
            (new(4, ImportProblem.BadDate, "x", [TorHeaders.StartDate, "eile"]), $"Rea 4 veeru „{TorHeaders.StartDate}“ väärtust „eile“ ei saanud lugeda."),
            (new(5, ImportProblem.NoAmount, "x"), "Real 5 ei ole summat."),
            (new(5, ImportProblem.BadAmount, "x", ["palju"]), "Rea 5 summat „palju“ ei saanud lugeda."),
            (new(6, ImportProblem.BadMarker, "x", ["X", "D", "C"]), "Rea 6 suund „X“ ei ole „D“ ega „C“."),
            (new(7, TorParser.NoCode, "x", ["MARI", "KASK"]), "Real 7 (MARI KASK) ei ole isikukoodi."),
            (new(8, TorParser.NoName, "x", ["47001010000", "first"]), "Real 8 (47001010000) ei ole eesnime."),
            (new(8, TorParser.NoName, "x", ["47001010000", "last"]), "Real 8 (47001010000) ei ole perekonnanime."),
            (new(1, ImportProblem.AboveHeader, "x"), "Rida 1 on päiserea kohal ja näeb välja nagu makse; päiserea kohal olevaid ridu ei loeta."),
            (new(null, ImportProblem.NoRows, "x"), "Lehel ei ole ühtegi rida, kus oleks saaja nimi."),
            (new(null, TorParser.NoEmployerCode, "x", ["3"]), "Tööandja registrikoodita ridu 3: neid ei saa ettevõttega võrrelda ja need loetakse ettevõtte omaks."),
        };
        Assert.All(said, pair =>
        {
            Assert.Equal(pair.Estonian, UiText.ImportProblemText(pair.Problem));
            NoEnglish(pair.Estonian, pair.Problem.Code);
        });

        // a problem stored before its figures were kept, an unknown code, a note without its figures: the English, as before
        Assert.Null(UiText.ImportProblemText(new ImportProblem(5, ImportProblem.BadAmount, "Amount 'palju' is not understood.")));
        Assert.Null(UiText.ImportProblemText(new ImportProblem(5, ImportProblem.BadDate, "Date 'eile' is not understood.")));
        Assert.Null(UiText.ImportProblemText(new ImportProblem(null, ImportProblem.ReaderWarning, "x")));
        var unknown = new ImportNote("SOMETHING_NEW", "sheet 'X': something new.", "X", 1, []);
        Assert.Null(UiText.SheetNoteText(unknown));
        Assert.Equal("sheet 'X': something new.", UiText.SheetNoteLine(unknown));
        Assert.Null(UiText.SheetNoteText(new ImportNote(SheetNotes.NearestLayout, "x", "X", 1, ["lhv-et-short"])));
        Assert.Null(UiText.SheetNoteText(new ImportNote(ImportProblem.ReaderWarning, "a reader's word", null, 0, [])));
        Assert.Null(UiText.SheetNoteText(new ImportNote("", "sheet 'X': classified elsewhere", "X", 1, [])));
    }

    [Fact]
    public void The_files_refusals_the_kinds_and_a_statement_read_before_its_state_are_worded_in_Estonian()
    {
        var files = new (string Code, string Estonian)[]
        {
            (ImportRefusal.NotListed, "Fail ei ole enam selle perioodi failide hulgas; sulgege aken ja avage see uuesti."),
            (ImportRefusal.BytesMissing, "Faili „palgad.xlsx“ sisu ei ole andmebaasis; faili ei saa uuesti lugeda. Importige fail uuesti."),
            (ImportRefusal.NotFound, "Faili ei ole enam andmebaasis; sulgege aken ja avage see uuesti."),
            (ImportRefusal.OtherCompany, "Fail kuulub teisele ettevõttele kui see periood."),
            (ImportRefusal.PeriodNotDraft, "Kuu ei ole enam mustand: selle failid jäävad kuu dokumendiks."),
            (ImportRefusal.PeriodNotFound, "Perioodi ei ole enam andmebaasis; valige periood uuesti."),
        };
        Assert.All(files, pair =>
        {
            var refusal = ImportRefusal.Tag(new KeyNotFoundException("english"), pair.Code, subject: "palgad.xlsx");
            Assert.Equal(pair.Estonian, UiText.Worded(refusal));
            NoEnglish(pair.Estonian, pair.Code);
        });

        Assert.Equal(["pangaväljavõte", "TÖR väljavõte", "TÖR ja pangaväljavõte"], Enum.GetValues<ImportKind>().Select(k => UiText.ImportKindText(k)));
        Assert.Equal("palgad.xlsx: imporditud (TÖR ja pangaväljavõte)", UiText.ImportedLine("palgad.xlsx", false, ImportKind.Mixed));
        Assert.Equal("palgad.xlsx: juba imporditud, seotud perioodiga (pangaväljavõte)", UiText.ImportedLine("palgad.xlsx", true, ImportKind.Statement));

        var free = new MatchProblem(MatchProblem.StateUnread, "english", Source: "Kaart", RowNo: 2, Subject: "2, 5");
        Assert.Equal("Kaart loeti programmi varasema versiooniga, mis ei lugenud tehingu olekut: read 2, 5 ei liigutanud raha (tühistatud, tagasi lükatud, ebaõnnestunud või ootel), kuid on loetud makseteks. "
            + "Loe fail uuesti (Failid › Loe uuesti) või võta need read käsitsi deklaratsioonist välja (märge „Deklareeri“ maha).", UiText.MatchProblemText(free));
        var locked = UiText.MatchProblemText(free with { Variant = MatchProblem.StateUnreadLocked });
        Assert.EndsWith("Faili ei saa uuesti lugeda (see on juba eksporditud või deklareeritud kuu failide hulgas või sellest koostatud rida on olnud serveris): võta need read käsitsi deklaratsioonist välja (märge „Deklareeri“ maha).", locked, StringComparison.Ordinal);
        NoEnglish(UiText.MatchProblemText(free), MatchProblem.StateUnread);
        NoEnglish(locked, MatchProblem.StateUnread);
        Assert.Null(UiText.MatchProblemText(free with { Subject = null })); // found in a run stored before the rows were kept: its English
        Assert.Null(UiText.MatchProblemText(new MatchProblem("SOMETHING_NEW", "english")));
    }

    [Fact]
    public void The_simulation_says_the_imports_notes_and_its_stop_in_Estonian()
    {
        var hidden = new ImportNote(SheetNotes.Hidden, "sheet 'SEB': the sheet is hidden in the workbook; it was read all the same.", "SEB", 1, []);
        var report = UiText.SimReport(new SimResult { ImportNotes = [hidden], ImportStopped = true });
        Assert.Contains("PEATUS: Näidist ei saanud lugeda." + Environment.NewLine, report, StringComparison.Ordinal);
        Assert.Contains("Sammud:" + Environment.NewLine + "  SHEET_HIDDEN: Leht „SEB“: töövihikus peidetud, loeti siiski.", report, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden", report, StringComparison.Ordinal);

        var nothing = ReaderRefusal.Of(ReaderRefusal.NothingRecognised, PeriodPipeline.NothingRecognised);
        Assert.Equal(UiText.SimImportRefused(UiText.ReaderRefused(nothing)!), UiText.SimStop(new SimResult { ImportStopped = true, Refusal = nothing.Message, RefusalError = nothing }));
        Assert.Equal(UiText.SimImportRefused("plain"), UiText.SimStop(new SimResult { ImportStopped = true, Refusal = "plain" })); // a refusal without a code: its own words
    }

    [Fact]
    public void The_import_log_says_every_sheet_in_Estonian_with_its_code()
    {
        using var fixture = new WindowFixture();
        var workbook = Path.Combine(fixture.Folder, "koik.xlsx");
        File.WriteAllBytes(workbook, EverySheet());
        var csv = Path.Combine(fixture.Folder, "lhv.csv");
        var headers = string.Join(",", LayoutCatalog.Default.Find("lhv-et-short")!.Headers);
        File.WriteAllBytes(csv, Encoding.UTF8.GetBytes(headers + "\n" + "EE497700771001131757,1,2026-10-05,EE12,Mari Maasikas,D,-905.92,,2026100500000001,palk,EUR\n" + "EE497700771001131757,2,2026-10-06,EE13,\"Jaan Tamm,D,-300,,,töövõtt,EUR\n"));
        Ui.Run(() => fixture.Drive(form =>
        {
            var before = form.LogList.Items.Count;
            Run(form, form.ImportFilesAsync([workbook, csv]));
            var lines = form.LogList.Items.Cast<object>().Select(i => i.ToString()!).Skip(before).ToList();
            void Said(string line) => Assert.Contains(lines, l => l.EndsWith(line, StringComparison.Ordinal));
            Said("  koik.xlsx: imporditud (pangaväljavõte)");
            Said("   SHEET_EMPTY: Leht „Tühi“: tühi leht, sealt ei loetud midagi.");
            Said("   SHEET_NO_LAYOUT: Leht „Kirjad“: päiserida ei vasta ühelegi väljavõtte paigutusele; lehte ei loetud.");
            Assert.Contains(lines, l => l.Contains("   SHEET_NEAREST_LAYOUT: Leht „Võõras“: lehte ei tuntud ära. Lähim paigutus on „lhv-et-short“: 1. rea ", StringComparison.Ordinal));
            Assert.Contains(lines, l => l.Contains("   SHEET_TOR_LACKS: Leht „Pooleli“: sarnaneb TÖR väljavõttega, kuid sellel puuduvad veerud „", StringComparison.Ordinal));
            Said("   SHEET_HIDDEN: Leht „SEB“: töövihikus peidetud, loeti siiski.");
            Said("   ABOVE_HEADER: Leht „SEB“: rida 1 on päiserea kohal ja näeb välja nagu makse; päiserea kohal olevaid ridu ei loeta.");
            Said("   SHEET_ROW_PROBLEMS: Leht „SEB“: probleemiga ridu 1 (BAD_AMOUNT 1, BAD_DATE 1).");
            Said("  lhv.csv: imporditud (pangaväljavõte)");
            Assert.Contains(lines, l => l.Contains("   READ_UNCLOSED_QUOTE: ", StringComparison.Ordinal) && l.EndsWith(". kirjes avatud jutumärk jääb sulgemata: kõik sellele järgnev loeti üheks väljaks.", StringComparison.Ordinal));
            Assert.DoesNotContain(lines, l => l.Contains("sheet '", StringComparison.Ordinal) || l.Contains("row(s)", StringComparison.Ordinal) || l.Contains("(Statement)", StringComparison.Ordinal));
        }));
    }

    [Fact]
    public void The_files_dialog_says_the_kind_and_the_re_reads_notes_and_refusal_in_Estonian()
    {
        var lhv = LayoutCatalog.Default.Find("lhv-et-short")!;
        using var fixture = new WindowFixture((store, company, period, _) =>
        {
            var bytes = Tables.Xlsx(("LHV", [lhv.Headers.ToArray<object?>(), ["EE497700771001131757", 1, new DateTime(2026, 10, 5), "EE12", "Mari Maasikas", "D", -905.92, null, "2026100500000001", "palk", "EUR"]], true));
            var file = store.AddImport(company.Id, ReviewFixtures.Importer.Read("peidetud.xlsx", bytes), bytes);
            store.LinkImport(period.Id, file.Id);
        });
        Ui.Run(() => fixture.Drive(form =>
        {
            var (logged, warnings) = (new List<string>(), new List<(string Text, string Title)>());
            using var dialog = new FilesDialog(form.Services!, form.View!, logged.Add)
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-4000, -4000),
            };
            dialog.Confirming = (_, _) => true;
            dialog.Warning = (text, title) => warnings.Add((text, title));
            dialog.Show(form);
            Ui.Pump();
            string Kind(string name) => dialog.List.Items.Cast<ListViewItem>().Single(i => i.Text == name).SubItems[1].Text;
            Assert.Equal("TÖR ja pangaväljavõte", Kind("palgad.xlsx"));
            Assert.Equal("pangaväljavõte", Kind("peidetud.xlsx"));

            void Reread()
            {
                dialog.List.Items.Cast<ListViewItem>().Single(i => i.Text == "peidetud.xlsx").Selected = true;
                Ui.Pump();
                dialog.RereadButton.PerformClick();
                Ui.Pump();
            }

            Reread();
            Assert.Contains($"{UiText.Reread}: peidetud.xlsx: SHEET_HIDDEN: Leht „LHV“: töövihikus peidetud, loeti siiski.", logged);
            Assert.Empty(warnings);

            // the month went past Draft behind the dialog's back: the pipeline's own refusal, in Estonian
            form.Services!.Store.SetPeriodState(form.View!.Period.Id, PeriodState.Exported, DateTime.UtcNow);
            Reread();
            Assert.Equal([(UiText.Worded(new ReviewException(ReviewWorkspace.PeriodNotDraft, "x"))!, UiText.Problem)], warnings);
        }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_card_export_read_before_its_state_was_read_is_said_in_Estonian_in_the_persons_tooltip(bool locked)
    {
        using var fixture = new WindowFixture((store, company, period, _) =>
        {
            var card = LayoutCatalog.Default.Find("source1")!;
            var headers = card.Headers.ToList();
            object?[] Row(string id, string state) => headers.Select(h => (object?)(h == card.Roles.Date ? new DateTime(2026, 10, 5, 10, 0, 0) : h == card.Roles.Counterparty ? "Mari Maasikas"
                : h == card.Roles.Amount ? -905.92d : h == "Orig amount" ? 905.92d : h == card.Roles.Currency ? "EUR" : h == card.Roles.TransactionId ? id : h == card.Roles.State ? state : null)).ToArray();
            var before = card with { Roles = card.Roles with { State = null }, StatesNotDone = null };
            var then = new PeriodPipeline(store, new Importer(LayoutCatalog.Default.WithOverrides([before]), new ExcelTableReader()));
            var file = then.ImportFile(period.Id, "kaart.xlsx", Tables.Xlsx("Kaart", headers.ToArray<object?>(), Row("ID-1", "REVERTED"), Row("ID-2", "COMPLETED"))).File!;
            then.RunMatch(period.Id);
            if (locked)
            {
                // a month past Draft lists the file: it cannot be read again
                var november = store.GetOrCreatePeriod(company.Id, 2026, 11);
                store.LinkImport(november.Id, file.Id);
                store.SetPeriodState(november.Id, PeriodState.Exported, DateTime.UtcNow);
            }
        });
        Ui.Run(() => fixture.Drive(form =>
        {
            var unmatched = form.PersonsGrid.Rows.Cast<DataGridViewRow>().Single(r => r.Tag is ReviewPerson { IsUnmatched: true });
            var tip = form.ProblemTips.TextOf(unmatched)!;
            var line = Assert.Single(tip.Split(Environment.NewLine), l => l.StartsWith($"{MatchProblem.StateUnread}: ", StringComparison.Ordinal));
            Assert.Contains("loeti programmi varasema versiooniga, mis ei lugenud tehingu olekut: rida 2 ei liigutanud raha", line, StringComparison.Ordinal);
            Assert.Contains(locked ? "Faili ei saa uuesti lugeda" : "Loe fail uuesti (Failid › Loe uuesti)", line, StringComparison.Ordinal);
            Assert.DoesNotContain("earlier version", line, StringComparison.Ordinal);
            Assert.Contains(MatchProblem.StateUnread, unmatched.Cells["problems"].Value?.ToString(), StringComparison.Ordinal); // the code in the cell, as before
        }));
    }

    /// <summary>A statement workbook with a sheet of every kind the import log speaks of (the register's own are worded above):
    /// empty, a header row no layout knows, one near a layout, a TÖR lacking columns, a hidden SEB export with a payment row
    /// above its header and a row with two faults.</summary>
    private static byte[] EverySheet()
    {
        var lhv = LayoutCatalog.Default.Find("lhv-et-short")!;
        var renamed = lhv.Headers.Select(h => h == lhv.Roles.Amount ? "Summa EUR" : h).ToArray<object?>();
        var seb = LayoutCatalog.Default.Find("seb-et")!;
        return Tables.Xlsx(
            ("Tühi", [], false),
            ("Kirjad", [["a", "b", "c"], ["1", "2", "3"]], false),
            ("Võõras", [renamed, ["EE497700771001131757", 1, new DateTime(2026, 10, 5), "EE12", "Mari Maasikas", "D", -905.92, null, "2026100500000001", "palk", "EUR"]], false),
            ("Pooleli", [TorHeaders.All.Take(4).ToArray<object?>(), ["1", 48712219406d, "MAASIKAS", "MARI"]], false),
            ("SEB", [
                ["EE591010220215133228", "TT", "02.10.2026", "EE497700771001131757", "StopFire OÜ", "LHVBEE22", "H", "D", "500,00", "", "RO4262101896L02", "Reserv SEB", "0", "EUR", ""],
                seb.Headers.ToArray<object?>(),
                ["EE591010220215133228", "TM168358936", "01.10.2026", "", "AS SEB PANK", "EEUHEE2X", "H", "C", "1,01", "", "RO4248947827L02", "intresside väljamakse EUR", "0", "EUR", ""],
                ["EE591010220215133228", "TM168358937", "eile", "", "AS SEB PANK", "EEUHEE2X", "H", "C", "palju", "", "RO4248947827L03", "intress", "0", "EUR", ""]], true));
    }

    private static void Run(MainForm form, Task work)
    {
        Ui.Until(() => work.IsCompleted && !form.IsBusy, what: "the import");
        work.GetAwaiter().GetResult();
    }
}
