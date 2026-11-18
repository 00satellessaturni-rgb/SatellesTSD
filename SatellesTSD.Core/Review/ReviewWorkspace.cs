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

    /// <summary>The MTA non-resident code typed on the card is none for certain: a space or another sign in it (decision 155).</summary>
    public const string MrrInvalid = "MRR_INVALID";
    public const string CodeInvalid = ReviewChecks.CodeInvalid; // the card's save and the review's refusal name the fault alike
    public const string LineMissing = "LINE_MISSING";
    public const string AmountInvalid = "AMOUNT_INVALID";
    public const string SplitUnread = "SPLIT_UNREAD";
    public const string SplitCopy = "SPLIT_COPY";
    public const string SplitOwnShare = "SPLIT_OWN_SHARE";
    public const string SplitPartTooBig = "SPLIT_PART_TOO_BIG";

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

    /// <summary>The rule for a salary paid on a day two employments of different types are in force (the window's Seaded
    /// choice, decision 209).</summary>
    public TwoContractsRule TwoContracts { get; set; } = TwoContractsRule.SalaryToEmploymentContract;

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
        // a row's own overlay, and the parts split off its payment in the order they were made (decision 150)
        var overlays = lines.Where(l => !l.IsByHand && !l.IsPart).ToDictionary(l => l.RowId!.Value);
        var partsByRow = lines.Where(l => l.IsPart).GroupBy(l => l.RowId!.Value).ToDictionary(g => g.Key, g => g.ToList());

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
        // whether the month was searched against a register, as the build tells it (PeriodDeclarer): a "TÖR-väline palgad"
        // payment of no type is refused as OUTSIDE_TOR, worded by it
        var monthHasRegister = storedRun?.TorSheetId is not null || run?.Persons.Count > 0;
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
        var totalsLines = TotalsPerType ? TotalsLinesOf(standing) : [];
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
        var twinByWord = new HashSet<Guid>(); // rows a hand line would pair with but for the accountant's word on Declare: the word is kept as given
        List<MatchProblem> ProblemsOf(Guid id) => personProblems.TryGetValue(id, out var list) ? list : personProblems[id] = [];
        // a register person beside a store person of the same names under another personal code whom this month's register
        // does not list: the register may have corrected the code, and the card made anew carries the defaults, not the
        // accountant's earlier word on the other card — said, never merged (decision 197)
        var inRun = personsByKey.Values.Select(p => p.Id).ToHashSet();
        foreach (var person in personsByKey.Values.Where(p => p.PersonalCode is not null).DistinctBy(p => p.Id))
        {
            if (storePersons.FirstOrDefault(o => !inRun.Contains(o.Id) && o.PersonalCode is { } other && other != person.PersonalCode && o.RegisterKey is not null
                    && SearchPerson.NameKey(o.LastName, o.FirstName) == SearchPerson.NameKey(person.LastName, person.FirstName)) is { } namesake)
            {
                ProblemsOf(person.Id).Add(new MatchProblem(MatchProblem.NamesakeOtherCode, $"A person of the same names with another personal code ({namesake.PersonalCode}) is in the store and not in this month's register: if the register corrected the code, this card carries the defaults, not that card's choices (the exemption, the A1 case, Deklareerimine) — compare the two cards.", Args: [namesake.PersonalCode!]));
            }
        }

        var byPerson = new Dictionary<Guid, List<ReviewPayment>>();
        var unmatched = new List<ReviewPayment>();
        var leftOut = 0;
        // the sources whose layout only assumes the direction (the currency column read as the marker): by the layout the sheet
        // was read with (its snapshot, decision 14), since the owner's layouts may change under a run (136)
        var layouts = LayoutCatalog.Default.WithOverrides(_store.Layouts());
        var sheetsById = files.SelectMany(f => _store.Sheets(f.Id)).ToDictionary(s => s.Id);
        // a card export read by the layout of before 0.16.0 (the always-positive "Orig amount"): its rows all read as money
        // in, and since decision 141 money in is no payout — only reading the file again puts the amounts, the currency and
        // the direction right (decision 171)
        foreach (var stale in run?.Sources.Where(s => s.SheetId is { } id && sheetsById.TryGetValue(id, out var sheet) && sheet.Layout?.IsOldCardReading() == true) ?? [])
        {
            unmatchedProblems.Add(new MatchProblem(MatchProblem.OldReading, $"{stale.Name} was read by an earlier version, which took every amount of a card export as money in: its payouts are left out. Read the file again (Failid › Loe uuesti).", Source: stale.Name, Args: [stale.Name]));
        }

        // a sheet read before its layout read the transaction's state (decision 179): a reverted, declined or failed row
        // of it stands as a payment until the file is read again — said in a month that can still be put right
        if (period.State == PeriodState.Draft && run is not null && storedHits.Count == run.Hits.Count)
        {
            // only the rows the review would declare: paid to a person (by the search or by hand), money out, not taken
            // out of the declaration — a shop's declined card payment or a top-up never pending is no business of the TSD
            var hitRows = new Dictionary<(string Source, int RowNo), (Guid RowId, bool Person)>();
            for (var i = 0; i < run.Hits.Count; i++)
            {
                var key = (run.Hits[i].Source, run.Hits[i].Row.RowNo);
                hitRows[key] = (storedHits[i].RowId, (hitRows.TryGetValue(key, out var was) && was.Person) || run.Hits[i].PersonKey is not null);
            }

            bool Declared(MatchSource source, StatementRow row) => row.Direction == Direction.Debit && hitRows.TryGetValue((source.Name, row.RowNo), out var hit)
                && (hit.Person || (overlays.TryGetValue(hit.RowId, out var given) && given.PersonId is not null))
                && !(overlays.TryGetValue(hit.RowId, out var overlay) && overlay.Declare == false);
            foreach (var source in run.Sources)
            {
                if (source.SheetId is not { } id || !sheetsById.TryGetValue(id, out var sheet) || sheet.Layout is not { } snapshot || snapshot.IsOldCardReading()
                    || snapshot.Roles.State is not null || layouts.Find(snapshot.Id) is not { Roles.State: { } state, StatesNotDone: { Count: > 0 } notDone }
                    || snapshot.ColumnOf(state) is not (> 0 and var column))
                {
                    continue;
                }

                // the rows of the month whose stored state cell says the money did not move: named, and only when there are any
                var rows = source.Rows.Where(r => r.IsPayment && r.Date is { } day && day.Year == period.Year && day.Month == period.Month && column <= r.Cells.Count
                    && notDone.Any(s => !string.IsNullOrWhiteSpace(s) && string.Equals(s.Trim(), r.Cells[column - 1].Trim(), StringComparison.OrdinalIgnoreCase)) // a blank entry a layout stored before the save check may hold (the fifth audit, item 6)
                    && Declared(source, r)).Select(r => r.RowNo).ToList();
                if (rows.Count == 0)
                {
                    continue;
                }

                // the re-read's own refusals (decision 131): a month past Draft lists the file, or a line built from it has
                // been on the server (a month reopened for a correction keeps its lines' status)
                var listing = _store.Periods(period.CompanyId).Where(p => _store.PeriodImports(p.Id).Any(f => f.Id == sheet.FileId)).ToList();
                var fileRows = _store.Sheets(sheet.FileId).Where(s => s.Kind == SheetKind.Statement).SelectMany(s => _store.StatementRows(s.Id)).Select(r => r.Id).ToHashSet();
                var locked = listing.Any(p => p.State != PeriodState.Draft)
                    || listing.Any(p => _store.Payments(p.Id).Any(x => (x.Status != PaymentStatus.Draft || x.Version > 1)
                        && ((x.SourceRowId is { } rowId && fileRows.Contains(rowId)) || Declare.PeriodDeclarer.IsTotals(x))));
                unmatchedProblems.Add(new MatchProblem(MatchProblem.StateUnread,
                    $"{source.Name} was read by an earlier version, which did not read the transaction's state: row(s) {string.Join(", ", rows)} moved no money (reverted, declined, failed or pending) yet count as payments. "
                    + (locked ? "The file cannot be read again (a month past Draft lists it, or a line built from it has been on the server): take these rows out of the declaration (Deklareeri) by hand." : "Read the file again (Failid › Loe uuesti), or take these rows out of the declaration (Deklareeri) by hand."),
                    Source: source.Name, RowNo: rows[0], Subject: string.Join(", ", rows), Variant: locked ? MatchProblem.StateUnreadLocked : null));
            }
        }

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
                if (copy.Flags.Contains(MatchProblem.DuplicateRow, StringComparer.Ordinal) && partsByRow.ContainsKey(copyRow) && !overlays.ContainsKey(copyRow)
                    && copy.Text.StartsWith(copyOf, StringComparison.Ordinal))
                {
                    // parts split off a row that has since turned into a copy stay where they are: the copy is declared nowhere (a
                    // copy with a decision of its own as well is said once, below)
                    var what = period.State == PeriodState.Draft ? "split the original, and take them out with 0" : "check the original";
                    unmatchedProblems.Add(new MatchProblem(MatchProblem.DecisionOnCopy, $"{copy.Reference} is a copy of {copy.Text[copyOf.Length..]} and carries parts split off its payment; they stay where they are — {what}.", Source: copy.Source, RowNo: copy.Row.RowNo,
                        Variant: MatchProblem.CopyParts, Args: [copy.Reference, copy.Text[copyOf.Length..], period.State == PeriodState.Draft ? "draft" : "pastDraft"]));
                }

                if (!copy.Flags.Contains(MatchProblem.DuplicateRow, StringComparer.Ordinal) || !overlays.TryGetValue(copyRow, out var decided)
                    || !copy.Text.StartsWith(copyOf, StringComparison.Ordinal) || !rowByReference.TryGetValue(copy.Text[copyOf.Length..], out var originalRow))
                {
                    continue;
                }

                var original = copy.Text[copyOf.Length..];
                // a split copy stays together: its own share's decision was made for that share, not for the original's whole amount
                var split = partsByRow.ContainsKey(copyRow);
                if (period.State == PeriodState.Draft && !overlays.ContainsKey(originalRow) && !split)
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
                    var why = period.State != PeriodState.Draft ? "the month is past Draft" : split ? "it carries parts split off its payment" : "the original carries its own";
                    unmatchedProblems.Add(new MatchProblem(MatchProblem.DecisionOnCopy, $"{copy.Reference} is a copy of {original} and carries a decision; {why}, so it stays where it is — check the original.", Source: copy.Source, RowNo: copy.Row.RowNo,
                        Variant: period.State != PeriodState.Draft ? MatchProblem.CopyPastDraft : split ? MatchProblem.CopySplit : MatchProblem.CopyOriginalOwn, Args: [copy.Reference, original]));
                }
            }

            foreach (var marked in overlays.Values.Where(l => l.Note?.StartsWith(MovedFromMark, StringComparison.Ordinal) == true))
            {
                var mark = marked.Note![..(marked.Note.IndexOf('»') is var end and >= 0 ? end + 1 : marked.Note.Length)];
                unmatchedProblems.Add(new MatchProblem(MatchProblem.DecisionMoved, $"{mark}: a decision made on a copy was moved to the original row; check it, then clear the mark from the note.", Args: [mark]));
            }

            foreach (var rowId in rowOrder)
            {
                var indices = hitsByRow[rowId];
                overlays.TryGetValue(rowId, out var overlay);
                var shared = indices.Count > 1;
                var hit = run.Hits[indices[0]];
                var defaultPerson = !shared && hit.PersonKey is { } key && personsByKey.TryGetValue(key, out var dp) ? dp
                    : !shared && hit.PersonKey is null ? PayeeOf(hit, overlay, rowId) : null;
                var candidates = shared ? indices.Select(i => run.Hits[i].PersonKey).OfType<string>().Distinct().Select(k => personsByKey[k].FullName).ToList() : [];
                var parts = partsByRow.GetValueOrDefault(rowId) ?? [];
                // a part saved by 0.26–0.28 could say "as the search says" (no category, not marked none): no part is saved
                // so any more, so such a line is an old one and is settled here once, as the accountant sees it now — the
                // category the search gives the row today by name, else none — and follows no later search (decision 165)
                for (var n = 0; n < parts.Count; n++)
                {
                    if (parts[n] is { Category: null, NoCategory: false } old)
                    {
                        var settled = hit.Verdict.Kind == VerdictKind.Matched && hit.Verdict.Category?.Name is { } named ? old with { Category = named } : old with { NoCategory = true };
                        parts[n] = period.State == PeriodState.Draft ? _store.SaveReviewLine(settled) : settled;
                    }
                }

                // the row's own share is what its parts leave; each part is a payment of its own, with its own person and decisions
                var whole = hit.Amount;
                var rest = whole - parts.Sum(p => p.Amount!.Value);
                Emit(overlay, parts.Count == 0 ? null : rest, parts.Count == 0 ? 0 : 1, twinRows.Contains(rowId), twinRows.Contains(rowId) || twinByWord.Contains(rowId));
                for (var n = 0; n < parts.Count; n++)
                {
                    Emit(parts[n], parts[n].Amount, n + 2, twinRows.Contains(rowId), false); // a row a hand line already declares: none of its shares by default
                }

                void Emit(ReviewLine? decided, decimal? amount, int part, bool twin, bool handLineTwin)
                {
                    // a part of a row nobody of the register was found for has the payee its own category calls for (decision 108)
                    var shareDefault = part >= 2 && !shared && hit.PersonKey is null ? PayeeOf(hit, decided, rowId, pairTwin: false) : defaultPerson;
                    var personId = decided is { NoPerson: true } ? null : decided?.PersonId ?? shareDefault?.Id;
                    var person = personId is { } pid && byId.TryGetValue(pid, out var sp) ? sp : null;
                    var own = hit;
                    if (shared && person is not null)
                    {
                        // the copy the run made for the chosen person carries that person's masks and verdict
                        var mine = indices.FirstOrDefault(i => run.Hits[i].PersonKey is { } k && personsByKey.TryGetValue(k, out var p) && p.Id == person.Id, -1);
                        if (mine >= 0)
                        {
                            own = run.Hits[mine];
                        }
                    }

                    // the employment that decides the salary type: the chosen person's register entry, else (nobody's row) the run's
                    var register = person is not null
                        ? registerByPersonId.GetValueOrDefault(person.Id)
                        : own.PersonKey is { } hk ? run.Persons.FirstOrDefault(p => p.Key == hk) : null;
                    Place(FromHit(period, own, rowId, decided, person, shareDefault?.Id, register, catalog, shared, candidates, assumed.Contains(own.Source), monthHasRegister, twin, amount, part, handLineTwin));
                }
            }
        }

        foreach (var line in lines.Where(l => l.IsByHand))
        {
            var person = line.PersonId is { } pid && byId.TryGetValue(pid, out var sp) ? sp : null;
            Place(FromHandLine(period, line, person, run is not null && person is not null ? registerByPersonId.GetValueOrDefault(person.Id) : null, catalog, monthHasRegister));
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

                persons.Add(Make(registerPerson.Key, person, registerPerson, Take(person.Id), run.Problems.Where(p => p.PersonKey == registerPerson.Key).Concat(ProblemsOf(person.Id)).ToList(), catalog, false, TotalsPerType, totalsLines));
            }
        }

        foreach (var id in byPerson.Keys.OrderBy(id => byId[id].LastName, StringComparer.OrdinalIgnoreCase).ThenBy(id => byId[id].FirstName, StringComparer.OrdinalIgnoreCase).ToList())
        {
            persons.Add(Make(null, byId[id], null, Take(id), ProblemsOf(id), catalog, false, TotalsPerType, totalsLines));
        }

        // a person the card declares as if employed ("kaasa") whom the month's register does not list and who has no payment:
        // shown with the word, nothing built for them (decision 137) — a forgotten "kaasa" declares no minimum for one who left.
        // Only in a Draft that was searched: before the search the register is not known, and a month declared is read as it was
        if (run is not null && period.State == PeriodState.Draft)
        {
            foreach (var included in storePersons.Where(p => p.Declaration == DeclarationChoice.Include && persons.All(listed => listed.PersonId != p.Id))
                .OrderBy(p => p.LastName, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.FirstName, StringComparer.OrdinalIgnoreCase))
            {
                var said = new MatchProblem(MatchProblem.IncludedWithoutRow, "Declared as if employed on the card (kaasa), but the month's register lists no row for the person and no payment was found: nothing is built this month. Set the card's Deklareerimine to \"reeglite järgi\" if the employment has ended.", Args: []);
                persons.Add(Make(null, included, null, [], [said, .. ProblemsOf(included.Id)], catalog, false, TotalsPerType, totalsLines));
            }
        }

        persons.Add(Make(null, null, null, unmatched, unmatchedProblems, catalog, true, TotalsPerType, totalsLines));

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

        static void Once(List<MatchProblem> list, MatchProblem problem)
        {
            if (!list.Contains(problem))
            {
                list.Add(problem);
            }
        }

        // the payee of a row nobody of the register was found for, when its category is declared on the payee's own code
        // (Üür): a person of the company, created while the month is prepared; a month past Draft keeps its run as it was
        Person? PayeeOf(MatchHit hit, ReviewLine? overlay, Guid rowId, bool pairTwin = true)
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
                    || (Declare.PeriodDeclarer.TotalsOf(l.SourceRef) is { } totals && totals.References.Split("; ").Any(r => r == hit.Reference || r.StartsWith(hit.Reference + " " + ReviewPayment.PartWord + " ", StringComparison.Ordinal))));
                return built ? PayeePersons.Find(payeeKnown, counterparty, hit.Row.CounterpartyCode) : null;
            }

            var payee = PayeePersons.Ensure(_store, company.Id, counterparty, hit.Row.CounterpartyCode, payeeKnown, out var changed);
            if (payee is null)
            {
                PayeePersons.Find(payeeKnown, counterparty, hit.Row.CounterpartyCode, out var ambiguous);
                if (ambiguous)
                {
                    Once(unmatchedProblems, new MatchProblem(MatchProblem.PayeeAmbiguous, $"{hit.Reference}: two persons of the company have the payee's names; name the one on the row.", Source: hit.Source, RowNo: hit.Row.RowNo, Args: [hit.Reference])); // once for the row, however many of its shares ask
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
                Once(ProblemsOf(payee.Id), new MatchProblem(MatchProblem.PayeeCodeElsewhere, $"{hit.Reference}: the row's personal code belongs to another person of the company; the row is the payee's by the bound names — check the two cards.", Source: hit.Source, RowNo: hit.Row.RowNo, Args: [hit.Reference]));
            }

            // a line added by hand of the same amount for the payee this month, declaring something: the rent may be declared
            // there already — the row is not declared by default, and the person says so, until the accountant's tick or
            // untick on the row ends the word; one hand line pairs with one row
            if (pairTwin
                && lines.FirstOrDefault(l => l.IsByHand && !twinHandLines.Contains(l.Id) && l.PersonId == payee.Id && l.Amount == hit.Amount && l.Declare != false
                    && l.Date is { } day && day.Year == period.Year && day.Month == period.Month && HandLineDeclares(l, payee)) is { } twin)
            {
                if (overlay?.Declare is null)
                {
                    twinHandLines.Add(twin.Id);
                    twinRows.Add(rowId);
                    ProblemsOf(payee.Id).Add(new MatchProblem(MatchProblem.PayeeHandLine, $"{hit.Reference}: a line added by hand of the same amount stands for the person this month; the row is not declared by default (tick it when both are real).", Source: hit.Source, RowNo: hit.Row.RowNo, Args: [hit.Reference]));
                }
                else
                {
                    // the word ends the pairing: nothing paired, no hand line taken, nothing said; but the row's default turns
                    // on the word being stored, so the word is kept as given (the fifth audit, item 11)
                    twinByWord.Add(rowId);
                }
            }

            return payee;
        }

        // a hand line declares something when it has a type — its own or its category's for the person, as FromHandLine
        // gives it — and is ticked, or not unticked while its default declares it (decision 190)
        bool HandLineDeclares(ReviewLine line, Person person)
        {
            var found = line.Category is null ? null : catalog.Find(line.Category);
            var pick = ReviewDefaults.PickFor(run is not null ? registerByPersonId.GetValueOrDefault(person.Id) : null, person, line.Date, found is { IsSalary: true }, person.Residency, TwoContracts);
            var typeCode = line.TypeCode ?? (pick.Ask ? null : ReviewDefaults.TypeCodeFor(found, pick.TorTypeId, person.Residency));
            return typeCode is not null && (line.Declare ?? true);
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
                // a part never follows the run: none is stored as none, a category as the category named (decision 165)
                ? l with { Category = null, NoCategory = payment.IsPart || payment.DefaultCategory is not null, TypeCode = null }
                : l with { Category = !payment.IsPart && Same(clean, payment.DefaultCategory) ? null : clean, NoCategory = false, TypeCode = null },
            l => l with { Category = clean, NoCategory = false, TypeCode = null });
    }

    /// <summary>The payment type by the accountant's word; derived from the category and the person when null or equal to it.</summary>
    public ReviewLine? SetTypeCode(ReviewPayment payment, string? typeCode)
    {
        var clean = Clean(typeCode);
        return Change(payment, l => l with { TypeCode = Same(clean, payment.DefaultTypeCode) ? null : clean }, l => l with { TypeCode = Same(clean, payment.DefaultTypeCode) ? null : clean });
    }

    /// <summary>Whether the payment is declared, by the accountant's word; the default when null or equal to it.</summary>
    public ReviewLine? SetDeclare(ReviewPayment payment, bool? declare)
    {
        // a row with a hand-line twin: its default turns on whether a word is stored, so a word equal to the default shown is
        // still the accountant's — stored as no word it would bring the pairing back and undo itself (the fifth audit, item 11)
        bool? Stored() => declare is null ? null : payment.HandLineTwin || declare != payment.DefaultDeclare ? declare : null;
        return Change(payment, l => l with { Declare = Stored() }, l => l with { Declare = Stored() });
    }

    public ReviewLine? SetNote(ReviewPayment payment, string? note) =>
        Change(payment, l => l with { Note = Clean(note) }, l => l with { Note = Clean(note) });

    /// <summary>What a new amount typed on a payment would do (decision 150), asked before it is done; null when it is the
    /// amount the payment has. A row's own share given less splits the difference off as a new part; a part given another
    /// amount takes it from the row's own share or gives it back, and a part given 0 goes, its amount back to the row's
    /// own share; a hand line's amount is just changed. Refused (<see cref="ReviewException"/>): a month past Draft, an
    /// amount that is no whole cents or below 0, a row whose amount was not read or that is a copy of another, a row's own
    /// share given 0 or more than it has, and a part that would leave the row's own share nothing.</summary>
    public AmountChange? PlanAmount(ReviewPayment payment, decimal amount)
    {
        ArgumentNullException.ThrowIfNull(payment);
        RequireDraft(FindPeriod(payment.PeriodId));
        if (amount < 0m || amount != Math.Round(amount, 2))
        {
            throw new ReviewException(AmountInvalid, "An amount is whole cents, not below 0.");
        }

        if (amount == payment.Amount)
        {
            return null;
        }

        if (payment.IsByHand)
        {
            return new AmountChange(AmountChangeKind.HandLine, payment.Amount, amount, 0m, amount - payment.Amount);
        }

        var rowId = payment.RowId ?? throw new ArgumentException("A row payment names its row.", nameof(payment));
        if (payment.AmountText is not null || payment.RowAmount is not { } whole || payment.Flags.Contains(MatchProblem.BadAmount, StringComparer.Ordinal))
        {
            throw new ReviewException(SplitUnread, "The statement gave no amount for the row; there is nothing to split.");
        }

        if (payment.Flags.Contains(MatchProblem.DuplicateRow, StringComparer.Ordinal) && !(payment.IsPart && amount == 0m))
        {
            throw new ReviewException(SplitCopy, "The row is a copy of another; split the original (a part left on it is taken out with 0).");
        }

        // the row's own share by the store as it stands now, not by the payment the window still shows
        var parts = _store.ReviewLines(payment.PeriodId).Where(l => l.IsPart && l.RowId == rowId).ToList();
        var rest = whole - parts.Sum(p => p.Amount!.Value);
        if (!payment.IsPart)
        {
            if (amount <= 0m || amount >= rest)
            {
                throw new ReviewException(SplitOwnShare, $"The row's own share is {rest:0.00}: give it less to split the rest off as a part; a part's amount is lowered to give the row more.", rest);
            }

            return new AmountChange(AmountChangeKind.SplitOff, rest, amount, amount, rest - amount);
        }

        var line = parts.FirstOrDefault(l => l.Id == payment.LineId)
            ?? throw new ReviewException(LineMissing, "The part is no longer in the store.", variant: ReviewException.Part);
        var was = line.Amount!.Value;
        if (amount == 0m)
        {
            return new AmountChange(AmountChangeKind.PartRemoved, was, 0m, rest + was, was);
        }

        var left = rest + was - amount;
        if (left <= 0m)
        {
            throw new ReviewException(SplitPartTooBig, $"The row's own share is {rest:0.00}; the part can take at most {rest + was - 0.01m:0.00}.", rest + was - 0.01m);
        }

        return new AmountChange(AmountChangeKind.PartResized, was, amount, left, amount - was);
    }

    /// <summary>Does what <see cref="PlanAmount"/> says the amount would do; the part split off has the person the row's
    /// own share has by the accountant's word and no category (the row's own share keeps its own), the rest of its decisions
    /// the run's. Returns the change made, null for none.</summary>
    public AmountChange? SetAmount(ReviewPayment payment, decimal amount)
    {
        var change = PlanAmount(payment, amount);
        if (change is null)
        {
            return null;
        }

        var lines = _store.ReviewLines(payment.PeriodId);
        switch (change.Kind)
        {
            case AmountChangeKind.HandLine:
                var hand = lines.FirstOrDefault(l => l.Id == payment.LineId && l.IsByHand)
                    ?? throw new ReviewException(LineMissing, "The line added by hand is no longer in the store.", variant: ReviewException.HandLine);
                _store.SaveReviewLine(hand with { Amount = amount });
                break;
            case AmountChangeKind.SplitOff:
                var own = lines.FirstOrDefault(l => l.RowId == payment.RowId && !l.IsPart);
                // the part split off has no category until the accountant gives it one, whatever the run says of the row now or after a
                // later search: "none" is stored as the accountant's word, never as "follow the run" (decision 165)
                _store.SaveReviewLine(ReviewLine.PartOf(payment.PeriodId, payment.RowId!.Value, change.Difference, own?.PersonId, own?.NoPerson == true, noCategory: true));
                break;
            case AmountChangeKind.PartResized:
                _store.SaveReviewLine(lines.First(l => l.Id == payment.LineId) with { Amount = amount });
                break;
            case AmountChangeKind.PartRemoved:
                _store.DeleteReviewLine(payment.LineId!.Value);
                break;
        }

        return change;
    }

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

    /// <summary>Takes the basic exemption's application off every non-resident among the persons the period shows (its
    /// register persons and the persons its payments are given to): the exemption is ticked for every employee of the
    /// register by default and a non-resident is not given it by the payer. The residency and every other fact stay as
    /// they are. The persons changed are returned (decision 173).</summary>
    public IReadOnlyList<Person> ClearExemptionOfNonResidents(Guid periodId)
    {
        var view = Load(periodId);
        if (!view.Editable)
        {
            throw new ReviewException(PeriodNotDraft, $"The period is {view.Period.State}; the persons' facts are changed for a month while it is a Draft.");
        }

        var changed = new List<Person>();
        foreach (var person in NonResidentsWithExemption(view))
        {
            var off = person with { BasicExemptionApplied = false, BasicExemptionMonthly = null }; // the own figure goes with the application (decision 186)
            _store.UpdatePerson(off);
            changed.Add(off);
        }

        return changed;
    }

    /// <summary>The non-residents among the persons the period shows whose basic exemption's application is ticked, as the
    /// store holds them now.</summary>
    public IReadOnlyList<Person> NonResidentsWithExemption(Guid periodId) => NonResidentsWithExemption(Load(periodId));

    private List<Person> NonResidentsWithExemption(ReviewView view) =>
        NonResidentsShown(view).Where(p => p.BasicExemptionApplied).ToList();

    private List<Person> NonResidentsShown(ReviewView view)
    {
        var shown = view.Persons.Select(p => p.PersonId).OfType<Guid>().ToHashSet();
        return _store.Persons(view.Period.CompanyId).Where(p => shown.Contains(p.Id) && p.Residency == Residency.NonResident).ToList();
    }

    /// <summary>Ticks the basic exemption's application for every non-resident among the persons the period shows who has
    /// it unticked — the accountant's one word for all of them, as a tick set by hand on the card (decision 173) — except a
    /// person whose card names a country of tax residence outside the EEA, whom MTA refuses it (decision 144): those are
    /// left as they are and named. An own monthly figure is not given back (it went with the tick, decision 186): the
    /// legal allowance applies until one is entered. Every other fact stays. The persons ticked are returned.</summary>
    public ExemptionTicks GiveExemptionToNonResidents(Guid periodId)
    {
        var view = Load(periodId);
        if (!view.Editable)
        {
            throw new ReviewException(PeriodNotDraft, $"The period is {view.Period.State}; the persons' facts are changed for a month while it is a Draft.");
        }

        var (due, outside) = NonResidentsWithoutExemption(view);
        var ticked = new List<Person>();
        foreach (var person in due)
        {
            var on = person with { BasicExemptionApplied = true, BasicExemptionMonthly = null }; // the legal allowance: an own figure left on an unticked card is not given back
            _store.UpdatePerson(on);
            ticked.Add(on);
        }

        return new ExemptionTicks(ticked, outside);
    }

    /// <summary>The non-residents among the persons the period shows whose basic exemption's application is unticked, as
    /// the store holds them now: those it would be ticked for, and those left out for a country outside the EEA.</summary>
    public ExemptionTicks NonResidentsWithoutExemption(Guid periodId)
    {
        var (due, outside) = NonResidentsWithoutExemption(Load(periodId));
        return new ExemptionTicks(due, outside);
    }

    private (List<Person> Due, List<Person> OutsideEea) NonResidentsWithoutExemption(ReviewView view)
    {
        var without = NonResidentsShown(view).Where(p => !p.BasicExemptionApplied).ToList();
        return (without.Where(p => !PaymentChecks.OutsideEea(p.ResidenceCountry)).ToList(), without.Where(p => PaymentChecks.OutsideEea(p.ResidenceCountry)).ToList());
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
        // refused as typed: a code the store already holds (saved before this was checked) does not stand in the way of
        // another edit of the card — the export names it if the person is declared by it
        var mrr = PickText(edited.MrrCode, basis?.MrrCode, fresh.MrrCode);
        if (!string.Equals(mrr, Clean(fresh.MrrCode), StringComparison.Ordinal) && Xbrl.MrrCodes.Problem(mrr) is { } mrrProblem)
        {
            throw new ReviewException(MrrInvalid, mrrProblem, subject: mrr);
        }

        // the registers answered for the code as it was: a corrected code drops what they said (residency, pension, the
        // days of the checks, the control date), so the review asks for the registers again under the new code — a fact
        // the accountant set by hand stands (decision 180)
        var codeChanged = !string.Equals(code, Clean(fresh.PersonalCode), StringComparison.Ordinal);
        var dropResidency = codeChanged && !residencyChanged && fresh.ResidencySource == LookupSource.Emta;
        var dropPension = codeChanged && !pensionChanged && fresh.PensionSource is LookupSource.Pensionikeskus or LookupSource.Derived;
        var exemptionApplied = Pick(edited.BasicExemptionApplied, basis?.BasicExemptionApplied ?? edited.BasicExemptionApplied, fresh.BasicExemptionApplied);
        var next = fresh with
        {
            FirstName = edited.FirstName.Trim(),
            LastName = edited.LastName.Trim(),
            PersonalCode = code,
            MrrCode = mrr,
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
            BasicExemptionApplied = exemptionApplied,
            // the person's own figure goes with the application: without it the figure refuses every payment
            // (EXEMPTION_WITHOUT_APPLICATION), as the lookup and the non-residents' command already drop it (the fifth audit, item 20)
            BasicExemptionMonthly = exemptionApplied ? Pick(edited.BasicExemptionMonthly, basis?.BasicExemptionMonthly, fresh.BasicExemptionMonthly) : null,
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
        if (dropResidency)
        {
            next = next with { ResidencyCheckedOn = null, ResidencySource = LookupSource.Unknown };
        }

        if (dropPension)
        {
            next = next with { FundedPensionMember = null, PensionRate = null, PensionCheckedOn = null, PensionSource = LookupSource.Unknown };
        }

        if (codeChanged)
        {
            next = next with { FactsControlDate = null };
        }

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

    /// <param name="partAmount">The share of a split row this payment is (its own share or a part's); null for a row not split.</param>
    /// <param name="part">The payment's place among the row's parts (<see cref="ReviewPayment.Part"/>).</param>
    /// <param name="monthHasRegister">The month was searched against a register (words the OUTSIDE_TOR refusal).</param>
    private ReviewPayment FromHit(Period period, MatchHit hit, Guid rowId, ReviewLine? overlay, Person? person, Guid? defaultPersonId, MatchPerson? register, MatchCatalog catalog, bool shared, IReadOnlyList<string> candidates, bool directionAssumed, bool monthHasRegister, bool twin = false, decimal? partAmount = null, int part = 0, bool handLineTwin = false)
    {
        var amount = partAmount ?? hit.Amount;
        var defaultCategory = hit.Verdict.Kind == VerdictKind.Matched ? hit.Verdict.Category?.Name : null;
        var category = overlay is { NoCategory: true } ? null : overlay?.Category ?? defaultCategory;
        var found = category is null ? null : catalog.Find(category);
        var residency = person?.Residency ?? Residency.Resident;
        var pick = ReviewDefaults.PickFor(register, person, hit.Row.Date, found is { IsSalary: true }, residency, TwoContracts);
        var defaultType = pick.Ask ? null : ReviewDefaults.TypeCodeFor(found, pick.TorTypeId, residency);
        var typeCode = overlay?.TypeCode ?? defaultType;
        var asking = pick.Ask && typeCode is null; // a type set by hand ends the question (decision 209)
        // the amount counts when it was read, the row is one person's, it is no copy of an earlier source's row, and it is in euros
        var euro = ReviewChecks.IsEuro(hit.Row.Currency);
        var duplicate = hit.Flags.Contains(MatchProblem.DuplicateRow, StringComparer.Ordinal);
        var amountOk = hit.Row.Amount is not null && (!shared || person is not null) && !duplicate;
        // money that came in is no payout, whatever category the accountant gave the row (decision 141)
        var moneyIn = hit.Row.Direction == Direction.Credit;
        // a salary asked for its type stays to declare, refused until it is chosen: dropped, it would leave the month unpaid
        var defaultDeclare = (typeCode is not null || asking) && hit.InPeriod && amountOk && euro && person is not null && !twin && !moneyIn;
        var declare = overlay?.Declare ?? defaultDeclare;
        var outsideTor = OutsideTorOf(category, typeCode, register, monthHasRegister);
        var blockers = ReviewChecks.Of(person, typeCode, amount, amountOk, hit.InPeriod, period.Year, RatesFor(period.Year), hit.Row.Currency, duplicate, hit.Row.Direction, asking ? pick.InForce : null, outsideTor);
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
            Amount = amount,
            RowAmount = hit.Amount,
            Part = part,
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
            ConflictingCategories = hit.Verdict.Kind == VerdictKind.Ambiguous ? hit.Verdict.Matched : [],
            DefaultCategory = defaultCategory,
            Category = category,
            DefaultTypeCode = defaultType,
            TypeCode = typeCode,
            ContractTypes = pick.InForce,
            DefaultDeclare = defaultDeclare,
            Declare = declare,
            HandLineTwin = handLineTwin,
            InPeriod = hit.InPeriod,
            Counted = hit.Counted,
            Flags = hit.Flags,
            IsShared = shared,
            Candidates = candidates,
            Note = overlay?.Note,
            HasOverride = overlay is not null,
            Checks = declare ? blockers : [],
            Blockers = blockers,
            OutsideTor = outsideTor,
            Notices = declare && !moneyIn ?ReviewChecks.NoticesOf(overlay?.TypeCode, defaultType, pick.TorTypeId, found is { IsSalary: true }, amount) : [],
        };
    }

    /// <summary>Why a payment is refused as OUTSIDE_TOR, null when it is not: a "TÖR-väline palgad" payment of no type
    /// whose person the month's register does not list, or of a month searched with no register. The category stays
    /// codeless and the row undeclared (decisions 10, 40, 100); only the refusal says what to do.</summary>
    private static OutsideTorReason? OutsideTorOf(string? category, string? typeCode, MatchPerson? register, bool monthHasRegister) =>
        typeCode is null && register is null && string.Equals(category, ReviewDefaults.OutsideTorCategory, StringComparison.Ordinal)
            ? monthHasRegister ? OutsideTorReason.NotListed : OutsideTorReason.NoRegister
            : null;

    private ReviewPayment FromHandLine(Period period, ReviewLine line, Person? person, MatchPerson? register, MatchCatalog catalog, bool monthHasRegister)
    {
        var found = line.Category is null ? null : catalog.Find(line.Category);
        var residency = person?.Residency ?? Residency.Resident;
        var pick = ReviewDefaults.PickFor(register, person, line.Date, found is { IsSalary: true }, residency, TwoContracts);
        var defaultType = pick.Ask ? null : ReviewDefaults.TypeCodeFor(found, pick.TorTypeId, residency);
        var typeCode = line.TypeCode ?? defaultType;
        var asking = pick.Ask && typeCode is null;
        var inPeriod = line.Date is { } date && date.Year == period.Year && date.Month == period.Month;
        var amount = line.Amount ?? 0m;
        var defaultDeclare = (typeCode is not null || asking) && inPeriod && person is not null;
        var declare = line.Declare ?? defaultDeclare;
        var outsideTor = OutsideTorOf(line.Category, typeCode, register, monthHasRegister);
        var blockers = ReviewChecks.Of(person, typeCode, amount, true, inPeriod, period.Year, RatesFor(period.Year), twoContracts: asking ? pick.InForce : null, outsideTor: outsideTor);
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
            ContractTypes = pick.InForce,
            DefaultDeclare = defaultDeclare,
            Declare = declare,
            InPeriod = inPeriod,
            Counted = true,
            Flags = [],
            Note = line.Note,
            HasOverride = true,
            Checks = declare ? blockers : [],
            Blockers = blockers,
            OutsideTor = outsideTor,
            Notices = declare ? ReviewChecks.NoticesOf(line.TypeCode, defaultType, pick.TorTypeId, found is { IsSalary: true }, amount) : [],
        };
    }

    /// <summary>The totals lines standing for each person (a draft's and, beneath it, the line it stands on), as their type
    /// and the payments' references they add up: under the totals cut a refused payment holds the type whose line adds
    /// it up, whatever type it has now — the build holds the same (decision 199).</summary>
    private Dictionary<Guid, List<(string TypeCode, string[] References)>> TotalsLinesOf(IReadOnlyList<StoredPayment> standing)
    {
        var byPerson = new Dictionary<Guid, List<(string, string[])>>();
        foreach (var line in standing.Where(s => s.PersonId is not null && Declare.PeriodDeclarer.IsTotals(s)))
        {
            var versions = new List<StoredPayment>();
            if (line.Status != PaymentStatus.Draft)
            {
                versions.Add(line);
            }
            else
            {
                var history = _store.History(line.PeriodId, line.Section, line.LineNumber).Where(v => v.Id != line.Id).ToList();
                if ((history.LastOrDefault(v => v.Status != PaymentStatus.Rejected) ?? history.LastOrDefault()) is { } beneath)
                {
                    versions.Add(beneath);
                }
            }

            // only a version on the server holds a type, as the build's own rule (a draft alone or a line refused black holds nothing)
            foreach (var version in versions.Where(v => v.OnServer && v.Action != LineAction.T))
            {
                if (Declare.PeriodDeclarer.TotalsOf(version.SourceRef) is { } totals)
                {
                    if (!byPerson.TryGetValue(line.PersonId!.Value, out var list))
                    {
                        byPerson[line.PersonId!.Value] = list = [];
                    }

                    list.Add((totals.TypeCode, totals.References.Split("; ")));
                }
            }
        }

        return byPerson;
    }

    private static ReviewPerson Make(string? key, Person? person, MatchPerson? register, List<ReviewPayment> payments, IReadOnlyList<MatchProblem> problems, MatchCatalog catalog, bool isUnmatched, bool totalsPerType, Dictionary<Guid, List<(string TypeCode, string[] References)>> totalsLines)
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
        // salary-pattern money paid to the person outside the register is said of the person too (OUTSIDE_TOR_SALARY): a
        // problem in the persons grid (yellow as any problem, plan line 322), never a figure of the Deklareerimata sum
        // (decision 100). Its figure adds the euro amounts read, as that sum does. The rows nobody was found for have no
        // person to say it of: their rows carry the refusal alone.
        if (person is not null && !isUnmatched)
        {
            var outside = payments.Where(p => p.OutsideTor is not null && p.InPeriod && p.Direction != Direction.Credit
                && !p.Flags.Contains(MatchProblem.DuplicateRow, StringComparer.Ordinal)).ToList();
            if (outside.Count > 0)
            {
                var sum = outside.Where(p => p.AmountText is null && ReviewChecks.IsEuro(p.Currency)).Sum(p => p.Amount)
                    .ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                problems = [.. problems, new MatchProblem(MatchProblem.OutsideTorSalary, $"{outside.Count} salary-pattern payment(s) ({sum} EUR) to a person outside TÖR are not declared: register the work in TÖR, or give the row a category or a payment type.", Args: [outside.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), sum])];
            }
        }

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
            Undeclared = UndeclaredOf(person, payments, catalog, isUnmatched, totalsPerType, person is not null && totalsLines.TryGetValue(person.Id, out var lines) ? lines : []),
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
    private static List<Undeclared> UndeclaredOf(Person? person, List<ReviewPayment> payments, MatchCatalog catalog, bool isUnmatched, bool totalsPerType, List<(string TypeCode, string[] References)> totalsLines)
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
            var refusedPayments = list.Where(u => u.Reason == Undeclared.Refused).Select(u => u.Payment).ToList();
            var held = refusedPayments.Where(p => p.TypeCode is not null).Select(p => p.TypeCode!).ToHashSet(StringComparer.Ordinal);
            held.UnionWith(totalsLines.Where(t => refusedPayments.Any(p => Declare.PeriodDeclarer.AddsUp(t.References, p))).Select(t => t.TypeCode));
            foreach (var payment in payments.Where(p => p.Declare && p.Checks.Count == 0 && p.TypeCode is { } type && held.Contains(type)))
            {
                list.Add(new Undeclared(payment, Undeclared.Held, [Undeclared.TypeHeld], false));
            }
        }

        return list;

        static List<string> Codes(IReadOnlyList<Refusal> refusals) => refusals.Select(r => r.Code).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Applies a change to the payment's stored decision: a row's overlay is found by the row (never by a
    /// possibly stale line id), a part of a row and a hand line by their own id.</summary>
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
                throw new ReviewException(LineMissing, "The line added by hand is no longer in the store.", variant: ReviewException.HandLine);
            }

            return _store.SaveReviewLine(hand(existingHand));
        }

        var rowId = payment.RowId ?? throw new ArgumentException("A row payment names its row.", nameof(payment));
        if (payment.IsPart)
        {
            var part = lines.FirstOrDefault(l => l.Id == payment.LineId && l.IsPart && l.RowId == rowId)
                ?? throw new ReviewException(LineMissing, "The part is no longer in the store.", variant: ReviewException.Part);
            return _store.SaveReviewLine(overlay(part)); // a part keeps its line whatever it decides: its amount is a decision
        }

        var existing = lines.FirstOrDefault(l => l.RowId == rowId && !l.IsPart);
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
            throw new ReviewException(CodeInvalid, $"'{code}' is not an Estonian personal code; a foreign person is named by the MTA non-resident code instead.", subject: code);
        }

        if (_store.FindPerson(companyId, code) is { } other && other.Id != self)
        {
            throw new ReviewException(PersonExists, $"Another person of the company has the personal code {code}.", subject: code);
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
