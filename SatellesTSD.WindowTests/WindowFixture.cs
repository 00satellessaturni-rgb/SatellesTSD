using System.Runtime.ExceptionServices;
using SatellesTSD.Core.Declare;
using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Review;
using SatellesTSD.Core.Store;
using SatellesTSD.Infrastructure;
using SatellesTSD.Tests;

namespace SatellesTSD.WindowTests;

/// <summary>A temporary folder with a file store built as the probe builds one — the review fixture's workbook
/// (fake data) imported and matched, the first person a confirmed resident outside the II pillar with the exemption
/// applied and the minimum off — and a settings file naming it with the company and the month, so the window opens
/// on it without a dialog. The store is closed before the window opens (the window's store is one connection).</summary>
internal sealed class WindowFixture : IDisposable
{
    public const string RegistryCode = "10235688";

    /// <param name="prepare">More to do in the store before the window opens: the store, the company, the period, and
    /// a folder inside the fixture's for files that go out.</param>
    public WindowFixture(Action<SqliteStore, Company, Period, string>? prepare = null)
    {
        Folder = Path.Combine(Path.GetTempPath(), $"satellestsd-window-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Folder);
        StorePath = Path.Combine(Folder, "smoke.db");
        SettingsPath = Path.Combine(Folder, "settings.json");
        ExportFolder = Path.Combine(Folder, "export");
        using (var store = SqliteStore.Open(StorePath))
        {
            var company = store.AddCompany(RegistryCode, "Näidis OÜ");
            var period = store.GetOrCreatePeriod(company.Id, 2026, 10);
            var bytes = ReviewFixtures.Workbook();
            var file = store.AddImport(company.Id, ReviewFixtures.Importer.Read("palgad.xlsx", bytes), bytes);
            store.LinkImport(period.Id, file.Id);
            new MatchRunner(store).Run(period.Id);
            var workspace = new ReviewWorkspace(store);
            var mari = workspace.Load(period.Id).Persons[0].Person!;
            workspace.SavePerson(mari with { FundedPensionMember = false, BasicExemptionApplied = true, SocialTaxMinimumApplies = false }, confirmResidency: true);
            prepare?.Invoke(store, company, period, ExportFolder);
        }

        new AppSettings { StorePath = StorePath, LastCompany = RegistryCode, LastYear = 2026, LastMonth = 10 }.Save(SettingsPath);
    }

    public string Folder { get; }

    public string StorePath { get; }

    public string SettingsPath { get; }

    public string ExportFolder { get; }

    /// <summary>What the window would have shown as a failure message box.</summary>
    public List<Exception> Failures { get; } = [];

    /// <summary>Opens the window on the fixture's store, runs the test's body on it, and closes it — the body's own
    /// failure is what the test reports, not the closing's; the window's collected failures fail the test too.</summary>
    public void Drive(Action<MainForm> body, Func<string, string, bool>? confirm = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        var form = Open(confirm);
        Exception? first = null;
        try
        {
            body(form);
        }
        catch (Exception e)
        {
            first = e;
        }

        try
        {
            Close(form);
        }
        catch (Exception e)
        {
            first ??= e;
        }

        if (first is not null)
        {
            ExceptionDispatchInfo.Capture(first).Throw();
        }
    }

    /// <summary>The window on the fixture's store, shown off-screen, its dialogs answered by the test. A window that
    /// could not open its store fails here, with the window's own reason.</summary>
    public MainForm Open(Func<string, string, bool>? confirm = null)
    {
        var form = new MainForm(SettingsPath, AppSettings.Load(SettingsPath), StorePath)
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-4000, -4000),
            Confirming = confirm ?? ((_, _) => true),
        };
        form.Failing = e => Failures.Add(e);
        form.Show();
        if (Failures.Count > 0 || form.View is null)
        {
            var why = Failures.Count > 0 ? string.Join(" | ", Failures.Select(e => e.Message)) : "no view after the store was opened";
            form.Close();
            form.Dispose();
            throw new InvalidOperationException("The window did not open on the store: " + why);
        }

        Ui.Until(() => !form.IsBusy, what: "the window to go idle after opening");
        return form;
    }

    /// <summary>Closes the window (so the store is released), disposes it, and fails on anything the window would
    /// have shown as a failure. The exception carries no inner one: the test thread sees the exception itself.</summary>
    public void Close(MainForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        Ui.Until(() => !form.IsBusy, what: "the window to go idle before closing");
        form.Close();
        Ui.Pump();
        try
        {
            form.Dispose();
        }
        catch (InvalidOperationException e)
        {
            Failures.Add(e); // a control left mid-way through a handle creation by an earlier failure
        }

        if (Failures.Count > 0)
        {
            throw new InvalidOperationException("The window failed: " + string.Join(" | ", Failures.Select(e => e.ToString())));
        }
    }

    /// <summary>Builds the lines, exports and marks the file uploaded at store level (no dialog), the way the owner's
    /// first upload leaves a period.</summary>
    public static void ExportAndUpload(SqliteStore store, Period period, string folder)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(period);
        new PeriodDeclarer(store).Build(period.Id);
        var submitter = Submitter(store);
        submitter.Export(period.Id, folder);
        submitter.MarkUploaded(period.Id);
    }

    public static PeriodSubmitter Submitter(SqliteStore store) => new(store, Path.Combine(AppContext.BaseDirectory, "schemas", "apa-tsd"));

    public void Dispose()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (Directory.Exists(Folder))
                {
                    Directory.Delete(Folder, true);
                }

                return;
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(100);
            }
        }
    }
}
