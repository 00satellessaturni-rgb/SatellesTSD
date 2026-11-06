using System.Text.Json;

namespace SatellesTSD;

/// <summary>The window's own memory, outside the store: which file the firm's store is, the company and the period
/// last looked at, the folder the files were last picked from. %AppData%\SatellesTSD\settings.json.</summary>
public sealed class AppSettings
{
    public string? StorePath { get; set; }

    /// <summary>The registry code of the company last shown.</summary>
    public string? LastCompany { get; set; }

    public int? LastYear { get; set; }

    public int? LastMonth { get; set; }

    public string? LastFolder { get; set; }

    /// <summary>Where the XML files go.</summary>
    public string? ExportFolder { get; set; }

    /// <summary>The declaration's cut: false = one line per payment (the default), true = one line per person and payment
    /// type, the payments added up and dated by the latest (the Seaded menu; kept across sessions).</summary>
    public bool DeclareTotalsPerType { get; set; }

    /// <summary>The log under the window one line high instead of its default height (the Vaade menu; kept across sessions).</summary>
    public bool LogOneRow { get; set; }

    /// <summary>The window's place and size when it was last closed (its normal size when it was maximized).</summary>
    public int? WindowX { get; set; }

    public int? WindowY { get; set; }

    public int? WindowWidth { get; set; }

    public int? WindowHeight { get; set; }

    public bool WindowMaximized { get; set; }

    /// <summary>The person card's width beside the grids, in pixels.</summary>
    public int? CardWidth { get; set; }

    /// <summary>The persons grid's height above the payments grid, in pixels.</summary>
    public int? PersonsGridHeight { get; set; }

    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SatellesTSD", "settings.json");

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
            }
        }
        catch (JsonException)
        {
            // a settings file that cannot be read is as good as none
        }
        catch (IOException)
        {
        }

        return new AppSettings();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
