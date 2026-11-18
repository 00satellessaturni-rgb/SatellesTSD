namespace SatellesTSD.Core.Import;

/// <summary>One thing the import log says of a file's reading (decision 135), by code with its figures, so the window words it
/// in Estonian; <see cref="English"/> is the Core's line as it always was (the tests and support read it). The codes are
/// <see cref="SheetNotes"/>' and the sheet problems' own (<see cref="ImportProblem"/>, <see cref="TorParser"/>).</summary>
/// <param name="Code">What the note says, verbatim in the window (decision 46).</param>
/// <param name="English">The whole line in the Core's English, the sheet named in it.</param>
/// <param name="Sheet">The sheet's name; null for a note of the file, or a sheet without a name.</param>
/// <param name="SheetNo">The sheet's number from 1; 0 for a note of the file (the reader's own).</param>
/// <param name="Args">The figures by code: see <see cref="SheetNotes"/> and <see cref="ImportProblem.Args"/>.</param>
/// <param name="Count">SHEET_ROW_PROBLEMS: the rows with a problem.</param>
/// <param name="RowNo">ABOVE_HEADER: the row above the header.</param>
public sealed record ImportNote(string Code, string English, string? Sheet, int SheetNo, IReadOnlyList<string> Args, int? Count = null, int? RowNo = null)
{
    /// <summary>SHEET_NEAREST_LAYOUT: how the sheet's header row differs from the nearest layout's.</summary>
    public IReadOnlyList<HeaderDifference> Differences { get; init; } = [];

    public bool Equals(ImportNote? other) =>
        other is not null && string.Equals(Code, other.Code, StringComparison.Ordinal) && string.Equals(English, other.English, StringComparison.Ordinal)
        && string.Equals(Sheet, other.Sheet, StringComparison.Ordinal) && SheetNo == other.SheetNo && Seq.Equal(Args, other.Args) && Count == other.Count && RowNo == other.RowNo
        && Differences.SequenceEqual(other.Differences);

    public override int GetHashCode() => HashCode.Combine(Code, English, Sheet, SheetNo, Seq.Hash(Args), Count, RowNo, Differences.Count);
}

/// <summary>The import log's own codes (beside the sheet problems' codes, which a note reuses as they are).</summary>
public static class SheetNotes
{
    /// <summary>The sheet has no cells.</summary>
    public const string Empty = "SHEET_EMPTY";

    /// <summary>No statement layout has the sheet's header row, nor anything near it.</summary>
    public const string NoLayout = "SHEET_NO_LAYOUT";

    /// <summary>The sheet looks like the TÖR export but lacks columns; the args are the missing headers.</summary>
    public const string TorLacks = "SHEET_TOR_LACKS";

    /// <summary>The sheet's header row is near a layout's; the arg is the layout's id, the note's differences say how.</summary>
    public const string NearestLayout = "SHEET_NEAREST_LAYOUT";

    /// <summary>The sheet is hidden in the workbook, and was read all the same.</summary>
    public const string Hidden = "SHEET_HIDDEN";

    /// <summary>Rows with a problem, counted (the note's count); the args are "CODE n" per kind.</summary>
    public const string RowProblems = "SHEET_ROW_PROBLEMS";

    /// <summary>A CSV quote never closed; the arg is the record it opened at.</summary>
    public const string UnclosedQuote = "READ_UNCLOSED_QUOTE";
}
