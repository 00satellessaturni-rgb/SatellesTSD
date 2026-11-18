using SatellesTSD.Core.Declare;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Review;
using SatellesTSD.Core.Store;
using SatellesTSD.Core.Tax;

namespace SatellesTSD.WindowTests;

/// <summary>The build's notes and refusals in Estonian (plan item 66, the build notes): every code and case the build says
/// is worded by the window with its figures, the code verbatim before it (decision 46); the log after „Koosta read“ and
/// the simulation's text show the Estonian; a code the window does not know falls back to the Core's English.</summary>
public class BuildNotesWindowTests
{
    private static readonly string[] EnglishGiveaways = [" the ", " is ", " not ", " was ", " and ", " of ", "declared", " payment", " line", "register has"];

    private static BuildNote Note(string code, string? variant = null) => new(code, code + ": english") { Variant = variant };

    /// <summary>Every note the build makes, by its code and case, with the figures it carries, and a figure the Estonian must show.</summary>
    public static TheoryData<string, string> Cases() => new()
    {
        { "INCLUDED noRowNoPay", "Näidis Isik" },
        { "INCLUDED noValid", "sel kuul kehtivat töötamist" },
        { "INCLUDED noValid paid", "2 makset" },
        { "INCLUDED noValid paid one ask", "1 makse" },
        { "INCLUDED noValid noTypeNoneDeclared", "Nullrida ega makse rida" },
        { "INCLUDED noValid noType", "töötamise liik on teadmata" },
        { "INCLUDED noValid typeNoMinimum", "liik 17 ei kanna" },
        { "INCLUDED noValid a1", "A1 juhul" },
        { "INCLUDED paused", "17.10.2026" },
        { "EXCLUDED", "välja jäetud." },
        { "EXCLUDED one", "1 deklareerimiseks märgitud makse jääb" },
        { "EXCLUDED many", "3 deklareerimiseks märgitud makset jääb" },
        { "REFUSED", "käsitsi" },
        { "RESIDENCY_NOT_CHECKED", "väljamakse liik 10" },
        { "MINIMUM_HELD zeroLine", "LHV [12], käsitsi" },
        { "MINIMUM_HELD topUp", "kuu makse LHV [12] kannab" },
        { "PENSION_NOT_CHECKED", "Kontrolli registrid" },
        { "PENSION_RATE_UNKNOWN", "määr on teadmata" },
        { "PENSION_RATE_INVALID", "ei ole lubatud" },
        { "ZERO_LINE valid", "TÖR liik 1)" },
        { "ZERO_LINE ended", "lõpetatud 03.11.2026" },
        { "ZERO_LINE paused card", "peatatud alates 01.11.2026; liik kaardilt" },
        { "ZERO_LINE asIfEmployed", "nullrida väljamakse liigiga 10" },
        { "NO_PAYEE", "isikukoodi" },
        { "CODE_INVALID", "„AB12“" },
        { "FACTS_OF_ANOTHER_MONTH lookedUpFor", "seisuga 30.09.2026" },
        { "FACTS_OF_ANOTHER_MONTH checkedOn", "kontrolliti 15.09.2026" },
        { "FACTS_OF_ANOTHER_DAY", "seisuga 20.10.2026, ülevaatuse järgi on isiku kontrollpäev nüüd 31.10.2026" },
        { "NO_RESIDENCE_COUNTRY", "„Maksuresidentsuse riik“" },
        { "NO_EMPLOYMENT_IN_MONTH notCovered", "ei kata" },
        { "NO_EMPLOYMENT_IN_MONTH notListed", "kuu TÖR-is isikut ei ole" },
        { "NO_EMPLOYMENT_IN_MONTH paused", "kogu kuu peatatud" },
        { "NO_EMPLOYMENT_IN_MONTH notInForce", "„9“" },
        { "MINIMUM_WITH_PENSION", "„Vanaduspensionil“" },
        { "NOT_ALLOCATED keptPools one", "rida 0007 on serveris ja kasutab" },
        { "NOT_ALLOCATED keptPools many", "read 0007, 0008 on serveris ja kasutavad" },
        { "NOT_ALLOCATED engine", "kuu makseid ei saanud arvutada: avalduse maksuvaba summa" },
        { "KEY_DEAD", "LHV [12]: võti on tühistatud" },
        { "CANCELLATION_PENDING keyRule", "märgi enne MTA vastus" },
        { "DRAFT_PENDING", "mustand ootab saatmist" },
        { "KEY_EXISTS", "KR_XBRL_VALJAMAKSED_4" },
        { "KEY_UNKNOWN", "võtit pole serveris" },
        { "NOT_BUILT", "miinimum: rida ei saa koostada: A1 juhul on vaja nii „A1 riik“" },
        { "NOT_BUILT store", "rea 0007 (Töötasud (01))" },
        { "CANCELLATION_PENDING reconcile", "rea 0005 tühistus ootab MTA vastust; uus rida" },
        { "CANCELLATION_PENDING retire", "koond, liik 10: LHV [12]: rea 0005" },
        { "SECTION_CHANGED", "osasse Hüvitised jm (02); rida 0004" },
    };

    private static BuildNote Make(string name)
    {
        var day = new DateOnly(2026, 10, 17);
        return name switch
        {
            "INCLUDED noRowNoPay" => Note(PeriodDeclarer.Included, "noRowNoPay") with { Subject = "Näidis Isik" },
            "INCLUDED noValid" => Note(PeriodDeclarer.Included, "noValid"),
            "INCLUDED noValid paid" => Note(PeriodDeclarer.Included, "noValid") with { Count = 2, Items = ["OUTSIDE_TOR"] },
            "INCLUDED noValid paid one ask" => Note(PeriodDeclarer.Included, "noValid") with { Count = 1, Items = ["OUTSIDE_TOR"], AskCardType = true, Unmet = "noTypeNoneDeclared" },
            "INCLUDED noValid noTypeNoneDeclared" => Note(PeriodDeclarer.Included, "noValid") with { Unmet = "noTypeNoneDeclared" },
            "INCLUDED noValid noType" => Note(PeriodDeclarer.Included, "noValid") with { Unmet = "noType" },
            "INCLUDED noValid typeNoMinimum" => Note(PeriodDeclarer.Included, "noValid") with { Unmet = "typeNoMinimum", TypeId = "17" },
            "INCLUDED noValid a1" => Note(PeriodDeclarer.Included, "noValid") with { Unmet = "a1" },
            "INCLUDED paused" => Note(PeriodDeclarer.Included, "paused") with { Day = day },
            "EXCLUDED" => Note(PeriodDeclarer.Excluded) with { Count = 0 },
            "EXCLUDED one" => Note(PeriodDeclarer.Excluded) with { Count = 1 },
            "EXCLUDED many" => Note(PeriodDeclarer.Excluded) with { Count = 3 },
            "REFUSED" => Note(PeriodDeclarer.Refused) with { Reference = ReviewWorkspace.ByHandSource, Items = ["TYPE_MISSING"] },
            "RESIDENCY_NOT_CHECKED" => Note(ReviewChecks.ResidencyNotChecked, "zeroLine") with { PaymentType = "10" },
            "MINIMUM_HELD zeroLine" => Note(PeriodDeclarer.MinimumHeld, "zeroLine") with { PaymentType = "10", Items = ["LHV [12]", ReviewWorkspace.ByHandSource] },
            "MINIMUM_HELD topUp" => Note(PeriodDeclarer.MinimumHeld, "topUp") with { Items = ["LHV [12]"] },
            "PENSION_NOT_CHECKED" => Note(PaymentChecks.PensionNotChecked, "zeroLine") with { PaymentType = "10" },
            "PENSION_RATE_UNKNOWN" => Note(PaymentChecks.PensionRateUnknown, "zeroLine") with { PaymentType = "10" },
            "PENSION_RATE_INVALID" => Note(PaymentChecks.PensionRateInvalid, "zeroLine") with { PaymentType = "10" },
            "ZERO_LINE valid" => Note(PeriodDeclarer.ZeroLine, "valid") with { TypeId = "1", PaymentType = "10" },
            "ZERO_LINE ended" => Note(PeriodDeclarer.ZeroLine, "ended") with { TypeId = "1", PaymentType = "10", Day = new DateOnly(2026, 11, 3) },
            "ZERO_LINE paused card" => Note(PeriodDeclarer.ZeroLine, "paused") with { TypeId = "1", PaymentType = "10", Day = new DateOnly(2026, 11, 1), FromCard = true },
            "ZERO_LINE asIfEmployed" => Note(PeriodDeclarer.ZeroLine, "asIfEmployed") with { TypeId = "1", PaymentType = "10" },
            "NO_PAYEE" => Note(PeriodDeclarer.NoPayee),
            "CODE_INVALID" => Note(ReviewChecks.CodeInvalid) with { Subject = "AB12" },
            "FACTS_OF_ANOTHER_MONTH lookedUpFor" => Note(PeriodDeclarer.FactsOfAnotherMonth, "lookedUpFor") with { Day = new DateOnly(2026, 9, 30) },
            "FACTS_OF_ANOTHER_MONTH checkedOn" => Note(PeriodDeclarer.FactsOfAnotherMonth, "checkedOn") with { Day = new DateOnly(2026, 9, 15) },
            "FACTS_OF_ANOTHER_DAY" => Note(PeriodDeclarer.FactsOfAnotherDay) with { Day = new DateOnly(2026, 10, 20), OtherDay = new DateOnly(2026, 10, 31) },
            "NO_RESIDENCE_COUNTRY" => Note(PeriodDeclarer.NoResidenceCountry),
            "NO_EMPLOYMENT_IN_MONTH notCovered" => Note(PeriodDeclarer.NoEmploymentInMonth, "notCovered"),
            "NO_EMPLOYMENT_IN_MONTH notListed" => Note(PeriodDeclarer.NoEmploymentInMonth, "notListed"),
            "NO_EMPLOYMENT_IN_MONTH paused" => Note(PeriodDeclarer.NoEmploymentInMonth, "paused"),
            "NO_EMPLOYMENT_IN_MONTH notInForce" => Note(PeriodDeclarer.NoEmploymentInMonth, "notInForce") with { Subject = "9" },
            "MINIMUM_WITH_PENSION" => Note(PeriodDeclarer.MinimumWithPension),
            "NOT_ALLOCATED keptPools one" => Note(PeriodDeclarer.NotAllocated, "keptPools") with { Items = ["0007"] },
            "NOT_ALLOCATED keptPools many" => Note(PeriodDeclarer.NotAllocated, "keptPools") with { Items = ["0007", "0008"] },
            "NOT_ALLOCATED engine" => Note(PeriodDeclarer.NotAllocated, "engine") with { Cause = TaxRefusal.Of(PaymentChecks.ExemptionAboveLegal, "x", figure: 5000m, other: 700m) },
            "KEY_DEAD" => Note(LineKeyRules.KeyDead, "keyRule") with { Reference = "LHV [12]" },
            "CANCELLATION_PENDING keyRule" => Note(LineKeyRules.CancellationPending, "keyRule") with { Reference = "LHV [12]" },
            "DRAFT_PENDING" => Note(LineKeyRules.DraftPending, "keyRule") with { Reference = "LHV [12]" },
            "KEY_EXISTS" => Note(LineKeyRules.KeyExists, "keyRule") with { Reference = "LHV [12]" },
            "KEY_UNKNOWN" => Note(LineKeyRules.KeyUnknown, "keyRule") with { Reference = "LHV [12]" },
            "NOT_BUILT" => Note(PeriodDeclarer.NotBuilt) with { Reference = PeriodDeclarer.MinimumSource, Cause = TaxRefusal.Of(PaymentChecks.A1CountriesMissing, "x") },
            "NOT_BUILT store" => Note(PeriodDeclarer.NotBuilt) with { Reference = "LHV [12]", Cause = TaxRefusal.Of(TaxRefusal.RowTaken, "x", "EE0201001 0007") },
            "CANCELLATION_PENDING reconcile" => Note(PeriodDeclarer.CancellationPending, "reconcile") with { Reference = "LHV [12]", Key = "0005" },
            "CANCELLATION_PENDING retire" => Note(PeriodDeclarer.CancellationPending, "retire") with { Reference = PeriodDeclarer.TotalsRef("10", ["LHV [12]"]), Key = "0005" },
            "SECTION_CHANGED" => Note(PeriodDeclarer.SectionChanged) with { Reference = "LHV [12]", Section = Section.EE0201002, Key = "0004" },
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Every_note_of_the_build_is_worded_in_Estonian_with_its_figures(string name, string figure)
    {
        var note = Make(name);
        var said = UiText.BuildNoteText(note);
        Assert.NotNull(said);
        Assert.StartsWith(note.Code + ": ", said, StringComparison.Ordinal); // the code verbatim (decision 46)
        Assert.DoesNotContain("english", said, StringComparison.Ordinal); // never the Core's text
        foreach (var giveaway in EnglishGiveaways)
        {
            Assert.DoesNotContain(giveaway, said, StringComparison.Ordinal);
        }

        Assert.Contains(figure, said, StringComparison.Ordinal);
        Assert.Equal(said, UiText.BuildNoteLine(note));
    }

    [Fact]
    public void Every_code_the_build_emits_is_in_the_table()
    {
        // the declarer's own codes, the review's it reuses, the key's rules: each one worded by some case of the table
        string[] emitted =
        [
            PeriodDeclarer.Included, PeriodDeclarer.Excluded, PeriodDeclarer.Refused, ReviewChecks.ResidencyNotChecked, PeriodDeclarer.MinimumHeld,
            PaymentChecks.PensionNotChecked, PaymentChecks.PensionRateUnknown, PaymentChecks.PensionRateInvalid, PeriodDeclarer.ZeroLine,
            PeriodDeclarer.NoPayee, ReviewChecks.CodeInvalid, PeriodDeclarer.FactsOfAnotherMonth, PeriodDeclarer.FactsOfAnotherDay,
            PeriodDeclarer.NoResidenceCountry, PeriodDeclarer.NoEmploymentInMonth, PeriodDeclarer.MinimumWithPension, PeriodDeclarer.NotAllocated,
            LineKeyRules.KeyDead, LineKeyRules.CancellationPending, LineKeyRules.DraftPending, LineKeyRules.KeyExists, LineKeyRules.KeyUnknown,
            PeriodDeclarer.NotBuilt, PeriodDeclarer.SectionChanged,
        ];
        var worded = Cases().Select(c => Make((string)c[0]).Code).ToHashSet(StringComparer.Ordinal);
        Assert.Empty(emitted.Where(code => !worded.Contains(code)));
    }

    [Fact]
    public void An_invalid_pension_rate_is_said_in_percent_as_the_review_says_it()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        var said = UiText.TaxRefusedText(TaxRefusal.Of(PaymentChecks.PensionRateInvalid, "x", figure: 1.5m));
        Assert.Equal($"II samba määr {150m.ToString("0.####", culture)} % ei ole lubatud (peab olema üle 0 % ja alla 100 %)", said);
    }

    [Fact]
    public void Every_refusal_the_engine_the_line_factory_and_the_store_carry_is_worded()
    {
        foreach (var code in TaxRefusal.Codes)
        {
            var said = UiText.TaxRefusedText(TaxRefusal.Of(code, "x", subject: "10", figure: 1.5m, other: 700m, detail: "Resident"));
            Assert.False(string.IsNullOrEmpty(said), code);
            foreach (var giveaway in EnglishGiveaways)
            {
                Assert.DoesNotContain(giveaway, said, StringComparison.Ordinal);
            }

            // escaping the build, the same refusal is a sentence through Worded
            Assert.EndsWith(".", UiText.Worded(TaxRefusal.Of(code, "x")), StringComparison.Ordinal);
        }

        foreach (var code in new[] { LineKeyRules.KeyDead, LineKeyRules.CancellationPending, LineKeyRules.DraftPending, LineKeyRules.KeyExists, LineKeyRules.KeyUnknown })
        {
            Assert.NotNull(UiText.LineKeyText(code));
            Assert.StartsWith(char.ToUpperInvariant(UiText.LineKeyText(code)![0]).ToString(), UiText.Worded(new LineKeyException(code, "x")), StringComparison.Ordinal);
        }

        Assert.Equal($"avalduse maksuvaba summa ({UiText.Money(5000m)}) peab olema 0 kuni seaduse järgi {UiText.Money(700m)} kuus (KR_KOOND_02/03)",
            UiText.TaxRefusedText(TaxRefusal.Of(PaymentChecks.ExemptionAboveLegal, "x", figure: 5000m, other: 700m)));
        Assert.Equal("väljamakse liik 10 on mitteresidendile, isik on resident", UiText.TaxRefusedText(TaxRefusal.Of(PaymentChecks.TypePayeeMismatch, "x", "10", detail: nameof(Residency.Resident))));
        Assert.Equal("väljamakse liik 120 on residendile, isik on mitteresident", UiText.TaxRefusedText(TaxRefusal.Of(PaymentChecks.TypePayeeMismatch, "x", "120", detail: nameof(Residency.NonResident))));
        Assert.Contains("on saadetud", UiText.TaxRefusedText(TaxRefusal.Of(TaxRefusal.NotDraft, "x", nameof(PaymentStatus.Sent))), StringComparison.Ordinal);
    }

    [Fact]
    public void The_builds_own_refusals_are_worded_with_their_figure()
    {
        Assert.Equal("Kuu ei ole enam mustand (Eksporditud) – ridu ei saa koostada.",
            UiText.Worded(new DeclareException(ReviewWorkspace.PeriodNotDraft, "The period is Exported; …") { Subject = nameof(PeriodState.Exported) }));
        Assert.Equal("Kuu ei ole enam mustand – ridu ei saa koostada.", UiText.Worded(new DeclareException(ReviewWorkspace.PeriodNotDraft, "x")));
        Assert.StartsWith("Programmi selles versioonis ei ole 2031. aasta maksumäärasid", UiText.Worded(new DeclareException(PeriodDeclarer.RatesMissing, "x") { Subject = "2031" }), StringComparison.Ordinal);
        Assert.StartsWith("Perioodi maksed on otsimata:", UiText.Worded(new DeclareException(PeriodDeclarer.RunStale, "x") { Variant = "notSearched" }), StringComparison.Ordinal);
        Assert.StartsWith("Perioodi failid muutusid pärast otsingut", UiText.Worded(new DeclareException(PeriodDeclarer.RunStale, "x") { Variant = "changed" }), StringComparison.Ordinal);
        Assert.StartsWith("Rida 0003 on e-MTA-s ühe makse rida", UiText.Worded(new DeclareException(PeriodDeclarer.ModeChanged, "x") { Subject = "0003", Variant = "perPayment" }), StringComparison.Ordinal);
        Assert.StartsWith("Rida 0003 on e-MTA-s väljamakse liigi koondrida", UiText.Worded(new DeclareException(PeriodDeclarer.ModeChanged, "x") { Subject = "0003", Variant = "totals" }), StringComparison.Ordinal);
        Assert.EndsWith(UiText.DeclareModeRefused[UiText.DeclareModeRefused.IndexOf("Vali", StringComparison.Ordinal)..],
            UiText.Worded(new DeclareException(PeriodDeclarer.ModeChanged, "x") { Subject = "0003", Variant = "totals" }), StringComparison.Ordinal); // the box's own way out
        Assert.Equal("serveris pole midagi tühistada", UiText.LineNoteText(BuiltLine.NothingToCancel));
        Assert.Equal("tühistus on koostatud", UiText.LineNoteText(BuiltLine.CancellationDrafted));
        Assert.Equal(PeriodDeclarer.SectionChanged, UiText.LineNoteText(PeriodDeclarer.SectionChanged)); // a code stays verbatim
    }

    [Fact]
    public void A_note_or_a_cause_the_window_does_not_know_falls_back_to_the_English()
    {
        var unknown = new BuildNote("SOMETHING_NEW", "SOMETHING_NEW: the Core's English.");
        Assert.Null(UiText.BuildNoteText(unknown));
        Assert.Equal("SOMETHING_NEW: the Core's English.", UiText.BuildNoteLine(unknown));
        Assert.Null(UiText.BuildNoteText(Note(PeriodDeclarer.ZeroLine, "someOtherCase"))); // a case not known: the English
        Assert.Equal("ZERO_LINE: english", UiText.BuildNoteLine(Note(PeriodDeclarer.ZeroLine, "someOtherCase")));

        // a cause with no code: the Estonian frame, the Core's English in brackets for support, the parameter's name left out
        var internalGuard = Note(PeriodDeclarer.NotAllocated, "engine") with { Cause = new ArgumentException("All payments must fall in the same calendar month.", "payments") };
        Assert.Equal("NOT_ALLOCATED: kuu makseid ei saanud arvutada: programmi viga (All payments must fall in the same calendar month). Ridu ei koostata – kuu maksete mustandid kustutatakse, serveris olevad read jäävad.",
            UiText.BuildNoteText(internalGuard));
        Assert.Null(UiText.BuildRefused(new InvalidOperationException("x")));
    }

    [Fact]
    public void The_simulations_problems_are_said_in_Estonian_each_once()
    {
        var pension = new BuildNote(PeriodDeclarer.MinimumWithPension, "MINIMUM_WITH_PENSION: the card says …");
        var retired = new BuildNote(PeriodDeclarer.CancellationPending, "CANCELLATION_PENDING: minimum: the cancellation of 0002 awaits MTA's verdict.")
            { Variant = "retire", Reference = PeriodDeclarer.MinimumSource, Key = "0002" };
        var result = new SimResult
        {
            Build = new PersonBuild { Name = "Näidis Isik", Notes = [pension, retired] },
            BuildNotes = [retired with { Items = new List<string>() }], // the same note said for the period too
        };
        var text = UiText.SimReport(result);
        Assert.Contains("  MINIMUM_WITH_PENSION: kaardil on märgitud „Vanaduspensionil“", text, StringComparison.Ordinal);
        Assert.Contains("  CANCELLATION_PENDING: miinimum: rea 0002 tühistus ootab MTA vastust.", text, StringComparison.Ordinal);
        Assert.Single(text.Split('\n'), l => l.Contains("CANCELLATION_PENDING", StringComparison.Ordinal));
        Assert.DoesNotContain("the card says", text, StringComparison.Ordinal);
        Assert.DoesNotContain("awaits", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_build_log_says_the_notes_in_Estonian()
    {
        using var fixture = new WindowFixture((store, _, period, _) =>
        {
            // a pensioner with the minimum on, paid no salary this month: the zero line and the pension's note
            var workspace = new ReviewWorkspace(store);
            var view = workspace.Load(period.Id);
            var mari = view.Persons[0].Person!;
            workspace.SavePerson(mari with { OnOldAgeOrFlexiblePension = true, SocialTaxMinimumApplies = true });
            workspace.SetDeclare(workspace.Load(period.Id).Persons[0].Payments.Single(p => !p.IsByHand), false);
        });
        Ui.Run(() => fixture.Drive(form =>
        {
            form.Tabs.SelectedIndex = 1;
            Ui.Pump();
            form.Declaration.BuildButton.PerformClick();
            List<string> Log() => form.LogList.Items.Cast<string>().ToList();
            Ui.Until(() => !form.IsBusy && Log().Any(l => l.Contains(PeriodDeclarer.MinimumWithPension + ":", StringComparison.Ordinal)), what: "the build's notes in the log");
            var log = Log();
            var pension = log.Single(l => l.Contains(PeriodDeclarer.MinimumWithPension + ":", StringComparison.Ordinal));
            Assert.Contains("MINIMUM_WITH_PENSION: kaardil on märgitud „Vanaduspensionil“ ja „SM miinimumkohustus“", pension, StringComparison.Ordinal);
            var zero = log.Single(l => l.Contains(PeriodDeclarer.ZeroLine + ":", StringComparison.Ordinal));
            Assert.Contains("ZERO_LINE: kuul kehtib töötamine (TÖR liik 1)", zero, StringComparison.Ordinal);
            Assert.Contains(log, l => l.Contains("— " + Core.Report.ReportText.Minimum, StringComparison.Ordinal)); // the zero line's source: miinimum
            Assert.DoesNotContain(log, l => l.Contains("the card says", StringComparison.Ordinal) || l.Contains("a valid employment", StringComparison.Ordinal));
        }));
    }
}
