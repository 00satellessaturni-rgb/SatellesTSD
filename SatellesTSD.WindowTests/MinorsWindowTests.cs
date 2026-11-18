using SatellesTSD.Core.Declare;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Review;
using SatellesTSD.Core.Store;

namespace SatellesTSD.WindowTests;

/// <summary>The fourth audit's minors that live in the window (plan item 61): the refusals in Estonian (decision 183), the
/// month picked without a company (185), the non-resident's own exemption figure on the card (186), the reload after a note
/// committed by a click elsewhere (187), and a TÖR type the card's list does not know (188).</summary>
public class MinorsWindowTests
{
    [Fact]
    public void The_cards_and_the_files_refusals_and_the_confirmations_question_are_worded_in_Estonian()
    {
        Assert.Equal("Ettevõtte teisel isikul on juba isikukood 48712219406.", UiText.Worded(new ReviewException(ReviewWorkspace.PersonExists, "Another person…", subject: "48712219406")));
        Assert.StartsWith("„AB123“ ei ole Eesti isikukood", UiText.Worded(new ReviewException(ReviewWorkspace.CodeInvalid, "x", subject: "AB123")), StringComparison.Ordinal);
        Assert.Equal("Isikul peab olema eesnimi ja perekonnanimi.", UiText.Worded(new ReviewException(ReviewWorkspace.NameMissing, "x")));
        Assert.StartsWith("„EE 12“ ei ole MTA mitteresidendi kood", UiText.Worded(new ReviewException(ReviewWorkspace.MrrInvalid, "x", subject: "EE 12")), StringComparison.Ordinal);
        Assert.Contains("deklareeritud 2 rida", UiText.Worded(ImportRefusal.Of(ImportRefusal.Declared, "2 declared line(s)…", 2)), StringComparison.Ordinal);
        Assert.Contains("2026-11", UiText.Worded(ImportRefusal.Of(ImportRefusal.OtherMonths, "x", 1, "2026-11")), StringComparison.Ordinal);
        Assert.Null(UiText.Worded(new InvalidOperationException("anything else"))); // the exception's own text then
        Assert.Null(UiText.Worded(new ReviewException(ReviewWorkspace.SplitCopy, "x"))); // the amount's own wording lives elsewhere

        var open = UiText.ConfirmRefused(new DeclareException(PeriodSubmitter.LinesPending, "x") { OpenLines = ["7"], RefusedLines = ["9"] });
        Assert.Contains("Vastust ootavad või parandust vajavad read: 7.", open, StringComparison.Ordinal);
        Assert.Contains("tagasi lükatud read (pole serveris): 9.", open, StringComparison.Ordinal);
        Assert.StartsWith("Saatmata mustandread: 3, 4.", UiText.ConfirmRefused(new DeclareException(PeriodSubmitter.LinesPending, "x") { DraftLines = ["3", "4"] }), StringComparison.Ordinal);
    }

    [Fact]
    public void Marking_a_month_confirmed_asks_in_Estonian_naming_the_line_that_waits()
    {
        using var fixture = new WindowFixture();
        var asked = new List<string>();
        Ui.Run(() => fixture.Drive(form =>
        {
            form.Tabs.SelectedIndex = 1;
            Ui.Pump();
            var panel = form.Declaration;
            panel.BuildButton.PerformClick();
            Ui.Until(() => !form.IsBusy && panel.LinesGrid.Rows.Count == 1, what: "the build");
            form.Services!.Submitter.Export(form.View!.Period.Id, fixture.ExportFolder);
            form.Reload();
            Ui.Pump();
            panel.UploadedButton.PerformClick();
            Ui.Pump();
            var key = form.Services.Store.Payments(form.View!.Period.Id).Single().LineNumber;

            panel.ConfirmedButton.PerformClick();
            Ui.Pump();
            var question = Assert.Single(asked);
            Assert.StartsWith($"Vastust ootavad või parandust vajavad read: {key}.", question, StringComparison.Ordinal);
            Assert.DoesNotContain("await", question, StringComparison.Ordinal);
            Assert.EndsWith(UiText.ConfirmForce, question, StringComparison.Ordinal);
            Assert.Equal(PeriodState.Uploaded, form.View!.Period.State);
        }, confirm: (question, title) =>
        {
            if (title == UiText.MarkConfirmed)
            {
                asked.Add(question);
                return false;
            }

            return true;
        }));
    }

    [Fact]
    public void A_month_picked_while_no_company_is_shown_is_the_month_the_company_picked_next_opens_at()
    {
        using var fixture = new WindowFixture();
        Ui.Run(() => fixture.Drive(form =>
        {
            var october = form.MonthBox.SelectedIndex;
            var company = form.CompanyBox.SelectedIndex;
            form.CloseCompanyButton.PerformClick();
            Ui.Pump();
            Assert.Null(form.View);
            form.MonthBox.SelectedIndex = october == 0 ? 1 : october - 1;
            Ui.Pump();
            var picked = form.MonthBox.SelectedIndex;

            form.CompanyBox.SelectedIndex = company;
            Ui.Pump();
            Assert.Equal(picked, form.MonthBox.SelectedIndex);
            Assert.NotEqual(10, AppSettings.Load(fixture.SettingsPath).LastMonth); // and kept for the next session
        }));
    }

    [Fact]
    public void Picking_non_resident_on_the_card_takes_the_persons_own_exemption_figure_with_the_tick_and_resident_gives_both_back()
    {
        using var fixture = new WindowFixture();
        Ui.Run(() => fixture.Drive(form =>
        {
            Mari(form);
            var card = form.PersonEditor;
            card.ExemptionBox.Text = "300";
            card.SaveButton.PerformClick();
            Ui.Until(() => !form.IsBusy, what: "the save of a figure of her own");
            Assert.Equal(300m, StoredMari(form).BasicExemptionMonthly);

            Mari(form);
            var shown = card.ExemptionBox.Text;
            card.ResidencyBox.SelectedIndex = 1;
            Ui.Pump();
            Assert.Equal((false, ""), (card.ExemptionAppliedBox.Checked, card.ExemptionBox.Text));
            card.ResidencyBox.SelectedIndex = 0; // a slip: resident again gives both back
            Ui.Pump();
            Assert.Equal((true, shown), (card.ExemptionAppliedBox.Checked, card.ExemptionBox.Text));

            card.ResidencyBox.SelectedIndex = 1; // an EEA non-resident keeps the exemption: ticked again by hand, the figure with it
            Ui.Pump();
            card.ExemptionAppliedBox.Checked = true;
            Ui.Pump();
            Assert.Equal((true, shown), (card.ExemptionAppliedBox.Checked, card.ExemptionBox.Text));
            card.ResidencyBox.SelectedIndex = 0;
            Ui.Pump();

            card.ResidencyBox.SelectedIndex = 1;
            Ui.Pump();
            card.SaveButton.PerformClick();
            Ui.Until(() => !form.IsBusy, what: "the save as a non-resident");
            var stored = StoredMari(form);
            Assert.Equal((Residency.NonResident, false, (decimal?)null), (stored.Residency, stored.BasicExemptionApplied, stored.BasicExemptionMonthly));
            Assert.Empty(fixture.Failures);
        }));
    }

    [Fact]
    public void A_note_committed_by_a_click_on_another_person_saves_the_note_and_the_click_stands()
    {
        using var fixture = new WindowFixture();
        Ui.Run(() => fixture.Drive(form =>
        {
            Mari(form);
            var grid = form.PaymentsGrid;
            var payment = (ReviewPayment)grid.Rows[0].Tag!;
            grid.CurrentCell = grid.Rows[0].Cells["note"];
            Assert.True(grid.BeginEdit(true));
            Assert.IsAssignableFrom<TextBox>(grid.EditingControl).Text = "kokkulepe";
            Assert.True(grid.EndEdit()); // committed as the click elsewhere commits it: the reload is posted, not yet run
            form.PersonsGrid.CurrentCell = form.PersonsGrid.Rows[1].Cells[0]; // the click on the other person
            Ui.Pump(); // the posted reload runs now

            Assert.Equal(1, form.PersonsGrid.CurrentRow!.Index);
            Assert.Equal("kokkulepe", form.Services!.Store.ReviewLines(form.View!.Period.Id).Single(l => l.RowId == payment.RowId).Note);
            Assert.Empty(fixture.Failures);
        }));
    }

    [Fact]
    public void A_TOR_type_the_cards_list_does_not_know_is_shown_and_kept_on_a_save_of_another_field()
    {
        using var fixture = new WindowFixture();
        Ui.Run(() => fixture.Drive(form =>
        {
            form.Services!.Store.UpdatePerson(StoredMari(form) with { TorEmploymentTypeId = "504" });
            form.Reload();
            Ui.Pump();
            Mari(form);
            var card = form.PersonEditor;
            Assert.Equal("504", card.TorTypeBox.SelectedItem?.ToString());

            card.DeclarationBox.SelectedIndex = 1;
            card.SaveButton.PerformClick();
            Ui.Until(() => !form.IsBusy, what: "the save of another field");
            Assert.Equal(("504", DeclarationChoice.Include), (StoredMari(form).TorEmploymentTypeId, StoredMari(form).Declaration));
            Assert.Empty(fixture.Failures);
        }));
    }

    private static void Mari(MainForm form)
    {
        form.PersonsGrid.CurrentCell = form.PersonsGrid.Rows[0].Cells[0];
        Ui.Pump();
    }

    private static Person StoredMari(MainForm form) => form.Services!.Store.FindPerson(form.View!.Period.CompanyId, SatellesTSD.Tests.ReviewFixtures.MariCode)!;
}
