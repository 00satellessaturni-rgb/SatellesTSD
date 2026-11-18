using SatellesTSD.Core.Declare;
using SatellesTSD.Core.Matching;
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
    public void Unticking_a_residents_exemption_on_the_card_drops_their_own_figure_and_a_retick_before_the_save_keeps_it()
    {
        // the fifth audit, item 20: unticked with a figure of her own kept, every payment of hers was refused
        using var fixture = new WindowFixture();
        Ui.Run(() => fixture.Drive(form =>
        {
            Mari(form);
            var card = form.PersonEditor;
            Assert.True(card.ExemptionAppliedBox.Checked);
            card.ExemptionBox.Text = "300";
            card.SaveButton.PerformClick();
            Ui.Until(() => !form.IsBusy, what: "the save of a figure of her own");
            Assert.Equal((true, (decimal?)300m), (StoredMari(form).BasicExemptionApplied, StoredMari(form).BasicExemptionMonthly));

            Mari(form);
            card.ExemptionAppliedBox.Checked = false; // a resident, unticked by hand
            Ui.Pump();
            Assert.Null(card.Collect()!.BasicExemptionMonthly); // the card itself sends no figure without the application
            card.SaveButton.PerformClick();
            Ui.Until(() => !form.IsBusy, what: "the save unticked");
            Assert.Equal((false, (decimal?)null), (StoredMari(form).BasicExemptionApplied, StoredMari(form).BasicExemptionMonthly));
            var mari = form.View!.Persons.Single(p => p.PersonId == StoredMari(form).Id);
            Assert.DoesNotContain(mari.Payments.SelectMany(p => p.Checks), c => c.Code == Core.Tax.PaymentChecks.ExemptionWithoutApplication);
            Assert.Empty(fixture.Failures);

            // ticked, 300 saved again; then unticked and ticked again before the save: the figure stays
            Mari(form);
            card.ExemptionAppliedBox.Checked = true;
            card.ExemptionBox.Text = "300";
            card.SaveButton.PerformClick();
            Ui.Until(() => !form.IsBusy, what: "the save of the figure again");
            Mari(form);
            var shown = card.ExemptionBox.Text;
            card.ExemptionAppliedBox.Checked = false;
            Ui.Pump();
            card.ExemptionAppliedBox.Checked = true;
            Ui.Pump();
            Assert.Equal(shown, card.ExemptionBox.Text);
            card.SaveButton.PerformClick();
            Ui.Until(() => !form.IsBusy, what: "the save after the slip");
            Assert.Equal((true, (decimal?)300m), (StoredMari(form).BasicExemptionApplied, StoredMari(form).BasicExemptionMonthly));
            Assert.Empty(fixture.Failures);
        }));
    }

    [Fact]
    public void Picking_an_A1_case_on_the_card_fills_its_fixed_country_when_the_box_is_empty_and_never_over_typed_text()
    {
        // the fifth audit, items 14 and 21: an Estonian A1 has the A1 country EE, a foreign A1 the work country EE
        using var fixture = new WindowFixture();
        Ui.Run(() => fixture.Drive(form =>
        {
            Mari(form);
            var card = form.PersonEditor;
            Assert.Equal(("", ""), (card.A1CountryBox.Text, card.WorkCountryBox.Text));
            card.A1Box.SelectedIndex = (int)A1Case.EstonianA1WorkAbroad;
            Ui.Pump();
            Assert.Equal(("EE", ""), (card.A1CountryBox.Text, card.WorkCountryBox.Text));
            card.A1Box.SelectedIndex = (int)A1Case.ForeignA1WorkInEstonia;
            Ui.Pump();
            Assert.Equal(("EE", "EE"), (card.A1CountryBox.Text, card.WorkCountryBox.Text)); // the A1 country typed (here, filled) is kept

            // text already typed is kept
            card.A1Box.SelectedIndex = (int)A1Case.None;
            card.A1CountryBox.Text = "LV";
            card.WorkCountryBox.Text = "FI";
            card.A1Box.SelectedIndex = (int)A1Case.ForeignA1WorkInEstonia;
            Ui.Pump();
            Assert.Equal(("LV", "FI"), (card.A1CountryBox.Text, card.WorkCountryBox.Text));
            card.A1Box.SelectedIndex = (int)A1Case.EstonianA1WorkAbroad;
            Ui.Pump();
            Assert.Equal(("LV", "FI"), (card.A1CountryBox.Text, card.WorkCountryBox.Text));

            // saved with the A1 country left empty: filling the card from the store fills nothing
            card.A1CountryBox.Text = "";
            card.SaveButton.PerformClick();
            Ui.Until(() => !form.IsBusy, what: "the save of an Estonian A1 without its country");
            Assert.Equal((A1Case.EstonianA1WorkAbroad, (string?)null), (StoredMari(form).A1, StoredMari(form).A1Country));
            Mari(form);
            Assert.Equal((A1Case.EstonianA1WorkAbroad, "", "FI"), ((A1Case)card.A1Box.SelectedIndex, card.A1CountryBox.Text, card.WorkCountryBox.Text));
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

    [Fact]
    public void The_calculator_computes_a_pension_age_person_and_says_in_Estonian_what_it_cannot_use()
    {
        // the fifth audit, item 13
        Ui.Run(() =>
        {
            using var dialog = new CalculatorDialog();
            var said = new List<string>();
            dialog.Complaining = said.Add;

            // the pension-age tick: the person's own 776 a month is the legal figure and is used
            dialog.AmountBox.Text = "1000";
            dialog.ExemptionBox.Text = "776";
            dialog.PensionAgeBox.Checked = true;
            dialog.ComputeNow();
            Assert.Empty(said);
            var used = Assert.Single(dialog.OutputText.Split(Environment.NewLine), l => l.StartsWith(UiText.ExemptionUsed, StringComparison.Ordinal));
            Assert.EndsWith(UiText.Money(776m), used, StringComparison.Ordinal);

            // without it, 776 is above the legal 700: said before anything is computed, the engine's English never shown
            dialog.PensionAgeBox.Checked = false;
            var shown = dialog.OutputText;
            dialog.ComputeNow();
            Assert.Equal(UiText.CalculatorExemptionOutOfRange(700m), Assert.Single(said));
            Assert.Equal(shown, dialog.OutputText);

            // a share that is no number, or outside above 0 up to 1, is said, never replaced by 1
            dialog.ExemptionBox.Text = "";
            foreach (var share in new[] { "abc", "1,5", "1.5", "0" })
            {
                said.Clear();
                dialog.ShareBox.Text = share;
                dialog.ComputeNow();
                Assert.Equal(UiText.CalculatorShareInvalid(share), Assert.Single(said));
            }

            // an amount or an exemption that is no number, in the words of the field
            said.Clear();
            dialog.ShareBox.Text = "1";
            dialog.AmountBox.Text = "abc";
            dialog.ComputeNow();
            dialog.AmountBox.Text = "1000";
            dialog.ExemptionBox.Text = "x";
            dialog.ComputeNow();
            Assert.Equal([UiText.SimNotANumber(UiText.Amount, "abc"), UiText.SimNotANumber(UiText.ExemptionMonthly, "x")], said);

            // the engine's other refusals of an own figure are said in Estonian too: no application, part of a cent
            said.Clear();
            dialog.ExemptionBox.Text = "500";
            dialog.ExemptionAppliedBox.Checked = false;
            dialog.ComputeNow();
            dialog.ExemptionAppliedBox.Checked = true;
            dialog.ExemptionBox.Text = "500.555";
            dialog.ComputeNow();
            Assert.Equal([UiText.CalculatorExemptionWithoutApplication, UiText.CalculatorExemptionSubCent("500.555")], said);
        });
    }

    [Fact]
    public void The_phrases_messages_name_the_search_by_its_commands_own_name()
    {
        // the fifth audit, item 18
        foreach (var text in new[] { UiText.PhrasesResetConfirm, UiText.PhrasesImported(3), UiText.PhrasesResetDone })
        {
            Assert.Contains($"„{UiText.RunMatch}“", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Sobita", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_phrases_cell_that_does_not_read_is_refused_in_Estonian_throughout()
    {
        // the fifth audit, item 27
        static string Phrases(string cell) => $$"""{ "kind": "{{MatchCatalog.PhrasesKind}}", "sets": { "standard": { "Palgad": "{{cell}}" } } }""";
        var unclosed = Assert.Throws<MatchException>(() => MatchCatalog.Default.WithPhrases(Phrases("palk["), out _));
        Assert.Equal("Fraasid ei ole loetavad (standard / Palgad): mustris „palk[“ jääb 5. märgil olev „[“ sulgemata. Midagi ei salvestatud.", UiText.Worded(unclosed));
        var downwards = Assert.Throws<MatchException>(() => MatchCatalog.Default.WithPhrases(Phrases("*[z-a]*"), out _));
        Assert.Equal("Fraasid ei ole loetavad (standard / Palgad): mustris „*[z-a]*“ on vahemik z-a kirjutatud tagurpidi (suurem märk enne väiksemat). Midagi ei salvestatud.", UiText.Worded(downwards));
        // a merged catalog that fails its check names no pattern: a general sentence
        var general = PhrasesRefusal.Of(PhrasesRefusal.BadCell, "The phrases cannot be used:\n'x[': the \"[\" at position 2 is never closed.");
        Assert.Equal("Fraasid ei ole koos sisseehitatud kataloogiga kasutatavad. Midagi ei salvestatud.", UiText.Worded(general));
        foreach (var refusal in new[] { unclosed, downwards, general })
        {
            Assert.DoesNotContain("never", UiText.Worded(refusal), StringComparison.Ordinal);
            Assert.DoesNotContain("downwards", UiText.Worded(refusal), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MTAs_answers_are_worded_in_Estonian_never_as_the_enums_names()
    {
        // the fifth audit, item 29
        foreach (var result in Enum.GetValues<Core.Feedback.ProcessingResult>())
        {
            Assert.NotEqual(result.ToString(), UiText.ProcessingResultText(result));
        }

        foreach (var outcome in Enum.GetValues<Core.Feedback.ConfirmationOutcome>())
        {
            Assert.NotEqual(outcome.ToString(), UiText.ConfirmationText(outcome));
            Assert.DoesNotContain(outcome.ToString(), UiText.ConfirmationReportLine("x.xml", outcome), StringComparison.Ordinal);
        }

        Assert.Equal("korras", UiText.ProcessingResultText(Core.Feedback.ProcessingResult.Ok));
        Assert.Contains("hiljem uuesti", UiText.ConfirmationReportLine("x.xml", Core.Feedback.ConfirmationOutcome.StillProcessing), StringComparison.Ordinal);
    }

    private static void Mari(MainForm form)
    {
        form.PersonsGrid.CurrentCell = form.PersonsGrid.Rows[0].Cells[0];
        Ui.Pump();
    }

    private static Person StoredMari(MainForm form) => form.Services!.Store.FindPerson(form.View!.Period.CompanyId, SatellesTSD.Tests.ReviewFixtures.MariCode)!;
}
