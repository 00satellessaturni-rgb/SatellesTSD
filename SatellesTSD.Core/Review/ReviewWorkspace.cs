using SatellesTSD.Core.Import;
using SatellesTSD.Core.Lookups;
using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Store;
using SatellesTSD.Core.Tax;

namespace SatellesTSD.Core.Review;

/// <summary>The review of a period as the window works it: the stored run read with the accountant's decisions on top
/// (<see cref="Load"/>), each decision saved as a sparse overlay keyed by the statement row — a value equal to the
/// default is not stored, an overlay deciding nothing is deleted — lines added by hand, and the person's facts edited
/// by hand (a residency or pension set by hand becomes Manual with "checked on" = today, decision 36).</summary>
public sealed class ReviewWorkspace(IStore store, TaxRates? rates = null, TimeProvider? clock = null)
{
    public const string PeriodNotDraft = "PERIOD_NOT_DRAFT";
    public const string PersonExists = "PERSON_EXISTS";
    public const string NameMissing = "NAME_MISSING";
    public const string CodeInvalid = "CODE_INVALID";
    public const string LineMissing = "LINE_MISSING";

    /// <summary>The source name of the lines added by hand.</summary>
    public const string ByHandSource = "by hand";

    private readonly IStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly TaxRates _rates = rates ?? TaxRates.Year2026;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>The catalog in force for a new run: the owner's saved document, else the embedded default.</summary>
    public MatchCatalog CatalogInForce() => new MatchRunner(_store).Catalog();

    /// <summary>The period's review: the run's persons get their store person (created from the register when missing),
    /// every payment row of the run its defaults and its overlay — a row the run found for two persons once, nobody's
    /// until the accountant names one — the hand lines their place; the rows nobody matched come last. Without a run
    /// the view holds the files and the persons of the company only.</summary>
    public ReviewView Load(Guid periodId)
    {
        var period = FindPeriod(periodId);
        var company = _store.Companies().First(c => c.Id == period.CompanyId);
        var files = _store.PeriodImports(periodId);
        var storedRun = _store.MatchRun(periodId);
        var run = storedRun is null ? null : _store.LoadMatchResult(periodId);
        var storedHits = storedRun is null ? [] : _store.MatchHits(storedRun.Id);
        var catalog = storedRun is { CatalogJson.Length: > 0 } ? MatchCatalog.Parse(storedRun.CatalogJson) : CatalogInForce();
        var lines = _store.ReviewLines(periodId);
        var overlays = lines.Where(l => !l.IsByHand).ToDictionary(l => l.RowId!.Value);

        var personsByKey = new Dictionary<string, Person>(StringComparer.Ordinal);
        if (run is not null)
        {
            var known = _store.Persons(company.Id);
            foreach (var registerPerson in run.Persons)
            {
                var person = RegisterPersons.Ensure(_store, company.Id, registerPerson, known);
                personsByKey[registerPerson.Key] = person;
                if (!known.Any(p => p.Id == person.Id && p.RegisterKey == person.RegisterKey))
                {
                    known = _store.Persons(company.Id);
                }
            }
        }

        var registerByPersonId = new Dictionary<Guid, MatchPerson>();
        if (run is not null)
        {
            foreach (var registerPerson in run.Persons)
            {
                registerByPersonId.TryAdd(personsByKey[registerPerson.Key].Id, registerPerson);
            }
        }

        var storePersons = _store.Persons(company.Id);
        var byId = storePersons.ToDictionary(p => p.Id);
        var byPerson = new Dictionary<Guid, List<ReviewPayment>>();
        var unmatched = new List<ReviewPayment>();
        var leftOut = 0;
        if (run is not null)
        {
            if (storedHits.Count != run.Hits.Count)
            {
                throw new InvalidOperationException("The stored run and its result disagree on the number of hits.");
            }

            // the payment rows in order of first appearance; a row the run found for two persons holds two hits
            var hitsByRow = new Dictionary<Guid, List<int>>();
            var rowOrder = new List<Guid>();
            for (var i = 0; i < run.Hits.Count; i++)
            {
                if (!run.Hits[i].Row.IsPayment)
                {
                    leftOut++;
                    continue;
                }

                var rowId = storedHits[i].RowId;
                if (!hitsByRow.TryGetValue(rowId, out var list))
                {
                    list = [];
                    hitsByRow[rowId] = list;
                    rowOrder.Add(rowId);
                }

                list.Add(i);
            }

            foreach (var rowId in rowOrder)
            {
                var indices = hitsByRow[rowId];
                overlays.TryGetValue(rowId, out var overlay);
                var shared = indices.Count > 1;
                var hit = run.Hits[indices[0]];
                var defaultPerson = !shared && hit.PersonKey is { } key && personsByKey.TryGetValue(key, out var dp) ? dp : null;
                var personId = overlay is { NoPerson: true } ? null : overlay?.PersonId ?? defaultPerson?.Id;
                var person = personId is { } pid && byId.TryGetValue(pid, out var sp) ? sp : null;
                var candidates = shared ? indices.Select(i => run.Hits[i].PersonKey).OfType<string>().Distinct().Select(k => personsByKey[k].FullName).ToList() : [];
                if (shared && person is not null)
                {
                    // the copy the run made for the chosen person carries that person's masks and verdict
                    var own = indices.FirstOrDefault(i => run.Hits[i].PersonKey is { } k && personsByKey.TryGetValue(k, out var p) && p.Id == person.Id, -1);
                    if (own >= 0)
                    {
                        hit = run.Hits[own];
                    }
                }

                // the employment that decides the salary type: the chosen person's register entry, else (nobody's row) the run's
                var register = person is not null
                    ? registerByPersonId.GetValueOrDefault(person.Id)
                    : hit.PersonKey is { } hk ? run.Persons.FirstOrDefault(p => p.Key == hk) : null;
                Place(FromHit(period, hit, rowId, overlay, person, defaultPerson?.Id, register, catalog, shared, candidates));
            }
        }

        foreach (var line in lines.Where(l => l.IsByHand))
        {
            var person = line.PersonId is { } pid && byId.TryGetValue(pid, out var sp) ? sp : null;
            Place(FromHandLine(period, line, person, run is not null && person is not null ? registerByPersonId.GetValueOrDefault(person.Id) : null, catalog));
        }

        var persons = new List<ReviewPerson>();
        if (run is not null)
        {
            foreach (var registerPerson in run.Persons)
            {
                var person = personsByKey[registerPerson.Key];
                if (persons.Any(p => p.PersonId == person.Id))
                {
                    continue; // two register rows bound to one store person show once
                }

                persons.Add(Make(registerPerson.Key, person, registerPerson, Take(person.Id), run.Problems.Where(p => p.PersonKey == registerPerson.Key).ToList(), catalog, false));
            }
        }

        foreach (var id in byPerson.Keys.OrderBy(id => byId[id].LastName, StringComparer.OrdinalIgnoreCase).ThenBy(id => byId[id].FirstName, StringComparer.OrdinalIgnoreCase).ToList())
        {
            persons.Add(Make(null, byId[id], null, Take(id), [], catalog, false));
        }

        persons.Add(Make(null, null, null, unmatched, [], catalog, true));

        var sources = run?.Sources.Select(s => s.Name).ToList() ?? [];
        if (lines.Any(l => l.IsByHand))
        {
            sources.Add(ByHandSource);
        }

        return new ReviewView
        {
            Period = period,
            Company = company,
            Run = run,
            StoredRun = storedRun,
            Catalog = catalog,
            Files = files,
            Sources = sources,
            Persons = persons,
            StorePersons = storePersons,
            Problems = run?.Problems ?? [],
            RowsLeftOut = leftOut,
        };

        void Place(ReviewPayment payment)
        {
            if (payment.PersonId is { } id && byId.ContainsKey(id))
            {
                if (!byPerson.TryGetValue(id, out var list))
                {
                    list = [];
                    byPerson[id] = list;
                }

                list.Add(payment);
            }
            else
            {
                unmatched.Add(payment);
            }
        }

        List<ReviewPayment> Take(Guid id) => byPerson.Remove(id, out var list) ? list : [];
    }

    /// <summary>The payment's person by the accountant's word: a person, or nobody (null) — the run's person when it
    /// equals them. A row the run gave to someone, given to nobody, is marked so; a shared row given to nobody is shared again.</summary>
    public ReviewLine? SetPerson(ReviewPayment payment, Guid? personId) =>
        Change(payment,
            l => personId is null
                ? l with { PersonId = null, NoPerson = payment.DefaultPersonId is not null }
                : l with { PersonId = personId == payment.DefaultPersonId ? null : personId, NoPerson = false },
            l => l with { PersonId = personId, NoPerson = false });

    /// <summary>The payment's category by the accountant's word: a category, or none (null) — the run's verdict when it
    /// equals it. The type follows the new category: a type set by hand before is cleared.</summary>
    public ReviewLine? SetCategory(ReviewPayment payment, string? category)
    {
        var clean = Clean(category);
        return Change(payment,
            l => clean is null
                ? l with { Category = null, NoCategory = payment.DefaultCategory is not null, TypeCode = null }
                : l with { Category = Same(clean, payment.DefaultCategory) ? null : clean, NoCategory = false, TypeCode = null },
            l => l with { Category = clean, NoCategory = false, TypeCode = null });
    }

    /// <summary>The payment type by the accountant's word; derived from the category and the person when null or equal to it.</summary>
    public ReviewLine? SetTypeCode(ReviewPayment payment, string? typeCode)
    {
        var clean = Clean(typeCode);
        return Change(payment, l => l with { TypeCode = Same(clean, payment.DefaultTypeCode) ? null : clean }, l => l with { TypeCode = Same(clean, payment.DefaultTypeCode) ? null : clean });
    }

    /// <summary>Whether the payment is declared, by the accountant's word; the default when null or equal to it.</summary>
    public ReviewLine? SetDeclare(ReviewPayment payment, bool? declare) =>
        Change(payment, l => l with { Declare = declare == payment.DefaultDeclare ? null : declare }, l => l with { Declare = declare == payment.DefaultDeclare ? null : declare });

    public ReviewLine? SetNote(ReviewPayment payment, string? note) =>
        Change(payment, l => l with { Note = Clean(note) }, l => l with { Note = Clean(note) });

    public ReviewLine AddHandLine(Guid periodId, Guid? personId, DateOnly date, decimal amount, string payee, string? description = null, string? category = null, string? typeCode = null, bool? declare = null, string? note = null)
    {
        RequireDraft(FindPeriod(periodId));
        return _store.SaveReviewLine(ReviewLine.AddedByHand(periodId, personId, date, amount, payee, description, category, typeCode, declare, note));
    }

    public ReviewLine UpdateHandLine(ReviewLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (!line.IsByHand || line.Id == Guid.Empty)
        {
            throw new ArgumentException("Only a stored line added by hand is updated this way.", nameof(line));
        }

        RequireDraft(FindPeriod(line.PeriodId));
        return _store.SaveReviewLine(line);
    }

    public void RemoveHandLine(Guid periodId, Guid lineId)
    {
        RequireDraft(FindPeriod(periodId));
        var line = _store.ReviewLines(periodId).FirstOrDefault(l => l.Id == lineId)
            ?? throw new ReviewException(LineMissing, "The line is no longer in the store.");
        if (!line.IsByHand)
        {
            throw new ArgumentException("Only a line added by hand is removed; a row's decisions are cleared by setting them back to the default.", nameof(lineId));
        }

        _store.DeleteReviewLine(lineId);
    }

    /// <summary>Saves the facts the accountant edited onto a fresh copy of the person (so a lookup that ran meanwhile is
    /// not undone): a residency or pension changed — or confirmed — by hand becomes Manual, checked on today. The
    /// register link (<see cref="Person.RegisterKey"/>) stays, so a corrected code or name keeps the person one.</summary>
    public Person SavePerson(Person edited, bool confirmResidency = false, bool confirmPension = false)
    {
        ArgumentNullException.ThrowIfNull(edited);
        var fresh = _store.Persons(edited.CompanyId).FirstOrDefault(p => p.Id == edited.Id)
            ?? throw new KeyNotFoundException($"Person {edited.Id} is not in the store.");
        var code = CheckedCode(edited.CompanyId, edited.PersonalCode, fresh.Id);
        RequireNames(edited.FirstName, edited.LastName);
        var residencyChanged = confirmResidency || fresh.Residency != edited.Residency
            || !string.Equals(Clean(fresh.ResidenceCountry), Clean(edited.ResidenceCountry), StringComparison.OrdinalIgnoreCase);
        var pensionChanged = confirmPension || fresh.FundedPensionMember != edited.FundedPensionMember || fresh.PensionRate != edited.PensionRate;
        var today = Today();
        var next = fresh with
        {
            FirstName = edited.FirstName.Trim(),
            LastName = edited.LastName.Trim(),
            PersonalCode = code,
            MrrCode = Clean(edited.MrrCode),
            Residency = edited.Residency,
            ResidenceCountry = Clean(edited.ResidenceCountry),
            ResidencyCheckedOn = residencyChanged ? today : fresh.ResidencyCheckedOn,
            ResidencySource = residencyChanged ? LookupSource.Manual : fresh.ResidencySource,
            FundedPensionMember = edited.FundedPensionMember,
            PensionRate = edited.PensionRate,
            PensionCheckedOn = pensionChanged ? today : fresh.PensionCheckedOn,
            PensionSource = pensionChanged ? LookupSource.Manual : fresh.PensionSource,
            OnOldAgeOrFlexiblePension = edited.OnOldAgeOrFlexiblePension,
            PensionAgeYear = edited.PensionAgeYear,
            BasicExemptionApplied = edited.BasicExemptionApplied,
            BasicExemptionMonthly = edited.BasicExemptionMonthly,
            SocialTaxMinimumApplies = edited.SocialTaxMinimumApplies,
            A1 = edited.A1,
            A1Country = Clean(edited.A1Country),
            WorkCountry = Clean(edited.WorkCountry),
            TreatyIncomeTaxRate = edited.TreatyIncomeTaxRate,
            TorEmploymentTypeId = Clean(edited.TorEmploymentTypeId),
            Notes = Clean(edited.Notes),
        };
        _store.UpdatePerson(next);
        return next;
    }

    /// <summary>A person added by hand (the workbook's "adds people by hand (with Töötamise liik text)"): unchecked
    /// facts, the employment type the accountant names.</summary>
    public Person AddPerson(Guid companyId, string firstName, string lastName, string? personalCode, string? torEmploymentTypeId, Residency residency = Residency.Resident)
    {
        RequireNames(firstName, lastName);
        var code = CheckedCode(companyId, personalCode, null);
        return _store.AddPerson(Person.New(companyId, firstName.Trim(), lastName.Trim(), code, residency) with { TorEmploymentTypeId = Clean(torEmploymentTypeId) });
    }

    private ReviewPayment FromHit(Period period, MatchHit hit, Guid rowId, ReviewLine? overlay, Person? person, Guid? defaultPersonId, MatchPerson? register, MatchCatalog catalog, bool shared, IReadOnlyList<string> candidates)
    {
        var defaultCategory = hit.Verdict.Kind == VerdictKind.Matched ? hit.Verdict.Category?.Name : null;
        var category = overlay is { NoCategory: true } ? null : overlay?.Category ?? defaultCategory;
        var torType = register?.Entry.TypeId ?? person?.TorEmploymentTypeId;
        var defaultType = ReviewDefaults.TypeCodeFor(category is null ? null : catalog.Find(category), torType, person?.Residency ?? Residency.Resident);
        var typeCode = overlay?.TypeCode ?? defaultType;
        var amountOk = hit.Row.Amount is not null && (!shared || person is not null);
        var defaultDeclare = typeCode is not null && hit.InPeriod && amountOk && person is not null;
        var declare = overlay?.Declare ?? defaultDeclare;
        return new ReviewPayment
        {
            PeriodId = period.Id,
            RowId = rowId,
            LineId = overlay?.Id,
            IsByHand = false,
            Source = hit.Source,
            RowNo = hit.Row.RowNo,
            Date = hit.Row.Date,
            DateText = hit.Row.DateText,
            Payee = hit.Row.Counterparty ?? "",
            Description = hit.Row.Description,
            Amount = hit.Amount,
            AmountOk = amountOk,
            RunPersonKey = hit.PersonKey,
            DefaultPersonId = defaultPersonId,
            PersonId = person?.Id,
            VerdictText = hit.Text,
            Masks = hit.FullMask,
            DefaultCategory = defaultCategory,
            Category = category,
            DefaultTypeCode = defaultType,
            TypeCode = typeCode,
            DefaultDeclare = defaultDeclare,
            Declare = declare,
            InPeriod = hit.InPeriod,
            Counted = hit.Counted,
            Flags = hit.Flags,
            IsShared = shared,
            Candidates = candidates,
            Note = overlay?.Note,
            HasOverride = overlay is not null,
            Checks = declare ? ReviewChecks.Of(person, typeCode, hit.Amount, amountOk, hit.InPeriod, period.Year, _rates) : [],
        };
    }

    private ReviewPayment FromHandLine(Period period, ReviewLine line, Person? person, MatchPerson? register, MatchCatalog catalog)
    {
        var torType = register?.Entry.TypeId ?? person?.TorEmploymentTypeId;
        var defaultType = ReviewDefaults.TypeCodeFor(line.Category is null ? null : catalog.Find(line.Category), torType, person?.Residency ?? Residency.Resident);
        var typeCode = line.TypeCode ?? defaultType;
        var inPeriod = line.Date is { } date && date.Year == period.Year && date.Month == period.Month;
        var amount = line.Amount ?? 0m;
        var defaultDeclare = typeCode is not null && inPeriod && person is not null;
        var declare = line.Declare ?? defaultDeclare;
        return new ReviewPayment
        {
            PeriodId = period.Id,
            RowId = null,
            LineId = line.Id,
            IsByHand = true,
            Source = ByHandSource,
            RowNo = null,
            Date = line.Date,
            DateText = line.Date?.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture),
            Payee = line.Payee ?? "",
            Description = line.Description,
            Amount = amount,
            AmountOk = true,
            RunPersonKey = null,
            DefaultPersonId = null,
            PersonId = person?.Id,
            VerdictText = "",
            DefaultCategory = null,
            Category = line.Category,
            DefaultTypeCode = defaultType,
            TypeCode = typeCode,
            DefaultDeclare = defaultDeclare,
            Declare = declare,
            InPeriod = inPeriod,
            Counted = true,
            Flags = [],
            Note = line.Note,
            HasOverride = true,
            Checks = declare ? ReviewChecks.Of(person, typeCode, amount, true, inPeriod, period.Year, _rates) : [],
        };
    }

    private static ReviewPerson Make(string? key, Person? person, MatchPerson? register, List<ReviewPayment> payments, IReadOnlyList<MatchProblem> problems, MatchCatalog catalog, bool isUnmatched)
    {
        var sums = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var payment in payments)
        {
            if (payment.Category is { } name && catalog.Find(name) is { IsSalary: true } && payment.InPeriod && payment.AmountOk)
            {
                sums[payment.Source] = sums.GetValueOrDefault(payment.Source) + payment.Amount;
            }
        }

        var rounded = sums.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value, 2, MidpointRounding.ToEven), StringComparer.Ordinal);
        return new ReviewPerson
        {
            Key = key,
            PersonId = person?.Id,
            Person = person,
            Register = register,
            IsUnmatched = isUnmatched,
            SalaryBySource = rounded,
            SalaryTotal = Math.Round(rounded.Values.Sum(), 2, MidpointRounding.ToEven),
            Payments = payments,
            Problems = problems,
        };
    }

    /// <summary>Applies a change to the payment's stored decision: a row's overlay is found by the row (never by a
    /// possibly stale line id), a hand line by its id.</summary>
    private ReviewLine? Change(ReviewPayment payment, Func<ReviewLine, ReviewLine> overlay, Func<ReviewLine, ReviewLine> hand)
    {
        ArgumentNullException.ThrowIfNull(payment);
        RequireDraft(FindPeriod(payment.PeriodId));
        var lines = _store.ReviewLines(payment.PeriodId);
        if (payment.IsByHand)
        {
            var existingHand = payment.LineId is { } id ? lines.FirstOrDefault(l => l.Id == id) : null;
            if (existingHand is null)
            {
                throw new ReviewException(LineMissing, "The line added by hand is no longer in the store.");
            }

            return _store.SaveReviewLine(hand(existingHand));
        }

        var rowId = payment.RowId ?? throw new ArgumentException("A row payment names its row.", nameof(payment));
        var existing = lines.FirstOrDefault(l => l.RowId == rowId);
        var next = overlay(existing ?? ReviewLine.ForRow(payment.PeriodId, rowId));
        if (next.IsEmpty)
        {
            if (existing is not null)
            {
                _store.DeleteReviewLine(existing.Id);
            }

            return null;
        }

        return _store.SaveReviewLine(next);
    }

    private string? CheckedCode(Guid companyId, string? personalCode, Guid? self)
    {
        var code = Clean(personalCode);
        if (code is null)
        {
            return null;
        }

        if (!PersonalCode.LooksValid(code))
        {
            throw new ReviewException(CodeInvalid, $"'{code}' is not an Estonian personal code; a foreign person is named by the MTA non-resident code instead.");
        }

        if (_store.FindPerson(companyId, code) is { } other && other.Id != self)
        {
            throw new ReviewException(PersonExists, $"Another person of the company has the personal code {code}.");
        }

        return code;
    }

    private static void RequireNames(string? firstName, string? lastName)
    {
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
        {
            throw new ReviewException(NameMissing, "A person needs a first and a last name.");
        }
    }

    private static void RequireDraft(Period period)
    {
        if (period.State != PeriodState.Draft)
        {
            throw new ReviewException(PeriodNotDraft, $"The period is {period.State}; its review stays as its record.");
        }
    }

    private Period FindPeriod(Guid periodId) =>
        _store.Companies().SelectMany(c => _store.Periods(c.Id)).FirstOrDefault(p => p.Id == periodId)
        ?? throw new KeyNotFoundException($"Period {periodId} is not in the store.");

    private DateOnly Today() => DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.Ordinal);
}
