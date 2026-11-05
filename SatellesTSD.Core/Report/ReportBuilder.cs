using System.Globalization;
using SatellesTSD.Core.Declare;
using SatellesTSD.Core.Import;
using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Review;
using SatellesTSD.Core.Store;

namespace SatellesTSD.Core.Report;

/// <summary>Builds the report-details workbook of a period as the macro workbook built it — the Vahearuanne with its
/// sub-tables, TOR kontroll, Otsingu kontroll, Tundmatu kategooria, a sheet per reporting category, a copy of every
/// statement with "Vaste leitud" — from the review as it stands (the accountant's word, not the run's), plus a
/// Deklaratsioon sheet with the declared lines. What the workbook got wrong is not copied: TOR kontroll's texts sit
/// under the statement they were found on, and a statement row's "Vaste leitud" names everyone found on it.</summary>
public sealed class ReportBuilder(IStore store, ReviewWorkspace workspace, PeriodDeclarer declarer, TimeProvider? clock = null)
{
    private readonly IStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly ReviewWorkspace _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
    private readonly PeriodDeclarer _declarer = declarer ?? throw new ArgumentNullException(nameof(declarer));
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>The Otsingu kontroll headers, in the workbook's order and spelling, with the twelfth column of ours.</summary>
    public static readonly IReadOnlyList<string> SearchHeaders =
    [
        ReportText.SearchPersonalCode, ReportText.LastName, ReportText.FirstName, ReportText.Document, ReportText.Date, ReportText.PayeeOnStatement,
        ReportText.Masks, ReportText.DebitCredit, ReportText.Amount, ReportText.Description, ReportText.Verdict, ReportText.Decision,
    ];

    public static readonly IReadOnlyList<string> DeclarationHeaders =
    [
        ReportText.Section, ReportText.Key, ReportText.Action, ReportText.Status, ReportText.Person, ReportText.PayeeId, ReportText.Type, ReportText.DocumentDate,
        ReportText.Net, ReportText.Gross, ReportText.SocialTax, ReportText.MinimumIncrease, ReportText.FundedPension, ReportText.UnemploymentEmployee,
        ReportText.UnemploymentEmployer, ReportText.IncomeTax, ReportText.ExemptionUsed, ReportText.Source, ReportText.Feedback, ReportText.Version,
    ];

    /// <summary>The whole workbook of the period. Reads the store (and, as every load of the review does, creates the
    /// register's persons when they are new), so it runs where the store's other calls run.</summary>
    public ReportBook Build(Guid periodId)
    {
        var view = _workspace.Load(periodId);
        var at = _clock.GetLocalNow().DateTime;
        var context = new Context(view, _store, _workspace.Rates);
        var sheets = new List<ReportSheet> { Interim(context), };
        if (TorSheet(context) is { } tor)
        {
            sheets.Add(tor);
        }

        sheets.Add(Search(context, ReportText.SearchSheet, (_, _) => true));
        sheets.Add(Search(context, ReportText.UnknownCategorySheet, Undecided));
        foreach (var category in view.Catalog.Categories.Where(c => c.ReportSheet))
        {
            sheets.Add(Search(context, category.Name, (_, p) => string.Equals(p.Category, category.Name, StringComparison.OrdinalIgnoreCase)));
        }

        sheets.AddRange(StatementCopies(context));
        sheets.Add(Declaration(_declarer.Lines(periodId)));
        return new ReportBook(ReportBook.FileNameFor(view.Company.Name, at), sheets);
    }

    /// <summary>What "Tundmatu kategooria" lists, as the macro listed it: a person's row of the month without a
    /// category (a row outside the month or with an unreadable date went to Otsingu kontroll alone), and a row nobody
    /// has only when the run had something to say about it (a verdict text; a row no pattern touched was left off).</summary>
    public static bool Undecided(ReviewPerson owner, ReviewPayment payment)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(payment);
        return payment.Category is null && (owner.IsUnmatched ? payment.VerdictText.Length > 0 : payment.InPeriod);
    }

    // ---------------------------------------------------------------- Vahearuanne

    private static ReportSheet Interim(Context context)
    {
        var view = context.View;
        var sources = view.Sources;
        var rows = new List<ReportRow>();
        var header = new List<string?> { ReportText.PersonalCode, ReportText.LastName, ReportText.FirstName };
        header.AddRange(sources.Select(SourceHeader));
        header.AddRange([ReportText.SalaryTotal, ReportText.TaxFree, ReportText.Residency, ReportText.Pension, ReportText.ControlDate, ReportText.EmploymentStart, ReportText.EmploymentType, ReportText.EntryState, ReportText.EmploymentEnd]);
        rows.Add(ReportRow.Headers(header.ToArray()));
        foreach (var person in view.Persons.Where(p => !p.IsUnmatched))
        {
            var cells = new List<ReportCell> { ReportCell.Identifier(person.PersonalCode), ReportCell.Of(person.LastName), ReportCell.Of(person.FirstName) };
            cells.AddRange(sources.Select(s => person.SalaryBySource.TryGetValue(s, out var sum) ? ReportCell.Amount(sum) : ReportCell.Empty));
            cells.Add(ReportCell.Amount(person.SalaryTotal));
            var store = person.Person;
            var entry = person.Register is { Entries.Count: > 0 } register ? register.Entry : null;
            cells.Add(store is { BasicExemptionApplied: true } ? ReportCell.Amount(store.ExemptionMonthInForce(view.Period.Year, context.Rates)) : ReportCell.Empty);
            cells.Add(store is null ? ReportCell.Empty : ReportCell.Of(ReportText.ResidencyText(store)));
            cells.Add(store is null ? ReportCell.Empty : ReportCell.Of(ReportText.PensionText(store)));
            cells.Add(ReportCell.Day(CheckedOn(store)));
            cells.Add(ReportCell.Day(entry?.StartDate));
            cells.Add(ReportCell.Of(store is { TypeFromCard: true } && EmploymentTypes.Find(store.TorEmploymentTypeId) is { } cardType ? cardType.Name : entry?.Type)); // the type that applies, as the grid shows it
            cells.Add(ReportCell.Of(entry?.State));
            cells.Add(ReportCell.Day(entry?.EndDate));
            rows.Add(new ReportRow(cells));
        }

        foreach (var category in view.Catalog.Categories.Where(c => c.SubTable))
        {
            // the workbook: the title five rows below the last used row, so four blank rows between
            rows.AddRange(Enumerable.Repeat(ReportRow.Blank, 4));
            rows.Add(ReportRow.Texts(category.Name));
            var subHeader = new List<string?> { ReportText.PersonalCode, ReportText.LastName, ReportText.FirstName };
            subHeader.AddRange(sources.Select(SourceHeader));
            subHeader.Add(ReportText.Total);
            rows.Add(ReportRow.Headers(subHeader.ToArray()));
            foreach (var row in SubTable(view, category.Name))
            {
                var cells = new List<ReportCell> { ReportCell.Identifier(row.Code), ReportCell.Of(row.LastName), ReportCell.Of(row.FirstName) };
                cells.AddRange(sources.Select(s => row.BySource.TryGetValue(s, out var sum) ? ReportCell.Amount(sum) : ReportCell.Empty));
                cells.Add(ReportCell.Amount(row.BySource.Values.Sum()));
                rows.Add(new ReportRow(cells));
            }
        }

        return new ReportSheet(ReportText.InterimSheet, rows);
    }

    private sealed record SubRow(string? Code, string LastName, string FirstName, IReadOnlyDictionary<string, decimal> BySource);

    /// <summary>The sub-table of a category from what stands: the persons in the view's order with their rows of the
    /// category in the month that count, then the rows nobody has, by the payee's name on the statement (in the
    /// last-name column, as the macro put them — and of any month, as the macro's second pass never asked the date).</summary>
    private static IEnumerable<SubRow> SubTable(ReviewView view, string categoryName)
    {
        static bool Counts(ReviewPayment p, string name) => string.Equals(p.Category, name, StringComparison.OrdinalIgnoreCase) && p.AmountOk;
        foreach (var person in view.Persons.Where(p => !p.IsUnmatched))
        {
            var own = person.Payments.Where(p => Counts(p, categoryName) && p.InPeriod).ToList();
            if (own.Count > 0)
            {
                yield return new SubRow(person.PersonalCode, person.LastName, person.FirstName, Sums(own));
            }
        }

        foreach (var group in view.Unmatched.Payments.Where(p => Counts(p, categoryName)).GroupBy(p => p.Payee, StringComparer.OrdinalIgnoreCase))
        {
            yield return new SubRow(null, group.Key, "", Sums(group.ToList()));
        }
    }

    private static IReadOnlyDictionary<string, decimal> Sums(IReadOnlyList<ReviewPayment> payments) =>
        payments.GroupBy(p => p.Source, StringComparer.Ordinal).ToDictionary(g => g.Key, g => Math.Round(g.Sum(p => p.Amount), 2, MidpointRounding.ToEven), StringComparer.Ordinal);

    // ---------------------------------------------------------------- Otsingu kontroll and the category sheets

    private static ReportSheet Search(Context context, string name, Func<ReviewPerson, ReviewPayment, bool> take)
    {
        var rows = new List<ReportRow> { ReportRow.Headers(SearchHeaders.ToArray<string?>()) };
        foreach (var person in context.View.Persons)
        {
            foreach (var payment in person.Payments.Where(p => take(person, p)))
            {
                var row = payment.RowId is { } rowId ? context.Row(rowId) : null;
                rows.Add(new ReportRow(
                [
                    person.IsUnmatched ? ReportCell.Empty : ReportCell.Identifier(person.PersonalCode),
                    ReportCell.Of(person.IsUnmatched ? null : person.LastName),
                    ReportCell.Of(person.IsUnmatched ? null : person.FirstName),
                    ReportCell.Of(Reference(payment)),
                    payment.Date is { } date ? ReportCell.Day(date) : ReportCell.Of(payment.DateText),
                    ReportCell.Of(payment.Payee),
                    ReportCell.Of(payment.Masks),
                    ReportCell.Of(DebitCredit(row)),
                    AmountCell(payment, row),
                    ReportCell.Of(payment.Description),
                    ReportCell.Of(payment.VerdictText),
                    ReportCell.Of(Decision(person, payment)),
                ]));
            }
        }

        return new ReportSheet(name, rows);
    }

    /// <summary>The accountant's word where it differs from the run's: the person, the category, the type, whether it
    /// is declared, the note; a shared row's candidates while nobody is named.</summary>
    public static string Decision(ReviewPerson owner, ReviewPayment payment)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(payment);
        var parts = new List<string>();
        if (payment.IsShared && payment.PersonId is null)
        {
            parts.Add($"{ReportText.Shared} {string.Join("; ", payment.Candidates)}");
        }
        else if (payment.PersonId != payment.DefaultPersonId)
        {
            parts.Add(owner.IsUnmatched ? ReportText.Nobody : $"{owner.LastName} {owner.FirstName}".Trim());
        }

        if (!string.Equals(payment.Category, payment.DefaultCategory, StringComparison.Ordinal))
        {
            parts.Add(payment.Category is null ? ReportText.NoCategory : $"[{payment.Category}]");
        }

        if (!string.Equals(payment.TypeCode, payment.DefaultTypeCode, StringComparison.Ordinal))
        {
            parts.Add(payment.TypeCode ?? "-");
        }

        if (payment.Declare != payment.DefaultDeclare)
        {
            parts.Add(payment.Declare ? ReportText.Declared : ReportText.NotDeclared);
        }

        if (!string.IsNullOrWhiteSpace(payment.Note))
        {
            parts.Add(payment.Note.Trim());
        }

        return string.Join("; ", parts);
    }

    // ---------------------------------------------------------------- the statement copies

    private static IEnumerable<ReportSheet> StatementCopies(Context context)
    {
        var view = context.View;
        if (view.Run is null)
        {
            yield break;
        }

        // every person found on a row, for "Vaste leitud"
        var notes = new Dictionary<Guid, List<string>>();
        foreach (var person in view.Persons)
        {
            foreach (var payment in person.Payments.Where(p => p.RowId is not null))
            {
                var note = FoundNote(person, payment);
                if (note.Length > 0)
                {
                    if (!notes.TryGetValue(payment.RowId!.Value, out var list))
                    {
                        notes[payment.RowId.Value] = list = [];
                    }

                    list.Add(note);
                }
            }
        }

        foreach (var source in view.Run.Sources)
        {
            if (source.SheetId is not { } sheetId || context.Store.Sheet(sheetId) is not { } sheet)
            {
                continue;
            }

            var stored = context.Store.StatementRows(sheetId);
            var layout = sheet.Layout;
            var dateColumn = layout is null ? 0 : layout.ColumnOf(layout.Roles.Date);
            var amountColumn = layout is null ? 0 : layout.ColumnOf(layout.Roles.Amount);
            var width = Math.Max(sheet.Headers.Count, stored.Count == 0 ? 0 : stored.Max(r => r.Row.Cells.Count));
            var lastRow = Math.Max(sheet.HeaderRowNo, stored.Count == 0 ? 0 : stored.Max(r => r.Row.RowNo));
            var rows = new ReportRow[lastRow];
            Array.Fill(rows, ReportRow.Blank);
            if (sheet.HeaderRowNo >= 1)
            {
                var header = sheet.Headers.Concat(Enumerable.Repeat("", width - sheet.Headers.Count)).Append(ReportText.MatchFound).Select(ReportCell.Of).ToList();
                rows[sheet.HeaderRowNo - 1] = new ReportRow(header, header: true);
            }

            foreach (var row in stored)
            {
                if (row.Row.RowNo < 1 || row.Row.RowNo > lastRow)
                {
                    continue;
                }

                var cells = new List<ReportCell>(width + 1);
                for (var c = 1; c <= width; c++)
                {
                    var text = c <= row.Row.Cells.Count ? row.Row.Cells[c - 1] : "";
                    cells.Add(
                        c == dateColumn && row.Row.Date is { } date ? ReportCell.Day(date)
                        : c == amountColumn && row.Row.Amount is { } amount ? ReportCell.Amount(amount)
                        : ReportCell.Of(text));
                }

                cells.Add(notes.TryGetValue(row.Id, out var found) ? ReportCell.Of(string.Join("\n", found)) : ReportCell.Empty);
                rows[row.Row.RowNo - 1] = new ReportRow(cells);
            }

            yield return new ReportSheet(source.Name.Replace("*", "", StringComparison.Ordinal), rows);
        }
    }

    /// <summary>The workbook's note on a statement row: the register's "Last First" and the category; here the person
    /// the row stands with, their category after the accountant's word ("[puudub]" where the accountant took the run's
    /// away), the candidates while a shared row is nobody's, and the run's verdict for a row nobody has.</summary>
    private static string FoundNote(ReviewPerson person, ReviewPayment payment)
    {
        var category = payment.Category is { } c ? $"[{c}]" : payment.DefaultCategory is not null ? $"[{ReportText.NoCategory}]" : payment.VerdictText;
        if (!person.IsUnmatched)
        {
            return $"{person.LastName} {person.FirstName} {category}".Trim();
        }

        if (payment.IsShared && payment.Candidates.Count > 0)
        {
            return $"{ReportText.Shared} {string.Join("; ", payment.Candidates)} {category}".Trim();
        }

        return category.Trim();
    }

    // ---------------------------------------------------------------- TOR kontroll

    private static ReportSheet? TorSheet(Context context)
    {
        var view = context.View;
        var candidates = view.Files.SelectMany(f => context.Store.Sheets(f.Id)).Where(s => s.Kind == SheetKind.Tor)
            .Select(s => (Sheet: s, Entries: context.Store.TorEntries(view.Company.Id, s.FileId).Where(e => e.SheetId == s.Id).ToList())).ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        // the sheet the run read (a file linked after the run is not it): the one whose rows are the run's register
        var (sheet, entries) = view.Run is { } run && run.Persons.Count > 0
            ? candidates.FirstOrDefault(c => run.Persons.SelectMany(p => p.Entries).All(e => c.Entries.Any(s => s.Entry.RowNo == e.RowNo && s.Entry.PersonalCode == e.PersonalCode && s.Entry.LastName == e.LastName && s.Entry.FirstName == e.FirstName)), candidates[0])
            : candidates[0];

        // every register person's rows → the person they stand with in the view (a second register row bound to the
        // same store person as an earlier one has no row of its own in the view, but its TÖR rows are still theirs)
        var byRow = new Dictionary<int, ReviewPerson>();
        foreach (var person in view.Persons.Where(p => p.Register is not null))
        {
            foreach (var entry in person.Register!.Entries)
            {
                byRow.TryAdd(entry.RowNo, person);
            }
        }

        foreach (var registerPerson in view.Run?.Persons ?? [])
        {
            var person = view.Persons.FirstOrDefault(p => p.Key == registerPerson.Key)
                ?? view.Persons.FirstOrDefault(p => p.Person?.RegisterKey == registerPerson.Key)
                ?? (registerPerson.Person.PersonalCode is { } code ? view.Persons.FirstOrDefault(p => !p.IsUnmatched && p.Person?.PersonalCode == code) : null);
            if (person is not null)
            {
                foreach (var entry in registerPerson.Entries)
                {
                    byRow.TryAdd(entry.RowNo, person);
                }
            }
        }

        var sources = view.Sources;
        var width = Math.Max(sheet.Headers.Count, entries.Count == 0 ? 0 : entries.Max(e => e.Entry.Cells.Count));
        var header = sheet.Headers.Concat(Enumerable.Repeat("", width - sheet.Headers.Count)).Select(ReportCell.Of).Concat(sources.Select(s => ReportCell.Of(SourceHeader(s)))).ToList();
        var rows = new List<ReportRow> { new(header, header: true) };
        foreach (var stored in entries)
        {
            var cells = new List<ReportCell>(width + sources.Count);
            for (var c = 1; c <= width; c++)
            {
                cells.Add(ReportCell.FromStored(c <= stored.Entry.Cells.Count ? stored.Entry.Cells[c - 1] : ""));
            }

            var person = byRow.GetValueOrDefault(stored.Entry.RowNo);
            foreach (var source in sources)
            {
                var texts = person is null ? [] : person.Payments.Where(p => p.Source == source).Select(p => $"[{(p.RowNo is { } n ? n.ToString(CultureInfo.InvariantCulture) : "-")}][{p.Payee}][{p.Category}]{p.Description}").ToList();
                cells.Add(texts.Count == 0 ? ReportCell.Empty : ReportCell.Of(string.Join("\n", texts)));
            }

            rows.Add(new ReportRow(cells));
        }

        return new ReportSheet(ReportText.TorSheet, rows);
    }

    // ---------------------------------------------------------------- Deklaratsioon

    private static ReportSheet Declaration(IReadOnlyList<DeclaredLine> lines)
    {
        var rows = new List<ReportRow> { ReportRow.Headers(DeclarationHeaders.ToArray<string?>()) };
        foreach (var line in lines)
        {
            var p = line.Payment;
            var r = p.Result;
            rows.Add(new ReportRow(
            [
                ReportCell.Of(p.Section.ToString()),
                ReportCell.Of(p.LineNumber),
                ReportCell.Of(ReportText.ActionText(p.Action)),
                ReportCell.Of(ReportText.PaymentStatusText(p.Status)),
                ReportCell.Of(line.Person is { } person ? $"{person.LastName} {person.FirstName}" : null),
                ReportCell.Of(p.Line.Payee?.Code),
                ReportCell.Of(p.Line.TypeCode),
                ReportCell.Day(p.Line.DocumentDate),
                p.NetPaid is { } net ? ReportCell.Amount(net) : ReportCell.Empty,
                r is null ? ReportCell.Empty : ReportCell.Amount(r.Gross),
                r is null ? ReportCell.Empty : ReportCell.Amount(r.SocialTax),
                r is null || r.SocialTaxMinimumIncrease == 0 ? ReportCell.Empty : ReportCell.Amount(r.SocialTaxMinimumIncrease),
                r is null ? ReportCell.Empty : ReportCell.Amount(r.FundedPension),
                r is null ? ReportCell.Empty : ReportCell.Amount(r.UnemploymentEmployee),
                r is null ? ReportCell.Empty : ReportCell.Amount(r.UnemploymentEmployer),
                r is null ? ReportCell.Empty : ReportCell.Amount(r.IncomeTax),
                r is null || r.ExemptionUsed == 0 ? ReportCell.Empty : ReportCell.Amount(r.ExemptionUsed),
                ReportCell.Of(ReportText.SourceText(p.SourceRef)),
                ReportCell.Of(line.LastFeedback is { } f ? $"{f.Severity}: {string.Join(", ", f.Messages.Select(m => m.Code))}" : null),
                ReportCell.Of(line.Versions.ToString(CultureInfo.InvariantCulture)),
            ]));
        }

        return new ReportSheet(ReportText.DeclarationSheet, rows);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>The source's column header: the run's name ("LHV pank *1757", " #2" kept), the hand lines' in Estonian.</summary>
    public static string SourceHeader(string source) => source == ReviewWorkspace.ByHandSource ? ReportText.ByHandSource : source;

    private static string Reference(ReviewPayment payment) => payment.IsByHand ? ReportText.ByHandSource : payment.Reference;

    /// <summary>"Kontroll-kp": the day the person's facts were last established — the later of the residency and the
    /// pension check (a lookup's, or the accountant's hand); nothing while neither was.</summary>
    public static DateOnly? CheckedOn(Person? person) =>
        person is null ? null
        : person.ResidencyCheckedOn is { } r && person.PensionCheckedOn is { } p ? (r > p ? r : p)
        : person.ResidencyCheckedOn ?? person.PensionCheckedOn;

    private static string? DebitCredit(StatementRow? row) => row is null ? null : row.DcText ?? row.Direction switch
    {
        Direction.Debit => "D",
        Direction.Credit => "C",
        _ => null,
    };

    /// <summary>The amount as the statement had it, signed; its text when it could not be read; a hand line's own.</summary>
    private static ReportCell AmountCell(ReviewPayment payment, StatementRow? row) =>
        row is null ? ReportCell.Amount(payment.Amount)
        : row.Amount is { } amount ? ReportCell.Amount(amount)
        : ReportCell.Of(row.AmountText);

    private sealed class Context(ReviewView view, IStore store, Tax.TaxRates rates)
    {
        private Dictionary<Guid, StatementRow>? _rows;

        public ReviewView View { get; } = view;

        public IStore Store { get; } = store;

        public Tax.TaxRates Rates { get; } = rates;

        /// <summary>A stored statement row of the run's sources, by id.</summary>
        public StatementRow? Row(Guid rowId)
        {
            if (_rows is null)
            {
                _rows = new Dictionary<Guid, StatementRow>();
                foreach (var source in View.Run?.Sources ?? [])
                {
                    if (source.SheetId is { } sheetId)
                    {
                        foreach (var stored in Store.StatementRows(sheetId))
                        {
                            _rows[stored.Id] = stored.Row;
                        }
                    }
                }
            }

            return _rows.GetValueOrDefault(rowId);
        }
    }
}
