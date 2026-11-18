using SatellesTSD.Core.Declare;
using SatellesTSD.Core.Feedback;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Report;
using SatellesTSD.Core.Review;
using SatellesTSD.Core.Store;
using SatellesTSD.Core.Xbrl;

namespace SatellesTSD.WindowTests;

/// <summary>The submission and feedback path in Estonian: the export's, the withdrawal's, the marks' and the reopen's
/// refusals, the file's own check, the feedback reader's and the store's refusals, each worded by its code and figures; the
/// verdict's severity and the finding's level in words. The Core's English stays the message, after the Estonian in the log.</summary>
public class SubmitFeedbackWindowTests
{
    // words of the Core's English that no Estonian text of this path holds
    private static readonly string[] English = [" the ", " is ", " not ", "cannot", "submission", "draft line", "Only ", "period", "report", "line ", "section", "must ", " no "];

    private static void NoEnglish(string key, string? said)
    {
        Assert.False(string.IsNullOrWhiteSpace(said), key);
        Assert.All(English, english => Assert.DoesNotContain(english, said!, StringComparison.Ordinal));
    }

    [Fact]
    public void Every_refusal_of_the_submitter_is_worded_in_Estonian_by_its_code_and_case()
    {
        static DeclareException Of(string code, string? variant = null, string? subject = null) => new(code, "The English as the Core has it.") { Variant = variant, Subject = subject };
        var said = new Dictionary<string, string?>
        {
            ["NO_DRAFTS"] = UiText.Worded(Of(PeriodSubmitter.NoDrafts)),
            ["LINES_STALE"] = UiText.Worded(new DeclareException(PeriodSubmitter.LinesStale, "x") { Subject = "12", Variant = "M", Section = Section.EE0201001, Detail = LineKeyRules.KeyUnknown }),
            ["REASON none"] = UiText.Worded(Of(PeriodSubmitter.ReasonNeeded, PeriodSubmitter.ReasonNone)),
            ["REASON unknown"] = UiText.Worded(Of(PeriodSubmitter.ReasonNeeded, PeriodSubmitter.ReasonUnknown, "PARANDUSPOHJUS_9")),
            ["REASON missing"] = UiText.Worded(new DeclareException(PeriodSubmitter.ReasonNeeded, "x") { Variant = PeriodSubmitter.ReasonMissing, Section = Section.EE0201002 }),
            ["REASON misfit"] = UiText.Worded(new DeclareException(PeriodSubmitter.ReasonNeeded, "x") { Variant = PeriodSubmitter.ReasonMisfit, Subject = "PARANDUSPOHJUS_4", Section = Section.EE0201001 }),
            ["INVALID_FILE structure"] = UiText.Worded(new DeclareException(PeriodSubmitter.InvalidFile, "x") { Variant = PeriodSubmitter.FileStructure, Problems = [new DeclarationProblem(DeclarationValidator.PayerEmpty, "x")] }),
            ["INVALID_FILE schema"] = UiText.Worded(new DeclareException(PeriodSubmitter.InvalidFile, "x") { Variant = PeriodSubmitter.FileSchema, SchemaProblems = [new XbrlSchemaProblem(System.Xml.Schema.XmlSeverityType.Error, 3, 7, "x")] }),
            ["SCHEMAS_MISSING"] = UiText.Worded(Of(PeriodSubmitter.SchemasMissing, subject: @"C:\programm\schemas")),
            ["WRONG_STATE withdraw"] = UiText.Worded(Of(PeriodSubmitter.WrongState, PeriodSubmitter.StateWithdraw, "Uploaded")),
            ["WRONG_STATE withdrawNotLatest"] = UiText.Worded(Of(PeriodSubmitter.WrongState, PeriodSubmitter.StateWithdrawNotLatest, "tsd-2.xml")),
            ["WRONG_STATE upload"] = UiText.Worded(Of(PeriodSubmitter.WrongState, PeriodSubmitter.StateUpload, "Draft")),
            ["WRONG_STATE confirm"] = UiText.Worded(Of(PeriodSubmitter.WrongState, PeriodSubmitter.StateConfirm, "Confirmed")),
            ["WRONG_STATE reopen"] = UiText.Worded(Of(PeriodSubmitter.WrongState, PeriodSubmitter.StateReopen, "Draft")),
            ["WRONG_STATE undoReopen"] = UiText.Worded(Of(PeriodSubmitter.WrongState, PeriodSubmitter.StateUndoReopen, "Exported")),
            ["LINES_PENDING confirm"] = UiText.Worded(new DeclareException(PeriodSubmitter.LinesPending, "x") { DraftLines = ["3"] }),
            ["LINES_PENDING undoReopen"] = UiText.Worded(new DeclareException(PeriodSubmitter.LinesPending, "x") { Variant = PeriodSubmitter.StateUndoReopen, DraftLines = ["3", "4"] }),
        };
        Assert.All(said, pair => NoEnglish(pair.Key, pair.Value));

        // the figures, as the window shows them
        Assert.Equal("Rida 12 (Töötasud (01), M muudatus) ei sobi enam võtme varasemate versioonidega: " + UiText.LineKeyText(LineKeyRules.KeyUnknown) + ". Vajuta enne eksporti uuesti „Koosta read“.", said["LINES_STALE"]);
        Assert.Equal("Põhjus „4 — tagasiulatuv hüvitis (ainult hüvitised)“ ei sobi osale Töötasud (01). Vali sellele osale sobiv põhjus.", said["REASON misfit"]);
        Assert.StartsWith("Osale Hüvitised jm (02) ei ole paranduse põhjust valitud.", said["REASON missing"], StringComparison.Ordinal);
        Assert.StartsWith("Periood on olekus „Üles laaditud“. Eksporti saab tühistada („Tühista eksport“)", said["WRONG_STATE withdraw"], StringComparison.Ordinal);
        Assert.Equal("Tühistada saab ainult viimase faili eksporti (tsd-2.xml); varasem fail jääb ajalukku.", said["WRONG_STATE withdrawNotLatest"]);
        Assert.StartsWith("Periood on olekus „Kinnitatud“. Kinnitatuks saab märkida ainult eksporditud või üles laaditud perioodi.", said["WRONG_STATE confirm"], StringComparison.Ordinal);
        Assert.Contains("rida 3, veerg 7: x", said["INVALID_FILE schema"], StringComparison.Ordinal);
        Assert.Equal("XML-faili ei kirjutatud, sest kontroll leidis vead:" + Environment.NewLine + "Maksja registrikood on tühi.", said["INVALID_FILE structure"]);
        Assert.Contains(@"(C:\programm\schemas)", said["SCHEMAS_MISSING"], StringComparison.Ordinal);
        Assert.Equal("Ülevaatus erineb e-MTA-s olevast: 2 rida tuleb saata parandusena (3, 4).", said["LINES_PENDING undoReopen"]);
        Assert.StartsWith("Saatmata mustandread: 3. Ekspordi need", said["LINES_PENDING confirm"], StringComparison.Ordinal);

        // a code or case the window does not know: the Core's English is shown, as before
        Assert.Null(UiText.Worded(Of(PeriodSubmitter.WrongState, "someOtherCase", "Draft")));
        Assert.Null(UiText.Worded(Of(PeriodSubmitter.WrongState, PeriodSubmitter.StateUpload, "NotAState")));
        Assert.Null(UiText.Worded(Of("SOME_NEW_CODE")));
        Assert.Null(UiText.Worded(Of(PeriodSubmitter.InvalidFile, PeriodSubmitter.FileStructure))); // an old refusal without its problems
    }

    [Fact]
    public void Every_problem_of_the_files_own_check_is_worded_in_Estonian_with_its_figures()
    {
        var octoberFirst = new DateOnly(2026, 10, 1);
        var said = DeclarationValidator.Codes.ToDictionary(code => code, code => UiText.DeclarationProblemText(new DeclarationProblem(code, "The English.")
        {
            Section = code is DeclarationValidator.PayerEmpty or DeclarationValidator.PayerNotRegistry or DeclarationValidator.PayerNotPersonal or DeclarationValidator.PayerMrrInvalid or DeclarationValidator.NoSection ? null : Section.EE0201002,
            Key = code is DeclarationValidator.SectionTwice or DeclarationValidator.KeyEmpty or DeclarationValidator.PayerEmpty or DeclarationValidator.PayerNotRegistry or DeclarationValidator.PayerNotPersonal or DeclarationValidator.PayerMrrInvalid or DeclarationValidator.NoSection ? null : "k7",
            Subject = code is DeclarationValidator.TypeWrongSection ? "10" : "A 1",
            OtherSection = Section.EE0201001,
            Day = new DateOnly(2026, 11, 1),
            Month = octoberFirst,
        }));
        Assert.All(said, pair => NoEnglish(pair.Key, pair.Value));

        Assert.Equal("Hüvitised jm (02), rida k7: väljamakse kuupäev 01.11.2026 jääb väljapoole faili kuud 10.2026 (KR_XBRL_VALJAMAKSED_17).", said[DeclarationValidator.DateOutsideMonth]);
        Assert.Equal("Hüvitised jm (02), rida k7: väljamakse liik 10 kuulub osasse Töötasud (01), mitte siia (KR_VM_05).", said[DeclarationValidator.TypeWrongSection]);
        Assert.StartsWith("Hüvitised jm (02), rida k7: „A 1“ ei ole MTA mitteresidendi kood", said[DeclarationValidator.PayeeMrrInvalid], StringComparison.Ordinal);
        Assert.Equal("Osa Hüvitised jm (02) on failis kaks korda; iga osa võib failis olla ainult üks kord.", said[DeclarationValidator.SectionTwice]);
        Assert.Null(UiText.DeclarationProblemText(new DeclarationProblem("XBRL_SOMETHING_NEW", "The English.")));

        // the simulation's XML tab says the export's refusal as the main window does
        var mrr = new DeclarationProblem(DeclarationValidator.PayeeMrrInvalid, "EE0201001 line k1: 'A 1' is not an MTA non-resident code…") { Section = Section.EE0201001, Key = "k1", Subject = "A 1" };
        var refused = new DeclareException(PeriodSubmitter.InvalidFile, "The file cannot be written: " + mrr.Message) { Variant = PeriodSubmitter.FileStructure, Problems = [mrr] };
        var tab = UiText.SimXmlText(new Core.Review.SimResult { XmlRefusal = refused.Message, XmlRefusalError = refused });
        Assert.Equal("XML-faili ei kirjutatud, sest kontroll leidis vead:" + Environment.NewLine + UiText.DeclarationProblemText(mrr), tab);
        Assert.StartsWith("Töötasud (01), rida k1: „A 1“ ei ole MTA mitteresidendi kood", UiText.DeclarationProblemText(mrr), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_refusal_of_the_store_and_the_feedback_reader_is_worded_in_Estonian_by_its_code()
    {
        static Exception Of(string code, string? subject = null, IReadOnlyList<string>? items = null) => SubmissionRefusal.Of(new InvalidOperationException("x"), code, subject, items: items);
        var said = SubmissionRefusal.Codes.ToDictionary(code => code, code => UiText.Worded(code switch
        {
            SubmissionRefusal.NotPending => Of(code, "Accepted"),
            SubmissionRefusal.WriteFailed => Of(code, @"S:\eksport"),
            SubmissionRefusal.NotReport => Of(code, "tsd"),
            SubmissionRefusal.NoEntry => Of(code, "ERROR"),
            SubmissionRefusal.OtherFile => Of(code, items: ["EE0201001/12", "EE0201002/k3"]),
            SubmissionRefusal.PlacedElsewhere => Of(code, items: ["EE0201001/12|1|2"]),
            _ => Of(code),
        }));
        Assert.All(said, pair => NoEnglish(pair.Key, pair.Value));

        Assert.Contains("(12 (Töötasud (01)), k3 (Hüvitised jm (02)))", said[SubmissionRefusal.OtherFile], StringComparison.Ordinal);
        Assert.Contains("(12 (Töötasud (01)): raportis 1. rida, failis 2. rida)", said[SubmissionRefusal.PlacedElsewhere], StringComparison.Ordinal);
        Assert.Contains("olek on „vastu võetud“", said[SubmissionRefusal.NotPending], StringComparison.Ordinal);
        Assert.StartsWith(@"XML-faili ei saanud kausta S:\eksport kirjutada.", said[SubmissionRefusal.WriteFailed], StringComparison.Ordinal);
        Assert.Equal(UiText.FileUnreadable("x"), said[SubmissionRefusal.FileUnopened]); // the system's own reason after it
        Assert.Contains("<tsd>", said[SubmissionRefusal.NotReport], StringComparison.Ordinal);

        // a code of another path, or none: not this path's words
        Assert.Null(UiText.FeedbackRefused(SubmissionRefusal.Of(new InvalidOperationException("x"), "READ_WEB_PAGE")));
        Assert.Null(UiText.FeedbackRefused(SubmissionRefusal.Of(new InvalidOperationException("x"), "FEEDBACK_SOMETHING_NEW")));
        Assert.Null(UiText.Worded(new InvalidDataException("The report has no root element.")));
    }

    [Fact]
    public void The_severity_and_the_level_are_words_for_every_value_and_the_two_grids_say_the_same()
    {
        Assert.All(Enum.GetValues<Severity>(), s => Assert.NotEqual(s.ToString(), ReportText.SeverityText(s)));
        Assert.All(Enum.GetValues<ValidationLevel>(), l => Assert.NotEqual(l.ToString(), ReportText.LevelText(l)));
        Assert.Equal(Enum.GetValues<Severity>().Length, Enum.GetValues<Severity>().Select(ReportText.SeverityText).Distinct().Count()); // each its own word
        var feedback = new LineFeedback(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, ValidationLevel.Error, 1, Severity.Red, NextAction.CorrectWithChange,
            [new ResponseItem("KR_TEST", "ERROR_RED", "vaata üle", null, null)], new Dictionary<string, decimal>(), []);
        Assert.Equal("punane viga: KR_TEST", ReportText.FeedbackCell(feedback));
        Assert.Contains("KR_TEST (punane viga): vaata üle", UiText.FeedbackDetail(feedback, null), StringComparison.Ordinal);
        Assert.Equal("Hüvitised jm (02): viga KR_A — kirjeldus", UiText.GeneralText(new GeneralValidation("EE0201002", new Validation(ValidationLevel.Error, "KR_A", "kirjeldus", null, null, []))));
        Assert.Equal("fail: hoiatus KR_B — kirjeldus", UiText.GeneralText(new GeneralValidation(null, new Validation(ValidationLevel.Warning, "KR_B", "kirjeldus", null, null, []))));
        Assert.Equal("EE9: teadmata KR_C — kirjeldus", UiText.GeneralText(new GeneralValidation("EE9", new Validation(ValidationLevel.Unknown, "KR_C", "kirjeldus", null, null, []))));
    }

    [Fact]
    public void A_report_of_another_file_is_refused_in_Estonian_with_the_English_after_it_in_the_log()
    {
        string? report = null;
        using var fixture = new WindowFixture((store, _, period, folder) =>
        {
            WindowFixture.ExportAndUpload(store, period, folder);
            report = Path.Combine(folder, "tagasiside.xml");
            File.WriteAllText(report, """
                <processing result="OK">
                  <entryHeader>
                    <entryNumber>EE0201001</entryNumber>
                    <validations><validation><errorLevel>OK</errorLevel><errorCode>OK</errorCode><errorDescription>ok</errorDescription>
                      <lineReferences><lineReference><lineInternalId>muu-voti</lineInternalId><lineNumberCounter>1</lineNumberCounter></lineReference></lineReferences>
                    </validation></validations>
                  </entryHeader>
                </processing>
                """);
        });
        Ui.Run(() => fixture.Drive(form =>
        {
            form.ChoosingFeedback = () => report;
            form.Tabs.SelectedIndex = 1;
            Ui.Pump();
            var panel = form.Declaration;
            panel.SubmissionsList.Items[0].Selected = true;
            Ui.Pump();
            var before = form.LogList.Items.Count;
            panel.FeedbackButton.PerformClick();
            Ui.Until(() => !form.IsBusy && fixture.Failures.Count > 0, what: "the report's refusal");

            var refusal = Assert.Single(fixture.Failures);
            var worded = UiText.Worded(refusal);
            Assert.Equal("Raport nimetab ridu, mida see fail ei kandnud (muu-voti (Töötasud (01))): raport kuulub teisele failile. Vali saadetud failide loendist fail, millele raport vastab, ja loe tagasiside uuesti. Midagi ei salvestatud.", worded);
            var line = Assert.Single(form.LogList.Items.Cast<object>().Select(i => i.ToString()!).Skip(before), l => l.Contains($"{UiText.Problem}: ", StringComparison.Ordinal));
            Assert.EndsWith($"{UiText.Problem}: {worded} ({refusal.Message})", line, StringComparison.Ordinal);
            Assert.Contains("another submission", refusal.Message, StringComparison.Ordinal); // the Core's English kept
            fixture.Failures.Clear();
        }));
    }

    [Fact]
    public void A_withdrawal_of_a_file_with_a_line_settled_by_hand_is_refused_in_Estonian()
    {
        using var fixture = new WindowFixture((store, _, period, folder) =>
        {
            new PeriodDeclarer(store).Build(period.Id);
            var submitter = WindowFixture.Submitter(store);
            submitter.Export(period.Id, folder);
            submitter.Resolve(store.Payments(period.Id).Single().Id, onServer: true); // settled by hand while still Exported
        });
        Ui.Run(() => fixture.Drive(form =>
        {
            form.Tabs.SelectedIndex = 1;
            Ui.Pump();
            var panel = form.Declaration;
            panel.SubmissionsList.Items[0].Selected = true;
            Ui.Pump();
            Assert.True(panel.WithdrawButton.Enabled);
            panel.WithdrawButton.PerformClick();
            Ui.Until(() => !form.IsBusy && fixture.Failures.Count > 0, what: "the withdrawal's refusal");

            var refusal = Assert.Single(fixture.Failures);
            Assert.Equal(SubmissionRefusal.WithdrawAnswered, SubmissionRefusal.CodeOf(refusal));
            Assert.StartsWith("Failil on juba vastus", UiText.Worded(refusal), StringComparison.Ordinal);
            Assert.Contains($"{UiText.Problem}: Failil on juba vastus", form.LogList.Items[^1]?.ToString(), StringComparison.Ordinal);
            fixture.Failures.Clear();
        }));
    }

    [Fact]
    public void An_export_to_a_folder_that_cannot_be_written_says_so_in_Estonian_with_the_systems_reason()
    {
        using var fixture = new WindowFixture((store, _, period, _) => new PeriodDeclarer(store).Build(period.Id));
        var blocked = Path.Combine(fixture.Folder, "fail-kausta-asemel");
        File.WriteAllText(blocked, "x");
        Ui.Run(() => fixture.Drive(form =>
        {
            form.ChoosingFolder = _ => blocked;
            form.Tabs.SelectedIndex = 1;
            Ui.Pump();
            form.Declaration.ExportButton.PerformClick();
            Ui.Until(() => !form.IsBusy && fixture.Failures.Count > 0, what: "the export's refusal");

            var refusal = Assert.Single(fixture.Failures);
            Assert.IsAssignableFrom<IOException>(refusal); // the system's own refusal, its type kept
            Assert.Equal($"XML-faili ei saanud kausta {blocked} kirjutada. Kontrolli, kas kaust on kättesaadav (kas võrguketas on ühendatud), ja ekspordi uuesti. Windowsi põhjus: {refusal.Message}", UiText.Worded(refusal));
            var logged = form.LogList.Items.Cast<object>().Last(i => i.ToString()!.Contains(UiText.Problem, StringComparison.Ordinal)).ToString()!;
            Assert.Contains($"{UiText.Problem}: XML-faili ei saanud kausta", logged, StringComparison.Ordinal);
            Assert.Equal($"{UiText.Problem}: {UiText.Worded(refusal)}", logged[logged.IndexOf(UiText.Problem, StringComparison.Ordinal)..]); // Windows' reason said once, not again in brackets
            Assert.Empty(form.Services!.Store.Submissions(form.View!.Period.Id));
            fixture.Failures.Clear();
        }));
    }

    [Fact]
    public void An_undone_reopen_with_lines_to_send_logs_them_in_Estonian()
    {
        using var fixture = new WindowFixture((store, _, period, folder) =>
        {
            WindowFixture.ExportAndUpload(store, period, folder);
            var submitter = WindowFixture.Submitter(store);
            submitter.Resolve(store.Payments(period.Id).Single().Id, onServer: true);
            submitter.MarkConfirmed(period.Id);
            submitter.Reopen(period.Id);
            var workspace = new ReviewWorkspace(store);
            var mari = workspace.Load(period.Id).Persons[0].Person!;
            workspace.SavePerson(mari with { FundedPensionMember = true, PensionRate = 0.04m }); // the salary's figures change: an M to send
        });
        var informed = new List<string>();
        Ui.Run(() => fixture.Drive(form =>
        {
            form.Informing = informed.Add;
            form.Tabs.SelectedIndex = 1;
            Ui.Pump();
            var panel = form.Declaration;
            Assert.Equal(UiText.UndoReopen, panel.ReopenButton.Text);
            var before = form.LogList.Items.Count;
            panel.ReopenButton.PerformClick();
            Ui.Until(() => !form.IsBusy && informed.Count > 0, what: "the undo's answer");

            var line = Assert.Single(form.LogList.Items.Cast<object>().Select(i => i.ToString()!).Skip(before), l => l.Contains($"{UiText.UndoReopen}: Ülevaatus", StringComparison.Ordinal));
            Assert.Matches(@"Loobu parandamisest: Ülevaatus erineb e-MTA-s olevast: 1 rida tuleb saata parandusena \([^)]+\)\. \(1 draft line\(s\) differ from the server", line);
        }));
    }
}
