using System.Xml.Linq;
using SatellesTSD.Core.Declare;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Review;
using SatellesTSD.Core.Store;
using SatellesTSD.Core.Tax;
using SatellesTSD.Core.Xbrl;
using SatellesTSD.Infrastructure;

namespace SatellesTSD.Tests;

/// <summary>A person with partial or no work ability (SMS § 2 lg 4 p 6: no social-tax minimum, by the status alone) and the
/// employer's application by which Töötukassa pays the social tax on up to one kuumäär of the month's wages (§ 6 lg 1 p 5,
/// lg 3): TSD_OPTV_SM_VAHENDUS on the types of matrix column O, spread in date order, SM per KR_SM_04.</summary>
public class WorkAbilityTests
{
    private static readonly string SchemaRoot = Path.Combine(AppContext.BaseDirectory, "schemas", "apa-tsd");
    private static readonly TaxEngine Engine = new(TaxRates.Year2026);
    private static readonly MonthAllocator Allocator = new(Engine);
    private static readonly PaymentType Salary = PaymentTypes.Get("10");
    private static readonly PaymentType BoardFee = PaymentTypes.Get("21");

    /// <summary>A resident outside the II pillar (so the arithmetic is the social tax's alone), the exemption applied.</summary>
    private static readonly PayeeTaxFacts Plain = new(Residency.Resident, FundedPensionMember: false, BasicExemptionApplied: true);

    private static readonly PayeeTaxFacts Status = Plain with { ReducedWorkAbility = true };

    private static readonly PayeeTaxFacts Applied = Status with { StatePaysSocialTax = true };

    private static MonthPayment Pay(string key, PaymentType type, decimal amount, int day, bool net = false) =>
        new(key, type, amount, net, new DateOnly(2026, 10, day));

    [Fact]
    public void Column_O_is_JAH_on_the_wage_types_alone()
    {
        Assert.Equal(["10", "33", "37", "120", "138", "167"], PaymentTypes.All.Where(t => t.WorkAbilityReductionAllowed).Select(t => t.Code));
        // the Estonian-A1 rows the mapping shows (matrix rows 4 and 29); the others' column O is not known, so none
        Assert.Equal(["10", "120"], PaymentTypes.All.Where(t => t.WorkAbilityReductionWithEstonianA1).Select(t => t.Code));
        Assert.All(PaymentTypes.All.Where(t => t.WorkAbilityReductionAllowed), t => Assert.True(t.SocialTax && t.SocialTaxMinimum)); // pay of § 2 lg 1 p 1
        Assert.False(Salary.WorkAbilityReductionOn(A1Case.ForeignA1WorkInEstonia));
    }

    [Fact]
    public void A_payment_below_the_kuumaar_is_deducted_whole_and_carries_no_social_tax()
    {
        var month = Allocator.Allocate([Pay("a", Salary, 600m, 10)], Applied);

        var only = Assert.Single(month.Payments);
        Assert.True(only.Result.WorkAbilityReductionApplies);
        Assert.Equal((600m, 600m, 0m), (only.Result.SocialTaxBase, only.Result.WorkAbilityReduction, only.Result.SocialTax));
        Assert.Equal((886m, 286m, 600m), (month.WorkAbilityReductionOffered, month.WorkAbilityReductionLeft, month.WorkAbilityReductionTotal));
        Assert.Equal(0m, only.Result.SocialTaxComputedBase);
        // the other taxes are the status's own: the employee's and the employer's insurance, the income tax and the exemption stand
        var without = Assert.Single(Allocator.Allocate([Pay("a", Salary, 600m, 10)], Plain with { SocialTaxMinimumApplies = false }).Payments).Result;
        Assert.Equal(without with { WorkAbilityReductionApplies = true, WorkAbilityReduction = 600m, SocialTax = 0m }, only.Result);
    }

    [Fact]
    public void A_payment_above_the_kuumaar_is_deducted_one_kuumaar_and_taxed_on_the_rest()
    {
        var only = Assert.Single(Allocator.Allocate([Pay("a", Salary, 2000m, 10)], Applied).Payments).Result;

        Assert.Equal(886m, only.WorkAbilityReduction);
        Assert.Equal(367.62m, only.SocialTax); // (2000 − 886) × 0.33 (KR_SM_04)
        Assert.Equal(2000m, only.SocialTaxBase); // TSD_SMVM stays the amount (KR_SM_01_2)
    }

    [Fact]
    public void A_net_amount_is_grossed_up_as_before_and_the_deduction_taken_off_the_gross()
    {
        var net = Assert.Single(Allocator.Allocate([Pay("a", Salary, 1000m, 10, net: true)], Applied).Payments).Result;
        var plain = Assert.Single(Allocator.Allocate([Pay("a", Salary, 1000m, 10, net: true)], Plain with { SocialTaxMinimumApplies = false }).Payments).Result;

        Assert.Equal(plain.Gross, net.Gross); // the social tax is the employer's: the gross-up does not change
        Assert.Equal(886m, net.WorkAbilityReduction);
        Assert.Equal(Math.Round((net.Gross - 886m) * 0.33m, 2, MidpointRounding.AwayFromZero), net.SocialTax);
    }

    [Fact]
    public void Several_payments_share_one_kuumaar_in_date_order_each_capped_at_its_own_base()
    {
        var month = Allocator.Allocate([Pay("late", Salary, 600m, 20), Pay("early", Salary, 500m, 5), Pay("last", Salary, 300m, 28)], Applied);

        Assert.Equal(["early", "late", "last"], month.Payments.Select(p => p.Payment.Key));
        Assert.Equal([500m, 386m, 0m], month.Payments.Select(p => p.Result.WorkAbilityReduction));
        Assert.Equal([886m, 386m, 0m], month.Payments.Select(p => p.WorkAbilityReductionCap));
        Assert.Equal([0m, 70.62m, 99m], month.Payments.Select(p => p.Result.SocialTax)); // (600 − 386) × 0.33; 300 × 0.33
        Assert.Equal((886m, 0m), (month.WorkAbilityReductionTotal, month.WorkAbilityReductionLeft));
    }

    [Fact]
    public void Two_payments_of_one_day_go_in_the_order_given()
    {
        var month = Allocator.Allocate([Pay("first", Salary, 800m, 10), Pay("second", Salary, 800m, 10)], Applied);

        Assert.Equal([("first", 800m), ("second", 86m)], month.Payments.Select(p => (p.Payment.Key, p.Result.WorkAbilityReduction)));
    }

    [Fact]
    public void A_type_outside_column_O_takes_none_and_leaves_the_kuumaar_to_the_next()
    {
        var month = Allocator.Allocate([Pay("fee", BoardFee, 500m, 3), Pay("pay", Salary, 500m, 10)], Applied);

        var fee = month.Payments[0].Result;
        Assert.False(fee.WorkAbilityReductionApplies);
        Assert.Equal((0m, 165m), (fee.WorkAbilityReduction, fee.SocialTax)); // a board fee's social tax is the employer's in full (KR_SM_05)
        Assert.Equal((500m, 0m), (month.Payments[1].Result.WorkAbilityReduction, month.Payments[1].Result.SocialTax));
        Assert.Equal(386m, month.WorkAbilityReductionLeft);
        Assert.Equal(0m, Engine.Calculate(new TaxInput(BoardFee, 500m, false, 0m, WorkAbilityReductionCap: 886m), Applied).WorkAbilityReduction);
    }

    [Fact]
    public void The_deduction_never_leaves_less_than_nothing_of_the_base_KR_SM_06_and_10()
    {
        // the app declares no TSD_KUUMVAHENDUS: OPTV + KUUMVAHENDUS ≤ TSD_SMVM is OPTV ≤ TSD_SMVM, on every line
        var month = Allocator.Allocate([Pay("a", Salary, 120.5m, 2), Pay("b", Salary, 0.01m, 3), Pay("c", Salary, 2000m, 4)], Applied);
        foreach (var allocated in month.Payments)
        {
            var line = DeclarationLineFactory.Create(allocated.Payment.Key, 1, LineAction.R, Salary, Applied, PayeeId.Estonian(ReviewFixtures.MariCode), allocated.Payment.Date, allocated.Result);
            decimal Amount(string description) => line.Amounts.Where(a => a.Description == description).Sum(a => a.Quantity);
            Assert.DoesNotContain(line.Amounts, a => a.Description == "TSD_KUUMVAHENDUS");
            Assert.True(Amount(MeasurableAmount.WorkAbilityReduction) + Amount("TSD_KUUMVAHENDUS") <= Amount(MeasurableAmount.SocialTaxBase), allocated.Payment.Key);
            Assert.True(allocated.Result.SocialTax >= 0m);
        }

        Assert.Equal([120.5m, 0.01m, 765.49m], month.Payments.Select(p => p.Result.WorkAbilityReduction));
        Assert.True(month.WorkAbilityReductionTotal <= TaxRates.Year2026.SocialTaxMinimumBase); // one kuumäär, not KR_SM_07's two
    }

    [Fact]
    public void The_engine_refuses_a_cap_above_one_kuumaar_or_below_nothing()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Engine.Calculate(new TaxInput(Salary, 2000m, false, 0m, WorkAbilityReductionCap: 886.01m), Applied));
        Assert.Throws<ArgumentOutOfRangeException>(() => Engine.Calculate(new TaxInput(Salary, 2000m, false, 0m, WorkAbilityReductionCap: -1m), Applied));
    }

    [Fact]
    public void The_status_alone_takes_the_minimum_off_and_deducts_nothing()
    {
        // the card's minimum left ticked beside the status: the engine holds the law all the same (SMS § 2 lg 4 p 6)
        var facts = Status with { SocialTaxMinimumApplies = true };
        var month = Allocator.Allocate([Pay("a", Salary, 300m, 10)], facts);

        var only = Assert.Single(month.Payments).Result;
        Assert.False(only.SocialTaxMinimumApplies);
        Assert.False(only.WorkAbilityReductionApplies);
        Assert.Equal((0m, 0m, 0m, 99m), (only.SocialTaxMinimumIncrease, only.WorkAbilityReduction, month.MinimumDue, only.SocialTax));
        Assert.Null(month.TopUpKey);
        Assert.Equal(0m, month.WorkAbilityReductionOffered);
        Assert.False(EffectiveFlags.Of(Salary, facts).SocialTaxMinimum);
        Assert.True(EffectiveFlags.Of(Salary, Plain).SocialTaxMinimum);

        // a zero line gets no top-up, and the engine refuses one asked for
        var zero = Allocator.Allocate([MonthPayment.Zero("z", Salary, new DateOnly(2026, 10, 31))], facts);
        Assert.Equal((0m, 0m), (zero.MinimumShortfall, Assert.Single(zero.Payments).Result.SocialTax));
        Assert.Throws<InvalidOperationException>(() => Engine.Calculate(new TaxInput(Salary, 300m, false, 0m, SocialTaxMinimumIncrease: 586m), facts));
    }

    [Fact]
    public void The_application_without_the_status_is_refused_by_its_code()
    {
        var facts = Plain with { StatePaysSocialTax = true };

        Assert.Equal(PaymentChecks.WorkAbilityApplicationWithoutStatus, TaxRefusal.CodeOf(Assert.Throws<InvalidOperationException>(() => Allocator.Allocate([Pay("a", Salary, 300m, 10)], facts))));
        Assert.Equal(PaymentChecks.WorkAbilityApplicationWithoutStatus, TaxRefusal.CodeOf(Assert.Throws<InvalidOperationException>(() => Engine.Calculate(new TaxInput(Salary, 300m, false, 0m), facts))));
        Assert.Equal([PaymentChecks.WorkAbilityApplicationWithoutStatus], PaymentChecks.Check(Salary, facts, 300m, TaxRates.Year2026).Select(r => r.Code));
        Assert.Empty(PaymentChecks.Check(Salary, Applied, 300m, TaxRates.Year2026));
        Assert.Contains(PaymentChecks.WorkAbilityApplicationWithoutStatus, TaxRefusal.Codes);
        // the allocator's own guard: a month with no payment never reaches the engine, and is refused all the same
        Assert.Equal(PaymentChecks.WorkAbilityApplicationWithoutStatus, TaxRefusal.CodeOf(Assert.Throws<InvalidOperationException>(() => Allocator.Allocate([], facts))));
    }

    [Fact]
    public void A_payments_own_facts_may_not_differ_from_the_months_in_the_application()
    {
        var own = new MonthPayment("a", Salary, 300m, false, new DateOnly(2026, 10, 10), Status);
        Assert.EndsWith("on StatePaysSocialTax, which define the month's pools; only residency, A1 and treaty facts may differ per payment.", Assert.Throws<InvalidOperationException>(() => Allocator.Allocate([own], Applied)).Message);
    }

    [Fact]
    public void A_payments_own_facts_may_not_differ_from_the_months_in_the_status()
    {
        var own = new MonthPayment("a", Salary, 300m, false, new DateOnly(2026, 10, 10), Plain);
        Assert.EndsWith("on ReducedWorkAbility, which define the month's pools; only residency, A1 and treaty facts may differ per payment.", Assert.Throws<InvalidOperationException>(() => Allocator.Allocate([own], Status)).Message);
    }

    [Fact]
    public void An_Estonian_A1_keeps_the_deduction_on_10_and_a_foreign_A1_has_none()
    {
        var estonian = Applied with { A1 = A1Case.EstonianA1WorkAbroad, A1Country = "EE", WorkCountry = "FI" };
        Assert.Equal(886m, Engine.Calculate(new TaxInput(Salary, 1000m, false, 0m, WorkAbilityReductionCap: 886m), estonian).WorkAbilityReduction);
        Assert.Equal(0m, Engine.Calculate(new TaxInput(PaymentTypes.Get("33"), 1000m, false, 0m, WorkAbilityReductionCap: 886m), estonian).WorkAbilityReduction);

        var foreign = Applied with { A1 = A1Case.ForeignA1WorkInEstonia, A1Country = "FI", WorkCountry = "EE" };
        var none = Engine.Calculate(new TaxInput(Salary, 1000m, false, 0m, WorkAbilityReductionCap: 886m), foreign);
        Assert.Equal((false, 0m, 0m), (none.WorkAbilityReductionApplies, none.WorkAbilityReduction, none.SocialTax));
    }

    [Fact]
    public void The_line_carries_TSD_OPTV_SM_VAHENDUS_after_TSD_SMVM_and_none_without_a_deduction()
    {
        var result = Assert.Single(Allocator.Allocate([Pay("a", Salary, 2000m, 10)], Applied).Payments).Result;
        var line = DeclarationLineFactory.Create("0001", 1, LineAction.R, Salary, Applied, PayeeId.Estonian(ReviewFixtures.MariCode), new DateOnly(2026, 10, 10), result);

        Assert.Equal([MeasurableAmount.Mvt610, MeasurableAmount.SocialTaxBase, MeasurableAmount.WorkAbilityReduction], line.Amounts.Select(a => a.Description));
        Assert.Equal(886m, line.Amounts.Single(a => a.Description == "TSD_OPTV_SM_VAHENDUS").Quantity);
        Assert.Equal(367.62m, line.Taxes.Single(t => t.Code == TaxLine.SocialTax).Amount);

        var plain = Assert.Single(Allocator.Allocate([Pay("a", Salary, 2000m, 10)], Status).Payments).Result;
        var without = DeclarationLineFactory.Create("0001", 1, LineAction.R, Salary, Status, PayeeId.Estonian(ReviewFixtures.MariCode), new DateOnly(2026, 10, 10), plain);
        Assert.DoesNotContain(without.Amounts, a => a.Description == MeasurableAmount.WorkAbilityReduction);
    }

    [Fact]
    public void The_file_writes_the_deduction_reads_it_back_and_passes_the_schemas()
    {
        var result = Assert.Single(Allocator.Allocate([Pay("a", Salary, 600m, 10)], Applied).Payments).Result;
        var line = DeclarationLineFactory.Create("0001", 1, LineAction.R, Salary, Applied, PayeeId.Estonian(ReviewFixtures.MariCode), new DateOnly(2026, 10, 10), result);
        var declaration = new Declaration("10235688", 2026, 10, new DateOnly(2026, 11, 3), Declaration.MakeUniqueId("10235688", new DateTime(2026, 11, 3, 9, 0, 0)),
            "Saturni Raamatupidamine OÜ", "SatellesTSD", null, [new SectionEntries(Section.EE0201001, null, [line])]);

        var written = XbrlGlWriter.Write(declaration);
        var xml = written.ToString(SaveOptions.None);
        Assert.Contains("TSD_OPTV_SM_VAHENDUS", xml, StringComparison.Ordinal);
        Assert.Empty(DeclarationValidator.Validate(declaration));
        Assert.Empty(new XbrlGlValidator(SchemaRoot).Validate(written));

        var back = Assert.Single(Assert.Single(XbrlGlReader.Read(written).Sections).Lines);
        Assert.Equal(600m, back.Amounts.Single(a => a.Description == MeasurableAmount.WorkAbilityReduction).Quantity);
        Assert.Equal(0m, back.Taxes.Single(t => t.Code == TaxLine.SocialTax).Amount);
    }

    [Fact]
    public void The_persons_two_facts_are_kept_and_go_into_the_months_facts()
    {
        using var store = SqliteStore.InMemory();
        var company = store.AddCompany("10235688", "Näidis OÜ");
        var person = store.AddPerson(Person.New(company.Id, "Mari", "Maasikas", ReviewFixtures.MariCode) with { ReducedWorkAbility = true, StatePaysSocialTax = true });

        var read = Assert.Single(store.Persons(company.Id));
        Assert.Equal(person, read);
        Assert.Equal((true, true), (read.ReducedWorkAbility, read.StatePaysSocialTax));
        store.UpdatePerson(read with { StatePaysSocialTax = false });
        Assert.Equal((true, false), (Assert.Single(store.Persons(company.Id)).ReducedWorkAbility, Assert.Single(store.Persons(company.Id)).StatePaysSocialTax));

        var facts = read.FactsFor(2026, 10);
        Assert.Equal((true, true, false), (facts.ReducedWorkAbility, facts.StatePaysSocialTax, facts.SocialTaxMinimumApplies)); // the card's minimum tick is on: the status takes it off
        Assert.True(read.SocialTaxMinimumApplies);
        Assert.False(read.MinimumOwed);
    }

    [Fact]
    public void Saving_the_card_drops_the_application_without_the_status()
    {
        using var store = SqliteStore.InMemory();
        var company = store.AddCompany("10235688", "Näidis OÜ");
        var person = store.AddPerson(Person.New(company.Id, "Mari", "Maasikas", ReviewFixtures.MariCode));
        var workspace = new ReviewWorkspace(store, TaxRates.Year2026);

        var both = workspace.SavePerson(person with { ReducedWorkAbility = true, StatePaysSocialTax = true }, presented: person);
        Assert.Equal((true, true), (both.ReducedWorkAbility, both.StatePaysSocialTax));
        var off = workspace.SavePerson(both with { ReducedWorkAbility = false }, presented: both);
        Assert.Equal((false, false), (off.ReducedWorkAbility, off.StatePaysSocialTax));
        Assert.Equal((false, false), (Assert.Single(store.Persons(company.Id)).ReducedWorkAbility, Assert.Single(store.Persons(company.Id)).StatePaysSocialTax));
    }

    [Fact]
    public void A_v14_store_upgrades_to_v15_with_neither_fact_on_its_persons()
    {
        var path = Path.Combine(Path.GetTempPath(), $"satellestsd-{Guid.NewGuid():N}.db");
        try
        {
            Guid companyId;
            using (var v14 = SqliteStore.OpenAtVersion(path, 14))
            {
                var company = v14.AddCompany("10235688", "Näidis OÜ");
                companyId = company.Id;
                v14.ExecuteRaw($"INSERT INTO person (id, company_id, first_name, last_name, residency, on_pension, exemption_applied, minimum_applies, a1_case, residency_source, pension_source, declaration, card_type) VALUES ('{Guid.NewGuid():N}', '{company.Id:N}', 'Mari', 'Maasikas', 'Resident', 0, 1, 1, 'None', 'Emta', 'Unknown', 'Auto', 0)");
                Assert.Equal(0L, v14.ScalarRaw<long>("SELECT COUNT(*) FROM pragma_table_info('person') WHERE name = 'work_ability'"));
            }

            using var v15 = SqliteStore.Open(path);
            Assert.Equal(15L, v15.ScalarRaw<long>("SELECT MAX(version) FROM schema_version"));
            var person = Assert.Single(v15.Persons(companyId));
            Assert.Equal((false, false, true), (person.ReducedWorkAbility, person.StatePaysSocialTax, person.MinimumOwed));
            v15.UpdatePerson(person with { ReducedWorkAbility = true, StatePaysSocialTax = true });
            Assert.Equal((true, true), (Assert.Single(v15.Persons(companyId)).ReducedWorkAbility, Assert.Single(v15.Persons(companyId)).StatePaysSocialTax));
        }
        finally
        {
            File.Delete(path);
            File.Delete(SqliteStore.BackupPathFor(path));
        }
    }

    [Fact]
    public void A_stored_result_of_a_build_before_the_deduction_reads_as_none()
    {
        var result = Engine.Calculate(new TaxInput(Salary, 1000m, false, 700m), Plain);
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        var old = json.Replace(",\"WorkAbilityReductionApplies\":false", "", StringComparison.Ordinal).Replace(",\"WorkAbilityReduction\":0", "", StringComparison.Ordinal);
        Assert.DoesNotContain("WorkAbilityReduction\"", old, StringComparison.Ordinal);

        Assert.Equal(result, System.Text.Json.JsonSerializer.Deserialize<TaxResult>(old));
    }

    private static SimScenario Mari(decimal salary, bool minimum = true, bool status = true, bool application = true, params SimRow[] rows) => new(
        2026, 10,
        new SimPerson("Mari", "Maasikas", ReviewFixtures.MariCode, FundedPensionMember: false, PensionRate: null, SocialTaxMinimumApplies: minimum,
            ReducedWorkAbility: status, StatePaysSocialTax: application),
        SimRegister.Listed,
        [new SimEmployment("1", new DateOnly(2024, 2, 15))],
        rows.Length > 0 ? rows : salary > 0m ? [new SimRow(new DateOnly(2026, 10, 5), "Mari Maasikas", salary, "palk")] : []);

    [Fact]
    public void The_month_built_declares_the_deduction_and_the_file_passes_the_schemas()
    {
        var result = Simulator.Run(Mari(500m), SchemaRoot);

        Assert.Null(result.Refusal);
        var line = Assert.Single(result.Lines);
        Assert.Equal(508.13m, line.Line.Amount); // the bank paid out 500 net: the gross is the base
        Assert.Equal(508.13m, line.Line.Amounts.Single(a => a.Description == MeasurableAmount.WorkAbilityReduction).Quantity);
        Assert.Equal(0m, line.Result!.SocialTax);
        Assert.Equal(0m, line.Result.SocialTaxMinimumIncrease); // the card's minimum ticked, the status takes it off
        Assert.Equal((886m, 508.13m, 0m), (result.Build!.WorkAbilityReductionOffered, result.Build.WorkAbilityReduction, result.Build.MinimumShortfall));
        Assert.Null(result.XmlRefusal);
        Assert.Contains("TSD_OPTV_SM_VAHENDUS", result.Xml, StringComparison.Ordinal);
        Assert.Equal((true, true), (SimScenario.Parse(Mari(500m).ToJson()).Person.ReducedWorkAbility, SimScenario.Parse(Mari(500m).ToJson()).Person.StatePaysSocialTax));
    }

    [Fact]
    public void A_month_without_pay_builds_no_zero_line_for_a_person_with_the_status()
    {
        var with = Simulator.Run(Mari(0m, application: false));
        Assert.Empty(with.Lines);
        Assert.DoesNotContain(with.Build?.Problems ?? [], p => p.StartsWith(PeriodDeclarer.ZeroLine, StringComparison.Ordinal));

        var without = Simulator.Run(Mari(0m, status: false, application: false));
        Assert.Contains(without.Build!.Problems, p => p.StartsWith(PeriodDeclarer.ZeroLine, StringComparison.Ordinal)); // the same card without the status: the minimum's zero line
    }

    [Fact]
    public void Pay_of_a_type_outside_column_O_is_noted_where_the_application_leaves_a_kuumaar()
    {
        var fee = Mari(0m, rows: new SimRow(new DateOnly(2026, 10, 5), "Mari Maasikas", 500m, "palk", TypeCode: "17"));
        var noted = Simulator.Run(fee);
        var note = Assert.Single(noted.Build!.Notes, n => n.Code == PeriodDeclarer.WorkAbilityNotDeducted);
        Assert.Equal(["17"], note.Items);
        Assert.Equal(0m, Assert.Single(noted.Lines).Result!.WorkAbilityReduction);

        Assert.DoesNotContain(Simulator.Run(Mari(500m)).Build!.Notes, n => n.Code == PeriodDeclarer.WorkAbilityNotDeducted);
        Assert.DoesNotContain(Simulator.Run(fee with { Person = fee.Person with { StatePaysSocialTax = false } }).Build!.Notes, n => n.Code == PeriodDeclarer.WorkAbilityNotDeducted);
    }

    [Fact]
    public void Pay_of_33_under_an_Estonian_A1_is_noted_as_the_A1_rows_not_as_a_type_without_column_O()
    {
        // 33 allows the deduction, its Estonian-A1 row is not known to (decision 263): the note names the A1, not the type
        var fee = Mari(0m, rows: new SimRow(new DateOnly(2026, 10, 5), "Mari Maasikas", 500m, "hüvitis", TypeCode: "33"));
        var abroad = fee with { Person = fee.Person with { A1 = A1Case.EstonianA1WorkAbroad, A1Country = "EE", WorkCountry = "FI" } };
        var noted = Simulator.Run(abroad);
        Assert.Null(noted.Refusal);
        var note = Assert.Single(noted.Build!.Notes, n => n.Code == PeriodDeclarer.WorkAbilityNotDeducted);
        Assert.Equal("a1", note.Variant);
        Assert.Equal(["33"], note.Items);
        Assert.Contains("under the person's Estonian A1", note.Text, StringComparison.Ordinal);
        Assert.Equal(0m, Assert.Single(noted.Lines).Result!.WorkAbilityReduction);

        // a board fee beside it under the same A1: each type in its own note
        var both = abroad with { Rows = [.. abroad.Rows, new SimRow(new DateOnly(2026, 10, 6), "Mari Maasikas", 300m, "tasu", TypeCode: "17")] };
        var notes = Simulator.Run(both).Build!.Notes.Where(n => n.Code == PeriodDeclarer.WorkAbilityNotDeducted).ToList();
        Assert.Equal(new (string?, string)[] { (null, "17"), ("a1", "33") }, notes.Select(n => (n.Variant, Assert.Single(n.Items))).OrderBy(n => n.Item2, StringComparer.Ordinal));

        // without the A1, 33 takes the deduction and nothing is said
        Assert.DoesNotContain(Simulator.Run(fee).Build!.Notes, n => n.Code == PeriodDeclarer.WorkAbilityNotDeducted);
    }

    [Fact]
    public void A_simulation_file_saved_before_the_two_ticks_opens_with_neither()
    {
        var json = Mari(500m, status: false, application: false).ToJson();
        var old = json.Replace(",\n      \"ReducedWorkAbility\": false", "", StringComparison.Ordinal).Replace(",\n      \"StatePaysSocialTax\": false", "", StringComparison.Ordinal)
            .Replace(",\r\n      \"ReducedWorkAbility\": false", "", StringComparison.Ordinal).Replace(",\r\n      \"StatePaysSocialTax\": false", "", StringComparison.Ordinal);
        Assert.DoesNotContain("ReducedWorkAbility", old, StringComparison.Ordinal);

        var person = SimScenario.Parse(old).Person;
        Assert.Equal((false, false), (person.ReducedWorkAbility, person.StatePaysSocialTax));
    }
}
