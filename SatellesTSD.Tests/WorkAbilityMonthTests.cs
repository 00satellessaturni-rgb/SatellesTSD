using SatellesTSD.Core.Model;
using SatellesTSD.Core.Review;
using SatellesTSD.Core.Store;
using SatellesTSD.Core.Tax;
using SatellesTSD.Infrastructure;

namespace SatellesTSD.Tests;

/// <summary>Töötukassa's part by the month the pay is for (decision 267), against the examples of MTA's own page on the
/// social tax ("Osalise või puuduva töövõimega töötaja eest sotsiaalmaksu deklareerimine", read 24.11.2026): the kuumäär
/// in proportion to the calendar days from the application's day to the employment's end, both counted.</summary>
public class WorkAbilityMonthTests
{
    private static readonly TaxEngine Engine = new(TaxRates.Year2026);
    private static readonly MonthAllocator Allocator = new(Engine);
    private static readonly PaymentType Salary = PaymentTypes.Get("10");
    private static readonly PayeeTaxFacts Applied = new(Residency.Resident, FundedPensionMember: false, BasicExemptionApplied: true, ReducedWorkAbility: true, StatePaysSocialTax: true);

    private static DateOnly Day(int year, int month, int day) => new(year, month, day);

    [Fact]
    public void MTAs_example_of_an_application_on_the_tenth_of_January_gives_22_days_of_31()
    {
        var month = WorkAbilityMonth.Reckon(Day(2026, 1, 1), 886m, Day(2026, 1, 10), [(Day(2026, 1, 1), null)]);

        Assert.Equal((Day(2026, 1, 1), 22, 31, 628.77m, false), (month.PayMonth, month.Days, month.DaysInMonth, month.Amount, month.ByHand));
        // the January wage of 800, declared with that part: SM (800 − 628,77) × 33 % = 56,51
        var line = Assert.Single(Allocator.Allocate([new MonthPayment("a", Salary, 800m, false, Day(2026, 2, 5))], Applied, month.Amount).Payments).Result;
        Assert.Equal((628.77m, 56.51m), (line.WorkAbilityReduction, line.SocialTax));
    }

    [Fact]
    public void MTAs_example_of_a_last_working_day_on_the_17th_of_December_gives_17_days_and_two_months_in_one()
    {
        var december = WorkAbilityMonth.Reckon(Day(2026, 12, 9), 886m, null, [(Day(2024, 2, 15), Day(2026, 12, 17))]);
        Assert.Equal((17, 31, 485.87m), (december.Days, december.DaysInMonth, december.Amount));

        // November's wage of 1200 and the final pay of 500 paid together: 886 + 485,87 typed by hand, SM (1700 − 1371,87) × 33 % = 108,28
        var both = Allocator.Allocate(
            [new MonthPayment("a", Salary, 1200m, false, Day(2026, 12, 5)), new MonthPayment("b", Salary, 500m, false, Day(2026, 12, 17))], Applied, 886m + december.Amount);
        Assert.Equal((1371.87m, 1371.87m, 0m), (both.WorkAbilityReductionOffered, both.WorkAbilityReductionTotal, both.WorkAbilityReductionLeft));
        Assert.Equal(108.28m, both.Payments.Sum(p => p.Result.SocialTax));
    }

    [Fact]
    public void MTAs_example_of_holiday_pay_beside_a_months_wage_takes_two_rates()
    {
        var month = Allocator.Allocate(
            [new MonthPayment("a", Salary, 1000m, false, Day(2026, 9, 5)), new MonthPayment("b", Salary, 1000m, false, Day(2026, 9, 5))], Applied, 2 * 886m);
        Assert.Equal((1772m, 75.24m), (month.WorkAbilityReductionTotal, month.Payments.Sum(p => p.Result.SocialTax)));
    }

    [Fact]
    public void A_whole_month_is_the_whole_rate_and_a_month_outside_the_application_or_the_employment_is_nothing()
    {
        Assert.Equal((30, 886m), (WorkAbilityMonth.Reckon(Day(2026, 9, 1), 886m, null).Days, WorkAbilityMonth.Reckon(Day(2026, 9, 1), 886m, null).Amount));
        Assert.Equal(886m, WorkAbilityMonth.Reckon(Day(2026, 9, 1), 886m, Day(2026, 9, 1), [(Day(2026, 9, 1), Day(2026, 9, 30))]).Amount);
        // February to the cent, not a rounded share of thirty days
        Assert.Equal((14, 28, 443m), (WorkAbilityMonth.Reckon(Day(2026, 2, 1), 886m, Day(2026, 2, 15)).Days, WorkAbilityMonth.Reckon(Day(2026, 2, 1), 886m, Day(2026, 2, 15)).DaysInMonth, WorkAbilityMonth.Reckon(Day(2026, 2, 1), 886m, Day(2026, 2, 15)).Amount));

        // the application filed after the month, or the employment over before it: no day, nothing
        Assert.Equal((0, 0m), (WorkAbilityMonth.Reckon(Day(2026, 9, 1), 886m, Day(2026, 10, 15)).Days, WorkAbilityMonth.Reckon(Day(2026, 9, 1), 886m, Day(2026, 10, 15)).Amount));
        Assert.Equal(0m, WorkAbilityMonth.Reckon(Day(2026, 9, 1), 886m, null, [(Day(2024, 1, 1), Day(2026, 8, 31))]).Amount);

        // never before the employment began, whatever the application's day; two contracts end to end make one month
        Assert.Equal(11, WorkAbilityMonth.Reckon(Day(2026, 9, 1), 886m, Day(2026, 9, 3), [(Day(2026, 9, 20), null)]).Days);
        Assert.Equal(30, WorkAbilityMonth.Reckon(Day(2026, 9, 1), 886m, null, [(Day(2024, 1, 1), Day(2026, 9, 15)), (Day(2026, 9, 16), null)]).Days);
    }

    [Fact]
    public void The_pay_month_follows_the_companys_setting_and_Decembers_pay_takes_its_own_years_rate()
    {
        Assert.Equal(Day(2026, 9, 1), WorkAbilityMonth.PayMonthOf(2026, 10, wagesPaidFollowingMonth: true));
        Assert.Equal(Day(2026, 10, 1), WorkAbilityMonth.PayMonthOf(2026, 10, wagesPaidFollowingMonth: false));
        Assert.Equal(Day(2025, 12, 1), WorkAbilityMonth.PayMonthOf(2026, 1, wagesPaidFollowingMonth: true));
        Assert.Equal((820m, 886m, null), (WorkAbilityMonth.RateOf(2025), WorkAbilityMonth.RateOf(2026), WorkAbilityMonth.RateOf(2024)));

        // MTA's example of the year's turn: December's pay declared in January carries December's rate
        var person = Person.New(Guid.NewGuid(), "Mari", "Maasikas", ReviewFixtures.MariCode) with { ReducedWorkAbility = true, StatePaysSocialTax = true };
        var january = new Period(Guid.NewGuid(), person.CompanyId, 2026, 1, PeriodState.Draft, null, null, null);
        Assert.Equal((Day(2025, 12, 1), 820m), (Core.Declare.PeriodDeclarer.WorkAbilityMonthOf(january, person, [], true, TaxRates.Year2026).PayMonth, Core.Declare.PeriodDeclarer.WorkAbilityMonthOf(january, person, [], true, TaxRates.Year2026).Amount));
        Assert.Equal(886m, Core.Declare.PeriodDeclarer.WorkAbilityMonthOf(january, person, [], false, TaxRates.Year2026).Amount);
    }

    [Fact]
    public void The_allocator_takes_a_part_between_nothing_and_two_rates_and_ignores_it_without_both_ticks()
    {
        MonthPayment[] pay = [new("a", Salary, 1000m, false, Day(2026, 10, 5))];
        Assert.Equal(886m, Allocator.Allocate(pay, Applied).WorkAbilityReductionOffered); // none given: one whole rate, as the Calculator reckons
        Assert.Equal((0m, 330m), (Allocator.Allocate(pay, Applied, 0m).WorkAbilityReductionOffered, Allocator.Allocate(pay, Applied, 0m).Payments[0].Result.SocialTax));
        Assert.Equal(0m, Allocator.Allocate(pay, Applied with { StatePaysSocialTax = false }, 500m).WorkAbilityReductionOffered);
        Assert.Throws<ArgumentOutOfRangeException>(() => Allocator.Allocate(pay, Applied, 1772.01m));
        Assert.Throws<ArgumentOutOfRangeException>(() => Allocator.Allocate(pay, Applied, -0.01m));
    }

    [Fact]
    public void The_store_keeps_the_applications_day_the_companys_pay_month_and_the_amount_typed_for_a_month()
    {
        using var store = SqliteStore.InMemory();
        var company = store.AddCompany("10235688", "Näidis OÜ");
        Assert.True(company.WagesPaidFollowingMonth); // the usual way, and every company of an earlier store
        Assert.True(Assert.Single(store.Companies()).WagesPaidFollowingMonth);
        store.UpdateCompany(company with { WagesPaidFollowingMonth = false });
        Assert.False(Assert.Single(store.Companies()).WagesPaidFollowingMonth);
        Assert.False(store.FindCompany("10235688")!.WagesPaidFollowingMonth);

        var person = store.AddPerson(Person.New(company.Id, "Mari", "Maasikas", ReviewFixtures.MariCode) with { ReducedWorkAbility = true, StatePaysSocialTax = true, WorkAbilityApplicationDate = Day(2026, 1, 10) });
        Assert.Equal(Day(2026, 1, 10), Assert.Single(store.Persons(company.Id)).WorkAbilityApplicationDate);
        store.UpdatePerson(person with { WorkAbilityApplicationDate = null });
        Assert.Null(Assert.Single(store.Persons(company.Id)).WorkAbilityApplicationDate);

        var october = store.GetOrCreatePeriod(company.Id, 2026, 10);
        var november = store.GetOrCreatePeriod(company.Id, 2026, 11);
        Assert.Empty(store.WorkAbilityAmounts(october.Id));
        store.SetWorkAbilityAmount(october.Id, person.Id, 1371.87m);
        store.SetWorkAbilityAmount(october.Id, person.Id, 1772m); // typed again: the one amount of the month
        Assert.Equal(1772m, Assert.Single(store.WorkAbilityAmounts(october.Id)).Value);
        Assert.Empty(store.WorkAbilityAmounts(november.Id)); // one declared month alone
        store.SetWorkAbilityAmount(october.Id, person.Id, null);
        Assert.Empty(store.WorkAbilityAmounts(october.Id));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.SetWorkAbilityAmount(october.Id, person.Id, -1m));
    }

    [Fact]
    public void A_v15_store_upgrades_to_v16_with_no_day_the_following_month_and_no_amount()
    {
        var path = Path.Combine(Path.GetTempPath(), $"satellestsd-{Guid.NewGuid():N}.db");
        try
        {
            Guid companyId;
            using (var v15 = SqliteStore.OpenAtVersion(path, 15))
            {
                var company = v15.AddCompany("10235688", "Näidis OÜ");
                companyId = company.Id;
                Assert.True(Assert.Single(v15.Companies()).WagesPaidFollowingMonth); // read as the usual way where the column is not there yet
                v15.UpdateCompany(company with { Name = "Näidis AS" }); // and saved without it
                v15.ExecuteRaw($"INSERT INTO person (id, company_id, first_name, last_name, residency, on_pension, exemption_applied, minimum_applies, a1_case, residency_source, pension_source, declaration, card_type, work_ability, work_ability_application) VALUES ('{Guid.NewGuid():N}', '{company.Id:N}', 'Mari', 'Maasikas', 'Resident', 0, 1, 1, 'None', 'Emta', 'Unknown', 'Auto', 0, 1, 1)");
                Assert.Equal(0L, v15.ScalarRaw<long>("SELECT COUNT(*) FROM pragma_table_info('company') WHERE name = 'wages_following_month'"));
            }

            using var v16 = SqliteStore.Open(path);
            Assert.Equal(16L, v16.ScalarRaw<long>("SELECT MAX(version) FROM schema_version"));
            Assert.Equal(("Näidis AS", true), (Assert.Single(v16.Companies()).Name, Assert.Single(v16.Companies()).WagesPaidFollowingMonth));
            var person = Assert.Single(v16.Persons(companyId));
            Assert.Equal((true, true, null), (person.ReducedWorkAbility, person.StatePaysSocialTax, person.WorkAbilityApplicationDate));
            Assert.Empty(v16.WorkAbilityAmounts(v16.GetOrCreatePeriod(companyId, 2026, 10).Id));
        }
        finally
        {
            File.Delete(path);
            File.Delete(SqliteStore.BackupPathFor(path));
        }
    }

    [Fact]
    public void Saving_the_card_keeps_the_applications_day_only_with_the_application()
    {
        using var store = SqliteStore.InMemory();
        var company = store.AddCompany("10235688", "Näidis OÜ");
        var person = store.AddPerson(Person.New(company.Id, "Mari", "Maasikas", ReviewFixtures.MariCode));
        var workspace = new ReviewWorkspace(store, TaxRates.Year2026);

        var applied = workspace.SavePerson(person with { ReducedWorkAbility = true, StatePaysSocialTax = true, WorkAbilityApplicationDate = Day(2026, 9, 10) }, presented: person);
        Assert.Equal(Day(2026, 9, 10), applied.WorkAbilityApplicationDate);
        var without = workspace.SavePerson(applied with { StatePaysSocialTax = false }, presented: applied);
        Assert.Null(without.WorkAbilityApplicationDate);
        Assert.Null(Assert.Single(store.Persons(company.Id)).WorkAbilityApplicationDate);
    }
}
