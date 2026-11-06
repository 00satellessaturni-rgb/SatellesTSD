using System.Globalization;
using SatellesTSD.Core.Import;
using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Store;

namespace SatellesTSD.Core.Review;

/// <summary>The persons behind payments the register does not know: a row nobody of the register was found for whose
/// category is declared on its own code without being an employee's (Üür, code 57 / 205) is the payee's income, so the
/// payee is a person of the company too — found among the company's persons (bound by the payee's name as the statement
/// writes it, else by the personal code the statement gives, else by the names), created when missing. A payee that is a
/// legal person (OÜ, AS, KÜ …) or not two names is nobody's.</summary>
public static class PayeePersons
{
    /// <summary>The prefix of the binding key: never a register key (a personal code or "LAST|FIRST").</summary>
    public const string KeyPrefix = "payee:";

    /// <summary>The words that make a payee a legal person, compared case-insensitively.</summary>
    private static readonly HashSet<string> LegalForms = new(StringComparer.OrdinalIgnoreCase)
    {
        "OÜ", "OU", "AS", "MTÜ", "MTU", "SA", "TÜ", "TU", "KÜ", "KU", "UÜ", "UU", "FIE", "KORTERIÜHISTU", "SIHTASUTUS",
        "SIA", "UAB", "AB", "OY", "OYJ", "LLC", "LTD", "LIMITED", "GMBH", "INC", "BV", "SRO", "ZAO", "OOO", "SP", "ZOO",
        "BANK", "PANK", "OSAÜHING", "AKTSIASELTS", "MITTETULUNDUSÜHING", "TULUNDUSÜHISTU", "USALDUSÜHING", "UUSALDUSÜHING",
        "KOMMANDIITÜHING", "FONDS", "SELTS", "ÜHISTU", "ÜHING", "KOGUDUS", "VALITSUS", "LINNAVALITSUS", "VALLAVALITSUS",
        "KANTSELEI", "AMET", "RIIGIKASSA", "KINNISVARA", "HALDUS", "IK", "PS", "MB", "IĮ", "VŠĮ", "ŽŪB", "SE", "PLC", "SARL",
        "SAS", "SRL", "SPA", "NV", "APS", "ASA", "KFT", "ООО", "ОАО", "ЗАО", "ПАО", "АО", "ИП", "ТОО", "ЧП",
    };

    /// <summary>Whether a category makes the payee a person of the company: declared on its own code, and not one found
    /// for employees only (Haigusleht's payee is an employee the register knows).</summary>
    public static bool Declares(Category? category) => category is { Code: not null, EmployeeBased: not true };

    /// <summary>Whether a row's payee is its default person by decision 108: nobody of the register found for it, in the
    /// month, its category as it stands (the accountant's, else the run's) one that <see cref="Declares"/>, the payee a
    /// natural person. The review and the lookups share this, so both see the same rows.</summary>
    public static bool IsPayeeRow(MatchHit hit, ReviewLine? overlay, MatchCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(hit);
        ArgumentNullException.ThrowIfNull(catalog);
        var category = overlay is { NoCategory: true } ? null : overlay?.Category ?? (hit.Verdict.Kind == VerdictKind.Matched ? hit.Verdict.Category?.Name : null);
        return hit.PersonKey is null && hit.InPeriod && NamesOf(hit.Row.Counterparty) is not null && Declares(category is null ? null : catalog.Find(category));
    }

    /// <summary>The payee's names when the statement's name is a natural person's: two words or more, no digits, no legal
    /// form; the last word the last name (the banks write "MARI MAASIKAS"), an all-capitals name written as a name.</summary>
    public static (string FirstName, string LastName)? NamesOf(string? payee)
    {
        var tokens = Tokens(payee);
        if (tokens.Count < 2 || Tokens((payee ?? "").Replace(".", "", StringComparison.Ordinal)).Any(LegalForms.Contains) || tokens.Any(t => LegalForms.Contains(t) || t.Any(c => !char.IsLetter(c) && c is not '-' and not '\'')))
        {
            return null;
        }

        var names = tokens.Select(Written).ToList();
        return (string.Join(' ', names.Take(names.Count - 1)), names[^1]);
    }

    /// <summary>The binding key of a payee's name: the words as compared, in the statement's order.</summary>
    public static string KeyOf(string payee) => KeyPrefix + string.Join(' ', Tokens(payee)).ToUpperInvariant();

    /// <summary>The company's person the payee is: bound to the payee's name, else the one with the statement's
    /// personal code, else the one person whose names are the payee's words (in any order) and whose code does not say
    /// otherwise; null when there is none, or two of the same names.</summary>
    public static Person? Find(IReadOnlyList<Person> persons, string payee, string? personalCode) => Find(persons, payee, personalCode, out _);

    /// <param name="ambiguous">Two or more persons have the payee's names and none is bound or coded: nobody is the payee.</param>
    public static Person? Find(IReadOnlyList<Person> persons, string payee, string? personalCode, out bool ambiguous)
    {
        ambiguous = false;
        ArgumentNullException.ThrowIfNull(persons);
        var key = KeyOf(payee);
        if (persons.FirstOrDefault(p => string.Equals(p.RegisterKey, key, StringComparison.Ordinal)) is { } bound)
        {
            return bound;
        }

        var code = CodeOf(personalCode);
        if (code is not null && persons.FirstOrDefault(p => string.Equals(p.PersonalCode, code, StringComparison.Ordinal)) is { } coded)
        {
            return coded;
        }

        var words = Sorted(Tokens(payee));
        var named = persons.Where(p => (code is null || p.PersonalCode is null) && Sorted(Tokens(p.FirstName + " " + p.LastName)).SequenceEqual(words, StringComparer.Ordinal)).Take(2).ToList();
        ambiguous = named.Count > 1;
        return named.Count == 1 ? named[0] : null;
    }

    /// <summary>The company's person the payee is, created when missing (named from the statement, the statement's code
    /// when it is a personal code, no basic exemption — the payee never applied for it here — and no social-tax minimum),
    /// bound to the payee's name so a name or code corrected on the card keeps the link; a codeless person found by names
    /// takes the statement's code. Null when the payee is no natural person, or two persons have its names.</summary>
    /// <param name="persons">The company's persons as already read.</param>
    /// <param name="changed">Whether the store was written (a person created, bound or given the code).</param>
    public static Person? Ensure(IStore store, Guid companyId, string? payee, string? personalCode, IReadOnlyList<Person> persons, out bool changed)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(persons);
        changed = false;
        if (NamesOf(payee) is not { } names)
        {
            return null;
        }

        var key = KeyOf(payee!);
        var code = CodeOf(personalCode);
        var known = Find(persons, payee!, code, out var ambiguous);
        if (ambiguous)
        {
            return null; // two persons of these names: which one is the accountant's to say
        }

        if (known is { } found)
        {
            var next = found;
            if (next.PersonalCode is null && code is not null && !string.Equals(next.RegisterKey, key, StringComparison.Ordinal))
            {
                next = next with { PersonalCode = code }; // as a register that starts writing the code gives it (a bound person whose code was cleared on the card stays cleared)
            }

            if (next.RegisterKey is null)
            {
                next = next with { RegisterKey = key };
            }

            if (next != found)
            {
                store.UpdatePerson(next);
                changed = true;
            }

            return next;
        }

        changed = true;
        return store.AddPerson(Person.New(companyId, names.FirstName, names.LastName, code) with
        {
            RegisterKey = key,
            BasicExemptionApplied = false,
            SocialTaxMinimumApplies = false,
        });
    }

    /// <summary>Whether a person was made for a payee (bound by a payee key) whose words are these names, in any order:
    /// the register that lists the payee later finds them so.</summary>
    public static bool IsPayeeOf(Person person, string lastName, string firstName)
    {
        ArgumentNullException.ThrowIfNull(person);
        return person.RegisterKey?.StartsWith(KeyPrefix, StringComparison.Ordinal) == true
            && Sorted(Tokens(person.RegisterKey[KeyPrefix.Length..])).SequenceEqual(Sorted(Tokens(firstName + " " + lastName)), StringComparer.Ordinal);
    }

    private static string? CodeOf(string? personalCode)
    {
        var code = personalCode?.Trim();
        return PersonalCode.LooksValid(code) ? code : null;
    }

    private static List<string> Tokens(string? text)
    {
        var plain = NameText.Normalize(NameText.Demojibake(text ?? ""));
        foreach (var symbol in NameSearch.StrippedSymbols)
        {
            plain = plain.Replace(symbol, ' ');
        }

        return plain.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static List<string> Sorted(IEnumerable<string> tokens) => tokens.Select(t => t.ToUpperInvariant()).Order(StringComparer.Ordinal).ToList();

    /// <summary>A word as a name is written: an all-capitals word in capitals and small letters ("MAASIKAS" → "Maasikas"),
    /// any other as the statement has it.</summary>
    private static string Written(string word) =>
        word.Any(char.IsLower) ? word : CultureInfo.GetCultureInfo("et-EE").TextInfo.ToTitleCase(word.ToLower(CultureInfo.GetCultureInfo("et-EE")));
}
