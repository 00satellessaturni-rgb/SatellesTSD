namespace SatellesTSD.Core.Tax;

/// <summary>The part of a month's wages the state (Töötukassa) pays the social tax on for a person with partial or no work
/// ability, as MTA applies SMS § 6 lg 3 (decision 267): the kuumäär of the month the pay is for, in proportion to the
/// calendar days of that month the state's obligation covers — from the day the employer's application reached Töötukassa
/// (never before the employment began) to the day the employment ended, both counted.</summary>
/// <param name="PayMonth">The first day of the month the pay is for.</param>
/// <param name="Rate">The kuumäär of that month's year.</param>
/// <param name="Days">The days of the month the obligation covers.</param>
/// <param name="DaysInMonth">The calendar days of the month.</param>
/// <param name="Amount">Rate ÷ days in the month × days covered, to the cent; the whole rate for a whole month.</param>
/// <param name="ByHand">The accountant typed the amount for the declared month (holiday pay or a final pay brings two
/// months' pay into one); the days are then not what the amount came from.</param>
public sealed record WorkAbilityMonth(DateOnly PayMonth, decimal Rate, int Days, int DaysInMonth, decimal Amount, bool ByHand = false)
{
    /// <summary>The most a declared month may carry: two months' rates (MTA's own bound, KR_SM_07) — pay for two months
    /// paid in one, as with holiday pay or a final pay.</summary>
    public const int MostRates = 2;

    /// <summary>The kuumäär of a year for this reckoning: the year's rates when the build knows them, else the figure MTA
    /// published for a year only this needs (pay for December declared in January); null for a year not known.</summary>
    public static decimal? RateOf(int year) => TaxRates.For(year)?.SocialTaxMinimumBase ?? (year == 2025 ? 820m : null);

    /// <summary>The first day of the month the pay declared in a month is for: the month before where the company pays
    /// wages in the month after they were earned, else the declared month itself.</summary>
    public static DateOnly PayMonthOf(int year, int month, bool wagesPaidFollowingMonth)
    {
        var declared = new DateOnly(year, month, 1);
        return wagesPaidFollowingMonth ? declared.AddMonths(-1) : declared;
    }

    /// <summary>The month's amount.</summary>
    /// <param name="payMonth">Any day of the month the pay is for.</param>
    /// <param name="rate">The kuumäär of that month's year.</param>
    /// <param name="applicationDate">The day the application reached Töötukassa; null = it stood before the month.</param>
    /// <param name="employment">The spans of the person's employment the register knows (a null start = since before, a
    /// null end = going on); null or empty = the register says nothing, so only the application bounds the month.</param>
    public static WorkAbilityMonth Reckon(DateOnly payMonth, decimal rate, DateOnly? applicationDate, IReadOnlyList<(DateOnly? Start, DateOnly? End)>? employment = null)
    {
        var first = new DateOnly(payMonth.Year, payMonth.Month, 1);
        var daysInMonth = DateTime.DaysInMonth(first.Year, first.Month);
        var days = 0;
        for (var day = first; day.Month == first.Month && day.Year == first.Year; day = day.AddDays(1))
        {
            var applied = applicationDate is not { } since || day >= since;
            var employed = employment is not { Count: > 0 } || employment.Any(e => (e.Start is not { } start || day >= start) && (e.End is not { } end || day <= end));
            if (applied && employed)
            {
                days++;
            }
        }

        var amount = days == daysInMonth ? rate : Math.Round(rate / daysInMonth * days, 2, MidpointRounding.AwayFromZero);
        return new WorkAbilityMonth(first, rate, days, daysInMonth, amount);
    }

    /// <summary>The month with the accountant's own amount in place of the reckoned one.</summary>
    public WorkAbilityMonth WithAmountByHand(decimal amount) => this with { Amount = amount, ByHand = true };
}
