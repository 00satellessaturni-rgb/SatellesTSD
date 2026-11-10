using SatellesTSD.Core.Review;

namespace SatellesTSD.WindowTests;

/// <summary>The payments grid's "Kategooriate konflikt" tab: the rows the run found more than one category in, and
/// nothing else; a row stays there once the accountant has picked its category.</summary>
public class ConflictTabTests
{
    [Fact]
    public void The_conflict_tab_lists_the_rows_found_in_several_categories_settled_or_not()
    {
        using var fixture = new WindowFixture(conflicting: true);
        Ui.Run(() => fixture.Drive(form =>
        {
            var tabs = form.ScopeTabs;
            var pages = tabs.TabPages.Cast<TabPage>().ToList();
            Assert.Equal("Kategooriate konflikt", pages[^1].Text); // the last tab, after the payments without a category
            tabs.SelectedIndex = pages.Count - 1;
            Ui.Pump();
            var row = Assert.Single(form.PaymentsGrid.Rows.Cast<DataGridViewRow>());
            Assert.Equal("Kalle Kask", row.Cells["payee"].Value?.ToString());
            Assert.Equal(["Üür", "Kommunaalkulud", "TÖR-väline palgad"], ((ReviewPayment)row.Tag!).ConflictingCategories);

            // the category picked in the grid: the row has one now, and the tab still lists it
            var periodId = form.View!.Period.Id;
            form.PaymentsGrid.CurrentCell = row.Cells["category"];
            Assert.True(form.PaymentsGrid.BeginEdit(true));
            var combo = Assert.IsType<DataGridViewComboBoxEditingControl>(form.PaymentsGrid.EditingControl);
            combo.SelectedValue = "Üür";
            Ui.Until(() => !form.IsBusy && form.View is { } view && view.Period.Id == periodId
                && view.Persons.SelectMany(p => p.Payments).Single(p => p.ConflictingCategories.Count > 0).Category == "Üür", what: "the reload after the pick");
            Assert.Equal("Kategooriate konflikt", tabs.SelectedTab?.Text);
            var kept = Assert.Single(form.PaymentsGrid.Rows.Cast<DataGridViewRow>());
            Assert.Equal("Üür", kept.Cells["category"].Value?.ToString());

            // every other tab's rows: none of the fixture's own rows is a conflict
            tabs.SelectedIndex = 1;
            Ui.Pump();
            Assert.Equal(1, form.PaymentsGrid.Rows.Cast<DataGridViewRow>().Count(r => ((ReviewPayment)r.Tag!).ConflictingCategories.Count > 0));
        }));
    }
}
