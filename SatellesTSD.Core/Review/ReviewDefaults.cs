using SatellesTSD.Core.Import;
using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Model;

namespace SatellesTSD.Core.Review;

/// <summary>What the review assumes until the accountant says otherwise: the payment type of a category for a person.
/// A salary goes by the register's employment type (<see cref="EmploymentTypes"/>: the workbook's 1 / 507 / 501 → 10 / 120,
/// 600 / 602 → 17 / 123, 601 → 21; and beyond the workbook, by decision 30, 601 → 156 for a non-resident and 506 → 16).
/// A coded category goes by its matrix row: the workbook's uur → 57 / 205 and haigus → 24 / 129, and autorent → 50 for
/// a resident; 186 for a non-resident's car rent is the app's proposal (plan question 7, open with the accountant).
/// A codeless category (Dividendid, Kommunaalkulud, Kompensatsioon, Laenud, Trahvid, TÖR-väline palgad) gives nothing:
/// found, never declared (plan question 10).</summary>
/// <summary>The rule for a salary paid on a day two employments of different types are in force (the window's Seaded
/// choice, decision 209, amending 85): the Tööleping's type; no type, asked of the accountant (TWO_CONTRACTS); or the
/// old rule, the valid one first, then the latest start.</summary>
public enum TwoContractsRule
{
    SalaryToEmploymentContract,
    Ask,
    LatestStart,
}

/// <summary>The employment type a payment goes by under the two-contracts rule.</summary>
/// <param name="TorTypeId">The TÖR type; null when the rule asks.</param>
/// <param name="Ask">The rule asks: no type is offered and the payment is refused (TWO_CONTRACTS) until one is chosen.</param>
/// <param name="InForce">The TÖR types in force on the day when they declare a salary under different codes; empty otherwise.</param>
public sealed record TypePick(string? TorTypeId, bool Ask, IReadOnlyList<string> InForce);

public static class ReviewDefaults
{
    /// <summary>The second pass's salary-pattern category of a payee the register does not list (matching.json, the
    /// workbook's column): codeless, never declared (plan question 10); its payment of no type is refused as OUTSIDE_TOR.</summary>
    public const string OutsideTorCategory = "TÖR-väline palgad";

    /// <summary>The TÖR types of an employment contract (Tööleping): 1, and 507 by visa.</summary>
    public static readonly IReadOnlySet<string> EmploymentContractTypes = new HashSet<string>(["1", "507"], StringComparer.Ordinal);

    /// <summary>The employment type a payment goes by (decision 209): the card's type when the card says it stands for every
    /// payment; for a salary on a day two employments in force declare under different codes (a Tööleping and a VÕS contract,
    /// say), the rule's choice — the Tööleping in force (with none among them, the old rule), none (asked), or the old rule;
    /// else <see cref="EmploymentTypeFor"/>. Two Töölepingud (1 and 507, both 10) are no conflict.</summary>
    public static TypePick PickFor(MatchPerson? register, Store.Person? person, DateOnly? day, bool salary, Residency residency, TwoContractsRule rule)
    {
        var old = EmploymentTypeFor(register, person, day);
        if (person is { TypeFromCard: true, TorEmploymentTypeId: { Length: > 0 } card } && EmploymentTypes.Find(card) is not null)
        {
            return new TypePick(card, false, []);
        }

        if (!salary || day is not { } d || register is null)
        {
            return new TypePick(old, false, []);
        }

        var inForce = register.Entries.Where(e => e.InForceOn(d)).ToList(); // by its dates and its pause (decision 181)
        // two rows are one contract for the pay when they declare it alike — under the card's A1: a type whose social tax is
        // paid abroad (504) declares nothing without a foreign A1, so beside a Tööleping it is another contract (decision 259)
        if (inForce.Select(e => EmploymentTypes.Find(e.TypeId) is { ForeignA1Only: true } && person?.A1 != A1Case.ForeignA1WorkInEstonia
                ? e.TypeId : EmploymentTypes.DefaultCode(e.TypeId, residency) ?? e.TypeId).Distinct(StringComparer.Ordinal).Count() < 2)
        {
            return new TypePick(old, false, []);
        }

        var types = inForce.Select(e => e.TypeId).Distinct(StringComparer.Ordinal).ToList();
        return rule switch
        {
            TwoContractsRule.SalaryToEmploymentContract => new TypePick(
                inForce.Where(e => EmploymentContractTypes.Contains(e.TypeId?.Trim() ?? ""))
                    .OrderBy(e => e.StateId == TorStates.Valid ? 0 : 1).ThenByDescending(e => e.StartDate ?? DateOnly.MinValue)
                    .FirstOrDefault()?.TypeId ?? old, false, types),
            TwoContractsRule.Ask => new TypePick(null, true, types),
            _ => new TypePick(old, false, types),
        };
    }

    /// <summary>The resident and the non-resident VALJAMLIIK codes of a category's code.</summary>
    public static readonly IReadOnlyDictionary<string, (string Resident, string NonResident)> CategoryCodes = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
    {
        ["uur"] = ("57", "205"),
        ["haigus"] = ("24", "129"),
        ["autorent"] = ("50", "186"),
    };

    /// <summary>The TÖR employment type a payment of the person goes by: the card's type when the card says it stands for
    /// every payment; else the register's contract covering the payment's day (a valid one first, then the latest start),
    /// so a person with two contracts at once has each payment by its own day; on a day between contracts, the one ended
    /// last before it (a final settlement paid after the end); else the entry standing for the month; else the type kept on
    /// the card. A card type the app does not know is no card type: the register's stands, as the grid shows.</summary>
    public static string? EmploymentTypeFor(MatchPerson? register, Store.Person? person, DateOnly? day)
    {
        if (person is { TypeFromCard: true, TorEmploymentTypeId: { Length: > 0 } card } && EmploymentTypes.Find(card) is not null)
        {
            return card;
        }

        var byDay = day is { } d && register is not null
            ? register.EntryOn(d) ?? register.Entries.Where(e => e.EndDate is { } end && end < d).OrderByDescending(e => e.EndDate).FirstOrDefault()
            : null;
        return byDay?.TypeId ?? register?.Entry.TypeId ?? person?.TorEmploymentTypeId;
    }

    /// <summary>A salary under a register type whose social tax is paid abroad (504), on a card that does not say a foreign
    /// A1 with work in Estonia: it gets no code by default, so that it is never declared with Estonian social tax unasked
    /// (decision 259).</summary>
    public static bool NeedsForeignA1(Category? category, string? torEmploymentTypeId, A1Case a1) =>
        category is { IsSalary: true } && EmploymentTypes.Find(torEmploymentTypeId) is { ForeignA1Only: true } && a1 != A1Case.ForeignA1WorkInEstonia;

    public static string? TypeCodeFor(Category? category, string? torEmploymentTypeId, Residency residency, A1Case a1 = A1Case.None)
    {
        if (category is null)
        {
            return null;
        }

        if (category.IsSalary)
        {
            return NeedsForeignA1(category, torEmploymentTypeId, a1) ? null : EmploymentTypes.DefaultCode(torEmploymentTypeId, residency);
        }

        return category.Code is { } code && CategoryCodes.TryGetValue(code, out var pair)
            ? residency == Residency.NonResident ? pair.NonResident : pair.Resident
            : null;
    }
}
