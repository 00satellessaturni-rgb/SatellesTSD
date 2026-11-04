using SatellesTSD.Core.Import;
using SatellesTSD.Core.Lookups;
using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Review;
using SatellesTSD.Core.Store;
using SatellesTSD.Infrastructure;

namespace SatellesTSD.Tests;

/// <summary>The review half of the store: a decision kept per row and replaced under its id, the lines added by hand,
/// the guards, what unlinking and removing a file do to the decisions, the upgrade from v4.</summary>
public class ReviewStoreTests
{
    [Fact]
    public void An_overlay_is_keyed_by_its_row_and_a_hand_line_keeps_its_figures()
    {
        var (store, _, period, file) = ReviewFixtures.Setup();
        using (store)
        {
            var rows = ReviewFixtures.StatementRows(store, file);
            var saved = store.SaveReviewLine(ReviewLine.ForRow(period.Id, rows[0].Id) with { Category = " Üür ", Declare = false, Note = " " });
            Assert.NotEqual(Guid.Empty, saved.Id);
            Assert.Equal(("Üür", false, (string?)null), (saved.Category, saved.Declare, saved.Note));
            Assert.NotEqual(default, saved.UpdatedAt);

            var replaced = store.SaveReviewLine(ReviewLine.ForRow(period.Id, rows[0].Id) with { TypeCode = "57" });
            Assert.Equal(saved.Id, replaced.Id); // the same row, the same decision, replaced whole
            Assert.Null(replaced.Category);

            var hand = store.SaveReviewLine(ReviewLine.AddedByHand(period.Id, null, new DateOnly(2026, 10, 20), 100.5m, " Keegi ", "selgitus", "Palgad", "10", true, "märkus"));
            Assert.True(hand.IsByHand);
            Assert.Equal(("Keegi", 100.5m, new DateOnly(2026, 10, 20), "Palgad", "10", true, "märkus"), (hand.Payee, hand.Amount, hand.Date, hand.Category, hand.TypeCode, hand.Declare, hand.Note));
            Assert.Equal([replaced, hand], store.ReviewLines(period.Id));

            var edited = store.SaveReviewLine(hand with { Amount = 120m, Declare = null });
            Assert.Equal(hand.Id, edited.Id);
            Assert.Equal([replaced, edited], store.ReviewLines(period.Id));

            // "nobody" and "none" are kept apart from "as the run has it"
            var cleared = store.SaveReviewLine(ReviewLine.ForRow(period.Id, rows[3].Id) with { NoPerson = true, NoCategory = true });
            Assert.Equal((true, true, false), (cleared.NoPerson, cleared.NoCategory, cleared.IsEmpty));
            Assert.Equal([replaced, cleared, edited], store.ReviewLines(period.Id));
            store.DeleteReviewLine(cleared.Id);

            store.DeleteReviewLine(saved.Id);
            Assert.Equal([edited], store.ReviewLines(period.Id));
            Assert.Throws<KeyNotFoundException>(() => store.DeleteReviewLine(Guid.NewGuid()));
            Assert.Empty(store.ReviewLines(Guid.NewGuid()));
        }
    }

    [Fact]
    public void A_decision_needs_a_draft_period_a_linked_payment_row_and_the_companys_person()
    {
        var (store, company, period, file) = ReviewFixtures.Setup();
        using (store)
        {
            var rows = ReviewFixtures.StatementRows(store, file);
            var mari = store.AddPerson(Person.New(company.Id, "Mari", "Maasikas", ReviewFixtures.MariCode));

            // the fee line has no payee: a skipped row cannot carry a decision
            Assert.Contains("not a payment row", Assert.Throws<InvalidOperationException>(() => store.SaveReviewLine(ReviewLine.ForRow(period.Id, rows[1].Id))).Message);

            // a row of a file the period does not list
            var otherBytes = ReviewFixtures.Workbook(1000m);
            var other = store.AddImport(company.Id, ReviewFixtures.Importer.Read("teine.xlsx", otherBytes), otherBytes);
            var otherRow = ReviewFixtures.StatementRows(store, other)[0];
            Assert.Contains("linked to the period", Assert.Throws<InvalidOperationException>(() => store.SaveReviewLine(ReviewLine.ForRow(period.Id, otherRow.Id))).Message);

            // a person of another company, or none
            var stranger = store.AddCompany("99999999", "Teine OÜ");
            var strangerPerson = store.AddPerson(Person.New(stranger.Id, "Teine", "Isik", "50001010006"));
            Assert.Contains("another company", Assert.Throws<InvalidOperationException>(() => store.SaveReviewLine(ReviewLine.ForRow(period.Id, rows[0].Id) with { PersonId = strangerPerson.Id })).Message);
            Assert.Throws<KeyNotFoundException>(() => store.SaveReviewLine(ReviewLine.ForRow(period.Id, rows[0].Id) with { PersonId = Guid.NewGuid() }));
            Assert.Throws<KeyNotFoundException>(() => store.SaveReviewLine(ReviewLine.ForRow(Guid.NewGuid(), rows[0].Id)));

            // a hand line's figures
            Assert.Throws<ArgumentException>(() => store.SaveReviewLine(ReviewLine.AddedByHand(period.Id, mari.Id, new DateOnly(2026, 10, 1), 10m, " ")));
            Assert.Throws<ArgumentException>(() => store.SaveReviewLine(ReviewLine.AddedByHand(period.Id, mari.Id, new DateOnly(2026, 10, 1), -1m, "Keegi")));
            Assert.Throws<ArgumentException>(() => store.SaveReviewLine(ReviewLine.AddedByHand(period.Id, mari.Id, new DateOnly(2026, 10, 1), 1.005m, "Keegi")));
            Assert.Throws<ArgumentException>(() => store.SaveReviewLine(ReviewLine.AddedByHand(period.Id, mari.Id, new DateOnly(2026, 10, 1), 1m, "Keegi") with { Date = null }));

            var fine = store.SaveReviewLine(ReviewLine.ForRow(period.Id, rows[0].Id) with { PersonId = mari.Id, Declare = false });
            store.SetPeriodState(period.Id, PeriodState.Exported, DateTime.UtcNow);
            Assert.Contains("Exported", Assert.Throws<InvalidOperationException>(() => store.SaveReviewLine(fine with { Note = "late" })).Message);
            Assert.Contains("Exported", Assert.Throws<InvalidOperationException>(() => store.DeleteReviewLine(fine.Id)).Message);
            Assert.Equal([fine], store.ReviewLines(period.Id)); // read past Draft, as the record
        }
    }

    [Fact]
    public void Unlinking_a_file_forgets_the_periods_decisions_on_it_and_removal_waits_for_that()
    {
        var (store, _, period, file) = ReviewFixtures.Setup();
        using (store)
        {
            new MatchRunner(store).Run(period.Id);
            var rows = ReviewFixtures.StatementRows(store, file);
            store.SaveReviewLine(ReviewLine.ForRow(period.Id, rows[0].Id) with { Declare = false });
            var hand = store.SaveReviewLine(ReviewLine.AddedByHand(period.Id, null, new DateOnly(2026, 10, 1), 5m, "Keegi"));

            Assert.Contains("match run", Assert.Throws<InvalidOperationException>(() => store.RemoveImport(file.Id)).Message);
            store.ClearMatchRun(period.Id);
            Assert.Contains("review decision", Assert.Throws<InvalidOperationException>(() => store.RemoveImport(file.Id)).Message);

            store.UnlinkImport(period.Id, file.Id);
            Assert.Equal([hand], store.ReviewLines(period.Id)); // the row's decision went with the link; the hand line stays
            store.RemoveImport(file.Id);
            Assert.Empty(store.Imports(period.CompanyId));
        }
    }

    [Fact]
    public void A_decision_of_any_period_keeps_the_file_until_that_period_lets_go()
    {
        var (store, company, period, file) = ReviewFixtures.Setup();
        using (store)
        {
            var rows = ReviewFixtures.StatementRows(store, file);
            var november = store.GetOrCreatePeriod(company.Id, 2026, 11);
            store.LinkImport(november.Id, file.Id);
            store.SaveReviewLine(ReviewLine.ForRow(november.Id, rows[2].Id) with { Declare = false });

            store.UnlinkImport(period.Id, file.Id); // October never decided anything about the file
            Assert.Contains("review decision", Assert.Throws<InvalidOperationException>(() => store.RemoveImport(file.Id)).Message);
            store.UnlinkImport(november.Id, file.Id);
            Assert.Empty(store.ReviewLines(november.Id));
            store.RemoveImport(file.Id);
            Assert.Empty(store.Imports(company.Id));
        }
    }

    [Fact]
    public void A_v4_store_upgrades_to_v5_with_its_rows_intact()
    {
        var path = Path.Combine(Path.GetTempPath(), $"satellestsd-{Guid.NewGuid():N}.db");
        try
        {
            Guid periodId;
            Guid personId;
            using (var v4 = SqliteStore.OpenAtVersion(path, 4))
            {
                var company = v4.AddCompany("10235688", "Näidis OÜ");
                periodId = v4.GetOrCreatePeriod(company.Id, 2026, 10).Id;
                // a person row as a v4 build wrote it: no register key
                personId = Guid.NewGuid();
                v4.ExecuteRaw(
                    """
                    INSERT INTO person (id, company_id, first_name, last_name, personal_code, residency, funded_pension_member, pension_rate, on_pension, exemption_applied, minimum_applies, a1_case, residency_source, pension_source)
                    VALUES ($id, $company, 'Mari', 'Maasikas', $code, 'Resident', 1, '0.02', 0, 1, 1, 'None', 'Emta', 'Pensionikeskus')
                    """,
                    ("$id", personId.ToString("N")), ("$company", company.Id.ToString("N")), ("$code", ReviewFixtures.MariCode));
                Assert.Equal(4L, v4.ScalarRaw<long>("SELECT MAX(version) FROM schema_version"));
            }

            using var v5 = SqliteStore.Open(path);
            Assert.Equal((long)SqliteStore.SchemaVersion, v5.ScalarRaw<long>("SELECT MAX(version) FROM schema_version"));
            Assert.Empty(v5.ReviewLines(periodId));
            var line = v5.SaveReviewLine(ReviewLine.AddedByHand(periodId, personId, new DateOnly(2026, 10, 3), 10m, "Mari Maasikas"));
            Assert.Equal([line], v5.ReviewLines(periodId));
            var person = Assert.Single(v5.Persons(v5.FindCompany("10235688")!.Id));
            Assert.Equal((personId, (string?)null, LookupSource.Emta, 0.02m), (person.Id, person.RegisterKey, person.ResidencySource, person.PensionRate)); // a v4 person has no register link yet
            v5.UpdatePerson(person with { RegisterKey = " k " });
            Assert.Equal("k", Assert.Single(v5.Persons(person.CompanyId)).RegisterKey);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
