using SatellesTSD.Core.Store;
using SatellesTSD.Core.Tax;

namespace SatellesTSD.WindowTests;

/// <summary>The pension age and the minimum on the card and in the tools (decisions 208 and 237): the exemption box
/// reads the code's age as the store does; "Vanaduspensionil" ticked by hand takes the minimum off, unticked gives it
/// back; the Calculator's third tick drops the employee's unemployment premium.</summary>
public class PensionMinimumWindowTests
{
    private const string Born1950 = "35001010014"; // a synthetic code: pension age reached in 2013

    private static Person Pensioner(bool minimum = true, bool onPension = false) =>
        Person.New(Guid.NewGuid(), "Vaike", "Vanem", Born1950) with
        {
            BasicExemptionApplied = true, SocialTaxMinimumApplies = minimum, OnOldAgeOrFlexiblePension = onPension,
        };

    [Fact]
    public void The_link_changes_nothing_while_its_owner_fills_the_boxes()
    {
        // the review of the leftovers: the windows fill the pension tick before the minimum, so their own tests cannot see
        // the guard; the link alone shows it
        Ui.Run(() =>
        {
            using var pension = new CheckBox();
            using var minimum = new CheckBox { Checked = true };
            using var tips = new ToolTip();
            var filling = true;
            var link = new PensionMinimumLink(pension, minimum, () => filling, tips);
            pension.Checked = true;
            Assert.True(minimum.Checked);
            Assert.False(link.TakenOff);

            filling = false;
            pension.Checked = false;
            pension.Checked = true;
            Assert.False(minimum.Checked);
            Assert.True(link.TakenOff);
            filling = true;
            pension.Checked = false; // filled: the tick taken off is not given back by the fill
            Assert.False(minimum.Checked);
        });
    }

    [Fact]
    public void A_pension_age_read_from_the_code_keeps_700_typed_as_the_persons_own()
    {
        Ui.Run(() =>
        {
            using var card = new PersonPanel();
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            var rates = TaxRates.Year2026;
            card.Present(Pensioner(), editable: true, 2026, rates);
            Assert.Equal(776m.ToString("0.00", culture), card.ExemptionBox.Text); // no year on the card: the code's age

            card.ExemptionBox.Text = 700m.ToString("0.00", culture); // her own application, below the 776 in force
            Assert.Equal(700m, card.Collect()!.BasicExemptionMonthly);

            card.ExemptionBox.Text = 776m.ToString("0.00", culture); // the figure in force: nothing of her own
            Assert.Null(card.Collect()!.BasicExemptionMonthly);
        });
    }

    [Fact]
    public void Ticking_Vanaduspensionil_by_hand_unticks_the_minimum_and_unticking_it_gives_it_back()
    {
        Ui.Run(() =>
        {
            using var card = new PersonPanel();
            card.Present(Pensioner(), editable: true, 2026, TaxRates.Year2026);
            Assert.Equal(UiText.MinimumPensionTip, card.Tips.GetToolTip(card.MinimumBox));
            Assert.True(card.MinimumBox.Checked);

            card.OnPensionBox.Checked = true;
            Assert.False(card.MinimumBox.Checked); // at once, where it is seen
            Assert.True(card.IsDirty);
            card.OnPensionBox.Checked = false;
            Assert.True(card.MinimumBox.Checked); // the tick took it off: given back

            // a hand touch of the minimum is the accountant's word: it stands through the pension's untick and the save
            card.OnPensionBox.Checked = true;
            card.MinimumBox.Checked = true;
            Assert.Equal((true, true), (card.Collect()!.OnOldAgeOrFlexiblePension, card.Collect()!.SocialTaxMinimumApplies));
            card.OnPensionBox.Checked = false;
            card.MinimumBox.Checked = false;
            card.OnPensionBox.Checked = true;
            card.OnPensionBox.Checked = false;
            Assert.False(card.MinimumBox.Checked); // not taken off by the tick: not given back by it

            // a card stored with both ticks is shown as stored, untouched
            card.Present(Pensioner(onPension: true), editable: true);
            Assert.Equal((true, true), (card.OnPensionBox.Checked, card.MinimumBox.Checked));
            Assert.False(card.IsDirty);
            card.OnPensionBox.Checked = false; // untick on a card whose minimum the tick never took off
            Assert.True(card.MinimumBox.Checked);

            // a card shown afresh forgets what the tick took off on the one before
            card.Present(Pensioner(), editable: true);
            card.OnPensionBox.Checked = true;
            Assert.False(card.MinimumBox.Checked);
            card.Present(Pensioner(minimum: false), editable: true);
            card.OnPensionBox.Checked = true;
            card.OnPensionBox.Checked = false;
            Assert.False(card.MinimumBox.Checked);
        });
    }

    [Fact]
    public void The_calculators_pension_tick_takes_its_minimum_off_and_the_age_reached_drops_the_employees_premium()
    {
        static string Line(CalculatorDialog dialog, string label) =>
            Assert.Single(dialog.OutputText.Split(Environment.NewLine), l => l.StartsWith(label, StringComparison.Ordinal));

        Ui.Run(() =>
        {
            using var dialog = new CalculatorDialog();
            var said = new List<string>();
            dialog.Complaining = said.Add;
            Assert.Equal(UiText.MinimumPensionTip, dialog.Tips.GetToolTip(dialog.MinimumBox));
            dialog.AmountBox.Text = "500";
            dialog.ComputeNow();
            Assert.False(Line(dialog, UiText.MinimumIncrease).EndsWith(UiText.Money(0m), StringComparison.Ordinal)); // net 500: under 886, topped up

            dialog.OnPensionBox.Checked = true;
            Assert.False(dialog.MinimumBox.Checked);
            dialog.ComputeNow();
            Assert.EndsWith(UiText.Money(0m), Line(dialog, UiText.MinimumIncrease), StringComparison.Ordinal);
            dialog.OnPensionBox.Checked = false;
            Assert.True(dialog.MinimumBox.Checked);

            // the age reached before the month: no TK of the employee, the employer's stays; it is an age of the year too
            dialog.ComputeNow();
            Assert.False(Line(dialog, UiText.UnemploymentEmployee).EndsWith(UiText.Money(0m), StringComparison.Ordinal));
            dialog.PensionAgeReachedBox.Checked = true;
            Assert.True(dialog.PensionAgeBox.Checked);
            dialog.ComputeNow();
            Assert.Empty(said);
            Assert.EndsWith(UiText.Money(0m), Line(dialog, UiText.UnemploymentEmployee), StringComparison.Ordinal);
            Assert.False(Line(dialog, UiText.UnemploymentEmployer).EndsWith(UiText.Money(0m), StringComparison.Ordinal)); // the employer's 0.8 % stays

            // no age in the year: none reached either
            dialog.PensionAgeBox.Checked = false;
            Assert.False(dialog.PensionAgeReachedBox.Checked);
        });
    }

    [Fact]
    public void The_simulations_pension_tick_takes_its_minimum_off_and_a_scenario_filled_in_stands_as_given()
    {
        Ui.Run(() =>
        {
            using var dialog = new SimulationDialog(2026, 10);
            Assert.Equal(UiText.MinimumPensionTip, dialog.Tips.GetToolTip(dialog.MinimumBox));
            Assert.True(dialog.MinimumBox.Checked);
            dialog.OnPensionBox.Checked = true;
            Assert.False(dialog.MinimumBox.Checked);
            dialog.OnPensionBox.Checked = false;
            Assert.True(dialog.MinimumBox.Checked);

            var scenario = SimulationDialog.Example(2026, 10);
            dialog.Fill(scenario with { Person = scenario.Person with { OnOldAgeOrFlexiblePension = true, SocialTaxMinimumApplies = true } });
            Assert.Equal((true, true), (dialog.OnPensionBox.Checked, dialog.MinimumBox.Checked));
        });
    }
}
