using System.Globalization;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Review;
using SatellesTSD.Core.Store;
using SatellesTSD.Core.Tax;

namespace SatellesTSD.WindowTests;

/// <summary>The person's partial or no work ability and the employer's application under it, as the windows show them:
/// the card's two ticks (the application only with the status; the status takes the minimum off as the pension tick
/// does), the Calculator, the simulation, the rules matrix's column O, and TSD_OPTV_SM_VAHENDUS beside the minimum's
/// increase on the declared lines and in the build's log.</summary>
public class WorkAbilityWindowTests
{
    private static readonly CultureInfo Et = CultureInfo.GetCultureInfo("et-EE");

    private static Person Worker(bool minimum = true, bool status = false, bool application = false) =>
        Person.New(Guid.NewGuid(), "Tiiu", "Tamm", "49001010012") with
        {
            BasicExemptionApplied = true, SocialTaxMinimumApplies = minimum, ReducedWorkAbility = status, StatePaysSocialTax = application,
        };

    [Fact]
    public void The_cards_application_opens_only_with_the_status_and_the_status_leaves_the_minimums_tick_alone()
    {
        Ui.Run(() =>
        {
            using var card = new PersonPanel();
            card.Present(Worker(), editable: true, 2026, TaxRates.Year2026);
            Assert.Equal((false, false, false), (card.WorkAbilityBox.Checked, card.StatePaysBox.Checked, card.StatePaysBox.Enabled));
            Assert.True(card.MinimumBox.Checked);

            // the status leaves the minimum's tick as it is (decision 266): the card keeps what holds after the status
            card.WorkAbilityBox.Checked = true;
            Assert.True(card.MinimumBox.Checked);
            Assert.True(card.StatePaysBox.Enabled);
            card.StatePaysBox.Checked = true;
            Assert.True(card.IsDirty);
            var collected = card.Collect()!;
            Assert.Equal((true, true, true), (collected.ReducedWorkAbility, collected.StatePaysSocialTax, collected.SocialTaxMinimumApplies));
            Assert.False(collected.MinimumOwed); // not owed while the status stands, whatever the tick says

            // the status's untick takes the application off and shuts it; the minimum's tick was never touched
            card.WorkAbilityBox.Checked = false;
            Assert.Equal((false, false, true), (card.StatePaysBox.Checked, card.StatePaysBox.Enabled, card.MinimumBox.Checked));
            Assert.Equal((false, false), (card.Collect()!.ReducedWorkAbility, card.Collect()!.StatePaysSocialTax));

            // the pension's tick still takes the minimum off and gives it back, with the status ticked or not
            card.WorkAbilityBox.Checked = true;
            card.OnPensionBox.Checked = true;
            Assert.False(card.MinimumBox.Checked);
            card.OnPensionBox.Checked = false;
            Assert.True(card.MinimumBox.Checked);
            card.WorkAbilityBox.Checked = false;
            Assert.True(card.MinimumBox.Checked);

            // the status ticked and saved on one visit to the card, unticked on a later one: the minimum is owed again
            card.Present(Worker(), editable: true, 2026, TaxRates.Year2026);
            card.WorkAbilityBox.Checked = true;
            var saved = card.Collect()!;
            card.Present(saved, editable: true, 2026, TaxRates.Year2026);
            Assert.Equal((true, true), (card.WorkAbilityBox.Checked, card.MinimumBox.Checked));
            card.WorkAbilityBox.Checked = false;
            Assert.True(card.Collect()!.MinimumOwed);

            // a stored card is shown as stored; a card shown read-only keeps the application shut
            card.Present(Worker(minimum: false, status: true, application: true), editable: true);
            Assert.Equal((true, true, true, false), (card.WorkAbilityBox.Checked, card.StatePaysBox.Checked, card.StatePaysBox.Enabled, card.MinimumBox.Checked));
            Assert.False(card.IsDirty);
            card.Present(Worker(minimum: false, status: true, application: true), editable: false);
            Assert.False(card.StatePaysBox.Enabled);
            // an application stored without the status (the Core refuses it) is never shown as standing
            card.Present(Worker(application: true), editable: true);
            Assert.Equal((false, false), (card.StatePaysBox.Checked, card.StatePaysBox.Enabled));
            Assert.False(card.Collect()!.StatePaysSocialTax);
        });
    }

    [Fact]
    public void The_card_says_what_the_app_cannot_check_where_the_application_is_ticked()
    {
        Ui.Run(() =>
        {
            using var form = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000) };
            var card = new PersonPanel { Dock = DockStyle.Fill };
            form.Controls.Add(card);
            form.Show();
            card.Present(Worker(), editable: true, 2026, TaxRates.Year2026);
            static IEnumerable<Control> All(Control c) => c.Controls.Cast<Control>().SelectMany(child => All(child).Prepend(child));
            var note = Assert.Single(All(card).OfType<Label>(), l => l.Text == UiText.StatePaysSocialTaxNote);
            Assert.Contains("KR_SM_03", note.Text, StringComparison.Ordinal);
            // shown only while the status is ticked: by the accountant's tick, and as a card is shown
            Assert.False(note.Visible);
            card.WorkAbilityBox.Checked = true;
            Assert.True(note.Visible);
            card.WorkAbilityBox.Checked = false;
            Assert.False(note.Visible);
            card.Present(Worker(status: true), editable: true, 2026, TaxRates.Year2026);
            Assert.True(note.Visible);
            card.Present(Worker(), editable: true, 2026, TaxRates.Year2026);
            Assert.False(note.Visible);
            form.Close();
            // the minimum's tip no longer tells the accountant to untick it by hand for a person with the status, and says
            // the status does not change the tick
            Assert.Contains(UiText.ReducedWorkAbility, UiText.MinimumPensionTip, StringComparison.Ordinal);
            Assert.Contains("miinimumi märget see ei muuda", UiText.MinimumPensionTip, StringComparison.Ordinal);
            Assert.DoesNotContain("töövõimetute puhul eemalda", UiText.MinimumPensionTip, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_card_saved_with_both_ticks_builds_a_line_with_the_states_part_and_the_log_says_the_month()
    {
        using var fixture = new WindowFixture();
        Ui.Run(() => fixture.Drive(form =>
        {
            form.PersonsGrid.CurrentCell = form.PersonsGrid.Rows[0].Cells[0];
            Ui.Pump();
            var card = form.PersonEditor;
            card.WorkAbilityBox.Checked = true;
            card.StatePaysBox.Checked = true;
            card.SaveButton.PerformClick();
            Ui.Pump();
            var stored = form.Services!.Store.FindPerson(form.View!.Period.CompanyId, SatellesTSD.Tests.ReviewFixtures.MariCode)!;
            Assert.Equal((true, true), (stored.ReducedWorkAbility, stored.StatePaysSocialTax));

            form.Tabs.SelectedIndex = 1;
            Ui.Pump();
            var panel = form.Declaration;
            panel.BuildButton.PerformClick();
            Ui.Until(() => !form.IsBusy && panel.LinesGrid.Rows.Count == 1, what: "the build");
            var row = panel.LinesGrid.Rows[0];
            var result = ((Core.Declare.DeclaredLine)row.Tag!).Payment.Result!;
            Assert.Equal(TaxRates.Year2026.SocialTaxMinimumBase, (decimal)row.Cells["smminus"].Value!); // the salary is above one kuumäär
            Assert.Equal(result.SocialTax, (decimal)row.Cells["sm"].Value!);
            Assert.Equal(Math.Round((result.Gross - TaxRates.Year2026.SocialTaxMinimumBase) * TaxRates.Year2026.SocialTax, 2, MidpointRounding.AwayFromZero), result.SocialTax);
            Assert.Equal(panel.LinesGrid.Columns["smplus"]!.Index + 1, panel.LinesGrid.Columns["smminus"]!.Index); // beside the minimum's increase
            Assert.Contains(form.LogList.Items.Cast<object>(), i => i.ToString()!.Contains("TSD_OPTV_SM_VAHENDUS", StringComparison.Ordinal));
        }));
    }

    [Fact]
    public void The_calculator_takes_one_kuumaar_off_the_social_tax_and_its_application_needs_the_status()
    {
        static string Line(CalculatorDialog dialog, string label) =>
            Assert.Single(dialog.OutputText.Split(Environment.NewLine), l => l.StartsWith(label, StringComparison.Ordinal));

        Ui.Run(() =>
        {
            using var dialog = new CalculatorDialog();
            var said = new List<string>();
            dialog.Complaining = said.Add;
            Assert.False(dialog.StatePaysBox.Enabled);
            dialog.AmountBox.Text = "500";
            dialog.ComputeNow();
            Assert.EndsWith(UiText.Money(0m), Line(dialog, UiText.WorkAbilityReduction), StringComparison.Ordinal);

            // the status alone: no minimum (its tick stays, and is not reckoned), no deduction
            dialog.WorkAbilityBox.Checked = true;
            Assert.True(dialog.MinimumBox.Checked);
            Assert.True(dialog.StatePaysBox.Enabled);
            dialog.ComputeNow();
            Assert.EndsWith(UiText.Money(0m), Line(dialog, UiText.MinimumIncrease), StringComparison.Ordinal);
            Assert.EndsWith(UiText.Money(0m), Line(dialog, UiText.WorkAbilityReduction), StringComparison.Ordinal);
            Assert.False(Line(dialog, $"{UiText.SocialTax,-28}").EndsWith(UiText.Money(0m), StringComparison.Ordinal));

            // with the application: a net of 500 is below one kuumäär, deducted whole, no social tax of the employer's
            dialog.StatePaysBox.Checked = true;
            dialog.ComputeNow();
            Assert.Empty(said);
            Assert.False(Line(dialog, UiText.WorkAbilityReduction).EndsWith(UiText.Money(0m), StringComparison.Ordinal));
            Assert.EndsWith(UiText.Money(0m), Line(dialog, $"{UiText.SocialTax,-28}"), StringComparison.Ordinal);

            // a gross of 1000: one kuumäär off, the rest taxed
            dialog.AmountBox.Text = "1000";
            dialog.DirectionBox.SelectedIndex = 1; // gross
            dialog.ComputeNow();
            Assert.EndsWith(UiText.Money(TaxRates.Year2026.SocialTaxMinimumBase), Line(dialog, UiText.WorkAbilityReduction), StringComparison.Ordinal);
            Assert.EndsWith(UiText.Money(Math.Round((1000m - TaxRates.Year2026.SocialTaxMinimumBase) * TaxRates.Year2026.SocialTax, 2)), Line(dialog, $"{UiText.SocialTax,-28}"), StringComparison.Ordinal);

            // the status's untick takes the application off; the minimum's tick stood all along
            dialog.WorkAbilityBox.Checked = false;
            Assert.Equal((false, false, true), (dialog.StatePaysBox.Checked, dialog.StatePaysBox.Enabled, dialog.MinimumBox.Checked));
        });
    }

    [Fact]
    public void The_simulation_runs_with_the_two_ticks_shows_the_deduction_and_its_file_keeps_them()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"satellestsd-workability-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            Ui.Run(() =>
            {
                using var dialog = new SimulationDialog(2026, 10) { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000) };
                dialog.Show(); // its buttons click only when shown
                Ui.Pump();
                var warnings = new List<string>();
                dialog.Warning = warnings.Add;
                Assert.False(dialog.StatePaysBox.Enabled);
                dialog.WorkAbilityBox.Checked = true;
                Assert.True(dialog.MinimumBox.Checked); // the status leaves the tick; the run owes no minimum by the status
                dialog.StatePaysBox.Checked = true;
                Assert.Equal((true, true), (dialog.Scenario()!.Person.ReducedWorkAbility, dialog.Scenario()!.Person.StatePaysSocialTax));

                // the example's salary of 1000 net: one kuumäär off, shown beside the minimum's increase
                dialog.RunNow();
                Assert.Empty(warnings);
                var line = Assert.Single(dialog.LinesGrid.Rows.Cast<DataGridViewRow>());
                Assert.Equal(TaxRates.Year2026.SocialTaxMinimumBase.ToString("#,##0.00", Et), (string?)line.Cells["reduction"].Value);
                Assert.Equal(dialog.LinesGrid.Columns["increase"]!.Index + 1, dialog.LinesGrid.Columns["reduction"]!.Index);
                Assert.Contains("TSD_OPTV_SM_VAHENDUS", dialog.ReviewText, StringComparison.Ordinal);
                Assert.Contains("TSD_OPTV_SM_VAHENDUS", dialog.XmlText, StringComparison.Ordinal);

                // saved and opened back: the ticks go with the file
                var path = Path.Combine(folder, "simulatsioon.json");
                dialog.ChoosingFile = _ => path;
                dialog.SaveButton.PerformClick();
                var saved = SimScenario.Parse(File.ReadAllText(path)).Person;
                Assert.Equal((true, true), (saved.ReducedWorkAbility, saved.StatePaysSocialTax));
                dialog.WorkAbilityBox.Checked = false;
                dialog.OpeningFile = () => path;
                dialog.OpenButton.PerformClick();
                Assert.Equal((true, true, true), (dialog.WorkAbilityBox.Checked, dialog.StatePaysBox.Checked, dialog.StatePaysBox.Enabled));

                // a file saved before the ticks opens with neither
                var old = Path.Combine(folder, "vana.json");
                var json = File.ReadAllText(path);
                File.WriteAllText(old, System.Text.RegularExpressions.Regex.Replace(json, ",\\s*\"(ReducedWorkAbility|StatePaysSocialTax)\"\\s*:\\s*(true|false)", ""));
                Assert.DoesNotContain("ReducedWorkAbility", File.ReadAllText(old), StringComparison.Ordinal);
                dialog.OpeningFile = () => old;
                dialog.OpenButton.PerformClick();
                Assert.Empty(warnings);
                Assert.Equal((false, false, false), (dialog.WorkAbilityBox.Checked, dialog.StatePaysBox.Checked, dialog.StatePaysBox.Enabled));
            });
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void The_rules_matrix_shows_column_O_and_the_case_with_the_application_deducts_on_wages_alone()
    {
        Ui.Run(() =>
        {
            using var dialog = new RulesDialog(TaxRates.Year2026, 2026);
            string Cell(string code, string column) =>
                (string)dialog.MatrixGrid.Rows.Cast<DataGridViewRow>().Single(r => (string?)r.Cells["code"].Value == code).Cells[column].Value!;

            Assert.False(dialog.StatePaysBox.Enabled);
            Assert.Equal((UiText.RulesAllowed, UiText.RulesNone, UiText.Yes), (Cell("10", "optv"), Cell("21", "optv"), Cell("10", "min")));

            dialog.WorkAbilityBox.Checked = true;
            Assert.Equal((UiText.RulesAllowed, UiText.RulesNone), (Cell("10", "optv"), Cell("10", "min"))); // no minimum by the status alone
            dialog.StatePaysBox.Checked = true;
            var applied = UiText.RulesWorkAbilityApplied(TaxRates.Year2026.SocialTaxMinimumBase);
            Assert.Equal((applied, applied, UiText.RulesNone, UiText.RulesNone), (Cell("10", "optv"), Cell("33", "optv"), Cell("21", "optv"), Cell("17", "optv")));
            Assert.Equal((true, true), (dialog.Case().ReducedWorkAbility, dialog.Case().StatePaysSocialTax));

            // a foreign A1 carries no Estonian social tax: nothing to deduct
            dialog.A1Box.SelectedIndex = 2;
            Assert.Equal(UiText.RulesNone, Cell("10", "optv"));
            dialog.A1Box.SelectedIndex = 0;

            dialog.WorkAbilityBox.Checked = false;
            Assert.Equal((false, false, UiText.RulesAllowed), (dialog.StatePaysBox.Checked, dialog.StatePaysBox.Enabled, Cell("10", "optv")));
            Assert.Contains("TSD_OPTV_SM_VAHENDUS", dialog.NotesText, StringComparison.Ordinal);
            Assert.Contains("KR_SM_03", dialog.NotesText, StringComparison.Ordinal);
            Assert.Contains(dialog.RatesGrid.Rows.Cast<DataGridViewRow>(), r => (string?)r.Cells["field"].Value == "TSD_OPTV_SM_VAHENDUS");
        });
    }
}
