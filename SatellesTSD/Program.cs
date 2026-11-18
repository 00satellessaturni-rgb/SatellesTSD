using SatellesTSD.Infrastructure.Updates;

namespace SatellesTSD;

static class Program
{
    /// <summary>The signal the old copy waits for while this copy starts after an update; null otherwise. Cleared by
    /// the window once it has given the signal.</summary>
    public static string? ReadySignal { get; set; }

    /// <summary>The settings file named on the command line, when it is not the usual one; the new version started by
    /// an update is given the same.</summary>
    public static string? SettingsPathOverride { get; private set; }

    /// <summary>The store named on the command line, when one was; handed on likewise.</summary>
    public static string? StorePathOverride { get; private set; }

    /// <summary>The main entry point. <c>--store &lt;file&gt;</c> opens that store instead of the remembered one;
    /// <c>--settings &lt;file&gt;</c> reads and writes the settings there instead of %AppData%; <c>--update-ready
    /// &lt;name&gt;</c> is given by the copy that installed this one, which waits for the signal of that name.</summary>
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string? storePath = null;
        string? settingsPath = null;
        for (var i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--store":
                    storePath = args[++i];
                    break;
                case "--settings":
                    settingsPath = args[++i];
                    break;
                case Updater.ReadyArgument:
                    ReadySignal = args[++i];
                    break;
            }
        }

        // kept whole: a new version started by an update runs from the program's folder, not from where this one was started
        storePath = storePath is null ? null : Path.GetFullPath(storePath);
        settingsPath = settingsPath is null ? null : Path.GetFullPath(settingsPath);
        SettingsPathOverride = settingsPath;
        StorePathOverride = storePath;
        settingsPath ??= AppSettings.DefaultPath;
        _crashLog = Path.Combine(Path.GetDirectoryName(settingsPath) ?? ".", "viga.log");
        var crashLog = _crashLog;

        // a failure before the window stands (the start-up, a worker thread) is written down too, rather than dying mute at every start
        AppDomain.CurrentDomain.UnhandledException += (_, e) => WriteCrash(e.ExceptionObject);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            WriteCrash(e.Exception);

            if (ReadySignal is not null)
            {
                // started by an update and not yet known to run: end at once, so the copy that waits puts itself back,
                // rather than staying open behind an error message it would take for "running"
                Environment.Exit(Updater.FailedExitCode);
            }

            MessageBox.Show(e.Exception.Message + Environment.NewLine + Environment.NewLine + crashLog, UiText.Problem, MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        // the published program tidies the leftovers of an earlier update; a test host or a build never touches them
        var exePath = Environment.ProcessPath;
        if (exePath is not null && string.Equals(Path.GetFileName(exePath), Updater.ExeName, StringComparison.OrdinalIgnoreCase))
        {
            Updater.CleanUp(exePath);
        }

        Application.Run(new MainForm(settingsPath, AppSettings.Load(settingsPath), storePath));
    }

    private static string? _crashLog;

    /// <summary>What the program could not handle, written down beside the settings (viga.log), so the owner can send it along.</summary>
    private static void WriteCrash(object? failure)
    {
        if (_crashLog is not { } crashLog)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(crashLog)!);
            File.AppendAllText(crashLog, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {failure}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Started by an update and unable to go on (the store handed on does not open): the failure is written
    /// down and the copy ends at once, so the copy that waits puts itself back and opens the store again (decision 178).
    /// Does nothing when no update waits for this copy.</summary>
    public static void EndFailedStart(object failure)
    {
        if (ReadySignal is null)
        {
            return;
        }

        WriteCrash(failure);
        Environment.Exit(Updater.FailedExitCode);
    }
}
