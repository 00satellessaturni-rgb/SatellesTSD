using System.Globalization;
using SatellesTSD.Core.Lookups;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Store;

namespace SatellesTSD.Core.Report;

/// <summary>One person of a company who is a non-resident on the card, or was one on a line that used the basic
/// exemption — with what the store says of the exemption for them: the tick on the card, and the lines worked out for
/// a non-resident with an exemption.</summary>
/// <param name="ExemptionLines">The keys of the person on which an exemption stands for a non-resident: on the standing
/// line (a draft counts; a cancellation and a refused attempt do not), or on the version MTA still holds beneath a
/// change not yet sent.</param>
/// <param name="ExemptionUsed">The exemption on those keys: what MTA holds where it holds one, else the draft's.</param>
/// <param name="Months">The months of those keys, "yyyy-MM", earliest first.</param>
/// <param name="OnServer">How many of those keys MTA is presumed to hold with the exemption (sent, accepted, to change or in review).</param>
/// <param name="Unsent">How many of the keys MTA holds with the exemption have a change on top that is not on the server yet
/// (a draft M or T): the correction is made here and still to be sent.</param>
public sealed record NonResidentRow(Company Company, Person Person, int ExemptionLines, decimal ExemptionUsed, IReadOnlyList<string> Months, int OnServer, int Unsent)
{
    /// <summary>A non-resident on the card now (a person listed only for their lines is a resident now).</summary>
    public bool NonResidentNow => Person.Residency == Residency.NonResident;

    /// <summary>The basic exemption's application is ticked on the card of a person who is a non-resident now.</summary>
    public bool Applied => NonResidentNow && Person.BasicExemptionApplied;

    public bool Used => ExemptionLines > 0;

    public bool Equals(NonResidentRow? other) =>
        other is not null && Company == other.Company && Person == other.Person && ExemptionLines == other.ExemptionLines && ExemptionUsed == other.ExemptionUsed
        && Months.SequenceEqual(other.Months, StringComparer.Ordinal) && OnServer == other.OnServer && Unsent == other.Unsent;

    public override int GetHashCode() => HashCode.Combine(Company, Person, ExemptionLines, ExemptionUsed, Months.Count, OnServer, Unsent);
}

/// <summary>The non-residents of every company of the store, for the owner's look across the whole of it (decision
/// 174): in which companies there are non-residents, and in which a non-resident has the basic exemption — ticked on
/// the card, or used on a line built or sent for them as a non-resident. A line is judged by the residency it was
/// worked out with, never by the card's today. Read from the store as it stands; nothing is changed.</summary>
public static class NonResidentOverview
{
    public const string CompaniesSheet = "Ettevõtted";
    public const string PersonsSheet = "Mitteresidendid";

    public static readonly IReadOnlyList<string> CompanyHeaders =
        ["Registrikood", "Ettevõte", "Mitteresidente", "Maksuvaba avaldus märgitud", "Maksuvaba kasutatud ridadel"];

    public static readonly IReadOnlyList<string> PersonHeaders =
    [
        "Registrikood", "Ettevõte", "Perekonnanimi", "Eesnimi", "Isikukood / MRR kood", "Praegu", "Residentsusriik", "Residentsuse allikas", "Kontrollitud",
        "Maksuvaba avaldus", "Maksuvabaga ridu", "Neist e-MTA-s", "Parandus saatmata", "Maksuvaba kasutatud", "Kuud",
    ];

    /// <summary>The columns of <see cref="PersonHeaders"/> that hold a count, an amount and a date (0-based).</summary>
    public static readonly IReadOnlyList<int> CountColumns = [10, 11, 12];
    public const int AmountColumn = 13;
    public const int DateColumn = 8;

    /// <summary>Every person to look at, by the company's name and then the person's.</summary>
    public static IReadOnlyList<NonResidentRow> Build(IStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        var rows = new List<NonResidentRow>();
        foreach (var company in store.Companies())
        {
            // the keys of the company's months on which an exemption stands for a non-resident, by person
            var keys = new Dictionary<Guid, List<(Period Month, decimal Used, bool Held, bool Unsent)>>();
            foreach (var month in store.Periods(company.Id))
            {
                foreach (var standing in store.Payments(month.Id).Where(l => l.PersonId is not null))
                {
                    if (standing is { Action: LineAction.T, Status: PaymentStatus.Cancelled })
                    {
                        continue; // a key cancelled on the server is dead: MTA holds nothing on it, and nothing is pending
                    }

                    // what MTA holds of the key: the standing version when it is there; else the latest version beneath a
                    // change made here and not sent yet (a first version has nothing beneath it)
                    var held = standing.OnServer ? standing
                        : standing.Version > 1 ? store.History(month.Id, standing.Section, standing.LineNumber).LastOrDefault(v => v.OnServer) : null;
                    var heldCounts = held is not null && ForNonResident(held);
                    var standingCounts = standing.Status != PaymentStatus.Rejected && ForNonResident(standing);
                    if (!heldCounts && !standingCounts)
                    {
                        continue;
                    }

                    // the person the exemption was given to: the held version's when MTA has it, else the draft's
                    var personId = (heldCounts ? held!.PersonId : standing.PersonId) ?? standing.PersonId!.Value;
                    if (!keys.TryGetValue(personId, out var list))
                    {
                        list = [];
                        keys[personId] = list;
                    }

                    list.Add((month, heldCounts ? held!.Result!.ExemptionUsed : standing.Result!.ExemptionUsed, heldCounts, heldCounts && !ReferenceEquals(held, standing)));
                }
            }

            foreach (var person in store.Persons(company.Id).Where(p => p.Residency == Residency.NonResident || keys.ContainsKey(p.Id)))
            {
                var lines = keys.GetValueOrDefault(person.Id) ?? [];
                rows.Add(new NonResidentRow(
                    company,
                    person,
                    lines.Count,
                    lines.Sum(l => l.Used),
                    lines.Select(l => (l.Month.Year, l.Month.Month)).Distinct().Order().Select(m => $"{m.Year:0000}-{m.Month:00}").ToList(),
                    lines.Count(l => l.Held),
                    lines.Count(l => l.Unsent)));
            }
        }

        return rows
            .OrderBy(r => r.Company.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.Company.RegistryCode, StringComparer.Ordinal).ThenBy(r => r.Company.Id)
            .ThenBy(r => r.Person.LastName, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.Person.FirstName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>A line worked out for a non-resident with an exemption used: by the facts it was built with (the person
    /// may be a resident on the card today, or have been one when an earlier line was built). A cancellation carries none.</summary>
    private static bool ForNonResident(StoredPayment line) =>
        line.Action != LineAction.T && line.Facts is { Residency: Residency.NonResident } && line.Result is { ExemptionUsed: > 0m };

    /// <summary>The list as a workbook: the companies with their counts, then the persons one per row.</summary>
    /// <param name="at">The moment the list was made, for the file's name.</param>
    public static ReportBook ToBook(IReadOnlyList<NonResidentRow> rows, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var companies = new List<ReportRow> { ReportRow.Headers([.. CompanyHeaders]) };
        foreach (var group in rows.GroupBy(r => r.Company.Id))
        {
            var company = group.First().Company;
            companies.Add(new ReportRow(
            [
                ReportCell.Identifier(company.RegistryCode), ReportCell.Of(company.Name), Count(group.Count(r => r.NonResidentNow)), Count(group.Count(r => r.Applied)), Count(group.Count(r => r.Used)),
            ]));
        }

        var persons = new List<ReportRow> { ReportRow.Headers([.. PersonHeaders]) };
        persons.AddRange(rows.Select(row => new ReportRow(
        [
            ReportCell.Identifier(row.Company.RegistryCode), ReportCell.Of(row.Company.Name), ReportCell.Of(row.Person.LastName), ReportCell.Of(row.Person.FirstName),
            ReportCell.Identifier(CodeOf(row.Person)), ReportCell.Of(ResidencyNow(row)), ReportCell.Of(row.Person.ResidenceCountry), ReportCell.Of(SourceText(row.Person.ResidencySource)),
            ReportCell.Day(row.Person.ResidencyCheckedOn), ReportCell.Of(YesNo(row.Person.BasicExemptionApplied)), Count(row.ExemptionLines), Count(row.OnServer), Count(row.Unsent),
            row.Used ? ReportCell.Amount(row.ExemptionUsed) : ReportCell.Empty, ReportCell.Of(string.Join(", ", row.Months)),
        ])));

        return new ReportBook(
            $"mitteresidendid-{at.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture)}.xlsx",
            [new ReportSheet(CompaniesSheet, companies), new ReportSheet(PersonsSheet, persons)]);
    }

    /// <summary>The code the declaration names the person by: the personal code, else the MTA non-resident code.</summary>
    public static string? CodeOf(Person person) => !string.IsNullOrWhiteSpace(person.PersonalCode) ? person.PersonalCode : person.MrrCode;

    public static string ResidencyNow(NonResidentRow row) => row.NonResidentNow ? "mitteresident" : "resident";

    public static string YesNo(bool value) => value ? "jah" : "ei";

    public static string SourceText(LookupSource source) => source switch
    {
        LookupSource.Emta => "EMTA",
        LookupSource.Manual => "käsitsi",
        _ => "—",
    };

    /// <summary>A count as a whole number in a code cell (no decimals), nothing for none.</summary>
    private static ReportCell Count(int count) => count == 0 ? ReportCell.Empty : new ReportCell(ReportCellKind.Code, count.ToString(CultureInfo.InvariantCulture), count);
}
