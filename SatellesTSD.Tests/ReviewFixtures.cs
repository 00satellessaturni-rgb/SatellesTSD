using SatellesTSD.Core.Import;
using SatellesTSD.Core.Store;
using SatellesTSD.Infrastructure;

namespace SatellesTSD.Tests;

/// <summary>A company's month in a box for the review tests: a register of two (one employed in the month, one whose
/// contract ended before it) — and, when asked, a twin of the first by name, or the second without a personal code —
/// and one LHV statement with a salary, a fee line without a payee, a payment of the next month and a payment to
/// somebody outside the register.</summary>
internal static class ReviewFixtures
{
    public static readonly Importer Importer = new(LayoutCatalog.Default, new ExcelTableReader());

    public const string MariCode = "48712219406";
    public const string JaanCode = "38810140309";
    public const string TwinCode = "60001010008";
    public const string Source = "LHV pank *1757";

    /// <param name="mariType">Mari's employment type id in the register (1 = Tööleping, 501 = Tasuta töötamine …).</param>
    /// <param name="mariEnded">Mari's employment ended on 30 September 2026 (state 4), so no entry covers October.</param>
    /// <param name="mariPaused">Mari's employment is paused (state 3, Peatatud), with no end: the entry still covers October; paused since 3 August 2026 for Lapsehoolduspuhkus, until 30 June 2027.</param>
    /// <param name="mariRestarted">Mari's contract ended on 15 October 2026 and the next one began on the 16th: two rows of her code in the month.</param>
    /// <param name="mariSecond">Mari has a second valid contract at once: a VÕS leping (600) from 6 October 2026, the day after her salary.</param>
    public static byte[] Workbook(decimal salary = 905.92m, bool twin = false, bool jaanCodeless = false, int mariType = 1, bool mariEnded = false, bool mariPaused = false, bool mariRestarted = false, bool mariSecond = false)
    {
        List<object?[]> register =
        [
            TorHeaders.All.ToArray<object?>(),
            ["1", 48712219406d, "MAASIKAS", "MARI", mariRestarted ? new DateTime(2026, 10, 16) : new DateTime(2024, 2, 15), mariType, mariType == 1 ? "Tööleping" : "Muu", mariEnded ? 4 : mariPaused ? 3 : 2, mariEnded ? "Lõpetatud" : mariPaused ? "Peatatud" : "Kehtiv", mariEnded ? new DateTime(2026, 9, 30) : null, null, null, null, null, null, null, null, "EE", mariPaused ? new DateTime(2026, 8, 3) : null, mariPaused ? 2 : null, mariPaused ? "Lapsehoolduspuhkus" : null, mariPaused ? new DateTime(2027, 6, 30) : null, null, null, null, null, 10235688, "Näidis OÜ"],
            ["2", jaanCodeless ? null : 38810140309d, "TAMM", "JAAN", new DateTime(2020, 1, 1), 600, "VÕS leping", 4, "Lõpetatud", new DateTime(2026, 9, 30), null, null, null, null, null, null, null, "EE", null, null, null, null, null, null, null, null, 10235688, "Näidis OÜ"],
        ];
        if (mariSecond)
        {
            register.Add(["5", 48712219406d, "MAASIKAS", "MARI", new DateTime(2026, 10, 6), 600, "VÕS leping", 2, "Kehtiv", null, null, null, null, null, null, null, null, "EE", null, null, null, null, null, null, null, null, 10235688, "Näidis OÜ"]);
        }

        if (mariRestarted)
        {
            register.Add(["4", 48712219406d, "MAASIKAS", "MARI", new DateTime(2024, 2, 15), 1, "Tööleping", 4, "Lõpetatud", new DateTime(2026, 10, 15), null, null, null, null, null, null, null, "EE", null, null, null, null, null, null, null, null, 10235688, "Näidis OÜ"]);
        }

        if (twin)
        {
            register.Add(["3", 60001010008d, "MAASIKAS", "MARI", new DateTime(2025, 1, 1), 1, "Tööleping", 2, "Kehtiv", null, null, null, null, null, null, null, null, "EE", null, null, null, null, null, null, null, null, 10235688, "Näidis OÜ"]);
        }

        return Tables.Xlsx(
            ("Töötajate loend", register.ToArray(), false),
            ("LHV", [LayoutCatalog.Default.Find("lhv-et-short")!.Headers.ToArray<object?>(),
                ["EE497700771001131757", 1, new DateTime(2026, 10, 5), "EE12", "Mari Maasikas", "D", (double)-salary, null, "2026100500000001", "palk", "EUR", 48712219406d],
                ["EE497700771001131757", 2, new DateTime(2026, 10, 6), null, null, "D", -0.38, null, null, "Teenustasu", "EUR"],
                ["EE497700771001131757", 3, new DateTime(2026, 11, 2), "EE13", "Jaan Tamm", "D", -300d, null, "2026110200000002", "töövõtt", "EUR"],
                ["EE497700771001131757", 4, new DateTime(2026, 10, 12), "EE14", "Kinnisvara OÜ", "D", -450d, null, "2026101200000003", "ruumide kasutamine oktoober", "EUR"]], false));
    }

    public static (SqliteStore Store, Company Company, Period Period, ImportFile File) Setup(string registryCode = "10235688", bool twin = false, bool jaanCodeless = false, decimal salary = 905.92m, int mariType = 1, bool mariEnded = false, bool mariPaused = false, bool mariRestarted = false, bool mariSecond = false)
    {
        var store = SqliteStore.InMemory();
        var company = store.AddCompany(registryCode, "Näidis OÜ");
        var period = store.GetOrCreatePeriod(company.Id, 2026, 10);
        var bytes = Workbook(salary, twin, jaanCodeless, mariType, mariEnded, mariPaused, mariRestarted, mariSecond);
        var file = store.AddImport(company.Id, Importer.Read("palgad.xlsx", bytes), bytes);
        store.LinkImport(period.Id, file.Id);
        return (store, company, period, file);
    }

    /// <summary>The statement sheet's rows in sheet order: the salary, the fee line, the next month's payment, the stranger's.</summary>
    public static IReadOnlyList<StoredStatementRow> StatementRows(SqliteStore store, ImportFile file) =>
        store.StatementRows(store.Sheets(file.Id).Single(s => s.Kind == SheetKind.Statement).Id);
}
