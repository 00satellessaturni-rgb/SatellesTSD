using SatellesTSD.Core.Declare;
using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Review;
using SatellesTSD.Core.Tax;
using SatellesTSD.Infrastructure;

namespace SatellesTSD.Tests;

/// <summary>The simulation (decision 204): one example person run through the app's own import, search, review and build
/// on a store of its own.</summary>
public class SimulatorTests
{
    private static readonly string SchemaRoot = Path.Combine(AppContext.BaseDirectory, "schemas", "apa-tsd");

    private static SimScenario Mari(SimRegister register = SimRegister.Listed, decimal salary = 905.92m, bool minimum = false, string typeId = "1") => new(
        2026, 10,
        new SimPerson("Mari", "Maasikas", ReviewFixtures.MariCode, FundedPensionMember: false, PensionRate: null, SocialTaxMinimumApplies: minimum),
        register,
        [new SimEmployment(typeId, new DateOnly(2024, 2, 15))],
        [new SimRow(new DateOnly(2026, 10, 5), "Mari Maasikas", salary, "palk")]);

    [Fact]
    public void A_salary_paid_under_a_Toeoeleping_and_a_VOES_leping_at_once_follows_the_two_contracts_rule()
    {
        // the fifth audit's S11: the old rule typed the Tööleping salary 17 by the VÕS leping begun later, and added the
        // minimum's zero line for the Tööleping (decision 209)
        SimScenario Scenario(TwoContractsRule rule) => Mari(salary: 1200m, minimum: true) with
        {
            Employments = [new SimEmployment("1", new DateOnly(2023, 1, 1)), new SimEmployment("600", new DateOnly(2026, 9, 1))],
            Rows = [new SimRow(new DateOnly(2026, 10, 25), "Mari Maasikas", 1200m, "oktoobri palk")],
            TwoContracts = rule,
        };

        var byContract = Simulator.Run(Scenario(TwoContractsRule.SalaryToEmploymentContract), SchemaRoot);
        Assert.Equal("10", Assert.Single(byContract.Lines).Line.TypeCode);
        Assert.DoesNotContain(byContract.Build!.Problems, p => p.StartsWith(PeriodDeclarer.ZeroLine, StringComparison.Ordinal));
        Assert.NotNull(byContract.Xml);

        var asked = Simulator.Run(Scenario(TwoContractsRule.Ask), SchemaRoot);
        Assert.Empty(asked.Lines);
        Assert.Contains(asked.Build!.Problems, p => p.StartsWith(PeriodDeclarer.Refused, StringComparison.Ordinal) && p.Contains(ReviewChecks.TwoContracts, StringComparison.Ordinal));
        Assert.Contains(asked.Build.Problems, p => p.StartsWith(PeriodDeclarer.MinimumHeld, StringComparison.Ordinal)); // no zero line while the salary waits for its type

        var old = Simulator.Run(Scenario(TwoContractsRule.LatestStart), SchemaRoot);
        Assert.Contains(old.Lines, l => l.Line.TypeCode == "17");
        Assert.Contains(old.Build!.Problems, p => p.StartsWith(PeriodDeclarer.ZeroLine, StringComparison.Ordinal));

        Assert.Equal(TwoContractsRule.Ask, SimScenario.Parse(Scenario(TwoContractsRule.Ask).ToJson()).TwoContracts); // kept in the file
    }

    [Fact]
    public void A_listed_employees_salary_gives_the_very_line_the_apps_own_month_gives()
    {
        var result = Simulator.Run(Mari(), SchemaRoot);
        Assert.Null(result.Refusal);
        var line = Assert.Single(result.Lines);

        // the app's own path on the same month: the fixture's register and statement, the same card
        var (store, _, period, _) = ReviewFixtures.Setup();
        using (store)
        {
            new MatchRunner(store).Run(period.Id);
            var workspace = new ReviewWorkspace(store, TaxRates.Year2026);
            var mari = workspace.Load(period.Id).Persons[0].Person!;
            workspace.SavePerson(mari with { FundedPensionMember = false, SocialTaxMinimumApplies = false }, confirmResidency: true);
            var declarer = new PeriodDeclarer(store, TaxRates.Year2026);
            declarer.Build(period.Id);
            var own = declarer.Lines(period.Id).Single(l => l.Payment.PersonId == mari.Id).Payment;
            Assert.Equal(own.Line.Amount, line.Line.Amount);
            Assert.Equal(own.Line.TypeCode, line.Line.TypeCode);
            Assert.Equal(own.Result, line.Result);
        }

        Assert.Equal((1, 905.92m), (result.Totals.Lines, result.Totals.Net));
        Assert.NotNull(result.Xml);
        Assert.Null(result.XmlRefusal);
        Assert.Contains(ReviewFixtures.MariCode, result.Xml, StringComparison.Ordinal);
        Assert.Contains(Simulator.RegistryCode, result.Xml, StringComparison.Ordinal);
        Assert.Equal(PaymentTypes.Get("10").Code, line.Line.TypeCode);
    }

    private static readonly SimRow Salary500 = new(new DateOnly(2026, 10, 5), "Mari Maasikas", 500m, "palk", Category: "Palgad");

    [Fact]
    public void The_minimum_follows_how_the_person_stands_in_the_register()
    {
        // listed under a contract that carries it: topped up to the minimum
        var listed = Simulator.Run(Mari(salary: 500m, minimum: true));
        Assert.True(listed.Build!.MinimumShortfall > 0m);

        // a register of the month without the person: no minimum on the pay (decision 176)
        var notListed = Simulator.Run(Mari(SimRegister.NotListed, salary: 500m, minimum: true) with { Person = Mari(minimum: true).Person with { TorTypeId = "1" }, Rows = [Salary500] }); // the type on the card and the category on the row: the register and the search give none
        Assert.Null(notListed.Refusal);
        Assert.Equal(0m, notListed.Build!.MinimumShortfall);
        Assert.True(notListed.Build.Problems.Concat(notListed.BuildProblems).Any(p => p.Contains(PeriodDeclarer.NoEmploymentInMonth, StringComparison.Ordinal)), string.Join(" | ", notListed.Build.Problems.Concat(notListed.BuildProblems).Concat(notListed.Steps).Concat(notListed.Review!.Payments.SelectMany(p => p.Blockers.Select(b => b.Code)))));
        Assert.Single(notListed.Lines);

        // no register in the month: the card's word stands
        var none = Simulator.Run(Mari(SimRegister.NoRegister, salary: 500m, minimum: true) with { Person = Mari(minimum: true).Person with { TorTypeId = "1" }, Rows = [Salary500] });
        Assert.Null(none.Refusal);
        Assert.True(none.Build!.MinimumShortfall > 0m);
    }

    [Fact]
    public void The_reviews_word_on_a_row_stands_over_the_searchs_and_a_row_left_out_is_listed()
    {
        var scenario = Mari() with
        {
            Rows =
            [
                new SimRow(new DateOnly(2026, 10, 5), "Mari Maasikas", 905.92m, "palk"),
                new SimRow(new DateOnly(2026, 10, 20), "Mari Maasikas", 120m, "haigusleht", Category: "Haigusleht"),
                new SimRow(new DateOnly(2026, 10, 25), "Mari Maasikas", 50m, "palk", Declare: false),
            ],
        };
        var result = Simulator.Run(scenario);
        Assert.Null(result.Refusal);
        Assert.Equal(2, result.Lines.Count);
        Assert.Contains(result.Lines, l => l.NetPaid == 120m);
        var left = Assert.Single(result.Review!.Undeclared);
        Assert.Equal((50m, true), (left.Payment.Amount, left.ByChoice));
    }

    [Fact]
    public void A_scenario_goes_to_a_file_and_back_and_another_file_is_refused()
    {
        var scenario = Mari(SimRegister.NotListed) with { Mode = DeclarationMode.TotalsPerType };
        var back = SimScenario.Parse("﻿" + scenario.ToJson());
        Assert.Equal(scenario.Person, back.Person);
        Assert.Equal((scenario.Year, scenario.Month, scenario.Register, scenario.Mode), (back.Year, back.Month, back.Register, back.Mode));
        Assert.Equal(scenario.Employments, back.Employments);
        Assert.Equal(scenario.Rows, back.Rows);
        Assert.Contains("\"NotListed\"", scenario.ToJson(), StringComparison.Ordinal);

        Assert.Throws<ArgumentException>(() => SimScenario.Parse("{ \"kind\": \"satellestsd-category-phrases\" }"));
        Assert.Throws<ArgumentException>(() => SimScenario.Parse("not json"));
    }

    [Fact]
    public void A_month_without_rates_is_refused_by_the_build_and_said()
    {
        var result = Simulator.Run(Mari() with { Year = 2025 });
        Assert.NotNull(result.Refusal);
        Assert.NotNull(result.RefusalError);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void A_card_the_review_refuses_is_said_not_thrown_and_a_run_without_lines_has_no_xml_refusal_of_its_own()
    {
        var wrongCode = Simulator.Run(Mari() with { Person = Mari().Person with { PersonalCode = "12345678901" } });
        Assert.IsType<ReviewException>(wrongCode.RefusalError);
        Assert.Empty(wrongCode.Lines);

        var none = Simulator.Run(Mari() with { Rows = [new SimRow(new DateOnly(2026, 10, 5), "Mari Maasikas", 905.92m, "palk", Declare: false)] });
        Assert.Null(none.Refusal);
        Assert.Empty(none.Lines);
        Assert.Null(none.Xml);
        Assert.Null(none.XmlRefusal); // no file to write is no refusal: the window says it in its own words
    }

    [Fact]
    public void Not_listed_stays_not_listed_when_the_card_carries_the_stand_ins_own_code()
    {
        var scenario = Mari(SimRegister.NotListed, salary: 500m, minimum: true) with
        {
            Person = Mari(minimum: true).Person with { PersonalCode = Simulator.StandInCode, TorTypeId = "1" },
            Rows = [Salary500 with { Payee = "Mari Maasikas" }],
        };
        var result = Simulator.Run(scenario);
        Assert.Null(result.Refusal);
        Assert.Equal(0m, result.Build!.MinimumShortfall); // no minimum: the register does not list the person
        Assert.Null(result.Review!.Register);
    }

    [Fact]
    public void The_search_runs_with_the_owners_saved_phrases_when_given()
    {
        var scenario = Mari() with { Rows = [new SimRow(new DateOnly(2026, 10, 5), "Mari Maasikas", 300m, "kasumiosa")] };
        var own = $$"""{ "kind": "{{MatchCatalog.PhrasesKind}}", "sets": { "standard": { "Dividendid": "*kasumiosa*" } } }""";
        var builtIn = Simulator.Run(scenario);
        var withOwn = Simulator.Run(scenario, savedCatalog: own);
        Assert.False(builtIn.OwnPhrases);
        Assert.True(withOwn.OwnPhrases);
        Assert.NotEqual("Dividendid", builtIn.Review!.Payments.Single().Category);
        Assert.Equal("Dividendid", withOwn.Review!.Payments.Single().Category);
    }

    [Fact]
    public void A_scenario_file_with_an_empty_row_or_contract_is_refused()
    {
        var json = Mari().ToJson();
        Assert.Throws<ArgumentException>(() => SimScenario.Parse(json.Replace("\"Rows\": [", "\"Rows\": [ null,", StringComparison.Ordinal)));
        Assert.Throws<ArgumentException>(() => SimScenario.Parse(json.Replace("\"Employments\": [", "\"Employments\": [ null,", StringComparison.Ordinal)));
        Assert.Throws<ArgumentException>(() => SimScenario.Parse(json.Replace("\"Payee\": \"Mari Maasikas\"", "\"Payee\": null", StringComparison.Ordinal)));
        Assert.Equal(Mari().Rows, SimScenario.Parse(json).Rows);
    }
}
