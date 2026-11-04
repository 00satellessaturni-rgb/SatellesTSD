namespace SatellesTSD;

static class Program
{
    /// <summary>The main entry point. <c>--store &lt;file&gt;</c> opens that store instead of the remembered one;
    /// <c>--settings &lt;file&gt;</c> reads and writes the settings there instead of %AppData%.</summary>
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
            }
        }

        settingsPath ??= AppSettings.DefaultPath;
        var crashLog = Path.Combine(Path.GetDirectoryName(settingsPath) ?? ".", "viga.log");
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            // what the window could not handle is written down, so the owner can send it along
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(crashLog)!);
                File.AppendAllText(crashLog, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {e.Exception}{Environment.NewLine}{Environment.NewLine}");
            }
            catch (IOException)
            {
            }

            MessageBox.Show(e.Exception.Message + Environment.NewLine + Environment.NewLine + crashLog, UiText.Problem, MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        Application.Run(new MainForm(settingsPath, AppSettings.Load(settingsPath), storePath));
    }
}
