using System.Globalization;
using Microsoft.Data.Sqlite;
using SatellesTSD.Core.Review;
using SatellesTSD.Core.Store;

namespace SatellesTSD.Infrastructure;

/// <summary>The review half of the store: the accountant's decisions, one overlay per statement row and the lines
/// added by hand, kept apart from the run so a re-run never touches them.</summary>
public sealed partial class SqliteStore
{
    public ReviewLine SaveReviewLine(ReviewLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        using var tx = Tx();
        var period = RequirePeriod(line.PeriodId);
        if (period.State != PeriodState.Draft)
        {
            throw new InvalidOperationException($"The period is {period.State}; its review stays as its record.");
        }

        if (line.RowId is { } rowId)
        {
            var linked = Scalar<long>(
                """
                SELECT COUNT(*) FROM statement_row r
                  JOIN import_sheet s ON s.id = r.sheet_id
                  JOIN period_import pi ON pi.file_id = s.file_id
                WHERE r.id = $row AND pi.period_id = $period AND (r.skip_reason IS NULL OR r.skip_reason = $noName)
                """,
                ("$row", G(rowId)), ("$period", G(line.PeriodId)), ("$noName", SatellesTSD.Core.Import.SkipReason.NoName));
            if (linked == 0)
            {
                throw new InvalidOperationException("The statement row is not a payment or payee-less row of a file linked to the period.");
            }
        }
        else
        {
            if (line.Date is null)
            {
                throw new ArgumentException("A line added by hand needs a date.", nameof(line));
            }

            if (line.Amount is not { } amount || amount < 0m || amount != Math.Round(amount, 2))
            {
                throw new ArgumentException("A line added by hand needs an amount of whole cents, not below 0.", nameof(line));
            }

            if (string.IsNullOrWhiteSpace(line.Payee))
            {
                throw new ArgumentException("A line added by hand needs a payee.", nameof(line));
            }
        }

        if (line.PersonId is { } personId)
        {
            var company = Scalar<string?>("SELECT company_id FROM person WHERE id = $id", ("$id", G(personId)))
                ?? throw new KeyNotFoundException($"Person {personId} is not in the store.");
            if (company != G(period.CompanyId))
            {
                throw new InvalidOperationException("The person belongs to another company than the period.");
            }
        }

        var id = line.Id;
        if (id == Guid.Empty)
        {
            var earlier = line.RowId is { } r
                ? Scalar<string?>("SELECT id FROM review_line WHERE period_id = $period AND row_id = $row", ("$period", G(line.PeriodId)), ("$row", G(r)))
                : null;
            id = earlier is null ? Guid.NewGuid() : Guid.Parse(earlier);
        }

        var stored = line with
        {
            Id = id,
            Category = Clean(line.Category),
            TypeCode = Clean(line.TypeCode),
            Note = Clean(line.Note),
            Payee = Clean(line.Payee),
            Description = Clean(line.Description),
            UpdatedAt = Now(),
        };

        // an upsert, so an edited line keeps its rowid and with it its place in the list
        Exec(
            """
            INSERT INTO review_line (id, period_id, row_id, person_id, no_person, category, no_category, type_code, declare, note, date, amount, payee, description, updated_at)
            VALUES ($id, $period, $row, $person, $noperson, $category, $nocategory, $type, $declare, $note, $date, $amount, $payee, $description, $at)
            ON CONFLICT(id) DO UPDATE SET
                period_id = excluded.period_id, row_id = excluded.row_id, person_id = excluded.person_id, no_person = excluded.no_person,
                category = excluded.category, no_category = excluded.no_category, type_code = excluded.type_code, declare = excluded.declare,
                note = excluded.note, date = excluded.date, amount = excluded.amount, payee = excluded.payee, description = excluded.description,
                updated_at = excluded.updated_at
            """,
            ("$id", G(stored.Id)), ("$period", G(stored.PeriodId)), ("$row", stored.RowId is { } rid ? G(rid) : null), ("$person", stored.PersonId is { } pid ? G(pid) : null),
            ("$noperson", stored.NoPerson ? 1L : 0L), ("$category", stored.Category), ("$nocategory", stored.NoCategory ? 1L : 0L), ("$type", stored.TypeCode),
            ("$declare", stored.Declare is { } declare ? (declare ? 1L : 0L) : null), ("$note", stored.Note),
            ("$date", D(stored.Date)), ("$amount", stored.Amount?.ToString(CultureInfo.InvariantCulture)), ("$payee", stored.Payee), ("$description", stored.Description), ("$at", T(stored.UpdatedAt)));
        tx.Commit();
        return stored;
    }

    public void DeleteReviewLine(Guid lineId)
    {
        using var tx = Tx();
        var periodId = Scalar<string?>("SELECT period_id FROM review_line WHERE id = $id", ("$id", G(lineId)))
            ?? throw new KeyNotFoundException($"Review line {lineId} is not in the store.");
        var period = RequirePeriod(Guid.Parse(periodId));
        if (period.State != PeriodState.Draft)
        {
            throw new InvalidOperationException($"The period is {period.State}; its review stays as its record.");
        }

        Exec("DELETE FROM review_line WHERE id = $id", ("$id", G(lineId)));
        tx.Commit();
    }

    public IReadOnlyList<ReviewLine> ReviewLines(Guid periodId) =>
        Query(ReviewLineSelect + " WHERE period_id = $period ORDER BY (row_id IS NULL), rowid", ReadReviewLine, ("$period", G(periodId)));

    private const string ReviewLineSelect =
        "SELECT id, period_id, row_id, person_id, category, type_code, declare, note, date, amount, payee, description, updated_at, no_person, no_category FROM review_line";

    private static ReviewLine ReadReviewLine(SqliteDataReader r) => new(
        Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), r.IsDBNull(2) ? null : Guid.Parse(r.GetString(2)), r.IsDBNull(3) ? null : Guid.Parse(r.GetString(3)),
        Str(r, 4), Str(r, 5), r.IsDBNull(6) ? null : r.GetInt64(6) != 0, Str(r, 7), Date(r, 8), Dec(r, 9), Str(r, 10), Str(r, 11), Time(r, 12) ?? default,
        r.GetInt64(13) != 0, r.GetInt64(14) != 0);

    /// <summary>How many decisions, of any period or one, point at rows of a file (the guard behind removal).</summary>
    private long DecisionsOn(Guid fileId, Guid? periodId = null) =>
        Scalar<long>(
            """
            SELECT COUNT(*) FROM review_line l
              JOIN statement_row r ON r.id = l.row_id
              JOIN import_sheet s ON s.id = r.sheet_id
            WHERE s.file_id = $file AND ($period IS NULL OR l.period_id = $period)
            """,
            ("$file", G(fileId)), ("$period", periodId is { } p ? G(p) : null));
}
