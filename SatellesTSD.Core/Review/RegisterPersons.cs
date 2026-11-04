using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Store;

namespace SatellesTSD.Core.Review;

/// <summary>The store persons behind a run's register persons — bound by the register key once found (so a code or a
/// name the accountant corrects never splits the person from their register row), else found by personal code, a
/// codeless one by names among the company's codeless persons — created from the register when missing, named as the
/// register writes them, with the employment's type id. The review and the lookups share this, so both see one person.</summary>
public static class RegisterPersons
{
    public static Person? Find(IReadOnlyList<Person> persons, SearchPerson person)
    {
        ArgumentNullException.ThrowIfNull(persons);
        ArgumentNullException.ThrowIfNull(person);
        if (persons.FirstOrDefault(p => string.Equals(p.RegisterKey, person.Key, StringComparison.Ordinal)) is { } bound)
        {
            return bound;
        }

        if (person.PersonalCode is { } code)
        {
            return persons.FirstOrDefault(p => string.Equals(p.PersonalCode, code, StringComparison.Ordinal));
        }

        return persons.FirstOrDefault(p => p.PersonalCode is null && SearchPerson.NameKey(p.LastName, p.FirstName) == person.Key);
    }

    /// <summary>The store person of a register person, bound to the register key, created when missing.</summary>
    /// <param name="persons">The company's persons as already read, to spare a query; read afresh when null.</param>
    public static Person Ensure(IStore store, Guid companyId, MatchPerson registerPerson, IReadOnlyList<Person>? persons = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(registerPerson);
        var key = registerPerson.Key;
        if (Find(persons ?? store.Persons(companyId), registerPerson.Person) is { } found)
        {
            if (string.Equals(found.RegisterKey, key, StringComparison.Ordinal))
            {
                return found;
            }

            // found by code or by names: bound now, so a later correction of either keeps the link
            var bound = found with { RegisterKey = key };
            store.UpdatePerson(bound);
            return bound;
        }

        var entry = registerPerson.Entry;
        // an employee of the register has the basic exemption applied at this payer unless the accountant says otherwise
        return store.AddPerson(Person.New(companyId, entry.FirstName.Trim(), entry.LastName.Trim(), registerPerson.Person.PersonalCode) with { TorEmploymentTypeId = entry.TypeId, RegisterKey = key, BasicExemptionApplied = true });
    }
}
