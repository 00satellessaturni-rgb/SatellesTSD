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
            Assert.Equal(["Valitud isik", "Kõik", "Palgad", "Dividendid", "Üür", "Kommunaalkulud", "Kompensatsioon", "Haigusleht", "Laenud", "TÖR-väline palgad", "Trahvid", "Tundmatu kategooria"], tabs.TabPages.Cast<TabPage>().Select(p => p.Text));
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
            tabs.SelectedIndex = tabs.TabPages.Count - 1;
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
            dialog.Close();
            Ui.Pump();
            Assert.False(dialog.Visible);
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

    private static void Mari(MainForm form)
    {
        form.PersonsGrid.CurrentCell = form.PersonsGrid.Rows[0].Cells[0];
        Ui.Pump();
    }

    private static Person StoredMari(MainForm form) => form.Services!.Store.FindPerson(form.View!.Period.CompanyId, SatellesTSD.Tests.ReviewFixtures.MariCode)!;
}
