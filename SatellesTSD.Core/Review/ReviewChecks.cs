using SatellesTSD.Core.Store;
using SatellesTSD.Core.Tax;

namespace SatellesTSD.Core.Review;

/// <summary>What the review refuses before S8 ever computes a line: the reasons a payment cannot be declared as it
/// stands — its own (no person, no payee id, a code that is no personal code, residency never checked, no type, another
/// month, an amount that counts as nothing, money in, a copied row) and then the engine's (<see cref="PaymentChecks"/>) for the type and the person's facts of the year.</summary>
public static class ReviewChecks
{
    public const string NoPerson = "NO_PERSON";
    public const string NoPayeeId = "NO_PAYEE_ID";
    public const string ResidencyNotChecked = "RESIDENCY_NOT_CHECKED";
    public const string NoType = "NO_TYPE";

    /// <summary>A salary under a register type whose social tax is paid abroad (504) has no payment type until the card
    /// says a foreign A1 with work in Estonia (decision 259); the argument is the register's type.</summary>
    public const string ForeignA1Needed = "FOREIGN_A1_NEEDED";

    /// <summary>A salary-pattern payment ("TÖR-väline palgad") to a person the month's register does not list, or of a month
    /// searched with no register: refused under its own name in place of NO_TYPE, saying what to do. Nothing else changes:
    /// the category stays codeless, the row undeclared and out of the Deklareerimata sum (decisions 10, 40, 100).</summary>
    public const string OutsideTor = "OUTSIDE_TOR";

    /// <summary>A salary paid on a day two employments of different types are in force, and the Seaded rule says ask (decision 209).</summary>
    public const string TwoContracts = "TWO_CONTRACTS";
    public const string UnknownType = "UNKNOWN_TYPE";
    public const string NotInPeriod = "NOT_IN_PERIOD";
    public const string NotCounted = "NOT_COUNTED";
    public const string ForeignCurrency = "FOREIGN_CURRENCY";
    public const string DuplicateRow = "DUPLICATE_ROW";

    /// <summary>Money that came in (a credit row) is no payout, whatever category it was given (decision 141).</summary>
    public const string MoneyIn = "MONEY_IN";

    /// <summary>The person's code is no Estonian personal code (a foreign identifier in the register's code column, say):
    /// the line would name a payee e-MTA cannot know, so the payment is refused as such, a residency set by hand
    /// notwithstanding (decision 142). The card's save refuses the same code under the same name.</summary>
    public const string CodeInvalid = "CODE_INVALID";

    /// <summary>The person's code when one stands and it is no Estonian personal code (decision 142), else null. The review's
    /// refusal and the declarer's guard decide by this one test, so no line is ever built under such a code.</summary>
    public static string? InvalidCode(Person person)
    {
        ArgumentNullException.ThrowIfNull(person);
        var code = person.PersonalCode?.Trim();
        return !string.IsNullOrEmpty(code) && !Import.PersonalCode.LooksValid(code) ? code : null;
    }

    /// <summary>Whether an amount is in euros: the statement names no currency (the banks' euro accounts), or names the euro.</summary>
    public static bool IsEuro(string? currency) =>
        string.IsNullOrWhiteSpace(currency) || currency.Trim().Equals("EUR", StringComparison.OrdinalIgnoreCase) || currency.Trim() == "€";

    /// <param name="duplicate">The row is a copy of an earlier source's row (the run's DUPLICATE_ROW): the original counts, this one never.</param>
    /// <param name="twoContracts">The TÖR types in force on the day when the Seaded rule asks for the type (decision 209):
    /// a payment of no type is then asked, TWO_CONTRACTS, not NO_TYPE.</param>
    /// <param name="outsideTor">The payment is of "TÖR-väline palgad" and the person is not in the month's register (or the
    /// month has none): a payment of no type is then refused as OUTSIDE_TOR, not NO_TYPE.</param>
    public static IReadOnlyList<Refusal> Of(Person? person, string? typeCode, decimal amount, bool amountOk, bool inPeriod, int year, TaxRates rates, string? currency = null, bool duplicate = false, Import.Direction direction = Import.Direction.Unknown, IReadOnlyList<string>? twoContracts = null, OutsideTorReason? outsideTor = null, string? foreignA1For = null)
    {
        ArgumentNullException.ThrowIfNull(rates);
        var refusals = new List<Refusal>();
        if (!IsEuro(currency))
        {
            refusals.Add(new Refusal(ForeignCurrency, $"The amount is in {currency!.Trim()}, not in euros; declare the euro amount on a line added by hand.", [currency.Trim()]));
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
            else if (InvalidCode(person) is { } invalid)
            {
                // the declaration would name the person by this code (Person.PayeeId takes it before the MRR code)
                refusals.Add(new Refusal(CodeInvalid, $"The person's code '{invalid}' is not an Estonian personal code (eleven digits with a real birth date); correct it on the card, or clear it and give the MTA non-resident code. A residency set by hand does not stand in for it.", [invalid]));
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
            refusals.Add(twoContracts is { Count: > 0 }
                ? new Refusal(TwoContracts, $"Two employments of different types are in force on the payment's day (TÖR {string.Join(", ", twoContracts)}); choose the payment type, or the card's employment type for every payment.", [.. twoContracts])
                : foreignA1For is { } torType ? new Refusal(ForeignA1Needed, $"The register's employment type {torType} has the social tax paid in another state: set the card's A1 case to a foreign A1 with work in Estonia (and its countries), or choose the payment type by hand.", [torType])
                : outsideTor is { } reason ? OutsideTorRefusal(reason)
                : new Refusal(NoType, "No payment type: the category has no code for this person, or the employment type is unknown."));
        }
        else if (!PaymentTypes.TryGet(typeCode, out var type))
        {
            refusals.Add(new Refusal(UnknownType, $"Payment type {typeCode} is not in the table.", [typeCode]));
        }
        else if (person is not null)
        {
            refusals.AddRange(PaymentChecks.Check(type!, person.FactsFor(year, 1), amount, rates)); // the checks look at no fact the month places
        }

        return refusals;
    }

    /// <summary>The OUTSIDE_TOR refusal, worded for a month searched without a register or a person it does not list.</summary>
    public static Refusal OutsideTorRefusal(OutsideTorReason reason) => new(OutsideTor, reason == OutsideTorReason.NoRegister
        ? "Salary-pattern payment to a person outside TÖR: the month was searched without a TÖR file. Add the month's TÖR file and search again, or give the row a category (Palgad) or a payment type."
        : "Salary-pattern payment to a person the month's TÖR does not list: register the work in TÖR and search again, or give the row a category or a payment type.",
        [reason.ToString()]);

    /// <summary>A notice, not a refusal: a payment type chosen by hand carries other taxes than the type the register's
    /// employment type gives (a 16 on a TÖR 1 salary drops SM, TK, TTK and KP). The hand type stands (decisions 40, 85).</summary>
    public const string TypeTaxesDiffer = "TYPE_TAXES_DIFFER";

    /// <summary>A notice, not a refusal: a salary paid on a TÖR 501 Tasuta töötamine entry. It is declared as a salary,
    /// with no minimum (decision 48).</summary>
    public const string PayOnFreeWork = "PAY_ON_FREE_WORK";

    /// <summary>The TÖR employment type of work done for free (Tasuta töötamine).</summary>
    public const string FreeWorkTorType = "501";

    /// <summary>The notices of a declared payment, in a fixed order: shown only, they refuse nothing and change no figure,
    /// and they are never mixed into the refusals (<see cref="Of"/>).</summary>
    /// <param name="handTypeCode">The type stored by hand (the overlay's or the hand line's), null when none is stored: a
    /// type the rules picked (the card's, the Seaded rule's) gives no notice.</param>
    /// <param name="defaultTypeCode">The type the rules give, null when asked.</param>
    /// <param name="torTypeId">The TÖR type the default was picked from.</param>
    public static IReadOnlyList<string> NoticesOf(string? handTypeCode, string? defaultTypeCode, string? torTypeId, bool salary, decimal amount)
    {
        var notices = new List<string>();
        var hand = handTypeCode?.Trim();
        var byDefault = defaultTypeCode?.Trim();
        // only a salary's default is the TÖR type's; a coded category's own code (haigus 24, üür 57) is no register's word
        if (salary && !string.IsNullOrEmpty(hand) && !string.IsNullOrEmpty(byDefault) && !string.Equals(hand, byDefault, StringComparison.Ordinal)
            && PaymentTypes.TryGet(hand, out var handType) && PaymentTypes.TryGet(byDefault, out var defaultType)
            && TaxesDiffer(handType!, defaultType!))
        {
            notices.Add(TypeTaxesDiffer);
        }

        if (salary && amount > 0m && torTypeId?.Trim() == FreeWorkTorType)
        {
            notices.Add(PayOnFreeWork);
        }

        return notices;
    }

    /// <summary>Whether two types carry different taxes, by the matrix columns alone (never the person's facts, so it never throws).</summary>
    public static bool TaxesDiffer(PaymentType a, PaymentType b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return a.SocialTax != b.SocialTax || a.SocialTaxMinimum != b.SocialTaxMinimum || a.UnemploymentInsurance != b.UnemploymentInsurance
            || a.FundedPension != b.FundedPension || a.IncomeTaxRate != b.IncomeTaxRate;
    }
}

/// <summary>Why a "TÖR-väline palgad" payment has no register behind it (OUTSIDE_TOR).</summary>
public enum OutsideTorReason
{
    /// <summary>The month was searched with no TÖR file.</summary>
    NoRegister,

    /// <summary>The month's TÖR does not list the person.</summary>
    NotListed,
}
