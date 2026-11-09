using System.Xml.Linq;
using SatellesTSD.Core.Confirmation;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Tax;
using SatellesTSD.Core.Xbrl;

namespace SatellesTSD.Tests;

/// <summary>The confirmation message against MTA's sample and its XSD, and the totals from a declaration.</summary>
public class ConfirmationTests
{
    private const string TsdSample = "APA_TSD_20260326_kinnitamine_naidis.xml";

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "feedback", name);

    private static string ConfirmationXsd => Path.Combine(AppContext.BaseDirectory, "schemas", "apa-tsd", "confirmation", "confirmations-2026-09-23.xsd");

    private static ConfirmDeclaration SampleConfirmation() => new(
        PersonCode: "48304052720",
        DeclarationText: ConfirmDeclaration.DefaultText,
        TaxablePersonIdentifier: "14436333",
        TaxablePersonIdentifierType: PayeeId.Ariregistrikood,
        Kind: DeclarationKind.TSD,
        Month: 10,
        Year: 2026,
        Sections:
        [
            new("PAYMENTS", true), new("L3", false), new("L4", true), new("L5", true),
            new("L6", true), new("L7", true), new("L8", false), new("INF1", true),
        ],
        TaxSums:
        [
            new("110", 1499.54m), new("114", 1156.41m), new("115", 2085.80m), new("116", 104.00m),
            new("117", 120.00m), new("118", 4965.75m), new("119", 0m),
        ]);

    [Fact]
    public void The_writer_reproduces_MTAs_TSD_sample()
    {
        var expected = XDocument.Load(Fixture(TsdSample));

        var actual = ConfirmationWriter.Write(SampleConfirmation());

        XmlCanon.AssertEqual(expected.Root!, actual.Root!);
    }

    [Fact]
    public void The_sample_and_our_message_validate_against_MTAs_schema()
    {
        Assert.Empty(SchemaValidation.Validate(ConfirmationXsd, File.ReadAllText(Fixture(TsdSample))));
        Assert.Empty(SchemaValidation.Validate(ConfirmationXsd, ConfirmationWriter.Serialize(SampleConfirmation())));
        Assert.Empty(SchemaValidation.Validate(ConfirmationXsd, ConfirmationWriter.Serialize(SampleConfirmation() with { PersonCode = null, Month = 3 })));
    }

    [Fact]
    public void A_document_the_schema_does_not_know_is_not_waved_through()
    {
        Assert.NotEmpty(SchemaValidation.Validate(ConfirmationXsd, "<processing result=\"OK\"/>"));
        Assert.NotEmpty(SchemaValidation.Validate(ConfirmationXsd, "<confirmDeclaration xmlns=\"urn:x\"><declaration>t</declaration></confirmDeclaration>"));
        var validator = new XbrlGlValidator(Path.Combine(AppContext.BaseDirectory, "schemas", "apa-tsd"));
        Assert.NotEmpty(validator.Validate("<somethingElse/>"));
    }

    [Fact]
    public void The_serialized_message_starts_with_the_declaration_and_writes_two_decimals()
    {
        var text = ConfirmationWriter.Serialize(SampleConfirmation() with { Month = 3 });

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", text);
        Assert.Contains("<month>3</month>", text);
        Assert.Contains("<taxAmount>2085.80</taxAmount>", text);
        Assert.Contains("<taxAmount>0.00</taxAmount>", text);
        Assert.Contains("<hasData>false</hasData>", text);
        Assert.DoesNotContain("xmlns", text);
    }

    [Fact]
    public void Invalid_inputs_are_named_and_refused()
    {
        var bad = SampleConfirmation() with
        {
            PersonCode = "123",
            Month = 13,
            TaxablePersonIdentifierType = "REGCODE",
            TaxSums = [new("110", -1m), new("999", 1.005m)],
            Sections = [new("L9", true)],
        };

        var problems = ConfirmationWriter.Validate(bad);

        Assert.Contains(problems, p => p.Contains("personCode"));
        Assert.Contains(problems, p => p.Contains("month 13"));
        Assert.Contains(problems, p => p.Contains("REGCODE"));
        Assert.Contains(problems, p => p.Contains("negative"));
        Assert.Contains(problems, p => p.Contains("999"));
        Assert.Contains(problems, p => p.Contains("two decimals"));
        Assert.Contains(problems, p => p.Contains("L9"));
        Assert.Throws<InvalidOperationException>(() => ConfirmationWriter.Write(bad));
    }

    [Fact]
    public void A_file_total_is_added_up_from_its_tax_lines()
    {
        var engine = new TaxEngine(TaxRates.Year2026);
        var salaryType = PaymentTypes.Get("10");
        var rentType = PaymentTypes.Get("50");
        var employee = PayeeTaxFacts.ResidentEmployee();
        var pensioner = new PayeeTaxFacts(Residency.Resident, PensionAgeThisYear: true, BasicExemptionApplied: true);
        var salary = DeclarationLineFactory.Create("s", 1, LineAction.R, salaryType, employee, PayeeId.Estonian("48712219406"), new DateOnly(2026, 10, 10),
            engine.Calculate(new TaxInput(salaryType, 1000m, false, 700m), employee));
        var rent = DeclarationLineFactory.Create("r", 1, LineAction.M, rentType, pensioner, PayeeId.Estonian("36001010009"), new DateOnly(2026, 10, 12),
            engine.Calculate(new TaxInput(rentType, 100m, false, 50m), pensioner));
        var declaration = new Declaration("10235688", 2026, 10, new DateOnly(2026, 11, 3), "10235688-2026-11-03T10:00:00.000", "SatellesTSD", "SatellesTSD", null,
        [
            new SectionEntries(Section.EE0201001, null, [salary, PaymentLine.Cancellation("old", 2)]),
            new SectionEntries(Section.EE0201002, null, [rent]),
        ]);

        var totals = DeclaredTaxTotals.OfFile(declaration);

        Assert.Equal(58.08m + 11m, totals["110"]);
        Assert.Equal(0m, totals["114"]);
        Assert.Equal(330m, totals["115"]);
        Assert.Equal(16m + 8m, totals["116"]);
        Assert.Equal(20m, totals["117"]);
        Assert.Equal(58.08m + 11m + 330m + 24m + 20m, totals["118"]);
        Assert.Equal(0m, totals["119"]);
    }

    [Fact]
    public void A_refund_file_adds_up_to_negative_figures_and_only_the_confirmation_refuses_them()
    {
        var refund = new PaymentLine("ref", 1, LineAction.R, "10", [], -100m, null, PayeeId.Estonian("48712219406"), new DateOnly(2026, 10, 5),
            [new MeasurableEvent(MeasurableEvent.CalculationDate, new DateOnly(2026, 10, 5))], [], [new TaxLine(TaxLine.SocialTax, -33m)]);
        var declaration = new Declaration("10235688", 2026, 10, new DateOnly(2026, 11, 3), "u", "c", "s", null, [new SectionEntries(Section.EE0201001, null, [refund])]);

        var totals = DeclaredTaxTotals.OfFile(declaration);
        Assert.Equal(-33m, totals["115"]);

        var confirmation = ConfirmDeclaration.ForTsd("10235688", 2026, 10, new HashSet<string> { "PAYMENTS" }, totals);
        Assert.Contains(ConfirmationWriter.Validate(confirmation), p => p.Contains("negative"));
    }

    [Fact]
    public void The_confirmation_takes_the_accountants_sections_and_all_seven_totals()
    {
        var totals = new Dictionary<string, decimal>
        {
            ["110"] = 69.08m, ["114"] = 50m, ["115"] = 330m, ["116"] = 24m, ["117"] = 20m, ["118"] = 493.08m, ["119"] = 0m,
        };

        var confirmation = ConfirmDeclaration.ForTsd("10235688", 2026, 10, new HashSet<string> { "PAYMENTS", "L4", "INF1" }, totals, personCode: "48304052720");

        Assert.Equal(TsdSections.All, confirmation.Sections.Select(s => s.Section));
        Assert.Equal([true, false, true, false, false, false, false, true], confirmation.Sections.Select(s => s.HasData));
        Assert.Equal(TaxTotalCodes.All, confirmation.TaxSums.Select(t => t.TaxType));
        Assert.Equal(50m, confirmation.TaxSums.Single(t => t.TaxType == "114").Amount);
        Assert.Equal(PayeeId.Ariregistrikood, confirmation.TaxablePersonIdentifierType);
        Assert.Empty(SchemaValidation.Validate(ConfirmationXsd, ConfirmationWriter.Serialize(confirmation)));

        // a payer in MTA's non-resident register carries the label its declaration file carries (decision 139)
        var mrr = ConfirmDeclaration.ForTsd("65758008", 2026, 10, new HashSet<string> { "PAYMENTS" }, totals, registryCodeType: PayeeId.MrrKood);
        Assert.Equal(PayeeId.MrrKood, mrr.TaxablePersonIdentifierType);
        Assert.Empty(SchemaValidation.Validate(ConfirmationXsd, ConfirmationWriter.Serialize(mrr)));

        totals.Remove("114");
        Assert.Throws<ArgumentException>(() => ConfirmDeclaration.ForTsd("10235688", 2026, 10, new HashSet<string> { "PAYMENTS" }, totals));
        Assert.Throws<ArgumentException>(() => ConfirmDeclaration.ForTsd("10235688", 2026, 10, new HashSet<string> { "L9" }, totals));
    }
}
