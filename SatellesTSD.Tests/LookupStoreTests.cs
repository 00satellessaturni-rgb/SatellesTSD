using SatellesTSD.Core.Lookups;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Store;
using SatellesTSD.Infrastructure;

namespace SatellesTSD.Tests;

/// <summary>The lookup half of the store: answers kept as they came, the person's source marks, the upgrade from v3.</summary>
public class LookupStoreTests
{
    [Fact]
    public void An_answer_round_trips_and_the_latest_for_a_control_date_is_found()
    {
        using var store = SqliteStore.InMemory();
        var company = store.AddCompany("10235688", "Näidis OÜ");
        var person = store.AddPerson(Person.New(company.Id, "Mari", "Maasikas", "48712219406"));
        var control = new DateOnly(2026, 10, 31);

        var first = store.AddLookup(new LookupRecord(Guid.Empty, person.Id, LookupKind.Residency, LookupSource.Emta, default, control, null, "request timeout or access denied: HTTP 503", null, null));
        var second = store.AddLookup(new LookupRecord(Guid.Empty, person.Id, LookupKind.Residency, LookupSource.Emta, new DateTime(2026, 11, 4, 9, 30, 0, DateTimeKind.Utc), control, """{"AsOf":"2026-11-04"}""", null, "Otsingutulemustele vastavaid isikuid ei leitud.", " ", " 48712219406 "));

        Assert.NotEqual(Guid.Empty, first.Id);
        Assert.NotEqual(default, first.CheckedAt);
        Assert.Equal(new DateTime(2026, 11, 4, 9, 30, 0, DateTimeKind.Utc), second.CheckedAt);
        Assert.Null(second.Note); // blank text is no text
        Assert.Equal("48712219406", second.PersonalCode); // the code as asked, trimmed
        Assert.Null(first.PersonalCode);
        Assert.Equal([first, second], store.Lookups(person.Id));
        Assert.Equal(second, store.LatestLookup(person.Id, LookupKind.Residency, control));
        Assert.Null(store.LatestLookup(person.Id, LookupKind.Pension, control));
        Assert.Null(store.LatestLookup(person.Id, LookupKind.Residency, control.AddDays(-1)));
        Assert.Empty(store.Lookups(Guid.NewGuid()));
        Assert.Throws<KeyNotFoundException>(() => store.AddLookup(first with { Id = Guid.Empty, PersonId = Guid.NewGuid() }));
    }

    [Fact]
    public void A_persons_source_marks_round_trip_and_default_to_unknown()
    {
        using var store = SqliteStore.InMemory();
        var company = store.AddCompany("10235688", "Näidis OÜ");
        var person = store.AddPerson(Person.New(company.Id, "Mari", "Maasikas", "48712219406"));
        Assert.Equal((LookupSource.Unknown, LookupSource.Unknown), (person.ResidencySource, person.PensionSource));

        store.UpdatePerson(person with { Residency = Residency.NonResident, ResidencySource = LookupSource.Manual, PensionSource = LookupSource.Derived });
        var read = store.FindPerson(company.Id, "48712219406")!;
        Assert.Equal((Residency.NonResident, LookupSource.Manual, LookupSource.Derived), (read.Residency, read.ResidencySource, read.PensionSource));
        Assert.Equal(read, Assert.Single(store.Persons(company.Id)));
    }

    [Fact]
    public void A_v3_store_upgrades_to_v4_with_its_persons_intact()
    {
        var path = Path.Combine(Path.GetTempPath(), $"satellestsd-{Guid.NewGuid():N}.db");
        try
        {
            Guid personId;
            using (var v3 = SqliteStore.OpenAtVersion(path, 3))
            {
                var company = v3.AddCompany("10235688", "Näidis OÜ");
                // a person row as a v3 build wrote it: no source columns
                personId = Guid.NewGuid();
                v3.ExecuteRaw(
                    """
                    INSERT INTO person (id, company_id, first_name, last_name, personal_code, residency, funded_pension_member, pension_rate, on_pension, exemption_applied, minimum_applies, a1_case)
                    VALUES ($id, $company, 'Mari', 'Maasikas', '48712219406', 'Resident', 1, '0.02', 0, 1, 1, 'None')
                    """,
                    ("$id", personId.ToString("N")), ("$company", company.Id.ToString("N")));
                Assert.Equal(3L, v3.ScalarRaw<long>("SELECT MAX(version) FROM schema_version"));
            }

            using var v4 = SqliteStore.Open(path);
            Assert.Equal((long)SqliteStore.SchemaVersion, v4.ScalarRaw<long>("SELECT MAX(version) FROM schema_version"));
            var person = Assert.Single(v4.Persons(v4.FindCompany("10235688")!.Id));
            Assert.Equal((personId, true, 0.02m, LookupSource.Unknown, LookupSource.Unknown), (person.Id, person.FundedPensionMember, person.PensionRate, person.ResidencySource, person.PensionSource));
            Assert.Empty(v4.Lookups(personId));
            v4.AddLookup(new LookupRecord(Guid.Empty, personId, LookupKind.Pension, LookupSource.Pensionikeskus, default, new DateOnly(2026, 10, 31), "{}", null, null, null));
            Assert.Single(v4.Lookups(personId));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
