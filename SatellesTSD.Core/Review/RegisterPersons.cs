using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Model;
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
    /// <param name="taken">The store persons this run has already given to another of its rows: a codeless person stays
    /// codeless when the row's code is no personal code (decision 142), so the fallbacks by names never hand them to a
    /// namesake row of the same run too — that row gets a person of its own.</param>
    public static Person? Find(IReadOnlyList<Person> persons, SearchPerson person, IReadOnlySet<string>? runKeys = null, IReadOnlySet<Guid>? taken = null)
    {
        ArgumentNullException.ThrowIfNull(persons);
        ArgumentNullException.ThrowIfNull(person);
        if (persons.FirstOrDefault(p => string.Equals(p.RegisterKey, person.Key, StringComparison.Ordinal)) is { } bound)
        {
            return bound;
        }

        bool Free(Person p) => taken?.Contains(p.Id) != true;
        if (person.PersonalCode is { } code)
        {
            // by the code; else the person a codeless register row of the same names made (bound to that names key), so a
            // register that writes the code from some month on keeps the one person — unless that codeless row is in this
            // very run too, when the coded row is a namesake, another person
            var nameKey = SearchPerson.NameKey(person.LastName, person.FirstName);
            return persons.FirstOrDefault(p => string.Equals(p.PersonalCode, code, StringComparison.Ordinal))
                ?? (runKeys?.Contains(nameKey) == true ? null
                    : persons.FirstOrDefault(p => Free(p) && p.PersonalCode is null && string.Equals(p.RegisterKey, nameKey, StringComparison.Ordinal))
                        ?? OnePayee(persons, person, taken));
        }

        return persons.FirstOrDefault(p => Free(p) && p.PersonalCode is null && SearchPerson.NameKey(p.LastName, p.FirstName) == person.Key) ?? OnePayee(persons, person, taken);
    }

    /// <summary>The one codeless person made for a rent payee of these names (decision 108), whom the register now lists:
    /// the same person, given the register's code and kept; two such make none, and one another row of the run took too.</summary>
    private static Person? OnePayee(IReadOnlyList<Person> persons, SearchPerson person, IReadOnlySet<Guid>? taken)
    {
        var payees = persons.Where(p => p.PersonalCode is null && PayeePersons.IsPayeeOf(p, person.LastName, person.FirstName)).Take(2).ToList();
        return payees.Count == 1 && taken?.Contains(payees[0].Id) != true ? payees[0] : null;
    }

    /// <summary>The store person of a register person, bound to the register key, created when missing.</summary>
    /// <param name="persons">The company's persons as already read, to spare a query; read afresh when null.</param>
    /// <param name="runKeys">The register keys of the run (see <see cref="Find"/>).</param>
    /// <param name="taken">The store persons this run has already given to another of its rows (see <see cref="Find"/>).</param>
    public static Person Ensure(IStore store, Guid companyId, MatchPerson registerPerson, IReadOnlyList<Person>? persons = null, IReadOnlySet<string>? runKeys = null, IReadOnlySet<Guid>? taken = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(registerPerson);
        var key = registerPerson.Key;
        if (Find(persons ?? store.Persons(companyId), registerPerson.Person, runKeys, taken) is { } found)
        {
            if (found.PersonalCode is null && PayeePersons.CodeOf(registerPerson.Person.PersonalCode) is { } code && !string.Equals(found.RegisterKey, key, StringComparison.Ordinal))
            {
                // a codeless person the register now writes with the code: the code is theirs from now on (the payee, the
                // lookups); the binding to the names stays, so the earlier month still finds them. A person bound to this
                // very key whose code the accountant cleared on the card keeps it cleared. A code that is no personal code
                // (a foreign identifier in the column) is not adopted: the person stays named by the MRR code, or by
                // nothing, and a code cleared on the card stays cleared (decision 142).
                found = found with { PersonalCode = code };
                store.UpdatePerson(found);
            }

            if (string.Equals(found.RegisterKey, key, StringComparison.Ordinal))
            {
                return found;
            }

            if (found.RegisterKey?.StartsWith(PayeePersons.KeyPrefix, StringComparison.Ordinal) == true && found.TorEmploymentTypeId is null
                && (found.Notes ?? "").Contains(Report.ReportText.PayeeNowEmployee, StringComparison.Ordinal) == false)
            {
                // a person made for a rent payee (decision 108), whom the register lists for the first time: an employee
                // now, with what an employee of the register gets — the employment's type, the social-tax minimum and the
                // basic exemption (a resident's) — said on the card, since the rent months showed neither. Once only: the
                // type written and the card's note mark it (a register row without a type id writes none; the card's "—"
                // clears it), so the accountant's later word on the card stands.
                var employee = found with
                {
                    TorEmploymentTypeId = registerPerson.Entry.TypeId,
                    SocialTaxMinimumApplies = true,
                    BasicExemptionApplied = found.Residency == Residency.Resident,
                    Notes = string.IsNullOrWhiteSpace(found.Notes) ? Report.ReportText.PayeeNowEmployee : found.Notes + Environment.NewLine + Report.ReportText.PayeeNowEmployee,
                };
                store.UpdatePerson(employee);
                return employee;
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
