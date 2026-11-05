using SatellesTSD.Core.Declare;
using SatellesTSD.Core.Import;
using SatellesTSD.Core.Lookups;
using SatellesTSD.Core.Report;
using SatellesTSD.Core.Review;
using SatellesTSD.Infrastructure;
using SatellesTSD.Infrastructure.Lookups;

namespace SatellesTSD;

/// <summary>One open store and what the window works it with: the importer with the owner's layouts, the two
/// registers' clients (one session each, kept for the window's lifetime), the pipeline and the review workspace.
/// The store is one connection for one user; every call to it goes through the UI thread or a run the UI waits for.</summary>
public sealed class AppServices : IDisposable
{
    private readonly IDisposable? _emta;
    private readonly IDisposable? _pensionikeskus;

    public AppServices(string storePath)
        : this(storePath, null, null, null)
    {
    }

    /// <summary>The services with registers of the caller's own (a test's, never the live sites); null takes the real clients.</summary>
    internal AppServices(string storePath, IResidencyLookup? residency, IPensionLookup? pension, LookupOptions? lookupOptions)
    {
        StorePath = storePath;
        Store = SqliteStore.Open(storePath);
        try
        {
            var catalog = LayoutCatalog.Default.WithOverrides(Store.Layouts());
            var importer = new Importer(catalog, new ExcelTableReader());
            if (residency is null)
            {
                var emta = EmtaResidencyClient.Create();
                _emta = emta;
                residency = emta;
            }

            if (pension is null)
            {
                var pensionikeskus = PensionikeskusClient.Create();
                _pensionikeskus = pensionikeskus;
                pension = pensionikeskus;
            }

            Pipeline = new PeriodPipeline(Store, importer, residency, pension, lookupOptions);
            Workspace = new ReviewWorkspace(Store);
            Declarer = new PeriodDeclarer(Store);
            Submitter = new PeriodSubmitter(Store, Path.Combine(AppContext.BaseDirectory, "schemas", "apa-tsd"));
            Reports = new ReportBuilder(Store, Workspace, Declarer);
        }
        catch
        {
            _emta?.Dispose();
            _pensionikeskus?.Dispose();
            Store.Dispose();
            throw;
        }
    }

    public string StorePath { get; }

    public SqliteStore Store { get; }

    public PeriodPipeline Pipeline { get; }

    public ReviewWorkspace Workspace { get; }

    public PeriodDeclarer Declarer { get; }

    public PeriodSubmitter Submitter { get; }

    public ReportBuilder Reports { get; }

    public void Dispose()
    {
        _emta?.Dispose();
        _pensionikeskus?.Dispose();
        Store.Dispose();
    }
}
