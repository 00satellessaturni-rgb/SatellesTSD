using System.Text.Json;
using System.Text.Json.Serialization;
using SatellesTSD.Core.Declare;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Store;

namespace SatellesTSD.Core.Review;

/// <summary>How the example person stands in the month's register (decision 204): listed with the contracts given; a
/// register of the month that does not list the person (another employee in it); or no register in the month at all.</summary>
public enum SimRegister
{
    Listed,
    NotListed,
    NoRegister,
}

/// <summary>One contract of the example person in the register: its TÖR type, its dates and a pause.</summary>
public sealed record SimEmployment(string TypeId, DateOnly Start, DateOnly? End = null, DateOnly? PauseStart = null, DateOnly? PauseEnd = null);

/// <summary>One bank row of the example: paid out to the payee (or money in), with the review's own word over the
/// search's when given — a category, a type code, declared or not.</summary>
public sealed record SimRow(
    DateOnly Date,
    string Payee,
    decimal Amount,
    string Description,
    bool MoneyIn = false,
    string Currency = "EUR",
    string? Category = null,
    string? TypeCode = null,
    bool? Declare = null);

/// <summary>The example person's card: the facts the accountant keeps on it. The work-ability status and its application
/// come last, so a file saved before them opens with neither.</summary>
public sealed record SimPerson(
    string FirstName,
    string LastName,
    string? PersonalCode,
    Residency Residency = Residency.Resident,
    string? ResidenceCountry = null,
    string? MrrCode = null,
    bool? FundedPensionMember = true,
    decimal? PensionRate = 0.02m,
    bool OnOldAgeOrFlexiblePension = false,
    int? PensionAgeYear = null,
    bool BasicExemptionApplied = true,
    decimal? BasicExemptionMonthly = null,
    bool SocialTaxMinimumApplies = true,
    A1Case A1 = A1Case.None,
    string? A1Country = null,
    string? WorkCountry = null,
    decimal? TreatyIncomeTaxRate = null,
    string? TorTypeId = null,
    bool TypeFromCard = false,
    DeclarationChoice Declaration = DeclarationChoice.Auto,
    bool ReducedWorkAbility = false,
    bool StatePaysSocialTax = false);

/// <summary>A simulation (decision 204): one example person, the register as it stands for them, the month's bank rows,
/// the declaration's cut. Saved and opened as a JSON file; nothing of it touches the store in use.</summary>
public sealed record SimScenario(
    int Year,
    int Month,
    SimPerson Person,
    SimRegister Register,
    IReadOnlyList<SimEmployment> Employments,
    IReadOnlyList<SimRow> Rows,
    DeclarationMode Mode = DeclarationMode.PerPayment,
    TwoContractsRule TwoContracts = TwoContractsRule.SalaryToEmploymentContract)
{
    public const string FileKind = "satellestsd-simulation";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ToJson() => JsonSerializer.Serialize(new File(FileKind, this), Json);

    /// <summary>A scenario of a file the simulation saved; refused (an <see cref="ArgumentException"/>) when it is not one.</summary>
    public static SimScenario Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        File? file;
        try
        {
            file = JsonSerializer.Deserialize<File>(json.TrimStart('﻿'), Json);
        }
        catch (JsonException e)
        {
            throw new ArgumentException("The file is not a simulation's JSON: " + e.Message, nameof(json), e);
        }

        if (file is not { Kind: FileKind, Scenario: { Person: { FirstName: not null, LastName: not null } } scenario })
        {
            throw new ArgumentException($"The file is not a simulation (\"kind\": \"{FileKind}\").", nameof(json));
        }

        // a hand-edited file: an empty element, or one without its date, type or payee, is no scenario's
        if ((scenario.Employments ?? []).Any(e => e is null || e.TypeId is null) || (scenario.Rows ?? []).Any(r => r is null || r.Payee is null || r.Description is null || r.Currency is null))
        {
            throw new ArgumentException("The simulation's file holds an empty or incomplete contract or bank row.", nameof(json));
        }

        return scenario with { Employments = scenario.Employments ?? [], Rows = scenario.Rows ?? [] };
    }

    private sealed record File(string? Kind, SimScenario? Scenario);
}

/// <summary>What a simulation gave: the run's steps in words, the review of the example person, the build of the person,
/// the lines it drafted and their totals, the file's XML or why there is none. <see cref="Refusal"/> is set when the run
/// stopped before the build (the import or the search refused).</summary>
public sealed class SimResult
{
    /// <summary>What the import said of the example's sheets, by code (the window words them as the import log does); the
    /// steps after them are the search's.</summary>
    public IReadOnlyList<Import.ImportNote> ImportNotes { get; init; } = [];

    /// <summary>What the search said ("CODE: sentence", the Core's English); <see cref="RunProblems"/> holds the same problems
    /// by their codes and figures, which the window words.</summary>
    public IReadOnlyList<string> Steps => RunProblems.Select(p => $"{p.Code}: {p.Message}").ToList();

    /// <summary>What the search said of the example, by code (the window words them as the "Otsi maksed" log does).</summary>
    public IReadOnlyList<Matching.MatchProblem> RunProblems { get; init; } = [];

    /// <summary>The run stopped at the import: the example could not be read (<see cref="Refusal"/> and
    /// <see cref="RefusalError"/> say why, when the import said).</summary>
    public bool ImportStopped { get; init; }

    public string? Refusal { get; init; }

    /// <summary>The exception behind <see cref="Refusal"/> when one was thrown or the import refused by code (the window words
    /// it as the app does); null otherwise.</summary>
    public Exception? RefusalError { get; init; }

    /// <summary>Whether the search ran with the owner's saved phrases rather than the built-in ones (decision 202).</summary>
    public bool OwnPhrases { get; init; }

    public ReviewPerson? Review { get; init; }

    public PersonBuild? Build { get; init; }

    /// <summary>What the build said of the period beyond the person (<see cref="BuildReport.Notes"/>).</summary>
    public IReadOnlyList<BuildNote> BuildNotes { get; init; } = [];

    /// <summary>The period notes' English, "CODE: sentence".</summary>
    public IReadOnlyList<string> BuildProblems => BuildNotes.Select(n => n.Text).ToList();

    public IReadOnlyList<StoredPayment> Lines { get; init; } = [];

    public DeclarationTotals Totals { get; init; } = DeclarationTotals.None;

    public string? Xml { get; init; }

    public string? XmlRefusal { get; init; }

    /// <summary>The exception behind <see cref="XmlRefusal"/>; null when there was none, or no line to write.</summary>
    public Exception? XmlRefusalError { get; init; }
}
