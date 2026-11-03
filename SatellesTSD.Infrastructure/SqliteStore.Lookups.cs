using Microsoft.Data.Sqlite;
using SatellesTSD.Core.Lookups;

namespace SatellesTSD.Infrastructure;

/// <summary>The lookup half of the store: every answer the registers gave, per person, as it came.</summary>
public sealed partial class SqliteStore
{
    public LookupRecord AddLookup(LookupRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var stored = record with
        {
            Id = record.Id == Guid.Empty ? Guid.NewGuid() : record.Id,
            CheckedAt = record.CheckedAt == default ? Now() : Truncate(record.CheckedAt),
            Failure = Clean(record.Failure),
            Note = Clean(record.Note),
            PersonalCode = Clean(record.PersonalCode),
        };
        using var tx = Tx();
        if (Scalar<long>("SELECT COUNT(*) FROM person WHERE id = $id", ("$id", G(stored.PersonId))) == 0)
        {
            throw new KeyNotFoundException($"Person {stored.PersonId} is not in the store.");
        }

        Exec(
            """
            INSERT INTO lookup (id, person_id, kind, source, checked_at, control_date, personal_code, finding_json, failure, page, note)
            VALUES ($id, $person, $kind, $source, $at, $control, $code, $finding, $failure, $page, $note)
            """,
            ("$id", G(stored.Id)), ("$person", G(stored.PersonId)), ("$kind", stored.Kind.ToString()), ("$source", stored.Source.ToString()), ("$at", T(stored.CheckedAt)),
            ("$control", D(stored.ControlDate)), ("$code", stored.PersonalCode), ("$finding", stored.FindingJson), ("$failure", stored.Failure), ("$page", stored.Page), ("$note", stored.Note));
        tx.Commit();
        return stored;
    }

    public IReadOnlyList<LookupRecord> Lookups(Guid personId) =>
        Query(LookupSelect + " WHERE person_id = $person ORDER BY checked_at, rowid", ReadLookup, ("$person", G(personId)));

    public LookupRecord? LatestLookup(Guid personId, LookupKind kind, DateOnly controlDate) =>
        Query(LookupSelect + " WHERE person_id = $person AND kind = $kind AND control_date = $control ORDER BY checked_at DESC, rowid DESC LIMIT 1", ReadLookup,
            ("$person", G(personId)), ("$kind", kind.ToString()), ("$control", D(controlDate))).SingleOrDefault();

    private const string LookupSelect = "SELECT id, person_id, kind, source, checked_at, control_date, finding_json, failure, page, note, personal_code FROM lookup";

    private static LookupRecord ReadLookup(SqliteDataReader r) => new(
        Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), Enum.Parse<LookupKind>(r.GetString(2)), Enum.Parse<LookupSource>(r.GetString(3)), Time(r, 4) ?? default,
        Date(r, 5) ?? default, Str(r, 6), Str(r, 7), Str(r, 8), Str(r, 9), Str(r, 10));
}
