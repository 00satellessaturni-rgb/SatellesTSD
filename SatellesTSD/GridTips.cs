namespace SatellesTSD;

/// <summary>A long tooltip over one column of a grid. A cell's own ToolTipText is cut to 256 characters by the grid, too
/// short for a report's messages in full and the server's taxes; a ToolTip of its own shows the whole text, and stays up
/// long enough to be read.</summary>
internal sealed class GridTips : IDisposable
{
    private readonly ToolTip _tip = new() { AutoPopDelay = 30000, InitialDelay = 400, ReshowDelay = 100 };
    private readonly Dictionary<DataGridViewRow, string> _texts = [];
    private readonly DataGridView _grid;
    private readonly string _column;

    public GridTips(DataGridView grid, string column)
    {
        _grid = grid;
        _column = column;
        grid.CellMouseEnter += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && _grid.Columns[e.ColumnIndex].Name == _column && TextOf(_grid.Rows[e.RowIndex]) is { } text)
            {
                var cell = _grid.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, false);
                _tip.Show(text, _grid, cell.Left, cell.Bottom);
            }
            else
            {
                _tip.Hide(_grid); // a cell with nothing to tell never keeps another row's text up
            }
        };
        grid.CellMouseLeave += (_, _) => _tip.Hide(_grid);
    }

    /// <summary>The text shown over the row's cell of the column, or none.</summary>
    public void Set(DataGridViewRow row, string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            _texts.Remove(row);
        }
        else
        {
            _texts[row] = text;
        }
    }

    /// <summary>Forgets every row's text (the rows are made afresh), and takes down a tip still showing: a refill under a
    /// still mouse raises no leave.</summary>
    public void Clear()
    {
        _tip.Hide(_grid);
        _texts.Clear();
    }

    public string? TextOf(DataGridViewRow row) => _texts.GetValueOrDefault(row);

    public void Dispose() => _tip.Dispose();
}
