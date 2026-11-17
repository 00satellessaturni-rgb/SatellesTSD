using System.Globalization;
using SatellesTSD.Core.Report;
using SatellesTSD.Infrastructure;

namespace SatellesTSD;

/// <summary>The non-residents of every company of the store (decision 174): one row per person with the company, the
/// tick of the basic exemption's application, and the lines on which an exemption was used; a line of counts above;
/// the list saved as a workbook. It only reads: what is to be put right is put right on the card or by the Isik
/// menu's command, in the company and the month concerned.</summary>
public sealed class NonResidentsDialog : Form
{
    private readonly IReadOnlyList<NonResidentRow> _rows;
    private readonly Action<string> _log;
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

    private readonly Label _summary = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 8, 8, 6) };
    private readonly CheckBox _onlyExemption = new() { Text = UiText.NonResidentsOnlyExemption, AutoSize = true };
    private readonly Button _save = new() { Text = UiText.NonResidentsSave, AutoSize = true };
    private Font? _bold;

    public NonResidentsDialog(IReadOnlyList<NonResidentRow> rows, Action<string> log)
    {
        _rows = rows ?? throw new ArgumentNullException(nameof(rows));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        Text = UiText.NonResidentsTitle;
        Size = new Size(1100, 520);
        MinimumSize = new Size(640, 320);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        // the counts, the amount and the date are held as what they are, so a click on their header sorts them as numbers
        // and days, not as text
        for (var i = 0; i < NonResidentOverview.PersonHeaders.Count; i++)
        {
            var count = NonResidentOverview.CountColumns.Contains(i);
            var amount = i == NonResidentOverview.AmountColumn;
            var date = i == NonResidentOverview.DateColumn;
            var column = new DataGridViewTextBoxColumn
            {
                HeaderText = NonResidentOverview.PersonHeaders[i],
                SortMode = DataGridViewColumnSortMode.Automatic,
                ValueType = count ? typeof(int) : amount ? typeof(decimal) : date ? typeof(DateTime) : typeof(string),
                DefaultCellStyle = { Alignment = count || amount ? DataGridViewContentAlignment.MiddleRight : DataGridViewContentAlignment.MiddleLeft },
            };
            if (amount)
            {
                column.DefaultCellStyle.Format = "N2";
            }
            else if (date)
            {
                column.DefaultCellStyle.Format = "dd.MM.yyyy";
            }

            _grid.Columns.Add(column);
        }

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4) };
        var close = new Button { Text = UiText.Close, DialogResult = DialogResult.OK, AutoSize = true };
        buttons.Controls.AddRange([close, _save, _onlyExemption]);
        Controls.Add(_grid);
        Controls.Add(_summary);
        Controls.Add(buttons);
        AcceptButton = close;
        CancelButton = close;
        _grid.DataError += (_, e) => { e.ThrowException = false; };
        Disposed += (_, _) => _bold?.Dispose();
        _onlyExemption.CheckedChanged += (_, _) => Fill();
        _save.Click += (_, _) => Save();
        _save.Enabled = rows.Count > 0;
        _summary.Text = UiText.NonResidentsSummary(
            rows.Where(r => r.NonResidentNow).Select(r => r.Company.Id).Distinct().Count(), rows.Count(r => r.NonResidentNow),
            rows.Where(r => r.Applied).Select(r => r.Company.Id).Distinct().Count(), rows.Count(r => r.Applied),
            rows.Where(r => r.Used).Select(r => r.Company.Id).Distinct().Count(), rows.Count(r => r.Used));
        Fill();
    }

    // the window tests' seams
    internal DataGridView Grid => _grid;
    internal CheckBox OnlyExemptionBox => _onlyExemption;
    internal Button SaveButton => _save;
    internal string SummaryText => _summary.Text;

    /// <summary>Names the file to write in place of the save dialog (the name offered; null = cancelled).</summary>
    internal Func<string, string?>? ChoosingFile;

    /// <summary>Takes a failure of the save in place of the message box.</summary>
    internal Action<string>? Warning;

    /// <summary>The file last written.</summary>
    internal string? SavedPath { get; private set; }

    /// <summary>The rows shown: every non-resident, or only those with the exemption ticked or used on a line.</summary>
    private IEnumerable<NonResidentRow> Shown => _onlyExemption.Checked ? _rows.Where(r => r.Applied || r.Used) : _rows;

    private void Fill()
    {
        _grid.Rows.Clear();
        foreach (var row in Shown)
        {
            var index = _grid.Rows.Add(
                row.Company.RegistryCode, row.Company.Name, row.Person.LastName, row.Person.FirstName, NonResidentOverview.CodeOf(row.Person) ?? "",
                NonResidentOverview.ResidencyNow(row), row.Person.ResidenceCountry ?? "", NonResidentOverview.SourceText(row.Person.ResidencySource),
                row.Person.ResidencyCheckedOn is { } day ? day.ToDateTime(TimeOnly.MinValue) : null, NonResidentOverview.YesNo(row.Person.BasicExemptionApplied),
                row.ExemptionLines == 0 ? null : row.ExemptionLines, row.OnServer == 0 ? null : row.OnServer, row.Unsent == 0 ? null : row.Unsent,
                row.Used ? row.ExemptionUsed : null, string.Join(", ", row.Months));
            _grid.Rows[index].Tag = row;
            if (row.Applied || row.Used)
            {
                _grid.Rows[index].DefaultCellStyle.Font = _bold ??= new Font(_grid.Font, FontStyle.Bold); // the ones to look at
            }
        }

        // the filter changed under a sort: the rows are put in the header's order again, so the arrow tells the truth
        if (_grid.SortedColumn is { } sorted)
        {
            _grid.Sort(sorted, _grid.SortOrder == SortOrder.Descending ? System.ComponentModel.ListSortDirection.Descending : System.ComponentModel.ListSortDirection.Ascending);
        }
    }

    /// <summary>The list as it is shown (the filter applied) into a workbook where the accountant says.</summary>
    private void Save()
    {
        // the rows as they are on the screen: the filter applied, in the order the grid shows them
        var book = NonResidentOverview.ToBook(_grid.Rows.Cast<DataGridViewRow>().Select(r => r.Tag).OfType<NonResidentRow>().ToList(), DateTime.Now);
        string? path;
        if (ChoosingFile is { } choose)
        {
            path = choose(book.FileName);
        }
        else
        {
            using var dialog = new SaveFileDialog { Filter = UiText.XlsxFilter, FileName = book.FileName, OverwritePrompt = true, AddExtension = true, DefaultExt = "xlsx" };
            path = dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
        }

        if (path is null)
        {
            return;
        }

        var temporary = path + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, ReportWriter.ToBytes(book));
            File.Move(temporary, path, overwrite: true); // written beside its name and moved onto it whole
            SavedPath = path;
            _log($"{UiText.NonResidentsTitle}: {path}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException or System.Security.SecurityException)
        {
            if (Warning is { } take)
            {
                take(e.Message);
            }
            else
            {
                MessageBox.Show(this, e.Message, UiText.Problem, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // the half-written file could not be taken away: it harms nothing
            }
        }
    }
}
