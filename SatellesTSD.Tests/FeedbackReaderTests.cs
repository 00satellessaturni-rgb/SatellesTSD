using System.IO.Compression;
using System.Xml.Linq;
using SatellesTSD.Core.Feedback;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Xbrl;

namespace SatellesTSD.Tests;

/// <summary>The feedback reader against MTA's five sample reports (docs/reference/apa-xml-reference.md, section F).</summary>
public class FeedbackReaderTests
{
    private const string FileError = "APA_TSD_20260330_tagasisideraport_faili_enda_veaga.xml";
    private const string Partly = "APA_TSD_20260330_tagasisideraport_osaliselt_vigadega.xml";
    private const string Colours = "APA_TSD_tagasisideraporti_naidis_warning_red_blue_black_vigadega_20260910.xml";
    private const string ConfirmOk = "APA_TSD_20260330_tagasisideraport_kinnitamine_ok.xml";
    private const string ConfirmError = "APA_TSD_20260330_tagasisideraport_kinnitamine_error.xml";

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "feedback", name);

    private static string FeedbackXsd => Path.Combine(AppContext.BaseDirectory, "schemas", "apa-tsd", "feedback", "feedbackReport-2026-03-30.xsd");

    [Fact]
    public void A_file_level_error_refuses_the_whole_file_and_trims_the_code()
    {
        var report = FeedbackReader.Load(Fixture(FileError));

        Assert.Equal(ProcessingResult.Error, report.Result);
        Assert.True(report.FileRefused);
        var only = Assert.Single(report.FileValidations);
        Assert.Equal("KR_VALIDATE_XBRL_FILE_7", only.Code); // the sample has a leading space
        Assert.Equal("Maksustamisperiood peab olema kalendrikuu põhine", only.Description);
        Assert.Equal("Tax period must be one calendar month", only.DescriptionEn);
        Assert.Empty(report.Outcomes());
    }

    [Fact]
    public void A_partly_erroneous_report_gives_one_outcome_per_line_with_the_servers_taxes()
    {
        var report = FeedbackReader.Load(Fixture(Partly));

        Assert.Equal(ProcessingResult.PartlyError, report.Result);
        Assert.False(report.FileRefused);
        Assert.Equal([Section.EE0201001, Section.EE0201002], report.Sections.Select(s => s.Section));

        var outcomes = report.Outcomes();
        Assert.Equal(["XX1", "XX2", "XX_8", "XX_9", "XX_10"], outcomes.Select(o => o.LineNumber));

        var ok = outcomes[0];
        Assert.Equal(ValidationLevel.Ok, ok.Level);
        Assert.Equal(Severity.None, ok.Severity);
        Assert.Equal(NextAction.None, ok.NextAction);
        Assert.Equal(241.45m, ok.ServerTaxes["TSD_KTM"]); // decimal comma in the sample
        Assert.Equal(1, ok.Counter);

        var warned = outcomes[1];
        Assert.Equal(Severity.Warning, warned.Severity);
        Assert.Equal(NextAction.ReviewWarning, warned.NextAction);
        Assert.Equal("KR_ISIK_3", Assert.Single(warned.Messages).Code);
        Assert.Equal(161m, warned.ServerTaxes["TSD_KTM"]);
        Assert.Equal(200m, warned.ServerTaxes["TSD_SM"]);

        var plainError = outcomes[2];
        Assert.Equal(Severity.Error, plainError.Severity); // the 2026-03 vocabulary has no colour
        Assert.Equal(NextAction.Review, plainError.NextAction);
        Assert.Equal("Action type is missing", Assert.Single(plainError.Messages).InfoEn);

        Assert.Equal(["KR_SM_02", "KR_MVT_01"], outcomes[3].Messages.Select(m => m.Code));
        Assert.Equal("Русский перевод сообщения об ошибке", outcomes[3].Messages[0].InfoRu);

        var second = outcomes[4];
        Assert.Equal(Section.EE0201002, second.Section);
        Assert.Equal(5, second.Counter); // the counter does not restart in the second section of this sample
    }

    [Fact]
    public void The_colour_sample_ranks_each_line_by_its_worst_message()
    {
        var report = FeedbackReader.Load(Fixture(Colours));
        var outcomes = report.Outcomes().ToDictionary(o => o.LineNumber, StringComparer.Ordinal);

        var blue = outcomes["0110test01"];
        Assert.Equal(Severity.Blue, blue.Severity); // WARNING + ERROR_BLUE → blue
        Assert.Equal(NextAction.CorrectWithChange, blue.NextAction);
        Assert.Equal(["KR_VM_03", "KR_ISIK_3"], blue.Messages.Select(m => m.Code)); // worst first
        Assert.Equal(2, blue.Counter);

        var red = outcomes["0117test01"];
        Assert.Equal(Severity.Red, red.Severity);
        Assert.Equal(NextAction.CorrectWithChange, red.NextAction);
        Assert.Contains("106,04", Assert.Single(red.Messages).Info);

        var black = outcomes["0254test01"];
        Assert.Equal(Severity.Black, black.Severity);
        Assert.Equal(NextAction.Review, black.NextAction); // without the submitted action the right resend is unknown
        Assert.Null(black.SubmittedAction);
        Assert.Null(black.Counter); // black errors come without lineNumberCounter

        Assert.Equal(Severity.Black, outcomes["0201test01"].Severity);
        Assert.Equal(4, outcomes.Count);
    }

    [Fact]
    public void A_black_errors_resend_action_follows_what_was_sent()
    {
        var report = FeedbackReader.Load(Fixture(Colours));
        var submitted = new Dictionary<LineKey, LineAction>
        {
            [new LineKey("EE0201002", "0254test01")] = LineAction.R,
            [new LineKey("EE0201002", "0201test01")] = LineAction.M,
            [new LineKey("EE0201001", "0110test01")] = LineAction.R,
        };

        var outcomes = report.Outcomes(submitted).ToDictionary(o => o.LineNumber, StringComparer.Ordinal);

        Assert.Equal(NextAction.FixAndResendAsNew, outcomes["0254test01"].NextAction); // the R was not saved: send an R again
        Assert.Equal(LineAction.R, outcomes["0254test01"].SubmittedAction);
        Assert.Equal(NextAction.CorrectWithChange, outcomes["0201test01"].NextAction); // the M was refused, the original stands: fix and resend as M
        Assert.Equal(NextAction.CorrectWithChange, outcomes["0110test01"].NextAction); // blue on a saved R
    }

    [Fact]
    public void The_key_rules_decide_the_resend_whatever_was_sent()
    {
        Assert.Equal(NextAction.CorrectWithChange, FeedbackReport.ActionFor(Severity.Black, LineAction.R, [FeedbackReport.RuleNewOnExistingKey]));
        Assert.Equal(NextAction.FixAndResendAsNew, FeedbackReport.ActionFor(Severity.Black, LineAction.M, [FeedbackReport.RuleChangeOnUnknownKey]));
        Assert.Equal(NextAction.Review, FeedbackReport.ActionFor(Severity.Blue, LineAction.R, [FeedbackReport.RuleWrongTypeOrSection]));
        Assert.Equal(NextAction.Review, FeedbackReport.ActionFor(Severity.Black, LineAction.T, ["KR_XBRL_VALJAMAKSED_8"]));
        Assert.Equal(NextAction.Review, FeedbackReport.ActionFor(Severity.Black, null, ["KR_XBRL_VALJAMAKSED_14"]));
        Assert.Equal(NextAction.Review, FeedbackReport.ActionFor(Severity.Error, LineAction.R, ["KR_SM_02"]));
        Assert.Equal(NextAction.Review, FeedbackReport.ActionFor(Severity.Red, LineAction.T, ["KR_TM_4"]));
        Assert.Equal(NextAction.CorrectWithChange, FeedbackReport.ActionFor(Severity.Red, LineAction.M, ["KR_TM_4"]));
        Assert.Equal(NextAction.ReviewWarning, FeedbackReport.ActionFor(Severity.Warning, LineAction.R, ["KR_ISIK_3"]));
        Assert.Equal(NextAction.None, FeedbackReport.ActionFor(Severity.None, LineAction.R, []));
    }

    [Fact]
    public void A_confirmation_reports_result_is_named()
    {
        Assert.Equal(ConfirmationOutcome.Confirmed, FeedbackReader.Load(Fixture(ConfirmOk)).ConfirmationResult());
        Assert.Equal(ConfirmationOutcome.WithErrors, FeedbackReader.Load(Fixture(ConfirmError)).ConfirmationResult());
        Assert.Equal(ConfirmationOutcome.Unknown, FeedbackReader.Load(Fixture(Partly)).ConfirmationResult());
    }

    [Fact]
    public void A_confirmation_report_carries_the_result_code_and_the_totals()
    {
        var ok = FeedbackReader.Load(Fixture(ConfirmOk));
        Assert.Equal(ProcessingResult.Ok, ok.Result);
        Assert.False(ok.FileRefused);
        Assert.Equal("OK", Assert.Single(ok.FileValidations).Code);
        var totals = ok.Totals();
        Assert.Equal(7, totals.Count);
        Assert.Equal(509.58m, totals["110"]);
        Assert.Equal(1433.64m, totals["118"]); // decimal dot here
        Assert.Equal(0m, totals["119"]);
        Assert.Empty(ok.Outcomes());

        var error = FeedbackReader.Load(Fixture(ConfirmError));
        Assert.Equal(ProcessingResult.Error, error.Result);
        Assert.True(error.FileRefused);
        Assert.Equal("DECLARATION_WITH_ERRORS", Assert.Single(error.FileValidations).Code);
        Assert.Equal(847.11m, error.Totals()["115"]);
    }

    [Fact]
    public void A_zip_with_several_reports_is_read_in_name_order()
    {
        var path = Path.Combine(Path.GetTempPath(), $"satellestsd-feedback-{Guid.NewGuid():N}.zip");
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(Fixture(Partly), "b-" + Partly);
                zip.CreateEntryFromFile(Fixture(FileError), "a-" + FileError);
                zip.CreateEntry("notes.txt");
            }

            var reports = FeedbackReader.LoadAll(path);

            Assert.Equal([ProcessingResult.Error, ProcessingResult.PartlyError], reports.Select(r => r.Result));
            Assert.Single(FeedbackReader.LoadAll(Fixture(Colours)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("241,45", true, 241.45)]
    [InlineData("1433.64", true, 1433.64)]
    [InlineData(" 0.00 ", true, 0)]
    [InlineData("-1,6", true, -1.6)]
    [InlineData("1,433.64", false, 0)]
    [InlineData("1,433", false, 0)]
    [InlineData("abc", false, 0)]
    [InlineData("", false, 0)]
    public void Numbers_accept_a_comma_or_a_dot_but_not_both(string text, bool ok, double expected)
    {
        Assert.Equal(ok, FeedbackNumbers.TryParse(text, out var value));
        if (ok)
        {
            Assert.Equal((decimal)expected, value);
        }
    }

    [Fact]
    public void A_message_is_told_from_a_tax_by_its_description()
    {
        Assert.Equal(Severity.Black, new ResponseItem("KR_X", "ERROR_BLACK", "t", null, null).Severity);
        Assert.Equal(Severity.Error, new ResponseItem("KR_X", "ERROR", "t", null, null).Severity);
        Assert.Equal(Severity.None, new ResponseItem("TSD_SM", "Sotsiaalmaks", "200,00", null, null).Severity);
        Assert.True(new ResponseItem("TSD_SM", "Sotsiaalmaks", "200,00", null, null).IsTax);
        Assert.True(new ResponseItem("118", "Maksukohustus kokku", "1.00", null, null).IsTotal);
        Assert.False(new ResponseItem("KR_X", "WARNING", "t", null, null).IsTax);
    }

    [Fact]
    public void A_root_other_than_processing_or_a_report_without_headers_is_refused()
    {
        Assert.Throws<InvalidDataException>(() => FeedbackReader.Read(XDocument.Parse("<other result=\"OK\"/>")));
        Assert.Throws<InvalidDataException>(() => FeedbackReader.Read(XDocument.Parse("<processing result=\"ERROR\"/>")));
    }

    [Fact]
    public void A_default_namespace_does_not_hide_the_content()
    {
        var text = File.ReadAllText(Fixture(Partly)).Replace("<processing result=\"PARTLYERROR\">", "<processing xmlns=\"urn:mta:test\" result=\"PARTLYERROR\">");

        var report = FeedbackReader.Read(XDocument.Parse(text));

        Assert.Equal(2, report.Sections.Count);
        Assert.Equal(5, report.Outcomes().Count);
        Assert.Equal(241.45m, report.Outcomes()[0].ServerTaxes["TSD_KTM"]);
    }

    [Fact]
    public void The_servers_verdict_on_a_line_is_never_softened_by_a_lesser_message()
    {
        const string xml = """
            <processing result="ERROR">
              <entryHeader>
                <entryNumber>EE0201001</entryNumber>
                <validations>
                  <validation><errorLevel>WARNING</errorLevel><errorCode>WARNING</errorCode><errorDescription>h</errorDescription>
                    <lineReferences><lineReference><lineInternalId>L1</lineInternalId></lineReference></lineReferences></validation>
                  <validation><errorLevel>ERROR</errorLevel><errorCode>ERROR</errorCode><errorDescription>v</errorDescription>
                    <lineReferences>
                      <lineReference><lineInternalId>L1</lineInternalId><lineNumberCounter>7</lineNumberCounter></lineReference>
                      <lineReference><lineInternalId>L2</lineInternalId><lineNumberCounter>8</lineNumberCounter></lineReference>
                      <lineReference><lineInternalId>L3</lineInternalId><lineNumberCounter>9</lineNumberCounter></lineReference>
                    </lineReferences></validation>
                </validations>
                <responseData>
                  <entryDetail>
                    <lineReferences><lineReference><lineInternalId>L1</lineInternalId><lineNumberCounter>7</lineNumberCounter></lineReference></lineReferences>
                    <data><code>KR_ISIK_3</code><description>WARNING</description><info>surnud</info></data>
                    <data><code>TSD_SM</code><description>Sotsiaalmaks</description><info>1 433,64</info></data>
                    <data><code>KR_NEW</code><description>INFO</description><info>something new</info></data>
                  </entryDetail>
                  <entryDetail>
                    <lineReferences><lineReference><lineInternalId>L3</lineInternalId><lineNumberCounter>9</lineNumberCounter></lineReference></lineReferences>
                    <data><code>KR_A</code><description>ERROR_RED</description><info>r</info></data>
                    <data><code>KR_B</code><description>ERROR_BLUE</description><info>b</info></data>
                  </entryDetail>
                </responseData>
              </entryHeader>
            </processing>
            """;

        var outcomes = FeedbackReader.Read(XDocument.Parse(xml)).Outcomes().ToDictionary(o => o.LineNumber, StringComparer.Ordinal);

        var l1 = outcomes["L1"];
        Assert.Equal(ValidationLevel.Error, l1.Level);      // the later ERROR validation wins over the WARNING one
        Assert.Equal(7, l1.Counter);                         // filled in from the reference that carried it
        Assert.Equal(Severity.Error, l1.Severity);           // an ERROR verdict with only a WARNING message is an error
        Assert.Equal(NextAction.Review, l1.NextAction);
        Assert.Equal(2, l1.Unclassified.Count);              // the unparsable tax and the unknown description are kept
        Assert.Empty(l1.ServerTaxes);

        var l2 = outcomes["L2"];
        Assert.Equal(Severity.Error, l2.Severity);           // no message at all, the verdict stands
        Assert.Equal(NextAction.Review, l2.NextAction);

        var l3 = outcomes["L3"];
        Assert.Equal(Severity.Red, l3.Severity);             // red outranks blue whatever the order in the document
        Assert.Equal("KR_A", l3.Messages[0].Code);
    }

    [Fact]
    public void Black_outranks_an_uncoloured_error_and_a_section_level_validation_is_not_lost()
    {
        const string xml = """
            <processing result="ERROR">
              <entryHeader>
                <entryNumber>EE0201001</entryNumber>
                <validations>
                  <validation><errorLevel>ERROR</errorLevel><errorCode>KR_SECTION_X</errorCode><errorDescription>about the section</errorDescription></validation>
                </validations>
                <responseData>
                  <entryDetail>
                    <lineReferences><lineReference><lineInternalId>L1</lineInternalId></lineReference></lineReferences>
                    <data><code>KR_A</code><description>ERROR</description><info>e</info></data>
                    <data><code>KR_B</code><description>ERROR_BLACK</description><info>k</info></data>
                  </entryDetail>
                </responseData>
              </entryHeader>
            </processing>
            """;

        var report = FeedbackReader.Read(XDocument.Parse(xml));

        Assert.Equal(Severity.Black, Assert.Single(report.Outcomes()).Severity);
        var general = Assert.Single(report.GeneralValidations);
        Assert.Equal("EE0201001", general.EntryNumber);
        Assert.Equal("KR_SECTION_X", general.Validation.Code);
        Assert.Equal(ValidationLevel.Error, general.Validation.Level);
        Assert.True(report.HasGeneralError);
        Assert.False(report.FileRefused);
        Assert.Equal(ReportKind.Submission, report.Kind);
    }

    [Fact]
    public void The_kind_and_the_source_of_a_report_are_known()
    {
        Assert.Equal(ReportKind.Confirmation, FeedbackReader.Load(Fixture(ConfirmOk)).Kind);
        Assert.Equal(ReportKind.Confirmation, FeedbackReader.Load(Fixture(ConfirmError)).Kind);
        Assert.Equal(ReportKind.Submission, FeedbackReader.Load(Fixture(FileError)).Kind);
        Assert.Equal(ReportKind.Submission, FeedbackReader.Load(Fixture(Colours)).Kind);
        Assert.Equal(Colours, FeedbackReader.Load(Fixture(Colours)).Source);
    }

    [Fact]
    public void A_zip_is_recognised_by_its_content_not_its_name()
    {
        var path = Path.Combine(Path.GetTempPath(), $"satellestsd-feedback-{Guid.NewGuid():N}.xml");
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(Fixture(ConfirmOk), "sub/" + ConfirmOk);
            }

            var reports = FeedbackReader.LoadAll(path);

            var only = Assert.Single(reports);
            Assert.Equal(ReportKind.Confirmation, only.Kind);
            Assert.Equal("sub/" + ConfirmOk, only.Source);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(FileError, true)]
    [InlineData(Partly, true)]
    [InlineData(ConfirmOk, true)]
    [InlineData(ConfirmError, true)]
    [InlineData(Colours, false)] // MTA's own 2026-09 sample omits lineNumberCounter on black errors, which its XSD requires
    public void MTAs_samples_against_MTAs_feedback_schema(string file, bool valid)
    {
        var problems = SchemaValidation.Validate(FeedbackXsd, File.ReadAllText(Fixture(file)));

        Assert.Equal(valid, problems.Count == 0);
        if (!valid)
        {
            Assert.Contains(problems, p => p.Contains("lineNumberCounter"));
        }
    }
}
