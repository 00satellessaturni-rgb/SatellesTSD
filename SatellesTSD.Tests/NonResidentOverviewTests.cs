using SatellesTSD.Core.Declare;
using SatellesTSD.Core.Import;
using SatellesTSD.Core.Lookups;
using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Report;
using SatellesTSD.Core.Review;
using SatellesTSD.Core.Store;
using SatellesTSD.Core.Tax;
using SatellesTSD.Infrastructure;

namespace SatellesTSD.Tests;

/// <summary>The non-residents of every company of the store: who is listed, what is said of the basic exemption for
/// them (the tick, the lines it was used on as a non-resident, what MTA holds), the order, and the workbook.</summary>
public class NonResidentOverviewTests
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), $"satellestsd-nr-{Guid.NewGuid():N}");

    /// <summary>The fixture's month with its paid person's facts set and the lines built: the person as a non-resident of
    /// an EEA country with the exemption ticked, or as a resident with it.</summary>
    private static (SqliteStore Store, Company Company, Period Period, Guid Paid, ReviewWorkspace Workspace, PeriodDeclarer Declarer) Built(bool nonResident)
    {
        var (store, company, period, _) = ReviewFixtures.Setup();
        new MatchRunner(store).Run(period.Id);
        var workspace = new ReviewWorkspace(store, TaxRates.Year2026);
        var paid = workspace.Load(period.Id).Persons[0].Person!;
        workspace.SavePerson(
            paid with
            {
                Residency = nonResident ? Residency.NonResident : Residency.Resident, ResidenceCountry = nonResident ? "FI" : null,
                FundedPensionMember = false, BasicExemptionApplied = true, SocialTaxMinimumApplies = false,
            },
            confirmResidency: true);
        var declarer = new PeriodDeclarer(store, TaxRates.Year2026);
        declarer.Build(period.Id);
        return (store, company, period, paid.Id, workspace, declarer);
    }

    private static Person PersonOf(SqliteStore store, Company company, Guid id) => store.Persons(company.Id).Single(p => p.Id == id);

    [Fact]
    public void Every_companys_non_residents_are_listed_with_the_exemptions_tick_and_the_lines_it_was_used_on()
    {
        var (store, first, period, paidId, _, _) = Built(nonResident: true);
        using (store)
        {
            var line = Assert.Single(store.Payments(period.Id), p => p.PersonId == paidId);
            Assert.Equal(Residency.NonResident, line.Facts!.Residency);
            Assert.True(line.Result!.ExemptionUsed > 0m);

            // a second company: a non-resident known by an MRR code, the tick off, no line; a third: residents only
            var second = store.AddCompany("10000018", "Aabits OÜ");
            var plain = store.AddPerson(Person.New(second.Id, "Peeter", "Pihl", null, Residency.NonResident) with { MrrCode = "NR1234567", ResidencySource = LookupSource.Manual });
            var third = store.AddCompany("10000021", "Kolmas OÜ");
            store.AddPerson(Person.New(third.Id, "Kati", "Kask", "49001010008") with { BasicExemptionApplied = true });

            var rows = NonResidentOverview.Build(store);

            // by the company's name, then the person's; the company with residents only is not there
            Assert.Equal([(second.Id, plain.Id), (first.Id, paidId)], rows.Select(r => (r.Company.Id, r.Person.Id)));
            var quiet = rows[0];
            Assert.Equal((true, false, false, 0, 0m, 0, 0), (quiet.NonResidentNow, quiet.Applied, quiet.Used, quiet.ExemptionLines, quiet.ExemptionUsed, quiet.OnServer, quiet.Unsent));
            Assert.Empty(quiet.Months);
            var found = rows[1];
            Assert.Equal((true, true, true, 1, line.Result.ExemptionUsed, 0, 0), (found.NonResidentNow, found.Applied, found.Used, found.ExemptionLines, found.ExemptionUsed, found.OnServer, found.Unsent));
            Assert.Equal(["2026-10"], found.Months);

            // the workbook: the companies with their counts, then the persons
            var book = NonResidentOverview.ToBook(rows, new DateTime(2026, 11, 17, 9, 30, 0));
            Assert.Equal("mitteresidendid-2026-11-17_09-30-00.xlsx", book.FileName);
            Assert.Equal([NonResidentOverview.CompaniesSheet, NonResidentOverview.PersonsSheet], book.Sheets.Select(s => s.Name));
            var read = Tables.ReadXlsx(ReportWriter.ToBytes(book), book.FileName);
            var companies = read.Sheets[0];
            Assert.Equal(NonResidentOverview.CompanyHeaders, companies.Rows[0].Select(c => c.Text));
            Assert.Equal(["10000018", "Aabits OÜ", "1"], companies.Rows[1].Select(c => c.Text ?? "").Where(t => t.Length > 0)); // nobody ticked, nothing used: the two counts left blank
            Assert.Equal([first.RegistryCode, first.Name, "1", "1", "1"], companies.Rows[2].Select(c => c.Text ?? ""));
            var persons = read.Sheets[1];
            Assert.Equal(NonResidentOverview.PersonHeaders, persons.Rows[0].Select(c => c.Text));
            Assert.Equal(3, persons.Rows.Count);
            var mrr = persons.Rows[1].Select(c => c.Text ?? "").ToList();
            Assert.Equal(("NR1234567", "mitteresident", "käsitsi", "ei"), (mrr[4], mrr[5], mrr[7], mrr[9])); // named by the code the declaration uses
            var written = persons.Rows[2].Select(c => c.Text ?? "").ToList();
            Assert.Equal((first.RegistryCode, "mitteresident", "FI", "jah", "1", "2026-10"), (written[0], written[5], written[6], written[9], written[10], written[14]));
            Assert.Equal(CellKind.Number, persons.Rows[2][NonResidentOverview.AmountColumn].Kind);
            Assert.Equal(CellKind.Date, persons.Rows[2][NonResidentOverview.DateColumn].Kind);
        }
    }

    [Fact]
    public void A_line_is_judged_by_the_residency_it_was_built_with_not_by_the_cards_today()
    {
        // built for a resident with the exemption, the person a non-resident now: listed for the tick, no line counted
        var (store, company, _, paidId, _, _) = Built(nonResident: false);
        using (store)
        {
            store.UpdatePerson(PersonOf(store, company, paidId) with { Residency = Residency.NonResident, ResidencySource = LookupSource.Emta });
            var row = Assert.Single(NonResidentOverview.Build(store));
            Assert.Equal((true, true, false, 0), (row.NonResidentNow, row.Applied, row.Used, row.ExemptionLines));
        }

        // built for a non-resident with the exemption, the person a resident on the card now: still listed, for the line
        var (other, company2, _, paid2, _, _) = Built(nonResident: true);
        using (other)
        {
            other.UpdatePerson(PersonOf(other, company2, paid2) with { Residency = Residency.Resident, ResidencySource = LookupSource.Emta });
            var row = Assert.Single(NonResidentOverview.Build(other));
            Assert.Equal((false, false, true, 1), (row.NonResidentNow, row.Applied, row.Used, row.ExemptionLines));
            var book = NonResidentOverview.ToBook([row], new DateTime(2026, 11, 17));
            Assert.Equal("resident", book.Sheets[1].Rows[1].Cells[5].Text);
            Assert.True(book.Sheets[0].Rows[1].Cells[2].IsEmpty); // no non-resident in the company now…
            Assert.False(book.Sheets[0].Rows[1].Cells[4].IsEmpty); // …and one person with the exemption on a non-resident's line
        }
    }

    [Fact]
    public void What_MTA_holds_is_told_apart_from_a_refused_attempt_and_from_a_correction_not_yet_sent()
    {
        var (store, company, period, paidId, workspace, declarer) = Built(nonResident: true);
        using (store)
        {
            var submitter = new PeriodSubmitter(store);
            submitter.Export(period.Id, Folder);
            var sent = Assert.Single(store.Payments(period.Id), p => p.PersonId == paidId);
            var held = NonResidentOverview.Build(store).Single();
            Assert.Equal((1, 1, 0), (held.ExemptionLines, held.OnServer, held.Unsent));

            // on the server; the month confirmed and opened again, the tick taken off and the lines built: the draft M
            // carries no exemption, MTA still holds the one of the R — counted, and marked as not sent yet
            submitter.Resolve(sent.Id, onServer: true);
            submitter.MarkUploaded(period.Id);
            submitter.MarkConfirmed(period.Id, force: true);
            submitter.Reopen(period.Id);
            workspace.SavePerson(PersonOf(store, company, paidId) with { BasicExemptionApplied = false });
            declarer.Build(period.Id);
            var standing = Assert.Single(store.Payments(period.Id), p => p.PersonId == paidId);
            Assert.Equal((LineAction.M, PaymentStatus.Draft, 0m), (standing.Action, standing.Status, standing.Result!.ExemptionUsed));
            var pending = NonResidentOverview.Build(store).Single();
            Assert.Equal((1, 1, 1, sent.Result!.ExemptionUsed, false), (pending.ExemptionLines, pending.OnServer, pending.Unsent, pending.ExemptionUsed, pending.Applied));
        }
    }

    [Fact]
    public void A_line_MTA_refused_is_not_a_line_the_exemption_stands_on()
    {
        var (store, _, period, paidId, _, _) = Built(nonResident: true);
        using (store)
        {
            var submitter = new PeriodSubmitter(store);
            submitter.Export(period.Id, Folder);
            submitter.Resolve(Assert.Single(store.Payments(period.Id), p => p.PersonId == paidId).Id, onServer: false);
            Assert.Equal(PaymentStatus.Rejected, Assert.Single(store.Payments(period.Id), p => p.PersonId == paidId).Status); // its only version: the standing one
            var refused = NonResidentOverview.Build(store).Single();
            Assert.Equal((0, 0, false, true), (refused.ExemptionLines, refused.OnServer, refused.Used, refused.Applied));
        }
    }

    [Fact]
    public void A_key_cancelled_on_the_server_carries_no_exemption_any_more()
    {
        var (store, _, period, paidId, workspace, declarer) = Built(nonResident: true);
        using (store)
        {
            var submitter = new PeriodSubmitter(store);
            submitter.Export(period.Id, Folder);
            submitter.Resolve(Assert.Single(store.Payments(period.Id), p => p.PersonId == paidId).Id, onServer: true);
            submitter.MarkUploaded(period.Id);
            submitter.MarkConfirmed(period.Id, force: true);
            submitter.Reopen(period.Id);

            // the payment taken out of the declaration: a T is built on its key — still to be sent, MTA holds the R
            var payment = workspace.Load(period.Id).Persons.Single(p => p.PersonId == paidId).Payments.Single(p => !p.IsByHand);
            workspace.SetDeclare(payment, false);
            declarer.Build(period.Id);
            var cancel = Assert.Single(store.Payments(period.Id), p => p.PersonId == paidId);
            Assert.Equal((LineAction.T, PaymentStatus.Draft), (cancel.Action, cancel.Status));
            var pending = NonResidentOverview.Build(store).Single();
            Assert.Equal((1, 1, 1), (pending.ExemptionLines, pending.OnServer, pending.Unsent));

            // the T sent and taken by MTA: the key is dead, nothing stands on it
            submitter.Export(period.Id, Folder, PeriodSubmitter.ReasonsFor(cancel.Section)[0]);
            submitter.Resolve(Assert.Single(store.Payments(period.Id), p => p.PersonId == paidId).Id, onServer: true);
            Assert.Equal(PaymentStatus.Cancelled, Assert.Single(store.Payments(period.Id), p => p.PersonId == paidId).Status);
            var gone = NonResidentOverview.Build(store).Single();
            Assert.Equal((0, 0, 0, false), (gone.ExemptionLines, gone.OnServer, gone.Unsent, gone.Used));
        }
    }

    [Fact]
    public void A_store_without_non_residents_gives_an_empty_list_and_a_workbook_of_headers()
    {
        var (store, _, _, _) = ReviewFixtures.Setup();
        using (store)
        {
            var rows = NonResidentOverview.Build(store);
            Assert.Empty(rows);
            var book = NonResidentOverview.ToBook(rows, new DateTime(2026, 11, 17));
            Assert.All(book.Sheets, s => Assert.Single(s.Rows));
        }
    }
}
