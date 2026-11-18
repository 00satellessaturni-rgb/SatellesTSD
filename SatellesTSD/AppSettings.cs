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

    /// <summary>The rule for a salary paid on a day two employments of different types are in force (the Seaded menu,
    /// decision 209), by its name; kept as text so a name this build does not know reads as the default instead of
    /// losing every other setting.</summary>
    public string? TwoContracts { get; set; }

    /// <summary>The two-contracts rule in force: the kept name, else the default (the Tööleping's type).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Core.Review.TwoContractsRule TwoContractsRule
    {
        get => Enum.TryParse<Core.Review.TwoContractsRule>(TwoContracts, ignoreCase: false, out var rule) && Enum.IsDefined(rule) && !int.TryParse(TwoContracts, out _)
            ? rule : Core.Review.TwoContractsRule.SalaryToEmploymentContract;
        set => TwoContracts = value.ToString();
    }

    /// <summary>The log under the window one line high instead of its default height (the Vaade menu; kept across sessions).</summary>
    public bool LogOneRow { get; set; }

    /// <summary>The persons grid without its columns per source (each statement's and the hand lines' sums): only
    /// "PALK NETO KOKKU" of the income shown (the Vaade menu; kept across sessions).</summary>
    public bool PersonsSalaryOnly { get; set; }

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

    /// <summary>The sizable dialogs' size, place, maximized state and named splitters, by the dialog's key (decision 210).</summary>
    public Dictionary<string, DialogPlace>? Dialogs { get; set; }

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
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }

        return new AppSettings();
    }

    /// <summary>A copy that names another store, company and month: what is written while a one-off store is open, so
    /// the remembered ones stand (decision 147).</summary>
    public AppSettings SavedWith(string? storePath, string? lastCompany, int? lastYear, int? lastMonth)
    {
        var copy = (AppSettings)MemberwiseClone();
        (copy.StorePath, copy.LastCompany, copy.LastYear, copy.LastMonth) = (storePath, lastCompany, lastYear, lastMonth);
        return copy;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
