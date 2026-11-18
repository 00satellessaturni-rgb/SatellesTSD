using SatellesTSD.Core.Model;
using SatellesTSD.Core.Store;

namespace SatellesTSD.WindowTests;

/// <summary>The owner's four requests of 2026-11-18 (plan item 62) as the window runs them: the one click that ticks the
/// basic exemption for the month's non-residents (decision 201), the category phrases' file (202), the rules panel (203)
/// and the simulation (204).</summary>
public class OwnerToolsWindowTests
{
    [Fact]
    public void The_menu_ticks_the_exemption_for_the_months_non_residents_after_one_question_and_says_when_nobody_is_left()
    {
        using var fixture = new WindowFixture();
        var asked = new List<string>();
        Ui.Run(() => fixture.Drive(form =>
        {
            var notices = new List<string>();
            form.Informing = notices.Add;
            form.Services!.Store.UpdatePerson(StoredMari(form) with { Residency = Residency.NonResident, BasicExemptionApplied = false });
            form.Reload();
            Ui.Pump();

            Assert.True(form.ExemptionOnButton.Enabled);
            form.ExemptionOnButton.PerformClick();
            Ui.Pump();
            Assert.Equal(UiText.ExemptionOnQuestion(1, 0), Assert.Single(asked));
            Assert.Equal((Residency.NonResident, true), (StoredMari(form).Residency, StoredMari(form).BasicExemptionApplied));
            Mari(form);
            Assert.True(form.PersonEditor.ExemptionAppliedBox.Checked); // the card shows it

            // nobody left: said, nothing asked
            form.ExemptionOnButton.PerformClick();
            Ui.Pump();
            Assert.Single(asked);
            Assert.Equal(UiText.ExemptionOnNone(0), Assert.Single(notices));

            // a card naming a country outside the EEA is left out, and the notice says so
            form.Services.Store.UpdatePerson(StoredMari(form) with { BasicExemptionApplied = false, ResidenceCountry = "US" });
            form.Reload();
            Ui.Pump();
            form.ExemptionOnButton.PerformClick();
            Ui.Pump();
            Assert.Single(asked);
            Assert.Equal(UiText.ExemptionOnNone(1), notices[^1]);
            Assert.False(StoredMari(form).BasicExemptionApplied);
            Assert.Empty(fixture.Failures);
        }, confirm: (question, title) =>
        {
            if (title == UiText.ExemptionOnNonResidents)
            {
                asked.Add(question);
            }

            return true;
        }));
    }

    [Fact]
    public void The_phrases_dialog_saves_the_phrases_reads_them_back_refuses_a_bad_file_in_Estonian_and_restores_the_built_in_ones()
    {
        using var fixture = new WindowFixture();
        Ui.Run(() => fixture.Drive(form =>
        {
            var warnings = new List<string>();
            Assert.True(form.PhrasesItem.Enabled);
            PhrasesDialog? shown = null;
            form.PhrasesOpening = dialog =>
            {
                shown = dialog;
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(-4000, -4000);
                dialog.Show();
                Ui.Pump();
                dialog.Warning = warnings.Add;
                dialog.Confirming = (_, _) => true;
                Assert.Equal(UiText.PhrasesBuiltIn, dialog.StandingText);
                Assert.False(dialog.ResetButton.Enabled);
                Assert.Contains(dialog.List.Items.Cast<ListViewItem>(), i => i.SubItems[0].Text == "standard" && i.SubItems[1].Text == "Palgad" && i.SubItems[3].Text == "*laen*");

                Directory.CreateDirectory(fixture.ExportFolder);
                var path = Path.Combine(fixture.ExportFolder, "kategooriad.json");
                string? offered = null;
                dialog.ChoosingFile = name => { offered = name; return path; };
                dialog.SaveButton.PerformClick();
                Assert.Equal("kategooriad.json", offered);
                var saved = File.ReadAllText(path);
                File.WriteAllText(path, saved.Replace("\"*dividen*\"", "\"*dividen*,*kasumiosa*\"", StringComparison.Ordinal));

                dialog.OpeningFile = () => path;
                dialog.ImportButton.PerformClick();
                Assert.Empty(warnings);
                Assert.Equal(UiText.PhrasesOwn, dialog.StandingText);
                Assert.Contains(dialog.List.Items.Cast<ListViewItem>(), i => i.SubItems[1].Text == "Dividendid" && i.SubItems[2].Text == "*dividen*, *kasumiosa*");

                var bad = Path.Combine(fixture.ExportFolder, "vale.json");
                File.WriteAllText(bad, $$"""{ "kind": "{{Core.Matching.MatchCatalog.PhrasesKind}}", "sets": { "standard": { "Boonus": "*boonus*" } } }""");
                dialog.OpeningFile = () => bad;
                dialog.ImportButton.PerformClick();
                Assert.StartsWith("Failis on kategooria, mida rakendus ei tunne (standard / Boonus).", Assert.Single(warnings), StringComparison.Ordinal);

                Assert.True(dialog.ResetButton.Enabled);
                dialog.ResetButton.PerformClick();
                Assert.Equal(UiText.PhrasesBuiltIn, dialog.StandingText);
                dialog.Close();
            };
            form.PhrasesItem.PerformClick();
            Ui.Pump();
            Assert.NotNull(shown);
            Assert.False(form.Services!.Pipeline.OwnPhrases);
            Assert.Empty(fixture.Failures);
        }));
    }

    [Fact]
    public void The_rules_panel_lists_every_type_redraws_for_the_toggles_and_finds_by_the_search()
    {
        using var fixture = new WindowFixture();
        Ui.Run(() => fixture.Drive(form =>
        {
            RulesDialog? shown = null;
            form.RulesOpening = dialog =>
            {
                shown = dialog;
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(-4000, -4000);
                dialog.Show();
                Ui.Pump();
                var grid = dialog.MatrixGrid;
                Assert.Equal(Core.Tax.PaymentTypes.All.Count, grid.Rows.Count);
                DataGridViewRow Row(string code) => grid.Rows.Cast<DataGridViewRow>().Single(r => (string?)r.Cells["code"].Value == code);
                Assert.Equal(("22 %", "MVT_610", "33 %", UiText.Yes, "2/4/6 %"), ((string?)Row("10").Cells["tm"].Value, (string?)Row("10").Cells["mvt"].Value, (string?)Row("10").Cells["sm"].Value, (string?)Row("10").Cells["min"].Value, (string?)Row("10").Cells["kp"].Value));
                Assert.Equal(SystemColors.GrayText, Row("120").DefaultCellStyle.ForeColor); // a non-resident's code for a resident

                dialog.A1Box.SelectedIndex = 2; // a foreign A1: no social tax
                Assert.Equal(UiText.RulesNone, (string?)Row("10").Cells["sm"].Value);
                dialog.A1Box.SelectedIndex = 0;
                dialog.PensionAgeBox.Checked = true;
                Assert.Equal("MVT_650", (string?)Row("10").Cells["mvt"].Value);

                dialog.ResidencyBox.SelectedIndex = 1;
                dialog.TreatyBox.Text = "5";
                Assert.Equal(("5 %", UiText.RulesNone), ((string?)Row("123").Cells["tm"].Value, (string?)Row("120").Cells["kp"].Value));
                dialog.OnlyCaseBox.Checked = true;
                Assert.All(grid.Rows.Cast<DataGridViewRow>(), r => Assert.Equal(Residency.NonResident, ((Core.Tax.RuleRow)r.Tag!).Type.Payee));

                dialog.SearchBox.Text = "601";
                Assert.Equal("156", (string?)Assert.Single(grid.Rows.Cast<DataGridViewRow>()).Cells["code"].Value);
                Assert.Equal(UiText.RulesShown(1, Core.Tax.PaymentTypes.All.Count), dialog.ShownText);

                Assert.Equal(UiText.RatesInForce(Core.Tax.TaxRates.Year2026).Count, dialog.RatesGrid.Rows.Count);
                Assert.Contains("292,38 €", dialog.RatesGrid.Rows.Cast<DataGridViewRow>().Select(r => (string?)r.Cells["value"].Value).First(v => v!.Contains("päevad / 30", StringComparison.Ordinal)), StringComparison.Ordinal);
                Assert.Contains("KR_MVT_08", dialog.NotesText, StringComparison.Ordinal);
                dialog.Close();
            };
            Assert.True(form.RulesButton.Enabled);
            form.RulesButton.PerformClick();
            Ui.Pump();
            Assert.NotNull(shown);
            Assert.Empty(fixture.Failures);
        }));
    }

    [Fact]
    public void The_simulation_runs_the_example_saves_and_opens_a_scenario_and_says_what_it_cannot_read()
    {
        using var fixture = new WindowFixture();
        Ui.Run(() => fixture.Drive(form =>
        {
            SimulationDialog? shown = null;
            form.SimulationOpening = dialog =>
            {
                shown = dialog;
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(-4000, -4000);
                dialog.Show();
                Ui.Pump();
                var warnings = new List<string>();
                dialog.Warning = warnings.Add;
                Assert.Equal((form.View!.Period.Year, form.View.Period.Month), ((int)dialog.YearBox.Value, (int)dialog.MonthBox.Value));
                Assert.Single(dialog.EmploymentsGrid.Rows.Cast<DataGridViewRow>(), r => !r.IsNewRow);

                // the example: a resident's salary of 1000 under a contract of employment
                dialog.RunButton.PerformClick();
                Ui.Pump();
                Assert.Empty(warnings);
                Assert.Null(dialog.Result!.Refusal);
                var line = Assert.Single(dialog.LinesGrid.Rows.Cast<DataGridViewRow>());
                Assert.Equal("10", (string?)line.Cells["type"].Value);
                Assert.Equal(1000m.ToString("#,##0.00", System.Globalization.CultureInfo.GetCultureInfo("et-EE")), (string?)line.Cells["net"].Value);
                Assert.Contains("Sammud:", dialog.ReviewText, StringComparison.Ordinal);
                Assert.Contains(Infrastructure.Simulator.RegistryCode, dialog.XmlText, StringComparison.Ordinal);

                // saved, changed, opened back
                Directory.CreateDirectory(fixture.ExportFolder);
                var path = Path.Combine(fixture.ExportFolder, "simulatsioon.json");
                dialog.ChoosingFile = _ => path;
                dialog.ResidencyBox.SelectedIndex = 1;
                dialog.SaveButton.PerformClick();
                Assert.Equal(Residency.NonResident, Core.Review.SimScenario.Parse(File.ReadAllText(path)).Person.Residency);
                dialog.ResidencyBox.SelectedIndex = 0;
                dialog.FirstNameBox.Text = "Muu";
                dialog.OpeningFile = () => path;
                dialog.OpenButton.PerformClick();
                Assert.Equal((1, "Näidis"), (dialog.ResidencyBox.SelectedIndex, dialog.FirstNameBox.Text));
                Assert.Empty(warnings);
                // the earlier run's output goes with the scenario it was run on (the fifth audit, item 26)
                Assert.Null(dialog.Result);
                Assert.Equal((0, "", ""), (dialog.LinesGrid.Rows.Count, dialog.ReviewText, dialog.XmlText));
                dialog.RunButton.PerformClick();
                Ui.Pump();
                Assert.NotNull(dialog.Result);

                // a file of another kind, a day it cannot read: said, nothing run
                var other = Path.Combine(fixture.ExportFolder, "muu.json");
                File.WriteAllText(other, "{ \"kind\": \"satellestsd-category-phrases\" }");
                dialog.OpeningFile = () => other;
                dialog.OpenButton.PerformClick();
                Assert.Equal(UiText.SimNotAScenario, Assert.Single(warnings));
                var result = dialog.Result;
                Assert.NotNull(result); // a file refused leaves the results as they were
                var day = dialog.RowsGrid.Rows[0].Cells["date"].Value;
                dialog.RowsGrid.Rows[0].Cells["date"].Value = "32.13.2026";
                dialog.RunButton.PerformClick();
                Assert.Equal(UiText.SimNotADay(UiText.SimDate, "32.13.2026"), warnings[^1]);
                Assert.Same(result, dialog.Result);

                // an amount typed that is no figure is said as one, not as a row left incomplete (the fifth audit, item 28)
                dialog.RowsGrid.Rows[0].Cells["date"].Value = day;
                dialog.RowsGrid.Rows[0].Cells["amount"].Value = "1.000,50";
                dialog.RunButton.PerformClick();
                Assert.Equal(UiText.SimNotANumber(UiText.Amount, "1.000,50"), warnings[^1]);
                Assert.DoesNotContain(UiText.SimRowIncomplete(1), warnings[^1], StringComparison.Ordinal);
                Assert.Same(result, dialog.Result);
                dialog.RowsGrid.Rows[0].Cells["amount"].Value = "";
                dialog.RunButton.PerformClick();
                Assert.Equal(UiText.SimRowIncomplete(1), warnings[^1]);
                Assert.Same(result, dialog.Result);

                // a hand-edited file with an empty row; a pension year that is no year
                var edited = Path.Combine(fixture.ExportFolder, "tyhi.json");
                File.WriteAllText(edited, File.ReadAllText(path).Replace("\"Rows\": [", "\"Rows\": [ null,", StringComparison.Ordinal));
                dialog.OpeningFile = () => edited;
                dialog.OpenButton.PerformClick();
                Assert.Equal(UiText.SimNotAScenario, warnings[^1]);
                dialog.OpeningFile = () => path;
                dialog.OpenButton.PerformClick();
                Assert.Null(dialog.Result);
                dialog.RunButton.PerformClick();
                Ui.Pump();
                result = dialog.Result;
                Assert.NotNull(result);
                dialog.PensionAgeYearBox.Text = "99999999999";
                dialog.RunButton.PerformClick();
                Assert.Equal(UiText.SimPensionYearInvalid, warnings[^1]);
                Assert.Same(result, dialog.Result);
                Assert.Empty(fixture.Failures);
                dialog.Close();
            };
            Assert.True(form.SimulationButton.Enabled);
            form.SimulationButton.PerformClick();
            Ui.Pump();
            Assert.NotNull(shown);
            Assert.Empty(fixture.Failures);
        }));
    }

    [Fact]
    public void The_simulation_says_a_year_without_rates_as_the_main_window_does()
    {
        // the build's refusals the main window words itself are worded once, for every place that shows one (the fifth audit, item 19)
        Assert.Equal(UiText.RatesMissing, UiText.Worded(new Core.Declare.DeclareException(Core.Declare.PeriodDeclarer.RatesMissing, "x")));
        Assert.Equal(UiText.RunStaleRefused, UiText.Worded(new Core.Declare.DeclareException(Core.Declare.PeriodDeclarer.RunStale, "x")));
        Assert.Equal(UiText.DeclareModeRefused, UiText.Worded(new Core.Declare.DeclareException(Core.Declare.PeriodDeclarer.ModeChanged, "x")));
        Assert.Null(UiText.Worded(new Core.Declare.DeclareException("SOMETHING_ELSE", "x")));

        using var fixture = new WindowFixture();
        Ui.Run(() => fixture.Drive(form =>
        {
            SimulationDialog? shown = null;
            form.SimulationOpening = dialog =>
            {
                shown = dialog;
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(-4000, -4000);
                dialog.Show();
                Ui.Pump();
                var warnings = new List<string>();
                dialog.Warning = warnings.Add;
                dialog.Fill(SimulationDialog.Example(2025, 10));
                dialog.RunButton.PerformClick();
                Ui.Pump();
                Assert.Empty(warnings);
                Assert.Contains("PEATUS: " + UiText.RatesMissingFor("2025"), dialog.ReviewText, StringComparison.Ordinal); // the year named (plan item 66)
                Assert.Equal(UiText.RatesMissingFor("2025"), dialog.XmlText);
                Assert.DoesNotContain("This version knows", dialog.ReviewText, StringComparison.Ordinal);
                Assert.Empty(fixture.Failures);
                dialog.Close();
            };
            form.SimulationButton.PerformClick();
            Ui.Pump();
            Assert.NotNull(shown);
            Assert.Empty(fixture.Failures);
        }));
    }

    [Fact]
    public void The_simulation_fills_the_A1_cases_fixed_country_as_the_card_does_and_never_while_a_scenario_is_filled()
    {
        using var fixture = new WindowFixture();
        Ui.Run(() => fixture.Drive(form =>
        {
            SimulationDialog? shown = null;
            form.SimulationOpening = dialog =>
            {
                shown = dialog;
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(-4000, -4000);
                dialog.Show();
                Ui.Pump();
                var warnings = new List<string>();
                dialog.Warning = warnings.Add;
                (string, string) Countries() => (dialog.A1CountryBox.Text, dialog.WorkCountryBox.Text);
                void Pick(A1Case a1)
                {
                    dialog.A1Box.SelectedIndex = dialog.A1Box.Items.Cast<Choice>().ToList().FindIndex(c => c.Value == a1.ToString());
                    Ui.Pump();
                }

                // the hint row says the pairing, as on the card
                Assert.Contains(Descendants(dialog), c => c is Label { Text: UiText.A1CountriesHint });

                // (a) the example: both boxes empty; each case fills its own fixed country
                Assert.Equal(("", ""), Countries());
                Pick(A1Case.EstonianA1WorkAbroad);
                Assert.Equal(("EE", ""), Countries());
                Pick(A1Case.ForeignA1WorkInEstonia);
                Assert.Equal(("EE", "EE"), Countries());

                // (b) text typed in is never overwritten
                Pick(A1Case.None);
                dialog.A1CountryBox.Text = "LV";
                dialog.WorkCountryBox.Text = "FI";
                Pick(A1Case.ForeignA1WorkInEstonia);
                Assert.Equal(("LV", "FI"), Countries());
                Pick(A1Case.EstonianA1WorkAbroad);
                Assert.Equal(("LV", "FI"), Countries());

                // (c) a scenario filled in is shown as written: its empty A1 country stays empty
                var example = SimulationDialog.Example((int)dialog.YearBox.Value, (int)dialog.MonthBox.Value);
                var written = example with { Person = example.Person with { A1 = A1Case.EstonianA1WorkAbroad, A1Country = null, WorkCountry = "FI" } };
                Pick(A1Case.None);
                dialog.Fill(written);
                Ui.Pump();
                Assert.Equal(nameof(A1Case.EstonianA1WorkAbroad), ((Choice)dialog.A1Box.SelectedItem!).Value);
                Assert.Equal(("", "FI"), Countries());

                // (c) the same through a file saved and opened
                Directory.CreateDirectory(fixture.ExportFolder);
                var path = Path.Combine(fixture.ExportFolder, "a1.json");
                dialog.ChoosingFile = _ => path;
                dialog.SaveButton.PerformClick();
                Pick(A1Case.None);
                dialog.A1CountryBox.Text = "";
                dialog.WorkCountryBox.Text = "";
                dialog.OpeningFile = () => path;
                dialog.OpenButton.PerformClick();
                Ui.Pump();
                Assert.Empty(warnings);
                Assert.Equal(nameof(A1Case.EstonianA1WorkAbroad), ((Choice)dialog.A1Box.SelectedItem!).Value);
                Assert.Equal(("", "FI"), Countries());

                // (c) and the run is the scenario as written: the missing country is refused, not filled in
                dialog.RunButton.PerformClick();
                Ui.Pump();
                Assert.Empty(warnings);
                Assert.Contains(Core.Tax.PaymentChecks.A1CountriesMissing, dialog.ReviewText, StringComparison.Ordinal);
                Assert.Empty(fixture.Failures);
                dialog.Close();
            };
            form.SimulationButton.PerformClick();
            Ui.Pump();
            Assert.NotNull(shown);
            Assert.Empty(fixture.Failures);
        }));
    }

    [Fact]
    public void The_shared_A1_fill_puts_EE_only_into_the_cases_own_empty_box()
    {
        using var a1Country = new TextBox();
        using var workCountry = new TextBox();
        A1Fill.FixedCountry(A1Case.None, a1Country, workCountry);
        Assert.Equal(("", ""), (a1Country.Text, workCountry.Text));
        A1Fill.FixedCountry(A1Case.EstonianA1WorkAbroad, a1Country, workCountry);
        Assert.Equal(("EE", ""), (a1Country.Text, workCountry.Text));
        a1Country.Text = "LV";
        A1Fill.FixedCountry(A1Case.ForeignA1WorkInEstonia, a1Country, workCountry);
        Assert.Equal(("LV", "EE"), (a1Country.Text, workCountry.Text));
        workCountry.Text = "FI";
        A1Fill.FixedCountry(A1Case.ForeignA1WorkInEstonia, a1Country, workCountry);
        A1Fill.FixedCountry(A1Case.EstonianA1WorkAbroad, a1Country, workCountry);
        Assert.Equal(("LV", "FI"), (a1Country.Text, workCountry.Text));
        Assert.Equal(UiText.A1CountriesHint, A1Fill.Hint().Text);
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var deeper in Descendants(child))
            {
                yield return deeper;
            }
        }
    }

    private static void Mari(MainForm form)
    {
        form.PersonsGrid.CurrentCell = form.PersonsGrid.Rows[0].Cells[0];
        Ui.Pump();
    }

    private static Person StoredMari(MainForm form) => form.Services!.Store.FindPerson(form.View!.Period.CompanyId, SatellesTSD.Tests.ReviewFixtures.MariCode)!;
}
