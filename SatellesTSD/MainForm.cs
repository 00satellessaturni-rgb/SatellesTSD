using System.ComponentModel;
using System.Globalization;
using SatellesTSD.Core.Declare;
using SatellesTSD.Core.Import;
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
    private const string ColAmount = "amount";

    /// <summary>The combos' "nobody" / "no category" value; "" is "back to the default".</summary>
    private const string None = "-";

    /// <summary>A payment found again after a reload: a row's own share by the row, a part of a split row and a hand line by their line.</summary>
    private readonly record struct PaymentKey(Guid? RowId, Guid? LineId, bool IsPart = false)
    {
        public static PaymentKey Of(ReviewPayment payment) => new(payment.RowId, payment.LineId, payment.IsPart);
    }

    private readonly string _settingsPath;
    private readonly AppSettings _settings;
    private readonly string? _storeOverride;
    private AppServices? _services;
    private readonly ToolStripSeparator _updateSeparator = new(); // shown with the update item, hidden with it
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
    // the commands, in the menus by their group: the store, the company, the period, the person, the tools
    private readonly MenuStrip _menu = new();
    private readonly ToolStripMenuItem _openStoreButton = new(UiText.OpenStore);
    private readonly ToolStripMenuItem _newStoreButton = new(UiText.NewStore);
    private readonly ToolStripMenuItem _closeButton = new(UiText.Close);
    private readonly ToolStripMenuItem _newCompanyButton = new(UiText.NewCompany);
    private readonly ToolStripMenuItem _editCompanyButton = new(UiText.EditCompany);
    private readonly ToolStripMenuItem _closeCompanyButton = new(UiText.CloseCompany);
    private readonly Label _salaryTotal = new() { Dock = DockStyle.Bottom, Height = 24, TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(0, 0, 8, 0), Font = new Font(Control.DefaultFont, FontStyle.Bold) };
    private readonly ToolStripMenuItem _importButton = new(UiText.ImportFiles);
    private readonly ToolStripMenuItem _filesButton = new(UiText.Files);
    private readonly ToolStripMenuItem _matchButton = new(UiText.RunMatch);
    private readonly ToolStripMenuItem _lookupButton = new(UiText.RunLookups);
    private readonly ToolStripMenuItem _cancelButton = new(UiText.Cancel) { Enabled = false };
    private readonly ToolStripMenuItem _addLineButton = new(UiText.AddLine);
    private readonly ToolStripMenuItem _removeLineButton = new(UiText.RemoveLine) { Enabled = false };
    private readonly ToolStripMenuItem _newPersonButton = new(UiText.NewPerson);
    private readonly ToolStripMenuItem _calculatorButton = new(UiText.Calculator);
    private readonly ToolStripMenuItem _declareEachItem = new(UiText.DeclareEachPayment);
    private readonly ToolStripMenuItem _declareTotalsItem = new(UiText.DeclareTotalsPerType);
    private readonly ToolStripMenuItem _importLayoutsItem = new(UiText.ImportLayouts);
    private readonly ToolStripMenuItem _layoutsItem = new(UiText.Layouts);
    private readonly ToolStripMenuItem _logDefaultItem = new(UiText.LogHeightDefault);
    private readonly ToolStripMenuItem _logOneRowItem = new(UiText.LogHeightOneRow);
    private readonly ToolStripMenuItem _salaryOnlyItem = new(UiText.PersonsSalaryOnly);
    private readonly ToolStripMenuItem _reportButton = new(UiText.ExportReport);
    private readonly ToolStripMenuItem _updateButton = new(UiText.CheckUpdates);

    // the grids' tools: a search box over each, and the payments shown by the person picked or by category
    private readonly TextBox _personsSearch = new() { Width = 220 };
    private readonly TextBox _paymentsSearch = new() { Width = 220 };
    private readonly TabControl _scopeTabs = new() { Dock = DockStyle.Top, Height = 26, Multiline = false };
    private sealed record Scope(string Kind, string? Category);
    private const string ScopeSelected = "selected";
    private const string ScopeAll = "all";
    private const string ScopeUnknown = "unknown";
    private const string ScopeCategory = "category";
    private const string ScopeConflict = "conflict";
    private UpdateCheck? _updateCheck;
    private bool _checkingUpdates;
    private bool _handedOver;
    private readonly ToolStripLabel _stateLabel = new("") { Font = new Font(Control.DefaultFont, FontStyle.Bold) };
    // a register run's progress on the toolbar: shown while it runs, with its own stop button beside the menu's
    private readonly ToolStripSeparator _progressSeparator = new() { Visible = false };
    private readonly ToolStripProgressBar _progressBar = new() { Visible = false, Minimum = 0, AutoSize = false, Width = 160 };
    private readonly ToolStripLabel _progressLabel = new("") { Visible = false };
    private readonly ToolStripButton _stopButton = new(UiText.Cancel) { Visible = false };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly DeclarationPanel _declaration = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _personsGrid = Grid();
    private readonly DataGridView _paymentsGrid = Grid();
    private readonly PersonPanel _personPanel = new() { Dock = DockStyle.Fill };
    private readonly ListBox _log = new() { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
    private readonly Panel _logPanel = new() { Dock = DockStyle.Bottom, Height = LogDefaultHeight };
    private readonly SplitContainer _outer = new() { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, FixedPanel = FixedPanel.Panel2 }; // the card keeps its width when the window is resized
    private readonly SplitContainer _grids = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel1 }; // the persons grid keeps its height: a wrapped toolbar or the log takes from the payments

    /// <summary>The release version from the project file ("0.10.0"), as the title and the release folder name it.</summary>
    public static string AppVersion => typeof(MainForm).Assembly.GetName().Version is { } v ? v.ToString(3) : "0";

    public MainForm(string settingsPath, AppSettings settings, string? storeOverride)
    {
        _settingsPath = settingsPath;
        _settings = settings;
        _storeOverride = storeOverride;
        // a store named on the command line that is not the remembered one is a one-off: the settings keep the store, the
        // company and the month they had until a store is chosen in the window (decision 147)
        _remembered = (settings.StorePath, settings.LastCompany, settings.LastYear, settings.LastMonth);
        _oneOff = storeOverride is not null && !FileIdentity.Same(storeOverride, settings.StorePath);
        if (_oneOff)
        {
            _settings.LastCompany = null; // the other store's company means nothing here: the one-off store shows its first
        }

        // the published program alone looks for updates: a build folder's copy or a test host's window never touches the share
        UpdatesEnabled = Environment.ProcessPath is { } exe && string.Equals(Path.GetFileName(exe), Updater.ExeName, StringComparison.OrdinalIgnoreCase)
            && typeof(MainForm).Assembly.Location.Length == 0;
        Text = TitleText();
        Size = new Size(1500, 900);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = WindowLayout.MinimumSize;
        // the size and place the window was closed with; a place no screen shows any more opens centred
        if (WindowLayout.Fit(settings.WindowX, settings.WindowY, settings.WindowWidth, settings.WindowHeight, Screen.AllScreens.Select(s => s.WorkingArea).ToList(), WindowLayout.InvisibleFrame) is { } fit)
        {
            Size = fit.Size;
            if (fit.Location is { } at)
            {
                StartPosition = FormStartPosition.Manual;
                Location = at;
            }

            if (settings.WindowMaximized)
            {
                WindowState = FormWindowState.Maximized;
                _lastShownState = FormWindowState.Maximized;
            }
        }

        // the menus hold the commands by their group; the toolbar below them holds what is chosen: the store, the company, the month
        var storeMenu = new ToolStripMenuItem(UiText.MenuStore);
        storeMenu.DropDownItems.AddRange([_openStoreButton, _newStoreButton, _updateSeparator, _updateButton, new ToolStripSeparator(), _closeButton]);
        var companyMenu = new ToolStripMenuItem(UiText.MenuCompany);
        companyMenu.DropDownItems.AddRange([_newCompanyButton, _editCompanyButton, _filesButton, new ToolStripSeparator(), _closeCompanyButton]);
        _editCompanyButton.Click += (_, _) => EditCompany();
        var periodMenu = new ToolStripMenuItem(UiText.MenuPeriod);
        periodMenu.DropDownItems.AddRange([_importButton, _matchButton, _lookupButton, new ToolStripSeparator(), _cancelButton]);
        var personMenu = new ToolStripMenuItem(UiText.MenuPerson);
        personMenu.DropDownItems.AddRange([_newPersonButton, _addLineButton, _removeLineButton]);
        var toolsMenu = new ToolStripMenuItem(UiText.MenuTools);
        toolsMenu.DropDownItems.AddRange([_reportButton, _calculatorButton]);
        // how the window is laid out, kept across sessions: the log's height (one of the two items is checked)
        var viewMenu = new ToolStripMenuItem(UiText.MenuView);
        viewMenu.DropDownItems.AddRange([_logDefaultItem, _logOneRowItem, new ToolStripSeparator(), _salaryOnlyItem]);
        // the settings kept across sessions: the declaration's cut (one of the two items is checked)
        var settingsMenu = new ToolStripMenuItem(UiText.MenuSettings);
        settingsMenu.DropDownItems.AddRange([_declareEachItem, _declareTotalsItem, new ToolStripSeparator(), _importLayoutsItem, _layoutsItem]);
        _importLayoutsItem.Click += (_, _) => ImportLayouts();
        _layoutsItem.Click += (_, _) => ShowLayouts();
        _menu.Items.AddRange([storeMenu, companyMenu, periodMenu, personMenu, toolsMenu, viewMenu, settingsMenu]);
        _logDefaultItem.Click += (_, _) => SetLogOneRow(false);
        _logOneRowItem.Click += (_, _) => SetLogOneRow(true);
        _salaryOnlyItem.Click += (_, _) => SetPersonsSalaryOnly(!_settings.PersonsSalaryOnly);
        _salaryOnlyItem.Checked = _settings.PersonsSalaryOnly;
        _declareEachItem.Click += (_, _) => SetDeclarationMode(totals: false);
        _declareTotalsItem.Click += (_, _) => SetDeclarationMode(totals: true);
        ShowDeclarationMode();
        MainMenuStrip = _menu;
        _toolbar.Items.AddRange(
        [
            new ToolStripLabel(UiText.Store), _storeLabel, new ToolStripSeparator(),
            new ToolStripLabel(UiText.Company), _companyBox, new ToolStripSeparator(),
            new ToolStripLabel(UiText.Period), _yearBox, _monthBox, new ToolStripSeparator(), _stateLabel,
            _progressSeparator, _progressLabel, _progressBar, _stopButton,
        ]);
        _stopButton.Click += (_, _) => _lookups?.Cancel();
        _undeclaredTips = new GridTips(_personsGrid, "undeclared");
        Disposed += (_, _) => _undeclaredTips.Dispose();
        _problemTips = new GridTips(_personsGrid, "problems"); // the problems' texts, the cell holding the codes only
        Disposed += (_, _) => _problemTips.Dispose();
        _updateButton.Click += async (_, _) => await CheckForUpdatesAsync(manual: true);
        _closeButton.Click += (_, _) => Close();

        VisibleSplitter(_grids);
        VisibleSplitter(_outer);
        _grids.Panel1.Controls.Add(_personsGrid);
        _grids.Panel1.Controls.Add(SearchBar(_personsSearch));
        _grids.Panel1.Controls.Add(_salaryTotal);
        _grids.Panel2.Controls.Add(_paymentsGrid);
        _grids.Panel2.Controls.Add(SearchBar(_paymentsSearch));
        _grids.Panel2.Controls.Add(_scopeTabs);
        _personsSearch.TextChanged += (_, _) => { Filter(_personsGrid, _personsSearch.Text); UpdateSalaryTotal(); };
        _paymentsSearch.TextChanged += (_, _) => Filter(_paymentsGrid, _paymentsSearch.Text);
        _scopeTabs.SelectedIndexChanged += (_, _) =>
        {
            if (_filling)
            {
                return;
            }

            // the payment current stays current when the tab picked still shows it; the remove command follows
            var key = KeyOf(SelectedPayment);
            FillPayments();
            SelectPayment(key);
            _removeLineButton.Enabled = Editable && !_busy && SelectedPayment is { IsByHand: true };
        };
        var reviewTab = new TabPage(UiText.ReviewTab);
        reviewTab.Controls.Add(_grids);
        var declarationTab = new TabPage(UiText.DeclarationTab);
        declarationTab.Controls.Add(_declaration);
        _tabs.TabPages.Add(reviewTab);
        _tabs.TabPages.Add(declarationTab);
        _outer.Panel1.Controls.Add(_tabs);
        _outer.Panel2.Controls.Add(_personPanel);
        _logPanel.Controls.Add(_log);
        ShowLogHeight();
        Controls.Add(_outer);
        Controls.Add(_logPanel);
        Controls.Add(_toolbar);
        Controls.Add(_menu);

        BuildPaymentColumns();

        _openStoreButton.Click += (_, _) => OpenStoreDialog(false);
        _newStoreButton.Click += (_, _) => OpenStoreDialog(true);
        _newCompanyButton.Click += (_, _) => NewCompany();
        _companyBox.SelectedIndexChanged += (_, _) => PeriodPicked(company: true);
        _yearBox.SelectedIndexChanged += (_, _) => PeriodPicked(company: false);
        _monthBox.SelectedIndexChanged += (_, _) => PeriodPicked(company: false);
        _importButton.Click += async (_, _) => await ImportAsync();
        _filesButton.Click += (_, _) => ShowFiles();
        _matchButton.Click += async (_, _) => await RunMatchAsync();
        _lookupButton.Click += async (_, _) => await RunLookupsAsync();
        _cancelButton.Click += (_, _) => _lookups?.Cancel();
        _addLineButton.Click += (_, _) => AddLine();
        _removeLineButton.Click += (_, _) => RemoveLine();
        _newPersonButton.Click += (_, _) => NewPerson();
        _closeCompanyButton.Click += (_, _) => CloseCompany();
        // a row made current in code raises the selection's event while the old row is still the current one, a click
        // the current cell's first: both are listened to, and a person is shown once per row (the row already shown is
        // skipped — ShowSelectedPerson notes it, the fills' own calls included)
        void RowChanged()
        {
            if (!_filling && !ReferenceEquals(_personsGrid.CurrentRow, _shownRow))
            {
                ShowSelectedPerson();
            }
        }

        _personsGrid.SelectionChanged += (_, _) => RowChanged();
        _personsGrid.CurrentCellChanged += (_, _) => RowChanged();
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
        _paymentsGrid.CellParsing += (_, e) => OnAmountTyped(e);
        _paymentsGrid.CellBeginEdit += (_, e) =>
        {
            // a row is split by typing a smaller amount on it; nothing to split where no amount was read, nor on a copy (a part
            // left on a copy is still taken out with 0)
            if (e.ColumnIndex >= 0 && _paymentsGrid.Columns[e.ColumnIndex].Name == ColAmount && _paymentsGrid.Rows[e.RowIndex].Tag is ReviewPayment payment && !AmountEditable(payment))
            {
                e.Cancel = true;
            }
        };
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
    internal ToolStripItem ImportButton => _importButton;

    internal ToolStripItem CloseCompanyButton => _closeCompanyButton;

    internal ToolStripComboBox CompanyBox => _companyBox;

    internal ToolStripComboBox MonthBox => _monthBox;

    internal Label SalaryTotalLabel => _salaryTotal;

    internal ToolStripMenuItem DeclareEachItem => _declareEachItem;

    internal ToolStripMenuItem DeclareTotalsItem => _declareTotalsItem;
    internal ToolStripMenuItem ImportLayoutsItem => _importLayoutsItem;
    internal ToolStripMenuItem LayoutsItem => _layoutsItem;
    internal ToolStripMenuItem LogDefaultItem => _logDefaultItem;
    internal ToolStripMenuItem LogOneRowItem => _logOneRowItem;
    internal ToolStripMenuItem SalaryOnlyItem => _salaryOnlyItem;
    internal Panel LogPanel => _logPanel;
    internal ListBox LogList => _log;
    internal MenuStrip Menus => _menu;
    internal TextBox PersonsSearch => _personsSearch;
    internal TextBox PaymentsSearch => _paymentsSearch;
    internal TabControl ScopeTabs => _scopeTabs;
    internal DataGridView PersonsGrid => _personsGrid;
    internal DataGridView PaymentsGrid => _paymentsGrid;
    internal TabControl Tabs => _tabs;
    internal DeclarationPanel Declaration => _declaration;
    internal PersonPanel PersonEditor => _personPanel;
    internal SplitContainer GridsSplit => _grids;
    internal SplitContainer OuterSplit => _outer;
    internal AppServices? Services => _services;
    internal ReviewView? View => _view;

    /// <summary>Answers a confirmation question in place of the message box (the window tests set it).</summary>
    internal Func<string, string, bool>? Confirming;

    /// <summary>Takes a failure in place of the message box (the window tests set it).</summary>
    internal Action<Exception>? Failing;

    /// <summary>Takes a plain notice in place of the message box (the window tests set it).</summary>
    internal Action<string>? Informing;

    /// <summary>Whether a store's copy was made by the update that failed here, in place of the look at the update's
    /// note and the copy's time — for a test.</summary>
    internal Func<string, string?>? FailedUpdateProbe;

    /// <summary>The copy of the store from before an upgrade that an update which failed here left behind (on the
    /// update's record), or null: only that copy is offered to go back to (decision 156).</summary>
    private string? FailedUpdateCopy(string storePath) => FailedUpdateProbe is { } probe ? probe(storePath) : Updater.FailedUpdateCopy(ExePath, storePath, SqliteStore.SchemaVersion);

    /// <summary>What became of the offer to go back to the store's copy, for what is said after it.</summary>
    private enum RestoreOffer
    {
        NotOffered,
        Declined,
        Failed,
    }

    private RestoreOffer _restoreOffer;
    private bool _restoredNow; // the store was put back from its copy and is being opened again

    /// <summary>Makes the services for a store path in place of the real ones — a test's registers, never the live
    /// sites. Set before the window is shown: the store opens as it loads.</summary>
    internal Func<string, AppServices>? ServicesFactory;

    /// <summary>The persons grid's "Deklareerimata" tooltip: each payment left out and why.</summary>
    private readonly GridTips _undeclaredTips;
    private readonly GridTips _problemTips;

    internal GridTips UndeclaredTips => _undeclaredTips;

    internal ToolStripProgressBar ProgressBar => _progressBar;

    internal ToolStripLabel ProgressLabel => _progressLabel;

    internal ToolStripButton StopButton => _stopButton;

    internal ToolStripMenuItem LookupButton => _lookupButton;

    private void Inform(string text, string title)
    {
        if (Informing is { } take)
        {
            take(text);
            return;
        }

        MessageBox.Show(this, text, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    internal void Reload() => ReloadView();

    internal void RefillCompanies() => FillCompanies();

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
            if (ResolveDirtyPanel() == Unsaved.Kept)
            {
                return; // the edits stay on the card; the update waits
            }

            ReloadView();
        }

        // the new version reads the settings and opens the store when it starts: both are let go of just before
        var storePath = _services?.StorePath;
        // the store the new version will open: the one in hand, else the one the settings remember (a store that could
        // not be reached at the start is still the one it upgrades); its copies as they stand before this update
        var upgraded = storePath ?? _storeOverride ?? _settings.StorePath;
        var copiesBefore = Updater.StoreCopiesOf(upgraded);
        CommitPendingNote(); // a note being typed is saved before the update may close the window
        using var dialog = new UpdateDialog(ExePath, _updateCheck.Folder, release, CurrentVersion, fromStart, () =>
        {
            KeepLayout(); // the new version opens as this one was left
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

        // the update did not go through. The copy the new version made of the store (one that was not there before, or
        // is another file now) goes on record: only a copy on record is offered to go back to (decision 156)
        if (upgraded is not null && Updater.CopyMadeSince(copiesBefore, upgraded) is { } made)
        {
            Updater.NoteStoreCopy(ExePath, upgraded, made);
        }

        if (_services is null && storePath is not null)
        {
            // the store is taken up again — put back from its copy when the new version had already upgraded it beyond
            // this build
            OpenStore(storePath);
        }
    }

    /// <summary>A store this build cannot open because a newer one upgraded it: offers the copy that build left beside
    /// it before upgrading, and takes the store up again from it.</summary>
    private bool OfferStoreRestore(string path, Exception failure)
    {
        // only the copy the update that failed here made, for the program that update put back (decision 156): any
        // other older copy of the program — a second computer not yet updated, an old folder, this one weeks after a
        // failed update — is told to update, never to go back to a copy of weeks ago
        _restoreOffer = RestoreOffer.NotOffered;
        if (_restoredNow || !SqliteStore.IsNewerThanThisBuild(failure) || FailedUpdateCopy(path) is not { } backup)
        {
            return false; // a copy put back just now that is itself too new is not offered again: the window says to update
        }

        var aside = SqliteStore.FreeAsidePathFor(SqliteStore.AsidePathFor(path)); // the very name the store will go aside under
        var made = File.GetLastWriteTime(backup);
        if (!Confirm($"{UiText.StoreNewerRestore}\n{backup}\n{UiText.StoreBackupMade(made)}\n\n{UiText.StoreNewerRestoreQuestion}\n{aside}", UiText.Title))
        {
            _restoreOffer = RestoreOffer.Declined;
            return false;
        }

        try
        {
            var setAside = SqliteStore.RestoreBackup(path, backup, aside);
            Log($"{UiText.StoreRestored}: {backup} ({UiText.StoreSetAside}: {setAside})");
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _restoreOffer = RestoreOffer.Failed;
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

        if (!SettleCard())
        {
            return;
        }

        var services = _services;
        var periodId = _view.Period.Id;
        var mode = DeclarationMode;
        await RunBusyAsync(async () =>
        {
            BuildReport report;
            try
            {
                report = await Task.Run(() => services.Declarer.Build(periodId, mode));
            }
            catch (DeclareException e) when (e.Code == PeriodDeclarer.ModeChanged)
            {
                // the month is on the server in the other cut: said in the accountant's words, with the way out
                Log($"{UiText.BuildLines}: {e.Message}");
                Inform(UiText.DeclareModeRefused, UiText.BuildLines);
                return;
            }
            catch (DeclareException e) when (e.Code == PeriodDeclarer.RunStale)
            {
                // a file linked or unlinked after the search: the lines would miss what the search never saw
                Log($"{UiText.BuildLines}: {e.Message}");
                Inform(UiText.RunStaleRefused, UiText.BuildLines);
                return;
            }
            catch (DeclareException e) when (e.Code == PeriodDeclarer.RatesMissing)
            {
                // a year this version has no rates for: said, never computed with another year's figures
                Log($"{UiText.BuildLines}: {e.Message}");
                Inform(UiText.RatesMissing, UiText.BuildLines);
                return;
            }

            Log($"{UiText.BuildLines} ({(mode == Core.Declare.DeclarationMode.TotalsPerType ? UiText.ModeTotals : UiText.ModeEach)}): {report.Count(LineChange.New)} uut, {report.Count(LineChange.Replaced)} asendatud, {report.Count(LineChange.Changed)} muudatust (M), {report.Count(LineChange.Cancelled)} tühistust (T), {report.Count(LineChange.Kept)} jäi, {report.Count(LineChange.Deleted)} kustutatud");
            foreach (var person in report.Persons)
            {
                if (person.Lines.Count > 0 || person.Problems.Count > 0)
                {
                    var month = person.MinimumDue > 0 ? $"; SM miinimum {UiText.Money(person.MinimumDue)} (osa {person.MinimumShare:0.##}), puudujääk {UiText.Money(person.MinimumShortfall)}" : "";
                    Log($"   {person.Name}: maksuvaba {UiText.Money(person.ExemptionOffered)} → üle {UiText.Money(person.ExemptionLeft)}{month}");
                }

                foreach (var line in person.Lines)
                {
                    Log($"      {line.Key} {UiText.ActionText(line.Action)} {UiText.ChangeText(line.Change)} — {Core.Report.ReportText.SourceText(line.Source)}{(line.Note is null ? "" : $" ({line.Note})")}");
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

        if (_personPanel.IsDirty)
        {
            if (ResolveDirtyPanel() == Unsaved.Kept) // the card's edits are in the export or dropped, never saved into a locked month after it
            {
                return; // or kept on the card: nothing is exported
            }

            ReloadView(); // the card and the grids show what the store holds, whichever way it went — even if the export stops here
            if (_services is null || _view is null)
            {
                return;
            }
        }

        if (_view.Editable && _view.RunIsStale)
        {
            // a file linked or unlinked after the search: the drafts miss (or double) payments, whatever the build would say
            Log($"{UiText.Export}: {UiText.RunStale}");
            Inform(UiText.RunStaleExport, UiText.Export);
            return;
        }

        // The lines are those "Koosta read" made: a review changed after it (a card, a type, a link) would not be in them.
        var checkServices = _services;
        var checkPeriod = _view.Period.Id;
        var mode = DeclarationMode;
        var draftsCut = checkServices.Declarer.DraftsCut(checkPeriod);
        if (draftsCut is { } cut && cut != mode)
        {
            // the Seaded cut switched after "Koosta read": the drafts are of the other cut — said as such, not as a review change
            Log($"{UiText.Export}: {(cut == Core.Declare.DeclarationMode.TotalsPerType ? UiText.ModeTotals : UiText.ModeEach)} → {(mode == Core.Declare.DeclarationMode.TotalsPerType ? UiText.ModeTotals : UiText.ModeEach)}");
            if (!Confirm(UiText.CutChangedSinceBuild, UiText.Export))
            {
                return;
            }
        }

        // the review's own changes are checked in the drafts' own cut, whatever Seaded says now
        var checkMode = draftsCut ?? mode;
        bool? current = null;
        await RunBusyAsync(async () => current = await Task.Run(() => checkServices.Declarer.DraftsCurrent(checkPeriod, checkMode)));
        if (current is null)
        {
            Log(UiText.ReviewNotChecked);
        }
        else if (current == false && !Confirm(UiText.ReviewChangedSinceBuild, UiText.Export))
        {
            return;
        }

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
        IReadOnlyDictionary<Core.Model.Section, string>? reasons = null;
        if (_services.Submitter.NeedsCorrectionReason(_view.Period.Id))
        {
            // one reason for each section the file will carry (decision 154)
            using var dialog = new CorrectionReasonDialog(_services.Submitter.DraftSections(_view.Period.Id));
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            reasons = dialog.Reasons;
        }

        var services = _services;
        var periodId = _view.Period.Id;
        var folder = folders.SelectedPath;
        ExportResult? result = null;
        await RunBusyAsync(async () =>
        {
            result = await Task.Run(() => services.Submitter.Export(periodId, folder, reasons));
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
            if (ResolveDirtyPanel() == Unsaved.Kept)
            {
                return; // the edits stay on the card; no report is written
            }

            ReloadView(); // whichever way it went, the grid shows what the store holds before the folder is asked
            if (_services is null || _view is null)
            {
                return;
            }
        }

        if (_view.Editable && _view.RunIsStale)
        {
            // the search did not see every linked file (or there was none): the workbook would miss payments — said, the accountant decides
            Log($"{UiText.ExportReport}: {UiText.RunStale}");
            if (!Confirm(UiText.RunStaleReport, UiText.ExportReport))
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
            Core.Declare.WithdrawnFile? file = null;
            Declare(s => file = s.Submitter.Withdraw(submission.Id, _settings.ExportFolder), $"{UiText.Withdraw}: {submission.FileName}");
            if (file is not null)
            {
                Log(UiText.WithdrawnFileNote(file)); // what became of the file written: set aside, or left where a mistaken upload could find it
            }
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

        if (!SettleCard()) // before the report is read into the store
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
                foreach (var general in outcome.General)
                {
                    Log("   " + UiText.GeneralText(general)); // a finding about the file or a section, on no line
                }
            }
        });
        ReloadView();
    }

    private bool Editable => _view?.Editable == true;

    private bool HasCompany => _services is not null && _companyBox.SelectedItem is Choice;

    private Company? SelectedCompany => _services is not null && _companyBox.SelectedItem is Choice c ? _services.Store.FindCompany(c.Value) : null;

    private ReviewPerson? SelectedPerson => _personsGrid.CurrentRow?.Tag as ReviewPerson;

    private ReviewPayment? SelectedPayment => _paymentsGrid.CurrentRow?.Tag as ReviewPayment;

    private static PaymentKey? KeyOf(ReviewPayment? payment) => payment is null ? null : PaymentKey.Of(payment);

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // the card's width and the persons grid's height as they were left, else the defaults
        _outer.SplitterDistance = WindowLayout.Distance(_settings.CardWidth is { } card ? _outer.Width - _outer.SplitterWidth - card : null,
            Math.Max(400, _outer.Width - 440), _outer.Width, _outer.SplitterWidth, LeastPanel);
        _grids.SplitterDistance = WindowLayout.Distance(_settings.PersonsGridHeight, Math.Max(120, _grids.Height / 3), _grids.Height, _grids.SplitterWidth, LeastPanel / 2);
        // a fixed panel keeps its size only while it fits: a shorter or narrower window never pushes the other panel and
        // the splitter out of sight
        _outer.SizeChanged += (_, _) => FitSplitter(_outer, LeastPanel);
        _grids.SizeChanged += (_, _) => FitSplitter(_grids, LeastPanel / 2);
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

    /// <summary>The least a panel of a splitter keeps when the layout is restored.</summary>
    private const int LeastPanel = 160;

    /// <summary>The window's last state that was not minimized: what it comes back to, kept when it is closed minimized.</summary>
    private FormWindowState _lastShownState = FormWindowState.Normal;

    private void TrackShownState()
    {
        if (WindowState != FormWindowState.Minimized)
        {
            _lastShownState = WindowState;
        }
    }

    /// <summary>Moves a splitter back inside its container when the container got too small for the fixed panel; left
    /// alone while minimized or while the container is too small for both panels' least.</summary>
    private void FitSplitter(SplitContainer split, int least)
    {
        var length = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
        if (WindowState == FormWindowState.Minimized || length <= 2 * least + split.SplitterWidth)
        {
            return;
        }

        var fitted = WindowLayout.Distance(split.SplitterDistance, split.SplitterDistance, length, split.SplitterWidth, least);
        if (fitted != split.SplitterDistance)
        {
            split.SplitterDistance = fitted;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        TrackShownState();
    }

    /// <summary>The window's size and place and its splitters, into the settings (saved with them): the normal size of a
    /// maximized window; nothing of the splitters from a minimized one, whose panels have no size.</summary>
    private void KeepLayout()
    {
        TrackShownState();
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            (_settings.WindowX, _settings.WindowY, _settings.WindowWidth, _settings.WindowHeight) = (bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }

        _settings.WindowMaximized = _lastShownState == FormWindowState.Maximized; // a minimized window comes back as it was before
        if (WindowState != FormWindowState.Minimized)
        {
            _settings.CardWidth = _outer.Width - _outer.SplitterDistance - _outer.SplitterWidth;
            _settings.PersonsGridHeight = _grids.SplitterDistance;
        }
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

        if (!_handedOver && !SettleCard())
        {
            e.Cancel = true; // the edits stay on the card, the window open
            return;
        }

        if (!_handedOver)
        {
            KeepLayout();
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
            OpenChosenStore(path);
        }
    }

    /// <summary>Opens a store as the dialog's choice does: remembered from here on. The dialog itself is modal; a test
    /// comes in here.</summary>
    internal void OpenChosenStore(string path) => OpenStore(path, chosen: true);

    /// <param name="chosen">The accountant chose the store in the window: it is remembered from here on, a one-off start's
    /// store no longer standing in the way.</param>
    private void OpenStore(string path, bool chosen = false)
    {
        CommitPendingNote(); // a note being typed goes to the store it was typed for, before that store closes
        if (!SettleCard()) // against the store it was typed for, before that store closes
        {
            return;
        }

        // another store is opened before the one in hand is let go of: a file that cannot be opened leaves the window
        // as it was — the store, the company, the month, the grids (decision 149). The same file again (and no store in
        // hand) goes the old way round: one file is not opened twice.
        var inHand = _services;
        var another = inHand is not null && !FileIdentity.Same(path, inHand.StorePath);
        if (!another)
        {
            _lookups?.Cancel();
            inHand?.Dispose();
            _services = null;
            _view = null;
            _period = null;
        }

        AppServices opened;
        try
        {
            opened = ServicesFactory?.Invoke(path) ?? new AppServices(path);
        }
        catch (Exception e) when (OfferStoreRestore(path, e))
        {
            _restoredNow = true;
            try
            {
                OpenStore(path, chosen); // the copy put back opens as this build's
            }
            finally
            {
                _restoredNow = false;
            }

            return;
        }
        catch (Exception e)
        {
            if (SqliteStore.IsNewerThanThisBuild(e))
            {
                // a newer version of the program upgraded the store, said in the accountant's words: this copy is to be
                // updated — or, after the update that failed here, the copy is there to go back to (declined just now);
                // a restore that could not be done has said why itself
                Log($"{UiText.Problem}: {e.Message}");
                if (_restoreOffer != RestoreOffer.Failed)
                {
                    Inform(_restoreOffer == RestoreOffer.Declined ? UiText.StoreNewerDeclined : UiText.StoreNewerUpdate, UiText.Title);
                }

                _restoreOffer = RestoreOffer.NotOffered;
            }
            else
            {
                Fail(e);
            }

            if (!another)
            {
                _storeLabel.Text = UiText.NoStore;
                FillCompanies();
            }
            else
            {
                ReloadView(); // the store in hand shown afresh: a card settled on the way (saved or dropped) and the grids agree with it
            }

            return;
        }

        if (another)
        {
            _lookups?.Cancel();
            inHand!.Dispose();
            _view = null;
            _period = null;
        }

        Updater.ForgetStoreCopy(ExePath, path, SqliteStore.SchemaVersion); // opened by a build older than a failed update's upgrade: that episode is over
        _services = opened;
        _services.Workspace.TotalsPerType = _settings.DeclareTotalsPerType;

        _storeLabel.Text = path;
        if (chosen)
        {
            _oneOff = false; // chosen in the window: remembered again, with the company and the month shown in it
        }

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

        // the company last shown, else the first; none when the accountant closed the company (an empty setting)
        var wanted = Choice.IndexOf(_companyBox.Items.Cast<Choice>(), _settings.LastCompany);
        _companyBox.SelectedIndex = _companyBox.Items.Count == 0 || _settings.LastCompany is "" ? -1 : Math.Max(0, wanted);
        _filling = false;
        if (_companyBox.Items.Count == 0 && _services is not null)
        {
            Log(UiText.NoCompany);
        }
        else if (_companyBox.SelectedIndex < 0 && _services is not null)
        {
            Log(UiText.CompanyClosedHint); // closed earlier (also in another store): said, so that the empty window is no riddle
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
        var year = _settings.LastYear ?? previous.Year;
        var month = _settings.LastMonth ?? previous.Month;
        // the years go back to the company's oldest period, two years back, or the month last chosen — whichever is
        // earliest — so the month chosen before a switch of company is still there after it
        var firstYear = Math.Min(Math.Min(periods.Count == 0 ? today.Year - 2 : periods.Min(p => p.Year), today.Year - 2), year);
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

        try
        {
            _period = _services.Store.GetOrCreatePeriod(company.Id, key.Year, key.Month);
        }
        catch (Exception e)
        {
            Fail(e); // said in the window, not the crash handler's; the callers stop on null
            return null;
        }

        return _period;
    }

    /// <summary>The declaration's cut, as the settings keep it.</summary>
    private Core.Declare.DeclarationMode DeclarationMode => _settings.DeclareTotalsPerType ? Core.Declare.DeclarationMode.TotalsPerType : Core.Declare.DeclarationMode.PerPayment;

    /// <summary>The Seaded menu's choice: kept in the settings at once, shown as the check mark, said in the log. The
    /// lines follow at the next "Koosta read".</summary>
    private void SetDeclarationMode(bool totals)
    {
        if (_settings.DeclareTotalsPerType == totals)
        {
            return;
        }

        _settings.DeclareTotalsPerType = totals;
        SaveSettings();
        ShowDeclarationMode();
        if (_services is not null)
        {
            _services.Workspace.TotalsPerType = totals;
            ReloadView(); // the persons' undeclared income follows the cut (a refused payment holds its type)
        }

        Log($"{UiText.DeclareModeChanged} {(totals ? UiText.ModeTotals : UiText.ModeEach)} ({UiText.BuildLines})");
    }

    /// <summary>The log's default height, in pixels: about nine lines.</summary>
    private const int LogDefaultHeight = 150;

    /// <summary>The Vaade menu's choice: the log at its default height or one line high (the latest line still shown,
    /// the list scrolling to it); kept in the settings at once.</summary>
    private void SetLogOneRow(bool oneRow)
    {
        if (_settings.LogOneRow == oneRow)
        {
            return;
        }

        _settings.LogOneRow = oneRow;
        SaveSettings();
        ShowLogHeight();
    }

    /// <summary>The Vaade menu's choice: the persons grid with or without its columns per source (only "PALK NETO
    /// KOKKU" of the income then); kept in the settings at once, the grid's rows left as they are.</summary>
    private void SetPersonsSalaryOnly(bool salaryOnly)
    {
        _settings.PersonsSalaryOnly = salaryOnly;
        SaveSettings();
        _salaryOnlyItem.Checked = salaryOnly;
        if (salaryOnly && _personsGrid.CurrentCell is { } current && _personsGrid.Columns[current.ColumnIndex].Name.StartsWith("src:", StringComparison.Ordinal)
            && _personsGrid.Columns["total"] is { } total)
        {
            _personsGrid.CurrentCell = _personsGrid.Rows[current.RowIndex].Cells[total.Index]; // the person stays selected
        }

        foreach (DataGridViewColumn column in _personsGrid.Columns)
        {
            if (column.Name.StartsWith("src:", StringComparison.Ordinal))
            {
                column.Visible = !salaryOnly;
            }
        }

        // the search matches the text shown: with the source columns gone or back, the rows shown and the total follow
        if (_personsGrid.Rows.Count > 0)
        {
            Filter(_personsGrid, _personsSearch.Text);
            UpdateSalaryTotal();
        }
    }

    private void ShowLogHeight()
    {
        _logDefaultItem.Checked = !_settings.LogOneRow;
        _logOneRowItem.Checked = _settings.LogOneRow;
        // one line: exactly the list's line and its border; no horizontal scrollbar there, which would cover the line
        // (a long line is cut at the window's edge, and the default height shows it whole)
        _log.HorizontalScrollbar = !_settings.LogOneRow;
        _logPanel.Height = _settings.LogOneRow ? _log.ItemHeight + 2 * SystemInformation.Border3DSize.Height : LogDefaultHeight;
        _log.TopIndex = Math.Max(0, _log.Items.Count - 1);
    }

    private void ShowDeclarationMode()
    {
        _declareEachItem.Checked = !_settings.DeclareTotalsPerType;
        _declareTotalsItem.Checked = _settings.DeclareTotalsPerType;
    }

    /// <summary>"Sulge ettevõte": no company shown, so that the next import goes to the employer the files' TÖR export
    /// names (found or created) without the question of switching. The choice stands until a company is chosen.</summary>
    private void CloseCompany()
    {
        if (!HasCompany)
        {
            return;
        }

        if (!SettleCard())
        {
            return;
        }

        _settings.LastCompany = "";
        SaveSettings();
        _filling = true;
        _companyBox.SelectedIndex = -1;
        _filling = false;
        Log(UiText.CompanyClosed);
        FillPeriods();
        ReloadView();
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
            if (dialog.PayerCodeType is { } type)
            {
                company = company with { PayerCodeType = type };
                _services.Store.UpdateCompany(company);
            }

            Log(UiText.CompanySaved(company.Name, company.RegistryCode, Core.Xbrl.PayerCodes.DescriptionOf(company.RegistryCode, company.PayerCodeType)));
            _settings.LastCompany = company.RegistryCode;
            FillCompanies();
        }
        catch (Exception e)
        {
            Fail(e);
        }
    }

    /// <summary>The company shown, changed on its card: the name and how the file labels its code (decision 139).</summary>
    private void EditCompany()
    {
        if (_services is null || SelectedCompany is not { } company)
        {
            return;
        }

        using var dialog = new CompanyDialog(company);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var changed = company with { Name = dialog.CompanyTitle, PayerCodeType = dialog.PayerCodeType };
            _services.Store.UpdateCompany(changed);
            Log(UiText.CompanySaved(changed.Name, changed.RegistryCode, Core.Xbrl.PayerCodes.DescriptionOf(changed.RegistryCode, changed.PayerCodeType)));
            FillCompanies();
        }
        catch (Exception e)
        {
            Fail(e);
        }
    }

    // ---------------------------------------------------------------- the view

    private (int Company, int Year, int Month) _picked = (-1, -1, -1); // the company and the month the view was last loaded for

    /// <summary>Another company, year or month picked: the card's unsaved edits are settled first, and kept edits put
    /// the pickers back where they were — nothing is loaded or remembered for a switch called off (decision 145).</summary>
    private void PeriodPicked(bool company)
    {
        if (_filling)
        {
            return;
        }

        if (!SettleCard())
        {
            _filling = true;
            try
            {
                // the year's and the month's lists are still the old company's: they are rebuilt only after the settling
                _companyBox.SelectedIndex = _picked.Company < _companyBox.Items.Count ? _picked.Company : -1;
                _yearBox.SelectedIndex = _picked.Year < _yearBox.Items.Count ? _picked.Year : -1;
                _monthBox.SelectedIndex = _picked.Month < _monthBox.Items.Count ? _picked.Month : -1;
            }
            finally
            {
                _filling = false;
            }

            return;
        }

        if (company)
        {
            FillPeriods();
        }

        ReloadView();
    }

    private void ReloadView(Guid? keepPerson = null, PaymentKey? keepPayment = null)
    {
        CommitPendingNote(); // a Märkus being typed is saved before the rows go
        if (_busy)
        {
            return; // the store belongs to the run until it ends; the run's end reloads
        }

        _picked = (_companyBox.SelectedIndex, _yearBox.SelectedIndex, _monthBox.SelectedIndex);

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
        var sort = SortOf(_personsGrid);
        _undeclaredTips.Clear();
        _problemTips.Clear();
        _personsGrid.Rows.Clear();
        _personsGrid.Columns.Clear();
        if (_view is null)
        {
            RebuildScopes(null);
            UpdateSalaryTotal();
            _filling = false;
            ShowSelectedPerson();
            return;
        }

        RebuildScopes(_view);
        _personsGrid.Columns.Add(Column("code", UiText.PersonalCode));
        _personsGrid.Columns.Add(Column("last", UiText.LastName));
        _personsGrid.Columns.Add(Column("first", UiText.FirstName));
        var exemption = Column("exemption", UiText.Exemption, money: true);
        exemption.DefaultCellStyle.NullValue = UiText.No; // none applied: "ei", sorted below every amount
        _personsGrid.Columns.Add(exemption);
        foreach (var source in _view.Sources)
        {
            var sourceColumn = Column("src:" + source, source == ReviewWorkspace.ByHandSource ? UiText.ByHand : source, money: true);
            sourceColumn.Visible = !_settings.PersonsSalaryOnly; // the Vaade menu's choice
            _personsGrid.Columns.Add(sourceColumn);
        }

        _personsGrid.Columns.Add(Column("total", UiText.SalaryTotal, money: true));
        _personsGrid.Columns.Add(Column("undeclared", UiText.Undeclared, money: true));
        _personsGrid.Columns.Add(Column("residency", UiText.Residency));
        _personsGrid.Columns.Add(Column("pension", UiText.Pension));
        _personsGrid.Columns.Add(Column("checked", UiText.CheckedOn));
        _personsGrid.Columns.Add(Column("start", UiText.EmploymentStart, date: true));
        _personsGrid.Columns.Add(Column("type", UiText.EmploymentType));
        _personsGrid.Columns.Add(Column("state", UiText.EntryState));
        _personsGrid.Columns.Add(Column("end", UiText.EmploymentEnd, date: true));
        _personsGrid.Columns.Add(Column("pause", UiText.PauseStart, date: true));
        _personsGrid.Columns.Add(Column("pauseEnd", UiText.PauseEnd, date: true));
        _personsGrid.Columns.Add(Column("declaration", UiText.DeclarationLabel));
        _personsGrid.Columns.Add(Column("problems", UiText.Problems));

        DataGridViewRow? keepRow = null; // the row itself, since a sort moves the indexes
        foreach (var person in _view.Persons)
        {
            var values = new List<object?>();
            if (person.IsUnmatched)
            {
                values.AddRange(["", UiText.UnmatchedRow, "", null]);
                values.AddRange(_view.Sources.Select(_ => (object?)null));
                values.AddRange([null, UndeclaredSum(person), "", "", "", null, "", "", null, null, null, "", ProblemCodes(person)]);
            }
            else
            {
                var p = person.Person;
                values.AddRange([person.PersonalCode ?? "", person.LastName, person.FirstName, ExemptionValue(p)]);
                values.AddRange(_view.Sources.Select(s => MoneyOrBlank(person.SalaryBySource.TryGetValue(s, out var sum) ? sum : null)));
                values.Add(person.SalaryTotal);
                values.Add(UndeclaredSum(person));
                values.Add(p is null ? "" : UiText.ResidencyText(p));
                values.Add(p is null ? "" : UiText.PensionText(p));
                values.Add(p is null ? "" : CheckedText(p));
                values.Add(person.Register?.Entry.StartDate?.ToDateTime(TimeOnly.MinValue));
                values.Add(p is { TypeFromCard: true } && EmploymentTypes.Find(p.TorEmploymentTypeId) is { } cardType ? cardType.Name : person.Register?.Entry.Type ?? EmploymentTypes.Find(p?.TorEmploymentTypeId)?.Name ?? "");
                values.Add(person.Register?.Entry.State ?? "");
                values.Add(EmploymentEndValue(person.Register));
                values.Add(PausedEntry(person.Register)?.PauseStart?.ToDateTime(TimeOnly.MinValue));
                values.Add(PausedEntry(person.Register)?.PauseEnd?.ToDateTime(TimeOnly.MinValue));
                values.Add(p is null ? "" : UiText.DeclarationShort(p.Declaration));
                values.Add(ProblemCodes(person));
            }

            var index = _personsGrid.Rows.Add(values.ToArray());
            var row = _personsGrid.Rows[index];
            row.Tag = person;
            _undeclaredTips.Set(row, UiText.UndeclaredTip(person.Undeclared));
            _problemTips.Set(row, string.Join(Environment.NewLine, person.Problems.Select(x => $"{x.Code}: {x.Message}")));
            if (person.IsUnmatched)
            {
                row.DefaultCellStyle.Font = new Font(_personsGrid.Font, FontStyle.Italic);
                if (person.Undeclared.Count > 0)
                {
                    row.DefaultCellStyle.BackColor = Color.MistyRose;
                }

                row.Cells["exemption"].Style.NullValue = null; // an empty text would count as the default and inherit the column's "ei"
                if (keepUnmatched)
                {
                    keepRow = row;
                }
            }
            else if (person.Undeclared.Any(u => !u.ByChoice))
            {
                row.DefaultCellStyle.BackColor = Color.MistyRose; // income the month declares not, and no one chose so
            }

            else if (person.Problems.Count > 0)
            {
                row.DefaultCellStyle.BackColor = Color.LightYellow;
            }

            if (PausedEntry(person.Register) is { } paused && UiText.PauseTip(paused.PauseReason, paused.PauseEnd) is { Length: > 0 } tip)
            {
                row.Cells["pause"].ToolTipText = tip;
            }

            if (person.Person is null && !person.IsUnmatched)
            {
                row.Cells["exemption"].Style.NullValue = null; // nothing to say of a person the store does not hold
            }

            if (keepPerson is { } id && person.PersonId == id)
            {
                keepRow = row;
            }
        }

        if (_personsGrid.Rows.Count > 0)
        {
            Resort(_personsGrid, sort);
            Filter(_personsGrid, _personsSearch.Text);
            var target = keepRow is { Visible: true } ? keepRow : _personsGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Visible);
            if (target is not null)
            {
                _personsGrid.CurrentCell = target.Cells[0];
            }
        }

        UpdateSalaryTotal();
        _filling = false;
        ShowSelectedPerson();
        if (_view.RowsLeftOut > 0)
        {
            Log(UiText.RowsLeftOut(_view.RowsLeftOut));
        }
    }

    /// <summary>The payments tabs: the person picked, every payment, one tab per category of the run's catalog, the
    /// payments without a category, and the rows the run found more than one category in. Rebuilt only when the
    /// categories changed, the tab picked kept.</summary>
    private void RebuildScopes(ReviewView? view)
    {
        var wanted = new List<(string Text, Scope Tag)> { (UiText.ScopeSelectedPerson, new Scope(ScopeSelected, null)), (UiText.ScopeAll, new Scope(ScopeAll, null)) };
        if (view is not null)
        {
            wanted.AddRange(view.Catalog.Categories.Select(c => (c.Name, new Scope(ScopeCategory, c.Name))));
            wanted.Add((Core.Report.ReportText.UnknownCategorySheet, new Scope(ScopeUnknown, null)));
            wanted.Add((UiText.ScopeConflict, new Scope(ScopeConflict, null)));
        }

        var current = _scopeTabs.TabPages.Cast<TabPage>().Select(p => (p.Text, (Scope)p.Tag!)).ToList();
        if (current.SequenceEqual(wanted))
        {
            return;
        }

        var keep = _scopeTabs.SelectedTab?.Tag as Scope;
        _scopeTabs.TabPages.Clear();
        foreach (var (text, tag) in wanted)
        {
            _scopeTabs.TabPages.Add(new TabPage(text) { Tag = tag });
        }

        _scopeTabs.SelectedTab = _scopeTabs.TabPages.Cast<TabPage>().FirstOrDefault(p => Equals(p.Tag, keep)) ?? _scopeTabs.TabPages[0];
    }

    private Scope CurrentScope => _scopeTabs.SelectedTab?.Tag as Scope ?? new Scope(ScopeSelected, null);

    /// <summary>The payments the payments grid shows under the tab picked.</summary>
    private IReadOnlyList<ReviewPayment> ScopedPayments(ReviewPerson? person)
    {
        if (_view is null)
        {
            return [];
        }

        var scope = CurrentScope;
        var all = _view.Persons.SelectMany(p => p.Payments);
        return scope.Kind switch
        {
            ScopeAll => all.ToList(),
            ScopeUnknown => all.Where(p => p.Category is null).ToList(),
            ScopeConflict => all.Where(p => p.ConflictingCategories.Count > 0).ToList(), // settled by hand or not: the tab is where they are checked
            ScopeCategory => all.Where(p => string.Equals(p.Category, scope.Category, StringComparison.OrdinalIgnoreCase)).ToList(),
            _ => person?.Payments ?? [],
        };
    }

    private void FillPayments() => FillPayments(SelectedPerson);

    private DataGridViewRow? _shownRow; // the persons grid's row the events last showed
    private bool _goingBack; // the selection is on its way back to the person whose edits stay on the card: nothing is shown or asked meanwhile

    private void ShowSelectedPerson()
    {
        if (_goingBack)
        {
            return;
        }

        _shownRow = _personsGrid.CurrentRow;
        var person = SelectedPerson;
        FillPayments(person);
        if (_busy && _personPanel.IsDirty)
        {
            // a run holds the store: the typed edits stay on the card, unasked, until the run's reload shows a person again
            return;
        }

        var target = person?.Person;
        var sameAsTyped = _personPanel.IsDirty && _personPanel.PresentedId is { } presented && presented == target?.Id;
        if (!sameAsTyped)
        {
            // what the accountant typed for another person is saved or dropped, never lost in silence — or kept: the
            // selection goes back to that person, the card as typed (after the grid's own event, which this is)
            if (_personPanel.IsDirty)
            {
                switch (ResolveDirtyPanel())
                {
                    case Unsaved.Kept:
                        _goingBack = true;
                        BeginInvoke(ReselectEditedPerson);
                        return;
                    case Unsaved.Saved:
                        BeginInvoke(() => ReloadView());
                        break;
                }
            }

            if (_view is { } view)
            {
                _personPanel.Present(target, Editable && !_busy, view.Period.Year, TaxRates.For(view.Period.Year) ?? _services?.Workspace.Rates ?? TaxRates.Year2026);
            }
            else
            {
                _personPanel.Present(target, Editable && !_busy);
            }
        }

        _removeLineButton.Enabled = Editable && !_busy && SelectedPayment is { IsByHand: true };
    }

    /// <summary>What became of the card's unsaved edits: saved, dropped on the accountant's word, or kept on the card —
    /// the accountant cancelled, or the save could not happen — in which case the action that asked is called off.</summary>
    internal enum Unsaved
    {
        Saved,
        Dropped,
        Kept,
    }

    /// <summary>Answers the unsaved-card question for a test (Yes, No or Cancel) in place of the message box; takes
    /// precedence over <see cref="Confirming"/>, which answers it Yes or No.</summary>
    internal Func<DialogResult>? AnsweringUnsaved;

    /// <summary>The card's unsaved edits settled before an action goes on: true when the card is clean (nothing typed,
    /// saved, or dropped), false when the edits stay on the card and the action must stop (decision 145).</summary>
    private bool SettleCard() => !_personPanel.IsDirty || ResolveDirtyPanel() != Unsaved.Kept;

    /// <summary>Asks about the panel's unsaved edits: Yes saves them, No drops them, Cancel keeps them on the card. A
    /// save that cannot happen (a field that is no number, the store's refusal) keeps them too: nothing typed is lost
    /// to an error box.</summary>
    private Unsaved ResolveDirtyPanel()
    {
        var answer = AnsweringUnsaved is { } ask ? ask()
            : Confirming is { } hook ? (hook(UiText.UnsavedPerson, UiText.PersonPanel) ? DialogResult.Yes : DialogResult.No)
            : MessageBox.Show(this, UiText.UnsavedPerson, UiText.PersonPanel, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.No || (answer == DialogResult.Yes && _services is null))
        {
            _personPanel.MarkClean(); // dropped on the accountant's word (or no store to save into)
            return Unsaved.Dropped;
        }

        if (answer != DialogResult.Yes || _personPanel.Collect() is not { } edited || !MrrPrefixAccepted(edited))
        {
            return Unsaved.Kept; // cancelled, a field is no number (the card said which), or the MRR code's prefix not stood by: the edits stay
        }

        try
        {
            var person = _services!.Workspace.SavePerson(edited, _personPanel.ConfirmResidency, _personPanel.ConfirmPension, _personPanel.Presented);
            Log($"{UiText.Save}: {person.LastName} {person.FirstName}");
            _personPanel.MarkClean();
            return Unsaved.Saved;
        }
        catch (Exception e)
        {
            Fail(e);
            return Unsaved.Kept;
        }
    }

    /// <summary>The persons grid back on the person whose edits stay on the card, after a selection change was called off.</summary>
    private void ReselectEditedPerson()
    {
        try
        {
            DataGridViewRow? Find() => _personPanel.PresentedId is { } id
                ? _personsGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Tag is ReviewPerson { Person.Id: var personId } && personId == id)
                : null;
            var row = Find();
            if (row is { Visible: false })
            {
                // the search hid the person being edited: the search goes, so the grid, the payments and the card are
                // one person's again (the filter's own row changes ask nothing: the way back is still on)
                _personsSearch.Text = "";
                row = Find();
            }

            if (row is { Visible: true } && !ReferenceEquals(row, _personsGrid.CurrentRow))
            {
                var column = _personsGrid.CurrentCell?.ColumnIndex is { } current && row.Cells[current].Visible ? current : row.Cells.Cast<DataGridViewCell>().First(c => c.Visible).ColumnIndex;
                _personsGrid.CurrentCell = row.Cells[column];
            }
        }
        finally
        {
            _goingBack = false;
            _shownRow = _personsGrid.CurrentRow;
            FillPayments(); // the payments of the person the grid is on, whichever that is (the row may be filtered away)
            _removeLineButton.Enabled = Editable && !_busy && SelectedPayment is { IsByHand: true };
        }
    }

    private void BuildPaymentColumns()
    {
        _paymentsGrid.Columns.Add(Column("document", UiText.Document));
        _paymentsGrid.Columns.Add(Column("date", UiText.Date, date: true));
        _paymentsGrid.Columns.Add(Column("payee", UiText.PayeeOnStatement));
        _paymentsGrid.Columns.Add(Column("description", UiText.Description));
        _paymentsGrid.Columns.Add(Column(ColAmount, UiText.Amount, money: true));
        _paymentsGrid.Columns.Add(Column("dc", UiText.DebitCredit));
        _paymentsGrid.Columns.Add(Column("currency", UiText.Currency));
        _paymentsGrid.Columns.Add(Column("verdict", UiText.Verdict));
        _paymentsGrid.Columns.Add(new DataGridViewComboBoxColumn { Name = ColCategory, HeaderText = UiText.Category, DisplayMember = nameof(Choice.Text), ValueMember = nameof(Choice.Value), FlatStyle = FlatStyle.Flat, Width = 130 });
        _paymentsGrid.Columns.Add(new DataGridViewComboBoxColumn { Name = ColType, HeaderText = UiText.PaymentType, DisplayMember = nameof(Choice.Text), ValueMember = nameof(Choice.Value), FlatStyle = FlatStyle.Flat, Width = 260 });
        _paymentsGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = ColDeclare, HeaderText = UiText.Declare, Width = 70 });
        _paymentsGrid.Columns.Add(new DataGridViewComboBoxColumn { Name = ColPerson, HeaderText = UiText.Person, DisplayMember = nameof(Choice.Text), ValueMember = nameof(Choice.Value), FlatStyle = FlatStyle.Flat, Width = 200 });
        _paymentsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = ColNote, HeaderText = UiText.Note, Width = 160 });
        _paymentsGrid.Columns.Add(Column("check", UiText.Check));
        foreach (DataGridViewColumn column in _paymentsGrid.Columns)
        {
            // the columns the accountant edits are not sorted (a pick would move the row under the mouse); the rest sort by a
            // header click — the amount too, whose edit is a question answered first and a reload after (decision 150)
            var editable = column.Name is ColCategory or ColType or ColDeclare or ColPerson or ColNote;
            column.ReadOnly = !editable && column.Name != ColAmount;
            if (editable)
            {
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
            }
        }
    }

    private void FillPayments(ReviewPerson? person)
    {
        _filling = true;
        var sort = SortOf(_paymentsGrid);
        _paymentsGrid.Rows.Clear();
        var payments = ScopedPayments(person);
        if (_view is null || (person is null && CurrentScope.Kind == ScopeSelected))
        {
            _filling = false;
            return;
        }

        // every value a row can hold is in its combo's list, so nothing shows as a blank by mistake
        var categories = new List<Choice> { new("", UiText.Default), new(None, UiText.NoCategory) };
        categories.AddRange(_view.Catalog.Categories.Select(c => new Choice(c.Name, c.Name)));
        categories.AddRange(payments.Select(p => p.Category).OfType<string>().Distinct(StringComparer.Ordinal).Where(c => categories.All(x => x.Value != c)).Select(c => new Choice(c, c)));
        var types = new List<Choice> { new("", UiText.Default) };
        types.AddRange(PaymentTypes.All.Select(t => new Choice(t.Code, $"{t.Code} {t.NameEt}")));
        types.AddRange(payments.Select(p => p.TypeCode).OfType<string>().Distinct(StringComparer.Ordinal).Where(c => types.All(x => x.Value != c)).Select(c => new Choice(c, c)));
        var persons = new List<Choice> { new("", UiText.Default), new(None, UiText.Nobody) };
        persons.AddRange(_view.StorePersons.Select(p => new Choice(p.Id.ToString("N"), $"{p.LastName} {p.FirstName} {p.PersonalCode ?? p.MrrCode ?? ""}".Trim())));
        ((DataGridViewComboBoxColumn)_paymentsGrid.Columns[ColCategory]!).DataSource = categories;
        ((DataGridViewComboBoxColumn)_paymentsGrid.Columns[ColType]!).DataSource = types;
        ((DataGridViewComboBoxColumn)_paymentsGrid.Columns[ColPerson]!).DataSource = persons;

        foreach (var payment in payments)
        {
            var verdict = payment.IsByHand ? UiText.ByHand : payment.VerdictText + (payment.Masks.Length > 0 ? $"  ({payment.Masks})" : "");
            var parts = payment.Checks.Select(c => c.Code).Concat(payment.Flags.Where(f => payment.Checks.All(c => c.Code != f))).ToList();
            if (payment.Candidates.Count > 0)
            {
                parts.Add($"({string.Join(", ", payment.Candidates)})");
            }

            var index = _paymentsGrid.Rows.Add(
                payment.IsByHand ? UiText.ByHand : payment.Reference,
                payment.Date is { } d ? d.ToDateTime(TimeOnly.MinValue) : null,
                payment.Payee,
                payment.Description ?? "",
                payment.AmountText is not null || payment.Flags.Contains(MatchProblem.BadAmount, StringComparer.Ordinal) ? null : payment.Amount,
                UiText.DebitCreditText(payment.Direction, payment.DirectionAssumed),
                payment.Currency ?? "",
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
            if (payment.DirectionAssumed)
            {
                row.Cells["dc"].ToolTipText = UiText.DirectionAssumedTip;
            }

            if (payment.AmountText is not null || payment.Flags.Contains(MatchProblem.BadAmount, StringComparer.Ordinal))
            {
                // an amount that could not be read: its text, else "?", shown in its place and sorted as none
                row.Cells["amount"].Style.NullValue = payment.AmountText ?? UiText.BadAmountMark;
            }

            if (payment.Part > 0)
            {
                row.Cells[ColAmount].ToolTipText = payment.IsPart ? UiText.PartTip(payment.RowAmount ?? 0m) : AmountEditable(payment) ? UiText.OwnShareTip(payment.RowAmount ?? 0m) : UiText.CopyShareTip(payment.RowAmount ?? 0m);
            }

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

            if (payment.Date is null && payment.DateText is { Length: > 0 } dateText)
            {
                row.Cells["date"].Style.NullValue = dateText; // the text the statement had, which no date was read from, shown and searchable, sorted as none
                row.Cells["date"].ToolTipText = dateText;
            }
        }

        _paymentsGrid.ReadOnly = !Editable || _busy;
        Resort(_paymentsGrid, sort);
        Filter(_paymentsGrid, _paymentsSearch.Text);
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
            if (row.Visible && row.Tag is ReviewPayment p && (wanted.RowId is { } r && !wanted.IsPart ? p.RowId == r && !p.IsPart : p.LineId is not null && p.LineId == wanted.LineId))
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
        var key = PaymentKey.Of(payment);
        BeginInvoke(() => ReloadView(keepPerson, key));
    }

    /// <summary>Whether the payment's amount is typed on: a hand line's; a row's when its amount was read and it is no copy
    /// of another (a part left on a copy still is, to be taken out with 0).</summary>
    private static bool AmountEditable(ReviewPayment payment) =>
        payment.IsByHand
        || (payment.AmountText is null && !payment.Flags.Contains(MatchProblem.BadAmount, StringComparer.Ordinal)
            && (payment.IsPart || !payment.Flags.Contains(MatchProblem.DuplicateRow, StringComparer.Ordinal)));

    /// <summary>The amount typed on a payment (decision 150), as the grid parses the edit it commits: the grid never takes
    /// the text itself — the cell keeps its value, the store decides and the reload shows what it holds. Once the edit is
    /// over, what the amount would do is asked, and done on yes — a row split, a part resized or removed, a hand line's
    /// amount changed — the view reloaded on the payment edited.</summary>
    private void OnAmountTyped(DataGridViewCellParsingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || _paymentsGrid.Columns[e.ColumnIndex].Name != ColAmount)
        {
            return;
        }

        var typed = e.Value?.ToString() ?? "";
        e.Value = _paymentsGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value;
        e.ParsingApplied = true;
        if (_filling || _services is null || _view is null || _paymentsGrid.Rows[e.RowIndex].Tag is not ReviewPayment payment)
        {
            return;
        }

        var text = new string(typed.Where(c => !char.IsWhiteSpace(c)).ToArray()); // the group separator of the shown amount is a space
        if (text.Length == 0)
        {
            return; // nothing typed: nothing changes
        }

        const NumberStyles plain = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
        if (!decimal.TryParse(text, plain, CultureInfo.CurrentCulture, out var amount) && !decimal.TryParse(text, plain, CultureInfo.InvariantCulture, out amount))
        {
            BeginInvoke(() => Fail(new ReviewException(ReviewWorkspace.AmountInvalid, UiText.AmountNotNumber)));
            return;
        }

        var services = _services;
        var keepPerson = SelectedPerson?.PersonId;
        var key = PaymentKey.Of(payment);
        BeginInvoke(() =>
        {
            try
            {
                if (services.Workspace.PlanAmount(payment, amount) is not { } change)
                {
                    return;
                }

                if (Confirm(UiText.AmountChangeQuestion(payment.IsByHand ? UiText.ByHand : payment.Reference, change), UiText.AmountChangeTitle))
                {
                    services.Workspace.SetAmount(payment, amount);
                    Log(UiText.AmountChanged(payment.IsByHand ? UiText.ByHand : payment.Reference, change));
                }
            }
            catch (ReviewException ex) when (UiText.AmountRefused(ex, payment.IsByHand) is { } words)
            {
                Fail(new ReviewException(ex.Code, words, ex.Figure)); // the refusal in the window's own words
            }
            catch (Exception ex)
            {
                Fail(ex);
            }

            ReloadView(keepPerson, key); // on no or a refusal as well: the cell shows what the store holds, not the text typed
        });
    }

    private void UpdateState()
    {
        var hasCompany = HasCompany;
        var editable = _view?.Editable ?? hasCompany;
        _stateLabel.Text = _view is null
            ? hasCompany ? UiText.NoPeriodYet : ""
            : $"{UiText.PeriodState(_view.Period.State)} — {(_view.Editable ? UiText.Editable : UiText.Locked)}{StateDates(_view.Period)}{(_view.Editable && _view.RunIsStale ? " — " + UiText.RunStale : "")}";
        // without a company the files' TÖR export may name one (found or created); with one shown, its month must be open
        _importButton.Enabled = !_busy && _services is not null && (hasCompany ? editable : SelectedPeriod is not null);
        _filesButton.Enabled = !_busy && _view is not null;
        _matchButton.Enabled = !_busy && Editable;
        _lookupButton.Enabled = !_busy && Editable && _services?.Pipeline.HasLookups == true; // a locked month's lookups would rewrite the facts other months are built with
        _addLineButton.Enabled = !_busy && hasCompany && editable;
        _newPersonButton.Enabled = !_busy && hasCompany;
        _newCompanyButton.Enabled = _services is not null && !_busy;
        _closeCompanyButton.Enabled = !_busy && hasCompany;
        _editCompanyButton.Enabled = !_busy && hasCompany;
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
        _updateSeparator.Visible = UpdatesEnabled;
        _updateButton.Enabled = !_busy && UpdatesEnabled && !_checkingUpdates;
        _declareEachItem.Enabled = _declareTotalsItem.Enabled = !_busy; // the review follows the cut: never switched under a run
        _importLayoutsItem.Enabled = _layoutsItem.Enabled = !_busy && _services is not null; // the layouts live in the store
        UseWaitCursor = _busy;
    }

    // ---------------------------------------------------------------- the runs

    private async Task ImportAsync()
    {
        if (_services is null || _busy || !(_view?.Editable ?? true))
        {
            return;
        }

        using var dialog = new OpenFileDialog { Multiselect = true, Filter = UiText.FileFilter, InitialDirectory = ImportFolder() };
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.FileNames.Length == 0)
        {
            return;
        }

        _settings.LastFolder = Path.GetDirectoryName(dialog.FileNames[0]);
        SaveSettings();
        await ImportFilesAsync(dialog.FileNames);
    }

    /// <summary>Imports the files into the month shown. The company is the one the files' TÖR export names: taken up
    /// (or created from the export's name and registry code) when none is shown, asked about when another is shown;
    /// without a TÖR export the files go to the company shown, and nowhere when there is none.</summary>
    internal async Task ImportFilesAsync(IReadOnlyList<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (_services is null || _busy || files.Count == 0 || SelectedPeriod is not { } month)
        {
            return;
        }

        // the files are read once, off the UI thread and under the busy state (no second import, no combo change
        // meanwhile); a file that cannot be read gets its own line below and never stops the others
        var services = _services;
        var shownCode = SelectedCompany?.RegistryCode;
        List<(string Name, byte[]? Bytes, string? Error)> read = [];
        Employer? employer = null;
        Exception? refused = null;
        await RunBusyAsync(async () =>
        {
            await Task.Run(() =>
            {
                foreach (var file in files)
                {
                    try
                    {
                        read.Add((Path.GetFileName(file), File.ReadAllBytes(file), null));
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        read.Add((Path.GetFileName(file), null, e.Message));
                    }
                }

                try
                {
                    employer = services.Pipeline.EmployerOf(read.Where(r => r.Bytes is not null).Select(r => (r.Name, r.Bytes!)), shownCode);
                }
                catch (Exception e)
                {
                    refused = e;
                }
            });
        });
        if (refused is not null)
        {
            Fail(refused);
            return;
        }

        if (employer is null && !HasCompany)
        {
            Inform(UiText.ImportNeedsEmployer, UiText.ImportFiles);
            return;
        }

        if (employer is not null && shownCode != employer.RegistryCode)
        {
            try
            {
                var exists = services.Store.FindCompany(employer.RegistryCode) is not null;
                if (HasCompany && !Confirm($"{UiText.ImportOtherEmployer}\n{employer.Name} ({employer.RegistryCode})\n\n{(exists ? UiText.ImportSwitchToExisting : UiText.ImportSwitchToNew)}", UiText.ImportFiles))
                {
                    return;
                }

                if (!SettleCard())
                {
                    return;
                }

                var (company, created) = services.Pipeline.EnsureCompany(employer);
                Log($"{(created ? UiText.CompanyCreated : UiText.CompanyTaken)}: {company.Name} ({company.RegistryCode})");
                // the combos are filled afresh from the settings: the company found and the month chosen stay what they are
                _settings.LastCompany = company.RegistryCode;
                _settings.LastYear = month.Year;
                _settings.LastMonth = month.Month;
                FillCompanies();
            }
            catch (Exception e)
            {
                Fail(e);
                return;
            }
        }

        if (!(_view?.Editable ?? true))
        {
            // the month the files would go to is past its draft for this company: said, not silently skipped
            Log($"{UiText.ImportFiles}: {UiText.ImportLockedMonth}");
            Inform(UiText.ImportLockedMonth, UiText.ImportFiles);
            return;
        }

        var period = EnsurePeriod();
        if (period is null)
        {
            return;
        }

        if (!SettleCard()) // before the import holds the store
        {
            return;
        }

        var periodId = period.Id;
        await RunBusyAsync(async () =>
        {
            // every file gets its line, whatever happened to the one before it
            var outcomes = await Task.Run(() => read.Select(f =>
            {
                try
                {
                    return f.Bytes is null
                        ? new ImportOutcome { FileName = f.Name, Error = f.Error }
                        : services.Pipeline.ImportFile(periodId, f.Name, f.Bytes);
                }
                catch (Exception e)
                {
                    return new ImportOutcome { FileName = f.Name, Error = e.Message };
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

        if (_view.Run is not null && !Confirm(UiText.RunReplacesRun, UiText.RunMatch))
        {
            return;
        }

        if (!SettleCard()) // before the run holds the store
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

        if (!Confirm(UiText.LookupsLive, UiText.RunLookups))
        {
            return;
        }

        if (!SettleCard()) // the registers are asked about the persons as saved, and the card shown afresh with their answers
        {
            return;
        }

        var services = _services;
        var periodId = _view.Period.Id;
        var progress = new Progress<LookupProgress>(p =>
        {
            if (_lookups is null)
            {
                return; // the run is over; a report posted late changes nothing
            }

            ShowProgress(UiText.LookupProgress(p.Done, p.Total), p.Done, p.Total);
            if (p.Outcome is { } outcome)
            {
                LogOutcome(outcome);
            }
        });
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

        if (!SettleCard()) // the register is asked about the person as saved, never about half-typed facts
        {
            return;
        }

        var services = _services;
        var period = _period;
        Person fresh;
        try
        {
            fresh = services.Store.Persons(person.CompanyId).FirstOrDefault(p => p.Id == person.Id) ?? person;
        }
        catch (Exception e)
        {
            Fail(e);
            return;
        }

        ShowProgress(UiText.LookupOne($"{fresh.LastName} {fresh.FirstName}"), null, null);
        await RunCancellableAsync(token => services.Pipeline.LookUpAsync(fresh, period, token), LogOutcome);
        ReloadView(person.Id);
    }

    private async Task RunCancellableAsync<T>(Func<CancellationToken, Task<T>> work, Action<T> report)
    {
        _lookups = new CancellationTokenSource();
        var token = _lookups.Token;
        _cancelButton.Enabled = true;
        _stopButton.Visible = true;
        _progressSeparator.Visible = true;
        try
        {
            await RunBusyAsync(async () => report(await work(token)));
        }
        finally
        {
            _cancelButton.Enabled = false;
            _lookups.Dispose();
            _lookups = null;
            HideProgress(); // on every way out: done, cancelled between two persons or inside a query, failed
        }
    }

    /// <summary>The toolbar's progress: the text, and the bar when the run knows how many it asks.</summary>
    private void ShowProgress(string text, int? done, int? total)
    {
        _progressSeparator.Visible = true;
        _progressLabel.Text = text;
        _progressLabel.Visible = true;
        if (total is { } of && done is { } count)
        {
            _progressBar.Maximum = Math.Max(1, of);
            _progressBar.Value = Math.Clamp(count, 0, _progressBar.Maximum);
            _progressBar.Visible = true;
        }
        else
        {
            _progressBar.Visible = false;
        }
    }

    private void HideProgress()
    {
        _progressSeparator.Visible = false;
        _progressLabel.Visible = false;
        _progressLabel.Text = "";
        _progressBar.Visible = false;
        _progressBar.Value = 0;
        _stopButton.Visible = false;
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

    /// <summary>A note's edit still open in the payments grid is committed (CellValueChanged → OnPaymentEdited), so a reload
    /// or the busy switch to read-only does not drop what was typed. Nothing while the grid is being filled.</summary>
    private void CommitPendingNote()
    {
        if (!_filling && _paymentsGrid.IsCurrentCellInEditMode)
        {
            if (_paymentsGrid.CurrentCell?.OwningColumn.Name == ColAmount)
            {
                _paymentsGrid.CancelEdit(); // an amount half typed is no split: it is asked only when the accountant ends the edit
                _paymentsGrid.EndEdit();
                return;
            }

            _paymentsGrid.EndEdit();
        }
    }

    private void SetBusy(bool busy)
    {
        if (busy && !_busy)
        {
            CommitPendingNote();
        }

        var wasBusy = _busy;
        _busy = busy;
        UpdateState();
        if (wasBusy && !busy)
        {
            ResetGridCursors();
        }
    }

    /// <summary>A grid keeps the wait cursor as its own: over a header's divider it saves its Cursor and writes back what
    /// it saw, the form's wait cursor while the work ran, and keeps it after the work is over. Every grid is set back to
    /// its default on the way out of the work.</summary>
    private void ResetGridCursors()
    {
        foreach (var grid in Descendants(this).OfType<DataGridView>())
        {
            grid.ResetCursor();
        }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var inner in Descendants(child))
            {
                yield return inner;
            }
        }
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

    /// <summary>An MTA non-resident code typed with what looks like a country code before it is asked about, since MTA
    /// takes the code without one and a code may yet begin with letters of its own (decision 155). True when there is
    /// nothing to ask, or the accountant says the code stands as typed.</summary>
    private bool MrrPrefixAccepted(Person edited)
    {
        var typed = edited.MrrCode?.Trim();
        return string.Equals(typed, _personPanel.Presented?.MrrCode?.Trim(), StringComparison.OrdinalIgnoreCase)
            || Core.Xbrl.MrrCodes.Problem(typed) is not null // refused by the save itself
            || Core.Xbrl.MrrCodes.CountryPrefix(typed) is not { } prefix
            || Confirm(UiText.MrrPrefixQuestion(typed!, prefix), UiText.PersonPanel);
    }

    private void SavePerson(Person edited, bool confirmResidency, bool confirmPension)
    {
        if (_services is null)
        {
            return;
        }

        if (!MrrPrefixAccepted(edited))
        {
            return; // the typed values stay on the card
        }

        try
        {
            var saved = _services.Workspace.SavePerson(edited, confirmResidency, confirmPension, _personPanel.Presented);
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

    /// <summary>The owner's statement layouts from a JSON file, saved through the store and read by from now on (decision 136).</summary>
    private void ImportLayouts()
    {
        if (_services is null || _busy)
        {
            return;
        }

        using var dialog = new OpenFileDialog { Filter = UiText.LayoutFilter, CheckFileExists = true, InitialDirectory = ImportFolder() };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var ids = _services.Pipeline.ImportLayouts(File.ReadAllText(dialog.FileName));
            Log(UiText.LayoutsImported(ids));
        }
        catch (Exception e)
        {
            Fail(e);
        }
    }

    private void ShowLayouts()
    {
        if (_services is null || _busy)
        {
            return;
        }

        using var dialog = new LayoutsDialog(_services, Log);
        dialog.ShowDialog(this);
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

    /// <summary>A read-only text column, sortable by a click on its header. A money column holds decimals shown with
    /// two decimals, a date column dates shown as dd.MM.yyyy, so that the sort is by value, not by text.</summary>
    private static DataGridViewTextBoxColumn Column(string name, string header, bool right = false, bool money = false, bool date = false) => new()
    {
        Name = name,
        HeaderText = header,
        ReadOnly = true,
        SortMode = DataGridViewColumnSortMode.Automatic,
        ValueType = money ? typeof(decimal) : date ? typeof(DateTime) : typeof(string),
        DefaultCellStyle =
        {
            Alignment = right || money ? DataGridViewContentAlignment.MiddleRight : DataGridViewContentAlignment.MiddleLeft,
            Format = money ? "N2" : date ? "dd.MM.yyyy" : "",
        },
    };

    /// <summary>A splitter the accountant can see and grab: a darker bar between the two panels, wide enough to hit,
    /// the panels keeping the window's own colour (a SplitContainer paints its splitter in its own background colour,
    /// which the panels otherwise inherit, so the bar disappears).</summary>
    private static void VisibleSplitter(SplitContainer split)
    {
        split.BackColor = SystemColors.ControlDark;
        split.Panel1.BackColor = SystemColors.Control;
        split.Panel2.BackColor = SystemColors.Control;
        split.SplitterWidth = 6;
    }

    /// <summary>The search box over a grid: what is typed hides every row with no cell containing it.</summary>
    private static Panel SearchBar(TextBox box)
    {
        var bar = new Panel { Dock = DockStyle.Top, Height = 28, Padding = new Padding(3) };
        var label = new Label { Text = UiText.Search, AutoSize = true, Location = new Point(4, 7) };
        box.Location = new Point(44, 3);
        bar.Controls.Add(label);
        bar.Controls.Add(box);
        return bar;
    }

    private static void Filter(DataGridView grid, string text) => FilterRows(grid, text.Trim(), Matches);

    /// <summary>Shows the rows that match the needle and hides the rest. The current row stays when it matches; else the
    /// first row shown becomes current (a hidden row can hold no current cell, and a grid with rows should have one).</summary>
    internal static void FilterRows(DataGridView grid, string needle, Func<DataGridViewRow, string, bool> matches)
    {
        var rows = grid.Rows.Cast<DataGridViewRow>().ToList();
        var shown = rows.Where(r => needle.Length == 0 || matches(r, needle)).ToList();
        foreach (var row in shown)
        {
            row.Visible = true; // first, so that the row made current is a visible one
        }

        var current = grid.CurrentCell?.OwningRow;
        if (current is null || !shown.Contains(current))
        {
            grid.CurrentCell = shown.FirstOrDefault()?.Cells[0];
        }

        foreach (var row in rows.Except(shown))
        {
            row.Visible = false;
        }
    }

    private static bool Matches(DataGridViewRow row, string needle) =>
        row.Cells.Cast<DataGridViewCell>().Any(c => c.OwningColumn.Visible && c.FormattedValue?.ToString()?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true); // the text shown, a hidden column's not

    /// <summary>The sort a grid stands in, to put it back after the rows are made afresh.</summary>
    private static (string? Column, ListSortDirection Direction) SortOf(DataGridView grid) =>
        (grid.SortedColumn?.Name, grid.SortOrder == SortOrder.Descending ? ListSortDirection.Descending : ListSortDirection.Ascending);

    private static void Resort(DataGridView grid, (string? Column, ListSortDirection Direction) sort)
    {
        if (sort.Column is { } name && grid.Columns.Contains(name) && grid.Columns[name] is { SortMode: DataGridViewColumnSortMode.Automatic } column)
        {
            grid.Sort(column, sort.Direction);
        }
    }

    private static object? MoneyOrBlank(decimal? amount) => amount is { } a ? a : null;

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

    /// <summary>The line under the persons grid: the "PALK NETO KOKKU" column's total over every person (what the
    /// Vahearuanne adds up to), and the total of the rows the search leaves shown when that differs from it.</summary>
    private void UpdateSalaryTotal()
    {
        if (_view is null)
        {
            _salaryTotal.Text = "";
            return;
        }

        var total = _view.Persons.Where(p => !p.IsUnmatched).Sum(p => p.SalaryTotal);
        var rows = _personsGrid.Rows.Cast<DataGridViewRow>().ToList();
        var shown = rows.Any(r => !r.Visible)
            ? rows.Where(r => r.Visible).Select(r => r.Tag as ReviewPerson).Where(p => p is { IsUnmatched: false }).Sum(p => p!.SalaryTotal) as decimal?
            : (decimal?)null;
        _salaryTotal.Text = UiText.SalaryTotalLine(total, shown == total ? null : shown); // the bracket only when the rows shown add up to something else
    }

    /// <summary>"Maksuvaba": the monthly exemption applied at this payer in the month's year — the person's own figure,
    /// else the allowance in force — or none (shown as "ei") when none is applied.</summary>
    private decimal? ExemptionValue(Person? person) =>
        person is null || !person.BasicExemptionApplied ? null : person.ExemptionMonthInForce(_view!.Period.Year, _services?.Workspace.Rates ?? TaxRates.Year2026);

    /// <summary>"Töötamise lõpu aeg": the day the register entry standing for the person ended — shown only for an
    /// ended employment (kande olek 4, or an end on or before the month's last day), so a fixed-term contract still
    /// running shows nothing; blank when the person has no register entry. The entry is the one covering the month,
    /// else the latest begun by the month's end — not a later one, so a gap month between an ended contract and the
    /// next one shows the ended contract's day.</summary>
    private DateTime? EmploymentEndValue(MatchPerson? register)
    {
        if (register is null)
        {
            return null;
        }

        var last = new DateOnly(_view!.Period.Year, _view.Period.Month, 1).AddMonths(1).AddDays(-1);
        var entry = register.ActiveEntry ?? register.Entries.Where(e => e.StartDate is null || e.StartDate <= last).OrderByDescending(e => e.StartDate ?? DateOnly.MinValue).FirstOrDefault();
        if (entry is not { EndDate: { } end })
        {
            return null;
        }

        return entry.StateId == TorStates.Ended || end <= last ? end.ToDateTime(TimeOnly.MinValue) : null;
    }

    /// <summary>The sum in euros of the person's income the month does not declare, or none: a row in another currency
    /// or without an amount read is listed in the tooltip, never added (decision 87), nor a copy of another source's row
    /// (decision 86); a shared row nobody was named for is added, its amount was read.</summary>
    private static decimal? UndeclaredSum(ReviewPerson person) => person.UndeclaredSum; // one rule for the grid and the report

    /// <summary>The problems' codes, then why income goes undeclared, so the search finds both.</summary>
    private static string ProblemCodes(ReviewPerson person) =>
        string.Join(", ", person.Problems.Select(x => x.Code).Concat(person.Undeclared.SelectMany(u => u.Codes.Count > 0 ? u.Codes : [u.Reason])).Distinct());

    /// <summary>The register entry standing for the person (the one "Kande olek" shows) when it is paused (kande olek 3):
    /// "Peatamise algus" shows since when, the tooltip why and until when, as the register gives them.</summary>
    private static TorEntry? PausedEntry(MatchPerson? register) => register?.Entry is { StateId: TorStates.Paused } entry ? entry : null;

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

    private readonly (string? StorePath, string? Company, int? Year, int? Month) _remembered; // as the settings had them at the start
    private bool _oneOff; // the open store is the command line's one-off: its place, company and month are not remembered

    /// <summary>Whether the store shown is a one-off of the command line, which the settings do not remember.</summary>
    internal bool StoreIsOneOff => _oneOff;

    private void SaveSettings()
    {
        try
        {
            if (_oneOff)
            {
                // the window's own preferences are saved as ever; the store, the company and the month stay as remembered
                _settings.SavedWith(_remembered.StorePath, _remembered.Company, _remembered.Year, _remembered.Month).Save(_settingsPath);
                return;
            }

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
