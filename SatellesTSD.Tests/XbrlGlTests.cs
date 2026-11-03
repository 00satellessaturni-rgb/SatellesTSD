using System.Xml.Linq;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Tax;
using SatellesTSD.Core.Xbrl;

namespace SatellesTSD.Tests;

/// <summary>The writer against MTA's example instances: read → write must reproduce the file, and the line factory
/// fed with the engine's figures must reproduce the example's payment lines element for element.</summary>
public class XbrlGlTests
{
    private const string CompanyExample = "APA_TSD_lahteandmed_ariuhingule_naide_XML_20251017.xml";
    private const string CancellationExample = "APA_TSD_lahteandmed_ariuhingule_naide_tuhistamine_XML_20251017.xml";

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static Declaration Empty(params SectionEntries[] sections) =>
        new("10235688", 2026, 10, new DateOnly(2026, 11, 3),
            Declaration.MakeUniqueId("10235688", new DateTime(2026, 11, 3, 9, 36, 51, 554)),
            "Saturni Raamatupidamine OÜ", "SatellesTSD", null, sections);

    private static PaymentLine SalaryLine(string key, int counter, DateOnly date, decimal gross = 1000m)
    {
        var type = PaymentTypes.Get("10");
        var facts = PayeeTaxFacts.ResidentEmployee();
        var result = new TaxEngine(TaxRates.Year2026).Calculate(new TaxInput(type, gross, false, 700m), facts);
        return DeclarationLineFactory.Create(key, counter, LineAction.R, type, facts, PayeeId.Estonian("48712219406"), date, result);
    }

    [Theory]
    [InlineData(CompanyExample)]
    [InlineData(CancellationExample)]
    public void An_MTA_example_read_and_written_again_is_the_same_document(string file)
    {
        var original = XDocument.Load(Fixture(file));

        var model = XbrlGlReader.Read(original);
        var written = XbrlGlWriter.Write(model);

        XmlCanon.AssertEqual(original.Root!, written.Root!);
    }

    [Fact]
    public void The_company_example_reads_into_the_expected_shape()
    {
        var d = XbrlGlReader.Load(Fixture(CompanyExample));

        Assert.Equal("10235688", d.RegistryCode);
        Assert.Equal((2026, 9), (d.Year, d.Month));
        Assert.Equal(new DateOnly(2026, 9, 30), d.PeriodEnd);
        Assert.Equal([Section.EE0201001, Section.EE0201002], d.Sections.Select(s => s.Section));
        Assert.Equal(9, d.Sections[0].Lines.Count);
        Assert.Single(d.Sections[1].Lines);

        var salary = d.Sections[0].Lines.Single(l => l.LineNumber == "Tehing_2111");
        Assert.Equal("10", salary.TypeCode);
        Assert.Equal(LineAction.R, salary.Action);
        Assert.Equal(1000m, salary.Amount);
        Assert.Equal(PayeeId.Estonian("48712219406"), salary.Payee);
        Assert.Equal(new DateOnly(2026, 9, 10), salary.DocumentDate);
        Assert.Equal([TaxLine.SocialTax, TaxLine.FundedPension, TaxLine.UnemploymentEmployee, TaxLine.UnemploymentEmployer, TaxLine.IncomeTax],
            salary.Taxes.Select(t => t.Code));

        var refund = d.Sections[0].Lines.Single(l => l.LineNumber == "Tehing_11111");
        Assert.Equal(-100m, refund.Amount);
        Assert.Equal([MeasurableEvent.CalculationDate, MeasurableEvent.RefundOriginalDate], refund.Events.Select(e => e.Id));
    }

    [Fact]
    public void The_cancellation_example_is_a_bare_T_line()
    {
        var d = XbrlGlReader.Load(Fixture(CancellationExample));

        var line = Assert.Single(Assert.Single(d.Sections).Lines);
        Assert.Equal(LineAction.T, line.Action);
        Assert.Null(line.TypeCode);
        Assert.Null(line.Amount);
        Assert.Null(line.Payee);
        Assert.Empty(line.Taxes);
    }

    [Fact]
    public void The_reader_refuses_a_decimal_comma_and_a_fractional_counter()
    {
        var text = File.ReadAllText(Fixture(CompanyExample));

        var comma = XDocument.Parse(text.Replace(">58.08<", ">58,08<"));
        Assert.Throws<InvalidDataException>(() => XbrlGlReader.Read(comma));

        var fraction = XDocument.Parse(text.Replace("unitRef=\"NotUsed\">1<", "unitRef=\"NotUsed\">1.5<"));
        Assert.Throws<InvalidDataException>(() => XbrlGlReader.Read(fraction));
    }

    [Fact]
    public void A_written_file_starts_with_the_UTF8_declaration_and_has_no_BOM()
    {
        var declaration = Empty(new SectionEntries(Section.EE0201001, null, []), new SectionEntries(Section.EE0201002, null, []));

        var text = XbrlGlWriter.Serialize(declaration);

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", text);
        Assert.DoesNotContain('﻿', text);
        Assert.Contains("<gl-cor:uniqueID contextRef=\"now\">10235688-2026-11-03T09:36:51.554</gl-cor:uniqueID>", text);
        Assert.Contains("<gl-cor:periodCoveredStart contextRef=\"now\">2026-10-01</gl-cor:periodCoveredStart>", text);
        Assert.Contains("<gl-cor:periodCoveredEnd contextRef=\"now\">2026-10-31</gl-cor:periodCoveredEnd>", text);
        Assert.Equal(2, text.Split("<gl-cor:entryHeader>").Length - 1);
        Assert.DoesNotContain("entryDetail", text);

        var path = Path.Combine(Path.GetTempPath(), $"satellestsd-{Guid.NewGuid():N}.xml");
        try
        {
            XbrlGlWriter.Save(declaration, path);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal([(byte)'<', (byte)'?', (byte)'x'], bytes.Take(3));
            Assert.Contains("Saturni Raamatupidamine OÜ", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Numbers_are_written_with_two_decimals_a_dot_and_no_grouping()
    {
        Assert.Equal("1125.12", XbrlGlWriter.Amount(1125.12m));
        Assert.Equal("1000.00", XbrlGlWriter.Amount(1000m));
        Assert.Equal("-1.60", XbrlGlWriter.Amount(-1.6m));
        Assert.Equal("0.22", XbrlGlWriter.Amount(0.22m));
        Assert.Equal("1234567.89", XbrlGlWriter.Amount(1234567.89m));
    }

    [Fact]
    public void A_correction_reason_is_written_on_the_section_and_read_back()
    {
        var section = new SectionEntries(Section.EE0201001, "PARANDUSPOHJUS_1", [PaymentLine.Cancellation("Tehing_2111", 1)]);

        var header = XbrlGlWriter.WriteSection(section);
        Assert.Equal(["entryNumber", "entryComment", "entryDetail"], header.Elements().Select(e => e.Name.LocalName));

        var back = XbrlGlReader.Read(XbrlGlWriter.Write(Empty(section)));
        Assert.Equal("PARANDUSPOHJUS_1", Assert.Single(back.Sections).CorrectionReason);
    }

    [Fact]
    public void Counters_are_assigned_per_file_from_the_lines_positions()
    {
        var day = new DateOnly(2026, 10, 10);
        var declaration = Empty(
            new SectionEntries(Section.EE0201001, null, [SalaryLine("a", 0, day), PaymentLine.Cancellation("old", 0), SalaryLine("b", 0, day)]),
            new SectionEntries(Section.EE0201002, null, []));

        var numbered = declaration.WithCounters();

        Assert.Equal([1, 2, 3], numbered.Sections[0].Lines.Select(l => l.Counter));
        Assert.Equal(["a", "old", "b"], numbered.Sections[0].Lines.Select(l => l.LineNumber));
        Assert.Equal([0, 0, 0], declaration.Sections[0].Lines.Select(l => l.Counter)); // the original is untouched
    }

    [Fact]
    public void The_validator_names_every_problem_and_the_writer_refuses_the_file()
    {
        var day = new DateOnly(2026, 10, 10);
        var lineWithoutPayee = SalaryLine("nopayee", 2, day) with { Payee = null };
        var declaration = Empty(
            new SectionEntries(Section.EE0201001, null, [SalaryLine("dup", 1, day), SalaryLine("dup", 2, day), SalaryLine("late", 3, new DateOnly(2026, 11, 1)), lineWithoutPayee]),
            new SectionEntries(Section.EE0201002, null, [SalaryLine("wrongsection", 1, day)]),
            new SectionEntries(Section.EE0201002, null, []));

        var problems = DeclarationValidator.Validate(declaration);

        Assert.Contains(problems, p => p.Contains("dup") && p.Contains("repeated"));
        Assert.Contains(problems, p => p.Contains("late") && p.Contains("outside the file's month"));
        Assert.Contains(problems, p => p.Contains("wrongsection") && p.Contains("belongs in EE0201001"));
        Assert.Contains(problems, p => p.Contains("nopayee") && p.Contains("payee"));
        Assert.Contains(problems, p => p.Contains("EE0201002 appears twice"));
        Assert.Equal(5, problems.Count);
        var refused = Assert.Throws<InvalidOperationException>(() => XbrlGlWriter.Write(declaration));
        Assert.Contains("late", refused.Message);
    }

    [Fact]
    public void A_file_without_any_section_is_refused()
    {
        var problems = DeclarationValidator.Validate(Empty());

        Assert.Contains(problems, p => p.Contains("no data section"));
        Assert.Throws<InvalidOperationException>(() => XbrlGlWriter.Write(Empty()));
    }

    [Fact]
    public void A_valid_file_has_no_problems()
    {
        var declaration = Empty(
            new SectionEntries(Section.EE0201001, null, [SalaryLine("a", 1, new DateOnly(2026, 10, 1)), SalaryLine("b", 2, new DateOnly(2026, 10, 31))]),
            new SectionEntries(Section.EE0201002, null, []));

        Assert.Empty(DeclarationValidator.Validate(declaration));
        Assert.Empty(DeclarationValidator.Validate(XbrlGlReader.Load(Fixture(CompanyExample))));
    }

    public static IEnumerable<object[]> ExampleLines()
    {
        var rates = TaxRates.Year2026;
        var employee = PayeeTaxFacts.ResidentEmployee();
        yield return Case("Tehing_2111", "10", 1000m, 700m, employee, rates);
        yield return Case("Tehing_2112", "24", 100m, 0m, employee, rates);
        yield return Case("Tehing_21113", "10", 900m, 776m,
            new PayeeTaxFacts(Residency.Resident, FundedPensionMember: false, OnOldAgeOrFlexiblePension: true, PensionAgeThisYear: true, BasicExemptionApplied: true), rates);
        yield return Case("Tehing_21114", "10", 1000m, 700m,
            employee with { A1 = A1Case.EstonianA1WorkAbroad, A1Country = "EE", WorkCountry = "FI" }, rates);
        yield return Case("Tehing_21115", "10", 1000m, 700m,
            employee with { A1 = A1Case.ForeignA1WorkInEstonia, A1Country = "LV", WorkCountry = "EE" }, rates);
        yield return Case("Tehing_21116", "120", 1000m, 700m,
            new PayeeTaxFacts(Residency.NonResident, ResidenceCountry: "LV", BasicExemptionApplied: true), rates);
        yield return Case("Tehing_21117", "10", 0m, 0m, employee, rates with { SocialTaxMinimumBase = 820m }, increase: 820m);
        yield return Case("Tehing_31111", "50", 100m, 50m,
            new PayeeTaxFacts(Residency.Resident, PensionAgeThisYear: true, BasicExemptionApplied: true), rates);

        static object[] Case(string line, string type, decimal gross, decimal cap, PayeeTaxFacts facts, TaxRates rates, decimal increase = 0m) =>
            [line, type, gross, cap, facts, rates, increase];
    }

    [Theory]
    [MemberData(nameof(ExampleLines))]
    public void The_factory_reproduces_the_example_line(string lineNumber, string typeCode, decimal gross, decimal cap, PayeeTaxFacts facts, TaxRates rates, decimal increase)
    {
        var example = XbrlGlReader.Load(Fixture(CompanyExample));
        var expected = example.Sections.SelectMany(s => s.Lines).Single(l => l.LineNumber == lineNumber);
        var type = PaymentTypes.Get(typeCode);

        var result = new TaxEngine(rates).Calculate(new TaxInput(type, gross, false, cap, increase), facts);
        var actual = DeclarationLineFactory.Create(
            expected.LineNumber, expected.Counter, LineAction.R, type, facts, expected.Payee!, expected.DocumentDate!.Value, result,
            calculationDate: expected.Events.Single(e => e.Id == MeasurableEvent.CalculationDate).Start,
            allocationCode: expected.AllocationCode);

        if (lineNumber == "Tehing_21117")
        {
            // MTA's example (17.10.2025) omits TSD_SMVM on the zero-amount line; rule KR_SM_01_1 (v2.0, 24.07.2026) requires it
            // on every type subject to social tax, and SMVM = amount holds at 0, so the factory writes it.
            expected = expected with { Amounts = [new MeasurableAmount(MeasurableAmount.SocialTaxBase, 0m), .. expected.Amounts] };
        }

        var expectedXml = XbrlGlWriter.WriteLine(expected);
        if (lineNumber == "Tehing_21115")
        {
            // MTA's example writes parentSubaccountType = RR_A1 on this line; the classifier (and every other example line)
            // uses RIIGIROLL2022ap. docs/reference/apa-xml-reference.md, gotcha 11.
            foreach (var wrong in expectedXml.Descendants(XbrlGlWriter.GlCor + "parentSubaccountType").Where(e => e.Value == "RR_A1"))
            {
                wrong.Value = AccountSubEntry.RoleClassifier;
            }
        }

        XmlCanon.AssertEqual(expectedXml, XbrlGlWriter.WriteLine(actual));
    }

    [Fact]
    public void The_factory_defaults_the_calculation_date_to_the_payment_date()
    {
        var line = SalaryLine("L1", 1, new DateOnly(2026, 10, 10));

        var only = Assert.Single(line.Events);
        Assert.Equal(MeasurableEvent.CalculationDate, only.Id);
        Assert.Equal(new DateOnly(2026, 10, 10), only.Start);
    }

    [Fact]
    public void The_factory_refuses_a_calculation_date_after_the_payment_date()
    {
        var type = PaymentTypes.Get("10");
        var facts = PayeeTaxFacts.ResidentEmployee();
        var result = new TaxEngine(TaxRates.Year2026).Calculate(new TaxInput(type, 1000m, false, 700m), facts);

        Assert.Throws<InvalidOperationException>(() => DeclarationLineFactory.Create("L1", 1, LineAction.R, type, facts,
            PayeeId.Estonian("48712219406"), new DateOnly(2026, 10, 10), result, calculationDate: new DateOnly(2026, 10, 11)));
    }

    [Fact]
    public void The_factory_refuses_a_non_resident_without_a_residence_country()
    {
        var type = PaymentTypes.Get("120");
        var facts = new PayeeTaxFacts(Residency.NonResident);
        var result = new TaxEngine(TaxRates.Year2026).Calculate(new TaxInput(type, 1000m, false, 0m), facts);

        Assert.Throws<InvalidOperationException>(() => DeclarationLineFactory.Create("L1", 1, LineAction.R, type, facts,
            PayeeId.Mrr("A0003689779"), new DateOnly(2026, 10, 10), result));
    }

    [Fact]
    public void A_posted_workers_rent_carries_no_A1_entries_and_keeps_its_income_tax()
    {
        var type = PaymentTypes.Get("50");
        var facts = PayeeTaxFacts.ResidentEmployee(exemptionApplied: false) with { A1 = A1Case.EstonianA1WorkAbroad, A1Country = "EE", WorkCountry = "FI" };
        var result = new TaxEngine(TaxRates.Year2026).Calculate(new TaxInput(type, 500m, false, 0m), facts);

        var line = DeclarationLineFactory.Create("rent", 1, LineAction.R, type, facts, PayeeId.Estonian("48712219406"), new DateOnly(2026, 10, 10), result);

        Assert.Empty(line.ExtraAccountSubs);
        Assert.Equal(110m, Assert.Single(line.Taxes).Amount);
        Assert.Equal(TaxLine.IncomeTax, Assert.Single(line.Taxes).Code);
    }

    [Fact]
    public void A_cancellation_line_carries_only_the_key_the_counter_and_T()
    {
        var xml = XbrlGlWriter.WriteLine(PaymentLine.Cancellation("Tehing_2111", 1));

        Assert.Equal(["lineNumber", "lineNumberCounter", "account"], xml.Elements().Select(e => e.Name.LocalName));
        var sub = Assert.Single(xml.Element(XbrlGlWriter.GlCor + "account")!.Elements());
        Assert.Equal("T", sub.Element(XbrlGlWriter.GlCor + "accountSubID")!.Value);
        Assert.Equal(XbrlGlWriter.ActionClassifier, sub.Element(XbrlGlWriter.GlCor + "accountSubType")!.Value);
    }
}
