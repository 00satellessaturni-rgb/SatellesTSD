using SatellesTSD.Core.Review;

namespace SatellesTSD.WindowTests;

/// <summary>A row's payment split by typing a smaller amount in the payments grid (decision 150): asked first, done on
/// yes, nothing on no; the part typed 0 goes again.</summary>
public class RowSplitWindowTests
{
    private static void Type(MainForm form, int row, string text)
    {
        var grid = form.PaymentsGrid;
        grid.CurrentCell = grid.Rows[row].Cells["document"]; // from another cell, as the accountant comes to it
        grid.CurrentCell = grid.Rows[row].Cells["amount"];
        Assert.True(grid.BeginEdit(true));
        var box = Assert.IsAssignableFrom<TextBox>(grid.EditingControl);
        box.Text = text;
        Assert.True(grid.EndEdit());
    }

    private static List<(int Part, decimal Amount)> Shown(MainForm form) =>
        form.PaymentsGrid.Rows.Cast<DataGridViewRow>().Select(r => (ReviewPayment)r.Tag!).Where(p => !p.IsByHand).Select(p => (p.Part, p.Amount)).ToList();

    [Fact]
    public void A_smaller_amount_typed_on_a_row_asks_and_splits_it_and_a_part_typed_zero_goes()
    {
        using var fixture = new WindowFixture();
        var questions = new List<string>();
        var yes = true;
        Ui.Run(() => fixture.Drive(form =>
        {
            form.PersonsGrid.CurrentCell = form.PersonsGrid.Rows[0].Cells[0];
            Ui.Pump();
            Assert.Equal([(0, 905.92m)], Shown(form));

            // no: nothing is stored, the amount shown is the row's again
            yes = false;
            Type(form, 0, "800");
            Ui.Pump();
            Assert.Empty(fixture.Failures.Select(f => f.Message));
            Ui.Until(() => questions.Count == 1, what: "the question about the split");
            Ui.Pump();
            Assert.Contains("osadeks", questions[0], StringComparison.Ordinal);
            Assert.Contains("105,92", questions[0].Replace('.', ','), StringComparison.Ordinal);
            Assert.Equal([(0, 905.92m)], Shown(form));
            Assert.Equal(905.92m, form.PaymentsGrid.Rows[0].Cells["amount"].Value);
            Assert.Empty(form.Services!.Store.ReviewLines(form.View!.Period.Id));

            // yes: the row in two, the cursor on the row's own share
            yes = true;
            Type(form, 0, "800");
            Ui.Until(() => questions.Count == 2 || fixture.Failures.Count > 0, what: "the second question");
            Assert.Empty(fixture.Failures.Select(f => f.ToString()));
            Ui.Until(() => !form.IsBusy && Shown(form).Count == 2, what: "the reload after the split");
            Assert.Equal([(1, 800m), (2, 105.92m)], Shown(form));
            Assert.Equal(1, ((ReviewPayment)form.PaymentsGrid.CurrentRow!.Tag!).Part);
            Assert.EndsWith("osa 2", form.PaymentsGrid.Rows.Cast<DataGridViewRow>().Single(r => ((ReviewPayment)r.Tag!).Part == 2).Cells["document"].Value?.ToString(), StringComparison.Ordinal);

            // the row's own share typed 0: refused in the window's own words, nothing asked, nothing changed
            var ownRow = form.PaymentsGrid.Rows.Cast<DataGridViewRow>().Single(r => ((ReviewPayment)r.Tag!).Part == 1).Index;
            Type(form, ownRow, "0");
            Ui.Until(() => fixture.Failures.Count == 1, what: "the refusal of 0 on the row's own share");
            Ui.Until(() => !form.IsBusy, what: "the reload after the refusal");
            Assert.StartsWith("Makse põhiosa on 800", fixture.Failures[0].Message, StringComparison.Ordinal);
            Assert.Equal(ReviewWorkspace.SplitOwnShare, Assert.IsType<ReviewException>(fixture.Failures[0]).Code);
            Assert.Equal(2, questions.Count);
            Assert.Equal([(1, 800m), (2, 105.92m)], Shown(form));
            fixture.Failures.Clear();

            // the part typed 0: asked, and gone, the row whole again
            var partRow = form.PaymentsGrid.Rows.Cast<DataGridViewRow>().Single(r => ((ReviewPayment)r.Tag!).Part == 2).Index;
            Type(form, partRow, "0");
            Ui.Until(() => !form.IsBusy && Shown(form).Count == 1, what: "the reload after the part went");
            Assert.Contains("Eemaldada osa", questions[^1], StringComparison.Ordinal);
            Assert.Equal([(0, 905.92m)], Shown(form));
        }, confirm: (question, _) =>
        {
            questions.Add(question);
            return yes;
        }));
        Assert.Equal(3, questions.Count);
    }
}
