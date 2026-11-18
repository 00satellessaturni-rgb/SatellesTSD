using SatellesTSD.Core.Model;
using SatellesTSD.Core.Tax;

namespace SatellesTSD.Tests;

/// <summary>The rules matrix (decision 203): drawn from the type table and the engine's own rule, never a copy of them.</summary>
public class RulesMatrixTests
{
    [Fact]
    public void Every_type_is_a_row_with_the_engines_own_flags_and_the_register_types_that_declare_under_it()
    {
        var cases = new[]
        {
            new RuleCase(),
            new RuleCase(Residency.NonResident, FundedPensionMember: false, BasicExemptionApplied: false, TreatyIncomeTaxRate: 0.05m),
            new RuleCase(OldAgeOrFlexiblePension: true, PensionAgeThisYear: true, SocialTaxMinimumApplies: false),
            new RuleCase(A1: A1Case.EstonianA1WorkAbroad),
            new RuleCase(A1: A1Case.ForeignA1WorkInEstonia),
        };
        foreach (var rule in cases)
        {
            var rows = RulesMatrix.Rows(rule);
            Assert.Equal(PaymentTypes.All.Select(t => t.Code), rows.Select(r => r.Type.Code));
            Assert.All(rows, r => Assert.Equal(EffectiveFlags.Of(r.Type, rule.Facts), r.Flags));
            Assert.All(rows, r => Assert.Equal(r.Type.Payee == rule.Residency, r.PayeeMatches));
        }

        var resident = RulesMatrix.Rows(new RuleCase()).ToDictionary(r => r.Type.Code);
        Assert.Equal(["1", "507", "501"], resident["10"].Employments.Select(e => e.TorTypeId));
        Assert.Equal(["601"], resident["21"].Employments.Select(e => e.TorTypeId));
        Assert.Equal(new EffectiveFlags(A1Case.None, true, true, true, true, true, 0.22m, ExemptionKind.Mvt610), resident["10"].Flags);
        Assert.False(resident["120"].PayeeMatches);

        var nonResident = RulesMatrix.Rows(new RuleCase(Residency.NonResident, TreatyIncomeTaxRate: 0.05m)).ToDictionary(r => r.Type.Code);
        Assert.False(nonResident["120"].Flags.FundedPension); // never a non-resident's
        Assert.Equal(0.05m, nonResident["123"].Flags.IncomeTaxRate); // the treaty rate on a type that allows it
        Assert.Equal(0.22m, nonResident["120"].Flags.IncomeTaxRate); // and only there

        var pensioner = RulesMatrix.Rows(new RuleCase(OldAgeOrFlexiblePension: true, PensionAgeThisYear: true)).Single(r => r.Type.Code == "10").Flags;
        Assert.Equal((false, true, ExemptionKind.Mvt650), (pensioner.UnemploymentEmployee, pensioner.UnemploymentEmployer, pensioner.ExemptionKind));
        Assert.False(RulesMatrix.Rows(new RuleCase(A1: A1Case.ForeignA1WorkInEstonia)).Single(r => r.Type.Code == "10").Flags.SocialTax);
        Assert.Null(RulesMatrix.Rows(new RuleCase(A1: A1Case.EstonianA1WorkAbroad)).Single(r => r.Type.Code == "10").Flags.IncomeTaxRate);
    }

    [Fact]
    public void The_search_finds_a_row_by_its_code_name_or_register_type_every_word_of_it()
    {
        var rows = RulesMatrix.Rows(new RuleCase());
        Assert.Equal(["21", "156"], rows.Where(r => r.Matches("601")).Select(r => r.Type.Code));
        Assert.Equal(["10", "120"], rows.Where(r => r.Matches("tööleping 507")).Select(r => r.Type.Code));
        Assert.Contains(rows, r => r.Matches("haigushüvitis") && r.Type.Code == "24");
        Assert.Equal(rows.Count, rows.Count(r => r.Matches("  ")));
        Assert.Empty(rows.Where(r => r.Matches("dividend")));

        // a word of digits is a code, whole: the section's own name (EE0201001) and a longer code do not answer "10"
        Assert.Equal(["10"], rows.Where(r => r.Matches("10")).Select(r => r.Type.Code));
        Assert.Equal(rows.Where(r => r.Type.Section == Section.EE0201002).Select(r => r.Type.Code), rows.Where(r => r.Matches("2")).Select(r => r.Type.Code));
        Assert.Empty(rows.Where(r => r.Matches("20")));
    }

    [Fact]
    public void A_treaty_rate_the_build_refuses_is_shown_refused_not_applied()
    {
        // the matrix draws the build's own rule: above the type's rate, or not a whole percent, is TREATY_RATE_INVALID
        foreach (var (rate, refused) in new[] { (0.05m, false), (0.22m, false), (0.30m, true), (0.155m, true) })
        {
            var rows = RulesMatrix.Rows(new RuleCase(Residency.NonResident, TreatyIncomeTaxRate: rate)).ToDictionary(r => r.Type.Code);
            Assert.Equal(refused, rows["123"].TreatyRefused);
            Assert.False(rows["120"].TreatyRefused); // a type that takes no treaty rate
        }

        Assert.False(RulesMatrix.Rows(new RuleCase(TreatyIncomeTaxRate: 0.30m)).Single(r => r.Type.Code == "10").TreatyRefused); // a resident's
    }
}
