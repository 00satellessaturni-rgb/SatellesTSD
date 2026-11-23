namespace SatellesTSD;

/// <summary>A question with its own answers: a text (several lines, a path among them) and two to four buttons in a row,
/// each a choice in plain words. Closed with X or Esc it is no choice at all (-1): the caller takes that as the way that
/// changes nothing.</summary>
public sealed class ChoiceDialog : Form
{
    public ChoiceDialog(string text, string title, IReadOnlyList<string> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        if (choices.Count is < 1 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(choices), "A choice dialog has one to four choices.");
        }

        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, Padding = new Padding(12) };
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        // the text wraps at a readable width and is never cut: a long path breaks onto the next line
        var label = new Label { Text = text, AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(3, 3, 3, 12) };
        table.Controls.Add(label, 0, 0);
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Dock = DockStyle.Fill };
        for (var i = 0; i < choices.Count; i++)
        {
            var index = i;
            var button = new Button { Text = choices[i], AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
            button.Click += (_, _) =>
            {
                Chosen = index;
                DialogResult = DialogResult.OK;
            };
            buttons.Controls.Add(button);
            Buttons.Add(button);
        }

        table.Controls.Add(buttons, 0, 1);
        Controls.Add(table);
        AcceptButton = Buttons[0]; // Enter takes the first choice, the one the question leads with
    }

    /// <summary>The choice's index, or -1 while none was made (closed with X or Esc).</summary>
    public int Chosen { get; private set; } = -1;

    internal List<Button> Buttons { get; } = [];

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Chosen = -1;
            DialogResult = DialogResult.Cancel;
            return true;
        }

        return base.ProcessDialogKey(keyData);
    }

    /// <summary>Asks and waits: the index of the choice made, -1 for none.</summary>
    public static int Ask(IWin32Window? owner, string text, string title, IReadOnlyList<string> choices)
    {
        using var dialog = new ChoiceDialog(text, title, choices);
        dialog.ShowDialog(owner);
        return dialog.Chosen;
    }
}
