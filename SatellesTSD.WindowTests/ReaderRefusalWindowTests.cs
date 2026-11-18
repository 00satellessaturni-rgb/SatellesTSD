using System.Text;
using SatellesTSD.Core.Import;
using SatellesTSD.Core.Review;
using SatellesTSD.Core.Store;
using SatellesTSD.Tests;

namespace SatellesTSD.WindowTests;

/// <summary>The reader's refusals and a reading that recognised nothing, said in Estonian by their code in the import log, the
/// message box and the files' dialog, the Core's English kept after them in brackets (decision 229, the owner's option A).</summary>
public class ReaderRefusalWindowTests
{
    private static readonly string[] English = ["save it as", "web page", " sheet", "could not", "Excel file and", "already imported", "several employers", "Nothing in"];

    [Fact]
    public void Every_reader_refusal_is_worded_in_Estonian_by_its_code_and_no_other_import_exception_is()
    {
        var said = new Dictionary<string, string?>
        {
            [ReaderRefusal.SpreadsheetXml] = UiText.Worded(ReaderRefusal.Of(ReaderRefusal.SpreadsheetXml, "x")),
            [ReaderRefusal.WebPage] = UiText.Worded(ReaderRefusal.Of(ReaderRefusal.WebPage, "x")),
            [ReaderRefusal.UnknownFormat] = UiText.Worded(ReaderRefusal.Of(ReaderRefusal.UnknownFormat, "x")),
            [ReaderRefusal.NoExcelReader] = UiText.Worded(ReaderRefusal.Of(ReaderRefusal.NoExcelReader, "x")),
            [ReaderRefusal.NotExcel] = UiText.Worded(ReaderRefusal.Of(ReaderRefusal.NotExcel, "x", "Csv")),
            [ReaderRefusal.ExcelUnopened] = UiText.Worded(ReaderRefusal.Of(ReaderRefusal.ExcelUnopened, "x", detail: "the library's sheet text")),
            [ReaderRefusal.SheetUnreadable] = UiText.Worded(ReaderRefusal.Of(ReaderRefusal.SheetUnreadable, "x", "2", "the library's sheet text")),
            [ReaderRefusal.NothingRecognised] = UiText.Worded(ReaderRefusal.Of(ReaderRefusal.NothingRecognised, PeriodPipeline.NothingRecognised)),
            [ReaderRefusal.SeveralEmployers] = UiText.Worded(ReaderRefusal.Of(ReaderRefusal.SeveralEmployers, "x", "10235688, 12345678")),
            [ReaderRefusal.AlreadyImported] = UiText.Worded(ReaderRefusal.Of(ReaderRefusal.AlreadyImported, "x", "2026-11-02 10:15", "palgad.xlsx")),
        };
        Assert.All(said, pair =>
        {
            Assert.False(string.IsNullOrWhiteSpace(pair.Value), pair.Key);
            Assert.All(English, english => Assert.DoesNotContain(english, pair.Value!, StringComparison.OrdinalIgnoreCase));
        });

        Assert.StartsWith("Fail on Excel 2003 XML-tabel", said[ReaderRefusal.SpreadsheetXml], StringComparison.Ordinal);
        Assert.StartsWith("Fail on Exceli nime all salvestatud veebileht", said[ReaderRefusal.WebPage], StringComparison.Ordinal);
        Assert.Equal("Fail ei ole Exceli töövihik (Csv).", said[ReaderRefusal.NotExcel]);
        Assert.Contains("kahjustatud või parooliga kaitstud", said[ReaderRefusal.ExcelUnopened], StringComparison.Ordinal);
        Assert.StartsWith("Faili 2. lehte ei saanud lugeda", said[ReaderRefusal.SheetUnreadable], StringComparison.Ordinal);
        Assert.StartsWith("Failis ei tuntud ära ühtegi lehte", said[ReaderRefusal.NothingRecognised], StringComparison.Ordinal);
        Assert.Contains("mitut tööandjat (10235688, 12345678)", said[ReaderRefusal.SeveralEmployers], StringComparison.Ordinal);
        // the store's UTC time is said as the files dialog shows it: local, dd.MM.yyyy HH:mm
        var local = new DateTime(2026, 11, 2, 10, 15, 0, DateTimeKind.Utc).ToLocalTime().ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal($"Sama fail on juba imporditud {local} nimega „palgad.xlsx“.", said[ReaderRefusal.AlreadyImported]);

        // an import exception with no code, or another area's code, is not the reader's
        Assert.Null(UiText.Worded(new ImportException("plain")));
        Assert.Null(UiText.ReaderRefused(LayoutsRefusal.Of(LayoutsRefusal.NoLayout, "x")));
        Assert.Null(UiText.ReaderRefused(ImportRefusal.Of(ImportRefusal.Runs, "x")));
    }

    [Fact]
    public void The_import_log_says_a_refused_file_in_Estonian_with_the_English_after_it()
    {
        using var fixture = new WindowFixture();
        var html = Path.Combine(fixture.Folder, "valjavote.xls");
        File.WriteAllBytes(html, Encoding.UTF8.GetBytes("<html><table><tr><td>Kliendi konto</td></tr></table></html>"));
        var stray = Path.Combine(fixture.Folder, "x.csv");
        File.WriteAllBytes(stray, Encoding.UTF8.GetBytes("a;b;c\n1;2;3\n"));
        var locked = Path.Combine(fixture.Folder, "lukus.xlsx");
        File.WriteAllBytes(locked, ReviewFixtures.Workbook(111m));
        Ui.Run(() => fixture.Drive(form =>
        {
            var before = form.LogList.Items.Count;
            using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Run(form, form.ImportFilesAsync([html, stray, locked]));
            }

            var lines = form.LogList.Items.Cast<object>().Select(i => i.ToString()!).Skip(before).ToList();
            var web = Assert.Single(lines, l => l.Contains("  valjavote.xls: ", StringComparison.Ordinal));
            Assert.Contains("  valjavote.xls: Fail on Exceli nime all salvestatud veebileht", web, StringComparison.Ordinal);
            Assert.EndsWith("(valjavote.xls is a web page saved under an Excel name; open it in Excel and save it as .xlsx first.)", web, StringComparison.Ordinal);
            var nothing = Assert.Single(lines, l => l.Contains("  x.csv: ", StringComparison.Ordinal));
            Assert.EndsWith($"  x.csv: {UiText.ReaderRefused(ReaderRefusal.Of(ReaderRefusal.NothingRecognised, "x"))} ({PeriodPipeline.NothingRecognised})", nothing, StringComparison.Ordinal);
            var unreadable = Assert.Single(lines, l => l.Contains("  lukus.xlsx: ", StringComparison.Ordinal));
            Assert.Contains("  lukus.xlsx: Faili ei saanud avada: ", unreadable, StringComparison.Ordinal);
            Assert.Single(form.View!.Files); // none of them stored
        }));
    }

    [Fact]
    public void A_refusal_the_import_itself_throws_is_logged_in_Estonian_when_the_window_words_it()
    {
        using var fixture = new WindowFixture();
        var copy = Path.Combine(fixture.Folder, "teine.xlsx");
        File.WriteAllBytes(copy, ReviewFixtures.Workbook(111m));
        Ui.Run(() => fixture.Drive(form =>
        {
            // the month went past Draft behind the window's back: the pipeline's own refusal (a ReviewException) is the file's line
            form.Services!.Store.SetPeriodState(form.View!.Period.Id, PeriodState.Exported, DateTime.UtcNow);
            var before = form.LogList.Items.Count;
            Run(form, form.ImportFilesAsync([copy]));
            var line = Assert.Single(form.LogList.Items.Cast<object>().Select(i => i.ToString()!).Skip(before), l => l.Contains("  teine.xlsx: ", StringComparison.Ordinal));
            Assert.Contains($"  teine.xlsx: {UiText.Worded(new ReviewException(ReviewWorkspace.PeriodNotDraft, "x"))} (The period is Exported", line, StringComparison.Ordinal);
        }));
    }

    [Fact]
    public void Several_employers_among_the_files_are_refused_in_Estonian_the_log_keeping_the_English()
    {
        using var fixture = new WindowFixture(empty: true);
        var palgad = Path.Combine(fixture.Folder, "palgad.xlsx");
        File.WriteAllBytes(palgad, ReviewFixtures.Workbook());
        var other = Path.Combine(fixture.Folder, "teine.xlsx");
        File.WriteAllBytes(other, Tables.Xlsx("Töötajate loend", [TorHeaders.All.ToArray<object?>(),
            ["1", 47001010000d, "KASK", "KATI", new DateTime(2024, 2, 15), 1, "Tööleping", 2, "Kehtiv", null, null, null, null, null, null, null, null, "EE", null, null, null, null, null, null, null, null, 12345678, "Teine OÜ"]]));
        var failed = new List<Exception>();
        Ui.Run(() => fixture.Drive(form =>
        {
            form.Failing = failed.Add;
            Run(form, form.ImportFilesAsync([palgad, other]));
            var refusal = Assert.Single(failed);
            Assert.Equal(ReaderRefusal.SeveralEmployers, ReaderRefusal.CodeOf(refusal));
            var worded = UiText.Worded(refusal);
            Assert.Contains("mitut tööandjat", worded, StringComparison.Ordinal);
            var line = form.LogList.Items.Cast<object>().Select(i => i.ToString()!).Last();
            Assert.EndsWith($"  {UiText.Problem}: {worded} ({refusal.Message})", line, StringComparison.Ordinal);
            Assert.Empty(form.Services!.Store.Companies()); // nothing imported
        }));
    }

    [Fact]
    public void A_re_read_that_recognises_nothing_is_told_in_Estonian_and_the_log_keeps_the_English()
    {
        var lhv = LayoutCatalog.Default.Find("lhv-et-short")!;
        using var fixture = new WindowFixture((store, company, period, _) =>
        {
            var bytes = Tables.Xlsx("LHV", lhv.Headers.ToArray<object?>(), ["EE497700771001131757", 1, new DateTime(2026, 10, 20), "EE12", "Mari Maasikas", "D", -100d, null, "2026102000000009", "lisatasu", "EUR", 48712219406d]);
            var file = store.AddImport(company.Id, ReviewFixtures.Importer.Read("lhv.xlsx", bytes), bytes);
            store.LinkImport(period.Id, file.Id);
            // the owner's layout no longer knows the file's header row: a re-read finds nothing
            store.SaveLayouts([lhv with { Headers = lhv.Headers.Select(h => h == lhv.Roles.Account ? "Konto (muudetud)" : h).ToList(), Roles = lhv.Roles with { Account = "Konto (muudetud)" } }]);
        });
        Ui.Run(() => fixture.Drive(form =>
        {
            var (logged, warnings) = (new List<string>(), new List<string>());
            using var dialog = new FilesDialog(form.Services!, form.View!, logged.Add)
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-4000, -4000),
            };
            dialog.Confirming = (_, _) => true;
            dialog.Warning = (text, _) => warnings.Add(text);
            dialog.Show(form);
            Ui.Pump();
            dialog.List.Items.Cast<ListViewItem>().Single(i => i.Text == "lhv.xlsx").Selected = true;
            Ui.Pump();
            dialog.RereadButton.PerformClick();
            Ui.Pump();

            var worded = UiText.ReaderRefused(ReaderRefusal.Of(ReaderRefusal.NothingRecognised, "x"));
            Assert.Equal([worded!], warnings);
            Assert.Contains($"{UiText.Reread}: lhv.xlsx: {worded} ({PeriodPipeline.NothingRecognised})", logged);
            Assert.Contains(form.Services!.Store.PeriodImports(form.View!.Period.Id), f => f.FileName == "lhv.xlsx"); // the stored file stays
        }));
    }

    private static void Run(MainForm form, Task work)
    {
        Ui.Until(() => work.IsCompleted && !form.IsBusy, what: "the import");
        work.GetAwaiter().GetResult();
    }
}
