using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Store;
using SatellesTSD.Core.Tax;

namespace SatellesTSD.Core.Review;

/// <summary>One payment as the review grid shows it: a row of the run with the run's defaults and the accountant's
/// overlay on top, or a line added by hand. The "Default…" members say what the review assumes; the plain members
/// what stands after the accountant's word. Plain classes, not records: grid rows are never compared.</summary>
public sealed class ReviewPayment
{
    public required Guid PeriodId { get; init; }

    /// <summary>The stored statement row; null for a line added by hand.</summary>
    public Guid? RowId { get; init; }

    /// <summary>The stored decision (overlay or hand line) when there is one.</summary>
    public Guid? LineId { get; init; }

    public bool IsByHand { get; init; }

    public required string Source { get; init; }

    public int? RowNo { get; init; }

    public DateOnly? Date { get; init; }

    public string? DateText { get; init; }

    public required string Payee { get; init; }

    public string? Description { get; init; }

    /// <summary>The amount as the sums take it: its absolute value, 0 when unreadable.</summary>
    public decimal Amount { get; init; }

    /// <summary>The amount was read and the row is not shared between two persons: it counts.</summary>
    public bool AmountOk { get; init; }

    /// <summary>The register person the run found the row for (their key); null for a row nobody matched.</summary>
    public string? RunPersonKey { get; init; }

    public Guid? DefaultPersonId { get; init; }

    public Guid? PersonId { get; init; }

    /// <summary>The run's "Kontroll/Kategooria" text, verbatim.</summary>
    public required string VerdictText { get; init; }

    /// <summary>The run's "Otsing*" masks.</summary>
    public string Masks { get; init; } = "";

    public string? DefaultCategory { get; init; }

    public string? Category { get; init; }

    public string? DefaultTypeCode { get; init; }

    public string? TypeCode { get; init; }

    public bool DefaultDeclare { get; init; }

    public bool Declare { get; init; }

    public bool InPeriod { get; init; }

    /// <summary>The run's own Counted flag (a Matched verdict, a readable amount, not shared).</summary>
    public bool Counted { get; init; }

    public IReadOnlyList<string> Flags { get; init; } = [];

    /// <summary>The run found the row for more than one person (SHARED_ROW): the row is shown once, nobody's until the
    /// accountant names one of these.</summary>
    public bool IsShared { get; init; }

    /// <summary>The persons the run found the row for, when it is shared.</summary>
    public IReadOnlyList<string> Candidates { get; init; } = [];

    /// <summary>What would be refused if the payment were declared as it stands; empty when nothing, or when it is not declared.</summary>
    public IReadOnlyList<Refusal> Checks { get; init; } = [];

    public string? Note { get; init; }

    public bool HasOverride { get; init; }

    /// <summary>The workbook's "Dokument" cell: "LHV pank *1757 [12]".</summary>
    public string Reference => IsByHand ? ReviewWorkspace.ByHandSource : $"{Source} [{RowNo}]";
}

/// <summary>One row of the persons grid: a register person with their store person, the Vahearuanne's numbers and
/// their payments; or a store person outside the register who has lines; or the pseudo-row of the rows nobody matched.</summary>
public sealed class ReviewPerson
{
    /// <summary>The register key (personal code, or the names) when the person is in the run's register.</summary>
    public string? Key { get; init; }

    public Guid? PersonId { get; init; }

    public Person? Person { get; init; }

    public MatchPerson? Register { get; init; }

    public bool IsUnmatched { get; init; }

    /// <summary>The net salary per source — the Vahearuanne's cells, from what stands after the accountant's word.</summary>
    public required IReadOnlyDictionary<string, decimal> SalaryBySource { get; init; }

    /// <summary>"PALK NETO KOKKU".</summary>
    public decimal SalaryTotal { get; init; }

    public required IReadOnlyList<ReviewPayment> Payments { get; init; }

    public required IReadOnlyList<MatchProblem> Problems { get; init; }

    public string LastName => Person?.LastName ?? Register?.Person.LastName ?? "";

    public string FirstName => Person?.FirstName ?? Register?.Person.FirstName ?? "";

    public string? PersonalCode => Person?.PersonalCode ?? Register?.Person.PersonalCode;
}

/// <summary>A period as the window shows it: the run (or none yet), the catalog the run was made with, the persons in
/// register order, the hand-added persons with lines after them, the rows nobody matched last.</summary>
public sealed class ReviewView
{
    public required Period Period { get; init; }

    public required Company Company { get; init; }

    public bool Editable => Period.State == PeriodState.Draft;

    public MatchResult? Run { get; init; }

    public StoredMatchRun? StoredRun { get; init; }

    /// <summary>The catalog the run was made with (the category picker's list), else the one in force.</summary>
    public required MatchCatalog Catalog { get; init; }

    public required IReadOnlyList<ImportFile> Files { get; init; }

    /// <summary>The source names in the run's order, "by hand" last when hand lines exist — the Vahearuanne's columns.</summary>
    public required IReadOnlyList<string> Sources { get; init; }

    public required IReadOnlyList<ReviewPerson> Persons { get; init; }

    /// <summary>Every person of the company, for the pickers.</summary>
    public required IReadOnlyList<Person> StorePersons { get; init; }

    public required IReadOnlyList<MatchProblem> Problems { get; init; }

    /// <summary>The run's rows that are not payment rows (balances, fees, blank lines) and so are not shown.</summary>
    public int RowsLeftOut { get; init; }

    public ReviewPerson Unmatched => Persons.Last(p => p.IsUnmatched);
}
