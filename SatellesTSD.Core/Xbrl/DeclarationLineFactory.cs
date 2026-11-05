using SatellesTSD.Core.Model;
using SatellesTSD.Core.Tax;

namespace SatellesTSD.Core.Xbrl;

/// <summary>Turns a computed payment into the declared line: the classifier entries the payee's facts require,
/// the TSD01 calculation date, the base and exemption measurables, and one tax line per tax that applies
/// (MTA's example emits a tax line when the tax applies, even at 0, and omits taxes that do not apply; on a
/// zero-amount minimum-only line only TSD_SM and TSD_SMVM appear). The counter is the line's position in the
/// section of the file being sent — assign it when the file is assembled (<see cref="SectionEntries.WithCounters"/>),
/// not from storage.</summary>
public static class DeclarationLineFactory
{
    public static PaymentLine Create(
        string lineNumber,
        int counter,
        LineAction action,
        PaymentType type,
        PayeeTaxFacts facts,
        PayeeId payee,
        DateOnly documentDate,
        TaxResult result,
        DateOnly? calculationDate = null,
        string? allocationCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lineNumber);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(payee);
        ArgumentNullException.ThrowIfNull(result);
        if (action == LineAction.T)
        {
            throw new ArgumentException("A cancellation carries no payment data; use PaymentLine.Cancellation.", nameof(action));
        }

        if (result.EffectiveA1 != A1Case.None && (string.IsNullOrWhiteSpace(facts.A1Country) || string.IsNullOrWhiteSpace(facts.WorkCountry)))
        {
            throw new InvalidOperationException("An A1 case needs both the A1 country and the work country.");
        }

        var calcDate = calculationDate ?? documentDate;
        if (calcDate > documentDate)
        {
            throw new InvalidOperationException("The calculation date (TSD01) may not be later than the payment date (KR_YLD_25).");
        }

        var extras = new List<AccountSubEntry>();
        if (result.EffectiveA1 != A1Case.None)
        {
            extras.Add(AccountSubEntry.A1Country(facts.A1Country!));
            extras.Add(AccountSubEntry.WorkCountry(facts.WorkCountry!));
        }

        if (facts.Residency == Residency.NonResident && !string.IsNullOrWhiteSpace(facts.ResidenceCountry))
        {
            // the country when known; without it the line goes out without RR_MAKSURESIDENTSUSE_RIIK (optional in the schema)
            extras.Add(AccountSubEntry.ResidenceCountry(facts.ResidenceCountry.Trim()));
        }

        var amounts = new List<MeasurableAmount>();
        if (result.ExemptionUsed > 0m)
        {
            var description = result.ExemptionKind switch
            {
                ExemptionKind.Mvt610 => MeasurableAmount.Mvt610,
                ExemptionKind.Mvt650 => MeasurableAmount.Mvt650,
                _ => throw new InvalidOperationException("An exemption was used but its kind is None."),
            };
            amounts.Add(new MeasurableAmount(description, result.ExemptionUsed));
        }

        if (result.SocialTaxApplies)
        {
            // Also at 0: KR_SM_01_1 (rules v2.0, 24.07.2026) wants TSD_SMVM on every type subject to social tax, and
            // SMVM = amount (KR_SM_01_2) holds at 0; MTA's older example Tehing_21117 omits it, the rule wins.
            amounts.Add(new MeasurableAmount(MeasurableAmount.SocialTaxBase, result.SocialTaxBase));
        }

        if (result.SocialTaxMinimumIncrease > 0m)
        {
            amounts.Add(new MeasurableAmount(MeasurableAmount.SocialTaxMinimumIncrease, result.SocialTaxMinimumIncrease));
        }

        var taxes = new List<TaxLine>();
        if (result.SocialTaxApplies && (result.Gross > 0m || result.SocialTaxMinimumIncrease > 0m))
        {
            taxes.Add(new TaxLine(TaxLine.SocialTax, result.SocialTax));
        }

        if (result.Gross > 0m)
        {
            if (result.FundedPensionApplies)
            {
                taxes.Add(new TaxLine(TaxLine.FundedPension, result.FundedPension));
            }

            if (result.UnemploymentEmployeeApplies)
            {
                taxes.Add(new TaxLine(TaxLine.UnemploymentEmployee, result.UnemploymentEmployee));
            }

            if (result.UnemploymentEmployerApplies)
            {
                taxes.Add(new TaxLine(TaxLine.UnemploymentEmployer, result.UnemploymentEmployer));
            }

            if (result.IncomeTaxApplies)
            {
                decimal? rate = facts.Residency == Residency.NonResident ? result.IncomeTaxRate : null;
                taxes.Add(new TaxLine(TaxLine.IncomeTax, result.IncomeTax, rate));
            }
        }

        return new PaymentLine(
            lineNumber,
            counter,
            action,
            type.Code,
            extras,
            result.Gross,
            allocationCode,
            payee,
            documentDate,
            [new MeasurableEvent(MeasurableEvent.CalculationDate, calcDate)],
            amounts,
            taxes);
    }
}
