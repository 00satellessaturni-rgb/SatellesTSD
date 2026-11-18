using SatellesTSD.Core.Matching;
using SatellesTSD.Infrastructure;

namespace SatellesTSD;

/// <summary>The words and phrases that find each category (decision 202): listed by filter set and category, saved to a
/// file, read back from one, and the built-in ones put back. The categories themselves stay the built-in ones; a month's
/// search keeps the phrases it ran with, so new ones find rows at the month's next "Otsi maksed".</summary>
public sealed class PhrasesDialog : Form
{
    private readonly AppServices _services;
    private readonly Action<string> _log;
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false };
    private readonly Label _standing = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
    private readonly Button _import = new() { Text = UiText.PhrasesImport, AutoSize = true };
    private readonly Button _save = new() { Text = UiText.PhrasesSave, AutoSize = true };
    private readonly Button _reset = new() { Text = UiText.PhrasesReset, AutoSize = true };

    public PhrasesDialog(AppServices services, Action<string> log)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        Text = UiText.PhrasesTitle;
        Size = new Size(900, 480);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false; // a modal dialog with no taskbar button, minimized, would strand itself
        ShowInTaskbar = false;
        _list.Columns.Add(UiText.PhrasesSet, 110);
        _list.Columns.Add(UiText.PhrasesCategory, 130);
        _list.Columns.Add(UiText.PhrasesInclude, 420);
        _list.Columns.Add(UiText.PhrasesExclude, 200);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var close = new Button { Text = UiText.Ok, DialogResult = DialogResult.OK, AutoSize = true };
        buttons.Controls.AddRange([close, _reset, _save, _import]);
        Controls.Add(_list);
        Controls.Add(_standing);
        Controls.Add(buttons);
        AcceptButton = close;
        _import.Click += (_, _) => Import();
        _save.Click += (_, _) => Save();
        _reset.Click += (_, _) => Reset();
        Fill();
    }

    internal ListView List => _list;
    internal Button ImportButton => _import;
    internal Button SaveButton => _save;
    internal Button ResetButton => _reset;
    internal string StandingText => _standing.Text;

    /// <summary>The file to write the phrases to, given the name offered; null = cancelled. A save dialog by default.</summary>
    internal Func<string, string?>? ChoosingFile;

    /// <summary>The file to read phrases from; null = cancelled. An open dialog by default.</summary>
    internal Func<string?>? OpeningFile;

    /// <summary>The question asked before the built-in phrases are put back (the text, the title): true = go on.</summary>
    internal Func<string, string, bool>? Confirming;

    /// <summary>A refusal shown. A message box by default.</summary>
    internal Action<string>? Warning;

    private void Fill()
    {
        _list.Items.Clear();
        var catalog = _services.Pipeline.MatchCatalog();
        foreach (var (setName, cells) in catalog.FilterSets.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            // in the catalog's own order of the categories, then any cell of a name the order does not hold
            var ordered = catalog.Categories.Select(c => cells.FirstOrDefault(k => string.Equals(k.Key, c.Name, StringComparison.OrdinalIgnoreCase)))
                .Where(k => k.Key is not null).ToList();
            foreach (var (categoryName, cell) in ordered)
            {
                var patterns = PatternList.Parse(cell);
                _list.Items.Add(new ListViewItem([setName, categoryName, string.Join(", ", patterns.Include), string.Join(", ", patterns.Exclude)]) { Tag = cell });
            }
        }

        var own = _services.Pipeline.OwnPhrases;
        _standing.Text = own ? UiText.PhrasesOwn : UiText.PhrasesBuiltIn;
        _reset.Enabled = own;
    }

    private void Save()
    {
        var path = ChoosingFile is { } choose ? choose("kategooriad.json") : AskSavePath();
        if (path is null)
        {
            return;
        }

        try
        {
            File.WriteAllText(path, _services.Pipeline.ExportPhrases(), new System.Text.UTF8Encoding(false));
            _log(UiText.PhrasesSaved(path));
        }
        catch (Exception e)
        {
            Tell(e.Message);
        }
    }

    private string? AskSavePath()
    {
        using var dialog = new SaveFileDialog { Filter = UiText.PhrasesFilter, FileName = "kategooriad.json", DefaultExt = "json" };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    private void Import()
    {
        var path = OpeningFile is { } open ? open() : AskOpenPath();
        if (path is null)
        {
            return;
        }

        try
        {
            var changed = _services.Pipeline.ImportPhrases(File.ReadAllText(path));
            _log(UiText.PhrasesImported(changed));
        }
        catch (Exception e)
        {
            Tell(UiText.Worded(e) ?? e.Message);
        }

        Fill();
    }

    private string? AskOpenPath()
    {
        using var dialog = new OpenFileDialog { Filter = UiText.PhrasesFilter, CheckFileExists = true };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    private void Reset()
    {
        var ask = Confirming ?? ((text, title) => MessageBox.Show(this, text, title, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK);
        if (!ask(UiText.PhrasesResetConfirm, UiText.PhrasesReset))
        {
            return;
        }

        try
        {
            _services.Pipeline.ResetPhrases();
            _log(UiText.PhrasesResetDone);
        }
        catch (Exception e)
        {
            Tell(e.Message);
        }

        Fill();
    }

    private void Tell(string message)
    {
        if (Warning is { } take)
        {
            take(message);
            return;
        }

        MessageBox.Show(this, message, UiText.Problem, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
