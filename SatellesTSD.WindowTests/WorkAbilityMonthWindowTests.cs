using SatellesTSD.Core.Model;
using SatellesTSD.Core.Store;
using SatellesTSD.Core.Tax;

namespace SatellesTSD.WindowTests;

/// <summary>Töötukassa's part by the month the pay is for (decision 267), as the windows keep it: the day of the
/// application on the person's card, the pay month on the company's, and the month's amount typed by hand from the Isik
/// menu.</summary>
public class WorkAbilityMonthWindowTests
{
    private static Person Worker(bool status = false, bool application = false, DateOnly? applied = null) =>
        Person.New(Guid.NewGuid(), "Tiiu", "Tamm", "49001010012") with
        {
            ResidencyCheckedOn = new DateOnly(2026, 10, 31), ResidencySource = Core.Lookups.LookupSource.Manual, FundedPensionMember = false,
            BasicExemptionApplied = true, ReducedWorkAbility = status, StatePaysSocialTax = application, WorkAbilityApplicationDate = applied,
        };

    [Fact]
    public void The_cards_application_day_opens_with_the_application_and_is_kept_only_with_it()
    {
        Ui.Run(() =>
        {
            using var card = new PersonPanel();
            var said = new List<string>();
            card.Complaining = said.Add;
            card.Present(Worker(), editable: true, 2026, TaxRates.Year2026);
            Assert.Equal(("", false), (card.ApplicationDateBox.Text, card.ApplicationDateBox.Enabled));
            Assert.Equal(UiText.WorkAbilityApplicationDateTip, card.Tips.GetToolTip(card.ApplicationDateBox));

            card.WorkAbilityBox.Checked = true;
            Assert.False(card.ApplicationDateBox.Enabled); // the status alone: no application, no day
            card.StatePaysBox.Checked = true;
            Assert.True(card.ApplicationDateBox.Enabled);
            Assert.Null(card.Collect()!.WorkAbilityApplicationDate); // left empty: the application stood before

            card.ApplicationDateBox.Text = "10.1.2026";
            Assert.True(card.IsDirty);
            Assert.Equal(new DateOnly(2026, 1, 10), card.Collect()!.WorkAbilityApplicationDate);
            card.ApplicationDateBox.Text = "kümnes";
            Assert.Null(card.Collect());
            card.ApplicationDateBox.Text = "10.01.0026"; // a slip of the year would stand before every month
            Assert.Null(card.Collect());
            Assert.Equal([UiText.WorkAbilityDateInvalid, UiText.WorkAbilityDateInvalid], said);

            // the application's tick taken off: the day typed is not kept, whatever stands in the box
            card.ApplicationDateBox.Text = "10.01.2026";
            card.StatePaysBox.Checked = false;
            Assert.False(card.ApplicationDateBox.Enabled);
            Assert.Null(card.Collect()!.WorkAbilityApplicationDate);

            // a stored card shows its day; read-only, the box is shut
            card.Present(Worker(status: true, application: true, applied: new DateOnly(2026, 9, 10)), editable: true, 2026, TaxRates.Year2026);
            Assert.Equal(("10.09.2026", true, false), (card.ApplicationDateBox.Text, card.ApplicationDateBox.Enabled, card.IsDirty));
            Assert.Equal(new DateOnly(2026, 9, 10), card.Collect()!.WorkAbilityApplicationDate);
            card.Present(Worker(status: true, application: true, applied: new DateOnly(2026, 9, 10)), editable: false);
            Assert.False(card.ApplicationDateBox.Enabled);
        });
    }

    [Fact]
    public void The_companys_card_says_whether_wages_are_paid_the_following_month()
    {
        Ui.Run(() =>
        {
            using var fresh = new CompanyDialog();
            Assert.True(fresh.WagesPaidFollowingMonth); // the usual way
            Assert.Equal(UiText.WagesPaidFollowingMonth, fresh.WagesFollowingBox.Text);

            using var within = new CompanyDialog(new Company(Guid.NewGuid(), "10235688", "Näidis OÜ", WagesPaidFollowingMonth: false));
            Assert.False(within.WagesPaidFollowingMonth);
            within.WagesFollowingBox.Checked = true;
            Assert.True(within.WagesPaidFollowingMonth);
        });
    }

    [Fact]
    public void The_amounts_dialog_takes_nothing_or_an_amount_up_to_two_rates()
    {
        Ui.Run(() =>
        {
            var reckoned = WorkAbilityMonth.Reckon(new DateOnly(2026, 9, 1), 886m, new DateOnly(2026, 9, 10));
            using var dialog = new WorkAbilityAmountDialog("Tamm Tiiu", reckoned, null, 1772m);
            var said = new List<string>();
            dialog.Complaining = said.Add;
            Assert.Equal("", dialog.AmountBox.Text);
            Assert.True(dialog.AcceptNow());
            Assert.Null(dialog.Amount);

            dialog.AmountBox.Text = "1371.87";
            Assert.True(dialog.AcceptNow());
            Assert.Equal(1371.87m, dialog.Amount);
            dialog.AmountBox.Text = "0";
            Assert.True(dialog.AcceptNow());
            Assert.Equal(0m, dialog.Amount); // nothing deducted this month is an amount too

            foreach (var wrong in new[] { "palju", "-1", "1772.01", "10.005" })
            {
                dialog.AmountBox.Text = wrong;
                Assert.False(dialog.AcceptNow());
            }

            Assert.Equal(4, said.Count);
            Assert.All(said, s => Assert.Equal(UiText.WorkAbilityAmountInvalid(1772m), s));
            Assert.Contains("620,20", UiText.WorkAbilityReckoned(reckoned), StringComparison.Ordinal);
            Assert.Contains("21/30", UiText.WorkAbilityReckoned(reckoned), StringComparison.Ordinal);

            using var again = new WorkAbilityAmountDialog("Tamm Tiiu", reckoned, 1000m, 1772m);
            Assert.True(again.AcceptNow());
            Assert.Equal(1000m, again.Amount); // the amount typed earlier stands in the box
        });
    }

    [Fact]
    public void The_months_amount_is_typed_from_the_Isik_menu_for_a_person_with_both_ticks_and_the_log_says_how_the_month_came()
    {
        using var fixture = new WindowFixture();
        Ui.Run(() => fixture.Drive(form =>
        {
            var informed = new List<string>();
            form.Informing = informed.Add;
            var asked = new List<(WorkAbilityMonth Reckoned, decimal? Typed)>();
            decimal? answer = 1371.87m;
            form.TypingWorkAbilityAmount = (reckoned, typed) =>
            {
                asked.Add((reckoned, typed));
                return (true, answer);
            };
            form.PersonsGrid.CurrentCell = form.PersonsGrid.Rows[0].Cells[0];
            Ui.Pump();
            var period = form.View!.Period;
            var store = form.Services!.Store;
            Assert.True(form.WorkAbilityAmountButton.Enabled);

            // without both ticks on the stored card there is nothing to type: said, nothing asked
            form.WorkAbilityAmountButton.PerformClick();
            Assert.Equal([UiText.WorkAbilityAmountNoPerson], informed);
            Assert.Empty(asked);

            var card = form.PersonEditor;
            card.WorkAbilityBox.Checked = true;
            card.StatePaysBox.Checked = true;
            card.ApplicationDateBox.Text = "10.09.2026";
            card.SaveButton.PerformClick();
            Ui.Pump();
            var mari = store.FindPerson(period.CompanyId, SatellesTSD.Tests.ReviewFixtures.MariCode)!;
            Assert.Equal(new DateOnly(2026, 9, 10), mari.WorkAbilityApplicationDate);

            // the reckoned part is shown: September's pay (wages paid the following month), 21 days of 30
            form.WorkAbilityAmountButton.PerformClick();
            var (reckoned, typed) = Assert.Single(asked);
            Assert.Equal((new DateOnly(2026, 9, 1), 21, 620.20m, (decimal?)null), (reckoned.PayMonth, reckoned.Days, reckoned.Amount, typed));
            Assert.Equal(1371.87m, store.WorkAbilityAmounts(period.Id)[mari.Id]);
            Assert.Contains(form.LogList.Items.Cast<object>(), i => i.ToString()!.Contains(UiText.WorkAbilityAmountSaved($"{mari.LastName} {mari.FirstName}", 1371.87m), StringComparison.Ordinal));

            // built: the line takes its whole base, and the log says the amount was typed by hand
            form.Tabs.SelectedIndex = 1;
            Ui.Pump();
            var panel = form.Declaration;
            panel.BuildButton.PerformClick();
            Ui.Until(() => !form.IsBusy && panel.LinesGrid.Rows.Count == 1, what: "the build");
            var result = ((Core.Declare.DeclaredLine)panel.LinesGrid.Rows[0].Tag!).Payment.Result!;
            Assert.Equal((result.SocialTaxBase, 0m), (result.WorkAbilityReduction, result.SocialTax));
            Assert.Contains(form.LogList.Items.Cast<object>(), i => i.ToString()!.Contains("käsitsi sisestatud", StringComparison.Ordinal));

            // the amount typed earlier is shown; an empty answer takes it away, and the part is reckoned again
            answer = null;
            form.WorkAbilityAmountButton.PerformClick();
            Assert.Equal(1371.87m, asked[^1].Typed);
            Assert.Empty(store.WorkAbilityAmounts(period.Id));
            panel.BuildButton.PerformClick();
            Ui.Until(() => !form.IsBusy && form.LogList.Items.Cast<object>().Any(i => i.ToString()!.Contains("21/30 päeva", StringComparison.Ordinal)), what: "the build's log of the part by days");
            Assert.Equal(620.20m, ((Core.Declare.DeclaredLine)panel.LinesGrid.Rows[0].Tag!).Payment.Result!.WorkAbilityReduction);
        }));
    }
}
