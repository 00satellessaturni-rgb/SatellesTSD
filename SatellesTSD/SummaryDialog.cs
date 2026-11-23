using SatellesTSD.Core.Review;

namespace SatellesTSD;

/// <summary>The summary of a search or a register check: a header (whose, which month, when, what it covers, how many),
/// the kinds of problems with their counts by section, and under them the places the kind picked was found. A double-click
/// on a place that is a person's closes the summary and hands the place back, for the main window to go to. It only shows:
/// what is to be put right is put right in the main window.</summary>
public sealed class SummaryDialog : Form
{
    private readonly RunSummary _summary;
    private readonly Label _header = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 8, 8, 4) };
    private readonly Label _hint = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 0, 8, 6), Text = UiText.SummaryHint };
    private readonly SplitContainer _split = new() { Name = "summarySplit", Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };

    private readonly DataGridView _groups = Grid();
    private readonly DataGridView _places = Grid();

    /// <param name="kept">The summary was kept in the store; false when keeping it failed, which the header says.</param>
    public SummaryDialog(RunSummary summary, bool kept = true)
    {
        _summary = summary ?? throw new ArgumentNullException(nameof(summary));
        Text = UiText.SummaryTitle;
        Size = new Size(1000, 620);
        MinimumSize = new Size(560, 360);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false; // a modal dialog with no taskbar button, minimized, would strand itself
        ShowInTaskbar = false;

        _groups.Columns.Add(Column(UiText.SummarySectionColumn, DataGridViewAutoSizeColumnMode.AllCells));
        _groups.Columns.Add(Column(UiText.SummaryCodeColumn, DataGridViewAutoSizeColumnMode.AllCells));
        _groups.Columns.Add(Column(UiText.SummaryKindColumn, DataGridViewAutoSizeColumnMode.Fill));
        var count = Column(UiText.SummaryCountColumn, DataGridViewAutoSizeColumnMode.AllCells);
        count.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _groups.Columns.Add(count);

        _places.Columns.Add(Column(UiText.SummaryWhoColumn, DataGridViewAutoSizeColumnMode.AllCells));
        var date = Column(UiText.Date, DataGridViewAutoSizeColumnMode.AllCells);
        date.DefaultCellStyle.Format = "dd.MM.yyyy";
        _places.Columns.Add(date);
        var amount = Column(UiText.Amount, DataGridViewAutoSizeColumnMode.AllCells);
        amount.DefaultCellStyle.Format = "N2";
        amount.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _places.Columns.Add(amount);
        var detail = Column(UiText.SummaryDetailColumn, DataGridViewAutoSizeColumnMode.Fill);
        detail.DefaultCellStyle.WrapMode = DataGridViewTriState.True; // a long explanation wraps, never cut
        _places.Columns.Add(detail);
        _places.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;

        _split.Panel1.Controls.Add(_groups);
        _split.Panel2.Controls.Add(_places);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4) };
        var close = new Button { Text = UiText.Close, DialogResult = DialogResult.OK, AutoSize = true };
        buttons.Controls.Add(close);
        Controls.Add(_split);
        Controls.Add(_hint);
        Controls.Add(_header);
        Controls.Add(buttons);
        AcceptButton = close;
        CancelButton = close;

        _header.Text = kept ? UiText.SummaryHeader(summary) : UiText.SummaryHeader(summary) + Environment.NewLine + UiText.SummaryNotKept;
        _header.Font = new Font(Font, FontStyle.Bold);
        Disposed += (_, _) => _header.Font.Dispose();
        Resize += (_, _) => WrapHeader();
        WrapHeader();
        _hint.Visible = summary.Total > 0;
        foreach (var section in summary.Sections)
        {
            foreach (var group in section.Groups)
            {
                var index = _groups.Rows.Add(section.Title, group.Code, group.Title ?? "", group.Count);
                _groups.Rows[index].Tag = group;
            }
        }

        // the current row moved, by a click or the arrows: the grid's SelectionChanged comes before its CurrentRow is the new one
        _groups.CurrentCellChanged += (_, _) => FillPlaces();
        _places.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0)
            {
                Pick(e.RowIndex);
            }
        };
        _places.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && _places.CurrentRow is { } row)
            {
                e.Handled = true;
                Pick(row.Index);
            }
        };
        FillPlaces();
    }

    // the window tests' seams
    internal DataGridView Groups => _groups;
    internal DataGridView Places => _places;
    internal string HeaderText => _header.Text;
    internal RunSummary Summary => _summary;

    /// <summary>The place double-clicked: a person's (and their payment's), for the main window to go to; null when none was.</summary>
    public SummaryOccurrence? Picked { get; private set; }

    /// <summary>A place of the kind shown, taken as a double-click takes it: one with a person closes the summary and is handed back.</summary>
    internal void Pick(int row)
    {
        if (row < 0 || row >= _places.Rows.Count || _places.Rows[row].Tag is not SummaryOccurrence { PersonId: not null } place)
        {
            return;
        }

        Picked = place;
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>Picks a kind (a row of the upper list), as a click does.</summary>
    internal void SelectGroup(int row)
    {
        _groups.CurrentCell = _groups.Rows[row].Cells[0];
        FillPlaces();
    }

    private void FillPlaces()
    {
        _places.Rows.Clear();
        if (_groups.CurrentRow?.Tag is not SummaryGroup group)
        {
            return;
        }

        foreach (var place in group.Occurrences)
        {
            var index = _places.Rows.Add(place.Who, place.Date?.ToDateTime(TimeOnly.MinValue), place.Amount, place.Detail);
            var row = _places.Rows[index];
            row.Tag = place;
            row.Cells[3].ToolTipText = place.Detail; // the whole text on the pointer as well
        }
    }

    /// <summary>The header wraps at the dialog's width: a long company name or note is never cut.</summary>
    private void WrapHeader()
    {
        var width = Math.Max(200, ClientSize.Width - 24);
        _header.MaximumSize = new Size(width, 0);
        _hint.MaximumSize = new Size(width, 0);
    }

    private static DataGridView Grid() => new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        ReadOnly = true,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
    };

    private static DataGridViewTextBoxColumn Column(string header, DataGridViewAutoSizeColumnMode size) =>
        new() { HeaderText = header, AutoSizeMode = size, SortMode = DataGridViewColumnSortMode.NotSortable };
}
