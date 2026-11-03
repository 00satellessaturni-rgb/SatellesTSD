using SatellesTSD.Core.Import;

namespace SatellesTSD.Core.Store;

/// <summary>A file the accountant imported for a company, kept with its bytes: provenance, and the faithful copy the
/// review workbook makes of every statement.</summary>
/// <param name="Encoding">The text encoding a CSV was decoded with (it decided every stored text); null for Excel files.</param>
public sealed record ImportFile(Guid Id, Guid CompanyId, ImportKind Kind, string FileName, SourceFormat Format, string Sha256, long Size, DateTime ImportedAt, string? Notes, string? Encoding = null);

/// <summary>One recognised sheet of an imported file, with the layout as it was when the sheet was read: a later
/// edit of the layout never re-interprets stored rows.</summary>
/// <param name="DisplayName">The workbook's source name: "LHV pank *1234", "Kassa", "TÖR".</param>
/// <param name="Problems">What the parser could not make sense of: the sheet-level findings (no payment rows, several employers, extra TÖR columns) and every row's, with their codes.</param>
public sealed record ImportSheet(
    Guid Id,
    Guid FileId,
    int SheetIndex,
    string? SheetName,
    SheetKind Kind,
    string? LayoutId,
    string? LayoutJson,
    int HeaderRowNo,
    IReadOnlyList<string> Headers,
    string? Account,
    string DisplayName,
    int RowCount,
    DateOnly? FirstDate,
    DateOnly? LastDate,
    IReadOnlyList<ImportProblem> Problems)
{
    public StatementLayout? Layout => LayoutJson is null ? null : LayoutCatalog.FromJson(LayoutJson);

    public bool Equals(ImportSheet? other) =>
        other is not null && Id == other.Id && FileId == other.FileId && SheetIndex == other.SheetIndex && SheetName == other.SheetName && Kind == other.Kind
        && LayoutId == other.LayoutId && LayoutJson == other.LayoutJson && HeaderRowNo == other.HeaderRowNo && Headers.SequenceEqual(other.Headers, StringComparer.Ordinal)
        && Account == other.Account && DisplayName == other.DisplayName && RowCount == other.RowCount && FirstDate == other.FirstDate && LastDate == other.LastDate
        && Problems.SequenceEqual(other.Problems);

    public override int GetHashCode() => HashCode.Combine(Id, FileId, SheetIndex, Kind, LayoutId, DisplayName, RowCount, Problems.Count);
}

/// <summary>A statement row in the store; the id is what a declared line points at.</summary>
public sealed record StoredStatementRow(Guid Id, Guid SheetId, StatementRow Row);

/// <summary>A TÖR row in the store.</summary>
public sealed record StoredTorEntry(Guid Id, Guid SheetId, Guid CompanyId, TorEntry Entry);

/// <summary>The imported files of a company and what they held, and the statement layouts the owner changed. Files
/// belong to the company, not to a period: a statement spans whatever months it spans, and a TÖR export is a dated
/// snapshot; a period records which files its run used.</summary>
public interface IImportStore : IDisposable
{
    /// <summary>Runs several store calls as one unit: all or nothing (an import and its link to the period, a month
    /// re-allocated line by line, an export that records its submission and the period's state together).</summary>
    void InTransaction(Action work);

    /// <summary>The earlier import of these very bytes, if any (the hash in either case).</summary>
    ImportFile? FindImport(Guid companyId, string sha256);

    /// <summary>Stores a parsed file with every recognised sheet and row, and the bytes it was parsed from — the
    /// hash is taken from those bytes, and a parse labelled with another hash or size is refused. Refused too when
    /// nothing was recognised or the same bytes were imported before (an <see cref="ImportException"/> naming the
    /// earlier import). The reader's warnings land among every stored sheet's problems.</summary>
    ImportFile AddImport(Guid companyId, ParsedImport import, byte[] content, string? notes = null);

    IReadOnlyList<ImportFile> Imports(Guid companyId);

    byte[]? ImportContent(Guid fileId);

    IReadOnlyList<ImportSheet> Sheets(Guid fileId);

    ImportSheet? Sheet(Guid sheetId);

    /// <summary>Every row of a statement sheet, skipped ones included, in sheet order.</summary>
    IReadOnlyList<StoredStatementRow> StatementRows(Guid sheetId);

    /// <summary>The payment rows of all the company's statements whose date falls within the range, oldest first.
    /// A row whose date could not be read (Date null, a problem on the row) is never here: a period's run reads its
    /// linked files sheet by sheet (<see cref="PeriodImports"/> → <see cref="Sheets"/> → <see cref="StatementRows(Guid)"/>)
    /// and reports such rows, as the workbook reported "viga: kehtetu kuupaev".</summary>
    IReadOnlyList<StoredStatementRow> StatementRows(Guid companyId, DateOnly from, DateOnly to);

    StoredStatementRow? StatementRow(Guid rowId);

    /// <summary>The TÖR rows of the company, from one file or from all, in file then sheet order.</summary>
    IReadOnlyList<StoredTorEntry> TorEntries(Guid companyId, Guid? fileId = null);

    /// <summary>Records that a period's run used a file.</summary>
    void LinkImport(Guid periodId, Guid fileId);

    /// <summary>Forgets that a Draft period's run used a file; a period past Draft keeps its record.</summary>
    void UnlinkImport(Guid periodId, Guid fileId);

    IReadOnlyList<ImportFile> PeriodImports(Guid periodId);

    /// <summary>Removes a file with its sheets and rows. Refused while a declared line points at one of its rows, or
    /// while a period past Draft lists it among the files of its run.</summary>
    void RemoveImport(Guid fileId);

    /// <summary>The owner's layout overrides, to merge over <see cref="LayoutCatalog.Default"/>.</summary>
    IReadOnlyList<StatementLayout> Layouts();

    /// <summary>Saves an override; refused when the layout, or the catalog it would be merged into, cannot be used.</summary>
    void SaveLayout(StatementLayout layout);

    void DeleteLayout(string layoutId);
}
