using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Store;

namespace SatellesTSD.Core.Review;

/// <summary>The store persons behind a run's register persons — bound by the register key once found (so a code or a
/// name the accountant corrects never splits the person from their register row), else found by personal code, a
/// codeless one by names among the company's codeless persons — created from the register when missing, named as the
/// register writes them, with the employment's type id. The review and the lookups share this, so both see one person.</summary>
public static class RegisterPersons
{
    /// <param name="runKeys">The register keys of the run the person comes from: a codeless row of these names still in it
    /// is that person's, so a coded namesake beside it is not given the codeless person.</param>
    public static Person? Find(IReadOnlyList<Person> persons, SearchPerson person, IReadOnlySet<string>? runKeys = null)
    {
        ArgumentNullException.ThrowIfNull(persons);
        ArgumentNullException.ThrowIfNull(person);
        if (persons.FirstOrDefault(p => string.Equals(p.RegisterKey, person.Key, StringComparison.Ordinal)) is { } bound)
        {
            return bound;
        }

        if (person.PersonalCode is { } code)
        {
            // by the code; else the person a codeless register row of the same names made (bound to that names key), so a
            // register that writes the code from some month on keeps the one person — unless that codeless row is in this
            // very run too, when the coded row is a namesake, another person
            var nameKey = SearchPerson.NameKey(person.LastName, person.FirstName);
            return persons.FirstOrDefault(p => string.Equals(p.PersonalCode, code, StringComparison.Ordinal))
                ?? (runKeys?.Contains(nameKey) == true ? null
                    : persons.FirstOrDefault(p => p.PersonalCode is null && string.Equals(p.RegisterKey, nameKey, StringComparison.Ordinal)));
        }

        return persons.FirstOrDefault(p => p.PersonalCode is null && SearchPerson.NameKey(p.LastName, p.FirstName) == person.Key);
    }

    /// <summary>The store person of a register person, bound to the register key, created when missing.</summary>
    /// <param name="persons">The company's persons as already read, to spare a query; read afresh when null.</param>
    /// <param name="runKeys">The register keys of the run (see <see cref="Find"/>).</param>
    public static Person Ensure(IStore store, Guid companyId, MatchPerson registerPerson, IReadOnlyList<Person>? persons = null, IReadOnlySet<string>? runKeys = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(registerPerson);
        var key = registerPerson.Key;
        if (Find(persons ?? store.Persons(companyId), registerPerson.Person, runKeys) is { } found)
        {
            if (found.PersonalCode is null && registerPerson.Person.PersonalCode is { } code && !string.Equals(found.RegisterKey, key, StringComparison.Ordinal))
            {
                // a codeless person the register now writes with the code: the code is theirs from now on (the payee, the
                // lookups); the binding to the names stays, so the earlier month still finds them. A person bound to this
                // very key whose code the accountant cleared on the card keeps it cleared.
                found = found with { PersonalCode = code };
                store.UpdatePerson(found);
            }

            if (string.Equals(found.RegisterKey, key, StringComparison.Ordinal))
            {
                return found;
            }

            if (found.RegisterKey is not null)
            {
                // bound to another key already (another month's register wrote the person otherwise): the first binding
                // stands, so that month still finds the person by it — rebinding would split the person between months
                return found;
            }

            // found by code or by names and never bound: bound now, so a later correction of either keeps the link
            var bound = found with { RegisterKey = key };
            store.UpdatePerson(bound);
            return bound;
        }

        var entry = registerPerson.Entry;
        // an employee of the register has the basic exemption applied at this payer unless the accountant says otherwise
        return store.AddPerson(Person.New(companyId, entry.FirstName.Trim(), entry.LastName.Trim(), registerPerson.Person.PersonalCode) with { TorEmploymentTypeId = entry.TypeId, RegisterKey = key, BasicExemptionApplied = true });
    }
}
