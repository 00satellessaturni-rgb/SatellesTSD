using SatellesTSD.Core.Model;
using SatellesTSD.Core.Tax;

namespace SatellesTSD.Core.Xbrl;

/// <summary>One problem the file's own check found: its code, the Core's English (<see cref="Message"/>, as the tests and
/// support read it), and the figures the window words it by in Estonian.</summary>
public sealed record DeclarationProblem(string Code, string Message)
{
    /// <summary>The section the problem is in (the line's, the one repeated).</summary>
    public Section? Section { get; init; }

    /// <summary>The line's key (lineNumber).</summary>
    public string? Key { get; init; }

    /// <summary>The code it names: the payer's or the payee's, the payment type's.</summary>
    public string? Subject { get; init; }

    /// <summary>The section a payment type belongs in (XBRL_TYPE_WRONG_SECTION).</summary>
    public Section? OtherSection { get; init; }

    /// <summary>The payment date outside the file's month.</summary>
    public DateOnly? Day { get; init; }

    /// <summary>The file's month (its first day).</summary>
    public DateOnly? Month { get; init; }
}

/// <summary>The structural checks a file must pass before it is worth sending: the ones MTA refuses with a BLACK
/// (whole file or payment not saved) or that the XSD rejects. Returns every problem found, so a UI can list them;
/// <see cref="XbrlGlWriter.Write"/> refuses to write while any remain.</summary>
public static class DeclarationValidator
{
    public const string PayerEmpty = "XBRL_PAYER_EMPTY";
    public const string PayerNotRegistry = "XBRL_PAYER_NOT_REGISTRY";
    public const string PayerNotPersonal = "XBRL_PAYER_NOT_PERSONAL";
    public const string PayerMrrInvalid = "XBRL_PAYER_MRR_INVALID";
    public const string NoSection = "XBRL_NO_SECTION";
    public const string SectionTwice = "XBRL_SECTION_TWICE";
    public const string KeyEmpty = "XBRL_KEY_EMPTY";
    public const string KeyRepeated = "XBRL_KEY_REPEATED";
    public const string CounterInvalid = "XBRL_COUNTER_INVALID";
    public const string TypeMissing = "XBRL_TYPE_MISSING";
    public const string TypeWrongSection = "XBRL_TYPE_WRONG_SECTION";
    public const string AmountMissing = "XBRL_AMOUNT_MISSING";
    public const string PayeeMissing = "XBRL_PAYEE_MISSING";
    public const string PayeeMrrInvalid = "XBRL_PAYEE_MRR_INVALID";
    public const string DateMissing = "XBRL_DATE_MISSING";
    public const string DateOutsideMonth = "XBRL_DATE_OUTSIDE_MONTH";

    /// <summary>Every code <see cref="Problems"/> gives.</summary>
    public static readonly IReadOnlyList<string> Codes =
    [
        PayerEmpty, PayerNotRegistry, PayerNotPersonal, PayerMrrInvalid, NoSection, SectionTwice, KeyEmpty, KeyRepeated, CounterInvalid,
        TypeMissing, TypeWrongSection, AmountMissing, PayeeMissing, PayeeMrrInvalid, DateMissing, DateOutsideMonth,
    ];

    /// <summary>Every problem's English, as before the codes.</summary>
    public static IReadOnlyList<string> Validate(Declaration declaration) => Problems(declaration).Select(p => p.Message).ToList();

    /// <summary>Every problem with its code and figures, in the order <see cref="Validate"/> gives their English.</summary>
    public static IReadOnlyList<DeclarationProblem> Problems(Declaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        var problems = new List<DeclarationProblem>();

        if (string.IsNullOrWhiteSpace(declaration.RegistryCode))
        {
            problems.Add(new DeclarationProblem(PayerEmpty, "The registry code (äriregistrikood) is empty."));
        }
        else
        {
            // the payer's code has the shape its label says (decision 155): MTA matches the header against the declarer
            var payer = declaration.RegistryCode.Trim();
            var misshapen = declaration.RegistryCodeType switch
            {
                // what the window can do about it: the kind is the company card's to say; the code itself is fixed once
                // the company is made, so a wrong one means the company made anew
                PayeeId.Ariregistrikood when payer.Length != 8 || !payer.All(char.IsAsciiDigit) => new DeclarationProblem(PayerNotRegistry, $"The payer's code '{payer}' is labelled ARIREGISTRIKOOD but is not eight digits; say on the company card (Ettevõte → Muuda) what kind of code it is, or make the company anew if the code itself is wrong."),
                PayeeId.Isikukood when payer.Length != 11 || !payer.All(char.IsAsciiDigit) => new DeclarationProblem(PayerNotPersonal, $"The payer's code '{payer}' is labelled ISIKUKOOD but is not eleven digits; say on the company card (Ettevõte → Muuda) what kind of code it is, or make the company anew if the code itself is wrong."),
                PayeeId.MrrKood => MrrCodes.Problem(payer) is { } problem ? new DeclarationProblem(PayerMrrInvalid, $"The payer's code: {problem} The company's code cannot be changed: make the company anew with the code as MTA has it.") : null,
                _ => null,
            };
            if (misshapen is not null)
            {
                problems.Add(misshapen with { Subject = payer });
            }
        }

        if (declaration.Sections.Count == 0)
        {
            problems.Add(new DeclarationProblem(NoSection, "The file has no data section; the XSD requires at least one entryHeader (send an empty section instead)."));
        }

        var seenSections = new HashSet<Section>();
        foreach (var section in declaration.Sections)
        {
            if (!seenSections.Add(section.Section))
            {
                problems.Add(new DeclarationProblem(SectionTwice, $"Section {section.Section} appears twice; a file carries one entryHeader per section.") { Section = section.Section });
            }

            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in section.Lines)
            {
                var where = $"{section.Section} line {line.LineNumber}";
                DeclarationProblem At(string code, string message) => new(code, message) { Section = section.Section, Key = line.LineNumber };
                if (string.IsNullOrWhiteSpace(line.LineNumber))
                {
                    problems.Add(new DeclarationProblem(KeyEmpty, $"{section.Section}: a line has an empty lineNumber.") { Section = section.Section });
                }
                else if (!seenKeys.Add(line.LineNumber))
                {
                    problems.Add(At(KeyRepeated, $"{where}: the lineNumber is repeated in this section (KR_XBRL_VALJAMAKSED_2)."));
                }

                if (line.Counter <= 0)
                {
                    problems.Add(At(CounterInvalid, $"{where}: lineNumberCounter must be a positive number."));
                }

                if (line.Action == LineAction.T)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line.TypeCode))
                {
                    problems.Add(At(TypeMissing, $"{where}: an R or M line needs a payment type (VALJAMLIIK2023ap)."));
                }
                else if (PaymentTypes.TryGet(line.TypeCode, out var type) && type!.Section != section.Section)
                {
                    problems.Add(At(TypeWrongSection, $"{where}: payment type {line.TypeCode} belongs in {type.Section}, not {section.Section} (KR_VM_05).") with { Subject = line.TypeCode, OtherSection = type.Section });
                }

                if (line.Amount is null)
                {
                    problems.Add(At(AmountMissing, $"{where}: an R or M line needs an amount (0 when nothing was paid)."));
                }

                if (line.Payee is null)
                {
                    problems.Add(At(PayeeMissing, $"{where}: an R or M line needs the payee (identifierReference)."));
                }
                else if (line.Payee.Description == PayeeId.MrrKood && MrrCodes.Problem(line.Payee.Code) is { } mrrProblem)
                {
                    problems.Add(At(PayeeMrrInvalid, $"{where}: {mrrProblem} Correct it on the person card.") with { Subject = line.Payee.Code?.Trim() });
                }

                if (line.DocumentDate is not { } date)
                {
                    problems.Add(At(DateMissing, $"{where}: an R or M line needs the payment date (documentDate)."));
                }
                else if (date < declaration.PeriodStart || date > declaration.PeriodEnd)
                {
                    problems.Add(At(DateOutsideMonth, $"{where}: the payment date {date:yyyy-MM-dd} is outside the file's month {declaration.PeriodStart:yyyy-MM} (KR_XBRL_VALJAMAKSED_17).") with { Day = date, Month = declaration.PeriodStart });
                }
            }
        }

        return problems;
    }
}
