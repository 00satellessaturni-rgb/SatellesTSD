using SatellesTSD.Core.Import;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Store;
using SatellesTSD.Core.Tax;

namespace SatellesTSD.WindowTests;

/// <summary>The person card's list of the register's rows (decision 270): where two contracts compete for a payment, the
/// accountant reads each row's type, state, dates and pause on the card, under its employment type.</summary>
public class RegisterRowsWindowTests
{
    private static TorEntry Row(int no, string typeId, string type, string stateId, string state, DateOnly? start, DateOnly? end = null, DateOnly? pauseStart = null, DateOnly? pauseEnd = null, string? pauseReason = null) =>
        new(no, no.ToString(System.Globalization.CultureInfo.InvariantCulture), "49001010012", "Tiiu", "Tamm", "EE", start, end, typeId, type, stateId, state, null, null, "10235688", "Näidis OÜ", [], null, pauseStart, pauseEnd, pauseReason);

    private static Person Worker() => Person.New(Guid.NewGuid(), "Tiiu", "Tamm", "49001010012") with { Id = Guid.NewGuid() };

    private static string?[] Cells(DataGridViewRow row) => row.Cells.Cast<DataGridViewCell>().Select(c => (string?)c.Value).ToArray();

    [Fact]
    public void The_card_lists_every_row_with_its_type_state_dates_and_pause_and_marks_the_one_standing_for_the_month()
    {
        Ui.Run(() =>
        {
            using var card = new PersonPanel();
            Assert.Equal(("", 0), (card.RegisterNoneLabel.Text, card.RegisterGrid.Rows.Count)); // an empty card says nothing of anyone's rows
            var worker = Worker();
            card.Present(worker, editable: true, 2026, TaxRates.Year2026);
            Assert.Equal("", card.RegisterNoneLabel.Text); // nothing given yet for this person

            // the month has a register and it has no row of the person; or the month has no register at all
            card.ShowRegister(worker.Id, [], null);
            Assert.Equal((UiText.RegisterRowsNone, 0), (card.RegisterNoneLabel.Text, card.RegisterGrid.Rows.Count));
            card.ShowRegister(worker.Id, [], null, monthHasRegister: false);
            Assert.Equal(UiText.RegisterNoRegister, card.RegisterNoneLabel.Text);

            var board = Row(2, "601", "Juhtimis- ja kontrollorgani liige", "2", "Kehtiv", new DateOnly(2025, 6, 1));
            var contract = Row(1, "1", "Tööleping", "3", "Peatatud", new DateOnly(2024, 2, 15), pauseStart: new DateOnly(2026, 10, 10), pauseEnd: new DateOnly(2026, 10, 20), pauseReason: "Tasustamata puhkus");
            var ended = Row(3, "1", "Tööleping", "4", "Lõpetatud", new DateOnly(2022, 1, 3), new DateOnly(2023, 12, 31));
            card.ShowRegister(worker.Id, [board, contract, ended], contract);
            Assert.Equal("", card.RegisterNoneLabel.Text);

            // the earliest begun first, whatever the register's order
            Assert.Equal(3, card.RegisterGrid.Rows.Count);
            Assert.Equal(["", "1 Tööleping", "4 Lõpetatud", "03.01.2022", "31.12.2023", "", "", ""], Cells(card.RegisterGrid.Rows[0]));
            Assert.Equal([UiText.RegisterStandingMark, "1 Tööleping", "3 Peatatud", "15.02.2024", "", "10.10.2026", "20.10.2026", "Tasustamata puhkus"], Cells(card.RegisterGrid.Rows[1]));
            Assert.Equal(["", "601 Juhtimis- ja kontrollorgani liige", "2 Kehtiv", "01.06.2025", "", "", "", ""], Cells(card.RegisterGrid.Rows[2]));
            Assert.Equal(UiText.RegisterStandingTip, card.RegisterGrid.Rows[1].Cells["standing"].ToolTipText);
            Assert.Same(contract, card.RegisterGrid.Rows[1].Tag);
            Assert.Equal(
                [UiText.RegisterStanding, UiText.EmploymentType, UiText.EntryState, UiText.EmploymentStart, UiText.EmploymentEnd, UiText.PauseStart, UiText.PauseEnd, UiText.PauseReason],
                card.RegisterGrid.Columns.Cast<DataGridViewColumn>().Select(c => c.HeaderText));

            // the register's word, not the card's: read-only, and showing it leaves the card clean
            Assert.True(card.RegisterGrid.ReadOnly);
            Assert.False(card.IsDirty);

            // typed over and taken back (Tühista): the card is shown again for the same person, the rows stay
            card.NotesBox.Text = "midagi";
            Assert.True(card.IsDirty);
            card.RevertButton.PerformClick();
            Assert.Equal((false, 3, ""), (card.IsDirty, card.RegisterGrid.Rows.Count, card.RegisterNoneLabel.Text));

            // a row the import could not read says so on the row
            var unread = Row(4, "1", "Tööleping", "2", "Kehtiv", null) with { Problem = "start date" };
            card.ShowRegister(worker.Id, [unread], unread);
            Assert.Equal(UiText.RegisterRowProblem("start date"), card.RegisterGrid.Rows[0].Cells["start"].ToolTipText);

            // a locked card shuts its boxes; the rows are read and scrolled all the same. Another person's rows never stay
            var other = Worker();
            card.Present(other, editable: false);
            Assert.Equal((0, ""), (card.RegisterGrid.Rows.Count, card.RegisterNoneLabel.Text));
            card.ShowRegister(other.Id, [board, contract, ended], null);
            Assert.True(card.RegisterGrid.Enabled);
            Assert.False(card.SaveButton.Enabled);
            Assert.All(card.RegisterGrid.Rows.Cast<DataGridViewRow>(), r => Assert.Equal("", (string?)r.Cells["standing"].Value)); // none given as standing: none marked
            card.Present(null, editable: false);
            Assert.Equal((0, ""), (card.RegisterGrid.Rows.Count, card.RegisterNoneLabel.Text));
        });
    }

    [Fact]
    public void The_window_gives_the_card_the_rows_of_the_person_shown()
    {
        using var fixture = new WindowFixture(mariSecond: true);
        Ui.Run(() => fixture.Drive(form =>
        {
            var card = form.PersonEditor;
            var rowOf = form.PersonsGrid.Rows.Cast<DataGridViewRow>().ToList();
            var shown = new List<(int Rows, int Marked)>();
            foreach (var row in rowOf)
            {
                form.PersonsGrid.CurrentCell = row.Cells[0];
                Ui.Pump();
                var review = (Core.Review.ReviewPerson)row.Tag!;
                var expected = review.PersonId is null ? 0 : review.Register?.Entries.Count ?? 0;
                Assert.Equal(expected, card.RegisterGrid.Rows.Count);
                Assert.Equal(expected == 0 && review.PersonId is not null ? UiText.RegisterRowsNone : "", card.RegisterNoneLabel.Text);
                if (review.Register is { } register && review.PersonId is not null)
                {
                    // the marked row is the one the persons grid reads the month from
                    Assert.Equal(register.Entry.RowNo, ((Core.Import.TorEntry)Assert.Single(card.RegisterGrid.Rows.Cast<DataGridViewRow>(), r => (string?)r.Cells["standing"].Value == UiText.RegisterStandingMark).Tag!).RowNo);
                }
                shown.Add((card.RegisterGrid.Rows.Count, card.RegisterGrid.Rows.Cast<DataGridViewRow>().Count(r => (string?)r.Cells["standing"].Value == UiText.RegisterStandingMark)));
            }

            // the person with two contracts shows both, one of them standing for the month
            Assert.Contains(shown, s => s.Rows == 2 && s.Marked == 1);
            Assert.All(shown, s => Assert.True(s.Marked <= 1));
        }));
    }
}
