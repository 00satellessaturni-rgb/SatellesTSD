using SatellesTSD.Core.Import;
using SatellesTSD.Core.Review;
using SatellesTSD.Infrastructure;
using SatellesTSD.Infrastructure.Lookups;

namespace SatellesTSD;

/// <summary>One open store and what the window works it with: the importer with the owner's layouts, the two
/// registers' clients (one session each, kept for the window's lifetime), the pipeline and the review workspace.
/// The store is one connection for one user; every call to it goes through the UI thread or a run the UI waits for.</summary>
public sealed class AppServices : IDisposable
{
    private readonly EmtaResidencyClient _emta;
    private readonly PensionikeskusClient _pensionikeskus;

    public AppServices(string storePath)
    {
        StorePath = storePath;
        Store = SqliteStore.Open(storePath);
        try
        {
            var catalog = LayoutCatalog.Default.WithOverrides(Store.Layouts());
            var importer = new Importer(catalog, new ExcelTableReader());
            _emta = EmtaResidencyClient.Create();
            _pensionikeskus = PensionikeskusClient.Create();
            Pipeline = new PeriodPipeline(Store, importer, _emta, _pensionikeskus);
            Workspace = new ReviewWorkspace(Store);
        }
        catch
        {
            Store.Dispose();
            throw;
        }
    }

    public string StorePath { get; }

    public SqliteStore Store { get; }

    public PeriodPipeline Pipeline { get; }

    public ReviewWorkspace Workspace { get; }

    public void Dispose()
    {
        _emta.Dispose();
        _pensionikeskus.Dispose();
        Store.Dispose();
    }
}
