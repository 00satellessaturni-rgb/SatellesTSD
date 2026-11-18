using SatellesTSD.Core.Import;

namespace SatellesTSD.Core.Tax;

/// <summary>When a person reaches old-age pension age (Riikliku pensionikindlustuse seadus § 7), and whether a payment's
/// month comes after it — the employee's unemployment insurance premium is not withheld from then on (Töötuskindlustuse
/// seadus § 42 lg 1 p 1: the duty ends on the last day of the month the age is reached, whether or not a pension is
/// granted; the employer's 0.8 % stays). The age by year of birth is the law's own table up to 1961 (65 from 1961); from
/// 1962 the Government sets it from life expectancy, so the month is not known here and the card's year decides.</summary>
public static class PensionAge
{
    /// <summary>The pension age (years, months) by year of birth, RPKS § 7 as amended (65 for 1961).</summary>
    private static (int Years, int Months)? AgeFor(int birthYear) => birthYear switch
    {
        < 1953 => (63, 0), // reached in 2015 or before: any figure of these years lies long behind
        1953 => (63, 0),
        1954 => (63, 3),
        1955 => (63, 6),
        1956 => (63, 9),
        1957 => (64, 0),
        1958 => (64, 3),
        1959 => (64, 6),
        1960 => (64, 9),
        1961 => (65, 0),
        _ => null,
    };

    /// <summary>The day an Estonian personal code's holder reaches pension age; null for no code that reads, or a year of
    /// birth whose pension age the law leaves to a later regulation.</summary>
    public static DateOnly? ReachedOn(string? personalCode)
    {
        if (PersonalCode.BirthDate(personalCode) is not { } born || AgeFor(born.Year) is not { } age)
        {
            return null;
        }

        return born.AddYears(age.Years).AddMonths(age.Months);
    }

    /// <summary>Whether the person reached pension age in a month before the payment's (year, month): the card's
    /// pension-age year first — an earlier year is before, a later one is not, the same year is before only when the
    /// personal code places the day in an earlier month of it; with no year on the card, the personal code alone.</summary>
    public static bool ReachedBefore(int? pensionAgeYear, string? personalCode, int year, int month)
    {
        var reached = ReachedOn(personalCode);
        if (pensionAgeYear is { } cardYear)
        {
            if (cardYear != year)
            {
                return cardYear < year;
            }

            return reached is { } day && day.Year == year && day.Month < month;
        }

        return reached is { } on && (on.Year < year || (on.Year == year && on.Month < month));
    }
}
