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
