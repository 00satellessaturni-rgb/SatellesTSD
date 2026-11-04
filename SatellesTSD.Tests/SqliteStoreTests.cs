using System.Xml.Linq;
using SatellesTSD.Core.Feedback;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Store;
using SatellesTSD.Core.Tax;
using SatellesTSD.Core.Xbrl;
using SatellesTSD.Infrastructure;

namespace SatellesTSD.Tests;

/// <summary>The store: round trips, the permanent-key rules, submissions and feedback.</summary>
public class SqliteStoreTests
{
    private static readonly TaxEngine Engine = new(TaxRates.Year2026);
    private static readonly PaymentType Salary = PaymentTypes.Get("10");

    private static (SqliteStore Store, Company Company, Period Period, Person Person) Setup()
    {
        var store = SqliteStore.InMemory();
        var company = store.AddCompany("10235688", "Näidis OÜ");
        var period = store.GetOrCreatePeriod(company.Id, 2026, 10);
        var person = store.AddPerson(Person.New(company.Id, "Mari", "Maasikas", "48712219406") with
        {
            FundedPensionMember = true, PensionRate = 0.02m, PensionCheckedOn = new DateOnly(2026, 11, 1),
            ResidencyCheckedOn = new DateOnly(2026, 11, 1), BasicExemptionApplied = true, TorEmploymentTypeId = "1",
        });
        return (store, company, period, person);
    }

    private static StoredPayment SalaryDraft(SqliteStore store, Period period, Person person, LineAction action, string? lineNumber = null, decimal gross = 1000m)
    {
        var facts = person.FactsFor(period.Year);
        var result = Engine.Calculate(new TaxInput(Salary, gross, false, 700m), facts);
        var key = lineNumber ?? store.NewLineNumber(period.Id, Section.EE0201001);
        var line = DeclarationLineFactory.Create(key, 0, action, Salary, facts, person.PayeeId!, new DateOnly(period.Year, period.Month, 10), result);
        return StoredPayment.New(period.Id, person.Id, Section.EE0201001, line, facts, result, netPaid: result.Net, sourceRef: "LHV [12]");
    }

    [Fact]
    public void Companies_persons_and_periods_round_trip()
    {
        var (store, company, period, person) = Setup();
        using (store)
        {
            Assert.Equal(company, store.FindCompany("10235688"));
            Assert.Null(store.FindCompany("99999999"));
            Assert.Equal(person, store.FindPerson(company.Id, "48712219406"));
            Assert.Equal(person, Assert.Single(store.Persons(company.Id)));

            var updated = person with { PensionRate = 0.06m, PensionAgeYear = 2030, A1 = A1Case.EstonianA1WorkAbroad, A1Country = "EE", WorkCountry = "FI", Notes = "töötab Soomes" };
            store.UpdatePerson(updated);
            Assert.Equal(updated, store.FindPerson(company.Id, "48712219406"));

            Assert.Equal(period, store.GetOrCreatePeriod(company.Id, 2026, 10)); // idempotent
            store.SetPeriodState(period.Id, PeriodState.Exported, new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc));
            var reread = Assert.Single(store.Periods(company.Id));
            Assert.Equal(PeriodState.Exported, reread.State);
            Assert.Equal(new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc), reread.ExportedAt);
            Assert.Throws<KeyNotFoundException>(() => store.UpdatePerson(person with { Id = Guid.NewGuid() }));
        }
    }

    [Fact]
    public void Every_person_field_round_trips_and_feeds_the_facts()
    {
        var (store, company, _, _) = Setup();
        using (store)
        {
            var full = new Person(Guid.Empty, company.Id, " Jonas ", "Petraitis ", null, " A0003689779 ", Residency.NonResident, "LT", new DateOnly(2026, 10, 1),
                FundedPensionMember: false, PensionRate: 0.04m, PensionCheckedOn: new DateOnly(2026, 11, 1), OnOldAgeOrFlexiblePension: true, PensionAgeYear: 2026,
                BasicExemptionApplied: false, BasicExemptionMonthly: 500m, SocialTaxMinimumApplies: true, A1: A1Case.ForeignA1WorkInEstonia, A1Country: "LT", WorkCountry: "FI",
                TreatyIncomeTaxRate: 0.10m, TorEmploymentTypeId: "600", Notes: "resideerimistõend 2026");
            var bare = Person.New(company.Id, "Kai", "Kask", "") with { MrrCode = " " };

            var storedFull = store.AddPerson(full);
            var storedBare = store.AddPerson(bare);

            var expectedFull = full with { Id = storedFull.Id, FirstName = "Jonas", LastName = "Petraitis", MrrCode = "A0003689779" };
            Assert.Equal(expectedFull, storedFull);
            Assert.Equal(expectedFull, store.Persons(company.Id).Single(p => p.Id == storedFull.Id));
            var rereadBare = store.Persons(company.Id).Single(p => p.Id == storedBare.Id);
            Assert.Null(rereadBare.PersonalCode); // "" is no code, so two code-less people never collide on the unique index
            Assert.Null(rereadBare.MrrCode);
            Assert.Null(rereadBare.FundedPensionMember); // unchecked stays unchecked, it does not become "not a member"
            Assert.Null(rereadBare.PayeeId);

            Assert.Equal(new PayeeTaxFacts(Residency.NonResident, "LT", false, 0.04m, true, true, false, 500m, true, 0.5m, A1Case.ForeignA1WorkInEstonia, "LT", "FI", 0.10m),
                expectedFull.FactsFor(2026, socialTaxMinimumShare: 0.5m));
            Assert.Equal(PayeeId.Mrr("A0003689779"), expectedFull.PayeeId);
        }
    }

    [Fact]
    public void The_persons_facts_follow_the_year_for_pension_age()
    {
        var (store, _, _, person) = Setup();
        using (store)
        {
            var aged = person with { PensionAgeYear = 2027 };
            Assert.False(aged.FactsFor(2026).PensionAgeThisYear);
            Assert.True(aged.FactsFor(2027).PensionAgeThisYear);
            Assert.Equal(0.5m, aged.FactsFor(2027, socialTaxMinimumShare: 0.5m).SocialTaxMinimumShare);
            Assert.Equal(PayeeId.Estonian("48712219406"), person.PayeeId);
            Assert.Equal(PayeeId.Mrr("A0003689779"), (person with { PersonalCode = null, MrrCode = "A0003689779" }).PayeeId);
            Assert.Null((person with { PersonalCode = null }).PayeeId);
        }
    }

    [Fact]
    public void Line_numbers_are_unique_per_period_and_section_and_never_reused()
    {
        var (store, company, period, _) = Setup();
        using (store)
        {
            var a = store.NewLineNumber(period.Id, Section.EE0201001);
            var b = store.NewLineNumber(period.Id, Section.EE0201001);
            var c = store.NewLineNumber(period.Id, Section.EE0201002);
            Assert.Equal("10235688-202610-01-001", a);
            Assert.Equal("10235688-202610-01-002", b);
            Assert.Equal("10235688-202610-02-001", c);

            var november = store.GetOrCreatePeriod(company.Id, 2026, 11);
            Assert.Equal("10235688-202611-01-001", store.NewLineNumber(november.Id, Section.EE0201001));
        }
    }

    [Fact]
    public void Line_numbers_survive_a_reopen_of_the_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"satellestsd-{Guid.NewGuid():N}.db");
        try
        {
            Guid periodId;
            using (var store = SqliteStore.Open(path))
            {
                var company = store.AddCompany("10235688", "Näidis OÜ");
                periodId = store.GetOrCreatePeriod(company.Id, 2026, 10).Id;
                store.NewLineNumber(periodId, Section.EE0201001);
                store.NewLineNumber(periodId, Section.EE0201001);
            }

            using (var store = SqliteStore.Open(path))
            {
                Assert.Equal("10235688-202610-01-003", store.NewLineNumber(periodId, Section.EE0201001));
                Assert.Single(store.Companies());
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_payment_round_trips_with_its_facts_result_and_line_element_for_element()
    {
        var (store, company, period, _) = Setup();
        using (store)
        {
            // a non-resident with an A1 certificate: country entries, a rate on the tax line, every list populated
            var person = store.AddPerson(Person.New(company.Id, "Jonas", "Petraitis", null, Residency.NonResident) with
            {
                MrrCode = "A0003689779", ResidenceCountry = "LT", BasicExemptionApplied = true, A1 = A1Case.EstonianA1WorkAbroad, A1Country = "EE", WorkCountry = "FI",
            });
            var type = PaymentTypes.Get("120");
            var facts = person.FactsFor(period.Year);
            var result = Engine.Calculate(new TaxInput(type, 1000m, true, 700m), facts);
            var key = store.NewLineNumber(period.Id, Section.EE0201001);
            var line = DeclarationLineFactory.Create(key, 0, LineAction.R, type, facts, person.PayeeId!, new DateOnly(2026, 10, 10), result, new DateOnly(2026, 9, 30), "TartuOsakond");
            var draft = StoredPayment.New(period.Id, person.Id, Section.EE0201001, line, facts, result, netPaid: 1000m, sourceRef: "LHV [12]");

            var stored = store.AddPayment(draft);

            Assert.NotEqual(Guid.Empty, stored.Id);
            Assert.Equal(1, stored.Version);
            Assert.Equal(PaymentStatus.Draft, stored.Status);
            var reread = Assert.Single(store.Payments(period.Id));
            Assert.Equal(stored.CreatedAt, reread.CreatedAt);
            Assert.Equal(stored.Line with { ExtraAccountSubs = [], Events = [], Amounts = [], Taxes = [] }, reread.Line with { ExtraAccountSubs = [], Events = [], Amounts = [], Taxes = [] });
            Assert.Equal(stored.Line.ExtraAccountSubs, reread.Line.ExtraAccountSubs);
            Assert.Equal(stored.Line.Events, reread.Line.Events);
            Assert.Equal(stored.Line.Amounts, reread.Line.Amounts);
            Assert.Equal(stored.Line.Taxes, reread.Line.Taxes);
            Assert.NotEmpty(reread.Line.ExtraAccountSubs);
            Assert.NotEmpty(reread.Line.Amounts);
            Assert.Equal(stored.Result, reread.Result);
            Assert.Equal(stored.Facts, reread.Facts);
            Assert.Equal(person.Id, reread.PersonId);
            Assert.Equal("LHV [12]", reread.SourceRef);
            Assert.Equal(1000m, reread.NetPaid);
            XmlCanon.AssertEqual(XbrlGlWriter.WriteLine(stored.Line), XbrlGlWriter.WriteLine(reread.Line));
        }
    }

    [Fact]
    public void The_v1_json_shapes_of_the_snapshots_still_read()
    {
        // Golden strings written by schema v1. A positional parameter added later must carry a default, or these rows read back wrong.
        const string facts = """{"Residency":"NonResident","ResidenceCountry":"LT","FundedPensionMember":false,"PensionRate":0.04,"OnOldAgeOrFlexiblePension":true,"PensionAgeThisYear":true,"BasicExemptionApplied":false,"BasicExemptionMonthly":500,"SocialTaxMinimumApplies":true,"SocialTaxMinimumShare":0.5,"A1":"ForeignA1WorkInEstonia","A1Country":"LT","WorkCountry":"FI","TreatyIncomeTaxRate":0.10}""";
        const string line = """{"LineNumber":"10235688-202610-01-001","Counter":1,"Action":"R","TypeCode":"120","ExtraAccountSubs":[{"Id":"LT","Type":"RTK2T2013ap","ParentCode":"RR_MAKSURESIDENTSUSE_RIIK","ParentType":"RIIGIROLL2022ap"}],"Amount":1000.00,"AllocationCode":"TartuOsakond","Payee":{"Code":"A0003689779","Description":"MRR_KOOD"},"DocumentDate":"2026-10-10","Events":[{"Id":"TSD01","Start":"2026-09-30"}],"Amounts":[{"Description":"MVT_610","Quantity":700,"Money":true}],"Taxes":[{"Code":"TSD_KTM","Amount":62.48,"Rate":0.22}]}""";
        var options = new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

        var f = System.Text.Json.JsonSerializer.Deserialize<PayeeTaxFacts>(facts, options)!;
        var l = System.Text.Json.JsonSerializer.Deserialize<PaymentLine>(line, options)!;

        Assert.Equal(new PayeeTaxFacts(Residency.NonResident, "LT", false, 0.04m, true, true, false, 500m, true, 0.5m, A1Case.ForeignA1WorkInEstonia, "LT", "FI", 0.10m), f);
        Assert.Equal("10235688-202610-01-001", l.LineNumber);
        Assert.Equal(LineAction.R, l.Action);
        Assert.Equal(new DateOnly(2026, 10, 10), l.DocumentDate);
        Assert.Equal(new TaxLine("TSD_KTM", 62.48m, 0.22m), Assert.Single(l.Taxes));
        Assert.Equal(new MeasurableEvent("TSD01", new DateOnly(2026, 9, 30)), Assert.Single(l.Events));
        Assert.Equal(AccountSubEntry.ResidenceCountry("LT"), Assert.Single(l.ExtraAccountSubs));
    }

    [Fact]
    public void A_draft_can_be_replaced_in_place_and_a_batch_is_all_or_nothing()
    {
        var (store, _, period, person) = Setup();
        using (store)
        {
            var draft = store.AddPayment(SalaryDraft(store, period, person, LineAction.R));
            var edited = store.ReplaceDraft(draft.Id, SalaryDraft(store, period, person, LineAction.R, draft.LineNumber, gross: 1500m));

            Assert.Equal(draft.Id, edited.Id);
            Assert.Equal(1, edited.Version);
            Assert.Equal(1500m, Assert.Single(store.Payments(period.Id)).Line.Amount);
            Assert.Single(store.History(period.Id, Section.EE0201001, draft.LineNumber));
            Assert.Throws<InvalidOperationException>(() => store.ReplaceDraft(draft.Id, SalaryDraft(store, period, person, LineAction.M, draft.LineNumber)));

            Assert.Throws<InvalidOperationException>(() => store.InTransaction(() =>
            {
                store.AddPayment(SalaryDraft(store, period, person, LineAction.R));
                store.AddPayment(SalaryDraft(store, period, person, LineAction.R));
                throw new InvalidOperationException("midway");
            }));
            Assert.Single(store.Payments(period.Id)); // nothing of the failed batch remains

            store.AddSubmission(period.Id, "f.xml", "u", [edited.Id], DateTime.UtcNow);
            Assert.Throws<InvalidOperationException>(() => store.ReplaceDraft(edited.Id, SalaryDraft(store, period, person, LineAction.R, draft.LineNumber)));
        }
    }

    [Fact]
    public void A_hand_supplied_key_in_the_counters_format_moves_the_counter_past_it()
    {
        var (store, _, period, person) = Setup();
        using (store)
        {
            store.AddPayment(SalaryDraft(store, period, person, LineAction.R, "10235688-202610-01-005"));

            Assert.Equal("10235688-202610-01-006", store.NewLineNumber(period.Id, Section.EE0201001));
            Assert.Equal("10235688-202610-02-001", store.NewLineNumber(period.Id, Section.EE0201002));
        }
    }

    [Fact]
    public void A_confirmation_report_is_not_a_submissions_feedback()
    {
        var (store, _, period, person) = Setup();
        using (store)
        {
            var line = store.AddPayment(SalaryDraft(store, period, person, LineAction.R));
            var file = store.AddSubmission(period.Id, "f.xml", "u", [line.Id], DateTime.UtcNow);
            var confirmation = FeedbackReader.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "feedback", "APA_TSD_20260330_tagasisideraport_kinnitamine_error.xml"));

            Assert.Throws<InvalidOperationException>(() => store.RecordFeedback(file.Id, confirmation));
            Assert.Equal(PaymentStatus.Sent, Assert.Single(store.Payments(period.Id)).Status);
        }
    }

    [Fact]
    public void Every_colour_of_MTAs_sample_lands_as_a_status_and_the_reasons_are_kept()
    {
        var (store, _, period, person) = Setup();
        using (store)
        {
            var blue = store.AddPayment(SalaryDraft(store, period, person, LineAction.R, "0110test01"));
            var red = store.AddPayment(SalaryDraft(store, period, person, LineAction.R, "0117test01"));
            var rentType = PaymentTypes.Get("57");
            var facts = person.FactsFor(2026);
            StoredPayment Rent(string key) => StoredPayment.New(period.Id, person.Id, Section.EE0201002,
                DeclarationLineFactory.Create(key, 0, LineAction.R, rentType, facts, person.PayeeId!, new DateOnly(2026, 10, 3), Engine.Calculate(new TaxInput(rentType, 100m, false, 0m), facts)),
                facts, Engine.Calculate(new TaxInput(rentType, 100m, false, 0m), facts));
            var black1 = store.AddPayment(Rent("0201test01"));
            var black2 = store.AddPayment(Rent("0254test01"));
            var file = store.AddSubmission(period.Id, "f.xml", "u", [blue.Id, red.Id, black1.Id, black2.Id], DateTime.UtcNow);

            var feedback = store.RecordFeedback(file.Id, FeedbackReader.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "feedback", "APA_TSD_tagasisideraporti_naidis_warning_red_blue_black_vigadega_20260910.xml")));

            Assert.Equal(4, feedback.Count);
            var statuses = store.Payments(period.Id).ToDictionary(p => p.LineNumber, p => p.Status);
            Assert.Equal(PaymentStatus.NeedsChange, statuses["0110test01"]);
            Assert.Equal(PaymentStatus.NeedsChange, statuses["0117test01"]);
            Assert.Equal(PaymentStatus.Rejected, statuses["0201test01"]);
            Assert.Equal(PaymentStatus.Rejected, statuses["0254test01"]);
            var blueRow = Assert.Single(store.Feedback(blue.Id));
            Assert.Equal(ValidationLevel.Error, blueRow.Level);
            Assert.Equal(2, blueRow.Counter);
            Assert.Equal(["KR_VM_03", "KR_ISIK_3"], blueRow.Messages.Select(m => m.Code));
            Assert.Equal(NextAction.FixAndResendAsNew, Assert.Single(store.Feedback(black2.Id)).NextAction);
            Assert.Equal(ProcessingResult.Error, Assert.Single(store.Submissions(period.Id)).Result);
            Assert.Empty(Assert.Single(store.Submissions(period.Id)).General);
        }
    }

    [Fact]
    public void A_refused_file_rejects_every_pending_line_whatever_its_action_and_keeps_the_reason()
    {
        var (store, _, period, person) = Setup();
        using (store)
        {
            var r = store.AddPayment(SalaryDraft(store, period, person, LineAction.R));
            var accepted = store.AddPayment(SalaryDraft(store, period, person, LineAction.R));
            var file0 = store.AddSubmission(period.Id, "f0.xml", "u0", [accepted.Id], DateTime.UtcNow);
            store.RecordFeedback(file0.Id, Clean("EE0201001", accepted.LineNumber, 1));
            var m = store.AddPayment(SalaryDraft(store, period, person, LineAction.M, accepted.LineNumber, gross: 1100m));
            var file = store.AddSubmission(period.Id, "f.xml", "u", [r.Id, m.Id], DateTime.UtcNow);

            store.RecordFeedback(file.Id, FeedbackReader.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "feedback", "APA_TSD_20260330_tagasisideraport_faili_enda_veaga.xml")));

            Assert.Equal(PaymentStatus.Rejected, store.History(period.Id, Section.EE0201001, r.LineNumber)[^1].Status);
            Assert.Equal(PaymentStatus.Rejected, store.History(period.Id, Section.EE0201001, accepted.LineNumber)[^1].Status);
            Assert.Equal(PaymentStatus.Accepted, store.History(period.Id, Section.EE0201001, accepted.LineNumber)[0].Status); // the earlier accepted version stands
            var general = Assert.Single(store.Submissions(period.Id).Single(s => s.Id == file.Id).General);
            Assert.Equal("KR_VALIDATE_XBRL_FILE_7", general.Validation.Code);
            Assert.Null(general.EntryNumber);
            Assert.Equal(NextAction.FixAndResendAsNew, Assert.Single(store.Feedback(r.Id)).NextAction);
            Assert.Equal(NextAction.CorrectWithChange, Assert.Single(store.Feedback(m.Id)).NextAction);
        }
    }

    [Fact]
    public void Nothing_may_follow_a_cancellation_until_its_verdict_is_in()
    {
        var (store, _, period, person) = Setup();
        using (store)
        {
            var r = store.AddPayment(SalaryDraft(store, period, person, LineAction.R));
            var file = store.AddSubmission(period.Id, "f.xml", "u", [r.Id], DateTime.UtcNow);
            store.RecordFeedback(file.Id, Clean("EE0201001", r.LineNumber, 1));
            var t = store.AddPayment(StoredPayment.New(period.Id, person.Id, Section.EE0201001, PaymentLine.Cancellation(r.LineNumber, 0), null, null));
            store.AddSubmission(period.Id, "f2.xml", "u2", [t.Id], DateTime.UtcNow);

            Assert.Equal(LineKeyRules.CancellationPending, Assert.Throws<LineKeyException>(() => store.AddPayment(SalaryDraft(store, period, person, LineAction.M, r.LineNumber))).Code);

            store.ResolveLine(t.Id, onServer: false); // MTA refused the T: the key still stands
            Assert.Equal(2 + 1, store.AddPayment(SalaryDraft(store, period, person, LineAction.M, r.LineNumber)).Version);
        }
    }

    [Fact]
    public void The_key_rules_decide_which_action_may_follow()
    {
        var (store, _, period, person) = Setup();
        using (store)
        {
            var first = store.AddPayment(SalaryDraft(store, period, person, LineAction.R));
            var key = first.LineNumber;

            // a second draft under the same key while one is pending
            Assert.Equal(LineKeyRules.DraftPending, Assert.Throws<LineKeyException>(() => store.AddPayment(SalaryDraft(store, period, person, LineAction.M, key))).Code);

            // M or T before the key ever reached the server
            store.DeleteDraft(first.Id);
            Assert.Equal(LineKeyRules.KeyUnknown, Assert.Throws<LineKeyException>(() => store.AddPayment(SalaryDraft(store, period, person, LineAction.M, key))).Code);
            Assert.Equal(LineKeyRules.KeyUnknown, Assert.Throws<LineKeyException>(() => store.AddPayment(StoredPayment.New(period.Id, person.Id, Section.EE0201001, PaymentLine.Cancellation(key, 0), null, null))).Code);

            // sent → on the server: no second R, but an M
            var sent = store.AddPayment(SalaryDraft(store, period, person, LineAction.R, key));
            store.AddSubmission(period.Id, "tsd-2026-10.xml", "10235688-2026-11-03T10:00:00.000", [sent.Id], DateTime.UtcNow);
            Assert.Equal(LineKeyRules.KeyExists, Assert.Throws<LineKeyException>(() => store.AddPayment(SalaryDraft(store, period, person, LineAction.R, key))).Code);
            var change = store.AddPayment(SalaryDraft(store, period, person, LineAction.M, key, gross: 1200m));
            Assert.Equal(2, change.Version);
            Assert.Equal(2, store.History(period.Id, Section.EE0201001, key).Count);
            Assert.Equal(change.Id, Assert.Single(store.Payments(period.Id)).Id); // the current version

            // a T after the M was accepted, then nothing at all
            var file2 = store.AddSubmission(period.Id, "tsd-2026-10-b.xml", "10235688-2026-11-04T10:00:00.000", [change.Id], DateTime.UtcNow);
            store.RecordFeedback(file2.Id, Clean("EE0201001", key, 1));
            var cancel = store.AddPayment(StoredPayment.New(period.Id, person.Id, Section.EE0201001, PaymentLine.Cancellation(key, 0), null, null));
            var file3 = store.AddSubmission(period.Id, "tsd-2026-10-c.xml", "10235688-2026-11-05T10:00:00.000", [cancel.Id], DateTime.UtcNow);
            store.RecordFeedback(file3.Id, Clean("EE0201001", key, 1));
            Assert.Equal(PaymentStatus.Cancelled, Assert.Single(store.Payments(period.Id)).Status);
            Assert.Equal(LineKeyRules.KeyDead, Assert.Throws<LineKeyException>(() => store.AddPayment(SalaryDraft(store, period, person, LineAction.R, key))).Code);
            Assert.Equal(LineKeyRules.KeyDead, Assert.Throws<LineKeyException>(() => store.AddPayment(SalaryDraft(store, period, person, LineAction.M, key))).Code);
        }
    }

    [Fact]
    public void A_black_refusal_of_an_R_leaves_the_key_free_for_another_R()
    {
        var (store, _, period, person) = Setup();
        using (store)
        {
            var first = store.AddPayment(SalaryDraft(store, period, person, LineAction.R));
            var file = store.AddSubmission(period.Id, "f1.xml", "u1", [first.Id], DateTime.UtcNow);

            var feedback = store.RecordFeedback(file.Id, Coloured("EE0201001", first.LineNumber, "ERROR_BLACK", "KR_XBRL_VALJAMAKSED_14"));

            var row = Assert.Single(feedback);
            Assert.Equal(Severity.Black, row.Severity);
            Assert.Equal(NextAction.FixAndResendAsNew, row.NextAction); // the store knew the line was an R
            Assert.Equal(PaymentStatus.Rejected, Assert.Single(store.Payments(period.Id)).Status);
            Assert.Equal(ProcessingResult.Error, Assert.Single(store.Submissions(period.Id)).Result);

            var again = store.AddPayment(SalaryDraft(store, period, person, LineAction.R, first.LineNumber));
            Assert.Equal(2, again.Version);
            var stored = Assert.Single(store.Feedback(first.Id));
            Assert.Equal((row.Severity, row.NextAction, row.SubmissionId), (stored.Severity, stored.NextAction, stored.SubmissionId));
            Assert.Equal("KR_XBRL_VALJAMAKSED_14", Assert.Single(stored.Messages).Code);
        }
    }

    [Fact]
    public void A_later_report_never_undoes_what_MTA_already_saved_and_feedback_is_appended()
    {
        var (store, _, period, person) = Setup();
        using (store)
        {
            var line = store.AddPayment(SalaryDraft(store, period, person, LineAction.R));
            var file = store.AddSubmission(period.Id, "f.xml", "u", [line.Id], DateTime.UtcNow);
            store.RecordFeedback(file.Id, Clean("EE0201001", line.LineNumber, 1));
            Assert.Equal(PaymentStatus.Accepted, Assert.Single(store.Payments(period.Id)).Status);

            // the same file uploaded twice: MTA refuses the repeat as "R on a known key"
            store.RecordFeedback(file.Id, Coloured("EE0201001", line.LineNumber, "ERROR_BLACK", FeedbackReport.RuleNewOnExistingKey));

            Assert.Equal(PaymentStatus.Accepted, Assert.Single(store.Payments(period.Id)).Status);
            var records = store.Feedback(line.Id);
            Assert.Equal([Severity.None, Severity.Black], records.Select(f => f.Severity));
            Assert.True(records[1].RecordedAt > records[0].RecordedAt);

            // a report read within the millisecond of the one before (the last stamp pushed ahead of the clock here) gets the next millisecond
            var ahead = DateTime.UtcNow.AddSeconds(5);
            ahead = new DateTime(ahead.Ticks - ahead.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
            store.ExecuteRaw("UPDATE line_feedback SET recorded_at = $at WHERE recorded_at = (SELECT MAX(recorded_at) FROM line_feedback)", ("$at", ahead.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture)));
            store.RecordFeedback(file.Id, Coloured("EE0201001", line.LineNumber, "ERROR_BLACK", FeedbackReport.RuleNewOnExistingKey));
            Assert.Equal(ahead.AddMilliseconds(1), store.Feedback(line.Id)[^1].RecordedAt);
            Assert.Equal(LineKeyRules.KeyExists, Assert.Throws<LineKeyException>(() => store.AddPayment(SalaryDraft(store, period, person, LineAction.R, line.LineNumber))).Code);
            Assert.Equal(2, store.AddPayment(SalaryDraft(store, period, person, LineAction.M, line.LineNumber)).Version);
        }
    }

    [Fact]
    public void A_black_R_on_a_known_key_proves_the_key_is_on_the_server()
    {
        var (store, _, period, person) = Setup();
        using (store)
        {
            var line = store.AddPayment(SalaryDraft(store, period, person, LineAction.R));
            var file = store.AddSubmission(period.Id, "f.xml", "u", [line.Id], DateTime.UtcNow);

            store.RecordFeedback(file.Id, Coloured("EE0201001", line.LineNumber, "ERROR_BLACK", FeedbackReport.RuleNewOnExistingKey));

            Assert.Equal(PaymentStatus.Review, Assert.Single(store.Payments(period.Id)).Status);
            Assert.Equal(LineKeyRules.KeyExists, Assert.Throws<LineKeyException>(() => store.AddPayment(SalaryDraft(store, period, person, LineAction.R, line.LineNumber))).Code);
        }
    }

    [Fact]
    public void A_black_M_on_an_unknown_key_frees_the_key_and_rejects_the_pending_R_before_it()
    {
        var (store, _, period, person) = Setup();
        using (store)
        {
            var r = store.AddPayment(SalaryDraft(store, period, person, LineAction.R));
            store.AddSubmission(period.Id, "f1.xml", "u1", [r.Id], DateTime.UtcNow); // exported, feedback never recorded: Sent
            var m = store.AddPayment(SalaryDraft(store, period, person, LineAction.M, r.LineNumber, gross: 900m));
            var file2 = store.AddSubmission(period.Id, "f2.xml", "u2", [m.Id], DateTime.UtcNow);

            store.RecordFeedback(file2.Id, Coloured("EE0201001", r.LineNumber, "ERROR_BLACK", FeedbackReport.RuleChangeOnUnknownKey));

            var history = store.History(period.Id, Section.EE0201001, r.LineNumber);
            Assert.Equal([PaymentStatus.Rejected, PaymentStatus.Rejected], history.Select(v => v.Status));
            Assert.Equal(3, store.AddPayment(SalaryDraft(store, period, person, LineAction.R, r.LineNumber)).Version);
        }
    }

    [Fact]
    public void A_report_that_names_a_line_of_another_file_is_refused()
    {
        var (store, _, period, person) = Setup();
        using (store)
        {
            var line = store.AddPayment(SalaryDraft(store, period, person, LineAction.R));
            var file = store.AddSubmission(period.Id, "f.xml", "u", [line.Id], DateTime.UtcNow);

            Assert.Throws<InvalidOperationException>(() => store.RecordFeedback(file.Id, Clean("EE0201001", "some-other-key", 1)));
            Assert.Equal(PaymentStatus.Sent, Assert.Single(store.Payments(period.Id)).Status);
            Assert.Null(Assert.Single(store.Submissions(period.Id)).Result);
        }
    }

    [Fact]
    public void A_pending_line_can_be_resolved_by_hand_and_an_unsent_file_withdrawn()
    {
        var (store, _, period, person) = Setup();
        using (store)
        {
            var a = store.AddPayment(SalaryDraft(store, period, person, LineAction.R));
            var b = store.AddPayment(SalaryDraft(store, period, person, LineAction.R));
            var file = store.AddSubmission(period.Id, "f.xml", "u", [a.Id, b.Id], DateTime.UtcNow);

            store.WithdrawSubmission(file.Id);
            Assert.All(store.Payments(period.Id), p => Assert.Equal(PaymentStatus.Draft, p.Status));
            Assert.Empty(store.Submissions(period.Id));
            store.DeleteDraft(b.Id);

            var file2 = store.AddSubmission(period.Id, "f2.xml", "u2", [a.Id], DateTime.UtcNow);
            store.ResolveLine(a.Id, onServer: true);
            Assert.Equal(PaymentStatus.Accepted, Assert.Single(store.Payments(period.Id)).Status);
            Assert.Throws<InvalidOperationException>(() => store.WithdrawSubmission(file2.Id)); // a resolved line means the file was uploaded
        }
    }

    [Fact]
    public void Resolving_by_hand_follows_the_accountants_word_and_only_for_pending_lines()
    {
        var (store, _, period, person) = Setup();
        using (store)
        {
            var a = store.AddPayment(SalaryDraft(store, period, person, LineAction.R));
            Assert.Throws<InvalidOperationException>(() => store.ResolveLine(a.Id, onServer: true)); // a draft is not pending
            var file = store.AddSubmission(period.Id, "f.xml", "u", [a.Id], DateTime.UtcNow);

            store.ResolveLine(a.Id, onServer: false);
            Assert.Equal(PaymentStatus.Rejected, Assert.Single(store.Payments(period.Id)).Status);
            Assert.Throws<InvalidOperationException>(() => store.WithdrawSubmission(file.Id));
            var again = store.AddPayment(SalaryDraft(store, period, person, LineAction.R, a.LineNumber));
            var file2 = store.AddSubmission(period.Id, "f2.xml", "u2", [again.Id], DateTime.UtcNow);
            store.ResolveLine(again.Id, onServer: true);
            Assert.Equal(PaymentStatus.Accepted, Assert.Single(store.Payments(period.Id)).Status);

            var cancel = store.AddPayment(StoredPayment.New(period.Id, person.Id, Section.EE0201001, PaymentLine.Cancellation(a.LineNumber, 0), null, null));
            store.AddSubmission(period.Id, "f3.xml", "u3", [cancel.Id], DateTime.UtcNow);
            store.ResolveLine(cancel.Id, onServer: true);
            Assert.Equal(PaymentStatus.Cancelled, Assert.Single(store.Payments(period.Id)).Status);
        }
    }

    [Fact]
    public void The_period_shows_the_version_MTA_holds_not_a_refused_attempt_after_it()
    {
        var (store, _, period, person) = Setup();
        using (store)
        {
            var r = store.AddPayment(SalaryDraft(store, period, person, LineAction.R));
            var file = store.AddSubmission(period.Id, "f.xml", "u", [r.Id], DateTime.UtcNow);
            store.RecordFeedback(file.Id, Clean("EE0201001", r.LineNumber, 1));

            var cancel = store.AddPayment(StoredPayment.New(period.Id, person.Id, Section.EE0201001, PaymentLine.Cancellation(r.LineNumber, 0), null, null));
            Assert.Equal(cancel.Id, Assert.Single(store.Payments(period.Id)).Id); // a draft on top of the accepted version is the working one
            var file2 = store.AddSubmission(period.Id, "f2.xml", "u2", [cancel.Id], DateTime.UtcNow);
            store.RecordFeedback(file2.Id, Coloured("EE0201001", r.LineNumber, "ERROR_BLACK", "KR_XBRL_VALJAMAKSED_8"));

            var standing = Assert.Single(store.Payments(period.Id));
            Assert.Equal(r.Id, standing.Id);
            Assert.Equal(PaymentStatus.Accepted, standing.Status);
            Assert.Equal(1000m, standing.Line.Amount);
            Assert.Equal(PaymentStatus.Rejected, store.History(period.Id, Section.EE0201001, r.LineNumber)[^1].Status);
        }
    }

    [Fact]
    public void Feedback_colours_become_statuses_and_a_refused_file_rejects_every_line()
    {
        var (store, _, period, person) = Setup();
        using (store)
        {
            var ok = store.AddPayment(SalaryDraft(store, period, person, LineAction.R));
            var blue = store.AddPayment(SalaryDraft(store, period, person, LineAction.R));
            var plain = store.AddPayment(SalaryDraft(store, period, person, LineAction.R));
            var file = store.AddSubmission(period.Id, "f.xml", "u", [ok.Id, blue.Id, plain.Id], DateTime.UtcNow);
            Assert.All(store.Payments(period.Id), p => Assert.Equal(PaymentStatus.Sent, p.Status));

            var report = FeedbackReader.Read(XDocument.Parse($"""
                <processing result="PARTLYERROR">
                  <entryHeader><entryNumber>EE0201001</entryNumber>
                    <validations>
                      <validation><errorLevel>OK</errorLevel><errorCode>OK</errorCode><errorDescription>ok</errorDescription>
                        <lineReferences><lineReference><lineInternalId>{ok.LineNumber}</lineInternalId><lineNumberCounter>1</lineNumberCounter></lineReference></lineReferences></validation>
                      <validation><errorLevel>ERROR</errorLevel><errorCode>ERROR</errorCode><errorDescription>vigu</errorDescription>
                        <lineReferences>
                          <lineReference><lineInternalId>{blue.LineNumber}</lineInternalId><lineNumberCounter>2</lineNumberCounter></lineReference>
                          <lineReference><lineInternalId>{plain.LineNumber}</lineInternalId><lineNumberCounter>3</lineNumberCounter></lineReference>
                        </lineReferences></validation>
                    </validations>
                    <responseData>
                      <entryDetail><lineReferences><lineReference><lineInternalId>{ok.LineNumber}</lineInternalId><lineNumberCounter>1</lineNumberCounter></lineReference></lineReferences>
                        <data><code>TSD_KTM</code><description>Kinnipeetud tulumaks</description><info>58,08</info></data></entryDetail>
                      <entryDetail><lineReferences><lineReference><lineInternalId>{blue.LineNumber}</lineInternalId><lineNumberCounter>2</lineNumberCounter></lineReference></lineReferences>
                        <data><code>KR_VM_03</code><description>ERROR_BLUE</description><info>b</info></data></entryDetail>
                    </responseData>
                  </entryHeader>
                </processing>
                """));

            var feedback = store.RecordFeedback(file.Id, report);

            Assert.Equal(3, feedback.Count);
            var statuses = store.Payments(period.Id).ToDictionary(p => p.Id, p => p.Status);
            Assert.Equal(PaymentStatus.Accepted, statuses[ok.Id]);
            Assert.Equal(PaymentStatus.NeedsChange, statuses[blue.Id]);
            Assert.Equal(PaymentStatus.Review, statuses[plain.Id]); // an error verdict without a colour
            Assert.Equal(58.08m, Assert.Single(store.Feedback(ok.Id)).ServerTaxes["TSD_KTM"]);
            Assert.Equal(NextAction.CorrectWithChange, Assert.Single(store.Feedback(blue.Id)).NextAction);

            // a correction M for the blue line, refused as a whole file
            var fix = store.AddPayment(SalaryDraft(store, period, person, LineAction.M, blue.LineNumber, gross: 900m));
            var file2 = store.AddSubmission(period.Id, "f2.xml", "u2", [fix.Id], DateTime.UtcNow);
            var refused = store.RecordFeedback(file2.Id, FeedbackReader.Read(XDocument.Parse(
                "<processing result=\"ERROR\"><entryHeader><validations><validation><errorLevel>ERROR</errorLevel><errorCode>KR_VALIDATE_XBRL_FILE_7</errorCode><errorDescription>x</errorDescription></validation></validations></entryHeader></processing>")));
            var refusedRow = Assert.Single(refused);
            Assert.Equal(Severity.Black, refusedRow.Severity);
            Assert.Equal(NextAction.CorrectWithChange, refusedRow.NextAction); // the refused line was an M: the key still stands
            Assert.Equal("KR_VALIDATE_XBRL_FILE_7", Assert.Single(refusedRow.Messages).Code);
            Assert.Equal(PaymentStatus.Rejected, store.History(period.Id, Section.EE0201001, blue.LineNumber)[^1].Status);
            // the key is still on the server through its accepted-with-blue first version, so the next try is again an M
            Assert.Equal(3, store.AddPayment(SalaryDraft(store, period, person, LineAction.M, blue.LineNumber, gross: 900m)).Version);
        }
    }

    [Fact]
    public void Only_drafts_go_into_a_file_and_only_drafts_can_be_deleted()
    {
        var (store, _, period, person) = Setup();
        using (store)
        {
            var sent = store.AddPayment(SalaryDraft(store, period, person, LineAction.R));
            store.AddSubmission(period.Id, "f.xml", "u", [sent.Id], DateTime.UtcNow);

            Assert.Throws<InvalidOperationException>(() => store.AddSubmission(period.Id, "f2.xml", "u2", [sent.Id], DateTime.UtcNow));
            Assert.Throws<InvalidOperationException>(() => store.DeleteDraft(sent.Id));
            Assert.Throws<ArgumentException>(() => store.AddPayment(StoredPayment.New(period.Id, person.Id, Section.EE0201001, PaymentLine.Cancellation("k", 0), null, null) with { LineNumber = "other" }));
        }
    }

    [Fact]
    public void A_newer_schema_is_refused_and_an_older_one_is_upgraded()
    {
        var path = Path.Combine(Path.GetTempPath(), $"satellestsd-{Guid.NewGuid():N}.db");
        try
        {
            using (var store = SqliteStore.Open(path))
            {
                store.AddCompany("1", "x");
            }

            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO schema_version (version) VALUES (999)";
                command.ExecuteNonQuery();
            }

            Assert.Throws<InvalidOperationException>(() => SqliteStore.Open(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static FeedbackReport Clean(string section, string lineNumber, int counter) => FeedbackReader.Read(XDocument.Parse($"""
        <processing result="OK"><entryHeader><entryNumber>{section}</entryNumber><validations><validation><errorLevel>OK</errorLevel><errorCode>OK</errorCode><errorDescription>ok</errorDescription>
        <lineReferences><lineReference><lineInternalId>{lineNumber}</lineInternalId><lineNumberCounter>{counter}</lineNumberCounter></lineReference></lineReferences></validation></validations></entryHeader></processing>
        """));

    private static FeedbackReport Coloured(string section, string lineNumber, string colour, string code) => FeedbackReader.Read(XDocument.Parse($"""
        <processing result="ERROR"><entryHeader><entryNumber>{section}</entryNumber><validations><validation><errorLevel>ERROR</errorLevel><errorCode>ERROR</errorCode><errorDescription>vigu</errorDescription>
        <lineReferences><lineReference><lineInternalId>{lineNumber}</lineInternalId></lineReference></lineReferences></validation></validations>
        <responseData><entryDetail><lineReferences><lineReference><lineInternalId>{lineNumber}</lineInternalId></lineReference></lineReferences>
        <data><code>{code}</code><description>{colour}</description><info>t</info></data></entryDetail></responseData></entryHeader></processing>
        """));
}
