using System.Globalization;
using SatellesTSD.Core.Declare;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Review;
using SatellesTSD.Core.Store;

namespace SatellesTSD;

/// <summary>The declaration tab: the period's standing lines with their taxes and their status, the files sent, and
/// the buttons that move a period along — build, export, upload, feedback, confirm, reopen — each raised to the window,
/// which owns the store.</summary>
public sealed class DeclarationPanel : UserControl
{
    private readonly FlowLayoutPanel _buttons = new() { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Padding = new Padding(3) };
    private readonly Button _build = Button(UiText.BuildLines);
    private readonly Button _export = Button(UiText.Export);
    private readonly Button _withdraw = Button(UiText.Withdraw);
    private readonly Button _uploaded = Button(UiText.MarkUploaded);
    private readonly Button _feedback = Button(UiText.ReadFeedback);
    private readonly Button _confirmed = Button(UiText.MarkConfirmed);
    private readonly Button _reopen = Button(UiText.Reopen);
    private readonly Button _accept = Button(UiText.ResolveAccepted);
    private readonly Button _reject = Button(UiText.ResolveRejected);
    private readonly DataGridView _lines = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        ReadOnly = true,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
    };
    private readonly ListView _submissions = new() { Dock = DockStyle.Bottom, Height = 110, View = View.Details, FullRowSelect = true, MultiSelect = false };
    private bool _filling;

    public event Action? BuildRequested;

    public event Action? ExportRequested;

    public event Action<Submission>? WithdrawRequested;

    public event Action? UploadedRequested;

    public event Action<Submission>? FeedbackRequested;

    public event Action? ConfirmedRequested;

    public event Action? ReopenRequested;

    public event Action<StoredPayment, bool>? ResolveRequested;

    public DeclarationPanel()
    {
        _buttons.Controls.AddRange([_build, _export, _withdraw, _uploaded, _feedback, _confirmed, _reopen, _accept, _reject]);
        foreach (var (name, header, right) in new[]
        {
            ("key", UiText.Key, false), ("action", UiText.Action, false), ("status", UiText.Status, false), ("person", UiText.Person, false), ("type", UiText.Type, false),
            ("date", UiText.Date, false), ("net", UiText.Net, true), ("gross", UiText.Gross, true), ("sm", UiText.SocialTax, true), ("smplus", UiText.MinimumIncrease, true),
            ("kp", UiText.FundedPension, true), ("tk", UiText.UnemploymentEmployee, true), ("ttk", UiText.UnemploymentEmployer, true), ("tm", UiText.IncomeTax, true),
            ("mvt", UiText.ExemptionUsed, true), ("source", UiText.SourceColumn, false), ("feedback", UiText.Feedback, false), ("version", UiText.Version, true),
        })
        {
            _lines.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = { Alignment = right ? DataGridViewContentAlignment.MiddleRight : DataGridViewContentAlignment.MiddleLeft },
            });
        }

        _submissions.Columns.Add(UiText.File, 300);
        _submissions.Columns.Add(UiText.Time, 120);
        _submissions.Columns.Add(UiText.UniqueId, 220);
        _submissions.Columns.Add(UiText.Result, 100);
        _submissions.Columns.Add(UiText.Feedback, 200);
        Controls.Add(_lines);
        Controls.Add(_submissions);
        Controls.Add(_buttons);

        _build.Click += (_, _) => BuildRequested?.Invoke();
        _export.Click += (_, _) => ExportRequested?.Invoke();
        _withdraw.Click += (_, _) => { if (SelectedSubmission is { } s) { WithdrawRequested?.Invoke(s); } };
        _uploaded.Click += (_, _) => UploadedRequested?.Invoke();
        _feedback.Click += (_, _) => { if (SelectedSubmission is { } s) { FeedbackRequested?.Invoke(s); } };
        _confirmed.Click += (_, _) => ConfirmedRequested?.Invoke();
        _reopen.Click += (_, _) => ReopenRequested?.Invoke();
        _accept.Click += (_, _) => { if (SelectedLine is { } l) { ResolveRequested?.Invoke(l.Payment, true); } };
        _reject.Click += (_, _) => { if (SelectedLine is { } l) { ResolveRequested?.Invoke(l.Payment, false); } };
        _lines.SelectionChanged += (_, _) => { if (!_filling) { EnableLineButtons(); } };
        _submissions.SelectedIndexChanged += (_, _) => { if (!_filling) { EnableSubmissionButtons(); } };
        _lines.DataError += (_, e) => { e.ThrowException = false; };
        Present(null, [], []);
    }

    public DeclaredLine? SelectedLine => _lines.CurrentRow?.Tag as DeclaredLine;

    public Submission? SelectedSubmission => _submissions.SelectedItems.Count > 0 ? _submissions.SelectedItems[0].Tag as Submission : null;

    private ReviewView? _view;
    private IReadOnlyList<Submission> _submissionList = [];

    public void Present(ReviewView? view, IReadOnlyList<DeclaredLine> lines, IReadOnlyList<Submission> submissions)
    {
        _filling = true;
        _view = view;
        _submissionList = submissions;
        var keepKey = SelectedLine?.Payment.LineNumber;
        _lines.Rows.Clear();
        foreach (var line in lines)
        {
            var p = line.Payment;
            var r = p.Result;
            var index = _lines.Rows.Add(
                p.LineNumber,
                UiText.ActionText(p.Action),
                UiText.PaymentStatusText(p.Status),
                line.Person is { } person ? $"{person.LastName} {person.FirstName}" : "",
                p.Line.TypeCode ?? "",
                UiText.Day(p.Line.DocumentDate),
                p.NetPaid is { } net ? UiText.Money(net) : "",
                r is null ? "" : UiText.Money(r.Gross),
                r is null ? "" : UiText.Money(r.SocialTax),
                r is null || r.SocialTaxMinimumIncrease == 0 ? "" : UiText.Money(r.SocialTaxMinimumIncrease),
                r is null ? "" : UiText.Money(r.FundedPension),
                r is null ? "" : UiText.Money(r.UnemploymentEmployee),
                r is null ? "" : UiText.Money(r.UnemploymentEmployer),
                r is null ? "" : UiText.Money(r.IncomeTax),
                r is null || r.ExemptionUsed == 0 ? "" : UiText.Money(r.ExemptionUsed),
                p.SourceRef == PeriodDeclarer.MinimumSource ? UiText.Minimum : p.SourceRef == ReviewWorkspace.ByHandSource ? UiText.ByHand : p.SourceRef ?? "",
                line.LastFeedback is { } f ? $"{f.Severity}: {string.Join(", ", f.Messages.Select(m => m.Code))}" : "",
                line.Versions.ToString(CultureInfo.InvariantCulture));
            var row = _lines.Rows[index];
            row.Tag = line;
            row.DefaultCellStyle.BackColor = p.Status switch
            {
                PaymentStatus.Draft => Color.White,
                PaymentStatus.Sent => Color.AliceBlue,
                PaymentStatus.Accepted => Color.Honeydew,
                PaymentStatus.NeedsChange => Color.MistyRose,
                PaymentStatus.Review => Color.LightYellow,
                _ => Color.Gainsboro,
            };
            if (p.Status is PaymentStatus.Rejected or PaymentStatus.Cancelled)
            {
                row.DefaultCellStyle.ForeColor = Color.Gray;
            }

            if (keepKey is not null && p.LineNumber == keepKey)
            {
                _lines.CurrentCell = row.Cells[0];
            }
        }

        _submissions.Items.Clear();
        foreach (var submission in submissions)
        {
            var item = new ListViewItem([submission.FileName, submission.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture), submission.UniqueId,
                submission.Result?.ToString() ?? "", submission.FeedbackSource ?? ""]) { Tag = submission };
            _submissions.Items.Add(item);
        }

        if (_submissions.Items.Count > 0)
        {
            _submissions.Items[^1].Selected = true;
        }

        _filling = false;
        EnableButtons(lines);
    }

    private void EnableButtons(IReadOnlyList<DeclaredLine> lines)
    {
        var state = _view?.Period.State;
        var draft = state == PeriodState.Draft;
        _build.Enabled = draft;
        _export.Enabled = draft && lines.Any(l => l.Payment.Status == PaymentStatus.Draft);
        _uploaded.Enabled = state == PeriodState.Exported;
        _confirmed.Enabled = state is PeriodState.Uploaded or PeriodState.Exported;
        _reopen.Enabled = state is not null && !draft;
        EnableSubmissionButtons();
        EnableLineButtons();
    }

    private void EnableSubmissionButtons()
    {
        var submission = SelectedSubmission;
        // only the latest file, nothing come of it, the period still Exported — as PeriodSubmitter.Withdraw allows
        _withdraw.Enabled = submission is { Result: null } && _view?.Period.State == PeriodState.Exported && _submissionList.Count > 0
            && _submissionList.OrderBy(s => s.CreatedAt).Last().Id == submission.Id;
        _feedback.Enabled = submission is not null;
    }

    private void EnableLineButtons()
    {
        var pending = SelectedLine?.Payment.Pending == true;
        _accept.Enabled = pending;
        _reject.Enabled = pending;
    }

    private static Button Button(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3) };
}
