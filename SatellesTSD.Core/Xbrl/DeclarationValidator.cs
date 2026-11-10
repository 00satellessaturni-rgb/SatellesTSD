using SatellesTSD.Core.Model;
using SatellesTSD.Core.Tax;

namespace SatellesTSD.Core.Xbrl;

/// <summary>The structural checks a file must pass before it is worth sending: the ones MTA refuses with a BLACK
/// (whole file or payment not saved) or that the XSD rejects. Returns every problem found, so a UI can list them;
/// <see cref="XbrlGlWriter.Write"/> refuses to write while any remain.</summary>
public static class DeclarationValidator
{
    public static IReadOnlyList<string> Validate(Declaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(declaration.RegistryCode))
        {
            problems.Add("The registry code (äriregistrikood) is empty.");
        }
        else
        {
            // the payer's code has the shape its label says (decision 155): MTA matches the header against the declarer
            var payer = declaration.RegistryCode.Trim();
            var misshapen = declaration.RegistryCodeType switch
            {
                // what the window can do about it: the kind is the company card's to say; the code itself is fixed once
                // the company is made, so a wrong one means the company made anew
                PayeeId.Ariregistrikood when payer.Length != 8 || !payer.All(char.IsAsciiDigit) => $"The payer's code '{payer}' is labelled ARIREGISTRIKOOD but is not eight digits; say on the company card (Ettevõte → Muuda) what kind of code it is, or make the company anew if the code itself is wrong.",
                PayeeId.Isikukood when payer.Length != 11 || !payer.All(char.IsAsciiDigit) => $"The payer's code '{payer}' is labelled ISIKUKOOD but is not eleven digits; say on the company card (Ettevõte → Muuda) what kind of code it is, or make the company anew if the code itself is wrong.",
                PayeeId.MrrKood => MrrCodes.Problem(payer) is { } problem ? $"The payer's code: {problem} The company's code cannot be changed: make the company anew with the code as MTA has it." : null,
                _ => null,
            };
            if (misshapen is not null)
            {
                problems.Add(misshapen);
            }
        }

        if (declaration.Sections.Count == 0)
        {
            problems.Add("The file has no data section; the XSD requires at least one entryHeader (send an empty section instead).");
        }

        var seenSections = new HashSet<Section>();
        foreach (var section in declaration.Sections)
        {
            if (!seenSections.Add(section.Section))
            {
                problems.Add($"Section {section.Section} appears twice; a file carries one entryHeader per section.");
            }

            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in section.Lines)
            {
                var where = $"{section.Section} line {line.LineNumber}";
                if (string.IsNullOrWhiteSpace(line.LineNumber))
                {
                    problems.Add($"{section.Section}: a line has an empty lineNumber.");
                }
                else if (!seenKeys.Add(line.LineNumber))
                {
                    problems.Add($"{where}: the lineNumber is repeated in this section (KR_XBRL_VALJAMAKSED_2).");
                }

                if (line.Counter <= 0)
                {
                    problems.Add($"{where}: lineNumberCounter must be a positive number.");
                }

                if (line.Action == LineAction.T)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line.TypeCode))
                {
                    problems.Add($"{where}: an R or M line needs a payment type (VALJAMLIIK2023ap).");
                }
                else if (PaymentTypes.TryGet(line.TypeCode, out var type) && type!.Section != section.Section)
                {
                    problems.Add($"{where}: payment type {line.TypeCode} belongs in {type.Section}, not {section.Section} (KR_VM_05).");
                }

                if (line.Amount is null)
                {
                    problems.Add($"{where}: an R or M line needs an amount (0 when nothing was paid).");
                }

                if (line.Payee is null)
                {
                    problems.Add($"{where}: an R or M line needs the payee (identifierReference).");
                }
                else if (line.Payee.Description == PayeeId.MrrKood && MrrCodes.Problem(line.Payee.Code) is { } mrrProblem)
                {
                    problems.Add($"{where}: {mrrProblem} Correct it on the person card.");
                }

                if (line.DocumentDate is not { } date)
                {
                    problems.Add($"{where}: an R or M line needs the payment date (documentDate).");
                }
                else if (date < declaration.PeriodStart || date > declaration.PeriodEnd)
                {
                    problems.Add($"{where}: the payment date {date:yyyy-MM-dd} is outside the file's month {declaration.PeriodStart:yyyy-MM} (KR_XBRL_VALJAMAKSED_17).");
                }
            }
        }

        return problems;
    }
}
