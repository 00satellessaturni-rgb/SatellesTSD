using SatellesTSD.Core.Review;
using SatellesTSD.Core.Store;
using SatellesTSD.Infrastructure;

namespace SatellesTSD.Tests;

/// <summary>The latest summary of a search or a register check, kept per company as it was shown (plan item 67): the
/// summary's own JSON round trip, the store's round trip, one per company replaced by the next, the upgrade from v13.</summary>
public class RunSummaryStoreTests
{
    private static readonly Guid Mari = Guid.Parse("0b6f3c1e-2d4a-4e8f-9a1b-3c5d7e9f1a2b");
    private static readonly Guid Row = Guid.Parse("1c7a4d2f-3e5b-4f9a-8b2c-4d6e8f0a2b3c");
    private static readonly Guid Part = Guid.Parse("2d8b5e3a-4f6c-4a0b-9c3d-5e7f9a1b3c4d");

    private static RunSummary Sample(Guid companyId, Guid periodId, int month = 10) => new(
        companyId, "Näidis OÜ", "10235688", periodId, 2026, month, new DateTime(2026, 11, 23, 9, 30, 15, DateTimeKind.Utc), Searched: true, CheckedRegisters: true, CheckCompleted: false,
        [
            new SummarySection(RunSummary.SearchSection, "Otsing",
                [new SummaryGroup("SHARED_ROW", "Rida leiti mitmele isikule", [new SummaryOccurrence("LHV [3]", "SHARED_ROW: kellelegi seda ei arvestatud")])]),
            new SummarySection(RunSummary.PaymentsSection, "Maksed",
                [new SummaryGroup("NO_TYPE", null, [new SummaryOccurrence("Kask Mari — LHV [2]", "NO_TYPE: liik puudub", Mari, new DateOnly(2026, 10, 5), 905.92m, Row, null, false),
                    new SummaryOccurrence("Kask Mari — LHV [2] osa 2", "NO_TYPE: liik puudub", Mari, null, null, Row, Part, true)])]),
            new SummarySection(RunSummary.RegistersSection, "Registrid", []),
        ]);

    [Fact]
    public void A_summary_comes_back_from_its_json_as_it_was_and_a_text_that_is_none_is_no_summary()
    {
        var summary = Sample(Guid.NewGuid(), Guid.NewGuid());
        var back = RunSummary.FromJson(summary.ToJson());
        Assert.Equal(summary, back);
        Assert.Equal(3, back!.Total);
        Assert.Equal(2, back.Sections[1].Groups[0].Count);
        Assert.True(back.Sections[1].Groups[0].Occurrences[1].IsPart);
        Assert.True(back.Sections[1].Groups[0].Occurrences[0].IsPayment);
        Assert.False(back.Sections[0].Groups[0].Occurrences[0].IsPayment);
        Assert.NotEqual(summary, summary with { Month = 9 });
        Assert.Null(RunSummary.FromJson("not json"));
        Assert.Null(RunSummary.FromJson("{}"));
    }

    [Fact]
    public void A_company_keeps_its_latest_summary_alone_and_another_companys_is_its_own()
    {
        using var store = SqliteStore.InMemory();
        var company = store.AddCompany("10235688", "Näidis OÜ");
        var other = store.AddCompany("12345678", "Muu OÜ");
        var october = store.GetOrCreatePeriod(company.Id, 2026, 10);
        var november = store.GetOrCreatePeriod(company.Id, 2026, 11);
        Assert.Null(store.RunSummary(company.Id));

        var first = new StoredRunSummary(company.Id, october.Id, new DateTime(2026, 11, 23, 9, 30, 15, 123, DateTimeKind.Utc).AddTicks(4567), Sample(company.Id, october.Id).ToJson());
        store.SaveRunSummary(first);
        var kept = store.RunSummary(company.Id);
        Assert.Equal(first with { MadeAt = new DateTime(2026, 11, 23, 9, 30, 15, 123, DateTimeKind.Utc) }, kept); // to the millisecond, as every time the store keeps
        Assert.Equal(Sample(company.Id, october.Id), RunSummary.FromJson(kept!.SummaryJson));

        // the next, of another month, replaces it; the other company's stays its own
        var theirs = new StoredRunSummary(other.Id, null, new DateTime(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc), "{\"x\":1}");
        store.SaveRunSummary(theirs);
        var second = new StoredRunSummary(company.Id, november.Id, new DateTime(2026, 11, 23, 10, 0, 0, DateTimeKind.Utc), Sample(company.Id, november.Id, 11).ToJson());
        store.SaveRunSummary(second);
        Assert.Equal(second, store.RunSummary(company.Id));
        Assert.Equal(theirs, store.RunSummary(other.Id));
        Assert.Equal(2L, store.ScalarRaw<long>("SELECT COUNT(*) FROM run_summary"));

        Assert.Throws<KeyNotFoundException>(() => store.SaveRunSummary(first with { CompanyId = Guid.NewGuid() }));
        Assert.Null(store.RunSummary(Guid.NewGuid()));
    }

    [Fact]
    public void A_v13_store_upgrades_to_v14_with_no_summary_until_one_is_made()
    {
        var path = Path.Combine(Path.GetTempPath(), $"satellestsd-{Guid.NewGuid():N}.db");
        try
        {
            Guid companyId;
            Guid periodId;
            using (var v13 = SqliteStore.OpenAtVersion(path, 13))
            {
                var company = v13.AddCompany("10235688", "Näidis OÜ");
                companyId = company.Id;
                periodId = v13.GetOrCreatePeriod(company.Id, 2026, 10).Id;
                Assert.Equal(0L, v13.ScalarRaw<long>("SELECT COUNT(*) FROM sqlite_master WHERE name = 'run_summary'"));
            }

            using var v14 = SqliteStore.Open(path);
            Assert.Equal((long)SqliteStore.SchemaVersion, v14.ScalarRaw<long>("SELECT MAX(version) FROM schema_version"));
            Assert.True(SqliteStore.SchemaVersion >= 14); // v14 brought the table; a later version keeps it
            Assert.Null(v14.RunSummary(companyId));
            var stored = new StoredRunSummary(companyId, periodId, new DateTime(2026, 11, 23, 9, 30, 0, DateTimeKind.Utc), Sample(companyId, periodId).ToJson());
            v14.SaveRunSummary(stored);
            Assert.Equal(stored, v14.RunSummary(companyId));
        }
        finally
        {
            File.Delete(path);
            File.Delete(SqliteStore.BackupPathFor(path));
        }
    }
}
