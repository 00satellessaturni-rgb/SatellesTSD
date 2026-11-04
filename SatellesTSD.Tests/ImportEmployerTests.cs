using SatellesTSD.Core.Import;
using SatellesTSD.Core.Review;
using SatellesTSD.Infrastructure;

namespace SatellesTSD.Tests;

/// <summary>The employer the files name (their TÖR export's registry code and name) and the company it stands for:
/// found in the store, or created from the export.</summary>
public class ImportEmployerTests
{
    private static object?[] RegisterRow(string torId, double code, string last, string first, long employerCode, string? employerName) =>
        [torId, code, last, first, new DateTime(2024, 2, 15), 1, "Tööleping", 2, "Kehtiv", null, null, null, null, null, null, null, null, "EE", null, null, null, null, null, null, null, null, employerCode, employerName];

    [Fact]
    public void The_employer_is_the_one_registry_code_of_the_files_TOR_sheets_with_its_name()
    {
        using var store = SqliteStore.InMemory();
        var pipeline = new PeriodPipeline(store, ReviewFixtures.Importer);
        var register = ReviewFixtures.Workbook();
        var statementOnly = Tables.Xlsx("LHV", [LayoutCatalog.Default.Find("lhv-et-short")!.Headers.ToArray<object?>(),
            ["EE497700771001131757", 1, new DateTime(2026, 10, 5), "EE12", "Mari Maasikas", "D", -905.92, null, "2026100500000001", "palk", "EUR", 48712219406d]]);

        Assert.Equal(new Employer("10235688", "Näidis OÜ"), pipeline.EmployerOf([("palgad.xlsx", register), ("lhv.xlsx", statementOnly)]));
        Assert.Null(pipeline.EmployerOf([("lhv.xlsx", statementOnly)]));
        Assert.Null(pipeline.EmployerOf([("junk.xlsx", new byte[] { 1, 2, 3 })])); // a file that cannot be read is the import's business
        Assert.Null(pipeline.EmployerOf([]));

        // a name-less export names the employer by its code; two employers are refused
        var nameless = Tables.Xlsx("Töötajate loend", [TorHeaders.All.ToArray<object?>(), RegisterRow("1", 48712219406d, "MAASIKAS", "MARI", 12345678, null)]);
        Assert.Equal(new Employer("12345678", "12345678"), pipeline.EmployerOf([("tor.xlsx", nameless)]));
        var two = Tables.Xlsx("Töötajate loend", [TorHeaders.All.ToArray<object?>(), RegisterRow("1", 48712219406d, "MAASIKAS", "MARI", 10235688, "Näidis OÜ"), RegisterRow("2", 38810140309d, "TAMM", "JAAN", 12345678, "Teine OÜ")]);
        Assert.Contains("several employers", Assert.Throws<ImportException>(() => pipeline.EmployerOf([("tor.xlsx", two)])).Message);
        Assert.Contains("several employers", Assert.Throws<ImportException>(() => pipeline.EmployerOf([("palgad.xlsx", register), ("tor.xlsx", nameless)])).Message);

        // the company shown among several employers takes the files; one not among them does not settle the matter
        Assert.Equal(new Employer("10235688", "Näidis OÜ"), pipeline.EmployerOf([("tor.xlsx", two)], "10235688"));
        Assert.Equal(new Employer("12345678", "Teine OÜ"), pipeline.EmployerOf([("tor.xlsx", two)], " 12345678 "));
        Assert.Contains("several employers", Assert.Throws<ImportException>(() => pipeline.EmployerOf([("tor.xlsx", two)], "99999999")).Message);
    }

    [Fact]
    public void The_company_is_found_by_the_registry_code_or_created_from_the_export()
    {
        using var store = SqliteStore.InMemory();
        var pipeline = new PeriodPipeline(store, ReviewFixtures.Importer);
        var (created, wasCreated) = pipeline.EnsureCompany(new Employer("10235688", "Näidis OÜ"));
        Assert.True(wasCreated);
        Assert.Equal(("10235688", "Näidis OÜ"), (created.RegistryCode, created.Name));
        var (found, wasCreatedAgain) = pipeline.EnsureCompany(new Employer("10235688", "Another Name OÜ")); // the store's name stands
        Assert.False(wasCreatedAgain);
        Assert.Equal(created, found);
        Assert.Single(store.Companies());
    }
}
