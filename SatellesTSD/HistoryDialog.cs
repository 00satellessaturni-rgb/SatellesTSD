using System.Globalization;
using SatellesTSD.Core.Declare;
using SatellesTSD.Core.Report;
using SatellesTSD.Core.Store;

namespace SatellesTSD;

/// <summary>One key's every version, oldest first — the action, the status, when it was made, the file it went out
/// in, the figures and the feedback's last word — with the two resolve buttons working on the version picked here,
/// standing or hidden beneath a later draft (the lines grid shows the standing version alone). The window owns the
/// store: a resolve is raised to it, and the dialog reloads when told.</summary>
public sealed class HistoryDialog : Form
{
    private readonly AppServices _services;
    private readonly StoredPayment _line;
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        ReadOnly = true,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
    };

    private readonly Button _accept = new() { Text = UiText.ResolveAccepted, AutoSize = true, Enabled = false };
    private readonly Button _reject = new() { Text = UiText.ResolveRejected, AutoSize = true, Enabled = false };
    private bool _filling;

    /// <summary>The accountant's word on the picked version: on the server (true) or refused (false).</summary>
    public event Action<StoredPayment, bool>? ResolveRequested;

    /// <summary>The feedback cell's long tooltip: the version's latest report in full.</summary>
    internal GridTips FeedbackTips { get; }

    public HistoryDialog(AppServices services, DeclaredLine line)
    {
        FeedbackTips = new GridTips(_grid, "feedback");
        Disposed += (_, _) => FeedbackTips.Dispose();
        ArgumentNullException.ThrowIfNull(line);
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _line = line.Payment;
        Text = $"{UiText.HistoryTitle} {_line.LineNumber}";
        Size = new Size(960, 360);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        foreach (var (name, header, right) in new[]
        {
            ("version", UiText.Version, true), ("action", UiText.Action, false), ("status", UiText.Status, false), ("created", UiText.CreatedAt, false),
            ("file", UiText.File, false), ("net", UiText.Net, true), ("gross", UiText.Gross, true), ("feedback", UiText.Feedback, false),
        })
        {
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = { Alignment = right ? DataGridViewContentAlignment.MiddleRight : DataGridViewContentAlignment.MiddleLeft },
            });
        }

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var close = new Button { Text = UiText.Close, DialogResult = DialogResult.OK, AutoSize = true };
        buttons.Controls.AddRange([close, _reject, _accept]);
        Controls.Add(_grid);
        Controls.Add(buttons);
        AcceptButton = close;
        CancelButton = close;
        _grid.SelectionChanged += (_, _) => { if (!_filling) { EnableButtons(); } };
        _grid.DataError += (_, e) => { e.ThrowException = false; };
        _accept.Click += (_, _) => { if (Selected is { } v) { ResolveRequested?.Invoke(v, true); } };
        _reject.Click += (_, _) => { if (Selected is { } v) { ResolveRequested?.Invoke(v, false); } };
        Reload();
    }

    /// <summary>The version picked in the grid.</summary>
    public StoredPayment? Selected => _grid.CurrentRow?.Tag as StoredPayment;

    /// <summary>The versions as last loaded, oldest first.</summary>
    public IReadOnlyList<StoredPayment> Versions { get; private set; } = [];

    internal DataGridView Grid => _grid;
    internal Button AcceptVersionButton => _accept;
    internal Button RejectVersionButton => _reject;

    /// <summary>Reads the key's versions from the store again (after a resolve), keeping the picked version picked.</summary>
    public void Reload()
    {
        var keep = Selected?.Id;
        _filling = true;
        var files = _services.Store.Submissions(_line.PeriodId).ToDictionary(s => s.Id, s => s.FileName);
        Versions = _services.Store.History(_line.PeriodId, _line.Section, _line.LineNumber);
        _grid.Rows.Clear();
        FeedbackTips.Clear();
        foreach (var version in Versions)
        {
            var feedback = _services.Store.Feedback(version.Id).LastOrDefault();
            // a version refused over its own green, yellow or red report by a later version's black _5 (decision 93): the row says so (one refused by hand, without a report, is left as it is)
            var rejectedBy = version.Status == PaymentStatus.Rejected && feedback is not null && feedback.Severity != Core.Feedback.Severity.Black
                ? Versions.FirstOrDefault(v => v.Version > version.Version && _services.Store.Feedback(v.Id).Any(f => f.Severity == Core.Feedback.Severity.Black && f.Messages.Any(m => m.Code == Core.Feedback.FeedbackReport.RuleChangeOnUnknownKey)))
                : null;
            var index = _grid.Rows.Add(
                version.Version.ToString(CultureInfo.InvariantCulture),
                ReportText.ActionText(version.Action),
                ReportText.PaymentStatusText(version.Status),
                version.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture),
                version.SubmissionId is { } submission && files.TryGetValue(submission, out var file) ? file : "",
                version.NetPaid is { } net ? UiText.Money(net) : "",
                version.Result is { } r ? UiText.Money(r.Gross) : "",
                rejectedBy is { } by ? UiText.HistoryRejectedBy(by.Version) : feedback is { } f ? $"{f.Severity}: {string.Join(", ", f.Messages.Select(m => m.Code))}" : "");
            var row = _grid.Rows[index];
            row.Tag = version;
            var tip = feedback is null ? null : UiText.FeedbackDetail(feedback, version.Result);
            FeedbackTips.Set(row, rejectedBy is { } later ? $"{UiText.HistoryRejectedBy(later.Version)}{(tip is null ? "" : Environment.NewLine + tip)}" : tip);

            if (version.Status is PaymentStatus.Rejected or PaymentStatus.Cancelled)
            {
                row.DefaultCellStyle.ForeColor = Color.Gray;
            }

            if (version.Id == keep)
            {
                _grid.CurrentCell = row.Cells[0];
            }
        }

        if (keep is null && _grid.Rows.Count > 0)
        {
            // first opened: on the version awaiting a verdict when there is one, else the latest
            var pending = Versions.LastOrDefault(v => v.Pending);
            var index = pending is null ? _grid.Rows.Count - 1 : Versions.ToList().IndexOf(pending);
            _grid.CurrentCell = _grid.Rows[index].Cells[0];
        }

        _filling = false;
        EnableButtons();
    }

    private void EnableButtons()
    {
        var pending = Selected?.Pending == true;
        _accept.Enabled = pending;
        _reject.Enabled = pending;
    }
}
