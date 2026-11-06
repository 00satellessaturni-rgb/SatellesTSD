namespace SatellesTSD;

/// <summary>The window's size and place, and its two splitters, kept across sessions in the settings. A place no
/// screen shows any more (a monitor unplugged, a resolution lowered) is never taken: the window keeps its size, fitted
/// to a screen, and opens centred.</summary>
internal static class WindowLayout
{
    /// <summary>The least of the title bar that must lie on a screen for the saved place to be taken.</summary>
    private static readonly Size Grip = new(120, 20);

    public static readonly Size MinimumSize = new(800, 500);

    /// <summary>The width of a sizable window's invisible resize border: the sizing frame plus its padding (8 px at 100 %,
    /// against the 7 px Windows really leaves; a pixel over only loosens the fit).</summary>
    public static int InvisibleFrame => SystemInformation.FrameBorderSize.Width + GetSystemMetrics(SmCxPaddedBorder);

    private const int SmCxPaddedBorder = 92;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    /// <summary>Where the window opens from what was saved: the place (null = centred) and the size; null when no size
    /// was saved. <paramref name="frame"/> is the width of the window's invisible resize border (Windows 10 and 11 count
    /// it in the window's bounds, left, right and below): a window laid flush against a screen's edge reaches that far
    /// past it, and comes back as it was left.</summary>
    public static (Point? Location, Size Size)? Fit(int? x, int? y, int? width, int? height, IReadOnlyList<Rectangle> workingAreas, int frame = 0)
    {
        ArgumentNullException.ThrowIfNull(workingAreas);
        if (width is not { } w || height is not { } h || w <= 0 || h <= 0 || workingAreas.Count == 0)
        {
            return null;
        }

        var areas = workingAreas.Select(a => new Rectangle(a.Left - frame, a.Top, a.Width + 2 * frame, a.Height + frame)).ToList();
        // the screen that shows most of its title bar, else the largest
        var home = Rectangle.Empty;
        if (x is { } left && y is { } top)
        {
            var bar = new Rectangle(left, top, w, Grip.Height);
            home = areas.Where(a => Shows(a, bar)).OrderByDescending(a => Seen(a, bar)).FirstOrDefault();
        }

        var screen = home.IsEmpty ? areas.OrderByDescending(a => (long)a.Width * a.Height).First() : home;
        var size = new Size(Math.Clamp(w, MinimumSize.Width, Math.Max(MinimumSize.Width, screen.Width)), Math.Clamp(h, MinimumSize.Height, Math.Max(MinimumSize.Height, screen.Height)));
        if (home.IsEmpty)
        {
            return (null, size);
        }

        // on its screen: moved in just enough that the whole window shows
        return (new Point(Math.Clamp(x!.Value, screen.Left, Math.Max(screen.Left, screen.Right - size.Width)), Math.Clamp(y!.Value, screen.Top, Math.Max(screen.Top, screen.Bottom - size.Height))), size);
    }

    /// <summary>A splitter's distance: the saved one where it leaves both panels their least, else the fallback; never
    /// outside what the container allows.</summary>
    public static int Distance(int? saved, int fallback, int length, int splitterWidth, int least)
    {
        var most = Math.Max(0, length - splitterWidth - least);
        return Math.Clamp(saved ?? fallback, Math.Min(least, most), most);
    }

    private static bool Shows(Rectangle area, Rectangle titleBar)
    {
        var seen = Rectangle.Intersect(area, titleBar);
        return seen.Width >= Grip.Width && seen.Height >= Grip.Height;
    }

    private static long Seen(Rectangle area, Rectangle titleBar)
    {
        var seen = Rectangle.Intersect(area, titleBar);
        return (long)seen.Width * seen.Height;
    }
}
