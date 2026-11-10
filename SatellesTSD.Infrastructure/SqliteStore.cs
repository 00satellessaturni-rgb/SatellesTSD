using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using SatellesTSD.Core.Feedback;
using SatellesTSD.Core.Lookups;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Store;
using SatellesTSD.Core.Tax;
using SatellesTSD.Core.Xbrl;

namespace SatellesTSD.Infrastructure;

/// <summary>The firm's store in one SQLite file (or in memory for tests). One open connection, one user at a time;
/// every write runs in a transaction, and <see cref="InTransaction"/> joins several into one. The schema is created on
/// first open and upgraded step by step by <see cref="Migrations"/>; a file from a newer build is refused.</summary>
public sealed partial class SqliteStore : IStore
{
    public static int SchemaVersion => Migrations.Count;

    /// <summary>The v1 JSON shape of the snapshots (facts, result, line) is pinned by a golden test; a positional
    /// parameter added later must carry a default, or old rows read back wrong without a word.</summary>
    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly SqliteConnection _connection;
    private SqliteTransaction? _transaction;

    private SqliteStore(SqliteConnection connection)
    {
        _connection = connection;
    }

    /// <summary>Opens (and creates or upgrades) the store at <paramref name="path"/>. Pooling is off: one desktop user,
    /// one connection, and the file is released the moment the store is disposed.</summary>
    public static SqliteStore Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true, Pooling = false }.ToString());
        connection.Open();
        var store = new SqliteStore(connection) { _openedPath = Path.GetFullPath(path) };
        try
        {
            store.BackUpBeforeUpgrade(path);
            store.EnsureSchema();
        }
        catch
        {
            store.Dispose();
            throw;
        }

        return store;
    }

    /// <summary>A store that lives only as long as this object (tests).</summary>
    public static SqliteStore InMemory()
    {
        var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        connection.Open();
        var store = new SqliteStore(connection);
        store.EnsureSchema();
        return store;
    }

    public void Dispose()
    {
        _transaction?.Dispose();
        _connection.Dispose();
    }

    public void InTransaction(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        using var tx = Tx();
        work();
        tx.Commit();
    }

    // ---------------------------------------------------------------- companies

    public Company AddCompany(string registryCode, string name, string? notes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registryCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var company = new Company(Guid.NewGuid(), registryCode.Trim(), name.Trim(), Clean(notes));
        using var tx = Tx();
        Exec("INSERT INTO company (id, registry_code, name, notes) VALUES ($id, $code, $name, $notes)",
            ("$id", G(company.Id)), ("$code", company.RegistryCode), ("$name", company.Name), ("$notes", company.Notes));
        tx.Commit();
        return company;
    }

    public Company? FindCompany(string registryCode) =>
        Query(CompanySelect + " WHERE registry_code = $code", ReadCompany, ("$code", registryCode.Trim())).SingleOrDefault();

    public IReadOnlyList<Company> Companies() => Query(CompanySelect + " ORDER BY name", ReadCompany);

    public void UpdateCompany(Company company)
    {
        ArgumentNullException.ThrowIfNull(company);
        using var tx = Tx();
        var changed = HasPayerColumn
            ? Exec("UPDATE company SET registry_code = $code, name = $name, notes = $notes, payer_code_type = $payer WHERE id = $id",
                ("$id", G(company.Id)), ("$code", company.RegistryCode.Trim()), ("$name", company.Name.Trim()), ("$notes", Clean(company.Notes)), ("$payer", Clean(company.PayerCodeType)))
            : Exec("UPDATE company SET registry_code = $code, name = $name, notes = $notes WHERE id = $id",
                ("$id", G(company.Id)), ("$code", company.RegistryCode.Trim()), ("$name", company.Name.Trim()), ("$notes", Clean(company.Notes)));
        if (changed == 0)
        {
            throw new KeyNotFoundException($"Company {company.Id} is not in the store.");
        }

        tx.Commit();
    }

    private bool? _hasPayerColumn;

    /// <summary>Whether the company table carries the v10 column: a store opened at an older version (the upgrade tests) has none.</summary>
    private bool HasPayerColumn => _hasPayerColumn ??= Scalar<long>("SELECT COUNT(*) FROM pragma_table_info('company') WHERE name = 'payer_code_type'") > 0;

    private string CompanySelect => HasPayerColumn
        ? "SELECT id, registry_code, name, notes, payer_code_type FROM company"
        : "SELECT id, registry_code, name, notes, NULL AS payer_code_type FROM company";

    private static Company ReadCompany(SqliteDataReader r) => new(Guid.Parse(r.GetString(0)), r.GetString(1), r.GetString(2), Str(r, 3), Str(r, 4));

    // ---------------------------------------------------------------- persons

    public Person AddPerson(Person person)
    {
        ArgumentNullException.ThrowIfNull(person);
        var stored = Normalize(person) with { Id = person.Id == Guid.Empty ? Guid.NewGuid() : person.Id };
        using var tx = Tx();
        Exec(
            """
            INSERT INTO person (id, company_id, first_name, last_name, personal_code, mrr_code, residency, residence_country, residency_checked_on,
                                funded_pension_member, pension_rate, pension_checked_on, on_pension, pension_age_year,
                                exemption_applied, exemption_monthly, minimum_applies, a1_case, a1_country, work_country, treaty_rate, tor_type_id, notes,
                                residency_source, pension_source, register_key, declaration, card_type, facts_control_date)
            VALUES ($id, $company, $first, $last, $code, $mrr, $residency, $country, $rchecked,
                    $member, $rate, $pchecked, $onpension, $pensionyear,
                    $applied, $monthly, $minimum, $a1, $a1country, $work, $treaty, $tor, $notes,
                    $rsource, $psource, $rkey, $declaration, $cardtype, $factsdate)
            """,
            PersonParameters(stored));
        tx.Commit();
        return stored;
    }

    public void UpdatePerson(Person person)
    {
        ArgumentNullException.ThrowIfNull(person);
        using var tx = Tx();
        var changed = Exec(
            """
            UPDATE person SET company_id = $company, first_name = $first, last_name = $last, personal_code = $code, mrr_code = $mrr,
                residency = $residency, residence_country = $country, residency_checked_on = $rchecked,
                funded_pension_member = $member, pension_rate = $rate, pension_checked_on = $pchecked, on_pension = $onpension, pension_age_year = $pensionyear,
                exemption_applied = $applied, exemption_monthly = $monthly, minimum_applies = $minimum,
                a1_case = $a1, a1_country = $a1country, work_country = $work, treaty_rate = $treaty, tor_type_id = $tor, notes = $notes,
                residency_source = $rsource, pension_source = $psource, register_key = $rkey, declaration = $declaration, card_type = $cardtype, facts_control_date = $factsdate
            WHERE id = $id
            """,
            PersonParameters(Normalize(person)));
        if (changed == 0)
        {
            throw new KeyNotFoundException($"Person {person.Id} is not in the store.");
        }

        tx.Commit();
    }

    public IReadOnlyList<Person> Persons(Guid companyId) =>
        Query(PersonSelect + " WHERE company_id = $company ORDER BY last_name, first_name", ReadPerson, ("$company", G(companyId)));

    public Person? FindPerson(Guid companyId, string personalCode) =>
        Query(PersonSelect + " WHERE company_id = $company AND personal_code = $code", ReadPerson, ("$company", G(companyId)), ("$code", personalCode.Trim())).SingleOrDefault();

    /// <summary>Trimmed text; an empty code is no code (NULL), so two code-less people do not collide on the unique index.</summary>
    private static Person Normalize(Person p) => p with
    {
        FirstName = p.FirstName.Trim(),
        LastName = p.LastName.Trim(),
        PersonalCode = Clean(p.PersonalCode),
        MrrCode = Clean(p.MrrCode),
        ResidenceCountry = Clean(p.ResidenceCountry),
        A1Country = Clean(p.A1Country),
        WorkCountry = Clean(p.WorkCountry),
        TorEmploymentTypeId = Clean(p.TorEmploymentTypeId),
        Notes = Clean(p.Notes),
        RegisterKey = Clean(p.RegisterKey),
    };

    private const string PersonSelect =
        """
        SELECT id, company_id, first_name, last_name, personal_code, mrr_code, residency, residence_country, residency_checked_on,
               funded_pension_member, pension_rate, pension_checked_on, on_pension, pension_age_year,
               exemption_applied, exemption_monthly, minimum_applies, a1_case, a1_country, work_country, treaty_rate, tor_type_id, notes,
               residency_source, pension_source, register_key, declaration, card_type, facts_control_date
        FROM person
        """;

    private static (string, object?)[] PersonParameters(Person p) =>
    [
        ("$id", G(p.Id)), ("$company", G(p.CompanyId)), ("$first", p.FirstName), ("$last", p.LastName), ("$code", p.PersonalCode), ("$mrr", p.MrrCode),
        ("$residency", p.Residency.ToString()), ("$country", p.ResidenceCountry), ("$rchecked", D(p.ResidencyCheckedOn)),
        ("$member", p.FundedPensionMember is null ? null : p.FundedPensionMember.Value ? 1 : 0), ("$rate", N(p.PensionRate)), ("$pchecked", D(p.PensionCheckedOn)),
        ("$onpension", p.OnOldAgeOrFlexiblePension ? 1 : 0), ("$pensionyear", p.PensionAgeYear),
        ("$applied", p.BasicExemptionApplied ? 1 : 0), ("$monthly", N(p.BasicExemptionMonthly)), ("$minimum", p.SocialTaxMinimumApplies ? 1 : 0),
        ("$a1", p.A1.ToString()), ("$a1country", p.A1Country), ("$work", p.WorkCountry), ("$treaty", N(p.TreatyIncomeTaxRate)), ("$tor", p.TorEmploymentTypeId), ("$notes", p.Notes),
        ("$rsource", p.ResidencySource.ToString()), ("$psource", p.PensionSource.ToString()), ("$rkey", p.RegisterKey), ("$declaration", p.Declaration.ToString()), ("$cardtype", p.TypeFromCard ? 1 : 0), ("$factsdate", D(p.FactsControlDate)),
    ];

    private static Person ReadPerson(SqliteDataReader r) => new(
        Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), r.GetString(2), r.GetString(3), Str(r, 4), Str(r, 5),
        Enum.Parse<Residency>(r.GetString(6)), Str(r, 7), Date(r, 8),
        r.IsDBNull(9) ? null : r.GetInt64(9) != 0, Dec(r, 10), Date(r, 11), r.GetInt64(12) != 0, r.IsDBNull(13) ? null : (int)r.GetInt64(13),
        r.GetInt64(14) != 0, Dec(r, 15), r.GetInt64(16) != 0, Enum.Parse<A1Case>(r.GetString(17)), Str(r, 18), Str(r, 19), Dec(r, 20), Str(r, 21), Str(r, 22),
        Enum.Parse<LookupSource>(r.GetString(23)), Enum.Parse<LookupSource>(r.GetString(24)), Str(r, 25),
        Enum.Parse<DeclarationChoice>(r.GetString(26)), r.GetInt64(27) != 0, Date(r, 28));

    // ---------------------------------------------------------------- periods

    public Period GetOrCreatePeriod(Guid companyId, int year, int month)
    {
        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month));
        }

        using var tx = Tx();
        var existing = Query(PeriodSelect + " WHERE company_id = $company AND year = $year AND month = $month", ReadPeriod,
            ("$company", G(companyId)), ("$year", year), ("$month", month)).SingleOrDefault();
        if (existing is not null)
        {
            tx.Commit();
            return existing;
        }

        var period = new Period(Guid.NewGuid(), companyId, year, month, PeriodState.Draft, null, null, null);
        Exec("INSERT INTO period (id, company_id, year, month, state) VALUES ($id, $company, $year, $month, $state)",
            ("$id", G(period.Id)), ("$company", G(companyId)), ("$year", year), ("$month", month), ("$state", period.State.ToString()));
        tx.Commit();
        return period;
    }

    public void SetPeriodState(Guid periodId, PeriodState state, DateTime at)
    {
        var column = state switch
        {
            PeriodState.Exported => "exported_at",
            PeriodState.Uploaded => "uploaded_at",
            PeriodState.Confirmed => "confirmed_at",
            _ => null,
        };
        using var tx = Tx();
        var changed = column is null
            ? Exec("UPDATE period SET state = $state WHERE id = $id", ("$id", G(periodId)), ("$state", state.ToString()))
            : Exec($"UPDATE period SET state = $state, {column} = $at WHERE id = $id", ("$id", G(periodId)), ("$state", state.ToString()), ("$at", T(at)));
        if (changed == 0)
        {
            throw new KeyNotFoundException($"Period {periodId} is not in the store.");
        }

        tx.Commit();
    }

    public IReadOnlyList<Period> Periods(Guid companyId) =>
        Query(PeriodSelect + " WHERE company_id = $company ORDER BY year, month", ReadPeriod, ("$company", G(companyId)));

    private const string PeriodSelect = "SELECT id, company_id, year, month, state, exported_at, uploaded_at, confirmed_at FROM period";

    private static Period ReadPeriod(SqliteDataReader r) => new(
        Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), (int)r.GetInt64(2), (int)r.GetInt64(3), Enum.Parse<PeriodState>(r.GetString(4)),
        Time(r, 5), Time(r, 6), Time(r, 7));

    private Period RequirePeriod(Guid periodId) =>
        Query(PeriodSelect + " WHERE id = $id", ReadPeriod, ("$id", G(periodId))).SingleOrDefault()
        ?? throw new KeyNotFoundException($"Period {periodId} is not in the store.");

    // ---------------------------------------------------------------- keys and payments

    public string NewLineNumber(Guid periodId, Section section)
    {
        using var tx = Tx();
        var period = RequirePeriod(periodId);
        var company = Query(CompanySelect + " WHERE id = $id", ReadCompany, ("$id", G(period.CompanyId))).Single();
        var next = Scalar<long?>("SELECT next FROM line_counter WHERE period_id = $period AND section = $section", ("$period", G(periodId)), ("$section", section.ToString())) ?? 1;
        SetCounter(periodId, section, next + 1);
        tx.Commit();
        return KeyFor(company.RegistryCode, period, section, next);
    }

    private static string KeyFor(string registryCode, Period period, Section section, long number) =>
        $"{registryCode}-{period.Year:0000}{period.Month:00}-{SectionCode(section)}-{number:000}";

    private static string SectionCode(Section section) => section == Section.EE0201001 ? "01" : "02";

    private void SetCounter(Guid periodId, Section section, long next) =>
        Exec("INSERT INTO line_counter (period_id, section, next) VALUES ($period, $section, $next) ON CONFLICT(period_id, section) DO UPDATE SET next = $next",
            ("$period", G(periodId)), ("$section", section.ToString()), ("$next", next));

    /// <summary>A caller-supplied key in the counter's own format moves the counter past it.</summary>
    private void ReserveSuppliedKey(Period period, Section section, string lineNumber)
    {
        var company = Query(CompanySelect + " WHERE id = $id", ReadCompany, ("$id", G(period.CompanyId))).Single();
        var match = CounterKey().Match(lineNumber);
        if (!match.Success || match.Groups["reg"].Value != company.RegistryCode
            || match.Groups["ym"].Value != $"{period.Year:0000}{period.Month:00}" || match.Groups["sec"].Value != SectionCode(section))
        {
            return;
        }

        var number = long.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture);
        var next = Scalar<long?>("SELECT next FROM line_counter WHERE period_id = $period AND section = $section", ("$period", G(period.Id)), ("$section", section.ToString())) ?? 1;
        if (number >= next)
        {
            SetCounter(period.Id, section, number + 1);
        }
    }

    [GeneratedRegex(@"^(?<reg>[^-]+)-(?<ym>\d{6})-(?<sec>01|02)-(?<n>\d+)$")]
    private static partial Regex CounterKey();

    /// <summary>A bank row feeds one line: a second key pointing at the same row is refused (the versions of one key
    /// share it). A dead key — one whose T was accepted — holds nothing any more, nor does a key every version of which
    /// was refused black (it never reached the server): the payment's new key takes the row.</summary>
    private void RequireRowFree(Guid rowId, Guid periodId, Section section, string lineNumber)
    {
        var other = Scalar<string?>(
            """
            SELECT p.section || ' ' || p.line_number FROM payment p
            WHERE p.source_row_id = $row AND NOT (p.period_id = $period AND p.section = $section AND p.line_number = $line)
              AND NOT EXISTS (SELECT 1 FROM payment d WHERE d.period_id = p.period_id AND d.section = p.section AND d.line_number = p.line_number AND d.action = $t AND d.status = $cancelled)
              AND EXISTS (SELECT 1 FROM payment d WHERE d.period_id = p.period_id AND d.section = p.section AND d.line_number = p.line_number AND d.status <> $rejected)
            LIMIT 1
            """,
            ("$row", G(rowId)), ("$period", G(periodId)), ("$section", section.ToString()), ("$line", lineNumber), ("$t", LineAction.T.ToString()), ("$cancelled", PaymentStatus.Cancelled.ToString()),
            ("$rejected", PaymentStatus.Rejected.ToString()));
        if (other is not null)
        {
            throw new InvalidOperationException($"The bank row already feeds line {other}; a row is declared once.");
        }
    }

    public StoredPayment AddPayment(StoredPayment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ValidateShape(payment);
        using var tx = Tx();
        var period = RequirePeriod(payment.PeriodId);
        var history = History(payment.PeriodId, payment.Section, payment.LineNumber);
        LineKeyRules.Check(payment.Action, history);

        // every version of a key keeps its bank row: a new version without one inherits it, one with another is refused
        var linkedRow = history.Select(v => v.SourceRowId).LastOrDefault(id => id is not null);
        if (payment.SourceRowId is { } given && linkedRow is { } had && given != had)
        {
            throw new InvalidOperationException("The key already points at another bank row; every version of a key keeps its row.");
        }

        var sourceRowId = payment.SourceRowId ?? linkedRow;
        if (sourceRowId is { } row && linkedRow is null)
        {
            RequireRowOf(row, period.CompanyId);
            RequireRowFree(row, payment.PeriodId, payment.Section, payment.LineNumber);
        }

        // and its hand line, the same way
        var linkedLine = history.Select(v => v.ReviewLineId).LastOrDefault(id => id is not null);
        if (payment.ReviewLineId is { } givenLine && linkedLine is { } hadLine && givenLine != hadLine)
        {
            throw new InvalidOperationException("The key already points at another hand line; every version of a key keeps its line.");
        }

        ReserveSuppliedKey(period, payment.Section, payment.LineNumber); // after every refusal, so a refused line never moves the counter
        var stored = payment with
        {
            Id = Guid.NewGuid(),
            Version = history.Count == 0 ? 1 : history[^1].Version + 1,
            Status = PaymentStatus.Draft,
            SubmissionId = null,
            CreatedAt = Now(),
            SourceRowId = sourceRowId,
            ReviewLineId = payment.ReviewLineId ?? linkedLine,
        };
        InsertPayment(stored);
        tx.Commit();
        return stored;
    }

    /// <summary>The statement row exists, is a payment row or a row without a payee (not a blank row or a pasted
    /// header) and belongs to the company — domain errors, not a foreign-key failure.</summary>
    private void RequireRowOf(Guid rowId, Guid companyId)
    {
        var row = Query(
            "SELECT f.company_id, r.skip_reason FROM statement_row r JOIN import_sheet s ON s.id = r.sheet_id JOIN import_file f ON f.id = s.file_id WHERE r.id = $id",
            r => (Owner: r.GetString(0), Skip: Str(r, 1)), ("$id", G(rowId))).SingleOrDefault();
        if (row.Owner is null)
        {
            throw new KeyNotFoundException($"Statement row {rowId} is not in the store.");
        }

        if (row.Owner != G(companyId))
        {
            throw new InvalidOperationException("The bank row belongs to another company's file.");
        }

        // a row without a payee may be declared once the accountant has named the person it went to
        if (row.Skip is not (null or SatellesTSD.Core.Import.SkipReason.NoName))
        {
            throw new InvalidOperationException($"The bank row is not a payment row ({row.Skip}); a declared line cannot come from it.");
        }
    }

    public StoredPayment ReplaceDraft(Guid paymentId, StoredPayment replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        ValidateShape(replacement);
        using var tx = Tx();
        var current = Query(PaymentSelect + " WHERE id = $id", ReadPayment, ("$id", G(paymentId))).SingleOrDefault()
            ?? throw new KeyNotFoundException($"Payment {paymentId} is not in the store.");
        if (current.Status != PaymentStatus.Draft)
        {
            throw new InvalidOperationException($"Only a Draft version can be replaced; this one is {current.Status}.");
        }

        if (replacement.PeriodId != current.PeriodId || replacement.Section != current.Section || replacement.LineNumber != current.LineNumber || replacement.Action != current.Action)
        {
            throw new InvalidOperationException("A replacement keeps the draft's period, section, key and action; for anything else delete the draft and add a new line.");
        }

        // a draft may be re-pointed at another bank row of the company while no other version of its key holds one (a key keeps its row);
        // a replacement without a row keeps the draft's
        var sourceRowId = replacement.SourceRowId ?? current.SourceRowId;
        if (replacement.SourceRowId is { } fresh && fresh != current.SourceRowId)
        {
            var keyRow = History(current.PeriodId, current.Section, current.LineNumber).Where(v => v.Id != current.Id).Select(v => v.SourceRowId).LastOrDefault(id => id is not null);
            if (keyRow is { } had && fresh != had)
            {
                throw new InvalidOperationException("The key already points at another bank row; every version of a key keeps its row.");
            }

            RequireRowOf(fresh, RequirePeriod(current.PeriodId).CompanyId);
            RequireRowFree(fresh, current.PeriodId, current.Section, current.LineNumber);
        }

        var stored = replacement with { Id = current.Id, Version = current.Version, Status = PaymentStatus.Draft, SubmissionId = null, CreatedAt = Now(), SourceRowId = sourceRowId, ReviewLineId = replacement.ReviewLineId ?? current.ReviewLineId };
        Exec("DELETE FROM payment WHERE id = $id", ("$id", G(paymentId)));
        InsertPayment(stored);
        tx.Commit();
        return stored;
    }

    private static void ValidateShape(StoredPayment payment)
    {
        if (string.IsNullOrWhiteSpace(payment.LineNumber) || payment.LineNumber != payment.Line.LineNumber)
        {
            throw new ArgumentException("The payment's LineNumber must equal its Line.LineNumber.", nameof(payment));
        }

        if (payment.Action != payment.Line.Action)
        {
            throw new ArgumentException("The payment's Action must equal its Line.Action.", nameof(payment));
        }

        if (payment.Action != LineAction.T && (payment.Facts is null || payment.Result is null))
        {
            throw new ArgumentException("An R or M needs the facts and the result it was computed from.", nameof(payment));
        }
    }

    private void InsertPayment(StoredPayment stored) =>
        Exec(
            """
            INSERT INTO payment (id, period_id, person_id, section, line_number, version, action, status, type_code, payment_date, gross, net_paid, source_ref,
                                 payee_code, payee_description, facts_json, result_json, line_json, submission_id, created_at, source_row_id, review_line_id)
            VALUES ($id, $period, $person, $section, $line, $version, $action, $status, $type, $date, $gross, $net, $source,
                    $payee, $payeeType, $facts, $result, $lineJson, NULL, $created, $sourceRow, $reviewLine)
            """,
            ("$id", G(stored.Id)), ("$period", G(stored.PeriodId)), ("$person", stored.PersonId is { } p ? G(p) : null), ("$section", stored.Section.ToString()),
            ("$line", stored.LineNumber), ("$version", stored.Version), ("$action", stored.Action.ToString()), ("$status", stored.Status.ToString()),
            ("$type", stored.Line.TypeCode), ("$date", D(stored.Line.DocumentDate)), ("$gross", N(stored.Line.Amount)), ("$net", N(stored.NetPaid)), ("$source", Clean(stored.SourceRef)),
            ("$payee", stored.Line.Payee?.Code), ("$payeeType", stored.Line.Payee?.Description),
            ("$facts", stored.Facts is null ? null : JsonSerializer.Serialize(stored.Facts, Json)),
            ("$result", stored.Result is null ? null : JsonSerializer.Serialize(stored.Result, Json)),
            ("$lineJson", JsonSerializer.Serialize(stored.Line, Json)), ("$created", T(stored.CreatedAt)), ("$sourceRow", stored.SourceRowId is { } row ? G(row) : null),
            ("$reviewLine", stored.ReviewLineId is { } reviewLine ? G(reviewLine) : null));

    public IReadOnlyList<StoredPayment> Payments(Guid periodId)
    {
        var all = Query(PaymentSelect + " WHERE period_id = $period ORDER BY section, line_number, version", ReadPayment, ("$period", G(periodId)));
        return all
            .GroupBy(p => (p.Section, p.LineNumber))
            .Select(versions => versions.LastOrDefault(v => v.Status != PaymentStatus.Rejected) ?? versions.Last())
            .ToList();
    }

    public IReadOnlyList<StoredPayment> History(Guid periodId, Section section, string lineNumber) =>
        Query(PaymentSelect + " WHERE period_id = $period AND section = $section AND line_number = $line ORDER BY version", ReadPayment,
            ("$period", G(periodId)), ("$section", section.ToString()), ("$line", lineNumber));

    public void DeleteDraft(Guid paymentId)
    {
        using var tx = Tx();
        var changed = Exec("DELETE FROM payment WHERE id = $id AND status = $draft", ("$id", G(paymentId)), ("$draft", PaymentStatus.Draft.ToString()));
        if (changed == 0)
        {
            throw new InvalidOperationException("Only a Draft version can be deleted; anything sent is history.");
        }

        tx.Commit();
    }

    private const string PaymentSelect =
        """
        SELECT id, period_id, person_id, section, line_number, version, action, status, facts_json, result_json, line_json, net_paid, source_ref, submission_id, created_at, source_row_id, review_line_id
        FROM payment
        """;

    private static StoredPayment ReadPayment(SqliteDataReader r) => new(
        Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), r.IsDBNull(2) ? null : Guid.Parse(r.GetString(2)),
        Enum.Parse<Section>(r.GetString(3)), r.GetString(4), (int)r.GetInt64(5), Enum.Parse<LineAction>(r.GetString(6)), Enum.Parse<PaymentStatus>(r.GetString(7)),
        JsonSerializer.Deserialize<PaymentLine>(r.GetString(10), Json)!,
        r.IsDBNull(8) ? null : JsonSerializer.Deserialize<PayeeTaxFacts>(r.GetString(8), Json),
        r.IsDBNull(9) ? null : JsonSerializer.Deserialize<TaxResult>(r.GetString(9), Json),
        Dec(r, 11), Str(r, 12), r.IsDBNull(13) ? null : Guid.Parse(r.GetString(13)), Time(r, 14) ?? default,
        r.IsDBNull(15) ? null : Guid.Parse(r.GetString(15)), r.IsDBNull(16) ? null : Guid.Parse(r.GetString(16)));

    // ---------------------------------------------------------------- submissions and feedback

    public Submission AddSubmission(Guid periodId, string fileName, string uniqueId, IReadOnlyCollection<Guid> paymentIds, DateTime at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(uniqueId);
        ArgumentNullException.ThrowIfNull(paymentIds);
        if (paymentIds.Count == 0)
        {
            throw new ArgumentException("A file carries at least one line.", nameof(paymentIds));
        }

        using var tx = Tx();
        var submission = new Submission(Guid.NewGuid(), periodId, Truncate(at), fileName, uniqueId, null, null, null, []);
        Exec("INSERT INTO submission (id, period_id, created_at, file_name, unique_id, general_json) VALUES ($id, $period, $at, $file, $unique, '[]')",
            ("$id", G(submission.Id)), ("$period", G(periodId)), ("$at", T(submission.CreatedAt)), ("$file", fileName), ("$unique", uniqueId));
        foreach (var paymentId in paymentIds)
        {
            var changed = Exec("UPDATE payment SET status = $sent, submission_id = $submission WHERE id = $id AND period_id = $period AND status = $draft",
                ("$id", G(paymentId)), ("$period", G(periodId)), ("$sent", PaymentStatus.Sent.ToString()), ("$submission", G(submission.Id)), ("$draft", PaymentStatus.Draft.ToString()));
            if (changed == 0)
            {
                throw new InvalidOperationException($"Payment {paymentId} is not a Draft of period {periodId}; only drafts go into a new file.");
            }
        }

        tx.Commit();
        return submission;
    }

    public void WithdrawSubmission(Guid submissionId)
    {
        using var tx = Tx();
        var submission = Query(SubmissionSelect + " WHERE id = $id", ReadSubmission, ("$id", G(submissionId))).SingleOrDefault()
            ?? throw new KeyNotFoundException($"Submission {submissionId} is not in the store.");
        bool answered = submission.Result is not null
            || Scalar<long>("SELECT COUNT(*) FROM line_feedback WHERE submission_id = $id", ("$id", G(submissionId))) > 0
            || Scalar<long>("SELECT COUNT(*) FROM payment WHERE submission_id = $id AND status <> $sent", ("$id", G(submissionId)), ("$sent", PaymentStatus.Sent.ToString())) > 0;
        if (answered)
        {
            throw new InvalidOperationException("The submission already has a verdict on it (feedback or a line resolved by hand); it was uploaded and cannot be withdrawn.");
        }

        Exec("UPDATE payment SET status = $draft, submission_id = NULL WHERE submission_id = $id AND status = $sent",
            ("$id", G(submissionId)), ("$draft", PaymentStatus.Draft.ToString()), ("$sent", PaymentStatus.Sent.ToString()));
        Exec("DELETE FROM submission WHERE id = $id", ("$id", G(submissionId)));
        tx.Commit();
    }

    public void SetSubmissionUuid(Guid submissionId, string uuid)
    {
        using var tx = Tx();
        if (Exec("UPDATE submission SET uuid = $uuid WHERE id = $id", ("$id", G(submissionId)), ("$uuid", uuid)) == 0)
        {
            throw new KeyNotFoundException($"Submission {submissionId} is not in the store.");
        }

        tx.Commit();
    }

    public IReadOnlyList<LineFeedback> RecordFeedback(Guid submissionId, FeedbackReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.Kind != ReportKind.Submission)
        {
            throw new InvalidOperationException("This is a confirmation report, not a submission's feedback; it says nothing about individual lines.");
        }

        using var tx = Tx();
        var sent = Query(PaymentSelect + " WHERE submission_id = $submission", ReadPayment, ("$submission", G(submissionId)));
        if (sent.Count == 0)
        {
            throw new KeyNotFoundException($"Submission {submissionId} has no lines (or is not in the store).");
        }

        var byKey = sent.ToDictionary(p => new LineKey(p.Section.ToString(), p.LineNumber));
        var outcomes = report.Outcomes(sent.ToDictionary(p => new LineKey(p.Section.ToString(), p.LineNumber), p => p.Action));
        var foreign = outcomes.Where(o => !byKey.ContainsKey(new LineKey(o.EntryNumber, o.LineNumber))).Select(o => $"{o.EntryNumber}/{o.LineNumber}").ToList();
        if (foreign.Count > 0)
        {
            throw new InvalidOperationException($"The report names lines this file did not carry ({string.Join(", ", foreign)}); it belongs to another submission.");
        }

        // a report read within the millisecond of the one before (two reads of the same file in a row) gets the next
        // millisecond: the feedback of a submission is a sequence, and its stamps are its order
        var recordedAt = Now();
        var lastStamp = Scalar<string?>("SELECT MAX(recorded_at) FROM line_feedback WHERE submission_id = $submission", ("$submission", G(submissionId)));
        if (lastStamp is not null && DateTime.ParseExact(lastStamp, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal) is { } last && last >= recordedAt)
        {
            recordedAt = last.AddMilliseconds(1);
        }

        Exec("UPDATE submission SET result = $result, feedback_source = $source, general_json = $general WHERE id = $id",
            ("$id", G(submissionId)), ("$result", report.Result.ToString()), ("$source", report.Source), ("$general", JsonSerializer.Serialize(report.GeneralValidations, Json)));

        var feedback = new List<LineFeedback>();
        if (report.FileRefused)
        {
            // Nothing in the file was saved; only the versions still awaiting a verdict learn anything from that.
            var fileMessages = report.FileValidations
                .Select(v => new ResponseItem(v.Code, "ERROR_BLACK", v.Description, v.DescriptionEn, v.DescriptionRu))
                .ToList();
            foreach (var payment in sent.Where(p => p.Pending))
            {
                SetStatus(payment.Id, PaymentStatus.Rejected);
                feedback.Add(AppendFeedback(new LineFeedback(payment.Id, submissionId, recordedAt, ValidationLevel.Error, null, Severity.Black,
                    FeedbackReport.ActionFor(Severity.Black, payment.Action, []), fileMessages, new Dictionary<string, decimal>(), [])));
            }

            tx.Commit();
            return feedback;
        }

        foreach (var outcome in outcomes)
        {
            var payment = byKey[new LineKey(outcome.EntryNumber, outcome.LineNumber)];
            var codes = outcome.Messages.Select(m => m.Code).ToHashSet(StringComparer.Ordinal);
            var wasPending = payment.Pending; // a report read again onto a version long settled says nothing new of the key
            // a refused version the report now shows saved — the same file uploaded again after a file-level refusal (an
            // open declaration, say) was put right in e-MTA — takes MTA's word: it stands on the server. Not when a later
            // version of the key stands (a draft built since): that one speaks for the key now; a later version that is itself
            // refused speaks for nothing (decision 151).
            var lifted = payment.Status == PaymentStatus.Rejected
                && outcome.Severity is Severity.None or Severity.Warning or Severity.Blue or Severity.Red
                && !History(payment.PeriodId, payment.Section, payment.LineNumber).Any(v => v.Version > payment.Version && v.Status != PaymentStatus.Rejected);
            if (payment.Pending || lifted)
            {
                var status = outcome.Severity switch
                {
                    Severity.None or Severity.Warning => payment.Action == LineAction.T ? PaymentStatus.Cancelled : PaymentStatus.Accepted,
                    Severity.Blue or Severity.Red => payment.Action == LineAction.T ? PaymentStatus.Review : PaymentStatus.NeedsChange,
                    // "R on a known key": the key IS on the server; this very version may have got there with an earlier upload.
                    Severity.Black when codes.Contains(FeedbackReport.RuleNewOnExistingKey) => PaymentStatus.Review,
                    Severity.Black => PaymentStatus.Rejected,
                    _ => PaymentStatus.Review,
                };
                SetStatus(payment.Id, status);
            }

            if (wasPending && outcome.Severity == Severity.Black && codes.Contains(FeedbackReport.RuleChangeOnUnknownKey))
            {
                // "M or T on an unknown key": the key is not on the server, whatever an earlier pending version presumed —
                // or an earlier version settled as on the server (by the accountant's word, or a report read onto the wrong
                // file): MTA has just said the key is not there, so the next build sends an R under it again. A Cancelled
                // version (a dead key) and any later version stand as they are.
                foreach (var earlier in History(payment.PeriodId, payment.Section, payment.LineNumber)
                    .Where(v => v.Id != payment.Id && (v.Pending || (v.Version < payment.Version && v.Status is PaymentStatus.Accepted or PaymentStatus.NeedsChange))))
                {
                    SetStatus(earlier.Id, PaymentStatus.Rejected);
                }
            }

            feedback.Add(AppendFeedback(new LineFeedback(payment.Id, submissionId, recordedAt, outcome.Level, outcome.Counter, outcome.Severity, outcome.NextAction,
                outcome.Messages, outcome.ServerTaxes, outcome.Unclassified)));
        }

        tx.Commit();
        return feedback;
    }

    public void ResolveLine(Guid paymentId, bool onServer)
    {
        using var tx = Tx();
        var payment = Query(PaymentSelect + " WHERE id = $id", ReadPayment, ("$id", G(paymentId))).SingleOrDefault()
            ?? throw new KeyNotFoundException($"Payment {paymentId} is not in the store.");
        if (!payment.Pending)
        {
            throw new InvalidOperationException($"Only a Sent or Review version can be resolved by hand; this one is {payment.Status}.");
        }

        var status = !onServer ? PaymentStatus.Rejected : payment.Action == LineAction.T ? PaymentStatus.Cancelled : PaymentStatus.Accepted;
        SetStatus(payment.Id, status);
        if (!onServer)
        {
            // the mirror of the feedback's "_5" rule: a version refused by hand never reached the server, so a later
            // pending M or T on the key presumed it wrongly — unless another version holds the key on the server
            var history = History(payment.PeriodId, payment.Section, payment.LineNumber);
            var held = history.Any(v => v.Id != payment.Id && v.Status is PaymentStatus.Accepted or PaymentStatus.NeedsChange);
            if (!held)
            {
                foreach (var later in history.Where(v => v.Version > payment.Version && v.Pending))
                {
                    SetStatus(later.Id, PaymentStatus.Rejected);
                }
            }
        }

        tx.Commit();
    }

    private LineFeedback AppendFeedback(LineFeedback row)
    {
        Exec(
            """
            INSERT INTO line_feedback (payment_id, submission_id, recorded_at, level, counter, severity, next_action, messages_json, server_taxes_json, unclassified_json)
            VALUES ($payment, $submission, $at, $level, $counter, $severity, $action, $messages, $taxes, $unclassified)
            """,
            ("$payment", G(row.PaymentId)), ("$submission", G(row.SubmissionId)), ("$at", T(row.RecordedAt)), ("$level", row.Level.ToString()), ("$counter", row.Counter),
            ("$severity", row.Severity.ToString()), ("$action", row.NextAction.ToString()),
            ("$messages", JsonSerializer.Serialize(row.Messages, Json)), ("$taxes", JsonSerializer.Serialize(row.ServerTaxes, Json)), ("$unclassified", JsonSerializer.Serialize(row.Unclassified, Json)));
        return row;
    }

    public IReadOnlyList<Submission> Submissions(Guid periodId) =>
        Query(SubmissionSelect + " WHERE period_id = $period ORDER BY created_at", ReadSubmission, ("$period", G(periodId)));

    private const string SubmissionSelect = "SELECT id, period_id, created_at, file_name, unique_id, uuid, result, feedback_source, general_json FROM submission";

    private static Submission ReadSubmission(SqliteDataReader r) => new(
        Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), Time(r, 2) ?? default, r.GetString(3), r.GetString(4), Str(r, 5),
        r.IsDBNull(6) ? null : Enum.Parse<ProcessingResult>(r.GetString(6)), Str(r, 7),
        JsonSerializer.Deserialize<List<GeneralValidation>>(r.GetString(8), Json)!);

    public IReadOnlyList<LineFeedback> Feedback(Guid paymentId) =>
        Query("SELECT payment_id, submission_id, recorded_at, level, counter, severity, next_action, messages_json, server_taxes_json, unclassified_json FROM line_feedback WHERE payment_id = $payment ORDER BY recorded_at", r => new LineFeedback(
            Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), Time(r, 2) ?? default, Enum.Parse<ValidationLevel>(r.GetString(3)), r.IsDBNull(4) ? null : (int)r.GetInt64(4),
            Enum.Parse<Severity>(r.GetString(5)), Enum.Parse<NextAction>(r.GetString(6)),
            JsonSerializer.Deserialize<List<ResponseItem>>(r.GetString(7), Json)!, JsonSerializer.Deserialize<Dictionary<string, decimal>>(r.GetString(8), Json)!,
            JsonSerializer.Deserialize<List<ResponseItem>>(r.GetString(9), Json)!),
            ("$payment", G(paymentId)));

    private void SetStatus(Guid paymentId, PaymentStatus status) =>
        Exec("UPDATE payment SET status = $status WHERE id = $id", ("$id", G(paymentId)), ("$status", status.ToString()));

    // ---------------------------------------------------------------- schema

    /// <summary>The copy an upgrade leaves behind: the store as it was just before this build's schema, beside the file
    /// ("firm.db.before-v9" for this build's v9), so that an older program can be put back after an update that went wrong. Made afresh at
    /// every upgrade to this version (a copy from an earlier, undone attempt would be stale), written whole or not at all.</summary>
    public static string BackupPathFor(string path) => $"{path}.before-v{SchemaVersion}";

    /// <summary>Where a store upgraded beyond this build goes when its copy is put back: "firm.db.newer-v10" for this build's v9.</summary>
    public static string AsidePathFor(string path) => $"{path}.newer-v{SchemaVersion + 1}";

    /// <summary>Whether a failure to open the store says it was upgraded by a newer build.</summary>
    public static bool IsNewerThanThisBuild(Exception failure) =>
        failure is InvalidOperationException && failure.Message.Contains("newer than this build", StringComparison.Ordinal);

    /// <summary>The copy a newer build left beside the store before upgrading it past this build's schema — the earliest
    /// such copy, the store as this build last knew it — or null when there is none.</summary>
    public static string? NewerBackupOf(string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return null;
        }

        var prefix = Path.GetFileName(path) + ".before-v";
        return Directory.GetFiles(folder, prefix + "*")
            .Select(f => (File: f, Version: int.TryParse(Path.GetFileName(f)[prefix.Length..], out var v) ? v : -1))
            .Where(x => x.Version > SchemaVersion)
            .OrderBy(x => x.Version)
            .Select(x => x.File)
            .FirstOrDefault();
    }

    /// <summary>Puts the copy back in place of the store: the upgraded store goes aside under the given name — or, when a
    /// store set aside earlier already holds that name, under the name with a stamp, so that nothing set aside is ever
    /// deleted (decision 68: the copies are the owner's to delete) — and the copy is copied in (the copy itself stays, for
    /// another try). Returns the name the store went aside under.</summary>
    public static string RestoreBackup(string path, string backup, string aside)
    {
        var target = FreeAsidePathFor(aside);
        File.Move(path, target);
        File.Copy(backup, path);
        return target;
    }

    /// <summary>The name a store goes aside under: the aside name itself when free, else with a stamp (and a count), so
    /// that the question put to the accountant names the very file.</summary>
    public static string FreeAsidePathFor(string aside)
    {
        if (!File.Exists(aside))
        {
            return aside;
        }

        var stamped = $"{aside}-{DateTime.Now:yyyyMMdd-HHmmss}";
        var target = stamped;
        for (var n = 2; File.Exists(target); n++)
        {
            target = $"{stamped}-{n}";
        }

        return target;
    }

    private void BackUpBeforeUpgrade(string path)
    {
        Exec("CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL)");
        var version = (int)(Scalar<long?>("SELECT MAX(version) FROM schema_version") ?? 0);
        if (version == 0 || version >= SchemaVersion)
        {
            return;
        }

        var backup = BackupPathFor(path);
        var partial = backup + ".partial";
        File.Delete(partial);
        using (var copy = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = partial, Pooling = false }.ToString()))
        {
            copy.Open();
            _connection.BackupDatabase(copy);
        }

        File.Move(partial, backup, overwrite: true);
    }

    private void EnsureSchema(int upTo = int.MaxValue)
    {
        Exec("CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL)");
        var version = (int)(Scalar<long?>("SELECT MAX(version) FROM schema_version") ?? 0);
        if (version > SchemaVersion)
        {
            throw new InvalidOperationException($"The store is at schema version {version}, newer than this build ({SchemaVersion}).");
        }

        var target = Math.Min(SchemaVersion, upTo);
        if (version >= target)
        {
            return;
        }

        // every step in one transaction: a failure half-way leaves the file at the version it was, never between two —
        // so the copy the next start sets aside (before-v<N>) is always a true pre-upgrade store
        using var tx = Tx();
        for (var step = version; step < target; step++)
        {
            if (FailMigrationForTests is { } fail && fail.Step == step && string.Equals(fail.Path, _openedPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"migration step {step + 1} failed (a test's doing).");
            }

            Exec(Migrations[step]);
            Exec("INSERT INTO schema_version (version) VALUES ($v)", ("$v", step + 1));
        }

        tx.Commit();
        _hasPayerColumn = null; // the columns may have changed
    }

    private string? _openedPath;

    /// <summary>A test seam: the file (full path) and the migration step (0-based) made to fail, so the one-transaction
    /// upgrade is proved; null in the program, and other stores are never touched by it.</summary>
    internal static (string Path, int Step)? FailMigrationForTests;

    /// <summary>Raw SQL for the tests (a row the way an older build wrote it, a version check); not part of the store's surface.</summary>
    internal int ExecuteRaw(string sql, params (string Name, object? Value)[] parameters) => Exec(sql, parameters);

    internal T ScalarRaw<T>(string sql, params (string Name, object? Value)[] parameters) => Scalar<T>(sql, parameters);

    /// <summary>A file store left at an older schema version — for the tests that prove the upgrade path.</summary>
    internal static SqliteStore OpenAtVersion(string path, int version)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true, Pooling = false }.ToString());
        connection.Open();
        var store = new SqliteStore(connection);
        store.EnsureSchema(version);
        return store;
    }

    /// <summary>The scripts as shipped, for the test that pins them: a shipped script is never edited, only followed by the next.</summary>
    internal static IReadOnlyList<string> MigrationScripts => Migrations;

    /// <summary>One SQL script per schema version, applied in order; a store at version n gets scripts n+1 … N.</summary>
    private static readonly IReadOnlyList<string> Migrations =
    [
        // v1 — companies, people, periods, the key counter, declared lines with versions, submissions, feedback.
        """
        CREATE TABLE company (
            id TEXT PRIMARY KEY,
            registry_code TEXT NOT NULL UNIQUE,
            name TEXT NOT NULL,
            notes TEXT);
        CREATE TABLE person (
            id TEXT PRIMARY KEY,
            company_id TEXT NOT NULL REFERENCES company(id),
            first_name TEXT NOT NULL,
            last_name TEXT NOT NULL,
            personal_code TEXT,
            mrr_code TEXT,
            residency TEXT NOT NULL,
            residence_country TEXT,
            residency_checked_on TEXT,
            funded_pension_member INTEGER,
            pension_rate TEXT,
            pension_checked_on TEXT,
            on_pension INTEGER NOT NULL DEFAULT 0,
            pension_age_year INTEGER,
            exemption_applied INTEGER NOT NULL DEFAULT 0,
            exemption_monthly TEXT,
            minimum_applies INTEGER NOT NULL DEFAULT 1,
            a1_case TEXT NOT NULL DEFAULT 'None',
            a1_country TEXT,
            work_country TEXT,
            treaty_rate TEXT,
            tor_type_id TEXT,
            notes TEXT,
            UNIQUE (company_id, personal_code));
        CREATE TABLE period (
            id TEXT PRIMARY KEY,
            company_id TEXT NOT NULL REFERENCES company(id),
            year INTEGER NOT NULL,
            month INTEGER NOT NULL,
            state TEXT NOT NULL,
            exported_at TEXT,
            uploaded_at TEXT,
            confirmed_at TEXT,
            UNIQUE (company_id, year, month));
        CREATE TABLE line_counter (
            period_id TEXT NOT NULL REFERENCES period(id),
            section TEXT NOT NULL,
            next INTEGER NOT NULL,
            PRIMARY KEY (period_id, section));
        CREATE TABLE submission (
            id TEXT PRIMARY KEY,
            period_id TEXT NOT NULL REFERENCES period(id),
            created_at TEXT NOT NULL,
            file_name TEXT NOT NULL,
            unique_id TEXT NOT NULL,
            uuid TEXT,
            result TEXT,
            feedback_source TEXT,
            general_json TEXT NOT NULL DEFAULT '[]');
        CREATE TABLE payment (
            id TEXT PRIMARY KEY,
            period_id TEXT NOT NULL REFERENCES period(id),
            person_id TEXT REFERENCES person(id),
            section TEXT NOT NULL,
            line_number TEXT NOT NULL,
            version INTEGER NOT NULL,
            action TEXT NOT NULL,
            status TEXT NOT NULL,
            type_code TEXT,
            payment_date TEXT,
            gross TEXT,
            net_paid TEXT,
            source_ref TEXT,
            payee_code TEXT,
            payee_description TEXT,
            facts_json TEXT,
            result_json TEXT,
            line_json TEXT NOT NULL,
            submission_id TEXT REFERENCES submission(id),
            created_at TEXT NOT NULL,
            UNIQUE (period_id, section, line_number, version));
        CREATE INDEX payment_by_submission ON payment (submission_id);
        CREATE TABLE line_feedback (
            payment_id TEXT NOT NULL REFERENCES payment(id),
            submission_id TEXT NOT NULL REFERENCES submission(id),
            recorded_at TEXT NOT NULL,
            level TEXT NOT NULL,
            counter INTEGER,
            severity TEXT NOT NULL,
            next_action TEXT NOT NULL,
            messages_json TEXT NOT NULL,
            server_taxes_json TEXT NOT NULL,
            unclassified_json TEXT NOT NULL DEFAULT '[]',
            PRIMARY KEY (payment_id, submission_id, recorded_at));
        """,
        // v2 — imports: files with their bytes, recognised sheets with the layout as used, every statement row and
        // TÖR row, the files a period's run used, the owner's layout overrides, and the bank row a declared line came from.
        """
        CREATE TABLE import_file (
            id TEXT PRIMARY KEY,
            company_id TEXT NOT NULL REFERENCES company(id),
            kind TEXT NOT NULL,
            file_name TEXT NOT NULL,
            format TEXT NOT NULL,
            sha256 TEXT NOT NULL,
            size INTEGER NOT NULL,
            imported_at TEXT NOT NULL,
            encoding TEXT,
            notes TEXT,
            content BLOB,
            UNIQUE (company_id, sha256));
        CREATE TABLE import_sheet (
            id TEXT PRIMARY KEY,
            file_id TEXT NOT NULL REFERENCES import_file(id),
            sheet_index INTEGER NOT NULL,
            sheet_name TEXT,
            kind TEXT NOT NULL,
            layout_id TEXT,
            layout_json TEXT,
            header_row_no INTEGER NOT NULL,
            headers_json TEXT NOT NULL,
            account TEXT,
            display_name TEXT NOT NULL,
            row_count INTEGER NOT NULL,
            first_date TEXT,
            last_date TEXT,
            problems_json TEXT NOT NULL DEFAULT '[]',
            UNIQUE (file_id, sheet_index));
        CREATE TABLE statement_row (
            id TEXT PRIMARY KEY,
            sheet_id TEXT NOT NULL REFERENCES import_sheet(id),
            row_no INTEGER NOT NULL,
            date TEXT,
            date_text TEXT,
            account TEXT,
            counterparty TEXT,
            counterparty_code TEXT,
            amount TEXT,
            amount_text TEXT,
            currency TEXT,
            description TEXT,
            reference TEXT,
            transaction_id TEXT,
            direction TEXT NOT NULL,
            dc_text TEXT,
            skip_reason TEXT,
            problem TEXT,
            row_hash TEXT NOT NULL,
            cells_json TEXT NOT NULL,
            UNIQUE (sheet_id, row_no));
        CREATE INDEX statement_row_by_date ON statement_row (date);
        CREATE INDEX statement_row_by_hash ON statement_row (row_hash);
        CREATE TABLE tor_entry (
            id TEXT PRIMARY KEY,
            sheet_id TEXT NOT NULL REFERENCES import_sheet(id),
            row_no INTEGER NOT NULL,
            company_id TEXT NOT NULL REFERENCES company(id),
            tor_id TEXT,
            personal_code TEXT,
            first_name TEXT NOT NULL,
            last_name TEXT NOT NULL,
            country TEXT,
            start_date TEXT,
            end_date TEXT,
            type_id TEXT,
            type TEXT,
            state_id TEXT,
            state TEXT,
            termination_id TEXT,
            termination TEXT,
            employer_code TEXT,
            employer_name TEXT,
            problem TEXT,
            cells_json TEXT NOT NULL,
            UNIQUE (sheet_id, row_no));
        CREATE INDEX tor_entry_by_code ON tor_entry (company_id, personal_code);
        CREATE TABLE period_import (
            period_id TEXT NOT NULL REFERENCES period(id),
            file_id TEXT NOT NULL REFERENCES import_file(id),
            PRIMARY KEY (period_id, file_id));
        CREATE INDEX period_import_by_file ON period_import (file_id);
        CREATE TABLE statement_layout (
            id TEXT PRIMARY KEY,
            json TEXT NOT NULL,
            updated_at TEXT NOT NULL);
        ALTER TABLE payment ADD COLUMN source_row_id TEXT REFERENCES statement_row(id);
        CREATE INDEX payment_by_source_row ON payment (source_row_id);
        """,

        // v3 — matching: the owner's match catalog, and each period's run (persons, sources, every hit) as the run said it.
        """
        CREATE TABLE match_catalog (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            json TEXT NOT NULL,
            updated_at TEXT NOT NULL);
        CREATE TABLE match_run (
            id TEXT PRIMARY KEY,
            period_id TEXT NOT NULL UNIQUE REFERENCES period(id),
            tor_sheet_id TEXT REFERENCES import_sheet(id),
            run_at TEXT NOT NULL,
            catalog_json TEXT NOT NULL,
            problems_json TEXT NOT NULL DEFAULT '[]');
        CREATE TABLE match_person (
            id TEXT PRIMARY KEY,
            run_id TEXT NOT NULL REFERENCES match_run(id),
            ord INTEGER NOT NULL,
            person_key TEXT NOT NULL,
            personal_code TEXT,
            last_name TEXT NOT NULL,
            first_name TEXT NOT NULL,
            active_entry_id TEXT REFERENCES tor_entry(id),
            entry_ids_json TEXT NOT NULL DEFAULT '[]',
            UNIQUE (run_id, person_key));
        CREATE TABLE match_source (
            id TEXT PRIMARY KEY,
            run_id TEXT NOT NULL REFERENCES match_run(id),
            ord INTEGER NOT NULL,
            sheet_id TEXT NOT NULL REFERENCES import_sheet(id),
            name TEXT NOT NULL,
            layout_id TEXT NOT NULL,
            UNIQUE (run_id, sheet_id),
            UNIQUE (run_id, name));
        CREATE INDEX match_source_by_sheet ON match_source (sheet_id);
        CREATE TABLE match_hit (
            id TEXT PRIMARY KEY,
            run_id TEXT NOT NULL REFERENCES match_run(id),
            ord INTEGER NOT NULL,
            source_id TEXT NOT NULL REFERENCES match_source(id),
            row_id TEXT NOT NULL REFERENCES statement_row(id),
            person_id TEXT REFERENCES match_person(id),
            last_mask TEXT NOT NULL DEFAULT '',
            first_mask TEXT NOT NULL DEFAULT '',
            mask_used INTEGER NOT NULL DEFAULT 0,
            verdict_kind TEXT NOT NULL,
            category TEXT,
            matched_json TEXT NOT NULL DEFAULT '[]',
            text TEXT NOT NULL DEFAULT '',
            in_period INTEGER NOT NULL,
            counted INTEGER NOT NULL,
            flags_json TEXT NOT NULL DEFAULT '[]');
        CREATE INDEX match_hit_by_row ON match_hit (row_id);
        """,

        // v4 — lookups: where a person's residency and pension facts came from (a lookup, or the accountant's hand),
        // and every answer the two registers gave, per person and control date, as it came.
        """
        ALTER TABLE person ADD COLUMN residency_source TEXT NOT NULL DEFAULT 'Unknown';
        ALTER TABLE person ADD COLUMN pension_source TEXT NOT NULL DEFAULT 'Unknown';
        CREATE TABLE lookup (
            id TEXT PRIMARY KEY,
            person_id TEXT NOT NULL REFERENCES person(id),
            kind TEXT NOT NULL,
            source TEXT NOT NULL,
            checked_at TEXT NOT NULL,
            control_date TEXT NOT NULL,
            personal_code TEXT,
            finding_json TEXT,
            failure TEXT,
            page TEXT,
            note TEXT);
        CREATE INDEX lookup_by_person ON lookup (person_id, kind, control_date);
        """,

        // v5 — the review: the register key a person was created from or bound to (a code or a name corrected by hand
        // never splits the person from their register row); the accountant's decisions about a period's payments, an
        // overlay per statement row (so a re-run over the same files keeps them; "nobody" and "no category" said
        // apart from "as the run has it") and the lines added by hand with their own figures.
        """
        ALTER TABLE person ADD COLUMN register_key TEXT;
        CREATE TABLE review_line (
            id TEXT PRIMARY KEY,
            period_id TEXT NOT NULL REFERENCES period(id),
            row_id TEXT REFERENCES statement_row(id),
            person_id TEXT REFERENCES person(id),
            no_person INTEGER NOT NULL DEFAULT 0,
            category TEXT,
            no_category INTEGER NOT NULL DEFAULT 0,
            type_code TEXT,
            declare INTEGER,
            note TEXT,
            date TEXT,
            amount TEXT,
            payee TEXT,
            description TEXT,
            updated_at TEXT NOT NULL);
        CREATE UNIQUE INDEX review_line_by_row ON review_line (period_id, row_id) WHERE row_id IS NOT NULL;
        CREATE INDEX review_line_by_period ON review_line (period_id);
        CREATE INDEX review_line_by_row_id ON review_line (row_id);
        """,

        // v6 — the declared lines know their hand line (no foreign key: a hand line removed later is the review's business).
        """
        ALTER TABLE payment ADD COLUMN review_line_id TEXT;
        """,

        // v7 — the accountant's word on declaring a person: as the rules say (Auto), always (Include: the minimum as under a
        // valid employment), or never (Exclude).
        """
        ALTER TABLE person ADD COLUMN declaration TEXT NOT NULL DEFAULT 'Auto';
        """,

        // v8 — the person card's employment type stands for every payment of the person, over the register's by date.
        """
        ALTER TABLE person ADD COLUMN card_type INTEGER NOT NULL DEFAULT 0;
        """,

        // v9 — the control date the lookups last set a person's residency or pension for, so a build of another month says so.
        """
        ALTER TABLE person ADD COLUMN facts_control_date TEXT;
        """,

        // v10 — how the file labels the company's code (ARIREGISTRIKOOD or MRR_KOOD) when the card says; null leaves it to the code's shape.
        """
        ALTER TABLE company ADD COLUMN payer_code_type TEXT;
        """,
    ];

    // ---------------------------------------------------------------- helpers

    /// <summary>Joins the active transaction or starts one; the outermost scope commits or rolls back.</summary>
    private Scope Tx()
    {
        if (_transaction is not null)
        {
            return new Scope(this, owner: false);
        }

        _transaction = _connection.BeginTransaction();
        return new Scope(this, owner: true);
    }

    private sealed class Scope(SqliteStore store, bool owner) : IDisposable
    {
        private bool _committed;

        public void Commit()
        {
            if (owner)
            {
                store._transaction!.Commit();
                store._transaction.Dispose();
                store._transaction = null;
            }

            _committed = true;
        }

        public void Dispose()
        {
            if (owner && !_committed && store._transaction is not null)
            {
                store._transaction.Rollback();
                store._transaction.Dispose();
                store._transaction = null;
            }
        }
    }

    private int Exec(string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command.ExecuteNonQuery();
    }

    private T Scalar<T>(string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        var result = command.ExecuteScalar();
        return result is null or DBNull ? default! : (T)result;
    }

    private IReadOnlyList<T> Query<T>(string sql, Func<SqliteDataReader, T> map, params (string Name, object? Value)[] parameters)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        using var reader = command.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read())
        {
            rows.Add(map(reader));
        }

        return rows;
    }

    /// <summary>Times are stored to the millisecond, so what a call returns is exactly what a later read gives back.</summary>
    private static DateTime Now() => Truncate(DateTime.UtcNow);

    private static DateTime Truncate(DateTime time)
    {
        var utc = time.ToUniversalTime();
        return new DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string G(Guid id) => id.ToString("N");

    private static string? D(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string T(DateTime time) => time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static string? N(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static string? Str(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static decimal? Dec(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : decimal.Parse(r.GetString(i), CultureInfo.InvariantCulture);

    private static DateOnly? Date(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : DateOnly.ParseExact(r.GetString(i), "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateTime? Time(SqliteDataReader r, int i) =>
        r.IsDBNull(i) ? null : DateTime.ParseExact(r.GetString(i), "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
}
