using SatellesTSD.Core.Store;
using SatellesTSD.Core.Tax;

namespace SatellesTSD.Core.Review;

/// <summary>What the review refuses before S8 ever computes a line: the reasons a payment cannot be declared as it
/// stands — its own (no person, no payee id, residency never checked, no type, another month, an amount that counts
/// as nothing) and then the engine's (<see cref="PaymentChecks"/>) for the type and the person's facts of the year.</summary>
public static class ReviewChecks
{
    public const string NoPerson = "NO_PERSON";
    public const string NoPayeeId = "NO_PAYEE_ID";
    public const string ResidencyNotChecked = "RESIDENCY_NOT_CHECKED";
    public const string NoType = "NO_TYPE";
    public const string UnknownType = "UNKNOWN_TYPE";
    public const string NotInPeriod = "NOT_IN_PERIOD";
    public const string NotCounted = "NOT_COUNTED";
    public const string ForeignCurrency = "FOREIGN_CURRENCY";
    public const string DuplicateRow = "DUPLICATE_ROW";

    /// <summary>Money that came in (a credit row) is no payout, whatever category it was given (decision 141).</summary>
    public const string MoneyIn = "MONEY_IN";

    /// <summary>Whether an amount is in euros: the statement names no currency (the banks' euro accounts), or names the euro.</summary>
    public static bool IsEuro(string? currency) =>
        string.IsNullOrWhiteSpace(currency) || currency.Trim().Equals("EUR", StringComparison.OrdinalIgnoreCase) || currency.Trim() == "€";

    /// <param name="duplicate">The row is a copy of an earlier source's row (the run's DUPLICATE_ROW): the original counts, this one never.</param>
    public static IReadOnlyList<Refusal> Of(Person? person, string? typeCode, decimal amount, bool amountOk, bool inPeriod, int year, TaxRates rates, string? currency = null, bool duplicate = false, Import.Direction direction = Import.Direction.Unknown)
    {
        ArgumentNullException.ThrowIfNull(rates);
        var refusals = new List<Refusal>();
        if (!IsEuro(currency))
        {
            refusals.Add(new Refusal(ForeignCurrency, $"The amount is in {currency!.Trim()}, not in euros; declare the euro amount on a line added by hand."));
        }

        if (direction == Import.Direction.Credit)
        {
            refusals.Add(new Refusal(MoneyIn, "The row is money in, not a payout, and is not declared here; a salary paid back is a correction of the payment it refunds, made in e-MTA."));
        }

        if (duplicate)
        {
            refusals.Add(new Refusal(DuplicateRow, "The row is a copy of an earlier statement's row (the same payment twice); the original counts, this one never."));
        }

        if (person is null)
        {
            refusals.Add(new Refusal(NoPerson, "The payment has no person; pick one, or leave it undeclared."));
        }
        else
        {
            if (person.PayeeId is null)
            {
                refusals.Add(new Refusal(NoPayeeId, "The person has neither an Estonian personal code nor an MTA non-resident code."));
            }

            if (person.ResidencyCheckedOn is null)
            {
                refusals.Add(new Refusal(ResidencyNotChecked, "The person's residency has not been checked — by the register or by hand."));
            }
        }

        if (!inPeriod)
        {
            refusals.Add(new Refusal(NotInPeriod, "The payment's date is not in the period's month; it belongs in another month's file."));
        }

        if (!amountOk && !duplicate)
        {
            refusals.Add(new Refusal(NotCounted, "The amount could not be read, or two persons were found for the row; it counts as nothing."));
        }

        if (typeCode is null)
        {
            refusals.Add(new Refusal(NoType, "No payment type: the category has no code for this person, or the employment type is unknown."));
        }
        else if (!PaymentTypes.TryGet(typeCode, out var type))
        {
            refusals.Add(new Refusal(UnknownType, $"Payment type {typeCode} is not in the table."));
        }
        else if (person is not null)
        {
            refusals.AddRange(PaymentChecks.Check(type!, person.FactsFor(year), amount, rates));
        }

        return refusals;
    }
}
