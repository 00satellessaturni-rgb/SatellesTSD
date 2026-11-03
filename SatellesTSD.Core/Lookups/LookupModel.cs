using System.Globalization;
using System.Text.Json.Serialization;
using SatellesTSD.Core.Model;

namespace SatellesTSD.Core.Lookups;

/// <summary>A stretch of days with an open or a closed end, as the registers state them.</summary>
public sealed record DatePeriod(DateOnly Start, DateOnly? End)
{
    public bool Covers(DateOnly date) => Start <= date && (End is null || date <= End);

    public override string ToString() => End is null ? $"{Start:dd.MM.yyyy} –" : $"{Start:dd.MM.yyyy} – {End:dd.MM.yyyy}";
}

/// <summary>One row of Pensionikeskus's rate table: the period and the contribution rate in force in it.</summary>
/// <param name="Rate">The rate as a fraction (0.02, 0.04, 0.06).</param>
public sealed record RatePeriod(DateOnly Start, DateOnly? End, decimal Rate, string? Explanation)
{
    /// <summary>An open end runs to the day asked about, as the workbook read it.</summary>
    public bool Covers(DateOnly date) => Start <= date && (End is null || date <= End);
}

/// <summary>What the EMTA non-resident register answered for one personal code: the periods of non-residency it
/// holds (none = the person is unknown to it or registered as a resident), as of the register's own date.</summary>
/// <param name="AsOf">The "seisuga" date of the answer, when the page states one; else the day of the query.</param>
/// <param name="Periods">The non-residency periods, oldest first.</param>
/// <param name="Notes">Anything else the answer said (residency certificates of a foreign tax authority), as text.</param>
public sealed record ResidencyFinding(DateOnly AsOf, IReadOnlyList<DatePeriod> Periods, IReadOnlyList<string> Notes)
{
    /// <summary>Resident unless a period of non-residency covers the day.</summary>
    public bool IsResidentOn(DateOnly date) => !Periods.Any(p => p.Covers(date));

    public Residency ResidencyOn(DateOnly date) => IsResidentOn(date) ? Residency.Resident : Residency.NonResident;

    public bool Equals(ResidencyFinding? other) =>
        other is not null && AsOf == other.AsOf && Periods.SequenceEqual(other.Periods) && Notes.SequenceEqual(other.Notes, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(AsOf, Periods.Count, Notes.Count);
}

public enum PensionStatus
{
    /// <summary>"Isik ei ole liitunud kohustusliku kogumispensioniga".</summary>
    NotJoined,
    /// <summary>"Isik on kohustusliku kogumispensioniga liitunud".</summary>
    Joined,
    /// <summary>"Isik on sünniaasta järgi kohustatud II sambaga liituma" (born 1983 or later).</summary>
    ObligedByBirthYear,
}

/// <summary>What Pensionikeskus answered for one personal code.</summary>
/// <param name="WithholdingStart">"Maksete kinnipidamise algus".</param>
/// <param name="ContributionsEnded">"Alates … on isiku kogumispensioni sissemaksed lõppenud".</param>
/// <param name="Rates">The rate table in the page's order — the first row covering a day gives its rate, as the workbook walked them; empty when the page shows none.</param>
public sealed record PensionFinding(PensionStatus Status, DateOnly? WithholdingStart, DateOnly? ContributionsEnded, IReadOnlyList<RatePeriod> Rates)
{
    /// <summary>A member on the day when the withholding has begun by then and the contributions have not ended —
    /// the day they ended is no longer a member's day (the workbook's rule).</summary>
    public bool MemberOn(DateOnly date) =>
        Status != PensionStatus.NotJoined && WithholdingStart is { } start && date >= start && (ContributionsEnded is not { } ended || date < ended);

    /// <summary>The rate in force on the day, or null when no row of the table covers it (never a default).</summary>
    public decimal? RateOn(DateOnly date) => Rates.FirstOrDefault(r => r.Covers(date))?.Rate;

    public bool Equals(PensionFinding? other) =>
        other is not null && Status == other.Status && WithholdingStart == other.WithholdingStart && ContributionsEnded == other.ContributionsEnded && Rates.SequenceEqual(other.Rates);

    public override int GetHashCode() => HashCode.Combine(Status, WithholdingStart, ContributionsEnded, Rates.Count);
}

/// <summary>One answer of a lookup: the finding, or the reason there is none, and the page it was read from.</summary>
/// <param name="Page">The relevant part of the page as text, kept for the accountant's eye and for a later re-read.</param>
public sealed record LookupResult<T>(T? Finding, string? Failure, string? Page)
    where T : class
{
    public bool Succeeded => Finding is not null;

    public static LookupResult<T> Found(T finding, string? page) => new(finding, null, page);

    public static LookupResult<T> Failed(string failure, string? page = null) => new(null, failure, page);
}

/// <summary>The EMTA non-resident register, asked about one personal code.</summary>
public interface IResidencyLookup
{
    Task<LookupResult<ResidencyFinding>> QueryAsync(string personalCode, CancellationToken cancellationToken = default);
}

/// <summary>Pensionikeskus's membership check, asked about one personal code.</summary>
public interface IPensionLookup
{
    Task<LookupResult<PensionFinding>> QueryAsync(string personalCode, CancellationToken cancellationToken = default);
}

public enum LookupKind
{
    Residency,
    Pension,
}

/// <summary>Where a fact or an answer came from.</summary>
public enum LookupSource
{
    /// <summary>Never looked up nor set by hand.</summary>
    Unknown,
    /// <summary>The EMTA non-resident register.</summary>
    Emta,
    /// <summary>Pensionikeskus.</summary>
    Pensionikeskus,
    /// <summary>Not asked: follows from another fact (a non-resident has no funded pension here).</summary>
    Derived,
    /// <summary>The accountant's own word; a lookup never overwrites it.</summary>
    Manual,
}

/// <summary>Every answer a lookup gave for a person, kept as it came (append-only): what was asked (the personal
/// code as asked, so a corrected code never reuses the old code's answer), when, for which control date, and what
/// came back — the finding as JSON, or the failure.</summary>
public sealed record LookupRecord(
    Guid Id,
    Guid PersonId,
    LookupKind Kind,
    LookupSource Source,
    DateTime CheckedAt,
    DateOnly ControlDate,
    string? FindingJson,
    string? Failure,
    string? Page,
    string? Note,
    string? PersonalCode = null)
{
    public bool Succeeded => FindingJson is not null;
}

/// <summary>Parsing of the registers' dates, dd.MM.yyyy as both sites write them.</summary>
public static class LookupDates
{
    public static DateOnly? Parse(string? text) =>
        text is not null && DateOnly.TryParseExact(text.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    /// <summary>Every dd.MM.yyyy date in the text, in order.</summary>
    public static IReadOnlyList<DateOnly> FindAll(string? text)
    {
        var found = new List<DateOnly>();
        if (string.IsNullOrEmpty(text))
        {
            return found;
        }

        for (var i = 0; i + 10 <= text.Length; i++)
        {
            if (char.IsAsciiDigit(text[i]) && (i == 0 || !char.IsAsciiDigit(text[i - 1])) && text[i + 2] == '.' && text[i + 5] == '.' && (i + 10 == text.Length || !char.IsAsciiDigit(text[i + 10]))
                && Parse(text.Substring(i, 10)) is { } date)
            {
                found.Add(date);
                i += 9;
            }
        }

        return found;
    }

    /// <summary>"2%", "4 %", "0.04" → a fraction; null for anything else.</summary>
    public static decimal? ParseRate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var cleaned = text.Trim().Replace(',', '.');
        if (cleaned.EndsWith('%'))
        {
            return decimal.TryParse(cleaned[..^1].Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var percent) ? percent / 100m : null;
        }

        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var fraction) && fraction is > 0 and < 1 ? fraction : null;
    }
}
