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

    public static IReadOnlyList<Refusal> Of(Person? person, string? typeCode, decimal amount, bool amountOk, bool inPeriod, int year, TaxRates rates)
    {
        ArgumentNullException.ThrowIfNull(rates);
        var refusals = new List<Refusal>();
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

        if (!amountOk)
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
