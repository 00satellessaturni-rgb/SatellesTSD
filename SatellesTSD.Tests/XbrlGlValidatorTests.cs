using System.Xml.Linq;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Tax;
using SatellesTSD.Core.Xbrl;

namespace SatellesTSD.Tests;

/// <summary>Schema validation against the APA-restricted set in schemas/apa-tsd: MTA's example and our own files must
/// pass, and the restrictions MTA refuses as BLACK must be caught.</summary>
public class XbrlGlValidatorTests
{
    private const string CompanyExample = "APA_TSD_lahteandmed_ariuhingule_naide_XML_20251017.xml";

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static readonly XbrlGlValidator Validator = new(Path.Combine(AppContext.BaseDirectory, "schemas", "apa-tsd"));

    private static Declaration SampleDeclaration()
    {
        var engine = new TaxEngine(TaxRates.Year2026);
        var allocator = new MonthAllocator(engine);
        var salary = PaymentTypes.Get("10");
        var rent = PaymentTypes.Get("57");
        var employee = PayeeTaxFacts.ResidentEmployee();
        var landlord = new PayeeTaxFacts(Residency.Resident, BasicExemptionApplied: true);
        var nonResident = new PayeeTaxFacts(Residency.NonResident, ResidenceCountry: "LV");

        var october = allocator.Allocate(
            [new MonthPayment("s1", salary, 1000m, true, new DateOnly(2026, 10, 5)), new MonthPayment("s2", salary, 300m, true, new DateOnly(2026, 10, 28))],
            employee);
        var lines1 = october.Payments
            .Select((p, i) => DeclarationLineFactory.Create($"10235688-202610-{p.Payment.Key}", i + 1, LineAction.R, salary, employee,
                PayeeId.Estonian("48712219406"), p.Payment.Date, p.Result, new DateOnly(2026, 9, 30)))
            .ToList();
        var minimumOnly = allocator.Allocate([MonthPayment.Zero("z", salary, new DateOnly(2026, 10, 31))], employee).Payments[0];
        lines1.Add(DeclarationLineFactory.Create("10235688-202610-z", 3, LineAction.R, salary, employee, PayeeId.Estonian("38001010033"),
            new DateOnly(2026, 10, 31), minimumOnly.Result));
        var foreign = engine.Calculate(new TaxInput(PaymentTypes.Get("120"), 800m, false, 0m), nonResident);
        lines1.Add(DeclarationLineFactory.Create("10235688-202610-nr", 4, LineAction.M, PaymentTypes.Get("120"), nonResident, PayeeId.Mrr("A0003689779"),
            new DateOnly(2026, 10, 15), foreign));
        lines1.Add(PaymentLine.Cancellation("10235688-202610-old", 5));

        var rentResult = engine.Calculate(new TaxInput(rent, 400m, false, 700m), landlord);
        var lines2 = new List<PaymentLine>
        {
            DeclarationLineFactory.Create("10235688-202610-rent", 1, LineAction.R, rent, landlord, PayeeId.Estonian("46002020011"), new DateOnly(2026, 10, 3), rentResult),
        };

        return new Declaration("10235688", 2026, 10, new DateOnly(2026, 11, 3),
            Declaration.MakeUniqueId("10235688", new DateTime(2026, 11, 3, 10, 0, 0, 1)),
            "Saturni Raamatupidamine OÜ", "SatellesTSD", "Andmepõhise aruandluse väljamaksete andmed MTA-le",
            [new SectionEntries(Section.EE0201001, "PARANDUSPOHJUS_1", lines1), new SectionEntries(Section.EE0201002, null, lines2)]);
    }

    [Fact]
    public void The_schema_set_compiles_offline_and_accepts_MTAs_company_example()
    {
        Assert.Empty(Validator.ValidateFile(Fixture(CompanyExample)));
    }

    [Fact]
    public void A_declaration_from_the_engine_is_schema_valid()
    {
        var declaration = SampleDeclaration();

        Assert.Empty(DeclarationValidator.Validate(declaration));
        Assert.Empty(Validator.Validate(XbrlGlWriter.Serialize(declaration)));
        Assert.Empty(Validator.Validate(XbrlGlWriter.Write(declaration)));
    }

    [Fact]
    public void An_element_outside_the_APA_restriction_is_refused()
    {
        var doc = XDocument.Load(Fixture(CompanyExample));
        var taxes = doc.Descendants(XbrlGlWriter.GlCor + "taxes").First();
        taxes.AddFirst(new XElement(XbrlGlWriter.GlCor + "taxBasis", new XAttribute("contextRef", "now"), "something"));

        var problems = Validator.Validate(doc);

        Assert.NotEmpty(problems);
        Assert.Contains(problems, p => p.Contains("taxBasis"));
    }

    [Fact]
    public void Decimals_other_than_2_are_refused()
    {
        var text = File.ReadAllText(Fixture(CompanyExample));
        var index = text.IndexOf("decimals=\"2\"", StringComparison.Ordinal);
        var broken = text[..index] + "decimals=\"4\"" + text[(index + "decimals=\"2\"".Length)..];

        var problems = Validator.Validate(broken);

        Assert.NotEmpty(problems);
        Assert.Contains(problems, p => p.Contains("decimals"));
    }

    [Fact]
    public void A_wrong_element_order_is_refused()
    {
        var doc = XDocument.Load(Fixture(CompanyExample));
        var detail = doc.Descendants(XbrlGlWriter.GlCor + "entryDetail").First();
        var documentDate = detail.Element(XbrlGlWriter.GlCor + "documentDate")!;
        documentDate.Remove();
        detail.Element(XbrlGlWriter.GlCor + "identifierReference")!.AddBeforeSelf(documentDate);

        Assert.NotEmpty(Validator.Validate(doc));
    }

    [Fact]
    public void A_second_context_is_refused()
    {
        var doc = XDocument.Load(Fixture(CompanyExample));
        var context = doc.Root!.Element(XbrlGlWriter.Xbrli + "context")!;
        var copy = new XElement(context);
        copy.SetAttributeValue("id", "later");
        context.AddAfterSelf(copy);

        Assert.NotEmpty(Validator.Validate(doc));
    }

    [Fact]
    public void A_missing_schema_folder_is_a_clear_error()
    {
        Assert.Throws<FileNotFoundException>(() => new XbrlGlValidator(Path.Combine(Path.GetTempPath(), "no-such-schemas")));
    }
}
