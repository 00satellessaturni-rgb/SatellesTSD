using SatellesTSD.Core.Declare;
using SatellesTSD.Core.Lookups;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Review;
using SatellesTSD.Core.Store;

namespace SatellesTSD.WindowTests;

/// <summary>The window on a store built like the probe's: the review tab's grids and an edit through them, the
/// declaration tab's build, upload and resolve, and the key's history dialog. Nothing here opens a modal dialog:
/// the window's questions are answered by the test, its failures thrown.</summary>
public class WindowTests
{
    [Fact]
    public void The_review_tab_shows_the_persons_and_their_payments_and_stores_an_edit()
    {
        using var fixture = new WindowFixture();
        Ui.Run(() => fixture.Drive(form =>
        {
            Assert.StartsWith("SatellesTSD v", form.Text, StringComparison.Ordinal);
            // the bar between the two grids is seen and grabbed: darker than the panels, wide enough to hit
            var split = form.GridsSplit;
            Assert.NotEqual(split.Panel1.BackColor, split.BackColor);
            Assert.True(split.SplitterWidth >= 6);
            var persons = form.PersonsGrid;
            Assert.Equal(3, persons.Rows.Count); // the two register persons and the rows nobody matched
            Assert.Equal("MAASIKAS", persons.Rows[0].Cells["last"].Value?.ToString());
            Assert.True(persons.Rows[2].Tag is ReviewPerson { IsUnmatched: true });

            persons.CurrentCell = persons.Rows[0].Cells[0];
            Ui.Pump();
            var payments = form.PaymentsGrid;
            Assert.Single(payments.Rows);
            Assert.Equal("LHV pank *1757 [2]", payments.Rows[0].Cells["document"].Value?.ToString());
            Assert.Equal("Palgad", payments.Rows[0].Cells["category"].Value?.ToString());
            Assert.Contains("905", persons.Rows[0].Cells["src:LHV pank *1757"].Value?.ToString(), StringComparison.Ordinal); // the net salary from the statement

            // the category picked in the grid's combo, as the accountant picks it: the pick is a decision at once
            // (CurrentCellDirtyStateChanged commits it), the overlay is stored, the view reloads and the grids follow
            var periodId = form.View!.Period.Id;
            payments.CurrentCell = payments.Rows[0].Cells["category"];
            Assert.True(payments.BeginEdit(true));
            var combo = Assert.IsType<DataGridViewComboBoxEditingControl>(payments.EditingControl);
            combo.SelectedValue = "Üür";
            Ui.Until(() => !form.IsBusy && form.View is { } view && view.Period.Id == periodId && view.Persons[0].Payments.Single().Category == "Üür", what: "the reload after the pick");
            var stored = form.Services!.Workspace.Load(periodId).Persons[0].Payments.Single();
            Assert.Equal(("Üür", "Palgad", "57"), (stored.Category, stored.DefaultCategory, stored.TypeCode));
            Assert.Equal("Üür", form.PaymentsGrid.Rows[0].Cells["category"].Value?.ToString());
            Assert.DoesNotContain("905", form.PersonsGrid.Rows[0].Cells["src:LHV pank *1757"].Value?.ToString() ?? "", StringComparison.Ordinal); // rent is no salary: the Vahearuanne's cell follows the accountant's word

            // the person card's "Deklareerimine": by the rules at first; "välja" saved is kept in the store and shown again
            form.PersonsGrid.CurrentCell = form.PersonsGrid.Rows[0].Cells[0];
            Ui.Pump();
            var card = form.PersonEditor;
            Assert.Equal(["reeglite järgi", "kaasa (nagu kehtiv töötamine)", "välja (ei deklareerita)"], card.DeclarationBox.Items.Cast<object>().Select(i => i.ToString()));
            Assert.Equal(0, card.DeclarationBox.SelectedIndex);
            card.DeclarationBox.SelectedIndex = 2;
            Assert.True(card.IsDirty);
            card.SaveButton.PerformClick();
            Ui.Until(() => !form.IsBusy, what: "the reload after the save");
            Assert.Equal(DeclarationChoice.Exclude, form.Services!.Store.FindPerson(form.View!.Period.CompanyId, SatellesTSD.Tests.ReviewFixtures.MariCode)!.Declaration);
            Assert.Equal(2, form.PersonEditor.DeclarationBox.SelectedIndex);
            Assert.Equal(["välja", "", ""], form.PersonsGrid.Rows.Cast<DataGridViewRow>().Select(r => r.Cells["declaration"].Value?.ToString() ?? "")); // the exception stands out in the grid; by the rules, nothing
        }));
    }

    [Fact]
    public void The_grids_sort_by_a_header_filter_by_the_search_box_and_the_payments_follow_the_tab_picked()
    {
        using var fixture = new WindowFixture((store, _, period, _) =>
        {
            var workspace = new ReviewWorkspace(store);
            var view = workspace.Load(period.Id);
            workspace.AddHandLine(period.Id, view.Persons[0].PersonId, new DateOnly(2026, 10, 20), 120m, "Mari Maasikas", "haigusleht", "Haigusleht");
            workspace.SetCategory(view.Unmatched.Payments.Single(p => p.Payee == "Kinnisvara OÜ"), "Üür");
            workspace.SavePerson(view.Persons[1].Person! with { BasicExemptionApplied = false }); // the second person asks for no exemption here
        });
        Ui.Run(() => fixture.Drive(form =>
        {
            // the commands sit in the menus, grouped; the toolbar keeps what is chosen
            Assert.Equal(["Andmebaas", "Ettevõte", "Periood", "Isik", "Tööriistad", "Vaade", "Seaded"], form.Menus.Items.Cast<ToolStripMenuItem>().Select(m => m.Text.Replace("&", "")));
            Assert.Contains(form.ImportButton, ((ToolStripMenuItem)form.Menus.Items[2]).DropDownItems.Cast<ToolStripItem>());

            // Maksuvaba: the amount when applied, "ei" when not, nothing on the unmatched row
            var persons = form.PersonsGrid;
            Assert.Equal(["700,00", "ei", ""], persons.Rows.Cast<DataGridViewRow>().Select(r => r.Cells["exemption"].FormattedValue?.ToString()?.Replace('.', ',')));

            // Töötamise lõpu aeg: the day an ended employment ended (the second person's VÕS ended in September), nothing
            // for an employment still running or for the unmatched row; a date column, so it sorts as dates
            Assert.Equal(["", "30.09.2026", ""], persons.Rows.Cast<DataGridViewRow>().Select(r => r.Cells["end"].FormattedValue?.ToString() ?? ""));
            Assert.Equal(typeof(DateTime), persons.Columns["end"]!.ValueType);

            // the persons sort by a header: the last names descending put the register's second person first
            persons.Sort(persons.Columns["last"]!, System.ComponentModel.ListSortDirection.Descending);
            Ui.Pump();
            Assert.Equal("TAMM", persons.Rows[0].Cells["last"].Value?.ToString());
            Assert.Equal(typeof(decimal), persons.Columns["total"]!.ValueType); // money sorts by value, not by text

            // the search box hides what does not match, and the hidden rows come back when it is cleared
            // the total line under the grid: the column's total, and the rows shown when the search hides some
            Assert.Equal("PALK NETO KOKKU: 905,92", form.SalaryTotalLabel.Text.Replace('.', ','));
            form.PersonsSearch.Text = "tamm";
            Ui.Pump();
            Assert.Equal("PALK NETO KOKKU: 905,92 (näidatud read: 0,00)", form.SalaryTotalLabel.Text.Replace('.', ','));
            form.PersonsSearch.Text = "maasi";
            Ui.Pump();
            Assert.Equal(1, persons.Rows.Cast<DataGridViewRow>().Count(r => r.Visible));
            Assert.Equal("MAASIKAS", persons.CurrentRow?.Cells["last"].Value?.ToString()); // the current row follows the search, and stays once it is cleared
            form.PersonsSearch.Text = "";
            Ui.Pump();
            Assert.Equal(3, persons.Rows.Cast<DataGridViewRow>().Count(r => r.Visible));
            Assert.Equal("PALK NETO KOKKU: 905,92", form.SalaryTotalLabel.Text.Replace('.', ','));

            // the payments tabs: the person picked, every payment, each category, the payments without one
            var tabs = form.ScopeTabs;
            Assert.Equal(["Valitud isik", "Kõik", "Palgad", "Dividendid", "Üür", "Kommunaalkulud", "Kompensatsioon", "Haigusleht", "Laenud", "TÖR-väline palgad", "Trahvid", "Tundmatu kategooria", "Kategooriate konflikt"], tabs.TabPages.Cast<TabPage>().Select(p => p.Text));
            tabs.SelectedIndex = 1;
            Ui.Pump();
            Assert.Equal(5, form.PaymentsGrid.Rows.Count); // the salary, the hand line, the second person's row, the fee without a payee, the stranger's
            // the amounts sort by value (typed cells), the dates likewise
            form.PaymentsGrid.Sort(form.PaymentsGrid.Columns["amount"]!, System.ComponentModel.ListSortDirection.Ascending);
            Ui.Pump();
            var amounts = form.PaymentsGrid.Rows.Cast<DataGridViewRow>().Select(r => Assert.IsType<decimal>(r.Cells["amount"].Value)).ToList();
            Assert.Equal(amounts.OrderBy(a => a), amounts);
            Assert.Equal(0.38m, amounts[0]); // the fee without a payee
            // the direction and the currency beside the amount: the statement's D (money out) and EUR; a hand line has neither
            Assert.Equal(
                form.PaymentsGrid.Rows.Cast<DataGridViewRow>().Select(r => ((ReviewPayment)r.Tag!).IsByHand ? ("", "") : ("D", "EUR")),
                form.PaymentsGrid.Rows.Cast<DataGridViewRow>().Select(r => (r.Cells["dc"].Value?.ToString() ?? "", r.Cells["currency"].Value?.ToString() ?? "")));
            Assert.Equal(["D/C", "Valuuta"], new[] { form.PaymentsGrid.Columns["dc"]!.HeaderText, form.PaymentsGrid.Columns["currency"]!.HeaderText });
            Assert.IsType<DateTime>(form.PaymentsGrid.Rows[0].Cells["date"].Value);
            Assert.Equal(SortOrder.Ascending, form.PaymentsGrid.SortOrder);
            form.PaymentsSearch.Text = "kinnisvara";
            Ui.Pump();
            Assert.Equal("Kinnisvara OÜ", form.PaymentsGrid.CurrentRow?.Cells["payee"].Value?.ToString()); // the search moves the current row to one shown
            form.PaymentsSearch.Text = "";
            Ui.Pump();
            tabs.SelectedIndex = tabs.TabPages.Cast<TabPage>().ToList().FindIndex(p => p.Text == "Üür");
            Ui.Pump();
            Assert.Equal("Kinnisvara OÜ", Assert.Single(form.PaymentsGrid.Rows.Cast<DataGridViewRow>()).Cells["payee"].Value?.ToString());
            tabs.SelectedIndex = tabs.TabPages.Cast<TabPage>().ToList().FindIndex(p => p.Text == "Tundmatu kategooria");
            Ui.Pump();
            Assert.Equal(["", "Jaan Tamm"], form.PaymentsGrid.Rows.Cast<DataGridViewRow>().Select(r => r.Cells["payee"].Value?.ToString())); // the fee without a payee (sorted by amount still), the row outside the month
            form.PaymentsSearch.Text = "nobody-here";
            Ui.Pump();
            Assert.Equal(0, form.PaymentsGrid.Rows.Cast<DataGridViewRow>().Count(r => r.Visible));
            form.PaymentsSearch.Text = "";
            tabs.SelectedIndex = 0;
            Ui.Pump();
            Assert.Equal(2, form.PaymentsGrid.Rows.Count); // the person picked: the salary and the hand line
            Assert.Equal(typeof(DateTime), form.PaymentsGrid.Columns["date"]!.ValueType);

            // the company closed: nothing shown, the import still open for the files' employer; chosen again, the view is back
            Assert.True(form.CloseCompanyButton.Enabled);
            form.CloseCompanyButton.PerformClick();
            Ui.Pump();
            Assert.Null(form.View);
            Assert.Equal(-1, form.CompanyBox.SelectedIndex);
            Assert.Equal("", form.SalaryTotalLabel.Text);
            Assert.False(form.CloseCompanyButton.Enabled);
            Assert.True(form.ImportButton.Enabled);
            Assert.Equal(0, form.PersonsGrid.Rows.Count);
            Assert.Equal(0, form.PaymentsGrid.Rows.Count);
            Assert.Equal(["Valitud isik", "Kõik"], form.ScopeTabs.TabPages.Cast<TabPage>().Select(p => p.Text));
            form.CompanyBox.SelectedIndex = 0;
            Ui.Pump();
            Assert.NotNull(form.View);
            Assert.Equal(3, form.PersonsGrid.Rows.Count);
        }));
    }

    [Fact]
    public void A_paused_employment_shows_since_when_and_the_tooltip_says_why_and_until_when()
    {
        using var fixture = new WindowFixture(mariPaused: true);
        Ui.Run(() => fixture.Drive(form =>
        {
            var persons = form.PersonsGrid;
            Assert.Equal(persons.Columns["end"]!.Index + 1, persons.Columns["pause"]!.Index); // right after Töötamise lõpu aeg
            Assert.Equal("Peatamise algus", persons.Columns["pause"]!.HeaderText);
            Assert.Equal(typeof(DateTime), persons.Columns["pause"]!.ValueType);
            Assert.Equal(["Peatatud", "Lõpetatud", ""], persons.Rows.Cast<DataGridViewRow>().Select(r => r.Cells["state"].Value?.ToString() ?? ""));
            Assert.Equal(["03.08.2026", "", ""], persons.Rows.Cast<DataGridViewRow>().Select(r => r.Cells["pause"].FormattedValue?.ToString() ?? ""));
            Assert.Equal("Peatamise alus: Lapsehoolduspuhkus" + Environment.NewLine + "Peatamise lõpu aeg: 30.06.2027", persons.Rows[0].Cells["pause"].ToolTipText);
            Assert.DoesNotContain("Peatamise", persons.Rows[1].Cells["pause"].ToolTipText, StringComparison.Ordinal);
        }));
    }

    [Fact]
    public void The_cards_employment_type_for_every_payment_is_saved_and_the_payments_and_the_grid_follow()
    {
        using var fixture = new WindowFixture();
        Ui.Run(() => fixture.Drive(form =>
        {
            form.PersonsGrid.CurrentCell = form.PersonsGrid.Rows[0].Cells[0];
            Ui.Pump();
            var card = form.PersonEditor;
            Assert.False(card.TypeFromCardBox.Checked);
            Assert.Equal("Tööleping", form.PersonsGrid.Rows[0].Cells["type"].Value?.ToString());
            card.TorTypeBox.SelectedIndex = card.TorTypeBox.Items.Cast<object>().Select(i => i.ToString()).ToList().IndexOf("600 VÕS leping");
            card.TypeFromCardBox.Checked = true;
            Assert.True(card.IsDirty);
            card.SaveButton.PerformClick();
            Ui.Until(() => !form.IsBusy, what: "the reload after the save");

            var stored = form.Services!.Store.FindPerson(form.View!.Period.CompanyId, SatellesTSD.Tests.ReviewFixtures.MariCode)!;
            Assert.Equal(("600", true), (stored.TorEmploymentTypeId, stored.TypeFromCard));
            Assert.True(form.PersonEditor.TypeFromCardBox.Checked);
            Assert.Equal("VÕS leping", form.PersonsGrid.Rows[0].Cells["type"].Value?.ToString()); // the grid shows the type that applies
            Assert.Equal("17", form.View!.Persons[0].Payments.Single().DefaultTypeCode); // the salary by the card's VÕS: 17, not the Tööleping's 10
        }));
    }

    [Fact]
    public void The_Vaade_menu_hides_the_persons_grids_columns_per_source_leaving_the_salary_total_and_keeps_it()
    {
        using var fixture = new WindowFixture();
        Ui.Run(() =>
        {
            fixture.Drive(form =>
            {
                var grid = form.PersonsGrid;
                var sources = grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Name.StartsWith("src:", StringComparison.Ordinal)).ToList();
                Assert.NotEmpty(sources);
                Assert.All(sources, c => Assert.True(c.Visible));
                Assert.False(form.SalaryOnlyItem.Checked);

                grid.CurrentCell = grid.Rows[0].Cells[sources[0].Index]; // the current cell in a column about to go
                form.SalaryOnlyItem.PerformClick();
                Ui.Pump();
                Assert.True(form.SalaryOnlyItem.Checked);
                Assert.True(AppSettings.Load(fixture.SettingsPath).PersonsSalaryOnly);
                Assert.All(sources, c => Assert.False(c.Visible));
                Assert.True(grid.Columns["total"]!.Visible);
                Assert.True(grid.Columns["undeclared"]!.Visible);
                Assert.Equal((0, "total"), (grid.CurrentCell?.RowIndex, grid.CurrentCell?.OwningColumn.Name)); // still on the person, on PALK NETO KOKKU
            });

            // the next window: the columns built hidden; and shown again
            fixture.Drive(form =>
            {
                var grid = form.PersonsGrid;
                Assert.True(form.SalaryOnlyItem.Checked);
                Assert.All(grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Name.StartsWith("src:", StringComparison.Ordinal)), c => Assert.False(c.Visible));
                form.SalaryOnlyItem.PerformClick();
                Ui.Pump();
                Assert.False(AppSettings.Load(fixture.SettingsPath).PersonsSalaryOnly);
                Assert.All(grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Name.StartsWith("src:", StringComparison.Ordinal)), c => Assert.True(c.Visible));
            });
        });
    }

    [Fact]
    public void The_Vaade_menu_sets_the_log_to_one_line_or_its_default_height_and_keeps_it_in_the_settings()
    {
        using var fixture = new WindowFixture();
        Ui.Run(() =>
        {
            fixture.Drive(form =>
            {
                Assert.True(form.LogDefaultItem.Checked);
                Assert.False(form.LogOneRowItem.Checked);
                var tall = form.LogPanel.Height;

                // one line: the panel shrinks to a line (and room for a scrollbar), the grids above take the rest
                form.LogOneRowItem.PerformClick();
                Ui.Pump();
                Assert.True(AppSettings.Load(fixture.SettingsPath).LogOneRow);
                Assert.True(form.LogOneRowItem.Checked);
                Assert.False(form.LogDefaultItem.Checked);
                Assert.InRange(form.LogPanel.Height, 1, tall / 3);

                // the line shown is the latest, and it fits: something said in the log (the Seaded choice says itself)
                form.DeclareTotalsItem.PerformClick();
                form.DeclareEachItem.PerformClick();
                Ui.Pump();
                var log = form.LogList;
                Assert.True(log.Items.Count >= 2);
                Assert.Equal(log.Items.Count - 1, log.TopIndex);
                Assert.True(log.ClientSize.Height >= log.ItemHeight, $"the list's client area ({log.ClientSize.Height}) holds no whole line ({log.ItemHeight})");
            });

            // a new window on the same settings: still one line; and back to the default height
            fixture.Drive(form =>
            {
                Assert.True(form.LogOneRowItem.Checked);
                var small = form.LogPanel.Height;
                form.LogDefaultItem.PerformClick();
                Ui.Pump();
                Assert.False(AppSettings.Load(fixture.SettingsPath).LogOneRow);
                Assert.True(form.LogDefaultItem.Checked);
                Assert.True(form.LogPanel.Height > small * 3);
            });
        });
    }

    [Fact]
    public void The_Seaded_menu_chooses_the_declarations_cut_keeps_it_in_the_settings_and_the_build_follows()
    {
        using var fixture = new WindowFixture((store, _, period, _) =>
        {
            var workspace = new ReviewWorkspace(store);
            var view = workspace.Load(period.Id);
            workspace.AddHandLine(period.Id, view.Persons[0].PersonId, new DateOnly(2026, 10, 20), 100m, "Mari Maasikas", "lisatasu", "Palgad"); // a second payment of type 10
        });
        Ui.Run(() =>
        {
            fixture.Drive(form =>
            {
                Assert.True(form.DeclareEachItem.Checked);
                Assert.False(form.DeclareTotalsItem.Checked);
                form.Tabs.SelectedIndex = 1;
                Ui.Pump();
                var panel = form.Declaration;
                panel.BuildButton.PerformClick();
                Ui.Until(() => !form.IsBusy && panel.LinesGrid.Rows.Count == 2, what: "the build, payment by payment");

                // the totals cut chosen: kept in the settings at once, the check mark moves
                form.DeclareTotalsItem.PerformClick();
                Ui.Pump();
                Assert.True(AppSettings.Load(fixture.SettingsPath).DeclareTotalsPerType);
                Assert.True(form.DeclareTotalsItem.Checked);
                Assert.False(form.DeclareEachItem.Checked);
            });

            // a new window on the same settings: the choice stands, and the build adds the type up
            fixture.Drive(form =>
            {
                Assert.True(form.DeclareTotalsItem.Checked);
                Assert.False(form.DeclareEachItem.Checked);
                form.Tabs.SelectedIndex = 1;
                Ui.Pump();
                var panel = form.Declaration;
                panel.BuildButton.PerformClick();
                Ui.Until(() => !form.IsBusy && panel.LinesGrid.Rows.Count == 1, what: "the build as totals");
                var row = panel.LinesGrid.Rows[0];
                Assert.Equal(("10", 1005.92m), (row.Cells["type"].Value, row.Cells["net"].Value));
                Assert.Contains("20.10.2026", row.Cells["date"].Value?.ToString(), StringComparison.Ordinal);
                Assert.StartsWith("koond, liik 10: ", row.Cells["source"].Value?.ToString(), StringComparison.Ordinal);

                // and back: the totals draft goes, the two lines are made afresh
                form.DeclareEachItem.PerformClick();
                Ui.Pump();
                Assert.False(AppSettings.Load(fixture.SettingsPath).DeclareTotalsPerType);
                panel.BuildButton.PerformClick();
                Ui.Until(() => !form.IsBusy && panel.LinesGrid.Rows.Count == 2, what: "the build, payment by payment again");
                Assert.All(panel.LinesGrid.Rows.Cast<DataGridViewRow>(), r => Assert.DoesNotContain("koond", r.Cells["source"].Value?.ToString() ?? "", StringComparison.Ordinal));
            });
        });
    }

    [Fact]
    public void The_declaration_tab_builds_the_lines_marks_the_upload_and_settles_a_line()
    {
        using var fixture = new WindowFixture();
        Ui.Run(() => fixture.Drive(form =>
        {
            form.Tabs.SelectedIndex = 1;
            Ui.Pump();
            var panel = form.Declaration;
            Assert.True(panel.BuildButton.Enabled);
            panel.BuildButton.PerformClick();
            Ui.Until(() => !form.IsBusy && panel.LinesGrid.Rows.Count == 1, what: "the build");
            // the lines' search box hides and restores, and leaves a current line
            panel.LinesSearch.Text = "nobody-here";
            Ui.Pump();
            Assert.False(panel.LinesGrid.Rows[0].Visible);
            panel.LinesSearch.Text = "";
            Ui.Pump();
            Assert.True(panel.LinesGrid.Rows[0].Visible);
            Assert.NotNull(panel.LinesGrid.CurrentRow);
            Assert.IsType<int>(panel.LinesGrid.Rows[0].Cells["version"].Value);
            var row = panel.LinesGrid.Rows[0];
            Assert.Equal(("R uus", "mustand", "MAASIKAS MARI", "10"), (row.Cells["action"].Value, row.Cells["status"].Value, row.Cells["person"].Value, row.Cells["type"].Value));
            Assert.False(panel.UploadedButton.Enabled);

            // the file goes out at store level (the folder dialog is not a test's business); the window reloads
            var services = form.Services!;
            var periodId = form.View!.Period.Id;
            services.Submitter.Export(periodId, fixture.ExportFolder);
            form.Reload();
            Ui.Pump();
            Assert.Equal("saadetud", panel.LinesGrid.Rows[0].Cells["status"].Value);
            Assert.True(panel.UploadedButton.Enabled);
            panel.UploadedButton.PerformClick();
            Ui.Pump();
            Assert.Equal(PeriodState.Uploaded, form.View!.Period.State);

            panel.LinesGrid.CurrentCell = panel.LinesGrid.Rows[0].Cells[0];
            Ui.Pump();
            Assert.True(panel.AcceptButton.Enabled);
            panel.AcceptButton.PerformClick();
            Ui.Pump();
            Assert.Equal("vastu võetud", panel.LinesGrid.Rows[0].Cells["status"].Value);
            Assert.False(panel.AcceptButton.Enabled);
        }));
    }

    [Fact]
    public void Files_import_without_a_company_when_their_TOR_names_the_employer_and_switch_to_another_employer_when_asked()
    {
        using var fixture = new WindowFixture(empty: true);
        var palgad = Path.Combine(fixture.Folder, "palgad.xlsx");
        File.WriteAllBytes(palgad, SatellesTSD.Tests.ReviewFixtures.Workbook());
        var other = Path.Combine(fixture.Folder, "teine.xlsx");
        File.WriteAllBytes(other, SatellesTSD.Tests.Tables.Xlsx("Töötajate loend", [Core.Import.TorHeaders.All.ToArray<object?>(),
            ["1", 47001010000d, "KASK", "KATI", new DateTime(2024, 2, 15), 1, "Tööleping", 2, "Kehtiv", null, null, null, null, null, null, null, null, "EE", null, null, null, null, null, null, null, null, 12345678, "Teine OÜ"]]));
        var statementOnly = Path.Combine(fixture.Folder, "lhv.xlsx");
        File.WriteAllBytes(statementOnly, SatellesTSD.Tests.Tables.Xlsx("LHV", [Core.Import.LayoutCatalog.Default.Find("lhv-et-short")!.Headers.ToArray<object?>(),
            ["EE497700771001131757", 1, new DateTime(2026, 10, 5), "EE12", "Mari Maasikas", "D", -905.92, null, "2026100500000001", "palk", "EUR", 48712219406d]]));
        var questions = new List<string>();
        var notices = new List<string>();
        var answer = true;
        Ui.Run(() => fixture.Drive(form =>
        {
            form.Informing = notices.Add;
            Assert.Null(form.View); // an empty store: no company to show
            Assert.True(form.ImportButton.Enabled); // the month is chosen, so the files' TÖR may name the company

            // a statement alone names nobody: told, nothing imported
            Run(form, form.ImportFilesAsync([statementOnly]), () => notices.Count == 1, "the notice");
            Assert.Null(form.View);
            Assert.Empty(form.Services!.Store.Companies());

            // the company comes from the files: created, shown, and the files land in the month chosen
            Run(form, form.ImportFilesAsync([palgad]), () => form.View is { Files.Count: 1 }, "the import without a company");
            var view = form.View!;
            Assert.Equal(("10235688", "Näidis OÜ", 2026, 10), (view.Company.RegistryCode, view.Company.Name, view.Period.Year, view.Period.Month));
            Assert.Null(view.Run); // the files are in; the search is the accountant's next step
            Assert.Single(form.Services!.Store.Companies());
            Assert.Empty(questions);

            // another employer's files while this company is shown: asked; "no" changes nothing
            answer = false;
            Run(form, form.ImportFilesAsync([other]), () => questions.Count == 1, "the question");
            Assert.Contains("Teine OÜ (12345678)", questions[0], StringComparison.Ordinal);
            Assert.Contains("Luua see ettevõte", questions[0], StringComparison.Ordinal);
            Assert.Single(form.Services!.Store.Companies());
            Assert.Equal("10235688", form.View!.Company.RegistryCode);

            // "yes" creates the company and switches to it, the month kept
            answer = true;
            Run(form, form.ImportFilesAsync([other]), () => form.View is { Company.RegistryCode: "12345678", Files.Count: 1 }, "the switch to the other employer");
            Assert.Equal(("Teine OÜ", 2026, 10), (form.View!.Company.Name, form.View.Period.Year, form.View.Period.Month));
            Assert.Equal(2, form.Services!.Store.Companies().Count);

            // back to the first company's files: the company exists, so the question says so, and it is taken up
            Run(form, form.ImportFilesAsync([palgad]), () => form.View is { Company.RegistryCode: "10235688" }, "the switch back");
            Assert.Contains("Vahetada ettevõte selle vastu", questions[^1], StringComparison.Ordinal);
            Assert.Equal(2, form.Services!.Store.Companies().Count);

            // the company closed: the other employer's files go to their company without a question
            var asked = questions.Count;
            form.CloseCompanyButton.PerformClick();
            Ui.Pump();
            Assert.Null(form.View);
            Assert.Equal("", AppSettings.Load(fixture.SettingsPath).LastCompany); // closed by the accountant, remembered
            form.RefillCompanies();
            Ui.Pump();
            Assert.Equal(-1, form.CompanyBox.SelectedIndex); // and kept closed when the companies are listed afresh
            Run(form, form.ImportFilesAsync([other]), () => form.View is { Company.RegistryCode: "12345678" }, "the import after the close");
            Assert.Equal(asked, questions.Count);
            Assert.Equal("12345678", AppSettings.Load(fixture.SettingsPath).LastCompany);
        }, confirm: (question, _) => { questions.Add(question); return answer; }));
    }

    /// <summary>Pumps until the window's work is done and the condition holds, then surfaces what the work threw.</summary>
    private static void Run(MainForm form, Task work, Func<bool> condition, string what)
    {
        Ui.Until(() => work.IsCompleted && !form.IsBusy && condition(), what: what);
        work.GetAwaiter().GetResult();
    }

    [Fact]
    public void An_update_starts_the_new_version_on_the_one_off_in_hand_and_on_no_store_once_one_was_chosen_in_the_window()
    {
        // the rule: the command line's one-off is handed on while it is the store in hand (or the one that could not be
        // opened); a store chosen in the window ends it — the settings name that store, and nothing is handed on
        Assert.Equal(@"C:\b.db", MainForm.StoreHandedOn(oneOff: true, inHand: @"C:\b.db", commandLine: @"C:\b.db"));
        Assert.Equal(@"C:\b.db", MainForm.StoreHandedOn(oneOff: true, inHand: null, commandLine: @"C:\b.db"));
        Assert.Null(MainForm.StoreHandedOn(oneOff: false, inHand: @"C:\c.db", commandLine: @"C:\b.db"));
        Assert.Null(MainForm.StoreHandedOn(oneOff: false, inHand: @"C:\c.db", commandLine: null));

        Ui.Run(() =>
        {
            var release = new SatellesTSD.Infrastructure.Updates.ReleaseManifest(new Version(0, 11, 0, 0), "SatellesTSD-v0.11.0.zip", 1, new string('A', 64), "trial", null);
            using var handing = new UpdateDialog(Path.Combine(Path.GetTempPath(), "nowhere", "SatellesTSD.exe"), Path.GetTempPath(), release, new Version(0, 10, 0, 0), fromStart: false, () => { })
            {
                StoreToHandOn = @"C:\b.db",
            };
            var arguments = handing.StartArguments();
            Assert.Equal(["--store", @"C:\b.db"], arguments.Take(2));
            using var plain = new UpdateDialog(Path.Combine(Path.GetTempPath(), "nowhere", "SatellesTSD.exe"), Path.GetTempPath(), release, new Version(0, 10, 0, 0), fromStart: false, () => { });
            Assert.DoesNotContain("--store", plain.StartArguments()); // whatever this process was started with
        });
    }

    [Fact]
    public void The_update_dialog_closes_on_a_hand_over_and_only_then_while_it_works()
    {
        Ui.Run(() =>
        {
            var release = new SatellesTSD.Infrastructure.Updates.ReleaseManifest(new Version(0, 11, 0, 0), "SatellesTSD-v0.11.0.zip", 1, new string('A', 64), "trial", null);
            using var dialog = new UpdateDialog(Path.Combine(Path.GetTempPath(), "nowhere", "SatellesTSD.exe"), Path.GetTempPath(), release, new Version(0, 10, 0, 0), fromStart: true, () => { })
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-4000, -4000),
            };
            dialog.Show();
            Ui.Pump();
            Assert.Equal("Hiljem", dialog.StopButton.Text); // Enter means later at the offer
            Assert.Same(dialog.StopButton, dialog.AcceptButton);

            // while it works the window cannot be closed; a hand-over closes it with OK
            dialog.SimulateWorking();
            dialog.Close();
            Ui.Pump();
            Assert.True(dialog.Visible);
            dialog.SimulateHandOver();
            Ui.Pump();
            Assert.True(dialog.Started);
            Assert.False(dialog.Visible);
            Assert.Equal(DialogResult.OK, dialog.DialogResult);
        });

        Ui.Run(() =>
        {
            var release = new SatellesTSD.Infrastructure.Updates.ReleaseManifest(new Version(0, 11, 0, 0), "SatellesTSD-v0.11.0.zip", 1, new string('A', 64), null, null);
            using var dialog = new UpdateDialog(Path.Combine(Path.GetTempPath(), "nowhere", "SatellesTSD.exe"), Path.GetTempPath(), release, new Version(0, 10, 0, 0), fromStart: false, () => { })
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-4000, -4000),
            };
            dialog.Show();
            Ui.Pump();
            // a silent new version with nothing to put back: the text says so, and the window may be closed
            dialog.SimulateNoAnswer(withPrevious: false);
            Assert.Equal(("Oota veel", "Sulge"), (dialog.InstallButton.Text, dialog.StopButton.Text));
            Assert.Contains("jääb tööle", dialog.StatusText, StringComparison.Ordinal);
            Assert.False(dialog.LeftRunning);
            dialog.Close();
            Ui.Pump();
            Assert.False(dialog.Visible);
            Assert.Equal((false, true), (dialog.Started, dialog.LeftRunning)); // the new version stays running: the old window makes way (decision 157)

            // a new copy that could not be ended: said, and the old window makes way the same
            using var stuck = new UpdateDialog(Path.Combine(Path.GetTempPath(), "nowhere", "SatellesTSD.exe"), Path.GetTempPath(), release, new Version(0, 10, 0, 0), fromStart: false, () => { })
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-4000, -4000),
            };
            stuck.Show();
            Ui.Pump();
            stuck.SimulateNotClosed();
            Assert.Equal(UiText.UpdateNotClosed, stuck.StatusText);
            stuck.Close();
            Ui.Pump();
            Assert.Equal((false, true, false), (stuck.Started, stuck.LeftRunning, stuck.Visible));

            // the silent copy gone by the time the dialog is closed (it crashed, or was closed): nothing is left running,
            // so the dialog says so and stays, and the old window keeps the store; answered after all — a plain hand-over
            using var gone = new UpdateDialog(Path.Combine(Path.GetTempPath(), "nowhere", "SatellesTSD.exe"), Path.GetTempPath(), release, new Version(0, 10, 0, 0), fromStart: false, () => { })
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-4000, -4000),
            };
            gone.Show();
            Ui.Pump();
            gone.SimulateNoAnswer(withPrevious: false);
            gone.CopyStateProbe = () => (false, true, 3);
            gone.Close();
            Ui.Pump();
            Assert.True(gone.Visible); // the close was called off: the ending is said first
            Assert.Contains("(kood 3)", gone.StatusText, StringComparison.Ordinal);
            gone.Close();
            Ui.Pump();
            Assert.Equal((false, false, false), (gone.Started, gone.LeftRunning, gone.Visible));
            using var late = new UpdateDialog(Path.Combine(Path.GetTempPath(), "nowhere", "SatellesTSD.exe"), Path.GetTempPath(), release, new Version(0, 10, 0, 0), fromStart: false, () => { })
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-4000, -4000),
            };
            late.Show();
            Ui.Pump();
            late.SimulateNoAnswer(withPrevious: false);
            late.CopyStateProbe = () => (true, false, 0);
            late.Close();
            Ui.Pump();
            Assert.Equal((true, false, false), (late.Started, late.LeftRunning, late.Visible));

            // an update that simply failed or was cancelled leaves nothing running: the old window stays
            using var cancelled = new UpdateDialog(Path.Combine(Path.GetTempPath(), "nowhere", "SatellesTSD.exe"), Path.GetTempPath(), release, new Version(0, 10, 0, 0), fromStart: false, () => { })
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-4000, -4000),
            };
            cancelled.Show();
            Ui.Pump();
            cancelled.Close();
            Ui.Pump();
            Assert.Equal((false, false), (cancelled.Started, cancelled.LeftRunning));
        });
    }

    [Fact]
    public void The_history_dialog_lists_every_version_and_settles_the_one_beneath()
    {
        using var fixture = new WindowFixture((store, _, period, folder) =>
        {
            // exported and uploaded, then reopened and changed before the feedback came: an M draft hides the sent R
            WindowFixture.ExportAndUpload(store, period, folder);
            WindowFixture.Submitter(store).Reopen(period.Id);
            var workspace = new ReviewWorkspace(store);
            var mari = workspace.Load(period.Id).Persons[0].Person!;
            workspace.SavePerson(mari with { FundedPensionMember = true, PensionRate = 0.02m });
            new Core.Declare.PeriodDeclarer(store).Build(period.Id);
        });
        Ui.Run(() => fixture.Drive(form =>
        {
            form.Tabs.SelectedIndex = 1;
            Ui.Pump();
            var panel = form.Declaration;
            Assert.Equal("M muudatus", panel.LinesGrid.Rows[0].Cells["action"].Value);
            panel.LinesGrid.CurrentCell = panel.LinesGrid.Rows[0].Cells[0];
            Ui.Pump();
            Assert.False(panel.AcceptButton.Enabled); // the standing version is a draft: nothing to settle from the grid
            Assert.True(panel.HistoryButton.Enabled);

            var dialog = form.CreateHistory(panel.SelectedLine!);
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Location = new Point(-4000, -4000);
            dialog.Show();
            Ui.Pump();
            Assert.Equal([PaymentStatus.Sent, PaymentStatus.Draft], dialog.Versions.Select(v => v.Status));
            Assert.Equal([LineAction.R, LineAction.M], dialog.Versions.Select(v => v.Action));
            Assert.Equal(PaymentStatus.Sent, dialog.Selected?.Status); // opened on the version awaiting its verdict
            Assert.True(dialog.RejectVersionButton.Enabled);
            dialog.RejectVersionButton.PerformClick();
            Ui.Pump();
            Assert.Equal([PaymentStatus.Rejected, PaymentStatus.Draft], dialog.Versions.Select(v => v.Status));
            Assert.False(dialog.RejectVersionButton.Enabled);
            dialog.Close();
            Ui.Pump();

            // the next build makes the draft an R again, as the key never reached the server
            panel.BuildButton.PerformClick();
            Ui.Until(() => !form.IsBusy && panel.LinesGrid.Rows.Count == 1 && panel.LinesGrid.Rows[0].Cells["action"].Value?.ToString() == "R uus", what: "the rebuild");
        }));
    }

    [Fact]
    public void The_cards_exemption_box_shows_the_years_figure_and_stores_only_a_figure_of_the_persons_own()
    {
        using var fixture = new WindowFixture();
        Ui.Run(() => fixture.Drive(form =>
        {
            Mari(form);
            var inForce = 700m.ToString("0.00", System.Globalization.CultureInfo.CurrentCulture);
            Assert.Equal(inForce, form.PersonEditor.ExemptionBox.Text); // the 2026 figure in force, not the 2025 one

            // saved with the box untouched: nothing of the person's own is stored, so another year's figure still follows the rates
            form.PersonEditor.DeclarationBox.SelectedIndex = 1;
            form.PersonEditor.SaveButton.PerformClick();
            Ui.Until(() => !form.IsBusy, what: "the save with the box untouched");
            Assert.Null(StoredMari(form).BasicExemptionMonthly);

            Mari(form);
            form.PersonEditor.ExemptionBox.Text = "500";
            form.PersonEditor.SaveButton.PerformClick();
            Ui.Until(() => !form.IsBusy, what: "the save of a figure of her own");
            Assert.Equal(500m, StoredMari(form).BasicExemptionMonthly);

            Mari(form);
            form.PersonEditor.ExemptionBox.Text = inForce; // typed back to the figure in force: no figure of her own
            form.PersonEditor.SaveButton.PerformClick();
            Ui.Until(() => !form.IsBusy, what: "the save of the figure in force");
            Assert.Null(StoredMari(form).BasicExemptionMonthly);
        }));
    }

    [Fact]
    public void The_export_asks_first_when_the_review_changed_after_the_lines_were_built()
    {
        using var fixture = new WindowFixture((store, _, period, _) =>
        {
            new Core.Declare.PeriodDeclarer(store).Build(period.Id);
            var workspace = new ReviewWorkspace(store);
            var mari = workspace.Load(period.Id).Persons[0].Person!;
            workspace.SavePerson(mari with { FundedPensionMember = true, PensionRate = 0.02m }); // after "Koosta read"
        });
        var asked = new List<string>();
        Ui.Run(() => fixture.Drive(form =>
        {
            form.Tabs.SelectedIndex = 1;
            Ui.Pump();
            var panel = form.Declaration;
            Assert.True(panel.ExportButton.Enabled);
            panel.ExportButton.PerformClick(); // answered "no": the folder dialog never opens
            Ui.Until(() => !form.IsBusy && asked.Count > 0, what: "the export's question");
            Assert.Equal([UiText.ReviewChangedSinceBuild], asked);
            Assert.Empty(form.Services!.Store.Submissions(form.View!.Period.Id));
            Assert.Equal(PaymentStatus.Draft, form.Services.Store.Payments(form.View.Period.Id).Single().Status);
        }, confirm: (question, _) =>
        {
            asked.Add(question);
            return false;
        }));
    }

    [Fact]
    public void The_feedback_cell_and_the_files_list_tell_the_reports_detail_in_their_tooltips()
    {
        using var fixture = new WindowFixture((store, _, period, folder) =>
        {
            WindowFixture.ExportAndUpload(store, period, folder);
            var line = store.Payments(period.Id).Single();
            var submission = store.Submissions(period.Id).Single();
            var report = Path.Combine(folder, "tagasiside.xml");
            File.WriteAllText(report, $"""
                <processing result="OK">
                  <entryHeader>
                    <entryNumber>EE0201001</entryNumber>
                    <validations><validation><errorLevel>WARNING</errorLevel><errorCode>KR_JAOTIS_TEST</errorCode><errorDescription>jaotuse märkus</errorDescription></validation></validations>
                    <responseData>
                      <entryDetail>
                        <lineReferences><lineReference><lineInternalId>{line.LineNumber}</lineInternalId><lineNumberCounter>1</lineNumberCounter></lineReference></lineReferences>
                        <data><code>TSD_SM</code><description>Sotsiaalmaks</description><info>330,00</info></data>
                        <data><code>TSD_KTM</code><description>Kinnipeetud tulumaks</description><info>120,00</info></data>
                        <data><code>KR_TEST_HOIATUS</code><description>WARNING</description><info>vaata üle</info></data>
                      </entryDetail>
                    </responseData>
                  </entryHeader>
                </processing>
                """);
            WindowFixture.Submitter(store).ReadFeedback(submission.Id, report);
        });
        Ui.Run(() => fixture.Drive(form =>
        {
            form.Tabs.SelectedIndex = 1;
            Ui.Pump();
            var panel = form.Declaration;
            var cell = panel.LinesGrid.Rows[0].Cells["feedback"];
            Assert.StartsWith("Warning: KR_TEST_HOIATUS", cell.Value?.ToString(), StringComparison.Ordinal); // the cell keeps its short text
            var tip = panel.FeedbackTips.TextOf(panel.LinesGrid.Rows[0]) ?? "";
            Assert.Contains("KR_TEST_HOIATUS (Warning): vaata üle", tip, StringComparison.Ordinal);
            Assert.Contains("sotsiaalmaks", tip, StringComparison.Ordinal);
            var ktm = Assert.Single(tip.Split(Environment.NewLine), l => l.Contains("tulumaks (kinnipeetud)", StringComparison.Ordinal)); // the server's KTM with our figure beside it
            Assert.Matches(@"tulumaks \(kinnipeetud\): 120[.,]00 \(\d+[.,]\d\d\)$", ktm);
            Assert.DoesNotContain("TSD_KTM", tip, StringComparison.Ordinal);
            Assert.Contains(UiText.NextActionText(Core.Feedback.NextAction.ReviewWarning), tip, StringComparison.Ordinal);
            Assert.DoesNotContain("sotsiaalmaks", cell.ToolTipText, StringComparison.Ordinal); // the grid's own tooltip would cut the text at 256 characters: never given it
            Assert.Contains("KR_JAOTIS_TEST", panel.SubmissionsList.Items[0].ToolTipText, StringComparison.Ordinal);
        }));
    }

    [Fact]
    public void A_pension_age_persons_exemption_box_shows_the_pension_age_figure_and_stores_700_as_theirs()
    {
        using var fixture = new WindowFixture((store, _, period, _) =>
        {
            var workspace = new ReviewWorkspace(store);
            var mari = workspace.Load(period.Id).Persons[0].Person!;
            workspace.SavePerson(mari with { PensionAgeYear = 2026 });
        });
        Ui.Run(() => fixture.Drive(form =>
        {
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            Mari(form);
            Assert.Equal(776m.ToString("0.00", culture), form.PersonEditor.ExemptionBox.Text); // the pension-age figure of 2026

            form.PersonEditor.ExemptionBox.Text = 700m.ToString("0.00", culture); // her own application: 700, below the 776 in force
            form.PersonEditor.SaveButton.PerformClick();
            Ui.Until(() => !form.IsBusy, what: "the save of 700");
            Assert.Equal(700m, StoredMari(form).BasicExemptionMonthly);

            Mari(form);
            Assert.Equal(700m.ToString("0.00", culture), form.PersonEditor.ExemptionBox.Text);
            form.PersonEditor.ExemptionBox.Text = 776m.ToString("0.00", culture);
            form.PersonEditor.SaveButton.PerformClick();
            Ui.Until(() => !form.IsBusy, what: "the save of the figure in force");
            Assert.Null(StoredMari(form).BasicExemptionMonthly);
        }));
    }

    [Fact]
    public void The_export_is_refused_while_the_search_has_not_seen_every_linked_file()
    {
        using var fixture = new WindowFixture((store, company, period, _) =>
        {
            new Core.Declare.PeriodDeclarer(store).Build(period.Id);
            var bytes = SatellesTSD.Tests.ReviewFixtures.Workbook(salary: 1000m);
            var file = store.AddImport(company.Id, SatellesTSD.Tests.ReviewFixtures.Importer.Read("teine.xlsx", bytes), bytes);
            store.LinkImport(period.Id, file.Id); // after "Koosta read", without a new search
        });
        var notices = new List<string>();
        Ui.Run(() => fixture.Drive(form =>
        {
            form.Informing = notices.Add;
            form.Tabs.SelectedIndex = 1;
            Ui.Pump();
            Assert.True(form.View!.RunIsStale);
            form.Declaration.ExportButton.PerformClick(); // refused before the folder dialog
            Ui.Until(() => !form.IsBusy && notices.Count > 0, what: "the export's refusal");
            Assert.Equal([UiText.RunStaleExport], notices);
            Assert.Empty(form.Services!.Store.Submissions(form.View!.Period.Id));
        }));
    }

    [Fact]
    public void A_new_companys_code_has_the_shape_of_what_the_file_will_call_it()
    {
        // by its shape: an Estonian registry code or a personal code passes; anything else is an MRR code, and is asked about
        Assert.Null(CompanyDialog.CodeProblem("10235688", null));
        Assert.False(CompanyDialog.AsksWhetherMrr("10235688", null));
        Assert.Null(CompanyDialog.CodeProblem("38810140309", null));
        Assert.False(CompanyDialog.AsksWhetherMrr("38810140309", null));
        Assert.Null(CompanyDialog.CodeProblem("65758008", null)); // a foreign company's code: nothing wrong with it
        Assert.True(CompanyDialog.AsksWhetherMrr("65758008", null)); // but is it one, or a slip?
        Assert.True(CompanyDialog.AsksWhetherMrr("1023568", null)); // a registry code a digit short reads as an MRR code: asked
        Assert.False(CompanyDialog.AsksWhetherMrr("65758008", Core.Xbrl.PayeeId.MrrKood)); // the card says so: no question

        // signs that no code has; a code that is not what the card calls it
        Assert.Equal(UiText.CompanyCodeSigns, CompanyDialog.CodeProblem("1023 5688", null));
        Assert.Equal(UiText.CompanyCodeSigns, CompanyDialog.CodeProblem("LT-65758008", Core.Xbrl.PayeeId.MrrKood));
        Assert.Equal(UiText.CompanyCodeShape(Core.Xbrl.PayeeId.Ariregistrikood), CompanyDialog.CodeProblem("1023568", Core.Xbrl.PayeeId.Ariregistrikood));
        Assert.Equal(UiText.CompanyCodeShape(Core.Xbrl.PayeeId.Isikukood), CompanyDialog.CodeProblem("10235688", Core.Xbrl.PayeeId.Isikukood));
        Assert.Null(CompanyDialog.CodeProblem("65758008", Core.Xbrl.PayeeId.Ariregistrikood)); // eight digits called a registry code: the card's word
    }

    [Fact]
    public void The_company_dialog_asks_about_a_code_that_reads_as_an_MRR_code_and_stays_open_on_a_no()
    {
        Ui.Run(() =>
        {
            using var dialog = new CompanyDialog { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000) };
            var asked = new List<string>();
            var answer = false;
            dialog.AskingMrr = question => { asked.Add(question); return answer; };
            dialog.Show();
            dialog.NameBox.Text = "Užsienio MB";
            dialog.CodeBox.Text = "65758008";

            // No: the dialog stays open, to correct the code
            ((Button)dialog.AcceptButton!).PerformClick();
            Assert.Equal(UiText.CompanyCodeAsMrr("65758008"), Assert.Single(asked));
            Assert.Equal(DialogResult.None, dialog.DialogResult);

            // Yes: accepted as typed
            answer = true;
            ((Button)dialog.AcceptButton!).PerformClick();
            Assert.Equal((2, DialogResult.OK), (asked.Count, dialog.DialogResult));

            // an Estonian registry code asks nothing
            using var plain = new CompanyDialog { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000) };
            plain.AskingMrr = question => { asked.Add(question); return false; };
            plain.Show();
            plain.NameBox.Text = "Näidis OÜ";
            plain.CodeBox.Text = "10235688";
            ((Button)plain.AcceptButton!).PerformClick();
            Assert.Equal((2, DialogResult.OK), (asked.Count, plain.DialogResult));
        });
    }

    [Fact]
    public void An_MRR_code_typed_with_a_country_code_before_it_is_asked_about_on_the_save_and_on_the_unsaved_question()
    {
        using var fixture = new WindowFixture();
        var questions = new List<string>();
        var answer = false;
        Ui.Run(() => fixture.Drive(form =>
        {
            Mari(form);
            var card = form.PersonEditor;
            card.MrrBox.Text = "LV40003689779";
            Assert.True(card.IsDirty);

            // Salvesta, and No to the question: nothing saved, the card as typed
            card.SaveButton.PerformClick();
            Ui.Pump();
            Assert.Equal(UiText.MrrPrefixQuestion("LV40003689779", "LV"), Assert.Single(questions));
            Assert.Null(StoredMari(form).MrrCode);
            Assert.Equal((true, "LV40003689779"), (card.IsDirty, card.MrrBox.Text));

            // another person picked, Yes to saving, No to the prefix: the edits stay and the selection goes back
            form.AnsweringUnsaved = () => DialogResult.Yes;
            form.PersonsGrid.CurrentCell = form.PersonsGrid.Rows[1].Cells[0];
            Ui.Pump();
            Assert.Equal(2, questions.Count);
            Assert.Null(StoredMari(form).MrrCode);
            Assert.Equal((0, true), (form.PersonsGrid.CurrentRow!.Index, card.IsDirty));

            // Yes to the prefix: saved as typed
            answer = true;
            card.SaveButton.PerformClick();
            Ui.Until(() => !form.IsBusy && !card.IsDirty, what: "the save");
            Assert.Equal("LV40003689779", StoredMari(form).MrrCode);

            // a code with a letter of its own asks nothing
            Mari(form);
            form.PersonEditor.MrrBox.Text = "A0003689779";
            form.PersonEditor.SaveButton.PerformClick();
            Ui.Until(() => !form.IsBusy && !form.PersonEditor.IsDirty, what: "the second save");
            Assert.Equal((3, "A0003689779"), (questions.Count, StoredMari(form).MrrCode));
        }, (question, _) =>
        {
            if (question.Contains("mitteresidendi kood", StringComparison.Ordinal))
            {
                questions.Add(question);
                return answer;
            }

            return true;
        }));
    }

    [Fact]
    public void A_store_a_newer_version_upgraded_is_told_to_update_and_the_copy_is_offered_only_after_a_failed_update()
    {
        using var fixture = new WindowFixture();
        var newer = Path.Combine(fixture.Folder, "uuem.db");
        using (SatellesTSD.Infrastructure.SqliteStore.Open(newer))
        {
        }

        var backup = $"{newer}.before-v{SatellesTSD.Infrastructure.SqliteStore.SchemaVersion + 1}";
        File.Copy(newer, backup); // the copy the newer version left before upgrading
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={newer};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"INSERT INTO schema_version (version) VALUES ({SatellesTSD.Infrastructure.SqliteStore.SchemaVersion + 1})";
            command.ExecuteNonQuery();
        }

        var questions = new List<string>();
        var notices = new List<string>();
        var restore = false;
        Ui.Run(() => fixture.Drive(form =>
        {
            form.Informing = notices.Add;
            var putBack = false;
            string? asked = null;
            form.FailedUpdateProbe = store => { asked = store; return putBack ? backup : null; };

            // an older copy that no update put back (a second computer, an old folder): told to update, nothing offered
            form.OpenChosenStore(newer);
            Ui.Pump();
            Assert.Equal([UiText.StoreNewerUpdate], notices);
            Assert.Equal(newer, asked); // asked of the store that would not open
            Assert.Empty(questions);
            Assert.Empty(fixture.Failures);
            Assert.Equal(fixture.StorePath, form.Services!.StorePath); // the store in hand stays
            Assert.True(File.Exists(backup));

            // the copy a failed update put back: the restore is offered, with the day the copy was made; declined, the update is said
            putBack = true;
            form.OpenChosenStore(newer);
            Ui.Pump();
            var question = Assert.Single(questions);
            Assert.Contains(backup, question, StringComparison.Ordinal);
            Assert.Contains(UiText.StoreBackupMade(File.GetLastWriteTime(backup)), question, StringComparison.Ordinal);
            Assert.Equal([UiText.StoreNewerUpdate, UiText.StoreNewerDeclined], notices); // not "update": the update is what failed
            Assert.Equal(fixture.StorePath, form.Services!.StorePath);

            // accepted: the copy is the store again, the upgraded one set aside
            restore = true;
            form.OpenChosenStore(newer);
            Ui.Until(() => !form.IsBusy && form.Services?.StorePath == newer, what: "the restored store to open");
            Assert.Equal(2, questions.Count);
            Assert.Single(Directory.GetFiles(fixture.Folder, "uuem.db.newer-v*"));
        }, (question, _) =>
        {
            if (question.Contains(UiText.StoreNewerRestoreQuestion, StringComparison.Ordinal))
            {
                questions.Add(question);
                return restore;
            }

            return true;
        }));
    }

    [Fact]
    public void The_correction_reason_is_asked_for_each_section_the_file_carries_of_the_reasons_that_fit_it()
    {
        Ui.Run(() =>
        {
            // one section: asked once, as before
            using (var one = new CorrectionReasonDialog([Section.EE0201001]))
            {
                var box = Assert.Single(one.ReasonBoxes).Value;
                Assert.Equal(["PARANDUSPOHJUS_1", "PARANDUSPOHJUS_2", "PARANDUSPOHJUS_3"], box.Items.Cast<Choice>().Select(c => c.Value));
                Assert.Equal(new Dictionary<Section, string> { [Section.EE0201001] = "PARANDUSPOHJUS_1" }, one.Reasons);
            }

            // both sections: a choice each, offering only what fits — 3 for the pay section, 4 for the benefits section
            using var both = new CorrectionReasonDialog([Section.EE0201001, Section.EE0201002]);
            Assert.Equal([Section.EE0201001, Section.EE0201002], both.ReasonBoxes.Keys);
            Assert.Equal(["PARANDUSPOHJUS_1", "PARANDUSPOHJUS_2", "PARANDUSPOHJUS_4"], both.ReasonBoxes[Section.EE0201002].Items.Cast<Choice>().Select(c => c.Value));
            both.ReasonBoxes[Section.EE0201001].SelectedIndex = 2;
            both.ReasonBoxes[Section.EE0201002].SelectedIndex = 2;
            Assert.Equal(("PARANDUSPOHJUS_3", "PARANDUSPOHJUS_4"), (both.Reasons[Section.EE0201001], both.Reasons[Section.EE0201002]));
        });
    }

    [Fact]
    public void A_store_that_cannot_be_opened_leaves_the_store_in_hand_open_and_shown()
    {
        using var fixture = new WindowFixture();
        var broken = Path.Combine(fixture.Folder, "katki.db");
        File.WriteAllText(broken, "this is no store file, only text long enough to be read as a page header ........");
        var other = Path.Combine(fixture.Folder, "teine.db");
        using (var store = SatellesTSD.Infrastructure.SqliteStore.Open(other))
        {
            store.AddCompany("87654321", "Teine OÜ");
        }

        Ui.Run(() => fixture.Drive(form =>
        {
            Mari(form);
            var (periodId, rows, services) = (form.View!.Period.Id, form.PersonsGrid.Rows.Count, form.Services!);

            // a file that is no store: the failure is said, and nothing else changes — the store, the view, the settings
            form.OpenChosenStore(broken);
            Ui.Pump();
            Assert.Single(fixture.Failures);
            fixture.Failures.Clear();
            Assert.Same(services, form.Services);
            Assert.Equal((fixture.StorePath, periodId, rows), (form.Services!.StorePath, form.View!.Period.Id, form.PersonsGrid.Rows.Count));
            Assert.Equal(fixture.StorePath, AppSettings.Load(fixture.SettingsPath).StorePath);
            Assert.Contains(WindowFixture.RegistryCode, form.CompanyBox.SelectedItem?.ToString(), StringComparison.Ordinal);
            Assert.NotEmpty(form.Services.Store.Persons(form.View.Period.CompanyId)); // the store in hand still answers

            // unsaved edits dropped on the way to a store that cannot be opened: the card shows what the store holds again
            var card = form.PersonEditor;
            card.FirstNameBox.Text = "Muudetud";
            Assert.True(card.IsDirty);
            form.AnsweringUnsaved = () => DialogResult.No;
            form.OpenChosenStore(broken);
            Ui.Pump();
            Assert.Single(fixture.Failures);
            fixture.Failures.Clear();
            Assert.Equal((false, "MARI"), (card.IsDirty, card.FirstNameBox.Text));

            // and saved on the way: the grid follows the save
            card.DeclarationBox.SelectedIndex = 2;
            form.AnsweringUnsaved = () => DialogResult.Yes;
            form.OpenChosenStore(broken);
            Ui.Pump();
            Assert.Single(fixture.Failures);
            fixture.Failures.Clear();
            Assert.Equal(DeclarationChoice.Exclude, StoredMari(form).Declaration);
            Assert.Equal("välja", form.PersonsGrid.Rows[0].Cells["declaration"].Value?.ToString());
            Assert.False(card.IsDirty);
            form.AnsweringUnsaved = null;

            // the same store again: closed and opened, as before
            form.OpenChosenStore(fixture.StorePath);
            Ui.Until(() => !form.IsBusy, what: "the store to open again");
            Assert.NotSame(services, form.Services);
            Assert.Equal((fixture.StorePath, periodId), (form.Services!.StorePath, form.View!.Period.Id));

            // another store that opens: the window goes over to it
            form.OpenChosenStore(other);
            Ui.Until(() => !form.IsBusy, what: "the other store to open");
            Assert.Equal(other, form.Services!.StorePath);
            Assert.Contains("87654321", form.CompanyBox.SelectedItem?.ToString(), StringComparison.Ordinal);
            Assert.Equal(other, AppSettings.Load(fixture.SettingsPath).StorePath);

            // and from there a broken one: the other store stays in hand
            form.OpenChosenStore(broken);
            Ui.Pump();
            Assert.Single(fixture.Failures);
            fixture.Failures.Clear();
            Assert.Equal(other, form.Services!.StorePath);
            Assert.Contains("87654321", form.CompanyBox.SelectedItem?.ToString(), StringComparison.Ordinal);
        }));
    }

    [Fact]
    public void A_store_named_on_the_command_line_is_a_one_off_the_settings_do_not_remember()
    {
        using var fixture = new WindowFixture();
        var other = Path.Combine(fixture.Folder, "teine.db");
        using (var store = SatellesTSD.Infrastructure.SqliteStore.Open(other))
        {
            store.GetOrCreatePeriod(store.AddCompany("00000001", "Aaa OÜ").Id, 2026, 9);
            store.AddCompany(WindowFixture.RegistryCode, "Zzz OÜ"); // the remembered company's code is in this store too: it is not what the one-off shows
        }

        void Remembered(bool salaryOnly)
        {
            var saved = AppSettings.Load(fixture.SettingsPath);
            Assert.Equal((fixture.StorePath, WindowFixture.RegistryCode, 2026, 10, salaryOnly), (saved.StorePath, saved.LastCompany, saved.LastYear, saved.LastMonth, saved.PersonsSalaryOnly));
        }

        MainForm Start(string? storeOverride)
        {
            var form = new MainForm(fixture.SettingsPath, AppSettings.Load(fixture.SettingsPath), storeOverride)
            {
                StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000), Confirming = (_, _) => true, Failing = fixture.Failures.Add,
            };
            form.Show();
            Ui.Until(() => !form.IsBusy, what: "the window to go idle after opening");
            return form;
        }

        Ui.Run(() =>
        {
            // started on another store by the switch: its first company is shown, and the month picked in it is not remembered
            var form = Start(other);
            try
            {
                Assert.True(form.StoreIsOneOff);
                Assert.Contains("00000001", form.CompanyBox.SelectedItem?.ToString(), StringComparison.Ordinal); // the store's first company, not the remembered code
                Remembered(salaryOnly: false);
                form.MonthBox.SelectedIndex = 8; // September, the other store's month
                Ui.Pump();
                Assert.Equal((2026, 9), (form.View!.Period.Year, form.View.Period.Month));
                Remembered(salaryOnly: false);

                // the window's own preferences are saved as ever
                form.SalaryOnlyItem.PerformClick();
                Ui.Pump();
                Remembered(salaryOnly: true);
            }
            finally
            {
                fixture.Close(form);
            }

            Remembered(salaryOnly: true); // nor at the closing

            // the next plain start opens the remembered store, its company and its month
            form = Start(null);
            try
            {
                Assert.False(form.StoreIsOneOff);
                Assert.Contains(WindowFixture.RegistryCode, form.CompanyBox.SelectedItem?.ToString(), StringComparison.Ordinal);
                Assert.Equal((2026, 10), (form.View!.Period.Year, form.View.Period.Month));
            }
            finally
            {
                fixture.Close(form);
            }

            // the switch naming the remembered store itself is no one-off: the window remembers as ever
            form = Start(fixture.StorePath.ToUpperInvariant());
            try
            {
                Assert.False(form.StoreIsOneOff);
                form.MonthBox.SelectedIndex = 8;
                Ui.Pump();
                var again = AppSettings.Load(fixture.SettingsPath);
                Assert.Equal((WindowFixture.RegistryCode, 9), (again.LastCompany, again.LastMonth)); // what the window shows is remembered again
                Assert.Equal(fixture.StorePath, again.StorePath, ignoreCase: true);
            }
            finally
            {
                fixture.Close(form);
            }
        
            // a one-off again, and a store chosen in the window (the dialog's way in): remembered from there on
            form = Start(other);
            try
            {
                Assert.True(form.StoreIsOneOff);
                form.OpenChosenStore(other);
                Ui.Until(() => !form.IsBusy, what: "the chosen store to open");
                Assert.False(form.StoreIsOneOff);
                var chosen = AppSettings.Load(fixture.SettingsPath);
                Assert.Equal((other, "00000001"), (chosen.StorePath, chosen.LastCompany));
            }
            finally
            {
                fixture.Close(form);
            }
        });
    }

    [Fact]
    public void Unsaved_edits_on_the_card_stay_when_the_question_is_cancelled_or_the_save_fails_and_the_action_stops()
    {
        using var fixture = new WindowFixture();
        Ui.Run(() => fixture.Drive(form =>
        {
            Mari(form);
            var card = form.PersonEditor;
            var answer = DialogResult.Cancel;
            form.AnsweringUnsaved = () => answer;
            card.FirstNameBox.Text = ""; // a card the store will refuse (a person needs a first name)
            Assert.True(card.IsDirty);

            // Cancel: another person picked — the selection goes back, the card stays as typed
            form.PersonsGrid.CurrentCell = form.PersonsGrid.Rows[1].Cells[0];
            Ui.Pump();
            Assert.Equal(0, form.PersonsGrid.CurrentRow!.Index);
            Assert.Equal((true, ""), (card.IsDirty, card.FirstNameBox.Text));

            Assert.Equal(["Mari Maasikas"], form.PaymentsGrid.Rows.Cast<DataGridViewRow>().Select(r => r.Cells["payee"].Value?.ToString())); // her payments again, not the other person's

            // Cancel: "Uus ettevõte" — the card is asked about before the dialog opens, and nothing is made or switched
            var companies = form.Services!.Store.Companies().Count;
            var shown = form.CompanyBox.SelectedItem?.ToString();
            form.NewCompanyButton.PerformClick();
            Ui.Pump();
            Assert.Equal((companies, shown), (form.Services!.Store.Companies().Count, form.CompanyBox.SelectedItem?.ToString()));
            Assert.Equal((true, ""), (card.IsDirty, card.FirstNameBox.Text));
            Assert.Equal(0, form.PersonsGrid.CurrentRow!.Index);

            // a search that hides her row is called off the same way: the search goes, the grid is on her again
            form.PersonsSearch.Text = "tamm";
            Ui.Pump();
            Assert.Equal(("", 0), (form.PersonsSearch.Text, form.PersonsGrid.CurrentRow!.Index));
            Assert.Equal((true, ""), (card.IsDirty, card.FirstNameBox.Text));
            Assert.All(form.PersonsGrid.Rows.Cast<DataGridViewRow>(), r => Assert.True(r.Visible));

            // another month picked: the pickers go back, the view and the settings stay
            var (month, periodId) = (form.MonthBox.SelectedIndex, form.View!.Period.Id);
            form.MonthBox.SelectedIndex = month == 0 ? 1 : month - 1;
            Ui.Pump();
            Assert.Equal((month, periodId), (form.MonthBox.SelectedIndex, form.View!.Period.Id));
            Assert.Equal((true, ""), (card.IsDirty, card.FirstNameBox.Text));

            // Yes, and the save cannot happen: the failure is said, the card stays as typed, the selection goes back
            answer = DialogResult.Yes;
            form.PersonsGrid.CurrentCell = form.PersonsGrid.Rows[1].Cells[0];
            Ui.Pump();
            Assert.Equal(ReviewWorkspace.NameMissing, Assert.IsType<ReviewException>(Assert.Single(fixture.Failures)).Code);
            fixture.Failures.Clear();
            Assert.Equal(0, form.PersonsGrid.CurrentRow!.Index);
            Assert.Equal((true, ""), (card.IsDirty, card.FirstNameBox.Text));
            Assert.Equal("MARI", StoredMari(form).FirstName);

            // the window's closing and the company's closing are called off the same way
            answer = DialogResult.Cancel;
            form.Close();
            Ui.Pump();
            Assert.True(form.Visible && !form.IsDisposed);
            form.CloseCompanyButton.PerformClick();
            Ui.Pump();
            Assert.NotNull(form.View);
            Assert.Equal((true, ""), (card.IsDirty, card.FirstNameBox.Text));

            // the card put right and Yes: saved, and the other person shown
            card.FirstNameBox.Text = "MARI";
            card.DeclarationBox.SelectedIndex = 2;
            answer = DialogResult.Yes;
            form.PersonsGrid.CurrentCell = form.PersonsGrid.Rows[1].Cells[0];
            Ui.Until(() => !form.IsBusy && !card.IsDirty && StoredMari(form).Declaration == DeclarationChoice.Exclude, what: "the save on the way to another person");
            Ui.Pump();
            Assert.Empty(fixture.Failures);
            Assert.Equal(1, form.PersonsGrid.CurrentRow!.Index);

            // No: the edits dropped, the store as it was
            Mari(form);
            card.DeclarationBox.SelectedIndex = 0;
            Assert.True(card.IsDirty);
            answer = DialogResult.No;
            form.PersonsGrid.CurrentCell = form.PersonsGrid.Rows[1].Cells[0];
            Ui.Pump();
            Assert.False(card.IsDirty);
            Assert.Equal(1, form.PersonsGrid.CurrentRow!.Index);
            Assert.Equal(DeclarationChoice.Exclude, StoredMari(form).Declaration);
        }));
    }

    private static void Mari(MainForm form)
    {
        form.PersonsGrid.CurrentCell = form.PersonsGrid.Rows[0].Cells[0];
        Ui.Pump();
    }

    private static Person StoredMari(MainForm form) => form.Services!.Store.FindPerson(form.View!.Period.CompanyId, SatellesTSD.Tests.ReviewFixtures.MariCode)!;

    [Fact]
    public void The_grid_shows_when_the_employment_began_and_until_when_it_is_paused()
    {
        using var fixture = new WindowFixture(mariPaused: true);
        Ui.Run(() => fixture.Drive(form =>
        {
            var persons = form.PersonsGrid;
            Assert.Equal(persons.Columns["checked"]!.Index + 1, persons.Columns["start"]!.Index);
            Assert.Equal(persons.Columns["start"]!.Index + 1, persons.Columns["type"]!.Index);
            Assert.Equal(persons.Columns["pause"]!.Index + 1, persons.Columns["pauseEnd"]!.Index); // right after Peatamise algus
            Assert.Equal(("Töötamise alguse aeg", "Peatamise lõpu aeg"), (persons.Columns["start"]!.HeaderText, persons.Columns["pauseEnd"]!.HeaderText));
            Assert.Equal((typeof(DateTime), typeof(DateTime)), (persons.Columns["start"]!.ValueType, persons.Columns["pauseEnd"]!.ValueType));
            var rows = persons.Rows.Cast<DataGridViewRow>().ToList();
            Assert.All(rows.Take(2), r => Assert.IsType<DateTime>(r.Cells["start"].Value)); // the register's two persons
            Assert.Null(rows[2].Cells["start"].Value); // the rows nobody matched
            Assert.Equal(["30.06.2027", "", ""], rows.Select(r => r.Cells["pauseEnd"].FormattedValue?.ToString() ?? ""));
        }));
    }

    [Fact]
    public void Income_left_out_of_the_declaration_is_summed_on_the_person_with_why_and_coloured_only_when_nobody_chose_it()
    {
        using var fixture = new WindowFixture(prepare: (store, _, period, _) =>
        {
            var workspace = new ReviewWorkspace(store);
            var view = workspace.Load(period.Id);
            workspace.SetDeclare(view.Persons[1].Payments.Single(), true); // Jaan's November payment: refused
            workspace.SetDeclare(view.Persons[0].Payments.Single(p => !p.IsByHand), false); // Mari's salary: unticked
        });
        Ui.Run(() => fixture.Drive(form =>
        {
            var persons = form.PersonsGrid;
            Assert.Equal(persons.Columns["total"]!.Index + 1, persons.Columns["undeclared"]!.Index);
            Assert.Equal("Deklareerimata", persons.Columns["undeclared"]!.HeaderText);
            var (mari, jaan, unmatched) = (persons.Rows[0], persons.Rows[1], persons.Rows[2]);
            var mariPerson = (ReviewPerson)mari.Tag!;
            var jaanPerson = (ReviewPerson)jaan.Tag!;
            Assert.Equal(mariPerson.Payments.Single(p => !p.IsByHand).Amount, mari.Cells["undeclared"].Value);
            Assert.Equal(jaanPerson.Payments.Single().Amount, jaan.Cells["undeclared"].Value);
            Assert.Null(unmatched.Cells["undeclared"].Value);

            Assert.Equal(Color.MistyRose, jaan.DefaultCellStyle.BackColor); // refused, and nobody chose it
            Assert.NotEqual(Color.MistyRose, mari.DefaultCellStyle.BackColor); // the accountant's own choice
            Assert.Matches("rida ei saa koostada: .*NOT_IN_PERIOD", form.UndeclaredTips.TextOf(jaan));
            Assert.Contains("märge \"Deklareeri\" on maha võetud", form.UndeclaredTips.TextOf(mari), StringComparison.Ordinal);
            Assert.Null(form.UndeclaredTips.TextOf(unmatched));
            Assert.Contains("NOT_IN_PERIOD", jaan.Cells["problems"].Value?.ToString(), StringComparison.Ordinal);

            form.PersonsSearch.Text = "NOT_IN_PERIOD"; // the search finds it
            Ui.Pump();
            Assert.Equal([jaan], persons.Rows.Cast<DataGridViewRow>().Where(r => r.Visible));
            form.PersonsSearch.Text = "";
        }));
    }

    [Fact]
    public void A_lookup_run_shows_its_progress_and_leaves_no_wait_cursor_on_the_grids()
    {
        var residency = new GatedResidency();
        using var fixture = new WindowFixture()
        {
            Services = path => new AppServices(path, residency, new NoPension(), new LookupOptions(TimeSpan.Zero)),
        };
        Ui.Run(() => fixture.Drive(form =>
        {
            Assert.False(form.ProgressBar.Visible || form.ProgressLabel.Visible || form.StopButton.Visible);
            form.LookupButton.PerformClick();
            Ui.Until(() => residency.Waiting, what: "the first query");
            Assert.True(form.IsBusy);
            Assert.True(form.StopButton.Visible);
            Assert.False(form.DeclareTotalsItem.Enabled || form.DeclareEachItem.Enabled); // the cut is never switched under a run
            Assert.Equal("Päringud: 0 / 2", form.ProgressLabel.Text);
            Assert.Equal((0, 2), (form.ProgressBar.Value, form.ProgressBar.Maximum));

            // the mouse over the headers' dividers while the window waits: a grid takes the wait cursor as its own
            var persons = form.PersonsGrid;
            var onMouseMove = typeof(Control).GetMethod("OnMouseMove", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            foreach (var grid in new[] { persons, form.PaymentsGrid })
            {
                for (var x = 0; x < grid.Width; x++)
                {
                    for (var y = 0; y < Math.Min(grid.Height, 80); y += 3)
                    {
                        onMouseMove.Invoke(grid, [new MouseEventArgs(MouseButtons.None, 0, x, y, 0)]);
                    }
                }
            }

            residency.Release();
            Ui.Until(() => !form.IsBusy, what: "the run to end");
            Assert.Equal(2, residency.Asked);
            Assert.False(form.ProgressBar.Visible || form.ProgressLabel.Visible || form.StopButton.Visible);
            Assert.Equal(0, form.ProgressBar.Value);
            Assert.True(form.DeclareTotalsItem.Enabled && form.DeclareEachItem.Enabled);
            Assert.False(form.UseWaitCursor);
            Assert.Equal(Cursors.Default, persons.Cursor);
            Assert.Equal(Cursors.Default, form.PaymentsGrid.Cursor);
        }));
    }

    [Fact]
    public void The_cards_check_asks_the_registers_afresh_and_offers_the_registers_word_over_a_fact_set_by_hand()
    {
        var residency = new CountingResidency();
        var pension = new SaidPension { Rate = 0.02m };
        var questions = new List<string>();
        var take = false;
        using var fixture = new WindowFixture()
        {
            Services = path => new AppServices(path, residency, pension, new LookupOptions(TimeSpan.Zero)),
        };
        Ui.Run(() => fixture.Drive(form =>
        {
            Person MariNow() => StoredMari(form);
            // the fixture sets her facts by hand; here they start as the registers' to give
            form.Services!.Store.UpdatePerson(MariNow() with { ResidencySource = LookupSource.Unknown, FundedPensionMember = null, PensionRate = null, PensionSource = LookupSource.Unknown });
            form.LookupButton.PerformClick();
            Ui.Until(() => !form.IsBusy && MariNow().PensionSource == LookupSource.Pensionikeskus, what: "the month's run");
            var asked = (residency.Asked, pension.Asked);

            // every person's line names the day that person was checked for; the closing line names no day of its own
            Ui.Pump();
            var logged = form.LogList.Items.Cast<object>().Select(i => i.ToString()!).ToList();
            var runner = new LookupRunner(form.Services!.Store, residency, pension);
            Assert.Contains(logged, l => l.Contains($"(seisuga {UiText.Day(runner.ControlDateFor(MariNow(), form.View!.Period))})", StringComparison.Ordinal));
            Assert.Equal(2, logged.Count(l => l.Contains("(seisuga ", StringComparison.Ordinal)));
            Assert.DoesNotContain(logged, l => l.Contains("kontrollkuupäev", StringComparison.Ordinal));
            Assert.Contains(logged, l => l.Contains($"{UiText.RunLookups}: {UiText.Done}", StringComparison.Ordinal) && l.EndsWith("päringut", StringComparison.Ordinal));

            // the month's run again reuses; the card's button asks both registers now
            form.LookupButton.PerformClick();
            Ui.Until(() => !form.IsBusy, what: "the second run");
            Ui.Pump();
            Assert.Equal(asked, (residency.Asked, pension.Asked));
            // a run that asks nobody still writes every person's line with their day, once each, before its closing line
            var again = form.LogList.Items.Cast<object>().Select(i => i.ToString()!).Skip(logged.Count).ToList();
            Assert.Equal(2, again.Count(l => l.Contains("(seisuga ", StringComparison.Ordinal) && l.Contains("reused", StringComparison.Ordinal)));
            Assert.EndsWith("0 päringut", again[^1], StringComparison.Ordinal);
            pension.Rate = 0.04m; // Pensionikeskus corrects itself
            Mari(form);
            form.PersonEditor.LookUpButton.PerformClick();
            Ui.Until(() => !form.IsBusy && MariNow().PensionRate == 0.04m, what: "the card's check");
            Assert.Equal((asked.Item1 + 1, asked.Item2 + 1), (residency.Asked, pension.Asked));
            Assert.Empty(questions); // nothing set by hand: nothing to ask

            // the rate set by hand, the register saying otherwise: the question is put; No keeps the hand's word
            form.Services!.Store.UpdatePerson(MariNow() with { PensionRate = 0.06m, PensionSource = LookupSource.Manual });
            Mari(form);
            form.PersonEditor.LookUpButton.PerformClick();
            Ui.Until(() => !form.IsBusy && questions.Count == 1, what: "the question");
            Ui.Pump();
            Assert.Contains("4 %", questions[0], StringComparison.Ordinal);
            Assert.Contains("6 %", questions[0], StringComparison.Ordinal);
            Assert.Equal((0.06m, LookupSource.Manual), (MariNow().PensionRate, MariNow().PensionSource));

            // Yes: the register's answer is the fact, without another query for it
            take = true;
            var before = (residency.Asked, pension.Asked);
            Mari(form);
            form.PersonEditor.LookUpButton.PerformClick();
            Ui.Until(() => !form.IsBusy && MariNow().PensionSource == LookupSource.Pensionikeskus, what: "the hand-back");
            Ui.Pump();
            Assert.Equal(2, questions.Count);
            Assert.Equal(0.04m, MariNow().PensionRate);
            Assert.Equal((before.Item1 + 1, before.Item2 + 1), (residency.Asked, pension.Asked)); // asked afresh once; the hand-back reused

            // both facts by hand and both spoken against: the residency is asked alone and first, then the pension against
            // the answer that applies once the residency is the register's
            form.Services!.Store.UpdatePerson(MariNow() with { Residency = Residency.NonResident, ResidencySource = LookupSource.Manual, FundedPensionMember = true, PensionRate = 0.06m, PensionSource = LookupSource.Manual });
            questions.Clear();
            Mari(form);
            form.PersonEditor.LookUpButton.PerformClick();
            Ui.Until(() => !form.IsBusy && MariNow().PensionSource == LookupSource.Pensionikeskus, what: "the two hand-backs");
            Ui.Pump();
            Assert.Equal(2, questions.Count);
            Assert.Contains("Residentsus", questions[0], StringComparison.Ordinal);
            Assert.DoesNotContain("II sammas", questions[0], StringComparison.Ordinal);
            Assert.Contains("Pensionikeskus", questions[1], StringComparison.Ordinal);
            Assert.Contains("4 %", questions[1], StringComparison.Ordinal);
            Assert.Equal((Residency.Resident, LookupSource.Emta, 0.04m), (MariNow().Residency, MariNow().ResidencySource, MariNow().PensionRate));
            Assert.Empty(fixture.Failures);
        }, confirm: (question, title) =>
        {
            if (title != UiText.LookUp)
            {
                return true;
            }

            questions.Add(question);
            return take;
        }));
    }

    private sealed class CountingResidency : IResidencyLookup
    {
        private int _asked;

        public int Asked => _asked;

        public async Task<LookupResult<ResidencyFinding>> QueryAsync(string personalCode, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _asked);
            await Task.Delay(1, cancellationToken); // an answer takes its time, as a site's does: the run is under way when it comes
            return LookupResult<ResidencyFinding>.Found(new ResidencyFinding(new DateOnly(2026, 11, 4), [], []), null);
        }
    }

    private sealed class SaidPension : IPensionLookup
    {
        private int _asked;

        public int Asked => _asked;

        public decimal Rate { get; set; }

        public async Task<LookupResult<PensionFinding>> QueryAsync(string personalCode, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _asked);
            await Task.Delay(1, cancellationToken);
            return LookupResult<PensionFinding>.Found(new PensionFinding(PensionStatus.Joined, new DateOnly(2019, 1, 1), null, [new RatePeriod(new DateOnly(2019, 1, 1), null, Rate, "x")]), null);
        }
    }

    [Fact]
    public void An_amount_refused_on_a_row_never_split_speaks_of_no_parts()
    {
        var refusal = new ReviewException(ReviewWorkspace.SplitOwnShare, "x", 100m);
        var unsplit = UiText.AmountRefused(refusal, unsplit: true)!;
        Assert.Contains("ei saa suurendada ega nulliks muuta", unsplit, StringComparison.Ordinal);
        Assert.DoesNotContain("vähendage mõne osa", unsplit, StringComparison.Ordinal); // there are none to lower
        Assert.Contains("vähendage mõne osa", UiText.AmountRefused(refusal)!, StringComparison.Ordinal); // a row with parts: as before
    }

    [Fact]
    public void Removing_a_line_added_by_hand_asks_first_and_a_no_keeps_it()
    {
        using var fixture = new WindowFixture(prepare: (store, company, period, _) =>
            new ReviewWorkspace(store).AddHandLine(period.Id, store.FindPerson(company.Id, SatellesTSD.Tests.ReviewFixtures.MariCode)!.Id, new DateOnly(2026, 10, 15), 120m, "Mari Maasikas", "haigusleht", "Haigusleht"));
        var asked = new List<string>();
        var answer = false;
        Ui.Run(() => fixture.Drive(form =>
        {
            int HandLines() => form.Services!.Store.ReviewLines(form.View!.Period.Id).Count(l => l.IsByHand);
            void PickHandLine()
            {
                Mari(form);
                var row = form.PaymentsGrid.Rows.Cast<DataGridViewRow>().Single(r => r.Tag is ReviewPayment { IsByHand: true });
                form.PaymentsGrid.CurrentCell = row.Cells[0];
                Ui.Pump();
            }

            Assert.Equal(1, HandLines());
            PickHandLine();
            Assert.True(form.RemoveLineButton.Enabled);
            form.RemoveLineButton.PerformClick();
            Ui.Pump();
            Assert.Equal(UiText.RemoveLineConfirm, Assert.Single(asked));
            Assert.Equal(1, HandLines()); // No: the line stays

            answer = true;
            PickHandLine();
            form.RemoveLineButton.PerformClick();
            Ui.Pump();
            Assert.Equal(2, asked.Count);
            Assert.Equal(0, HandLines());
            Assert.Empty(fixture.Failures);
        }, confirm: (question, title) =>
        {
            if (title != UiText.RemoveLine)
            {
                return true;
            }

            asked.Add(question);
            return answer;
        }));
    }

    [Fact]
    public void Closing_the_window_in_the_middle_of_a_run_is_refused_with_a_notice()
    {
        var residency = new GatedResidency();
        using var fixture = new WindowFixture()
        {
            Services = path => new AppServices(path, residency, new NoPension(), new LookupOptions(TimeSpan.Zero)),
        };
        Ui.Run(() => fixture.Drive(form =>
        {
            var notices = new List<string>();
            form.Informing = notices.Add;
            form.LookupButton.PerformClick();
            Ui.Until(() => residency.Waiting, what: "the first query");

            form.Close();
            Ui.Pump();
            Assert.Equal(UiText.BusyClosing, Assert.Single(notices));
            Assert.True(form.Visible); // the window stays; the run is told to stop
            residency.Release();
            Ui.Until(() => !form.IsBusy, what: "the run's end");
            Assert.Empty(fixture.Failures);
        }));
    }

    [Fact]
    public void The_report_goes_to_the_folder_named_and_the_question_about_opening_it_is_asked_after()
    {
        using var fixture = new WindowFixture();
        var asked = new List<string>();
        Ui.Run(() => fixture.Drive(form =>
        {
            Directory.CreateDirectory(fixture.ExportFolder);
            var descriptions = new List<string>();
            string? folder = null;
            form.ChoosingFolder = description =>
            {
                descriptions.Add(description);
                return folder;
            };

            // the folder dialog cancelled: nothing is written, nothing asked
            form.ReportButton.PerformClick();
            Ui.Until(() => !form.IsBusy && descriptions.Count == 1, what: "the cancelled export");
            Ui.Pump();
            Assert.Equal(UiText.ReportFolder, descriptions[0]);
            Assert.Empty(Directory.GetFiles(fixture.ExportFolder, "*.xlsx"));
            Assert.Empty(asked);

            // a folder named: the workbook is written there, and only then the question is put (answered No: no Explorer window)
            folder = fixture.ExportFolder;
            form.ReportButton.PerformClick();
            Ui.Until(() => !form.IsBusy && asked.Count == 1, what: "the export");
            var written = Assert.Single(Directory.GetFiles(fixture.ExportFolder, "*.xlsx"));
            Assert.Contains(written, asked[0], StringComparison.Ordinal);
            Assert.Contains(UiText.OpenFolderQuestion, asked[0], StringComparison.Ordinal);
            Assert.Empty(fixture.Failures);
        }, confirm: (question, title) =>
        {
            if (title != UiText.ExportReport)
            {
                return true;
            }

            asked.Add(question);
            return false;
        }));
    }

    [Fact]
    public void Marking_a_month_confirmed_with_lines_still_waiting_asks_first_and_a_no_leaves_it_uploaded()
    {
        using var fixture = new WindowFixture();
        var asked = new List<string>();
        var answer = false;
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
            Assert.Equal(PeriodState.Uploaded, form.View!.Period.State);

            // the line still waits for its answer: the question is put; No leaves the month as it was
            Assert.True(panel.ConfirmedButton.Enabled);
            panel.ConfirmedButton.PerformClick();
            Ui.Pump();
            Assert.Contains(UiText.ConfirmForce, Assert.Single(asked), StringComparison.Ordinal);
            Assert.Equal(PeriodState.Uploaded, form.View!.Period.State);

            answer = true;
            panel.ConfirmedButton.PerformClick();
            Ui.Pump();
            Assert.Equal(2, asked.Count);
            Assert.Equal(PeriodState.Confirmed, form.View!.Period.State);
            Assert.Empty(fixture.Failures);
        }, confirm: (question, title) =>
        {
            if (title != UiText.MarkConfirmed)
            {
                return true;
            }

            asked.Add(question);
            return answer;
        }));
    }

    [Fact]
    public void A_field_that_does_not_read_is_complained_about_through_the_seam_on_the_card_and_in_a_dialog()
    {
        using var fixture = new WindowFixture();
        Ui.Run(() => fixture.Drive(form =>
        {
            // the card: a figure that is no number is said, nothing is saved, the text stays for correcting
            var complaints = new List<string>();
            form.PersonEditor.Complaining = complaints.Add;
            Mari(form);
            var before = StoredMari(form);
            form.PersonEditor.ExemptionBox.Text = "abc";
            form.PersonEditor.SaveButton.PerformClick();
            Ui.Pump();
            Assert.Equal(UiText.NotANumber, Assert.Single(complaints));
            Assert.Equal(before, StoredMari(form));
            Assert.Equal("abc", form.PersonEditor.ExemptionBox.Text);
            Assert.Empty(fixture.Failures);
            form.PersonEditor.ExemptionBox.Text = 700m.ToString("0.00", System.Globalization.CultureInfo.CurrentCulture);

            // a dialog of fields: an empty name is said, and the dialog stays open
            using var dialog = new PersonDialog { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000) };
            var said = new List<string>();
            dialog.Complaining = said.Add;
            dialog.Show();
            ((Button)dialog.AcceptButton!).PerformClick();
            Assert.Single(said);
            Assert.Equal(DialogResult.None, dialog.DialogResult);
            dialog.Close();
        }));
    }

    /// <summary>EMTA faked: every query waits until the test lets them go, so the window is seen mid-run.</summary>
    private sealed class GatedResidency : IResidencyLookup
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _asked;
        private volatile bool _waiting;

        public bool Waiting => _waiting;

        public int Asked => _asked;

        public void Release() => _gate.TrySetResult();

        public async Task<LookupResult<ResidencyFinding>> QueryAsync(string personalCode, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _asked);
            _waiting = true;
            await _gate.Task.WaitAsync(cancellationToken);
            return LookupResult<ResidencyFinding>.Found(new ResidencyFinding(new DateOnly(2026, 11, 4), [], []), null);
        }
    }

    private sealed class NoPension : IPensionLookup
    {
        public Task<LookupResult<PensionFinding>> QueryAsync(string personalCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(LookupResult<PensionFinding>.Found(new PensionFinding(PensionStatus.NotJoined, null, null, []), null));
    }

    [Fact]
    public void The_declaration_tab_adds_up_the_net_and_the_taxes_to_pay_under_the_lines()
    {
        using var fixture = new WindowFixture(prepare: (store, company, period, _) =>
            new ReviewWorkspace(store).AddHandLine(period.Id, store.FindPerson(company.Id, SatellesTSD.Tests.ReviewFixtures.MariCode)!.Id, new DateOnly(2026, 10, 15), 120m, "Mari Maasikas", "haigusleht", "Haigusleht"));
        Ui.Run(() => fixture.Drive(form =>
        {
            form.Tabs.SelectedIndex = 1;
            Ui.Pump();
            var panel = form.Declaration;
            Assert.Equal("", panel.TotalsLabel.Text); // nothing built yet
            panel.BuildButton.PerformClick();
            Ui.Until(() => !form.IsBusy && panel.LinesGrid.Rows.Count == 2, what: "the build");

            var totals = DeclarationTotals.Of(form.Services!.Store.Payments(form.View!.Period.Id));
            Assert.Equal(1025.92m, totals.Net);
            Assert.Equal(totals.Net, panel.LinesGrid.Rows.Cast<DataGridViewRow>().Sum(r => (decimal)r.Cells["net"].Value!)); // the column adds up to the total
            Assert.Equal(UiText.DeclarationTotalsLine(totals, null), panel.TotalsLabel.Text);
            Assert.Equal($"NETO KOKKU: {UiText.Money(1025.92m)}    MAKSUD KOKKU: {UiText.Money(totals.Taxes)}", panel.TotalsLabel.Text);
            Assert.StartsWith($"MAKSUD KOKKU: {UiText.Money(totals.Taxes)} = TM {UiText.Money(totals.IncomeTax)} + SM {UiText.Money(totals.SocialTax)} + TK ", panel.TotalsTip, StringComparison.Ordinal);
            Assert.Contains("Arvestatud 2 rida", panel.TotalsTip, StringComparison.Ordinal);

            // the search leaves one line shown: its own sums beside
            var handSource = panel.LinesGrid.Rows.Cast<DataGridViewRow>().Single(r => (decimal)r.Cells["net"].Value! == 120m).Cells["source"].Value?.ToString();
            var salarySource = panel.LinesGrid.Rows.Cast<DataGridViewRow>().Single(r => (decimal)r.Cells["net"].Value! != 120m).Cells["source"].Value?.ToString();
            Assert.False(string.IsNullOrEmpty(handSource) || salarySource!.Contains(handSource, StringComparison.OrdinalIgnoreCase));
            panel.LinesSearch.Text = handSource;
            Ui.Pump();
            var shownRow = Assert.Single(panel.LinesGrid.Rows.Cast<DataGridViewRow>(), r => r.Visible);
            var shown = DeclarationTotals.Of([((DeclaredLine)shownRow.Tag!).Payment]);
            Assert.EndsWith($"(näidatud read: neto {UiText.Money(120m)}, maksud {UiText.Money(shown.Taxes)})", panel.TotalsLabel.Text, StringComparison.Ordinal);
            panel.LinesSearch.Text = "";
            Ui.Pump();
            Assert.Equal(UiText.DeclarationTotalsLine(totals, null), panel.TotalsLabel.Text);
        }));
    }

    [Fact]
    public void A_saved_place_no_screen_shows_opens_centred_with_its_size_and_a_place_half_off_its_screen_is_moved_in()
    {
        Rectangle[] screens = [new(0, 0, 1920, 1040), new(1920, 0, 1280, 984)];
        Assert.Null(WindowLayout.Fit(null, null, null, null, screens)); // nothing saved: the defaults
        Assert.Equal(((Point?)new Point(100, 50), new Size(1200, 800)), WindowLayout.Fit(100, 50, 1200, 800, screens));
        Assert.Equal(((Point?)new Point(2000, 20), new Size(1200, 800)), WindowLayout.Fit(2000, 20, 1200, 800, screens)); // the second screen
        // the second monitor unplugged: the size stays, the place is the screen's centre
        Assert.Equal(((Point?)null, new Size(1200, 800)), WindowLayout.Fit(2000, 20, 1200, 800, [screens[0]]));
        Assert.Equal(((Point?)null, new Size(1200, 800)), WindowLayout.Fit(-4000, -4000, 1200, 800, screens));
        // larger than the screen it is on: fitted to it, and moved in so that all of it shows
        Assert.Equal(((Point?)new Point(1920, 0), new Size(1280, 984)), WindowLayout.Fit(2500, 5, 3000, 2000, screens));
        Assert.Equal(((Point?)new Point(720, 240), new Size(1200, 800)), WindowLayout.Fit(1000, 300, 1200, 800, [screens[0]]));
        // never below the least size
        Assert.Equal(((Point?)new Point(10, 10), WindowLayout.MinimumSize), WindowLayout.Fit(10, 10, 300, 200, screens));
        Assert.Equal(((Point?)new Point(1120, 540), WindowLayout.MinimumSize), WindowLayout.Fit(1800, 900, 300, 200, [screens[0]]));
        // laid flush against the screen's edges: Windows counts the invisible resize border in, and it comes back as left
        Assert.Equal(((Point?)new Point(-7, 0), new Size(974, 1047)), WindowLayout.Fit(-7, 0, 974, 1047, [screens[0]], frame: 7));
        // across two screens: moved wholly onto the one that shows most of its title bar
        Assert.Equal(((Point?)new Point(1920, 100), new Size(1000, 700)), WindowLayout.Fit(1700, 100, 1000, 700, screens));
        Assert.Equal(((Point?)new Point(920, 100), new Size(1000, 700)), WindowLayout.Fit(1100, 100, 1000, 700, screens));

        // a splitter keeps both panels their least
        Assert.Equal(700, WindowLayout.Distance(700, 400, 1000, 6, 160));
        Assert.Equal(834, WindowLayout.Distance(990, 400, 1000, 6, 160));
        Assert.Equal(160, WindowLayout.Distance(20, 400, 1000, 6, 160));
        Assert.Equal(400, WindowLayout.Distance(null, 400, 1000, 6, 160));
    }

    [Fact]
    public void The_windows_size_the_cards_width_and_the_persons_grids_height_are_kept_for_the_next_session()
    {
        using var fixture = new WindowFixture();
        Ui.Run(() =>
        {
            fixture.Drive(form =>
            {
                form.Size = new Size(1000, 680); // small enough for any screen, far from the defaults
                Ui.Pump();
                form.OuterSplit.SplitterDistance = form.OuterSplit.Width - form.OuterSplit.SplitterWidth - 360; // the card 360 wide
                form.GridsSplit.SplitterDistance = 200;
                Ui.Pump();
            });

            var saved = AppSettings.Load(fixture.SettingsPath);
            Assert.Equal((1000, 680, 360, 200, false), (saved.WindowWidth, saved.WindowHeight, saved.CardWidth, saved.PersonsGridHeight, saved.WindowMaximized));

            // the next window: the fixture's place is off every screen, so the size alone is taken; the splitters as left
            fixture.Drive(form =>
            {
                Assert.Equal(new Size(1000, 680), form.Size);
                Assert.Equal(360, form.OuterSplit.Width - form.OuterSplit.SplitterDistance - form.OuterSplit.SplitterWidth);
                Assert.Equal(200, form.GridsSplit.SplitterDistance);

                // a longer status in the toolbar takes from the payments grid, never from the persons grid or the card
                form.Height += 40;
                Ui.Pump();
                Assert.Equal(200, form.GridsSplit.SplitterDistance);
                form.Height -= 40;

                // closed minimized: the size as it was, the splitters kept from before, not maximized
                form.WindowState = FormWindowState.Minimized;
                Ui.Pump();
            });

            saved = AppSettings.Load(fixture.SettingsPath);
            Assert.Equal((1000, 680, 360, 200, false), (saved.WindowWidth, saved.WindowHeight, saved.CardWidth, saved.PersonsGridHeight, saved.WindowMaximized));

            // a tall persons grid and a wide card, then the window shrunk to its least: both splitters stay in sight
            fixture.Drive(form =>
            {
                form.GridsSplit.SplitterDistance = form.GridsSplit.Height - form.GridsSplit.SplitterWidth - 90;
                form.OuterSplit.SplitterDistance = 170;
                Ui.Pump();
                form.Size = WindowLayout.MinimumSize;
                Ui.Pump();
                var grids = form.GridsSplit;
                Assert.True(grids.SplitterDistance <= grids.Height - grids.SplitterWidth - 80, $"{grids.SplitterDistance} of {grids.Height}");
                Assert.True(form.OuterSplit.SplitterDistance >= 160, $"{form.OuterSplit.SplitterDistance} of {form.OuterSplit.Width}");
            });
        });
    }

    [Fact]
    public void A_window_snapped_against_the_screens_edge_comes_back_as_it_was_left_with_the_systems_own_frame()
    {
        Assert.True(WindowLayout.InvisibleFrame >= SystemInformation.HorizontalResizeBorderThickness);
        var frame = WindowLayout.InvisibleFrame;
        Rectangle[] screen = [new(0, 0, 1920, 1040)];
        // snapped to the left half and to the right half: the invisible border reaches past the working area
        Assert.Equal(((Point?)new Point(-7, 0), new Size(974, 1047)), WindowLayout.Fit(-7, 0, 974, 1047, screen, frame));
        Assert.Equal(((Point?)new Point(953, 0), new Size(974, 1047)), WindowLayout.Fit(953, 0, 974, 1047, screen, frame));
    }
}
