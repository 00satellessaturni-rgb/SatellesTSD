using SatellesTSD.Infrastructure.Updates;

namespace SatellesTSD;

/// <summary>The update window: what will happen, then the copy, the checks, the swap and the start of the new version,
/// with the answer awaited — and, when the new version stays silent, the choice to wait more or to put the previous
/// one back. Returns OK when the new version runs and this window should close its program.</summary>
public sealed class UpdateDialog : Form
{
    private enum Stage
    {
        Offer,
        Working,
        Waiting,
        Done,
    }

    private readonly string _exePath;
    private readonly string _folder;
    private readonly ReleaseManifest _release;
    private readonly Version _current;
    private readonly bool _fromStart;
    private readonly Action _beforeStart;
    private readonly Label _description = new() { Dock = DockStyle.Top, AutoSize = false, Height = 150, Padding = new Padding(10, 10, 10, 0) };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoSize = false, Padding = new Padding(10, 6, 10, 6) };
    private readonly Button _install = new() { AutoSize = true, MinimumSize = new Size(180, 0) };
    private readonly Button _stop = new() { AutoSize = true, MinimumSize = new Size(110, 0) };
    private Stage _stage;
    private CancellationTokenSource? _cancel;
    private string? _previous;
    private Updater.StartedCopy? _copy;

    public UpdateDialog(string exePath, string folder, ReleaseManifest release, Version current, bool fromStart, Action beforeStart)
    {
        _exePath = exePath ?? throw new ArgumentNullException(nameof(exePath));
        _folder = folder ?? throw new ArgumentNullException(nameof(folder));
        _release = release ?? throw new ArgumentNullException(nameof(release));
        _current = current ?? throw new ArgumentNullException(nameof(current));
        _fromStart = fromStart;
        _beforeStart = beforeStart ?? throw new ArgumentNullException(nameof(beforeStart));
        Text = UiText.UpdateTitle;
        Size = new Size(620, 330);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        _description.Text = UiText.UpdateDescription(current.ToString(3), release.Version.ToString(3), release.Published, folder, Updater.OtherCopiesRunning(exePath));
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        buttons.Controls.AddRange([_stop, _install]);
        Controls.Add(_status);
        Controls.Add(_description);
        Controls.Add(buttons);
        _install.Click += async (_, _) => await OnInstallAsync();
        _stop.Click += async (_, _) => await OnStopAsync();
        SetStage(Stage.Offer);
    }

    /// <summary>True once the new version runs: the caller closes its window.</summary>
    public bool Started { get; private set; }

    /// <summary>True when the dialog ended with the new version still running though it never answered: closed while
    /// waiting with no previous version to go back to, or a new copy that could not be ended. The caller closes its
    /// window as after a hand-over — two programs on one store help nobody (decision 157).</summary>
    public bool LeftRunning { get; private set; }

    // the window tests drive the stage machine through these, without a release to install
    internal void SimulateWorking() => SetStage(Stage.Working);
    internal void SimulateHandOver() => HandOver();
    internal void SimulateNoAnswer(bool withPrevious)
    {
        _previous = withPrevious ? "previous" : null;
        _status.Text = _previous is null ? UiText.UpdateNoAnswerNoWayBack : UiText.UpdateNoAnswer;
        SetStage(Stage.Waiting);
    }

    /// <summary>What became of the silent new copy (answered, exited, its exit code), in place of the look at the
    /// process — for a test, which starts none.</summary>
    internal Func<(bool Ready, bool Exited, int Code)>? CopyStateProbe;

    internal void SimulateNotClosed()
    {
        LeftRunning = true;
        Failed(UiText.UpdateNotClosed);
    }

    internal Button StopButton => _stop;
    internal Button InstallButton => _install;
    internal string StatusText => _status.Text;

    private void SetStage(Stage stage)
    {
        _stage = stage;
        switch (stage)
        {
            case Stage.Offer:
                _install.Text = UiText.UpdateNow;
                _install.Enabled = true;
                _stop.Text = _fromStart ? UiText.Later : UiText.Cancel;
                _stop.Enabled = true;
                AcceptButton = _stop; // Enter at the wrong moment never starts an update
                CancelButton = _stop;
                break;
            case Stage.Working:
                _install.Enabled = false;
                _stop.Text = UiText.Cancel;
                _stop.Enabled = false;
                AcceptButton = null;
                CancelButton = null;
                break;
            case Stage.Waiting:
                _install.Text = UiText.WaitMore;
                _install.Enabled = true;
                _stop.Text = _previous is null ? UiText.Close : UiText.PutBack;
                _stop.Enabled = true;
                AcceptButton = _install;
                CancelButton = null;
                break;
            case Stage.Done:
                _install.Enabled = false;
                _stop.Text = UiText.Close;
                _stop.Enabled = true;
                AcceptButton = _stop;
                CancelButton = _stop;
                break;
        }
    }

    private async Task OnInstallAsync()
    {
        switch (_stage)
        {
            case Stage.Offer:
                await InstallAsync();
                break;
            case Stage.Waiting:
                await AwaitAnswerAsync();
                break;
        }
    }

    private async Task OnStopAsync()
    {
        switch (_stage)
        {
            case Stage.Offer or Stage.Done:
                DialogResult = DialogResult.Cancel;
                Close();
                break;
            case Stage.Working when _cancel is not null:
                _status.Text = UiText.UpdateCancelled;
                _cancel.Cancel();
                break;
            case Stage.Waiting:
                if (_previous is null)
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                }
                else
                {
                    await GoBackAsync();
                }

                break;
        }
    }

    private async Task InstallAsync()
    {
        SetStage(Stage.Working);
        try
        {
            // the offer may have changed since the window opened: withdrawn, or overtaken
            _status.Text = UiText.UpdateChecking;
            var fresh = await Updater.CheckAsync(_folder, _current);
            if (fresh.State != UpdateState.Available)
            {
                Failed(fresh.State == UpdateState.UpToDate ? UiText.UpdateNoLongerOffered : $"{UiText.UpdatesFailed} {fresh.Detail}");
                return;
            }

            if (!string.Equals(fresh.Release!.Sha256, _release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                Failed($"{UiText.UpdateNoLongerOffered} ({fresh.Release.Version.ToString(3)} {UiText.UpdateAvailableTitle})");
                return;
            }

            // another window may have installed a newer release since: never put an older one over it
            var onDisk = Updater.DiskVersion(_exePath);
            if (onDisk is not null && onDisk > _release.Version)
            {
                Failed(UiText.UpdateNewerInstalled);
                return;
            }

            if (onDisk != _release.Version)
            {
                using var held = Updater.TakeInstallLock(_exePath) ?? throw new UpdateException(UpdateException.AnotherWindowUpdating, "another window of the program is updating it.");
                _cancel = new CancellationTokenSource();
                var token = _cancel.Token;
                var kb = (_release.Size + 1023) / 1024;
                var shown = new Progress<long>(copied => { if (!token.IsCancellationRequested) { _status.Text = $"{UiText.UpdateDownloading} {copied / 1024:N0} / {kb:N0} KB"; } });
                _status.Text = UiText.UpdateDownloading;
                _stop.Enabled = true;
                var downloaded = await Task.Run(() => Updater.Download(_folder, _release, _exePath, shown, token));
                _stop.Enabled = false; // from here on it is quicker to finish than to stop
                if (token.IsCancellationRequested)
                {
                    TryDelete(downloaded);
                    throw new OperationCanceledException(token);
                }

                _status.Text = UiText.UpdateUnpacking;
                var unpacked = await Task.Run(() =>
                {
                    var folder = Updater.Unpack(downloaded, _exePath);
                    Updater.VerifyProgram(folder, _release);
                    TryDelete(downloaded);
                    return folder;
                });
                _status.Text = UiText.UpdateInstalling;
                _previous = await Task.Run(() => Updater.Swap(_exePath, unpacked));
            }

            // else another window has installed it already: only the restart is left
            _status.Text = UiText.UpdateStarting;
            _beforeStart();
            _copy = await Task.Run(() => Updater.Start(_exePath, StartArguments()));
            await AwaitAnswerAsync();
        }
        catch (OperationCanceledException)
        {
            Failed(UiText.UpdateCancelled);
        }
        catch (UpdateException e) when (_previous is null)
        {
            Failed($"{Words(e)}\n{e.Message}");
        }
        catch (Exception e) when (e is UpdateException or IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _copy?.Terminate();
            if (_previous is null)
            {
                Failed($"{UiText.UpdateFailed}\n{e.Message}"); // before the swap: nothing was changed
            }
            else
            {
                PutBack($"{UiText.UpdateFailed}\n{e.Message}"); // past the swap: the new version could not be started — the previous one goes back
            }
        }
    }

    /// <summary>The store the new version is started on: the command line's one-off while it is the store in hand; none
    /// once a store was chosen in the window, which the settings then name (decision 167). The window sets it.</summary>
    internal string? StoreToHandOn;

    /// <summary>The store to open and the settings file this copy was started with, handed on when they were named.</summary>
    internal string[] StartArguments()
    {
        var arguments = new List<string>();
        if (StoreToHandOn is { } store)
        {
            arguments.AddRange(["--store", store]);
        }

        if (Program.SettingsPathOverride is { } settings)
        {
            arguments.AddRange(["--settings", settings]);
        }

        return [.. arguments];
    }

    /// <summary>Waits for the new version's signal: running — this window's work is done; ended — the previous
    /// version goes back; silent — the user decides.</summary>
    private async Task AwaitAnswerAsync()
    {
        SetStage(Stage.Working);
        _status.Text = UiText.UpdateStarting;
        var copy = _copy!;
        var result = await Task.Run(() => Updater.WaitFor(copy, Updater.StartTimeoutMs));
        switch (result.Outcome)
        {
            case Updater.StartOutcome.Ready:
                HandOver();
                break;
            case Updater.StartOutcome.Exited:
                PutBack($"{UiText.UpdateNewExited} (kood {result.ExitCode}).");
                break;
            default:
                _status.Text = _previous is null ? UiText.UpdateNoAnswerNoWayBack : UiText.UpdateNoAnswer;
                SetStage(Stage.Waiting);
                break;
        }
    }

    /// <summary>The user gives up on a new version that does not answer: it is ended and the previous one goes back —
    /// unless it has answered after all in the meantime.</summary>
    private async Task GoBackAsync()
    {
        var copy = _copy!;
        if (copy.IsReady)
        {
            HandOver();
            return;
        }

        SetStage(Stage.Working);
        _status.Text = UiText.UpdateClosingNew;
        try
        {
            var ended = await Task.Run(copy.Terminate);
            if (!ended)
            {
                LeftRunning = true; // the new copy runs on: this program makes way for it
                Failed(UiText.UpdateNotClosed);
                return;
            }

            PutBack(UiText.UpdateNewExited + ".");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Failed($"{UiText.UpdateFailed}\n{e.Message}");
        }
    }

    private void HandOver()
    {
        Started = true;
        DisposeCopy();
        SetStage(Stage.Done); // the closing guard lets a finished dialog go
        DialogResult = DialogResult.OK;
        Close();
    }

    private void PutBack(string reason)
    {
        DisposeCopy();
        if (_previous is null)
        {
            Failed($"{reason}\n{UiText.UpdateTellOwner}");
            return;
        }

        try
        {
            Updater.Revert(_exePath, _previous, _release);
            _previous = null; // put back once; a second failure has nothing more to restore
            Failed($"{reason}\n{UiText.UpdateKept} {_current.ToString(3)}.\n{UiText.UpdateStoreNote}");
        }
        catch (UpdateException e)
        {
            Failed($"{reason}\n{UiText.UpdateRevertFailed}\n{e.Message}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Failed($"{reason}\n{UiText.UpdateRevertFailed}\n{e.Message}");
        }
    }

    private void Failed(string reason)
    {
        _status.Text = reason;
        SetStage(Stage.Done);
    }

    private void DisposeCopy()
    {
        _copy?.Dispose();
        _copy = null;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // not while something is being copied, swapped or started, nor — with a previous version to go back to —
        // before the user has chosen what to do about a new version that does not answer
        if (_stage == Stage.Working || (_stage == Stage.Waiting && _previous is not null))
        {
            e.Cancel = true;
            return;
        }

        if (_stage == Stage.Waiting)
        {
            // closed on a silent new version with nothing to go back to. Nothing watched it meanwhile, so it is looked
            // at now: answered after all — a hand-over; gone — said, and the old window keeps the store; still there —
            // it stays running, as the text said, and the old window makes way (decision 157)
            var (ready, exited, code) = CopyStateProbe?.Invoke() ?? (_copy?.IsReady == true, _copy is { IsReady: false, Child.HasExited: true }, _copy is { Child.HasExited: true } gone ? gone.Child.ExitCode : 0);
            if (ready)
            {
                Started = true;
            }
            else if (exited)
            {
                e.Cancel = true;
                PutBack($"{UiText.UpdateNewExited} (kood {code}).");
                return;
            }
            else
            {
                LeftRunning = true;
            }
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cancel?.Dispose();
            DisposeCopy();
        }

        base.Dispose(disposing);
    }

    private static string Words(UpdateException e) => e.Code switch
    {
        UpdateException.ReadFailed => UiText.UpdateReadFailed,
        UpdateException.Incomplete => UiText.UpdateIncomplete,
        UpdateException.TooLarge or UpdateException.HashMismatch => UiText.UpdateDamaged,
        UpdateException.NotTheProgram => UiText.UpdateNotProgram,
        UpdateException.VersionMismatch => UiText.UpdateVersionMismatch,
        UpdateException.SchemasMissing => UiText.UpdateSchemasMissing,
        UpdateException.NoWriteAccess => UiText.UpdateNoWrite,
        UpdateException.Busy => UiText.UpdateBusy,
        UpdateException.AnotherWindowUpdating => UiText.UpdateOtherWindowUpdating,
        UpdateException.SwapFailed => UiText.UpdateSwapFailed,
        UpdateException.RevertFailed => UiText.UpdateRevertFailed,
        UpdateException.BadZip => UiText.UpdateBadZip,
        _ => UiText.UpdateFailed,
    };

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (IOException)
        {
            // Updater.CleanUp removes a left-over download at the next start
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
