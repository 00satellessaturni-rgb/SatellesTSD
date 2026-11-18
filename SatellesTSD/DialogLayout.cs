namespace SatellesTSD;

/// <summary>A sizable dialog's size, place, maximized state and named splitters as it was last closed (decision 210).</summary>
public sealed class DialogPlace
{
    /// <summary>The place; null = centred on the main window (no screen showed it, as the tests open their dialogs).</summary>
    public int? X { get; set; }

    public int? Y { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public bool Maximized { get; set; }

    /// <summary>Each named splitter's distance, by the SplitContainer's Name.</summary>
    public Dictionary<string, int>? Splitters { get; set; }
}

/// <summary>The sizable dialogs keep their size, place and splitters between sessions, as the main window does (decision
/// 105, extended by decision 210): applied when the dialog is made, before a test seam takes it, the splitters once it is
/// laid out; taken back into the settings when it closes, which the main window writes out when it closes.</summary>
internal static class DialogLayout
{
    /// <summary>What each panel of a kept splitter keeps at least, in pixels.</summary>
    private const int LeastPanel = 60;

    private static readonly Size DefaultLeast = new(480, 300);

    public static void Keep(Form dialog, AppSettings settings, string key)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(settings);
        var all = settings.Dialogs ??= new Dictionary<string, DialogPlace>(StringComparer.Ordinal);
        all.TryGetValue(key, out var saved);
        var least = dialog.MinimumSize.IsEmpty ? DefaultLeast : dialog.MinimumSize;
        if (saved is not null && WindowLayout.Fit(saved.X, saved.Y, saved.Width, saved.Height, Screens(), WindowLayout.InvisibleFrame, least) is { } fit)
        {
            dialog.Size = fit.Size; // centred on the main window when no place is taken
            if (fit.Location is { } at)
            {
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = at;
            }

            if (saved.Maximized && dialog.MaximizeBox)
            {
                dialog.WindowState = FormWindowState.Maximized;
            }
        }

        dialog.Load += (_, _) =>
        {
            if (saved?.Splitters is not { } splitters)
            {
                return;
            }

            foreach (var split in Splits(dialog))
            {
                var length = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
                if (splitters.TryGetValue(split.Name, out var distance) && length > 2 * LeastPanel + split.SplitterWidth)
                {
                    split.SplitterDistance = WindowLayout.Distance(distance, split.SplitterDistance, length, split.SplitterWidth, LeastPanel);
                }
            }
        };
        dialog.FormClosed += (_, _) => all[key] = Taken(dialog, saved, Screens(), WindowLayout.InvisibleFrame, least);
    }

    /// <summary>What a closing dialog leaves: its normal bounds; its place only where a screen shows its title bar (an
    /// off-screen place opens centred next time); when minimized, the state and splitters it had before.</summary>
    internal static DialogPlace Taken(Form dialog, DialogPlace? before, IReadOnlyList<Rectangle> screens, int frame, Size least)
    {
        var bounds = dialog.WindowState == FormWindowState.Normal ? dialog.Bounds : dialog.RestoreBounds;
        var shown = WindowLayout.Fit(bounds.X, bounds.Y, bounds.Width, bounds.Height, screens, frame, least)?.Location is not null;
        var minimized = dialog.WindowState == FormWindowState.Minimized;
        return new DialogPlace
        {
            X = shown ? bounds.X : null,
            Y = shown ? bounds.Y : null,
            Width = bounds.Width,
            Height = bounds.Height,
            Maximized = minimized ? before?.Maximized ?? false : dialog.WindowState == FormWindowState.Maximized,
            Splitters = minimized ? before?.Splitters : Splits(dialog).ToDictionary(s => s.Name, s => s.SplitterDistance, StringComparer.Ordinal),
        };
    }

    /// <summary>A dialog's named splitter, for the tests.</summary>
    internal static SplitContainer Split(Control dialog, string name) => Splits(dialog).Single(s => s.Name == name);

    private static List<Rectangle> Screens() => Screen.AllScreens.Select(s => s.WorkingArea).ToList();

    private static IEnumerable<SplitContainer> Splits(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(c => (c is SplitContainer { Name.Length: > 0 } s ? [s] : Enumerable.Empty<SplitContainer>()).Concat(Splits(c)));
}
