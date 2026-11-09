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
    public const string CodeInvalid = ReviewChecks.CodeInvalid; // the card's save and the review's refusal name the fault alike
    public const string LineMissing = "LINE_MISSING";

    /// <summary>The source name of the lines added by hand.</summary>
    public const string ByHandSource = "by hand";

    /// <summary>The mark a decision carries in its note once the review moved it from a copy to the original row (decision 117): "«liigutatud koopialt <reference>»", said as DECISION_MOVED until the accountant clears it.</summary>
    public const string MovedFromMark = "«liigutatud koopialt ";

    private readonly IStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly TaxRates _rates = rates ?? TaxRates.Year2026;
    private readonly bool _ratesGiven = rates is not null;

    /// <summary>The rates the review reckons with (the report and the window show the allowance in force from them).</summary>
    public TaxRates Rates => _rates;

    /// <summary>The rates the checks of a month use: the rates given to this workspace, else the month's year's, else the
    /// default (the checks say what they can; the build is where a year without rates is refused).</summary>
    private TaxRates RatesFor(int year) => _ratesGiven ? _rates : TaxRates.For(year) ?? _rates;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>The declaration's cut the lines are built in (the window's Seaded choice): under the totals per type a
    /// refused payment holds its whole type, and the persons say so (decision 79).</summary>
    public bool TotalsPerType { get; set; }

    /// <summary>The catalog in force for a new run: the owner's saved document, else the embedded default.</summary>
    public MatchCatalog CatalogInForce() => new MatchRunner(_store).Catalog();

    /// <summary>The period's review: the run's persons get their store person (created from the register when missing),
    /// every payment row of the run its defaults and its overlay — a row the run found for two persons once, nobody's
    /// until the accountant names one — the hand lines their place; the rows nobody matched come last. Without a run
    /// the view holds the files and the persons of the company only.</summary>
    /// <summary>Whether the linked files are no longer those the run read: the same filter as the run's (register sheets, and
    /// statement sheets with a layout), compared by sheet id — not by import time, an older file can be linked after the run.</summary>
    /// <remarks>Without a run, the month is stale as soon as a file to search is linked: lines built then would come from no
    /// search at all (every bank-row line retired). A month of hand lines only, with no such file, needs no search.</remarks>
    private bool IsStale(StoredMatchRun? storedRun, MatchResult? run, IReadOnlyList<ImportFile> files, Company company)
    {
        var sheets = files.SelectMany(f => _store.Sheets(f.Id)).ToList();
        var tor = sheets.Where(s => s.Kind == SheetKind.Tor).Select(s => s.Id).ToHashSet();
        var statements = sheets.Where(s => s is { Kind: SheetKind.Statement, Layout: not null }).Select(s => s.Id).ToHashSet();
        if (storedRun is null || run is null)
        {
            return tor.Count + statements.Count > 0;
        }

        // a search made before another employer's rows were left out (decision 133) still holds them as persons: searched again
        if (run.Persons.Any(p => p.Entries.Any(e => MatchRunner.IsOtherEmployer(e, company))))
        {
            return true;
        }

        var runTor = storedRun.TorSheetId is { } t ? new HashSet<Guid> { t } : [];
        var runStatements = run.Sources.Select(s => s.SheetId).OfType<Guid>().ToHashSet();
        return !tor.SetEquals(runTor) || !statements.SetEquals(runStatements);
    }

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
            var runKeys = run.Persons.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
            var taken = new HashSet<Guid>(); // a person one row stands for is not handed to a namesake row too (decision 142)
            foreach (var registerPerson in run.Persons)
            {
                var person = RegisterPersons.Ensure(_store, company.Id, registerPerson, known, runKeys, taken);
                personsByKey[registerPerson.Key] = person;
                taken.Add(person.Id);
                if (!known.Any(p => p.Id == person.Id && p.RegisterKey == person.RegisterKey && p.PersonalCode == person.PersonalCode))
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
        var payeeKnown = storePersons.ToList();
        var payeesChanged = false;
        var standing = _store.Payments(periodId); // the lines built: a month past Draft gives a payee only to a row they were built from
        var personProblems = new Dictionary<Guid, List<MatchProblem>>(); // what the review itself says of a person, beside the run's problems
        // what the search said of the register as a whole (another employer's rows left out) is shown with the rows nobody
        // matched, so it is seen whenever the month is opened, not only in the search's log
        var unmatchedProblems = run?.Problems.Where(p => p.Code is MatchProblem.OtherEmployer or MatchProblem.RowsBelowLastPayee).ToList() ?? [];
        if (run is not null)
        {
            // a search made before decision 140 carries no count of the rows below the last payee row: told from the rows themselves
            foreach (var source in run.Sources.Where(s => unmatchedProblems.All(p => p.Code != MatchProblem.RowsBelowLastPayee || p.Source != s.Name)))
            {
                unmatchedProblems.AddRange(PeriodMatcher.RowsBelowLastPayee(source).Where(p => p.Code == MatchProblem.RowsBelowLastPayee));
            }
        }
        var twinRows = new HashSet<Guid>(); // rent rows whose payee has a hand line of the amount this month: not declared by default
        var twinHandLines = new HashSet<Guid>(); // the hand lines already paired with a row
        List<MatchProblem> ProblemsOf(Guid id) => personProblems.TryGetValue(id, out var list) ? list : personProblems[id] = [];
        var byPerson = new Dictionary<Guid, List<ReviewPayment>>();
        var unmatched = new List<ReviewPayment>();
        var leftOut = 0;
        // the sources whose layout only assumes the direction (the currency column read as the marker): by the layout the sheet
        // was read with (its snapshot, decision 14), since the owner's layouts may change under a run (136)
        var layouts = LayoutCatalog.Default.WithOverrides(_store.Layouts());
        var sheetsById = files.SelectMany(f => _store.Sheets(f.Id)).ToDictionary(s => s.Id);
        var assumed = run?.Sources
            .Where(s => (s.SheetId is { } sheetId && sheetsById.TryGetValue(sheetId, out var sheet) && sheet.Layout is { } snapshot ? snapshot : layouts.Find(s.LayoutId))?.DirectionIsAssumed() == true)
            .Select(s => s.Name).ToHashSet(StringComparer.Ordinal) ?? [];
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
                // a row without a payee is listed too (the accountant sees every transaction, as the workbook showed
                // them); only a blank row inside the data is left out
                if (run.Hits[i].Row.SkipReason is not (null or SkipReason.NoName))
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

            // a decision made on a row that a file linked since turned into a copy (decision 86) belongs to the original:
            // moved there in a Draft, unless the original carries its own; said either way, so the accountant looks
            const string copyOf = "duplikaat: ";
            var rowByReference = new Dictionary<string, Guid>(StringComparer.Ordinal);
            for (var i = 0; i < run.Hits.Count; i++)
            {
                rowByReference.TryAdd(run.Hits[i].Reference, storedHits[i].RowId);
            }

            for (var i = 0; i < run.Hits.Count; i++)
            {
                var copy = run.Hits[i];
                var copyRow = storedHits[i].RowId;
                if (!copy.Flags.Contains(MatchProblem.DuplicateRow, StringComparer.Ordinal) || !overlays.TryGetValue(copyRow, out var decided)
                    || !copy.Text.StartsWith(copyOf, StringComparison.Ordinal) || !rowByReference.TryGetValue(copy.Text[copyOf.Length..], out var originalRow))
                {
                    continue;
                }

                var original = copy.Text[copyOf.Length..];
                if (period.State == PeriodState.Draft && !overlays.ContainsKey(originalRow))
                {
                    // the move is marked in the line's note, so it is seen in the grid and said on every load until the
                    // accountant clears the mark
                    var note = $"{MovedFromMark}{copy.Reference}»" + (string.IsNullOrWhiteSpace(decided.Note) ? "" : " " + decided.Note);
                    var moved = _store.SaveReviewLine(decided with { RowId = originalRow, Note = note });
                    overlays.Remove(copyRow);
                    overlays[originalRow] = moved;
                }
                else
                {
                    var why = period.State == PeriodState.Draft ? "the original carries its own" : "the month is past Draft";
                    unmatchedProblems.Add(new MatchProblem(MatchProblem.DecisionOnCopy, $"{copy.Reference} is a copy of {original} and carries a decision; {why}, so it stays where it is — check the original.", Source: copy.Source, RowNo: copy.Row.RowNo));
                }
            }

            foreach (var marked in overlays.Values.Where(l => l.Note?.StartsWith(MovedFromMark, StringComparison.Ordinal) == true))
            {
                var mark = marked.Note![..(marked.Note.IndexOf('»') is var end and >= 0 ? end + 1 : marked.Note.Length)];
                unmatchedProblems.Add(new MatchProblem(MatchProblem.DecisionMoved, $"{mark}: a decision made on a copy was moved to the original row; check it, then clear the mark from the note."));
            }

            foreach (var rowId in rowOrder)
            {
                var indices = hitsByRow[rowId];
                overlays.TryGetValue(rowId, out var overlay);
                var shared = indices.Count > 1;
                var hit = run.Hits[indices[0]];
                var defaultPerson = !shared && hit.PersonKey is { } key && personsByKey.TryGetValue(key, out var dp) ? dp
                    : !shared && hit.PersonKey is null ? PayeeOf(hit, overlay, rowId) : null;
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
                Place(FromHit(period, hit, rowId, overlay, person, defaultPerson?.Id, register, catalog, shared, candidates, assumed.Contains(hit.Source), twinRows.Contains(rowId)));
            }
        }

        foreach (var line in lines.Where(l => l.IsByHand))
        {
            var person = line.PersonId is { } pid && byId.TryGetValue(pid, out var sp) ? sp : null;
            Place(FromHandLine(period, line, person, run is not null && person is not null ? registerByPersonId.GetValueOrDefault(person.Id) : null, catalog));
        }

        if (payeesChanged)
        {
            storePersons = _store.Persons(company.Id);
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

                persons.Add(Make(registerPerson.Key, person, registerPerson, Take(person.Id), run.Problems.Where(p => p.PersonKey == registerPerson.Key).Concat(ProblemsOf(person.Id)).ToList(), catalog, false, TotalsPerType));
            }
        }

        foreach (var id in byPerson.Keys.OrderBy(id => byId[id].LastName, StringComparer.OrdinalIgnoreCase).ThenBy(id => byId[id].FirstName, StringComparer.OrdinalIgnoreCase).ToList())
        {
            persons.Add(Make(null, byId[id], null, Take(id), ProblemsOf(id), catalog, false, TotalsPerType));
        }

        // a person the card declares as if employed ("kaasa") whom the month's register does not list and who has no payment:
        // shown with the word, nothing built for them (decision 137) — a forgotten "kaasa" declares no minimum for one who left.
        // Only in a Draft that was searched: before the search the register is not known, and a month declared is read as it was
        if (run is not null && period.State == PeriodState.Draft)
        {
            foreach (var included in storePersons.Where(p => p.Declaration == DeclarationChoice.Include && persons.All(listed => listed.PersonId != p.Id))
                .OrderBy(p => p.LastName, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.FirstName, StringComparer.OrdinalIgnoreCase))
            {
                var said = new MatchProblem(MatchProblem.IncludedWithoutRow, "Declared as if employed on the card (kaasa), but the month's register lists no row for the person and no payment was found: nothing is built this month. Set the card's Deklareerimine to \"reeglite järgi\" if the employment has ended.");
                persons.Add(Make(null, included, null, [], [said, .. ProblemsOf(included.Id)], catalog, false, TotalsPerType));
            }
        }

        persons.Add(Make(null, null, null, unmatched, unmatchedProblems, catalog, true, TotalsPerType));

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
            RunIsStale = IsStale(storedRun, run, files, company),
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

        // the payee of a row nobody of the register was found for, when its category is declared on the payee's own code
        // (Üür): a person of the company, created while the month is prepared; a month past Draft keeps its run as it was
        Person? PayeeOf(MatchHit hit, ReviewLine? overlay, Guid rowId)
        {
            if (!PayeePersons.IsPayeeRow(hit, overlay, catalog))
            {
                return null;
            }

            var counterparty = hit.Row.Counterparty!;
            if (overlay is { NoPerson: true } || overlay?.PersonId is not null)
            {
                // nobody is made for a row the accountant gave a person or nobody: the payee as already made, if any
                return PayeePersons.Find(payeeKnown, counterparty, hit.Row.CounterpartyCode);
            }

            if (period.State != PeriodState.Draft)
            {
                // a month past Draft is read, never written: its payee only where a line of the month was built from the row — a
                // person made since (in another month's Draft, or by hand) is not read back into a month declared without them
                var built = standing.Any(l => l.SourceRowId == rowId
                    || (Declare.PeriodDeclarer.TotalsOf(l.SourceRef) is { } totals && totals.References.Split("; ").Contains(hit.Reference, StringComparer.Ordinal)));
                return built ? PayeePersons.Find(payeeKnown, counterparty, hit.Row.CounterpartyCode) : null;
            }

            var payee = PayeePersons.Ensure(_store, company.Id, counterparty, hit.Row.CounterpartyCode, payeeKnown, out var changed);
            if (payee is null)
            {
                PayeePersons.Find(payeeKnown, counterparty, hit.Row.CounterpartyCode, out var ambiguous);
                if (ambiguous)
                {
                    unmatchedProblems.Add(new MatchProblem(MatchProblem.PayeeAmbiguous, $"{hit.Reference}: two persons of the company have the payee's names; name the one on the row.", Source: hit.Source, RowNo: hit.Row.RowNo));
                }

                return null;
            }

            if (changed)
            {
                payeesChanged = true;
                payeeKnown.RemoveAll(p => p.Id == payee.Id);
                payeeKnown.Add(payee);
                byId[payee.Id] = payee;
            }

            // the row's code held by another person of the company: not taken (decision 112), and said — the cards want a look
            if (PayeePersons.CodeOf(hit.Row.CounterpartyCode) is { } statementCode && payee.PersonalCode != statementCode
                && payeeKnown.Any(p => p.Id != payee.Id && string.Equals(p.PersonalCode, statementCode, StringComparison.Ordinal)))
            {
                ProblemsOf(payee.Id).Add(new MatchProblem(MatchProblem.PayeeCodeElsewhere, $"{hit.Reference}: the row's personal code belongs to another person of the company; the row is the payee's by the bound names — check the two cards.", Source: hit.Source, RowNo: hit.Row.RowNo));
            }

            // a line added by hand of the same amount for the payee this month, declaring something: the rent may be declared
            // there already — the row is not declared by default, and the person says so, until the accountant's tick or
            // untick on the row ends the word; one hand line pairs with one row
            if (overlay?.Declare is null
                && lines.FirstOrDefault(l => l.IsByHand && !twinHandLines.Contains(l.Id) && l.PersonId == payee.Id && l.Amount == hit.Amount && l.Declare != false
                    && (l.Category is not null || l.TypeCode is not null) && l.Date is { } day && day.Year == period.Year && day.Month == period.Month) is { } twin)
            {
                twinHandLines.Add(twin.Id);
                twinRows.Add(rowId);
                ProblemsOf(payee.Id).Add(new MatchProblem(MatchProblem.PayeeHandLine, $"{hit.Reference}: a line added by hand of the same amount stands for the person this month; the row is not declared by default (tick it when both are real).", Source: hit.Source, RowNo: hit.Row.RowNo));
            }

            return payee;
        }
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
    /// <param name="presented">The copy the card was filled from, when the caller has it: only what the accountant changed
    /// against it is written, so a fact the registers wrote to the store meanwhile is not covered by the card's stale copy;
    /// without it, every field of <paramref name="edited"/> stands.</param>
    public Person SavePerson(Person edited, bool confirmResidency = false, bool confirmPension = false, Person? presented = null)
    {
        ArgumentNullException.ThrowIfNull(edited);
        var fresh = _store.Persons(edited.CompanyId).FirstOrDefault(p => p.Id == edited.Id)
            ?? throw new KeyNotFoundException($"Person {edited.Id} is not in the store.");
        var basis = presented is { } shown && shown.Id == edited.Id ? shown : null;
        // the value to write: the edit where it differs from what the card showed, else what the store holds now
        T Pick<T>(T editedValue, T shownValue, T storeValue) => basis is null || !EqualityComparer<T>.Default.Equals(editedValue, shownValue) ? editedValue : storeValue;
        string? PickText(string? editedValue, string? shownValue, string? storeValue) =>
            basis is null || !string.Equals(Clean(editedValue), Clean(shownValue), StringComparison.Ordinal) ? Clean(editedValue) : Clean(storeValue);

        var code = CheckedCode(edited.CompanyId, Pick(edited.PersonalCode, basis?.PersonalCode, fresh.PersonalCode), fresh.Id);
        RequireNames(edited.FirstName, edited.LastName);
        var residency = Pick(edited.Residency, basis?.Residency ?? edited.Residency, fresh.Residency);
        var residenceCountry = PickText(edited.ResidenceCountry, basis?.ResidenceCountry, fresh.ResidenceCountry);
        var residencyChanged = confirmResidency || fresh.Residency != residency
            || !string.Equals(Clean(fresh.ResidenceCountry), residenceCountry, StringComparison.OrdinalIgnoreCase);
        var member = Pick(edited.FundedPensionMember, basis?.FundedPensionMember, fresh.FundedPensionMember);
        var rate = Pick(edited.PensionRate, basis?.PensionRate, fresh.PensionRate);
        var pensionChanged = confirmPension || fresh.FundedPensionMember != member || fresh.PensionRate != rate;
        var today = Today();
        var next = fresh with
        {
            FirstName = edited.FirstName.Trim(),
            LastName = edited.LastName.Trim(),
            PersonalCode = code,
            MrrCode = PickText(edited.MrrCode, basis?.MrrCode, fresh.MrrCode),
            Residency = residency,
            ResidenceCountry = residenceCountry,
            ResidencyCheckedOn = residencyChanged ? today : fresh.ResidencyCheckedOn,
            ResidencySource = residencyChanged ? LookupSource.Manual : fresh.ResidencySource,
            FundedPensionMember = member,
            PensionRate = rate,
            PensionCheckedOn = pensionChanged ? today : fresh.PensionCheckedOn,
            PensionSource = pensionChanged ? LookupSource.Manual : fresh.PensionSource,
            OnOldAgeOrFlexiblePension = Pick(edited.OnOldAgeOrFlexiblePension, basis?.OnOldAgeOrFlexiblePension ?? edited.OnOldAgeOrFlexiblePension, fresh.OnOldAgeOrFlexiblePension),
            PensionAgeYear = Pick(edited.PensionAgeYear, basis?.PensionAgeYear, fresh.PensionAgeYear),
            BasicExemptionApplied = Pick(edited.BasicExemptionApplied, basis?.BasicExemptionApplied ?? edited.BasicExemptionApplied, fresh.BasicExemptionApplied),
            BasicExemptionMonthly = Pick(edited.BasicExemptionMonthly, basis?.BasicExemptionMonthly, fresh.BasicExemptionMonthly),
            SocialTaxMinimumApplies = Pick(edited.SocialTaxMinimumApplies, basis?.SocialTaxMinimumApplies ?? edited.SocialTaxMinimumApplies, fresh.SocialTaxMinimumApplies),
            Declaration = Pick(edited.Declaration, basis?.Declaration ?? edited.Declaration, fresh.Declaration),
            TypeFromCard = Pick(edited.TypeFromCard, basis?.TypeFromCard ?? edited.TypeFromCard, fresh.TypeFromCard),
            A1 = Pick(edited.A1, basis?.A1 ?? edited.A1, fresh.A1),
            A1Country = PickText(edited.A1Country, basis?.A1Country, fresh.A1Country),
            WorkCountry = PickText(edited.WorkCountry, basis?.WorkCountry, fresh.WorkCountry),
            TreatyIncomeTaxRate = Pick(edited.TreatyIncomeTaxRate, basis?.TreatyIncomeTaxRate, fresh.TreatyIncomeTaxRate),
            TorEmploymentTypeId = PickText(edited.TorEmploymentTypeId, basis?.TorEmploymentTypeId, fresh.TorEmploymentTypeId),
            Notes = PickText(edited.Notes, basis?.Notes, fresh.Notes),
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
        // a resident has the basic exemption applied at this payer unless the accountant says otherwise; a non-resident
        // only by the EEA application the accountant enters
        // a person added as a non-resident is the accountant's word on the residency: checked by hand today, as a
        // residency set on the card is (a resident added by hand is still asked of the register)
        return _store.AddPerson(Person.New(companyId, firstName.Trim(), lastName.Trim(), code, residency) with { TorEmploymentTypeId = Clean(torEmploymentTypeId), BasicExemptionApplied = residency == Residency.Resident,
            ResidencySource = residency == Residency.NonResident ? LookupSource.Manual : LookupSource.Unknown,
            ResidencyCheckedOn = residency == Residency.NonResident ? Today() : null });
    }

    private ReviewPayment FromHit(Period period, MatchHit hit, Guid rowId, ReviewLine? overlay, Person? person, Guid? defaultPersonId, MatchPerson? register, MatchCatalog catalog, bool shared, IReadOnlyList<string> candidates, bool directionAssumed, bool twin = false)
    {
        var defaultCategory = hit.Verdict.Kind == VerdictKind.Matched ? hit.Verdict.Category?.Name : null;
        var category = overlay is { NoCategory: true } ? null : overlay?.Category ?? defaultCategory;
        var torType = ReviewDefaults.EmploymentTypeFor(register, person, hit.Row.Date);
        var defaultType = ReviewDefaults.TypeCodeFor(category is null ? null : catalog.Find(category), torType, person?.Residency ?? Residency.Resident);
        var typeCode = overlay?.TypeCode ?? defaultType;
        // the amount counts when it was read, the row is one person's, it is no copy of an earlier source's row, and it is in euros
        var euro = ReviewChecks.IsEuro(hit.Row.Currency);
        var duplicate = hit.Flags.Contains(MatchProblem.DuplicateRow, StringComparer.Ordinal);
        var amountOk = hit.Row.Amount is not null && (!shared || person is not null) && !duplicate;
        // money that came in is no payout, whatever category the accountant gave the row (decision 141)
        var moneyIn = hit.Row.Direction == Direction.Credit;
        var defaultDeclare = typeCode is not null && hit.InPeriod && amountOk && euro && person is not null && !twin && !moneyIn;
        var declare = overlay?.Declare ?? defaultDeclare;
        var blockers = ReviewChecks.Of(person, typeCode, hit.Amount, amountOk, hit.InPeriod, period.Year, RatesFor(period.Year), hit.Row.Currency, duplicate, hit.Row.Direction);
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
            AmountText = hit.Row.Amount is null ? hit.Row.AmountText ?? "" : null, // no amount read: its text, else nothing — never a 0 that looks read
            AmountOk = amountOk && euro,
            Direction = hit.Row.Direction,
            DirectionAssumed = directionAssumed,
            Currency = hit.Row.Currency,
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
            Checks = declare ? blockers : [],
            Blockers = blockers,
        };
    }

    private ReviewPayment FromHandLine(Period period, ReviewLine line, Person? person, MatchPerson? register, MatchCatalog catalog)
    {
        var torType = ReviewDefaults.EmploymentTypeFor(register, person, line.Date);
        var defaultType = ReviewDefaults.TypeCodeFor(line.Category is null ? null : catalog.Find(line.Category), torType, person?.Residency ?? Residency.Resident);
        var typeCode = line.TypeCode ?? defaultType;
        var inPeriod = line.Date is { } date && date.Year == period.Year && date.Month == period.Month;
        var amount = line.Amount ?? 0m;
        var defaultDeclare = typeCode is not null && inPeriod && person is not null;
        var declare = line.Declare ?? defaultDeclare;
        var blockers = ReviewChecks.Of(person, typeCode, amount, true, inPeriod, period.Year, RatesFor(period.Year));
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
            Checks = declare ? blockers : [],
            Blockers = blockers,
        };
    }

    private static ReviewPerson Make(string? key, Person? person, MatchPerson? register, List<ReviewPayment> payments, IReadOnlyList<MatchProblem> problems, MatchCatalog catalog, bool isUnmatched, bool totalsPerType)
    {
        var sums = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var payment in payments)
        {
            if (payment.Category is { } name && catalog.Find(name) is { IsSalary: true } && payment.InPeriod && payment.AmountOk && payment.Direction != Direction.Credit)
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
            Undeclared = UndeclaredOf(person, payments, catalog, isUnmatched, totalsPerType),
        };
    }

    /// <summary>The income of the month that stays out of the declaration, in the build's order of reasons: the person
    /// excluded on the card, a declared payment refused, a payment unticked by the accountant, one not declared by
    /// default. Income is a payment of the month (not a copy of another source's row, decision 86) of a salary or coded
    /// category, or given a type, and any payment ticked and refused; a codeless category's payment is found, never
    /// declared, and is not counted (decision 40). Under the totals cut a refused payment holds its whole type (decision
    /// 79): the type's other ticked payments are listed as held. A row in another currency is listed but is no error: it
    /// is declared in euros on a line added by hand (decision 87). On the rows nobody matched only a payment ticked to be
    /// declared counts: nobody carries it.</summary>
    private static List<Undeclared> UndeclaredOf(Person? person, List<ReviewPayment> payments, MatchCatalog catalog, bool isUnmatched, bool totalsPerType)
    {
        var list = new List<Undeclared>();
        foreach (var payment in payments)
        {
            if (isUnmatched)
            {
                if (payment.Declare && payment.Checks.Count > 0)
                {
                    list.Add(new Undeclared(payment, Undeclared.Refused, Codes(payment.Checks), false));
                }

                continue;
            }

            var category = payment.Category is { } name ? catalog.Find(name) : null;
            var income = (category is { IsSalary: true } or { Code: not null } || payment.TypeCode is not null)
                && payment.InPeriod && !payment.Flags.Contains(MatchProblem.DuplicateRow, StringComparer.Ordinal);
            var refused = payment.Declare && payment.Checks.Count > 0; // ticked and refused: the build says so, whatever it is
            if (!income && !refused)
            {
                continue;
            }

            if (person?.Declaration == DeclarationChoice.Exclude)
            {
                list.Add(new Undeclared(payment, Undeclared.Excluded, [], true));
            }
            else if (refused)
            {
                list.Add(new Undeclared(payment, Undeclared.Refused, Codes(payment.Checks), false));
            }
            else if (!payment.Declare && payment.DefaultDeclare)
            {
                list.Add(new Undeclared(payment, Undeclared.Unticked, [], true));
            }
            else if (!payment.Declare)
            {
                // no error of the review's: another currency is declared in euros by hand (87), money in is no payout (141)
                var byChoice = !ReviewChecks.IsEuro(payment.Currency) || payment.Direction == Direction.Credit;
                list.Add(new Undeclared(payment, Undeclared.NotDeclared, Codes(payment.Blockers), byChoice));
            }
        }

        if (totalsPerType && person?.Declaration != DeclarationChoice.Exclude)
        {
            var held = list.Where(u => u.Reason == Undeclared.Refused && u.Payment.TypeCode is not null).Select(u => u.Payment.TypeCode!).ToHashSet(StringComparer.Ordinal);
            foreach (var payment in payments.Where(p => p.Declare && p.Checks.Count == 0 && p.TypeCode is { } type && held.Contains(type)))
            {
                list.Add(new Undeclared(payment, Undeclared.Held, [Undeclared.TypeHeld], false));
            }
        }

        return list;

        static List<string> Codes(IReadOnlyList<Refusal> refusals) => refusals.Select(r => r.Code).Distinct(StringComparer.Ordinal).ToList();
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
