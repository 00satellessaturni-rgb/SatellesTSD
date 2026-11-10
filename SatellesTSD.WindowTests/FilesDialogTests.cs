using SatellesTSD.Tests;

namespace SatellesTSD.WindowTests;

/// <summary>The period's files dialog: several files, or all, taken out of the period or the store at once.</summary>
public class FilesDialogTests
{
    private static WindowFixture ThreeFiles(bool novemberListsOne = false) => new((store, company, period, _) =>
    {
        foreach (var salary in new[] { 111m, 222m })
        {
            var bytes = ReviewFixtures.Workbook(salary);
            var file = store.AddImport(company.Id, ReviewFixtures.Importer.Read($"extra-{salary:0}.xlsx", bytes), bytes);
            store.LinkImport(period.Id, file.Id);
            if (novemberListsOne && salary == 111m)
            {
                store.LinkImport(store.GetOrCreatePeriod(company.Id, 2026, 11).Id, file.Id);
            }
        }
    });

    private static FilesDialog OpenFiles(MainForm form, List<string> questions, List<string> warnings)
    {
        var dialog = new FilesDialog(form.Services!, form.View!, _ => { })
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-4000, -4000),
        };
        dialog.Confirming = (text, _) => { questions.Add(text); return true; };
        dialog.Warning = (text, _) => warnings.Add(text);
        dialog.Show(form);
        Ui.Pump();
        return dialog;
    }

    [Fact]
    public void The_files_dialog_takes_every_file_out_of_the_period_and_then_out_of_the_store_each_under_one_question()
    {
        using var fixture = ThreeFiles();
        Ui.Run(() => fixture.Drive(form =>
        {
            var (questions, warnings) = (new List<string>(), new List<string>());
            var period = form.View!.Period;
            using (var dialog = OpenFiles(form, questions, warnings))
            {
                Assert.Equal(3, dialog.List.Items.Count);
                Assert.False(dialog.UnlinkButton.Enabled);
                dialog.SelectAllButton.PerformClick();
                Assert.Equal(3, dialog.List.SelectedItems.Count);
                Assert.True(dialog.UnlinkButton.Enabled && dialog.RemoveButton.Enabled);
                Assert.False(dialog.RereadButton.Enabled); // one file at a time

                // out of the period: one question for the three, the run's second word once (it took the first file)
                dialog.UnlinkButton.PerformClick();
                Assert.Equal([UiText.UnlinkFilesConfirm(3), UiText.UnlinkClearsRun], questions);
                Assert.Empty(warnings);
                Assert.Empty(form.Services!.Store.PeriodImports(period.Id));
                Assert.Null(form.Services.Store.LoadMatchResult(period.Id));
                Assert.Equal(3, dialog.List.Items.Count); // listed now as the company's unlisted files

                // and out of the store: one question more
                questions.Clear();
                dialog.SelectAllButton.PerformClick();
                Assert.False(dialog.UnlinkButton.Enabled); // none of them is the period's
                dialog.RemoveButton.PerformClick();
                Assert.Equal([UiText.RemoveFilesConfirm(3)], questions);
                Assert.Empty(warnings);
                Assert.Empty(form.Services.Store.Imports(period.CompanyId));
                Assert.Equal(0, dialog.List.Items.Count);
                Assert.False(dialog.SelectAllButton.Enabled);
            }
        }));
    }

    [Fact]
    public void Removing_every_file_takes_out_those_it_may_and_tells_the_one_another_month_keeps()
    {
        using var fixture = ThreeFiles(novemberListsOne: true);
        Ui.Run(() => fixture.Drive(form =>
        {
            var (questions, warnings) = (new List<string>(), new List<string>());
            var period = form.View!.Period;
            using var dialog = OpenFiles(form, questions, warnings);
            dialog.SelectAllButton.PerformClick();
            dialog.RemoveButton.PerformClick();
            Assert.Equal([UiText.RemoveFilesConfirm(3), UiText.UnlinkClearsRun], questions);
            var warning = Assert.Single(warnings);
            Assert.Contains("extra-111.xlsx", warning);
            Assert.Equal(["extra-111.xlsx"], form.Services!.Store.Imports(period.CompanyId).Select(f => f.FileName));
            Assert.Equal(["extra-111.xlsx"], form.Services.Store.PeriodImports(period.Id).Select(f => f.FileName)); // refused whole: still October's too
        }));
    }

    [Fact]
    public void Declining_the_runs_second_word_leaves_the_run_and_the_files_not_yet_done()
    {
        using var fixture = ThreeFiles();
        Ui.Run(() => fixture.Drive(form =>
        {
            var (questions, warnings) = (new List<string>(), new List<string>());
            var period = form.View!.Period;
            using var dialog = OpenFiles(form, questions, warnings);
            dialog.Confirming = (text, _) => { questions.Add(text); return text != UiText.UnlinkClearsRun; };
            dialog.SelectAllButton.PerformClick();
            dialog.UnlinkButton.PerformClick();
            Assert.NotNull(form.Services!.Store.LoadMatchResult(period.Id));
            Assert.Equal(3, form.Services.Store.PeriodImports(period.Id).Count); // the run's file came first: nothing went
            Assert.Empty(warnings);
        }));
    }
}
