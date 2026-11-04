using SatellesTSD.Core.Model;
using SatellesTSD.Core.Review;
using SatellesTSD.Core.Store;

namespace SatellesTSD.WindowTests;

/// <summary>The window on a store built like the probe's: the review tab's grids and an edit through them, the
/// declaration tab's build, upload and resolve, and the key's history dialog. Nothing here opens a modal dialog:
/// the window's questions are answered by the test, its failures thrown.</summary>
public class WindowTests
{
    [Fact]
    public void The_review_tab_shows_the_persons_and_their_payments_and_stores_an_edit()
    {
        using var fixture = new WindowFixture();
        Ui.Run(() => fixture.Drive(form =>
        {
            Assert.StartsWith("SatellesTSD v", form.Text, StringComparison.Ordinal);
            var persons = form.PersonsGrid;
            Assert.Equal(3, persons.Rows.Count); // the two register persons and the rows nobody matched
            Assert.Equal("MAASIKAS", persons.Rows[0].Cells["last"].Value?.ToString());
            Assert.True(persons.Rows[2].Tag is ReviewPerson { IsUnmatched: true });

            persons.CurrentCell = persons.Rows[0].Cells[0];
            Ui.Pump();
            var payments = form.PaymentsGrid;
            Assert.Single(payments.Rows);
            Assert.Equal("LHV pank *1757 [2]", payments.Rows[0].Cells["document"].Value?.ToString());
            Assert.Equal("Palgad", payments.Rows[0].Cells["category"].Value?.ToString());
            Assert.Contains("905", persons.Rows[0].Cells["src:LHV pank *1757"].Value?.ToString(), StringComparison.Ordinal); // the net salary from the statement

            // the category picked in the grid's combo, as the accountant picks it: the pick is a decision at once
            // (CurrentCellDirtyStateChanged commits it), the overlay is stored, the view reloads and the grids follow
            var periodId = form.View!.Period.Id;
            payments.CurrentCell = payments.Rows[0].Cells["category"];
            Assert.True(payments.BeginEdit(true));
            var combo = Assert.IsType<DataGridViewComboBoxEditingControl>(payments.EditingControl);
            combo.SelectedValue = "Üür";
            Ui.Until(() => !form.IsBusy && form.View is { } view && view.Period.Id == periodId && view.Persons[0].Payments.Single().Category == "Üür", what: "the reload after the pick");
            var stored = form.Services!.Workspace.Load(periodId).Persons[0].Payments.Single();
            Assert.Equal(("Üür", "Palgad", "57"), (stored.Category, stored.DefaultCategory, stored.TypeCode));
            Assert.Equal("Üür", form.PaymentsGrid.Rows[0].Cells["category"].Value?.ToString());
            Assert.DoesNotContain("905", form.PersonsGrid.Rows[0].Cells["src:LHV pank *1757"].Value?.ToString() ?? "", StringComparison.Ordinal); // rent is no salary: the Vahearuanne's cell follows the accountant's word
        }));
    }

    [Fact]
    public void The_declaration_tab_builds_the_lines_marks_the_upload_and_settles_a_line()
    {
        using var fixture = new WindowFixture();
        Ui.Run(() => fixture.Drive(form =>
        {
            form.Tabs.SelectedIndex = 1;
            Ui.Pump();
            var panel = form.Declaration;
            Assert.True(panel.BuildButton.Enabled);
            panel.BuildButton.PerformClick();
            Ui.Until(() => !form.IsBusy && panel.LinesGrid.Rows.Count == 1, what: "the build");
            var row = panel.LinesGrid.Rows[0];
            Assert.Equal(("R uus", "mustand", "MAASIKAS MARI", "10"), (row.Cells["action"].Value, row.Cells["status"].Value, row.Cells["person"].Value, row.Cells["type"].Value));
            Assert.False(panel.UploadedButton.Enabled);

            // the file goes out at store level (the folder dialog is not a test's business); the window reloads
            var services = form.Services!;
            var periodId = form.View!.Period.Id;
            services.Submitter.Export(periodId, fixture.ExportFolder);
            form.Reload();
            Ui.Pump();
            Assert.Equal("saadetud", panel.LinesGrid.Rows[0].Cells["status"].Value);
            Assert.True(panel.UploadedButton.Enabled);
            panel.UploadedButton.PerformClick();
            Ui.Pump();
            Assert.Equal(PeriodState.Uploaded, form.View!.Period.State);

            panel.LinesGrid.CurrentCell = panel.LinesGrid.Rows[0].Cells[0];
            Ui.Pump();
            Assert.True(panel.AcceptButton.Enabled);
            panel.AcceptButton.PerformClick();
            Ui.Pump();
            Assert.Equal("vastu võetud", panel.LinesGrid.Rows[0].Cells["status"].Value);
            Assert.False(panel.AcceptButton.Enabled);
        }));
    }

    [Fact]
    public void Files_import_without_a_company_when_their_TOR_names_the_employer_and_switch_to_another_employer_when_asked()
    {
        using var fixture = new WindowFixture(empty: true);
        var palgad = Path.Combine(fixture.Folder, "palgad.xlsx");
        File.WriteAllBytes(palgad, SatellesTSD.Tests.ReviewFixtures.Workbook());
        var other = Path.Combine(fixture.Folder, "teine.xlsx");
        File.WriteAllBytes(other, SatellesTSD.Tests.Tables.Xlsx("Töötajate loend", [Core.Import.TorHeaders.All.ToArray<object?>(),
            ["1", 47001010000d, "KASK", "KATI", new DateTime(2024, 2, 15), 1, "Tööleping", 2, "Kehtiv", null, null, null, null, null, null, null, null, "EE", null, null, null, null, null, null, null, null, 12345678, "Teine OÜ"]]));
        var statementOnly = Path.Combine(fixture.Folder, "lhv.xlsx");
        File.WriteAllBytes(statementOnly, SatellesTSD.Tests.Tables.Xlsx("LHV", [Core.Import.LayoutCatalog.Default.Find("lhv-et-short")!.Headers.ToArray<object?>(),
            ["EE497700771001131757", 1, new DateTime(2026, 10, 5), "EE12", "Mari Maasikas", "D", -905.92, null, "2026100500000001", "palk", "EUR", 48712219406d]]));
        var questions = new List<string>();
        var notices = new List<string>();
        var answer = true;
        Ui.Run(() => fixture.Drive(form =>
        {
            form.Informing = notices.Add;
            Assert.Null(form.View); // an empty store: no company to show
            Assert.True(form.ImportButton.Enabled); // the month is chosen, so the files' TÖR may name the company

            // a statement alone names nobody: told, nothing imported
            Run(form, form.ImportFilesAsync([statementOnly]), () => notices.Count == 1, "the notice");
            Assert.Null(form.View);
            Assert.Empty(form.Services!.Store.Companies());

            // the company comes from the files: created, shown, and the files land in the month chosen
            Run(form, form.ImportFilesAsync([palgad]), () => form.View is { Files.Count: 1 }, "the import without a company");
            var view = form.View!;
            Assert.Equal(("10235688", "Näidis OÜ", 2026, 10), (view.Company.RegistryCode, view.Company.Name, view.Period.Year, view.Period.Month));
            Assert.Null(view.Run); // the files are in; the search is the accountant's next step
            Assert.Single(form.Services!.Store.Companies());
            Assert.Empty(questions);

            // another employer's files while this company is shown: asked; "no" changes nothing
            answer = false;
            Run(form, form.ImportFilesAsync([other]), () => questions.Count == 1, "the question");
            Assert.Contains("Teine OÜ (12345678)", questions[0], StringComparison.Ordinal);
            Assert.Contains("Luua see ettevõte", questions[0], StringComparison.Ordinal);
            Assert.Single(form.Services!.Store.Companies());
            Assert.Equal("10235688", form.View!.Company.RegistryCode);

            // "yes" creates the company and switches to it, the month kept
            answer = true;
            Run(form, form.ImportFilesAsync([other]), () => form.View is { Company.RegistryCode: "12345678", Files.Count: 1 }, "the switch to the other employer");
            Assert.Equal(("Teine OÜ", 2026, 10), (form.View!.Company.Name, form.View.Period.Year, form.View.Period.Month));
            Assert.Equal(2, form.Services!.Store.Companies().Count);

            // back to the first company's files: the company exists, so the question says so, and it is taken up
            Run(form, form.ImportFilesAsync([palgad]), () => form.View is { Company.RegistryCode: "10235688" }, "the switch back");
            Assert.Contains("Vahetada ettevõte selle vastu", questions[^1], StringComparison.Ordinal);
            Assert.Equal(2, form.Services!.Store.Companies().Count);
        }, confirm: (question, _) => { questions.Add(question); return answer; }));
    }

    /// <summary>Pumps until the window's work is done and the condition holds, then surfaces what the work threw.</summary>
    private static void Run(MainForm form, Task work, Func<bool> condition, string what)
    {
        Ui.Until(() => work.IsCompleted && !form.IsBusy && condition(), what: what);
        work.GetAwaiter().GetResult();
    }

    [Fact]
    public void The_update_dialog_closes_on_a_hand_over_and_only_then_while_it_works()
    {
        Ui.Run(() =>
        {
            var release = new SatellesTSD.Infrastructure.Updates.ReleaseManifest(new Version(0, 11, 0, 0), "SatellesTSD-v0.11.0.zip", 1, new string('A', 64), "trial", null);
            using var dialog = new UpdateDialog(Path.Combine(Path.GetTempPath(), "nowhere", "SatellesTSD.exe"), Path.GetTempPath(), release, new Version(0, 10, 0, 0), fromStart: true, () => { })
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-4000, -4000),
            };
            dialog.Show();
            Ui.Pump();
            Assert.Equal("Hiljem", dialog.StopButton.Text); // Enter means later at the offer
            Assert.Same(dialog.StopButton, dialog.AcceptButton);

            // while it works the window cannot be closed; a hand-over closes it with OK
            dialog.SimulateWorking();
            dialog.Close();
            Ui.Pump();
            Assert.True(dialog.Visible);
            dialog.SimulateHandOver();
            Ui.Pump();
            Assert.True(dialog.Started);
            Assert.False(dialog.Visible);
            Assert.Equal(DialogResult.OK, dialog.DialogResult);
        });

        Ui.Run(() =>
        {
            var release = new SatellesTSD.Infrastructure.Updates.ReleaseManifest(new Version(0, 11, 0, 0), "SatellesTSD-v0.11.0.zip", 1, new string('A', 64), null, null);
            using var dialog = new UpdateDialog(Path.Combine(Path.GetTempPath(), "nowhere", "SatellesTSD.exe"), Path.GetTempPath(), release, new Version(0, 10, 0, 0), fromStart: false, () => { })
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-4000, -4000),
            };
            dialog.Show();
            Ui.Pump();
            // a silent new version with nothing to put back: the text says so, and the window may be closed
            dialog.SimulateNoAnswer(withPrevious: false);
            Assert.Equal(("Oota veel", "Sulge"), (dialog.InstallButton.Text, dialog.StopButton.Text));
            Assert.Contains("jääb tööle", dialog.StatusText, StringComparison.Ordinal);
            dialog.Close();
            Ui.Pump();
            Assert.False(dialog.Visible);
        });
    }

    [Fact]
    public void The_history_dialog_lists_every_version_and_settles_the_one_beneath()
    {
        using var fixture = new WindowFixture((store, _, period, folder) =>
        {
            // exported and uploaded, then reopened and changed before the feedback came: an M draft hides the sent R
            WindowFixture.ExportAndUpload(store, period, folder);
            WindowFixture.Submitter(store).Reopen(period.Id);
            var workspace = new ReviewWorkspace(store);
            var mari = workspace.Load(period.Id).Persons[0].Person!;
            workspace.SavePerson(mari with { FundedPensionMember = true, PensionRate = 0.02m });
            new Core.Declare.PeriodDeclarer(store).Build(period.Id);
        });
        Ui.Run(() => fixture.Drive(form =>
        {
            form.Tabs.SelectedIndex = 1;
            Ui.Pump();
            var panel = form.Declaration;
            Assert.Equal("M muudatus", panel.LinesGrid.Rows[0].Cells["action"].Value);
            panel.LinesGrid.CurrentCell = panel.LinesGrid.Rows[0].Cells[0];
            Ui.Pump();
            Assert.False(panel.AcceptButton.Enabled); // the standing version is a draft: nothing to settle from the grid
            Assert.True(panel.HistoryButton.Enabled);

            var dialog = form.CreateHistory(panel.SelectedLine!);
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Location = new Point(-4000, -4000);
            dialog.Show();
            Ui.Pump();
            Assert.Equal([PaymentStatus.Sent, PaymentStatus.Draft], dialog.Versions.Select(v => v.Status));
            Assert.Equal([LineAction.R, LineAction.M], dialog.Versions.Select(v => v.Action));
            Assert.Equal(PaymentStatus.Sent, dialog.Selected?.Status); // opened on the version awaiting its verdict
            Assert.True(dialog.RejectVersionButton.Enabled);
            dialog.RejectVersionButton.PerformClick();
            Ui.Pump();
            Assert.Equal([PaymentStatus.Rejected, PaymentStatus.Draft], dialog.Versions.Select(v => v.Status));
            Assert.False(dialog.RejectVersionButton.Enabled);
            dialog.Close();
            Ui.Pump();

            // the next build makes the draft an R again, as the key never reached the server
            panel.BuildButton.PerformClick();
            Ui.Until(() => !form.IsBusy && panel.LinesGrid.Rows.Count == 1 && panel.LinesGrid.Rows[0].Cells["action"].Value?.ToString() == "R uus", what: "the rebuild");
        }));
    }
}
