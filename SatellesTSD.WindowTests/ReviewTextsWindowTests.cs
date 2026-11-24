using System.Reflection;
using SatellesTSD.Core.Lookups;
using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Review;
using SatellesTSD.Core.Store;
using SatellesTSD.Core.Tax;
using SatellesTSD.Infrastructure;
using SatellesTSD.Tests;

namespace SatellesTSD.WindowTests;

/// <summary>The review's texts in Estonian by their codes and figures (the owner's word, decision 46 kept): a payment's
/// refusals in the check cell's tooltip and the simulation, the run's and the review's problems in the persons grid's
/// tooltip, the „Otsi maksed“ log and the simulation, a line gone from the store, the search's own refusals, and the register
/// checks' steps and problems in the log (a register's failure with its own detail in brackets). A code or a text without its
/// figures the window does not know keeps the Core's English.</summary>
public class ReviewTextsWindowTests
{
    private static readonly string[] English =
    [
        " the ", " is ", " not ", " has ", " was ", " were ", " row", " amount", " payment", " person", " store", " month",
        "asked", "reused", "resident,", "member", "kept", "given back", "no answer", "the facts", "accountant",
    ];

    private static void NoEnglish(string? said, string what)
    {
        Assert.False(string.IsNullOrWhiteSpace(said), what);
        Assert.All(English, english => Assert.DoesNotContain(english, said!, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The codes a class names as string constants, upper case alone (a variant's or a TÖR type's constant is no code).</summary>
    private static IEnumerable<string> Codes(Type type) => type.GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()!)
        .Where(c => c.Length > 0 && c.All(ch => char.IsUpper(ch) || ch == '_'));

    [Fact]
    public void Every_refusal_of_the_review_and_the_engine_is_worded_in_Estonian_with_its_figures()
    {
        var rates = TaxRates.Year2026;
        var company = Guid.NewGuid();
        Assert.True(PaymentTypes.TryGet("10", out var salary));
        var nonResidentType = PaymentTypes.All.First(t => t.Payee == Residency.NonResident && t.TreatyRateAllowed && t.IncomeTaxRate is not null);
        var resident = PayeeTaxFacts.ResidentEmployee();
        var nonResident = new PayeeTaxFacts(Residency.NonResident, BasicExemptionApplied: true, ResidenceCountry: "us", TreatyIncomeTaxRate: 0.125m);

        // the refusals as the Core makes them, with the figures it puts beside them
        var made = new List<Refusal>();
        made.AddRange(ReviewChecks.Of(null, null, 100m, false, false, 2026, rates, currency: "usd ", direction: Core.Import.Direction.Credit));
        made.AddRange(ReviewChecks.Of(null, "10", 100m, true, true, 2026, rates, duplicate: true));
        made.AddRange(ReviewChecks.Of(Person.New(company, "Mari", "Kask", "X-1"), "99", 100m, true, true, 2026, rates));
        made.AddRange(ReviewChecks.Of(Person.New(company, "Mari", "Kask", null), null, 100m, true, true, 2026, rates, twoContracts: ["1", "600"]));
        made.AddRange(ReviewChecks.Of(Person.New(company, "Mari", "Kask", "48712219406"), null, 100m, true, true, 2026, rates, foreignA1For: "504"));
        made.Add(ReviewChecks.OutsideTorRefusal(OutsideTorReason.NoRegister));
        made.Add(ReviewChecks.OutsideTorRefusal(OutsideTorReason.NotListed));
        made.AddRange(PaymentChecks.Check(salary!, resident, -1m, rates));
        made.AddRange(PaymentChecks.Check(PaymentTypes.All.First(t => !t.ZeroAllowed && t.Payee == Residency.Resident), resident, 0m, rates));
        made.AddRange(PaymentChecks.Check(salary!, nonResident, 100m, rates));
        made.AddRange(PaymentChecks.Check(nonResidentType, nonResident, 100m, rates));
        made.AddRange(PaymentChecks.Check(salary!, resident with { A1 = A1Case.EstonianA1WorkAbroad }, 100m, rates));
        made.AddRange(PaymentChecks.Check(salary!, resident with { A1 = A1Case.EstonianA1WorkAbroad, A1Country = "FI", WorkCountry = "EE" }, 100m, rates));
        made.AddRange(PaymentChecks.Check(salary!, resident with { A1 = A1Case.ForeignA1WorkInEstonia, A1Country = "EE", WorkCountry = "EE" }, 100m, rates));
        made.AddRange(PaymentChecks.Check(salary!, resident with { FundedPensionMember = null }, 100m, rates));
        made.AddRange(PaymentChecks.Check(salary!, resident with { PensionRate = null }, 100m, rates));
        made.AddRange(PaymentChecks.Check(salary!, resident with { PensionRate = 1.5m }, 100m, rates));
        made.AddRange(PaymentChecks.Check(salary!, resident with { BasicExemptionApplied = false, BasicExemptionMonthly = 9999.5m }, 100m, rates));
        made.AddRange(PaymentChecks.Check(salary!, resident with { BasicExemptionMonthly = 100.005m }, 100m, rates));
        made.AddRange(PaymentChecks.Check(salary!, resident with { SocialTaxMinimumShare = 1.25m }, 100m, rates));
        made.AddRange(PaymentChecks.Check(PaymentTypes.Get("25"), resident, 373.01m, rates)); // the III-pillar contribution: its tax-free part is not computed
        made.AddRange(PaymentChecks.Check(salary!, resident with { StatePaysSocialTax = true }, 100m, rates)); // Töötukassa's application without the status
        made.Add(new Refusal(PaymentChecks.RatesExceedOne, "english")); // the rates of the year never add up to it

        // every code either class refuses with is made above, and worded with no English left in it
        var codes = Codes(typeof(ReviewChecks)).Concat(Codes(typeof(PaymentChecks))).Except([ReviewChecks.TypeTaxesDiffer, ReviewChecks.PayOnFreeWork]).ToList();
        Assert.All(codes, code => Assert.Contains(made, r => r.Code == code));
        Assert.All(made, r =>
        {
            NoEnglish(UiText.RefusalText(r), r.Code);
            Assert.Equal($"{r.Code}: {UiText.RefusalText(r)}", UiText.RefusalLine(r));
        });

        // the figures, as the window shows them
        string Said(string code, Func<Refusal, bool>? which = null) => UiText.RefusalText(made.First(r => r.Code == code && (which is null || which(r))))!;
        Assert.Equal("Summa on valuutas usd, mitte eurodes: deklareeri summa eurodes käsitsi lisatud real („Lisa rida…“).", Said(ReviewChecks.ForeignCurrency));
        Assert.StartsWith("Isiku kood „X-1“ ei ole Eesti isikukood", Said(ReviewChecks.CodeInvalid), StringComparison.Ordinal);
        Assert.Equal("Väljamakse liiki 99 ei ole liikide tabelis.", Said(ReviewChecks.UnknownType));
        Assert.StartsWith("Makse päeval kehtib kaks eri liiki töötamist (TÖR 1, 600): vali veerus „Väljamakse liik“", Said(ReviewChecks.TwoContracts), StringComparison.Ordinal);
        Assert.StartsWith("Registri töötamise liik 504 tähendab, et sotsiaalmaks makstakse välisriigis: märgi isiku kaardil A1 juhuks välisriigi A1 tõend", Said(ReviewChecks.ForeignA1Needed), StringComparison.Ordinal);
        Assert.Equal(UiText.OutsideTorTip(true), Said(ReviewChecks.OutsideTor, r => r.Args![0] == nameof(OutsideTorReason.NoRegister)));
        Assert.Equal(UiText.OutsideTorTip(false), Said(ReviewChecks.OutsideTor, r => r.Args![0] == nameof(OutsideTorReason.NotListed)));
        Assert.Equal("Väljamakse liik 10 on mõeldud residendile, kuid isik on mitteresident: vali teise residentsuse liik või kontrolli isiku residentsust.", Said(PaymentChecks.TypePayeeMismatch));
        Assert.StartsWith("Väljamakse liigil 25 on maksuvaba osa (MVT_640", Said(PaymentChecks.ThirdPillarNotComputed), StringComparison.Ordinal);
        Assert.Contains("võta real märge „Deklareeri“ maha", Said(PaymentChecks.ThirdPillarNotComputed), StringComparison.Ordinal); // what to do with the row here
        Assert.EndsWith("deklareeri makse e-MTA-s käsitsi.", Said(PaymentChecks.ThirdPillarNotComputed), StringComparison.Ordinal);
        Assert.Equal("Eesti A1 tõendi korral peab A1 riik olema EE ja töö riik muu kui EE (KR_YLD2_09, KR_YLD2_07).", Said(PaymentChecks.A1CountriesInconsistent, r => r.Args![0] == nameof(A1Case.EstonianA1WorkAbroad)));
        Assert.Equal("Välisriigi A1 tõendi korral peab A1 riik olema muu kui EE ja töö riik EE (KR_YLD2_07).", Said(PaymentChecks.A1CountriesInconsistent, r => r.Args![0] == nameof(A1Case.ForeignA1WorkInEstonia)));
        Assert.StartsWith("II samba määr 150 % ei sobi", Said(PaymentChecks.PensionRateInvalid), StringComparison.Ordinal);
        Assert.Equal($"Avalduse maksuvaba summa ({UiText.Money(9999.5m)}) peab olema vahemikus 0 kuni {UiText.Money(rates.BasicExemptionMonth)} kuus (seaduse järgi).", Said(PaymentChecks.ExemptionAboveLegal));
        Assert.Equal("Avalduse maksuvaba summa (100,005) peab olema täissentides.", Said(PaymentChecks.ExemptionSubCent));
        Assert.Contains("kaardi maksuresidentsuse riik (US) on väljaspool EMP-d", Said(PaymentChecks.ExemptionNotEea), StringComparison.Ordinal);
        Assert.StartsWith("Kaardi „Maksulepingu TM määr“ (12,5 %) peab olema täisprotsent vahemikus 0 kuni liigi oma määr ", Said(PaymentChecks.TreatyRateInvalid), StringComparison.Ordinal);
        Assert.Equal("SM miinimumkohustuse kuu osa (1,25) peab olema üle 0 ja kuni 1.", Said(PaymentChecks.MinimumShareInvalid));
        Assert.StartsWith("Liigiga 10 peetakse kinni II samba makse", Said(PaymentChecks.PensionNotChecked), StringComparison.Ordinal);
        Assert.StartsWith("„Töötukassa maksab sotsiaalmaksu (taotlus esitatud)“ on märgitud, kuid „Osaline või puuduv töövõime“ ei ole", Said(PaymentChecks.WorkAbilityApplicationWithoutStatus), StringComparison.Ordinal);

        // a code the window does not know, a refusal without its figures: the English
        Assert.Null(UiText.RefusalText(new Refusal("SOMETHING_NEW", "english")));
        Assert.Equal("SOMETHING_NEW: english", UiText.RefusalLine(new Refusal("SOMETHING_NEW", "english")));
        Assert.Null(UiText.RefusalText(new Refusal(ReviewChecks.UnknownType, "Payment type 99 is not in the table.")));
    }

    [Fact]
    public void Every_problem_of_the_run_and_the_review_is_worded_in_Estonian_with_its_figures()
    {
        MatchProblem P(string code, IReadOnlyList<string>? args, string? variant = null) => new(code, "english", Variant: variant, Args: args);
        var said = new (MatchProblem Problem, string Estonian)[]
        {
            (P(MatchProblem.HeaderRow, ["LHV", "7"]), "LHV [7]: andmete seas on päiserida; see jäeti välja."),
            (P(MatchProblem.RowsBelowLastPayee, ["LHV", "8", "9", "2"], MatchProblem.NoPayeeRow), "LHV [read 8–9]: ükski rida ei nimeta saajat, seega jäid kõik 2 rida loetlemata – kontrolli paigutuse saaja veergu."),
            (P(MatchProblem.RowsBelowLastPayee, ["LHV", "8", "8", "1"]), "LHV [rida 8]: viimasest saajaga reast allpool jäi 1 rida loetlemata – see võib olla teenustasu või lõppsaldo; vaata väljavõtet."),
            (P(MatchProblem.DuplicateRow, ["LHV2 [2]", "LHV [2]"]), "LHV2 [2] on sama makse kui LHV [2]; rida on loetletud, kuid arvesse ei lähe."),
            (P(MatchProblem.NoFilters, ["Kaart", "card-x"]), "Kaart: paigutusel „card-x“ ei ole kategooriate filtreid; selle väljavõtte ridadele kategooriat ei määrata."),
            (P(MatchProblem.SeveralEntries, ["Mari Kask", "2", "1, 5"]), "Mari Kask: TÖR loetleb isikut 2 korda (read 1, 5)."),
            (P(MatchProblem.NoEmploymentInMonth, ["Mari Kask", "2026", "10"]), "Mari Kask: ükski TÖR-i töötamine ei kata kuud 10.2026."),
            (P(MatchProblem.NoName, ["4"]), "TÖR-i rida 4: eesnimi või perekonnanimi puudub; isikut ei otsitud."),
            (P(MatchProblem.AmbiguousMask, ["Mari Kask", "KASK", "M*", "Mati Kask", "2", "LHV", "3, 4"]), "Mari Kask: otsingumaskid „KASK M*“ võivad tähendada ka isikut Mati Kask; väljavõttel LHV jäi 2 rida isikuta (read 3, 4)."),
            (P(MatchProblem.NameContained, ["LHV", "3", "Ben Ali Mari", "Ali Mari", "1"]), "LHV [3] leiti isikule Ben Ali Mari ja isikule Ali Mari, kelle nimi sisaldub tema nimes; makse arvestati isikule Ben Ali Mari."),
            (P(MatchProblem.NameContained, ["LHV", "3", "Ben Ali Mari", "Ali Mari, Ben Ali", "2"]), "LHV [3] leiti isikule Ben Ali Mari ja isikutele Ali Mari, Ben Ali, kelle nimed sisalduvad tema nimes; makse arvestati isikule Ben Ali Mari."),
            (P(MatchProblem.SharedRow, ["LHV", "3", "Mari Kask, Mari Kask"]), "LHV [3] leiti mitmele isikule (Mari Kask, Mari Kask); kellelegi seda ei arvestatud – vali real isik."),
            (P(MatchProblem.NotDone, ["Kaart", "2", "Pood", "12.50"]), $"Kaart [2]: Pood {UiText.Money(12.5m)} – tehingu oleku järgi raha ei liikunud (tühistatud, tagasi lükatud, ebaõnnestunud või ootel); rida jäeti välja, see ei ole makse."),
            (P(MatchProblem.BadAmount, ["LHV [5]"]), "LHV [5]: summat ei saanud lugeda; makse ei lähe arvesse."),
            (P(MatchProblem.OtherEmployer, ["4", "0", "1", "12345678 Muu OÜ", "10235688"]), "TÖR-i rida 4: teine tööandja (12345678 Muu OÜ), mitte ettevõte 10235688 – jäeti välja, see ei ole selle ettevõtte isik."),
            (P(MatchProblem.OtherEmployer, ["1, 2, 3, 4, 5, 6, 7, 8, 9, 10", "2", "12", "12345678 Muu OÜ", "10235688"]), "TÖR-i read 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 ja veel 2: teine tööandja (12345678 Muu OÜ), mitte ettevõte 10235688 – jäeti välja, need ei ole selle ettevõtte isikud."),
            (P(MatchProblem.NamesakeOtherCode, ["X-2"]), "Andmebaasis on sama nimega isik teise isikukoodiga (X-2), keda selle kuu TÖR ei loetle. Kui register parandas koodi, on sellel kaardil vaikimisi andmed, mitte teise kaardi valikud (maksuvaba, A1 tõend, Deklareerimine) – võrdle kaht kaarti."),
            (P(MatchProblem.OldReading, ["Kaart"]), "Kaart luges programmi varasem versioon, mis võttis kaardiväljavõtte iga summa laekumiseks: selle väljamaksed jäävad välja. Loe fail uuesti (Failid › Loe uuesti)."),
            (new MatchProblem(MatchProblem.StateUnread, "english", Source: "Kaart", Subject: "2"), "Kaart loeti programmi varasema versiooniga, mis ei lugenud tehingu olekut: rida 2 ei liigutanud raha (tühistatud, tagasi lükatud, ebaõnnestunud või ootel), kuid on loetud makseks. Loe fail uuesti (Failid › Loe uuesti) või võta see rida käsitsi deklaratsioonist välja (märge „Deklareeri“ maha)."),
            (new MatchProblem(MatchProblem.StateUnread, "english", Source: "Kaart", Subject: "2, 5"), "Kaart loeti programmi varasema versiooniga, mis ei lugenud tehingu olekut: read 2, 5 ei liigutanud raha (tühistatud, tagasi lükatud, ebaõnnestunud või ootel), kuid on loetud makseteks. Loe fail uuesti (Failid › Loe uuesti) või võta need read käsitsi deklaratsioonist välja (märge „Deklareeri“ maha)."),
            (P(MatchProblem.DecisionOnCopy, ["LHV2 [2]", "LHV [2]", "draft"], MatchProblem.CopyParts), "LHV2 [2] on rea LHV [2] koopia ja sellelt on makse osi jagatud; osad jäävad oma kohale – jaga originaal ja eemalda koopia osad summaga 0."),
            (P(MatchProblem.DecisionOnCopy, ["LHV2 [2]", "LHV [2]", "pastDraft"], MatchProblem.CopyParts), "LHV2 [2] on rea LHV [2] koopia ja sellelt on makse osi jagatud; osad jäävad oma kohale – kontrolli originaali."),
            (P(MatchProblem.DecisionOnCopy, ["LHV2 [2]", "LHV [2]"], MatchProblem.CopyPastDraft), "LHV2 [2] on rea LHV [2] koopia ja sellel on otsus; kuu ei ole enam mustand, seega jääb otsus oma kohale – kontrolli originaali."),
            (P(MatchProblem.DecisionOnCopy, ["LHV2 [2]", "LHV [2]"], MatchProblem.CopySplit), "LHV2 [2] on rea LHV [2] koopia ja sellel on otsus; sellelt on makse osi jagatud, seega jääb otsus oma kohale – kontrolli originaali."),
            (P(MatchProblem.DecisionOnCopy, ["LHV2 [2]", "LHV [2]"], MatchProblem.CopyOriginalOwn), "LHV2 [2] on rea LHV [2] koopia ja sellel on otsus; originaalil on oma otsus, seega jääb otsus oma kohale – kontrolli originaali."),
            (P(MatchProblem.DecisionMoved, ["«liigutatud koopialt LHV2 [2]»"]), "«liigutatud koopialt LHV2 [2]»: koopial tehtud otsus viidi originaalreale; kontrolli seda ja kustuta siis märge veerust „Märkus“."),
            (P(MatchProblem.IncludedWithoutRow, []), "Kaardil on Deklareerimine „kaasa (nagu kehtiv töötamine)“, kuid kuu TÖR isikut ei loetle ja makset ei leitud: sel kuul ei koostata midagi. Kui töötamine on lõppenud, vali kaardil „reeglite järgi“."),
            (P(MatchProblem.PayeeAmbiguous, ["LHV [5]"]), "LHV [5]: ettevõtte kahel isikul on saaja nimed; vali real õige isik."),
            (P(MatchProblem.PayeeCodeElsewhere, ["LHV [5]"]), "LHV [5]: rea isikukood kuulub ettevõtte teisele isikule; rida kuulub seotud nimede järgi saajale – kontrolli mõlemat kaarti."),
            (P(MatchProblem.PayeeHandLine, ["LHV [5]"]), "LHV [5]: isikul on sel kuul sama summaga käsitsi lisatud rida, seega rida vaikimisi ei deklareerita (märgi „Deklareeri“, kui mõlemad on päris maksed)."),
            (P(MatchProblem.OutsideTorSalary, ["2", "1000.00"]), $"2 palgamustriga makset ({UiText.Money(1000m)} €) isikule, keda TÖR ei loetle, jääb deklareerimata: registreeri töötamine TÖR-is või anna reale kategooria või väljamakse liik."),
        };
        Assert.All(said, pair =>
        {
            Assert.Equal(pair.Estonian, UiText.MatchProblemText(pair.Problem));
            Assert.Equal($"{pair.Problem.Code}: {pair.Estonian}", UiText.ProblemLine(pair.Problem));
            NoEnglish(pair.Estonian, pair.Problem.Code);
        });

        // every code a problem is said under is in the table: a code added later without its Estonian fails here
        Assert.All(Codes(typeof(MatchProblem)), code => Assert.Contains(said, pair => pair.Problem.Code == code));

        // a problem of a run stored before its figures were kept, an unknown code, an unknown case: the English
        Assert.All(said.Where(p => p.Problem.Code is not (MatchProblem.IncludedWithoutRow or MatchProblem.StateUnread)), pair =>
            Assert.Null(UiText.MatchProblemText(pair.Problem with { Args = null })));
        Assert.Null(UiText.MatchProblemText(P("SOMETHING_NEW", ["x"])));
        Assert.Equal("SOMETHING_NEW: english", UiText.ProblemLine(P("SOMETHING_NEW", ["x"])));
        Assert.Null(UiText.MatchProblemText(P(MatchProblem.DecisionOnCopy, ["a", "b"], "elsewhere")));
    }

    [Fact]
    public void Every_step_and_problem_of_a_register_check_is_worded_in_Estonian_and_a_failure_keeps_its_detail()
    {
        var day = new DateOnly(2026, 10, 31);
        var steps = new (LookupNote Note, string Estonian)[]
        {
            (new(LookupNote.Asked, Kind: LookupKind.Residency), "küsiti EMTA-lt residentsust"),
            (new(LookupNote.Asked, Kind: LookupKind.Pension), "küsiti Pensionikeskuselt II sammast"),
            (new(LookupNote.Reused, Kind: LookupKind.Residency, At: new DateTime(2026, 11, 1, 0, 40, 0)), "residentsus: kasutati 01.11.2026 00:40 saadud vastust"),
            (new(LookupNote.Reused, Kind: LookupKind.Pension, At: new DateTime(2026, 11, 1, 0, 40, 0)), "II sammas: kasutati 01.11.2026 00:40 saadud vastust"),
            (new(LookupNote.ResidencyKept), "residentsus on määratud käsitsi ja jääb"),
            (new(LookupNote.ResidencyBack), "residentsus võetakse taas registrist"),
            (new(LookupNote.ResidencySet, Value: true), "residentsus: resident"),
            (new(LookupNote.ResidencySet, Value: false), "residentsus: mitteresident"),
            (new(LookupNote.ExemptionOff), "maksuvaba tulu avalduse märge eemaldati: mitteresident"),
            (new(LookupNote.PensionDerived), "II sammast ei küsitud: mitteresident"),
            (new(LookupNote.PensionBack), "II sammas võetakse taas registrist"),
            (new(LookupNote.PensionKept), "II sammas on määratud käsitsi ja jääb"),
            (new(LookupNote.PensionSet, Value: true, Rate: 0.04m), "II sammas: liitunud, määr 4 %"),
            (new(LookupNote.PensionSet, Value: true), "II sammas: liitunud, määr teadmata"),
            (new(LookupNote.PensionSet, Value: false), "II sammas: ei ole liitunud"),
        };
        Assert.All(steps, pair => Assert.Equal(pair.Estonian, UiText.LookupStep(pair.Note)));

        var problems = new (LookupNote Note, string Estonian)[]
        {
            (new(LookupRunner.Skipped, LookupNote.NoCode), "SKIPPED: kehtiv isikukood puudub, registritest ei ole midagi küsida."),
            (new(LookupRunner.Skipped, LookupNote.RegisterNoCode, Name: "Mari Kask"), "SKIPPED: Mari Kask: isikukood puudub nii TÖR-is kui ka kaardil, registritest ei ole midagi küsida."),
            (new(LookupRunner.Skipped, LookupNote.PaidNoCode, Name: "Mari Kask"), "SKIPPED: Mari Kask: isikule on sel kuul makstud, kuid isikukood puudub, registritest ei ole midagi küsida – määra residentsus kaardil."),
            (new(LookupRunner.Failed, LookupNote.FactsStand, LookupKind.Residency, Failure: "request timeout or access denied: HTTP 503"),
                "FAILED: residentsuse päring ebaõnnestus – päring aegus või ligipääs keelati (HTTP 503); andmed jäävad, nagu olid."),
            (new(LookupRunner.Failed, LookupNote.FactsStand, LookupKind.Residency), "FAILED: residentsuse päring ebaõnnestus – vastust ei saadud; andmed jäävad, nagu olid."),
            (new(LookupRunner.Failed, LookupNote.HandUsed, LookupKind.Residency, Value: false, Failure: "content error: the period table is missing"),
                "FAILED: residentsuse päring ebaõnnestus – registri lehe vastust ei tuntud ära (the period table is missing); kasutatakse käsitsi määratud residentsust (mitteresident)."),
            (new(LookupRunner.Failed, LookupNote.FactsStand, LookupKind.Pension, Failure: "content error: no known answer on the page"),
                "FAILED: II samba päring ebaõnnestus – registri lehe vastust ei tuntud ära (no known answer on the page); andmed jäävad, nagu olid."),
            (new(LookupRunner.Failed, LookupNote.MembershipUnknown, LookupKind.Pension, Day: day, Ended: new DateOnly(2026, 12, 31)),
                "FAILED: II samba päring ebaõnnestus – isik on liitunud, kuid leht ei nimeta kinnipidamise algust, ainult et sissemaksed lõpevad 31.12.2026 ehk pärast 31.10.2026: ei ole teada, kas isik oli 31.10.2026 liitunud; andmed jäävad, nagu olid."),
            (new(LookupRunner.Disagrees, Kind: LookupKind.Residency, Value: false, Day: day, ByHand: true),
                "DISAGREES: registri järgi on isik 31.10.2026 seisuga mitteresident, käsitsi on määratud resident; käsitsi määratu jääb."),
            (new(LookupRunner.Disagrees, Kind: LookupKind.Pension, Value: true, Rate: 0.04m, Day: day, Derived: false, ByHand: true, ByHandRate: 0.06m),
                "DISAGREES: Pensionikeskuse järgi 31.10.2026 seisuga liitunud, määr 4 %, käsitsi on määratud: liitunud, määr 6 %; käsitsi määratu jääb."),
            (new(LookupRunner.Disagrees, Kind: LookupKind.Pension, Value: false, Day: day, Derived: true, ByHand: true, ByHandRate: 0.02m),
                "DISAGREES: mitteresidendina ei ole liitunud (Pensionikeskuselt ei küsitud), käsitsi on määratud: liitunud, määr 2 %; käsitsi määratu jääb."),
            (new(LookupRunner.RateUnknown, Day: day), "RATE_UNKNOWN: isik on 31.10.2026 seisuga liitunud, kuid ükski määra rida ei kata seda päeva; määr jääb teadmata (vaikimisi 2 % ei võeta)."),
            (new(LookupRunner.Reset), "RESET: II samba andmed tulenesid mitteresidentsusest; nüüd on isik resident ja andmed on teadmata, kuni Pensionikeskus vastab."),
            (new(LookupNote.NoRun), "Perioodi makseid ei ole otsitud („Otsi maksed“): kontrollitakse kõiki ettevõtte isikuid."),
        };
        Assert.All(problems, pair => Assert.Equal(pair.Estonian, UiText.LookupProblem(pair.Note)));

        // every step's and problem's code is in the tables
        Assert.All(Codes(typeof(LookupNote)), code => Assert.Contains(steps.Select(s => s.Note).Concat(problems.Select(p => p.Note)), n => n.Code == code));
        Assert.All(Codes(typeof(LookupRunner)), code => Assert.Contains(problems, p => p.Note.Code == code));

        // the failures by their prefixes, the detail kept as the site gave it; a failure without a known prefix keeps the line English
        Assert.Equal("päring aegus või ligipääs keelati (the site did not answer in time)", UiText.LookupFailure("request timeout or access denied: the site did not answer in time"));
        Assert.Equal("registri lehe vastust ei tuntud ära ('x' is not a date)", UiText.LookupFailure("content error: 'x' is not a date"));
        Assert.Equal("vastust ei saadud", UiText.LookupFailure("no answer"));
        // the parsers' own two failures (a page under maintenance, a table of another shape) are worded too
        Assert.Equal("registri leht ei andnud andmeid (leht võib olla hoolduses)", UiText.LookupFailure(SatellesTSD.Infrastructure.Lookups.EmtaResidencyParser.NoContent));
        Assert.Equal("registri leht ei andnud andmeid (leht võib olla hoolduses)", UiText.LookupFailure(SatellesTSD.Infrastructure.Lookups.PensionikeskusParser.NoContent));
        Assert.Equal("registri lehe tabel oli ootamatu kujuga", UiText.LookupFailure("table size mismatch"));
        Assert.Null(UiText.LookupFailure("something else broke"));
        var odd = new LookupNote(LookupRunner.Failed, LookupNote.FactsStand, LookupKind.Residency, Failure: "something else broke");
        Assert.Null(UiText.LookupProblem(odd));
        Assert.Equal("FAILED: residency — something else broke; the facts stand as they were.", UiText.LookupProblemLine("FAILED: residency — something else broke; the facts stand as they were.", odd));
        Assert.Null(UiText.LookupStep(new LookupNote("SOMETHING_NEW")));
        Assert.Null(UiText.LookupProblem(new LookupNote("SOMETHING_NEW")));

        // the person's line: each step by its note; an outcome without notes (made elsewhere) keeps its English
        var outcome = new PersonLookupOutcome(Guid.NewGuid(), "x", "Mari Kask", ["asked residency", "residency resident"], ["RATE_UNKNOWN: english"])
        {
            ControlDate = day,
            StepNotes = [steps[0].Note, steps[6].Note],
            ProblemNotes = [new LookupNote(LookupRunner.RateUnknown, Day: day)],
        };
        Assert.Equal("Mari Kask (seisuga 31.10.2026): küsiti EMTA-lt residentsust; residentsus: resident", UiText.LookupLine(outcome));
        Assert.Equal([problems.Single(p => p.Note.Code == LookupRunner.RateUnknown).Estonian], UiText.LookupProblemLines(outcome));
        Assert.Equal("Mari Kask (seisuga 31.10.2026): asked residency; residency resident", UiText.LookupLine(outcome with { StepNotes = [] }));
        Assert.Equal(["RATE_UNKNOWN: english"], UiText.LookupProblemLines(outcome with { ProblemNotes = [] }));
    }

    [Fact]
    public void A_line_gone_from_the_store_and_the_searchs_own_refusals_are_said_in_Estonian()
    {
        Assert.Equal("Seda käsitsi lisatud rida ei ole enam andmebaasis – vaade laaditakse uuesti.",
            UiText.Worded(new ReviewException(ReviewWorkspace.LineMissing, "english", variant: ReviewException.HandLine)));
        Assert.Equal("Seda osa ei ole enam andmebaasis – vaade laaditakse uuesti.", UiText.Worded(new ReviewException(ReviewWorkspace.LineMissing, "english", variant: ReviewException.Part)));
        Assert.Equal("Seda rida ei ole enam andmebaasis – vaade laaditakse uuesti.", UiText.Worded(new ReviewException(ReviewWorkspace.LineMissing, "english")));
        Assert.Null(UiText.Worded(new ReviewException("SOMETHING_NEW", "english")));

        // the search's refusals, as the Core throws them
        using var store = SqliteStore.InMemory();
        var company = store.AddCompany(WindowFixture.RegistryCode, "Näidis OÜ");
        var period = store.GetOrCreatePeriod(company.Id, 2026, 10);
        var none = Assert.Throws<MatchException>(() => new MatchRunner(store).Run(period.Id));
        Assert.Equal("Kuuga ei ole seotud ühtegi faili: impordi enne TÖR väljavõte ja pangaväljavõtted.", UiText.Worded(none));
        foreach (var mariSecond in new[] { false, true })
        {
            var bytes = ReviewFixtures.Workbook(mariSecond: mariSecond);
            store.LinkImport(period.Id, store.AddImport(company.Id, ReviewFixtures.Importer.Read($"palgad{(mariSecond ? 2 : 1)}.xlsx", bytes), bytes).Id);
        }

        var two = Assert.Throws<MatchException>(() => new MatchRunner(store).Run(period.Id));
        Assert.Equal("Kuu failides on 2 TÖR-i lehte, otsing kasutab ainult ühte: eemalda teised Failid-aknas kuu failide hulgast.", UiText.Worded(two));
        Assert.Null(UiText.RunRefused(new MatchException("english")));
    }

    [Fact]
    public void The_check_cell_the_persons_tooltip_and_the_search_log_say_the_review_in_Estonian()
    {
        using var fixture = new WindowFixture(mariSecond: true, outsider: "Kalle Kask", prepare: (store, company, period, _) =>
        {
            var workspace = new ReviewWorkspace(store);
            var kalle = workspace.AddPerson(company.Id, "Kalle", "Kask", null, null);
            workspace.SetPerson(workspace.Load(period.Id).Unmatched.Payments.Single(p => p.Payee == "Kalle Kask"), kalle.Id);
        });
        Ui.Run(() => fixture.Drive(form =>
        {
            // the persons grid: the code in the cell, the Estonian in the tooltip
            var persons = form.PersonsGrid;
            var mari = persons.Rows.Cast<DataGridViewRow>().First(r => r.Tag is ReviewPerson { Person.LastName: "MAASIKAS" or "Maasikas" });
            var mariTip = form.ProblemTips.TextOf(mari)!;
            Assert.Contains($"{MatchProblem.SeveralEntries}: ", mariTip, StringComparison.Ordinal);
            Assert.Contains(": TÖR loetleb isikut 2 korda (read ", mariTip, StringComparison.Ordinal);
            Assert.DoesNotContain("the register lists", mariTip, StringComparison.Ordinal);
            Assert.Contains(MatchProblem.SeveralEntries, mari.Cells["problems"].Value?.ToString(), StringComparison.Ordinal);
            var kalle = persons.Rows.Cast<DataGridViewRow>().Single(r => r.Tag is ReviewPerson { Person.LastName: "Kask" });
            Assert.Contains($"{MatchProblem.OutsideTorSalary}: 1 palgamustriga makse ({UiText.Money(500m)} €) isikule, keda TÖR ei loetle, jääb deklareerimata", form.ProblemTips.TextOf(kalle), StringComparison.Ordinal);

            // the check cell: Kalle has no residency checked; the refusals in Estonian, each after its code
            persons.CurrentCell = kalle.Cells[0];
            Ui.Pump();
            var row = Assert.Single(form.PaymentsGrid.Rows.Cast<DataGridViewRow>());
            var tip = form.CheckTips.TextOf(row)!;
            Assert.Contains(UiText.OutsideTorTip(false), tip, StringComparison.Ordinal); // not declared: the tip alone, without a refusal

            // declared by the accountant: the refusals, each after its code
            form.Services!.Workspace.SetDeclare((ReviewPayment)row.Tag!, true);
            form.Reload();
            Ui.Pump();
            persons.CurrentCell = persons.Rows.Cast<DataGridViewRow>().Single(r => r.Tag is ReviewPerson { Person.LastName: "Kask" }).Cells[0];
            Ui.Pump();
            row = Assert.Single(form.PaymentsGrid.Rows.Cast<DataGridViewRow>());
            tip = form.CheckTips.TextOf(row)!;
            Assert.Contains($"{ReviewChecks.OutsideTor}: {UiText.OutsideTorTip(false)}", tip, StringComparison.Ordinal);
            Assert.Contains($"{ReviewChecks.ResidencyNotChecked}: Isiku residentsust ei ole kontrollitud – ei registrist ega käsitsi („Kontrolli registrid“ või kaardil „kinnita käsitsi“).", tip, StringComparison.Ordinal);
            Assert.DoesNotContain("has not been checked", tip, StringComparison.Ordinal);
            Assert.DoesNotContain(":", row.Cells["check"].ToolTipText, StringComparison.Ordinal); // the grid's own tooltip, which cuts a text, has the codes alone (as the persons' problems)

            // „Otsi maksed“: the run's problems in the log, the code verbatim, the Estonian after it
            var before = form.LogList.Items.Count;
            form.MatchButton.PerformClick();
            Ui.Until(() => !form.IsBusy && form.LogList.Items.Count > before + 1, what: "the search");
            Ui.Pump();
            var lines = form.LogList.Items.Cast<object>().Select(i => i.ToString()!).Skip(before).ToList();
            Assert.Contains(lines, l => l.Contains($"   {MatchProblem.SeveralEntries}: ", StringComparison.Ordinal) && l.Contains(": TÖR loetleb isikut 2 korda (read ", StringComparison.Ordinal));
            Assert.DoesNotContain(lines, l => l.Contains("the register lists", StringComparison.Ordinal) || l.Contains("no employment of the register", StringComparison.Ordinal));
            Assert.Empty(fixture.Failures);
        }));
    }

    [Fact]
    public void A_line_gone_from_the_store_and_an_amount_on_a_month_past_Draft_are_refused_in_their_own_words()
    {
        using var fixture = new WindowFixture((store, _, period, _) =>
        {
            var workspace = new ReviewWorkspace(store);
            workspace.AddHandLine(period.Id, workspace.Load(period.Id).Persons[0].PersonId, new DateOnly(2026, 10, 20), 120m, "Mari Maasikas", "haigusleht", "Haigusleht");
        });
        Ui.Run(() => fixture.Drive(form =>
        {
            form.PersonsGrid.CurrentCell = form.PersonsGrid.Rows[0].Cells[0];
            Ui.Pump();
            var grid = form.PaymentsGrid;
            var hand = grid.Rows.Cast<DataGridViewRow>().Single(r => r.Tag is ReviewPayment { IsByHand: true });
            form.Services!.Store.DeleteReviewLine(((ReviewPayment)hand.Tag!).LineId!.Value); // gone behind the window's back
            var before = form.LogList.Items.Count;
            grid.CurrentCell = hand.Cells["note"];
            Assert.True(grid.BeginEdit(true));
            Assert.IsAssignableFrom<TextBox>(grid.EditingControl).Text = "kokkulepe";
            Assert.True(grid.EndEdit());
            Ui.Until(() => fixture.Failures.Count == 1, what: "the refusal of the edit");
            Ui.Pump();
            Assert.Equal(ReviewWorkspace.LineMissing, Assert.IsType<ReviewException>(fixture.Failures[0]).Code);
            var said = form.LogList.Items.Cast<object>().Select(i => i.ToString()!).Skip(before).Single(l => l.Contains(UiText.Problem + ":", StringComparison.Ordinal));
            Assert.EndsWith($"{UiText.Problem}: Seda käsitsi lisatud rida ei ole enam andmebaasis – vaade laaditakse uuesti. (The line added by hand is no longer in the store.)", said, StringComparison.Ordinal);
            fixture.Failures.Clear();

            // the month went past Draft behind the window's back: the amount's own words, not the card's
            form.Services!.Store.SetPeriodState(form.View!.Period.Id, PeriodState.Exported, DateTime.UtcNow);
            var row = grid.Rows.Cast<DataGridViewRow>().First(r => r.Tag is ReviewPayment { IsByHand: false });
            before = form.LogList.Items.Count;
            grid.CurrentCell = row.Cells["document"];
            grid.CurrentCell = row.Cells["amount"];
            Assert.True(grid.BeginEdit(true));
            Assert.IsAssignableFrom<TextBox>(grid.EditingControl).Text = "800";
            Assert.True(grid.EndEdit());
            Ui.Until(() => fixture.Failures.Count == 1, what: "the refusal of the amount");
            Ui.Pump();
            Assert.Equal("Kuu ei ole enam mustand – summat muuta ei saa.", fixture.Failures[0].Message);
            var logged = form.LogList.Items.Cast<object>().Select(i => i.ToString()!).Skip(before).Single(l => l.Contains(UiText.Problem + ":", StringComparison.Ordinal));
            Assert.Contains($"{UiText.Problem}: Kuu ei ole enam mustand – summat muuta ei saa. (", logged, StringComparison.Ordinal);
            Assert.DoesNotContain("seda ei saa muuta", logged, StringComparison.Ordinal);
            fixture.Failures.Clear();
        }));
    }

    [Fact]
    public void A_register_check_is_logged_in_Estonian_and_a_failure_keeps_the_sites_own_detail()
    {
        using var fixture = new WindowFixture()
        {
            Services = path => new AppServices(path, new RefusingResidency(), new JoinedPension(), new LookupOptions(TimeSpan.Zero)),
        };
        Ui.Run(() => fixture.Drive(form =>
        {
            var before = form.LogList.Items.Count;
            form.LookupButton.PerformClick();
            Ui.Until(() => !form.IsBusy && form.LogList.Items.Cast<object>().Skip(before).Any(i => i.ToString()!.Contains($"{UiText.RunLookups}: {UiText.Done}", StringComparison.Ordinal)), what: "the run");
            var lines = form.LogList.Items.Cast<object>().Select(i => i.ToString()!).Skip(before).ToList();
            Assert.Contains(lines, l => l.Contains("(seisuga ", StringComparison.Ordinal) && l.Contains("küsiti EMTA-lt residentsust", StringComparison.Ordinal));
            // Mari's residency set by hand: her own word is used; the detail of the site's failure stays as it came
            Assert.Contains(lines, l => l.EndsWith("   FAILED: residentsuse päring ebaõnnestus – päring aegus või ligipääs keelati (HTTP 503); kasutatakse käsitsi määratud residentsust (resident).", StringComparison.Ordinal));
            Assert.DoesNotContain(lines, l => l.Contains("asked residency", StringComparison.Ordinal) || l.Contains("the facts stand", StringComparison.Ordinal) || l.Contains("accountant", StringComparison.Ordinal));
            Assert.Empty(fixture.Failures);
        }));
    }

    [Fact]
    public void The_simulation_says_the_refusals_and_the_searchs_problems_in_Estonian()
    {
        var scenario = new SimScenario(2026, 10,
            new SimPerson("Mari", "Maasikas", ReviewFixtures.MariCode, FundedPensionMember: false, PensionRate: null),
            SimRegister.Listed,
            [new SimEmployment("1", new DateOnly(2024, 2, 15))],
            [new SimRow(new DateOnly(2026, 10, 5), "Mari Maasikas", 905.92m, "palk", Currency: "USD")]);
        var report = UiText.SimReport(Simulator.Run(scenario));
        Assert.Contains($"{ReviewChecks.ForeignCurrency}: Summa on valuutas USD, mitte eurodes", report, StringComparison.Ordinal);
        Assert.DoesNotContain("not in euros", report, StringComparison.Ordinal);

        var said = UiText.SimReport(new SimResult { RunProblems = [new MatchProblem(MatchProblem.HeaderRow, "LHV [7]: a header row inside the data, left out.", Args: ["LHV", "7"])] });
        Assert.Contains("Sammud:" + Environment.NewLine + "  HEADER_ROW: LHV [7]: andmete seas on päiserida; see jäeti välja.", said, StringComparison.Ordinal);
        Assert.DoesNotContain("left out", said, StringComparison.Ordinal);
    }

    private sealed class RefusingResidency : IResidencyLookup
    {
        public Task<LookupResult<ResidencyFinding>> QueryAsync(string personalCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(LookupResult<ResidencyFinding>.Failed("request timeout or access denied: HTTP 503"));
    }

    private sealed class JoinedPension : IPensionLookup
    {
        public Task<LookupResult<PensionFinding>> QueryAsync(string personalCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(LookupResult<PensionFinding>.Found(new PensionFinding(PensionStatus.Joined, new DateOnly(2019, 1, 1), null, [new RatePeriod(new DateOnly(2019, 1, 1), null, 0.02m, "x")]), null));
    }
}
