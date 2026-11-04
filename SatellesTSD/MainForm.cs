using System.Globalization;
using SatellesTSD.Core.Declare;
using SatellesTSD.Core.Lookups;
using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Report;
using SatellesTSD.Core.Review;
using SatellesTSD.Core.Store;
using SatellesTSD.Core.Tax;
using SatellesTSD.Infrastructure;
using SatellesTSD.Infrastructure.Updates;
using ReleaseState = SatellesTSD.Infrastructure.Updates.UpdateState;

namespace SatellesTSD;

/// <summary>The main window — the workbook's "Run main" and its review in one place: the store, the company and the
/// period on top; the persons grid (after the Vahearuanne) over the selected person's payments (the Otsingu kontroll's
/// rows with the accountant's edits); the person panel at the side; what the runs said at the bottom.</summary>
public sealed class MainForm : Form
{
    private const string ColCategory = "category";
    private const string ColType = "type";
    private const string ColDeclare = "declare";
    private const string ColPerson = "person";
    private const string ColNote = "note";

    /// <summary>The combos' "nobody" / "no category" value; "" is "back to the default".</summary>
    private const string None = "-";

    private readonly record struct PaymentKey(Guid? RowId, Guid? LineId);

    private readonly string _settingsPath;
    private readonly AppSettings _settings;
    private readonly string? _storeOverride;
    private AppServices? _services;
    private ReviewView? _view;
    private Period? _period;
    private CancellationTokenSource? _lookups;
    private bool _filling;
    private bool _busy;

    private readonly ToolStrip _toolbar = new() { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top, LayoutStyle = ToolStripLayoutStyle.Flow };
    private readonly ToolStripLabel _storeLabel = new(UiText.NoStore);
    private readonly ToolStripComboBox _companyBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = false, Width = 240 };
    private readonly ToolStripComboBox _yearBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = false, Width = 70 };
    private readonly ToolStripComboBox _monthBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = false, Width = 50 };
    private readonly ToolStripButton _openStoreButton = new(UiText.OpenStore);
    private readonly ToolStripButton _newStoreButton = new(UiText.NewStore);
    private readonly ToolStripButton _newCompanyButton = new(UiText.NewCompany);
    private readonly ToolStripButton _importButton = new(UiText.ImportFiles);
    private readonly ToolStripButton _filesButton = new(UiText.Files);
    private readonly ToolStripButton _matchButton = new(UiText.RunMatch);
    private readonly ToolStripButton _lookupButton = new(UiText.RunLookups);
    private readonly ToolStripButton _cancelButton = new(UiText.Cancel) { Enabled = false };
    private readonly ToolStripButton _addLineButton = new(UiText.AddLine);
    private readonly ToolStripButton _removeLineButton = new(UiText.RemoveLine) { Enabled = false };
    private readonly ToolStripButton _newPersonButton = new(UiText.NewPerson);
    private readonly ToolStripButton _calculatorButton = new(UiText.Calculator);
    private readonly ToolStripButton _reportButton = new(UiText.ExportReport);
    private readonly ToolStripButton _updateButton = new(UiText.CheckUpdates) { Alignment = ToolStripItemAlignment.Right };
    private UpdateCheck? _updateCheck;
    private bool _checkingUpdates;
    private bool _handedOver;
    private readonly ToolStripLabel _stateLabel = new("") { Font = new Font(Control.DefaultFont, FontStyle.Bold) };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly DeclarationPanel _declaration = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _personsGrid = Grid();
    private readonly DataGridView _paymentsGrid = Grid();
    private readonly PersonPanel _personPanel = new() { Dock = DockStyle.Fill };
    private readonly ListBox _log = new() { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
    private readonly SplitContainer _outer = new() { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
    private readonly SplitContainer _grids = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };

    /// <summary>The release version from the project file ("0.10.0"), as the title and the release folder name it.</summary>
    public static string AppVersion => typeof(MainForm).Assembly.GetName().Version is { } v ? v.ToString(3) : "0";

    public MainForm(string settingsPath, AppSettings settings, string? storeOverride)
    {
        _settingsPath = settingsPath;
        _settings = settings;
        _storeOverride = storeOverride;
        // the published program alone looks for updates: a build folder's copy or a test host's window never touches the share
        UpdatesEnabled = Environment.ProcessPath is { } exe && string.Equals(Path.GetFileName(exe), Updater.ExeName, StringComparison.OrdinalIgnoreCase)
            && typeof(MainForm).Assembly.Location.Length == 0;
        Text = TitleText();
        Size = new Size(1500, 900);
        StartPosition = FormStartPosition.CenterScreen;

        _toolbar.Items.AddRange(
        [
            new ToolStripLabel(UiText.Store), _storeLabel, _openStoreButton, _newStoreButton, new ToolStripSeparator(),
            new ToolStripLabel(UiText.Company), _companyBox, _newCompanyButton, new ToolStripSeparator(),
            new ToolStripLabel(UiText.Period), _yearBox, _monthBox, new ToolStripSeparator(),
            _importButton, _filesButton, _matchButton, _lookupButton, _cancelButton, new ToolStripSeparator(),
            _addLineButton, _removeLineButton, _newPersonButton, new ToolStripSeparator(), _reportButton, _calculatorButton, new ToolStripSeparator(), _stateLabel,
            _updateButton,
        ]);
        _updateButton.Click += async (_, _) => await CheckForUpdatesAsync(manual: true);

        _grids.Panel1.Controls.Add(_personsGrid);
        _grids.Panel2.Controls.Add(_paymentsGrid);
        var reviewTab = new TabPage(UiText.ReviewTab);
        reviewTab.Controls.Add(_grids);
        var declarationTab = new TabPage(UiText.DeclarationTab);
        declarationTab.Controls.Add(_declaration);
        _tabs.TabPages.Add(reviewTab);
        _tabs.TabPages.Add(declarationTab);
        _outer.Panel1.Controls.Add(_tabs);
        _outer.Panel2.Controls.Add(_personPanel);
        var logPanel = new Panel { Dock = DockStyle.Bottom, Height = 150 };
        logPanel.Controls.Add(_log);
        Controls.Add(_outer);
        Controls.Add(logPanel);
        Controls.Add(_toolbar);

        BuildPaymentColumns();

        _openStoreButton.Click += (_, _) => OpenStoreDialog(false);
        _newStoreButton.Click += (_, _) => OpenStoreDialog(true);
        _newCompanyButton.Click += (_, _) => NewCompany();
        _companyBox.SelectedIndexChanged += (_, _) => { if (!_filling) { FillPeriods(); ReloadView(); } };
        _yearBox.SelectedIndexChanged += (_, _) => { if (!_filling) { ReloadView(); } };
        _monthBox.SelectedIndexChanged += (_, _) => { if (!_filling) { ReloadView(); } };
        _importButton.Click += async (_, _) => await ImportAsync();
        _filesButton.Click += (_, _) => ShowFiles();
        _matchButton.Click += async (_, _) => await RunMatchAsync();
        _lookupButton.Click += async (_, _) => await RunLookupsAsync();
        _cancelButton.Click += (_, _) => _lookups?.Cancel();
        _addLineButton.Click += (_, _) => AddLine();
        _removeLineButton.Click += (_, _) => RemoveLine();
        _newPersonButton.Click += (_, _) => NewPerson();
        _personsGrid.SelectionChanged += (_, _) => { if (!_filling) { ShowSelectedPerson(); } };
        _paymentsGrid.SelectionChanged += (_, _) => { if (!_filling) { _removeLineButton.Enabled = Editable && !_busy && SelectedPayment is { IsByHand: true }; } };
        // a tick or a pick is a decision at once; a note is one when its editing ends
        _paymentsGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_paymentsGrid.IsCurrentCellDirty && _paymentsGrid.CurrentCell is DataGridViewCheckBoxCell or DataGridViewComboBoxCell)
            {
                _paymentsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        _paymentsGrid.CellValueChanged += (_, e) => OnPaymentEdited(e.RowIndex, e.ColumnIndex);
        _paymentsGrid.DataError += (_, e) => { e.ThrowException = false; };
        _personsGrid.DataError += (_, e) => { e.ThrowException = false; };
        _personPanel.SaveRequested += SavePerson;
        _personPanel.LookUpRequested += async person => await LookUpOneAsync(person);
        _calculatorButton.Click += (_, _) => { using var dialog = new CalculatorDialog(); dialog.ShowDialog(this); };
        _reportButton.Click += async (_, _) => await ExportReportAsync();
        _declaration.BuildRequested += async () => await BuildLinesAsync();
        _declaration.ExportRequested += async () => await ExportAsync();
        _declaration.WithdrawRequested += Withdraw;
        _declaration.UploadedRequested += () => Declare(s => s.Submitter.MarkUploaded(_view!.Period.Id), UiText.MarkUploaded);
        _declaration.FeedbackRequested += async submission => await ReadFeedbackAsync(submission);
        _declaration.ConfirmedRequested += MarkConfirmed;
        _declaration.ReopenRequested += () => { if (Confirm(UiText.ReopenConfirm, UiText.Reopen)) { Declare(s => s.Submitter.Reopen(_view!.Period.Id), UiText.Reopen); } };
        _declaration.ResolveRequested += (payment, onServer) => Resolve(payment, onServer);
        _declaration.HistoryRequested += ShowHistory;
        FormClosing += OnClosing;
    }

    // ---------------------------------------------------------------- the window tests' seams

    internal bool IsBusy => _busy;
    internal DataGridView PersonsGrid => _personsGrid;
    internal DataGridView PaymentsGrid => _paymentsGrid;
    internal TabControl Tabs => _tabs;
    internal DeclarationPanel Declaration => _declaration;
    internal PersonPanel PersonEditor => _personPanel;
    internal AppServices? Services => _services;
    internal ReviewView? View => _view;

    /// <summary>Answers a confirmation question in place of the message box (the window tests set it).</summary>
    internal Func<string, string, bool>? Confirming;

    /// <summary>Takes a failure in place of the message box (the window tests set it).</summary>
    internal Action<Exception>? Failing;

    internal void Reload() => ReloadView();

    /// <summary>Whether the window looks for updates in the release folder: only the published program does.</summary>
    internal bool UpdatesEnabled;

    // ---------------------------------------------------------------- updates from the release folder

    private string TitleText()
    {
        var title = $"{UiText.Title} v{AppVersion}";
        return _updateCheck is { State: ReleaseState.Available, Release: { } release } ? $"{title} — {UiText.UpdateAvailableTitle} v{release.Version.ToString(3)}" : title;
    }

    private static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, Updater.ExeName);

    /// <summary>The running version with every part, for the comparison (the title shows three).</summary>
    private static Version CurrentVersion => typeof(MainForm).Assembly.GetName().Version is { } v ? Updater.Normal(v) : new Version(0, 0, 0, 0);

    /// <summary>Looks in the release folder, off the UI thread with a timeout, and says what it found: in the title and
    /// on the button; at start the offer opens by itself when it may, by hand the answer is told either way. The copy
    /// that installed this one, if any, is told the window runs once the check is over.</summary>
    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (!UpdatesEnabled || _checkingUpdates)
        {
            StartupFinished(true);
            return;
        }

        _checkingUpdates = true;
        _updateButton.Text = UiText.CheckingUpdates;
        UpdateState();
        UpdateCheck result;
        try
        {
            var folder = Updater.ReleaseFolderFor(ExePath);
            result = await Updater.CheckAsync(folder, CurrentVersion);
        }
        catch (Exception e)
        {
            result = new UpdateCheck(ReleaseState.Failed, Updater.DefaultReleaseFolder, null, e.Message);
        }
        finally
        {
            _checkingUpdates = false;
        }

        if (IsDisposed)
        {
            return;
        }

        _updateCheck = result;
        ShowUpdateState();
        Log(result.State switch
        {
            ReleaseState.Available => $"{UiText.CheckUpdates}: {UiText.UpdateAvailableTitle} v{result.Release!.Version.ToString(3)}",
            ReleaseState.UpToDate => $"{UiText.CheckUpdates}: {UiText.UpToDate}",
            _ => $"{UiText.CheckUpdates}: {Words(result)} {result.Detail}",
        });
        StartupFinished(result.State != ReleaseState.Failed);

        if (manual)
        {
            switch (result.State)
            {
                case ReleaseState.Available:
                    OfferUpdate(fromStart: false);
                    break;
                case ReleaseState.UpToDate:
                    var elsewhere = !string.Equals(result.Folder, Updater.DefaultReleaseFolder, StringComparison.OrdinalIgnoreCase);
                    MessageBox.Show(this, UiText.UpToDate + (elsewhere ? $"\n\n{UiText.UpdateFolderOverride}\n{result.Folder}" : ""), UiText.UpdateTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
                default:
                    MessageBox.Show(this, $"{Words(result)}\n{result.Detail}\n\n{UiText.UpdateFolderLine} {result.Folder}", UiText.UpdateTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    break;
            }
        }
        else if (result.State == ReleaseState.Available && MayOfferAtStart(result.Release!))
        {
            OfferUpdate(fromStart: true);
        }
    }

    private static string Words(UpdateCheck result) => result.State switch
    {
        ReleaseState.NoRelease => UiText.NoRelease,
        ReleaseState.Unreachable => UiText.UpdatesUnreachable,
        ReleaseState.BadRelease => UiText.UpdatesBad,
        _ => UiText.UpdatesFailed,
    };

    /// <summary>Whether the check at start may open the update window by itself: only while this window is in front
    /// with no dialog of its own open and no run going, for a copy that is updated in place, and not for the build
    /// that already failed to start here — the title and the button still offer it in every case.</summary>
    private bool MayOfferAtStart(ReleaseManifest release) =>
        !_busy && ActiveForm == this && WindowState != FormWindowState.Minimized && OwnedForms.Length == 0
        && Updater.InstallBlockOf(ExePath, System.Diagnostics.Debugger.IsAttached) == InstallBlock.None && !Updater.FailedHere(ExePath, release);

    private void ShowUpdateState()
    {
        Text = TitleText();
        var available = _updateCheck is { State: ReleaseState.Available, Release: { } release };
        _updateButton.Text = available ? $"Uuenda v{_updateCheck!.Release!.Version.ToString(3)}…" : UiText.CheckUpdates;
        _updateButton.ToolTipText = _updateCheck is { State: not (ReleaseState.Available or ReleaseState.UpToDate) } failed ? $"{Words(failed)} {failed.Detail}" : "";
        UpdateState();
    }

    /// <summary>After an update the copy that installed this one waits for the signal; given once the window is up and
    /// its first check is over — or, when the check itself faulted, the window ends so that the old copy puts itself back.</summary>
    private static void StartupFinished(bool checkWorked)
    {
        if (Program.ReadySignal is not { } signal)
        {
            return;
        }

        if (!checkWorked)
        {
            Environment.Exit(Updater.FailedExitCode);
        }

        Updater.SignalReady(signal);
        Program.ReadySignal = null;
    }

    private void OfferUpdate(bool fromStart)
    {
        if (_busy || _updateCheck is not { State: ReleaseState.Available, Release: { } release })
        {
            return;
        }

        switch (Updater.InstallBlockOf(ExePath, System.Diagnostics.Debugger.IsAttached))
        {
            case InstallBlock.BuildFolder:
                MessageBox.Show(this, UiText.UpdateBlockedBuild, UiText.UpdateTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            case InstallBlock.NetworkDrive:
                MessageBox.Show(this, $"{UiText.UpdateBlockedNetwork}\n{ExePath}", UiText.UpdateTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
        }

        if (_personPanel.IsDirty)
        {
            ResolveDirtyPanel();
            ReloadView();
        }

        // the new version reads the settings and opens the store when it starts: both are let go of just before
        var storePath = _services?.StorePath;
        using var dialog = new UpdateDialog(ExePath, _updateCheck.Folder, release, CurrentVersion, fromStart, () =>
        {
            SaveSettings();
            _lookups?.Cancel();
            _services?.Dispose();
            _services = null;
            _view = null;
            _period = null;
        });
        dialog.ShowDialog(this);
        if (dialog.Started)
        {
            _handedOver = true; // the new version runs: this window closes without saving over what it may have changed
            Close();
            return;
        }

        if (_services is null && storePath is not null)
        {
            // the update did not go through: the store is taken up again — put back from its copy when the new
            // version had already upgraded it beyond this build
            OpenStore(storePath);
        }
    }

    /// <summary>A store this build cannot open because a newer one upgraded it: offers the copy that build left beside
    /// it before upgrading, and takes the store up again from it.</summary>
    private bool OfferStoreRestore(string path, Exception failure)
    {
        if (!SqliteStore.IsNewerThanThisBuild(failure) || SqliteStore.NewerBackupOf(path) is not { } backup)
        {
            return false;
        }

        var aside = SqliteStore.AsidePathFor(path);
        if (!Confirm($"{UiText.StoreNewerRestore}\n{backup}\n\n{UiText.StoreNewerRestoreQuestion}\n{aside}", UiText.Title))
        {
            return false;
        }

        try
        {
            SqliteStore.RestoreBackup(path, backup, aside);
            Log($"{UiText.StoreRestored}: {backup}");
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Fail(e);
            return false;
        }
    }

    private void Resolve(StoredPayment payment, bool onServer, Action? then = null)
    {
        if (Confirm(onServer ? UiText.ResolveAcceptedConfirm : UiText.ResolveRejectedConfirm, onServer ? UiText.ResolveAccepted : UiText.ResolveRejected))
        {
            Declare(s => s.Submitter.Resolve(payment.Id, onServer), $"{payment.LineNumber}: {(onServer ? UiText.ResolveAccepted : UiText.ResolveRejected)}");
            then?.Invoke();
        }
    }

    /// <summary>The history dialog of a key, wired to the window: a resolve asked there goes through the same
    /// confirmation and store call as the grid's, then the dialog reads the versions again.</summary>
    internal HistoryDialog CreateHistory(DeclaredLine line)
    {
        if (_services is null)
        {
            throw new InvalidOperationException("No store is open.");
        }

        var dialog = new HistoryDialog(_services, line);
        dialog.ResolveRequested += (version, onServer) => Resolve(version, onServer, dialog.Reload);
        return dialog;
    }

    private void ShowHistory(DeclaredLine line)
    {
        if (_services is null || _view is null || _busy)
        {
            return;
        }

        using var dialog = CreateHistory(line);
        dialog.ShowDialog(this);
        ReloadView();
    }

    // ---------------------------------------------------------------- the declaration

    private void PresentDeclaration()
    {
        if (_services is null || _view is null)
        {
            _declaration.Present(null, [], []);
            return;
        }

        try
        {
            _declaration.Present(_view, _services.Declarer.Lines(_view.Period.Id), _services.Store.Submissions(_view.Period.Id));
        }
        catch (Exception e)
        {
            Fail(e);
            _declaration.Present(_view, [], []);
        }
    }

    private bool Confirm(string question, string title) =>
        Confirming is { } answer ? answer(question, title) : MessageBox.Show(this, question, title, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK;

    /// <summary>A short store action on the declaration side, logged under its name, the view reloaded.</summary>
    private void Declare(Action<AppServices> action, string name)
    {
        if (_services is null || _view is null || _busy)
        {
            return;
        }

        try
        {
            action(_services);
            Log(name);
        }
        catch (Exception e)
        {
            Fail(e);
        }

        ReloadView();
    }

    private async Task BuildLinesAsync()
    {
        if (_services is null || _view is null || !Editable)
        {
            return;
        }

        if (_personPanel.IsDirty)
        {
            ResolveDirtyPanel();
        }

        var services = _services;
        var periodId = _view.Period.Id;
        await RunBusyAsync(async () =>
        {
            var report = await Task.Run(() => services.Declarer.Build(periodId));
            Log($"{UiText.BuildLines}: {report.Count(LineChange.New)} uut, {report.Count(LineChange.Replaced)} asendatud, {report.Count(LineChange.Changed)} muudatust (M), {report.Count(LineChange.Cancelled)} tühistust (T), {report.Count(LineChange.Kept)} jäi, {report.Count(LineChange.Deleted)} kustutatud");
            foreach (var person in report.Persons)
            {
                if (person.Lines.Count > 0 || person.Problems.Count > 0)
                {
                    var month = person.MinimumDue > 0 ? $"; SM miinimum {UiText.Money(person.MinimumDue)} (osa {person.MinimumShare:0.##}), puudujääk {UiText.Money(person.MinimumShortfall)}" : "";
                    Log($"   {person.Name}: maksuvaba {UiText.Money(person.ExemptionOffered)} → üle {UiText.Money(person.ExemptionLeft)}{month}");
                }

                foreach (var line in person.Lines)
                {
                    Log($"      {line.Key} {UiText.ActionText(line.Action)} {UiText.ChangeText(line.Change)} — {line.Source}{(line.Note is null ? "" : $" ({line.Note})")}");
                }

                foreach (var problem in person.Problems)
                {
                    Log("      " + problem);
                }
            }

            foreach (var problem in report.Problems)
            {
                Log("   " + problem);
            }
        });
        ReloadView();
    }

    private async Task ExportAsync()
    {
        if (_services is null || _view is null)
        {
            return;
        }

        using var folders = new FolderBrowserDialog { Description = UiText.ExportFolder, UseDescriptionForTitle = true, SelectedPath = _settings.ExportFolder ?? "" };
        if (folders.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _settings.ExportFolder = folders.SelectedPath;
        SaveSettings();
        string? reason = null;
        if (_services.Submitter.NeedsCorrectionReason(_view.Period.Id))
        {
            using var dialog = new CorrectionReasonDialog();
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            reason = dialog.Reason;
        }

        var services = _services;
        var periodId = _view.Period.Id;
        var folder = folders.SelectedPath;
        ExportResult? result = null;
        await RunBusyAsync(async () =>
        {
            result = await Task.Run(() => services.Submitter.Export(periodId, folder, reason));
            Log($"{UiText.Export}: {result.Path} ({result.Lines} rida, uniqueID {result.Submission.UniqueId})");
        });
        ReloadView();
        if (result is not null)
        {
            using var checklist = new ChecklistDialog(result);
            checklist.ShowDialog(this);
        }
    }

    /// <summary>The report-details workbook into a folder of the accountant's choosing (the XML's folder is offered),
    /// built off the UI thread under the busy state like the other store runs; the panel's edits saved first so the
    /// workbook shows what the grid shows.</summary>
    private async Task ExportReportAsync()
    {
        if (_services is null || _view is null || _busy)
        {
            return;
        }

        if (_personPanel.IsDirty)
        {
            ResolveDirtyPanel();
            ReloadView(); // whichever way it went, the grid shows what the store holds before the folder is asked
            if (_services is null || _view is null)
            {
                return;
            }
        }

        using var folders = new FolderBrowserDialog { Description = UiText.ReportFolder, UseDescriptionForTitle = true, SelectedPath = _settings.ExportFolder ?? "" };
        if (folders.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _settings.ExportFolder = folders.SelectedPath;
        SaveSettings();
        var services = _services;
        var periodId = _view.Period.Id;
        var folder = folders.SelectedPath;
        ReportExportResult? result = null;
        await RunBusyAsync(async () =>
        {
            result = await Task.Run(() =>
            {
                var book = services.Reports.Build(periodId);
                return ReportWriter.Write(book, folder);
            });
            Log($"{UiText.ExportReport}: {result.Path} ({result.Sheets} lehte)");
        });
        ReloadView();
        if (result is not null && MessageBox.Show(this, $"{UiText.ReportSaved}\n{result.Path}\n\n{UiText.OpenFolderQuestion}", UiText.ExportReport, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{result.Path}\"") { UseShellExecute = true });
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                Fail(e);
            }
        }
    }

    private void Withdraw(Submission submission)
    {
        if (Confirm(UiText.WithdrawConfirm, UiText.Withdraw))
        {
            Declare(s => s.Submitter.Withdraw(submission.Id), $"{UiText.Withdraw}: {submission.FileName}");
        }
    }

    private void MarkConfirmed()
    {
        if (_services is null || _view is null || _busy)
        {
            return;
        }

        try
        {
            _services.Submitter.MarkConfirmed(_view.Period.Id);
            Log(UiText.MarkConfirmed);
        }
        catch (DeclareException e) when (e.Code == PeriodSubmitter.LinesPending)
        {
            if (MessageBox.Show(this, e.Message + Environment.NewLine + Environment.NewLine + UiText.ConfirmForce, UiText.MarkConfirmed, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                try
                {
                    _services.Submitter.MarkConfirmed(_view.Period.Id, force: true);
                    Log(UiText.MarkConfirmed);
                }
                catch (Exception inner)
                {
                    Fail(inner);
                }
            }
        }
        catch (Exception e)
        {
            Fail(e);
        }

        ReloadView();
    }

    private async Task ReadFeedbackAsync(Submission submission)
    {
        if (_services is null || _view is null)
        {
            return;
        }

        // a report carries no uniqueID: the accountant names the file it answers
        if (!Confirm($"{UiText.ApplyReportTo} {submission.FileName} ({submission.CreatedAt.ToLocalTime():dd.MM.yyyy HH:mm})?", UiText.ReadFeedback))
        {
            return;
        }

        using var dialog = new OpenFileDialog { Filter = UiText.FeedbackFilter, InitialDirectory = _settings.ExportFolder ?? "" };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var services = _services;
        var path = dialog.FileName;
        await RunBusyAsync(async () =>
        {
            var outcomes = await Task.Run(() => services.Submitter.ReadFeedback(submission.Id, path));
            foreach (var outcome in outcomes)
            {
                Log(outcome.Confirmation is { } confirmation
                    ? $"{UiText.ReadFeedback} {outcome.Source}: kinnitamise raport — {confirmation}"
                    : $"{UiText.ReadFeedback} {outcome.Source}: {outcome.Recorded} rida; {string.Join(", ", outcome.Statuses.Select(kv => $"{UiText.PaymentStatusText(kv.Key)} {kv.Value}"))}");
            }
        });
        ReloadView();
    }

    private bool Editable => _view?.Editable == true;

    private bool HasCompany => _services is not null && _companyBox.SelectedItem is Choice;

    private Company? SelectedCompany => _services is not null && _companyBox.SelectedItem is Choice c ? _services.Store.FindCompany(c.Value) : null;

    private ReviewPerson? SelectedPerson => _personsGrid.CurrentRow?.Tag as ReviewPerson;

    private ReviewPayment? SelectedPayment => _paymentsGrid.CurrentRow?.Tag as ReviewPayment;

    private static PaymentKey? KeyOf(ReviewPayment? payment) => payment is null ? null : new PaymentKey(payment.RowId, payment.LineId);

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _outer.SplitterDistance = Math.Max(400, _outer.Width - 440);
        _grids.SplitterDistance = Math.Max(120, _grids.Height / 3);
        var path = _storeOverride ?? _settings.StorePath;
        if (path is null)
        {
            OpenStoreDialog(false);
        }
        else if (!File.Exists(path))
        {
            // a moved or renamed store is never replaced by a silent empty one
            Log($"{UiText.StoreMissing} {path}");
            OpenStoreDialog(false);
        }
        else
        {
            OpenStore(path);
        }

        // the release folder is asked once the window is up, off the UI thread, never holding it
        BeginInvoke(async () => await CheckForUpdatesAsync(manual: false));
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_busy)
        {
            e.Cancel = true;
            _lookups?.Cancel();
            MessageBox.Show(this, UiText.BusyClosing, UiText.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_personPanel.IsDirty && !_handedOver)
        {
            ResolveDirtyPanel();
        }

        if (!_handedOver)
        {
            SaveSettings();
        }

        // a window closed by hand while its first check still ran was running all the same: the copy that installed
        // it must not take the silence for a failed start
        StartupFinished(true);

        _services?.Dispose();
        _services = null;
    }

    // ---------------------------------------------------------------- the store, the company, the period

    private void OpenStoreDialog(bool create)
    {
        string? path;
        if (create)
        {
            using var dialog = new SaveFileDialog { Filter = UiText.StoreFilter, FileName = "SatellesTSD.db", OverwritePrompt = false };
            path = dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
        }
        else
        {
            using var dialog = new OpenFileDialog { Filter = UiText.StoreFilter, CheckFileExists = true };
            path = dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
        }

        if (path is not null)
        {
            OpenStore(path);
        }
    }

    private void OpenStore(string path)
    {
        _lookups?.Cancel();
        _services?.Dispose();
        _services = null;
        _view = null;
        _period = null;
        try
        {
            _services = new AppServices(path);
        }
        catch (Exception e) when (OfferStoreRestore(path, e))
        {
            OpenStore(path); // the copy put back opens as this build's
            return;
        }
        catch (Exception e)
        {
            Fail(e);
            _storeLabel.Text = UiText.NoStore;
            FillCompanies();
            return;
        }

        _storeLabel.Text = path;
        _settings.StorePath = path;
        SaveSettings();
        _personPanel.LookupsAvailable = _services.Pipeline.HasLookups;
        FillCompanies();
    }

    private void FillCompanies()
    {
        _filling = true;
        _companyBox.Items.Clear();
        if (_services is not null)
        {
            foreach (var company in _services.Store.Companies())
            {
                _companyBox.Items.Add(new Choice(company.RegistryCode, $"{company.Name} ({company.RegistryCode})"));
            }
        }

        var wanted = Choice.IndexOf(_companyBox.Items.Cast<Choice>(), _settings.LastCompany);
        _companyBox.SelectedIndex = _companyBox.Items.Count == 0 ? -1 : Math.Max(0, wanted);
        _filling = false;
        if (_companyBox.Items.Count == 0 && _services is not null)
        {
            Log(UiText.NoCompany);
        }

        FillPeriods();
        ReloadView();
    }

    private void FillPeriods()
    {
        _filling = true;
        var today = DateTime.Today;
        var previous = today.AddMonths(-1);
        var periods = SelectedCompany is { } company && _services is not null ? _services.Store.Periods(company.Id) : [];
        var firstYear = Math.Min(periods.Count == 0 ? today.Year - 2 : periods.Min(p => p.Year), today.Year - 2);
        var year = _settings.LastYear ?? previous.Year;
        var month = _settings.LastMonth ?? previous.Month;
        _yearBox.Items.Clear();
        for (var y = firstYear; y <= today.Year + 1; y++)
        {
            _yearBox.Items.Add(y.ToString(CultureInfo.InvariantCulture));
        }

        _monthBox.Items.Clear();
        for (var m = 1; m <= 12; m++)
        {
            _monthBox.Items.Add(m.ToString("00", CultureInfo.InvariantCulture));
        }

        _yearBox.SelectedIndex = Math.Clamp(year - firstYear, 0, _yearBox.Items.Count - 1);
        _monthBox.SelectedIndex = Math.Clamp(month - 1, 0, 11);
        _filling = false;
    }

    private (int Year, int Month)? SelectedPeriod =>
        _yearBox.SelectedItem is string y && _monthBox.SelectedItem is string m
            ? (int.Parse(y, CultureInfo.InvariantCulture), int.Parse(m, CultureInfo.InvariantCulture))
            : null;

    /// <summary>The period row of the month shown, created only now: looking at a month creates nothing.</summary>
    private Period? EnsurePeriod()
    {
        if (_period is not null)
        {
            return _period;
        }

        if (_services is null || SelectedCompany is not { } company || SelectedPeriod is not { } key)
        {
            return null;
        }

        _period = _services.Store.GetOrCreatePeriod(company.Id, key.Year, key.Month);
        return _period;
    }

    private void NewCompany()
    {
        if (_services is null)
        {
            return;
        }

        using var dialog = new CompanyDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            if (_services.Store.FindCompany(dialog.RegistryCode) is not null)
            {
                Fail(new InvalidOperationException($"Ettevõte registrikoodiga {dialog.RegistryCode} on juba olemas."));
                return;
            }

            var company = _services.Store.AddCompany(dialog.RegistryCode, dialog.CompanyTitle);
            _settings.LastCompany = company.RegistryCode;
            FillCompanies();
        }
        catch (Exception e)
        {
            Fail(e);
        }
    }

    // ---------------------------------------------------------------- the view

    private void ReloadView(Guid? keepPerson = null, PaymentKey? keepPayment = null)
    {
        if (_busy)
        {
            return; // the store belongs to the run until it ends; the run's end reloads
        }

        keepPerson ??= SelectedPerson?.PersonId;
        var keepUnmatched = keepPerson is null && SelectedPerson?.IsUnmatched == true;
        keepPayment ??= KeyOf(SelectedPayment);
        _view = null;
        _period = null;
        if (_services is not null && SelectedCompany is { } company && SelectedPeriod is { } key)
        {
            try
            {
                _period = _services.Store.Periods(company.Id).FirstOrDefault(p => p.Year == key.Year && p.Month == key.Month);
                if (_period is not null)
                {
                    _view = _services.Workspace.Load(_period.Id);
                }

                _settings.LastCompany = company.RegistryCode;
                _settings.LastYear = key.Year;
                _settings.LastMonth = key.Month;
                SaveSettings();
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        FillPersons(keepPerson, keepUnmatched);
        PresentDeclaration();
        UpdateState();
        SelectPayment(keepPayment);
    }

    private void FillPersons(Guid? keepPerson, bool keepUnmatched)
    {
        _filling = true;
        _personsGrid.Rows.Clear();
        _personsGrid.Columns.Clear();
        if (_view is null)
        {
            _filling = false;
            ShowSelectedPerson();
            return;
        }

        _personsGrid.Columns.Add(Column("code", UiText.PersonalCode));
        _personsGrid.Columns.Add(Column("last", UiText.LastName));
        _personsGrid.Columns.Add(Column("first", UiText.FirstName));
        _personsGrid.Columns.Add(Column("exemption", UiText.Exemption));
        foreach (var source in _view.Sources)
        {
            _personsGrid.Columns.Add(Column("src:" + source, source == ReviewWorkspace.ByHandSource ? UiText.ByHand : source, right: true));
        }

        _personsGrid.Columns.Add(Column("total", UiText.SalaryTotal, right: true));
        _personsGrid.Columns.Add(Column("residency", UiText.Residency));
        _personsGrid.Columns.Add(Column("pension", UiText.Pension));
        _personsGrid.Columns.Add(Column("checked", UiText.CheckedOn));
        _personsGrid.Columns.Add(Column("type", UiText.EmploymentType));
        _personsGrid.Columns.Add(Column("state", UiText.EntryState));
        _personsGrid.Columns.Add(Column("problems", UiText.Problems));

        var selectRow = 0;
        foreach (var person in _view.Persons)
        {
            var values = new List<object>();
            if (person.IsUnmatched)
            {
                values.AddRange(["", UiText.UnmatchedRow, "", ""]);
                values.AddRange(_view.Sources.Select(_ => (object)""));
                values.AddRange(["", "", "", "", "", "", ""]);
            }
            else
            {
                var p = person.Person;
                values.AddRange([person.PersonalCode ?? "", person.LastName, person.FirstName, ExemptionText(p)]);
                values.AddRange(_view.Sources.Select(s => (object)(person.SalaryBySource.TryGetValue(s, out var sum) ? UiText.Money(sum) : "")));
                values.Add(UiText.Money(person.SalaryTotal));
                values.Add(p is null ? "" : UiText.ResidencyText(p));
                values.Add(p is null ? "" : UiText.PensionText(p));
                values.Add(p is null ? "" : CheckedText(p));
                values.Add(person.Register?.Entry.Type ?? EmploymentTypes.Find(p?.TorEmploymentTypeId)?.Name ?? "");
                values.Add(person.Register?.Entry.State ?? "");
                values.Add(string.Join(", ", person.Problems.Select(x => x.Code).Distinct()));
            }

            var index = _personsGrid.Rows.Add(values.ToArray());
            var row = _personsGrid.Rows[index];
            row.Tag = person;
            if (person.IsUnmatched)
            {
                row.DefaultCellStyle.Font = new Font(_personsGrid.Font, FontStyle.Italic);
                if (keepUnmatched)
                {
                    selectRow = index;
                }
            }
            else if (person.Problems.Count > 0)
            {
                row.DefaultCellStyle.BackColor = Color.LightYellow;
            }

            if (keepPerson is { } id && person.PersonId == id)
            {
                selectRow = index;
            }
        }

        if (_personsGrid.Rows.Count > 0)
        {
            _personsGrid.CurrentCell = _personsGrid.Rows[selectRow].Cells[0];
        }

        _filling = false;
        ShowSelectedPerson();
        if (_view.RowsLeftOut > 0)
        {
            Log($"{_view.RowsLeftOut} {UiText.RowsLeftOut}");
        }
    }

    private void ShowSelectedPerson()
    {
        var person = SelectedPerson;
        FillPayments(person);
        var target = person?.Person;
        var sameAsTyped = _personPanel.IsDirty && _personPanel.PresentedId is { } presented && presented == target?.Id;
        if (!sameAsTyped)
        {
            // what the accountant typed for another person is saved or dropped, never lost in silence
            if (_personPanel.IsDirty && ResolveDirtyPanel())
            {
                BeginInvoke(() => ReloadView());
            }

            _personPanel.Present(target, Editable && !_busy);
        }

        _removeLineButton.Enabled = Editable && !_busy && SelectedPayment is { IsByHand: true };
    }

    /// <summary>Asks about the panel's unsaved edits: Yes saves them (true), No drops them.</summary>
    private bool ResolveDirtyPanel()
    {
        var saved = false;
        var answer = MessageBox.Show(this, UiText.UnsavedPerson, UiText.PersonPanel, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer == DialogResult.Yes && _services is not null && _personPanel.Collect() is { } edited)
        {
            try
            {
                var person = _services.Workspace.SavePerson(edited, _personPanel.ConfirmResidency, _personPanel.ConfirmPension);
                Log($"{UiText.Save}: {person.LastName} {person.FirstName}");
                saved = true;
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        _personPanel.MarkClean();
        return saved;
    }

    private void BuildPaymentColumns()
    {
        _paymentsGrid.Columns.Add(Column("document", UiText.Document));
        _paymentsGrid.Columns.Add(Column("date", UiText.Date));
        _paymentsGrid.Columns.Add(Column("payee", UiText.PayeeOnStatement));
        _paymentsGrid.Columns.Add(Column("description", UiText.Description));
        _paymentsGrid.Columns.Add(Column("amount", UiText.Amount, right: true));
        _paymentsGrid.Columns.Add(Column("verdict", UiText.Verdict));
        _paymentsGrid.Columns.Add(new DataGridViewComboBoxColumn { Name = ColCategory, HeaderText = UiText.Category, DisplayMember = nameof(Choice.Text), ValueMember = nameof(Choice.Value), FlatStyle = FlatStyle.Flat, Width = 130 });
        _paymentsGrid.Columns.Add(new DataGridViewComboBoxColumn { Name = ColType, HeaderText = UiText.PaymentType, DisplayMember = nameof(Choice.Text), ValueMember = nameof(Choice.Value), FlatStyle = FlatStyle.Flat, Width = 260 });
        _paymentsGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = ColDeclare, HeaderText = UiText.Declare, Width = 70 });
        _paymentsGrid.Columns.Add(new DataGridViewComboBoxColumn { Name = ColPerson, HeaderText = UiText.Person, DisplayMember = nameof(Choice.Text), ValueMember = nameof(Choice.Value), FlatStyle = FlatStyle.Flat, Width = 200 });
        _paymentsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = ColNote, HeaderText = UiText.Note, Width = 160 });
        _paymentsGrid.Columns.Add(Column("check", UiText.Check));
        foreach (DataGridViewColumn column in _paymentsGrid.Columns)
        {
            column.ReadOnly = column.Name is not (ColCategory or ColType or ColDeclare or ColPerson or ColNote);
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
        }
    }

    private void FillPayments(ReviewPerson? person)
    {
        _filling = true;
        _paymentsGrid.Rows.Clear();
        if (_view is null || person is null)
        {
            _filling = false;
            return;
        }

        // every value a row can hold is in its combo's list, so nothing shows as a blank by mistake
        var categories = new List<Choice> { new("", UiText.Default), new(None, UiText.NoCategory) };
        categories.AddRange(_view.Catalog.Categories.Select(c => new Choice(c.Name, c.Name)));
        categories.AddRange(person.Payments.Select(p => p.Category).OfType<string>().Distinct(StringComparer.Ordinal).Where(c => categories.All(x => x.Value != c)).Select(c => new Choice(c, c)));
        var types = new List<Choice> { new("", UiText.Default) };
        types.AddRange(PaymentTypes.All.Select(t => new Choice(t.Code, $"{t.Code} {t.NameEt}")));
        types.AddRange(person.Payments.Select(p => p.TypeCode).OfType<string>().Distinct(StringComparer.Ordinal).Where(c => types.All(x => x.Value != c)).Select(c => new Choice(c, c)));
        var persons = new List<Choice> { new("", UiText.Default), new(None, UiText.Nobody) };
        persons.AddRange(_view.StorePersons.Select(p => new Choice(p.Id.ToString("N"), $"{p.LastName} {p.FirstName} {p.PersonalCode ?? p.MrrCode ?? ""}".Trim())));
        ((DataGridViewComboBoxColumn)_paymentsGrid.Columns[ColCategory]!).DataSource = categories;
        ((DataGridViewComboBoxColumn)_paymentsGrid.Columns[ColType]!).DataSource = types;
        ((DataGridViewComboBoxColumn)_paymentsGrid.Columns[ColPerson]!).DataSource = persons;

        foreach (var payment in person.Payments)
        {
            var verdict = payment.IsByHand ? UiText.ByHand : payment.VerdictText + (payment.Masks.Length > 0 ? $"  ({payment.Masks})" : "");
            var parts = payment.Checks.Select(c => c.Code).Concat(payment.Flags.Where(f => payment.Checks.All(c => c.Code != f))).ToList();
            if (payment.Candidates.Count > 0)
            {
                parts.Add($"({string.Join(", ", payment.Candidates)})");
            }

            var index = _paymentsGrid.Rows.Add(
                payment.IsByHand ? UiText.ByHand : payment.Reference,
                payment.Date is { } d ? UiText.Day(d) : payment.DateText ?? "",
                payment.Payee,
                payment.Description ?? "",
                payment.Flags.Contains(MatchProblem.BadAmount, StringComparer.Ordinal) ? "?" : UiText.Money(payment.Amount),
                verdict,
                payment.Category ?? None,
                payment.TypeCode ?? "",
                payment.Declare,
                payment.PersonId?.ToString("N") ?? None,
                payment.Note ?? "",
                string.Join("; ", parts));
            var row = _paymentsGrid.Rows[index];
            row.Tag = payment;
            row.Cells["check"].ToolTipText = string.Join(Environment.NewLine, payment.Checks.Select(c => $"{c.Code}: {c.Message}"));
            if (!payment.Declare)
            {
                row.DefaultCellStyle.ForeColor = Color.Gray;
            }
            else if (payment.Checks.Count > 0)
            {
                row.DefaultCellStyle.BackColor = Color.MistyRose;
            }

            if (payment.HasOverride)
            {
                row.Cells[0].Style.Font = new Font(_paymentsGrid.Font, FontStyle.Bold);
            }
        }

        _paymentsGrid.ReadOnly = !Editable || _busy;
        _filling = false;
    }

    private void SelectPayment(PaymentKey? key)
    {
        if (key is not { } wanted)
        {
            return;
        }

        foreach (DataGridViewRow row in _paymentsGrid.Rows)
        {
            if (row.Tag is ReviewPayment p && (wanted.RowId is { } r ? p.RowId == r : p.LineId is not null && p.LineId == wanted.LineId))
            {
                _paymentsGrid.CurrentCell = row.Cells[0];
                return;
            }
        }
    }

    private void OnPaymentEdited(int rowIndex, int columnIndex)
    {
        if (_filling || rowIndex < 0 || columnIndex < 0 || _services is null || _view is null)
        {
            return;
        }

        var row = _paymentsGrid.Rows[rowIndex];
        if (row.Tag is not ReviewPayment payment)
        {
            return;
        }

        var column = _paymentsGrid.Columns[columnIndex].Name;
        var value = row.Cells[columnIndex].Value;
        var text = value as string;
        try
        {
            switch (column)
            {
                case ColCategory:
                    _services.Workspace.SetCategory(payment, string.IsNullOrEmpty(text) ? payment.DefaultCategory : text == None ? null : text);
                    break;
                case ColType:
                    _services.Workspace.SetTypeCode(payment, string.IsNullOrEmpty(text) ? payment.DefaultTypeCode : text);
                    break;
                case ColDeclare:
                    _services.Workspace.SetDeclare(payment, value is true);
                    break;
                case ColPerson:
                    _services.Workspace.SetPerson(payment, string.IsNullOrEmpty(text) ? payment.DefaultPersonId : text == None ? null : Guid.ParseExact(text, "N"));
                    break;
                case ColNote:
                    _services.Workspace.SetNote(payment, text);
                    break;
                default:
                    return;
            }
        }
        catch (Exception e)
        {
            Fail(e);
        }

        // the grid is rebuilt once its own event is over, never from inside it
        var keepPerson = SelectedPerson?.PersonId;
        var key = new PaymentKey(payment.RowId, payment.LineId);
        BeginInvoke(() => ReloadView(keepPerson, key));
    }

    private void UpdateState()
    {
        var hasCompany = HasCompany;
        var editable = _view?.Editable ?? hasCompany;
        _stateLabel.Text = _view is null
            ? hasCompany ? UiText.NoPeriodYet : ""
            : $"{UiText.PeriodState(_view.Period.State)} — {(_view.Editable ? UiText.Editable : UiText.Locked)}{StateDates(_view.Period)}";
        _importButton.Enabled = !_busy && hasCompany && editable;
        _filesButton.Enabled = !_busy && _view is not null;
        _matchButton.Enabled = !_busy && Editable;
        _lookupButton.Enabled = !_busy && _view is not null && _services?.Pipeline.HasLookups == true;
        _addLineButton.Enabled = !_busy && hasCompany && editable;
        _newPersonButton.Enabled = !_busy && hasCompany;
        _newCompanyButton.Enabled = _services is not null && !_busy;
        _openStoreButton.Enabled = !_busy;
        _newStoreButton.Enabled = !_busy;
        _companyBox.Enabled = !_busy;
        _yearBox.Enabled = !_busy;
        _monthBox.Enabled = !_busy;
        _paymentsGrid.ReadOnly = _busy || !Editable;
        _removeLineButton.Enabled = !_busy && Editable && SelectedPayment is { IsByHand: true };
        _personPanel.Enabled = !_busy;
        _declaration.Enabled = !_busy;
        _calculatorButton.Enabled = !_busy;
        _reportButton.Enabled = !_busy && _view is not null;
        _updateButton.Visible = UpdatesEnabled;
        _updateButton.Enabled = !_busy && UpdatesEnabled && !_checkingUpdates;
        UseWaitCursor = _busy;
    }

    // ---------------------------------------------------------------- the runs

    private async Task ImportAsync()
    {
        if (_services is null || !HasCompany || !(_view?.Editable ?? true))
        {
            return;
        }

        using var dialog = new OpenFileDialog { Multiselect = true, Filter = UiText.FileFilter, InitialDirectory = ImportFolder() };
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.FileNames.Length == 0)
        {
            return;
        }

        var period = EnsurePeriod();
        if (period is null)
        {
            return;
        }

        _settings.LastFolder = Path.GetDirectoryName(dialog.FileNames[0]);
        SaveSettings();
        var services = _services;
        var periodId = period.Id;
        var files = dialog.FileNames;
        await RunBusyAsync(async () =>
        {
            // every file gets its line, whatever happened to the one before it
            var outcomes = await Task.Run(() => files.Select(f =>
            {
                try
                {
                    return services.Pipeline.ImportFile(periodId, Path.GetFileName(f), File.ReadAllBytes(f));
                }
                catch (Exception e)
                {
                    return new ImportOutcome { FileName = Path.GetFileName(f), Error = e.Message };
                }
            }).ToList());
            foreach (var outcome in outcomes)
            {
                Log(outcome.Succeeded
                    ? $"{outcome.FileName}: {(outcome.AlreadyImported ? "juba imporditud, seotud perioodiga" : "imporditud")} ({outcome.Kind})"
                    : $"{outcome.FileName}: {outcome.Error}");
                foreach (var message in outcome.Messages)
                {
                    Log("   " + message);
                }
            }
        });
        ReloadView();
    }

    private async Task RunMatchAsync()
    {
        if (_services is null || _view is null || !Editable)
        {
            return;
        }

        if (_view.Run is not null && MessageBox.Show(this, UiText.RunReplacesRun, UiText.RunMatch, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
        {
            return;
        }

        var services = _services;
        var periodId = _view.Period.Id;
        await RunBusyAsync(async () =>
        {
            var result = await Task.Run(() => services.Pipeline.RunMatch(periodId));
            Log($"{UiText.RunMatch}: {result.Persons.Count} isikut, {result.Sources.Count} allikat, {result.Hits.Count} rida, {result.Problems.Count} probleemi");
            foreach (var problem in result.Problems)
            {
                Log($"   {problem.Code}: {problem.Message}");
            }
        });
        ReloadView();
    }

    private async Task RunLookupsAsync()
    {
        if (_services is null || _view is null)
        {
            return;
        }

        if (MessageBox.Show(this, UiText.LookupsLive, UiText.RunLookups, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
        {
            return;
        }

        var services = _services;
        var periodId = _view.Period.Id;
        var progress = new Progress<PersonLookupOutcome>(LogOutcome);
        await RunCancellableAsync(token => services.Pipeline.RunLookupsAsync(periodId, progress, token), report =>
        {
            foreach (var problem in report.Problems)
            {
                Log("   " + problem);
            }

            Log($"{UiText.RunLookups}: {UiText.Done}, {report.Queries} päringut, kontrollkuupäev {UiText.Day(report.ControlDate)}");
        });
        ReloadView();
    }

    private async Task LookUpOneAsync(Person person)
    {
        if (_services is null || _period is null)
        {
            return;
        }

        if (_personPanel.IsDirty)
        {
            ResolveDirtyPanel(); // the register is asked about the person as saved, never about half-typed facts
        }

        var services = _services;
        var period = _period;
        var fresh = services.Store.Persons(person.CompanyId).FirstOrDefault(p => p.Id == person.Id) ?? person;
        await RunCancellableAsync(token => services.Pipeline.LookUpAsync(fresh, period, token), LogOutcome);
        ReloadView(person.Id);
    }

    private async Task RunCancellableAsync<T>(Func<CancellationToken, Task<T>> work, Action<T> report)
    {
        _lookups = new CancellationTokenSource();
        var token = _lookups.Token;
        _cancelButton.Enabled = true;
        try
        {
            await RunBusyAsync(async () => report(await work(token)));
        }
        finally
        {
            _cancelButton.Enabled = false;
            _lookups.Dispose();
            _lookups = null;
        }
    }

    private void LogOutcome(PersonLookupOutcome outcome)
    {
        Log($"{outcome.Name}: {string.Join("; ", outcome.Steps)}");
        foreach (var problem in outcome.Problems)
        {
            Log("   " + problem);
        }
    }

    private async Task RunBusyAsync(Func<Task> work)
    {
        SetBusy(true);
        try
        {
            await work();
        }
        catch (OperationCanceledException)
        {
            Log(UiText.Cancelled);
        }
        catch (Exception e)
        {
            Fail(e);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        UpdateState();
    }

    // ---------------------------------------------------------------- the edits

    private void AddLine()
    {
        if (_services is null || !HasCompany || !(_view?.Editable ?? true))
        {
            return;
        }

        if (_view is null)
        {
            if (EnsurePeriod() is null)
            {
                return;
            }

            ReloadView();
            if (_view is null)
            {
                return;
            }
        }

        using var dialog = new HandLineDialog(_view, SelectedPerson?.PersonId);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            _services.Workspace.AddHandLine(_view.Period.Id, dialog.PersonId, dialog.Date, dialog.Amount, dialog.Payee, dialog.Description, dialog.Category, dialog.TypeCode, dialog.Declare, dialog.Note);
        }
        catch (Exception e)
        {
            Fail(e);
        }

        ReloadView(dialog.PersonId);
    }

    private void RemoveLine()
    {
        if (_services is null || _view is null || SelectedPayment is not { IsByHand: true, LineId: { } lineId })
        {
            return;
        }

        if (MessageBox.Show(this, UiText.RemoveLineConfirm, UiText.RemoveLine, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
        {
            return;
        }

        try
        {
            _services.Workspace.RemoveHandLine(_view.Period.Id, lineId);
        }
        catch (Exception e)
        {
            Fail(e);
        }

        ReloadView(SelectedPerson?.PersonId);
    }

    private void NewPerson()
    {
        if (_services is null || SelectedCompany is not { } company)
        {
            return;
        }

        using var dialog = new PersonDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var person = _services.Workspace.AddPerson(company.Id, dialog.FirstName, dialog.LastName, dialog.PersonalCode, dialog.TorTypeId, dialog.Residency);
            Log($"{UiText.NewPerson} {person.LastName} {person.FirstName}");
        }
        catch (Exception e)
        {
            Fail(e);
        }

        ReloadView();
    }

    private void SavePerson(Person edited, bool confirmResidency, bool confirmPension)
    {
        if (_services is null)
        {
            return;
        }

        try
        {
            var saved = _services.Workspace.SavePerson(edited, confirmResidency, confirmPension);
            Log($"{UiText.Save}: {saved.LastName} {saved.FirstName}");
            _personPanel.MarkClean();
        }
        catch (Exception e)
        {
            Fail(e);
            return; // the typed values stay for a second try
        }

        ReloadView(edited.Id);
    }

    private void ShowFiles()
    {
        if (_services is null || _view is null)
        {
            return;
        }

        using var dialog = new FilesDialog(_services, _view, Log);
        dialog.ShowDialog(this);
        ReloadView();
    }

    // ---------------------------------------------------------------- helpers

    private static DataGridView Grid() => new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
        EditMode = DataGridViewEditMode.EditOnEnter,
    };

    private static DataGridViewTextBoxColumn Column(string name, string header, bool right = false) => new()
    {
        Name = name,
        HeaderText = header,
        ReadOnly = true,
        SortMode = DataGridViewColumnSortMode.NotSortable,
        DefaultCellStyle = { Alignment = right ? DataGridViewContentAlignment.MiddleRight : DataGridViewContentAlignment.MiddleLeft },
    };

    /// <summary>The dates the state has come through (the store never clears a stamp, so a Draft shows none and an
    /// Exported period its export only).</summary>
    private static string StateDates(Period period)
    {
        var parts = new List<string>();
        if (period.ExportedAt is { } e && period.State >= PeriodState.Exported) { parts.Add($"{UiText.PeriodState(PeriodState.Exported)} {e.ToLocalTime():dd.MM.yyyy HH:mm}"); }
        if (period.UploadedAt is { } u && period.State >= PeriodState.Uploaded) { parts.Add($"{UiText.PeriodState(PeriodState.Uploaded)} {u.ToLocalTime():dd.MM.yyyy HH:mm}"); }
        if (period.ConfirmedAt is { } c && period.State == PeriodState.Confirmed) { parts.Add($"{UiText.PeriodState(PeriodState.Confirmed)} {c.ToLocalTime():dd.MM.yyyy HH:mm}"); }
        return parts.Count == 0 ? "" : " (" + string.Join(", ", parts) + ")";
    }

    private static string ExemptionText(Person? person) =>
        person is null ? "" : !person.BasicExemptionApplied ? UiText.No : person.BasicExemptionMonthly is { } amount ? UiText.Money(amount) : UiText.Yes;

    private static string CheckedText(Person person)
    {
        var dates = new[] { person.ResidencyCheckedOn, person.PensionCheckedOn }.Where(d => d is not null).Select(d => d!.Value).ToList();
        return dates.Count == 0 ? "" : UiText.Day(dates.Min());
    }

    /// <summary>Where the import dialog opens: the user's Downloads folder, where the exports and statements land;
    /// the folder last picked when there is no Downloads; the documents folder as the last resort.</summary>
    private string ImportFolder()
    {
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (Directory.Exists(downloads))
        {
            return downloads;
        }

        return _settings.LastFolder is { } last && Directory.Exists(last) ? last : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private void Log(string line)
    {
        _log.Items.Add($"{DateTime.Now:HH:mm:ss}  {line}");
        _log.TopIndex = Math.Max(0, _log.Items.Count - 1);
    }

    private void Fail(Exception e)
    {
        Log($"{UiText.Problem}: {e.Message}");
        if (Failing is { } take)
        {
            take(e);
            return;
        }

        MessageBox.Show(this, e.Message, UiText.Problem, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void SaveSettings()
    {
        try
        {
            _settings.Save(_settingsPath);
        }
        catch (IOException)
        {
            // the memory is a convenience; losing it costs a click
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
