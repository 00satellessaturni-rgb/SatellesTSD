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
public static class ReviewDefaults
{
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

    public static string? TypeCodeFor(Category? category, string? torEmploymentTypeId, Residency residency)
    {
        if (category is null)
        {
            return null;
        }

        if (category.IsSalary)
        {
            return EmploymentTypes.DefaultCode(torEmploymentTypeId, residency);
        }

        return category.Code is { } code && CategoryCodes.TryGetValue(code, out var pair)
            ? residency == Residency.NonResident ? pair.NonResident : pair.Resident
            : null;
    }
}
