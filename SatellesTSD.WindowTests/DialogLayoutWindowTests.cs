using SatellesTSD.Core.Import;
using SatellesTSD.Core.Store;

namespace SatellesTSD.WindowTests;

/// <summary>The fifth audit's owner points (plan item 63): the dialogs keep their size, place and splitters between
/// sessions (decision 210), and the statement layouts are one Seaded row whose dialog imports, saves and deletes (211).</summary>
public class DialogLayoutWindowTests
{
    [Fact]
    public void The_rules_dialog_opens_next_time_with_the_size_and_splitter_it_was_closed_with_and_an_off_screen_place_centred()
    {
        using var fixture = new WindowFixture();
        Ui.Run(() =>
        {
            fixture.Drive(form =>
            {
                RulesDialog? shown = null;
                form.RulesOpening = dialog =>
                {
                    shown = dialog;
                    dialog.StartPosition = FormStartPosition.Manual;
                    dialog.Location = new Point(-4000, -4000); // no screen shows it
                    dialog.Show();
                    Ui.Pump();
                    dialog.Size = new Size(820, 560);
                    Ui.Pump();
                    DialogLayout.Split(dialog, "below").SplitterDistance = 300;
                    dialog.Close();
                    Ui.Pump();
                };
                form.RulesButton.PerformClick();
                Ui.Pump();
                Assert.NotNull(shown);
            });

            var place = AppSettings.Load(fixture.SettingsPath).Dialogs!["rules"];
            Assert.Equal((null, null, 820, 560, false), (place.X, place.Y, place.Width, place.Height, place.Maximized));
            Assert.Equal(300, place.Splitters!["below"]);
            Assert.True(place.Splitters.ContainsKey("matrix"));

            fixture.Drive(form =>
            {
                RulesDialog? shown = null;
                form.RulesOpening = dialog =>
                {
                    shown = dialog;
                    Assert.Equal(new Size(820, 560), dialog.Size);
                    Assert.Equal(FormStartPosition.CenterParent, dialog.StartPosition); // the place no screen showed: centred
                    dialog.StartPosition = FormStartPosition.Manual;
                    dialog.Location = new Point(-4000, -4000);
                    dialog.Show();
                    Ui.Pump();
                    Assert.Equal(300, DialogLayout.Split(dialog, "below").SplitterDistance);
                    dialog.Close();
                };
                form.RulesButton.PerformClick();
                Ui.Pump();
                Assert.NotNull(shown);
            });
        });
    }

    [Fact]
    public void A_dialogs_place_on_a_screen_is_kept_a_minimized_one_keeps_its_earlier_state_and_its_least_size_holds()
    {
        Rectangle[] screens = [new(0, 0, 1920, 1040)];
        Assert.Equal(((Point?)new Point(10, 10), new Size(480, 300)), WindowLayout.Fit(10, 10, 300, 200, screens, least: new Size(480, 300)));

        using var dialog = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(100, 80), Size = new Size(700, 500), ShowInTaskbar = false };
        Ui.Run(() =>
        {
            dialog.Show();
            Ui.Pump();
            var on = DialogLayout.Taken(dialog, null, screens, 0, new Size(480, 300));
            Assert.Equal((100, 80, 700, 500, false), (on.X, on.Y, on.Width, on.Height, on.Maximized));
            Assert.Empty(on.Splitters!);

            var before = new DialogPlace { Maximized = true, Splitters = new() { ["main"] = 222 } };
            dialog.WindowState = FormWindowState.Minimized;
            Ui.Pump();
            var minimized = DialogLayout.Taken(dialog, before, screens, 0, new Size(480, 300));
            Assert.Equal((100, 80, 700, 500, true, 222), (minimized.X, minimized.Y, minimized.Width, minimized.Height, minimized.Maximized, minimized.Splitters!["main"]));
            dialog.Close();
        });
    }

    [Fact]
    public void The_layouts_are_one_Seaded_row_whose_dialog_imports_saves_and_deletes_and_says_a_refusal_in_Estonian()
    {
        using var fixture = new WindowFixture();
        Ui.Run(() => fixture.Drive(form =>
        {
            var settings = form.LayoutsItem.OwnerItem!;
            var rows = ((ToolStripMenuItem)settings).DropDownItems.Cast<ToolStripItem>().Select(i => i.Text).ToList();
            Assert.Single(rows, t => t is not null && t.Contains("paigutus", StringComparison.OrdinalIgnoreCase)); // one row, not an import beside it
            Assert.True(form.LayoutsItem.Enabled);

            LayoutsDialog? shown = null;
            form.LayoutsOpening = dialog =>
            {
                shown = dialog;
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(-4000, -4000);
                dialog.Show();
                Ui.Pump();
                var warnings = new List<string>();
                dialog.Warning = warnings.Add;
                dialog.Confirming = (_, _) => true;
                Assert.True(dialog.NoOwnLayoutsShown);
                Assert.Equal(UiText.LayoutsImport, dialog.ImportButton.Text);

                // saved: the layouts in force, the shape the import takes
                Directory.CreateDirectory(fixture.ExportFolder);
                var path = Path.Combine(fixture.ExportFolder, "paigutused.json");
                string? offered = null;
                dialog.ChoosingFile = name => { offered = name; return path; };
                dialog.SaveButton.PerformClick();
                Assert.Equal("paigutused.json", offered);
                Assert.Equal(form.Services!.Pipeline.Catalog.Layouts.Count, LayoutCatalog.ParseLayouts(File.ReadAllText(path)).Count());

                // imported: a layout of the owner's own, listed at once
                var lhv = LayoutCatalog.Default.Find("lhv-et-short")!;
                var own = lhv with { Id = "oma-lhv", Headers = lhv.Headers.Select(h => h == lhv.Roles.Amount ? "Summa EUR" : h).ToList(), Roles = lhv.Roles with { Amount = "Summa EUR" } };
                var file = Path.Combine(fixture.ExportFolder, "oma.json");
                File.WriteAllText(file, LayoutCatalog.ToJson([own]));
                dialog.OpeningFile = () => file;
                dialog.ImportButton.PerformClick();
                Assert.Empty(warnings);
                var item = Assert.Single(dialog.List.Items.Cast<ListViewItem>(), i => i.SubItems[0].Text == "oma-lhv");
                Assert.Equal(UiText.LayoutNew, item.SubItems[4].Text);
                Assert.False(dialog.NoOwnLayoutsShown);

                // refused: said in Estonian, nothing saved, the list as it was
                File.WriteAllText(file, "{ not json");
                dialog.ImportButton.PerformClick();
                Assert.StartsWith("Fail ei ole loetav paigutuste JSON", Assert.Single(warnings), StringComparison.Ordinal);
                Assert.Contains(" Midagi ei salvestatud.", warnings[0], StringComparison.Ordinal);
                File.WriteAllText(file, LayoutCatalog.ToJson([own, own]));
                dialog.ImportButton.PerformClick();
                Assert.StartsWith("Failis on sama ID mitu korda", warnings[^1], StringComparison.Ordinal);
                Assert.Single(dialog.List.Items.Cast<ListViewItem>(), i => i.SubItems[0].Text == "oma-lhv");

                // a cancelled pick does nothing
                dialog.OpeningFile = () => null;
                dialog.ImportButton.PerformClick();
                Assert.Equal(2, warnings.Count);

                // taken back
                dialog.List.Items.Cast<ListViewItem>().Single(i => i.SubItems[0].Text == "oma-lhv").Selected = true;
                Assert.True(dialog.DeleteButton.Enabled);
                dialog.DeleteButton.PerformClick();
                Assert.DoesNotContain(dialog.List.Items.Cast<ListViewItem>(), i => i.SubItems[0].Text == "oma-lhv");
                Assert.True(dialog.NoOwnLayoutsShown);
                dialog.Close();
            };
            form.LayoutsItem.PerformClick();
            Ui.Pump();
            Assert.NotNull(shown);
            Assert.Empty(form.Services!.Pipeline.OwnerLayouts());
            Assert.Empty(fixture.Failures);
        }));
    }
}
