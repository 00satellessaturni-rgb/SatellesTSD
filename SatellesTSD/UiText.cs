using SatellesTSD.Core.Lookups;
using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Review;
using SatellesTSD.Core.Store;
using SatellesTSD.Core.Tax;

namespace SatellesTSD;

/// <summary>Every word the window says, in one place: Estonian, as e-MTA and the workbook's reports speak to the
/// accountant (plan question 5). A later Russian or English switch replaces this class, nothing else.</summary>
internal static class UiText
{
    public const string Title = "SatellesTSD";

    // the toolbar
    public const string Store = "Andmebaas:";
    public const string NoStore = "(andmebaas avamata)";
    public const string OpenStore = "Ava andmebaas…";
    public const string NewStore = "Uus andmebaas…";
    public const string Company = "Ettevõte:";
    public const string NewCompany = "Uus ettevõte…";
    public const string Period = "Periood:";
    public const string ImportFiles = "Impordi failid…";
    public const string Files = "Failid…";
    public const string RunMatch = "Otsi maksed";
    public const string RunLookups = "Kontrolli registrid";
    public const string Cancel = "Katkesta";
    public const string AddLine = "Lisa rida…";
    public const string RemoveLine = "Eemalda rida";
    public const string NewPerson = "Uus isik…";

    // the persons grid (after the Vahearuanne's headers)
    public const string PersonalCode = "Isikukood";
    public const string LastName = "Perekonnanimi";
    public const string FirstName = "Eesnimi";
    public const string Exemption = "Maksuvaba";
    public const string SalaryTotal = "PALK NETO KOKKU";

    /// <summary>The line under the persons grid: the column's total, and the total of the rows shown when the search hides some.</summary>
    public static string SalaryTotalLine(decimal total, decimal? shown) =>
        shown is { } s ? $"{SalaryTotal}: {Money(total)} (näidatud read: {Money(s)})" : $"{SalaryTotal}: {Money(total)}";
    public const string DeclarationNetTotal = "NETO KOKKU";
    public const string TaxesTotal = "MAKSUD KOKKU";

    /// <summary>The line under the declaration's lines: the net and the taxes to pay with their parts, and the lines the
    /// search leaves shown when they add up to something else.</summary>
    public static string DeclarationTotalsLine(Core.Declare.DeclarationTotals all, Core.Declare.DeclarationTotals? shown)
    {
        var line = $"{DeclarationNetTotal}: {Money(all.Net)}    {TaxesTotal}: {Money(all.Taxes)}"; // short, one line at any width; the parts in the tooltip
        return shown is { } s ? $"{line}    (näidatud read: neto {Money(s.Net)}, maksud {Money(s.Taxes)})" : line;
    }

    /// <summary>The taxes' parts, and what the totals count, said over the line.</summary>
    public static string DeclarationTotalsTip(Core.Declare.DeclarationTotals all) =>
        $"{TaxesTotal}: {Money(all.Taxes)} = {IncomeTax} {Money(all.IncomeTax)} + {SocialTax} {Money(all.SocialTax)} + {UnemploymentEmployee} {Money(all.UnemploymentEmployee)} + {UnemploymentEmployer} {Money(all.UnemploymentEmployer)} + {FundedPension} {Money(all.FundedPension)}" + Environment.NewLine +
        $"Arvestatud {all.Lines} rida: mustandid, saadetud ja serveris olevad read." + Environment.NewLine +
        "Ei arvestata: MTA tagasi lükatud ja tühistatud read ning T-read (need võtavad rea tagasi)." + Environment.NewLine +
        "Maksud: TM, SM (koos miinimumi suurendusega), TK, TTK ja KP — tööandja enda maksud ja kinnipeetud maksud kokku.";

    public const string Residency = "Residentsus";
    public const string Pension = "Pension";
    public const string CheckedOn = "Kontrollitud";
    public const string EmploymentType = "Töötamise liik";
    public const string EntryState = "Kande olek";
    public const string EmploymentStart = Core.Report.ReportText.EmploymentStart;
    public const string EmploymentEnd = "Töötamise lõpu aeg";
    public const string PauseStart = "Peatamise algus";
    public const string PauseEnd = "Peatamise lõpu aeg";
    public const string Undeclared = "Deklareerimata";
    public const string Problems = "Probleemid";
    public const string UnmatchedRow = "— leidmata read —";

    // the payments grid (the Otsingu kontroll's headers)
    public const string Document = "Dokument";
    public const string Date = "Kuupäev";
    public const string PayeeOnStatement = "Nimi väljavõttel";
    public const string Description = "Selgitus";
    public const string Amount = "Summa";
    public const string DebitCredit = "D/C";
    public const string Currency = "Valuuta";

    /// <summary>The payments grid's direction: "D" money out, "C" money in, as the statement's D/C column or the
    /// amount's sign said it — one shape whatever the bank's (Д/К, a signed amount); nothing when the statement says neither.</summary>
    public static string DebitCreditText(Core.Import.Direction direction, bool assumed = false) => (direction switch
    {
        Core.Import.Direction.Debit => "D",
        Core.Import.Direction.Credit => "C",
        _ => "",
    }) + (assumed && direction != Core.Import.Direction.Unknown ? "?" : "");

    public const string DirectionAssumedTip = "Väljavõte ei ütle deebetit ega kreeditit: iga EUR-rida loetakse väljamakseks (nagu makroga töövihik).";
    public const string Verdict = "Kontroll/Kategooria";
    public const string Category = "Kategooria";
    public const string PaymentType = "Väljamakse liik";
    public const string Declare = "Deklareeri";
    public const string Person = "Isik";
    public const string Note = "Märkus";
    public const string Check = "Kontroll";
    public const string ByHand = "käsitsi";
    public const string NoPerson = "(isik puudub)";
    public const string Default = "(vaikimisi)";
    public const string NoCategory = "(puudub)";
    public const string Nobody = "(keegi)";

    // the person's facts as the workbook wrote them (one set of words with the report workbook)
    public const string Resident = Core.Report.ReportText.Resident;
    public const string NonResident = Core.Report.ReportText.NonResident;
    public const string NotChecked = Core.Report.ReportText.NotChecked;
    public const string PensionMember = Core.Report.ReportText.PensionMember;
    public const string PensionNotMember = Core.Report.ReportText.PensionNotMember;
    public const string Unknown = Core.Report.ReportText.Unknown;
    public const string Yes = "jah";
    public const string No = "ei";

    // the person panel
    public const string PersonPanel = "Isik";
    public const string MrrCode = "MTA mitteresidendi kood";

    public static string MrrPrefixQuestion(string code, string prefix) =>
        $"MTA mitteresidendi kood „{code}“ algab riigi tähisega {prefix}. MTA võtab koodi ilma riigi tähiseta. Kas salvestada kood nii, nagu see on?";

    public static string CompanyCodeAsMrr(string code) =>
        $"Kood „{code}“ ei ole 8-kohaline äriregistri kood ega 11-kohaline isikukood. Fail nimetab selle MTA mitteresidendi koodiks (MRR_KOOD). Kas see on õige?";

    public const string CompanyCodeSigns = "Koodis võivad olla ainult ladina tähed A–Z ja numbrid 0–9, ilma tühikute ja muude märkideta.";

    public static string CompanyCodeShape(string label) => $"Kood ei vasta valitud liigile ({label}): äriregistri kood on 8 numbrit, isikukood 11 numbrit.";
    public const string ResidenceCountry = "Maksuresidentsuse riik";
    public const string ConfirmByHand = "kinnita käsitsi";
    public const string PensionMemberLabel = "II sammas";
    public const string PensionRate = "Määr";
    public const string OnPension = "Vanaduspensionil";
    public const string PensionAgeYear = "Pensioniea aasta";
    public const string ExemptionApplied = "Maksuvaba tulu avaldus";
    public const string ExemptionMonthly = "Maksuvaba summa kuus";
    public const string ExemptionFull = "(tühi = seaduse summa)";
    public const string SocialTaxMinimum = "SM miinimumkohustus";

    // the person's partial or no work ability and the employer's application under it (SMS § 2 lg 4 p 6, § 6 lg 1 p 5, lg 3)
    public const string ReducedWorkAbility = "Osaline või puuduv töövõime";
    public const string StatePaysSocialTax = "Töötukassa maksab sotsiaalmaksu (taotlus esitatud)";

    // said under the application's tick, not in a tip alone: what the deduction is, and what MTA checks that the app cannot (KR_SM_03)
    public const string StatePaysSocialTaxNote = "Töötukassa maksab sotsiaalmaksu kuni ühe kuumäära ulatuses kuus töötasult (SMS § 6 lg 3); tööandja maksab ülejäänult. "
        + "MTA võtab vähenduse vastu ainult siis, kui Töötukassa on isiku sama või eelmise kuu deklaratsioonis deklareerinud (KR_SM_03) – programm seda kontrollida ei saa.";

    // the minimum box's tip: a state pension's recipient and a person with partial or no work ability owe no minimum; the
    // "Vanaduspensionil" tick and the work-ability tick take it off (decision 237 and the work-ability item)
    public const string MinimumPensionTip = "Riikliku pensioni saaja (vanadus-, töövõimetus-, toitjakaotuspension jm) ning osalise või puuduva töövõimega isiku eest ei ole tööandjal sotsiaalmaksu miinimumkohustust (SMS § 2 lg 4 p 6). "
        + $"Märke „{OnPension}“ või „{ReducedWorkAbility}“ lisamine võtab miinimumi maha (töövõime märke korral ei arvutata miinimumi ka siis, kui see märge jääb); teiste pensionisaajate puhul eemalda märge käsitsi.";
    public const string DeclarationLabel = "Deklareerimine";
    public const string DeclarationAuto = "reeglite järgi";
    public const string DeclarationInclude = "kaasa (nagu kehtiv töötamine)";
    public const string DeclarationExclude = "välja (ei deklareerita)";

    /// <summary>The persons grid's word for the card's choice: nothing when by the rules, so the exceptions stand out.</summary>
    public static string DeclarationShort(Core.Store.DeclarationChoice choice) => choice switch
    {
        Core.Store.DeclarationChoice.Include => "kaasa",
        Core.Store.DeclarationChoice.Exclude => "välja",
        _ => "",
    };

    /// <summary>The "Peatamise algus" cell's tooltip: why and until when the employment is paused, as the register says.</summary>
    public static string PauseTip(string? reason, DateOnly? end) => string.Join(Environment.NewLine,
        new[] { reason is null ? null : "Peatamise alus: " + reason, end is null ? null : "Peatamise lõpu aeg: " + Day(end) }.Where(t => t is not null));

    /// <summary>The "Deklareerimata" cell's tooltip: each payment the month does not declare, and why.</summary>
    public static string? UndeclaredTip(IReadOnlyList<Core.Review.Undeclared> undeclared) => undeclared.Count == 0 ? null :
        "Deklareerimata tulu:" + Environment.NewLine + string.Join(Environment.NewLine, undeclared.Select(u =>
            $"{(u.Payment.Date is { } day ? Day(day) : u.Payment.DateText)} {u.Payment.Payee}  {AmountOf(u.Payment)} — {UndeclaredReason(u)}"));

    /// <summary>The amount as the statement gave it: its text when none was read, the currency beside a foreign one.</summary>
    private static string AmountOf(Core.Review.ReviewPayment p) =>
        p.AmountText is { } text ? (text.Length > 0 ? text : "?") : Money(p.Amount) + (Core.Review.ReviewChecks.IsEuro(p.Currency) ? "" : " " + p.Currency);

    public static string UndeclaredReason(Core.Review.Undeclared u) => u.Reason switch
    {
        Core.Review.Undeclared.Excluded => "isik on deklaratsioonist välja jäetud",
        Core.Review.Undeclared.Unticked => "märge \"Deklareeri\" on maha võetud",
        Core.Review.Undeclared.Refused => "rida ei saa koostada: " + string.Join(", ", u.Codes),
        Core.Review.Undeclared.Held => "liigi kokkuvõte ootab sama liigi keeldutud makset",
        // by default not declared, with no refusal to name (a rent row a line added by hand already declares, say)
        _ when u.Codes.Count > 0 && u.Codes[0] == MatchProblem.PayeeHandLine =>
            "isikul on sel kuul sama summaga käsitsi lisatud rida, mis makse juba deklareerib (märgi „Deklareeri“ ainult siis, kui mõlemad on päris maksed)"
            + (u.Codes.Count == 1 ? "" : "; lisaks: " + string.Join(", ", u.Codes.Skip(1))),
        _ when u.Codes.Count == 0 => "ei lähe vaikimisi deklaratsiooni",
        _ => "ei lähe deklaratsiooni: " + string.Join(", ", u.Codes),
    };

    public const string A1 = "A1 tõend";
    public const string A1None = "puudub";
    public const string A1Estonian = "Eesti A1, töö välismaal";
    public const string A1Foreign = "välisriigi A1, töö Eestis";
    public const string A1Country = "A1 riik";
    public const string WorkCountry = "Töö riik";
    public const string A1CountriesHint = "Eesti A1: A1 riik EE, töö riik välismaal. Välisriigi A1: A1 riik välisriik, töö riik EE.";
    public const string TreatyRate = "Maksulepingu TM määr";
    public const string TorType = "TÖR töötamise liik";
    public const string TypeFromCard = "Kaardi töötamise liik kõigile maksetele (registri asemel)";
    public const string Notes = "Märkused";
    public const string Save = "Salvesta";
    public const string Revert = "Tühista";
    public const string LookUp = "Kontrolli registrist";
    public const string SourceOf = "allikas";
    public const string NotANumber = "Summa, määr või aasta ei ole arv.";

    // the dialogs
    public const string RegistryCode = "Registrikood";
    public const string Name = "Nimi";
    public const string Ok = "OK";
    public const string Payee = "Saaja";
    public const string AddLineTitle = "Lisa makse käsitsi";
    public const string NewPersonTitle = "Uus isik";
    public const string NewCompanyTitle = "Uus ettevõte";
    public const string EditCompany = "Muuda ettevõtet…";
    public const string EditCompanyTitle = "Ettevõte";
    public const string PayerCodeType = "Koodi liik failis";
    public const string PayerCodeAuto = "koodi kuju järgi (8 numbrit = äriregister, 11 = isikukood, muu = MRR)";
    public const string PayerCodeRegistry = "ARIREGISTRIKOOD — Äriregistri kood";
    public const string PayerCodePersonal = "ISIKUKOOD — FIE isikukood";
    public const string PayerCodeMrr = "MRR_KOOD — MTA mitteresidendi registri kood";
    public static string CompanySaved(string name, string code, string type) => $"{EditCompanyTitle}: {name} ({code}), koodi liik failis {type}.";
    public const string FilesTitle = "Perioodi failid";
    public const string Unlink = "Eemalda perioodist";
    public const string ImportedAt = "Imporditud";
    public const string Kind = "Liik";
    public const string FileFilter = "Pangaväljavõtted ja TÖR (*.csv;*.xlsx;*.xls)|*.csv;*.xlsx;*.xls|Kõik failid (*.*)|*.*";
    public const string StoreFilter = "SatellesTSD andmebaas (*.db)|*.db|Kõik failid (*.*)|*.*";

    // messages
    public const string RunReplacesRun = "Perioodi otsingu tulemus asendatakse uuega; tehtud otsused jäävad alles. Jätkata?";
    public const string UnlinkConfirm = "Eemaldada fail perioodi failide hulgast? Selle faili ridade otsused kustuvad.";
    public const string UnlinkClearsRun = "Perioodi otsingu tulemus kasutab seda faili. Otsingu tulemus kustutatakse ja fail eemaldatakse. Jätkata?";
    public const string Reread = "Loe uuesti";
    public const string RereadConfirm = "Lugeda fail uuesti praeguste paigutuste ja lugejaga? Ridade otsused kantakse üle sama sisuga ridadele. Igas mustandkuus, mis faili kasutab, kustutatakse otsingu tulemus ja faili ridadest koostatud read — pärast „Otsi maksed“ ja „Koosta read“ uuesti.";
    public const string RemoveFile = "Eemalda fail";
    public const string RemoveFileConfirm = "Eemaldada fail andmebaasist? Fail kaob perioodi failide hulgast; selle ridade otsused ja neist koostatud read kustuvad.";
    public const string SelectAll = "Vali kõik";
    public static string RemoveFilesConfirm(int n) => $"Eemaldada {n} faili andmebaasist? Failid kaovad perioodi failide hulgast; nende ridade otsused ja neist koostatud read kustuvad.";
    public static string UnlinkFilesConfirm(int n) => $"Eemaldada {n} faili perioodi failide hulgast? Nende failide ridade otsused kustuvad.";
    public static string FilesRefused(IReadOnlyList<string> refused) => $"Need failid jäid alles:\n{string.Join("\n", refused)}";
    public const string Unlisted = "sidumata";
    public static string RereadDone(string fileName, int carried, int dropped, int draftsDeleted, IReadOnlyList<string> runsCleared)
    {
        var parts = new List<string> { $"{Count(carried, "otsus", "otsust")} üle kantud", $"{Count(dropped, "otsus", "otsust")} kustunud (rida ei loetud enam samamoodi)" };
        if (draftsDeleted > 0)
        {
            parts.Add($"{Count(draftsDeleted, "koostatud rida", "koostatud rida")} kustutatud");
        }

        if (runsCleared.Count > 0)
        {
            parts.Add($"otsingu tulemus kustutatud: {string.Join(", ", runsCleared)} — „Otsi maksed“ uuesti");
        }

        return $"{Reread}: {fileName}: {string.Join("; ", parts)}.";
    }

    private static string Count(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";
    public const string RemoveLineConfirm = "Eemaldada käsitsi lisatud rida?";
    public const string AmountChangeTitle = "Summa muutmine";
    public const string AmountNotNumber = "Summa ei ole arv.";

    /// <summary>A typed amount's refusal in the window's own words (decision 150), by its code and figure; null for one this
    /// flow does not know, which is shown as the store said it. <paramref name="byHand"/>: the amount was typed on a line
    /// added by hand, not on a share of a row.</summary>
    /// <param name="unsplit">The payment is a row no part was split off yet: there are no parts to lower or take out.</param>
    public static string? AmountRefused(ReviewException refusal, bool byHand = false, bool unsplit = false) => refusal.Code switch
    {
        ReviewWorkspace.AmountInvalid => "Summa peab olema täissentides ja mitte alla nulli.",
        ReviewWorkspace.SplitUnread => "Väljavõte ei andnud sellele reale summat – pole midagi jagada.",
        ReviewWorkspace.SplitCopy => "See rida on teise rea koopia – jagage originaali. (Koopiale jäänud osa eemaldatakse summaga 0.)",
        ReviewWorkspace.SplitOwnShare when unsplit && refusal.Figure is { } whole => $"Väljavõtte summat ({whole:N2}) ei saa suurendada ega nulliks muuta; "
            + "väiksem summa (üle 0) jagab makse osadeks.",
        ReviewWorkspace.SplitOwnShare when refusal.Figure is { } rest => $"Makse põhiosa on {rest:N2}. Uue osa jagamiseks sisestage väiksem summa (üle 0); "
            + "põhiosa suurendamiseks vähendage mõne osa summat või eemaldage osa summaga 0.",
        ReviewWorkspace.SplitPartTooBig when refusal.Figure is { } max => $"Makse põhiosale peab jääma vähemalt 0,01; selle osa summa võib olla kuni {max:N2}.",
        ReviewWorkspace.PeriodNotDraft => "Kuu ei ole enam mustand – summat muuta ei saa.",
        ReviewWorkspace.LineMissing => byHand ? HandLineMissing : PartMissing,
        _ => null,
    };

    /// <summary>The question asked before a typed amount is taken (decision 150).</summary>
    public static string AmountChangeQuestion(string reference, AmountChange change) => change.Kind switch
    {
        AmountChangeKind.SplitOff => $"Jagada makse {reference} osadeks?\n\n{change.To:N2} jääb sellele reale, {change.Difference:N2} läheb uueks osaks. "
            + "Uus osa jääb kategooriata (puudub); kategooria, liigi, deklareerimise ja vajadusel isiku määrake sellele eraldi.",
        AmountChangeKind.PartResized when change.Difference > 0m => $"Muuta osa {reference} summat {change.From:N2} → {change.To:N2}?\n\n"
            + $"{change.Difference:N2} võetakse makse põhiosast (osa 1), sinna jääb {change.RowShare:N2}.",
        AmountChangeKind.PartResized => $"Muuta osa {reference} summat {change.From:N2} → {change.To:N2}?\n\n"
            + $"{-change.Difference:N2} läheb tagasi makse põhiosale (osa 1), see saab {change.RowShare:N2}.",
        AmountChangeKind.PartRemoved => $"Eemaldada osa {reference}?\n\nSelle summa {change.From:N2} läheb tagasi makse põhiosale (osa 1), see saab {change.RowShare:N2}.",
        _ => $"Muuta käsitsi lisatud rea summat {change.From:N2} → {change.To:N2}?",
    };

    /// <summary>The log's line after a typed amount was taken.</summary>
    public static string AmountChanged(string reference, AmountChange change) => change.Kind switch
    {
        AmountChangeKind.SplitOff => $"Makse {reference} jagatud: {change.To:N2} + {change.Difference:N2}.",
        AmountChangeKind.PartResized => $"Osa {reference}: {change.From:N2} → {change.To:N2}; põhiosa {change.RowShare:N2}.",
        AmountChangeKind.PartRemoved => $"Osa {reference} eemaldatud; põhiosa {change.RowShare:N2}.",
        _ => $"Käsitsi lisatud rida: {change.From:N2} → {change.To:N2}.",
    };

    /// <summary>The amount cell's tip on a share of a split row.</summary>
    public static string PartTip(decimal whole) => $"Jagatud makse: kogu summa väljavõttel {whole:N2}. Osa eemaldamiseks sisestage 0.";

    /// <summary>The amount cell's tip on the row's own share of a split row.</summary>
    public static string OwnShareTip(decimal whole) => $"Jagatud makse põhiosa: kogu summa väljavõttel {whole:N2}. Väiksem summa jagab siit uue osa.";

    /// <summary>The amount cell's tip on the own share of a split row that has since turned into a copy.</summary>
    public static string CopyShareTip(decimal whole) => $"Jagatud makse põhiosa koopial: kogu summa väljavõttel {whole:N2}. Jagage originaali; osa eemaldamiseks sisestage osal 0.";
    public static string LookupProgress(int done, int total) => $"Päringud: {done} / {total}";

    public static string LookupOne(string name) => $"Päring: {name}";

    /// <summary>A person's line in the log after a register check: who, the day their facts were checked for, what was done.</summary>
    /// <remarks>Each step in Estonian by its note; the Core's English for a step without one (the notes go one for one with the steps).</remarks>
    public static string LookupLine(PersonLookupOutcome outcome) =>
        $"{outcome.Name}{(outcome.ControlDate is { } day ? $" (seisuga {Day(day)})" : "")}: {string.Join("; ", outcome.Steps.Select((step, i) => (outcome.StepNotes.Count == outcome.Steps.Count ? LookupStep(outcome.StepNotes[i]) : null) ?? step))}";

    /// <summary>The problem lines of a person's register check, in Estonian where worded, in the Core's English else.</summary>
    public static IEnumerable<string> LookupProblemLines(PersonLookupOutcome outcome) =>
        outcome.Problems.Select((english, i) => LookupProblemLine(english, outcome.ProblemNotes.Count == outcome.Problems.Count ? outcome.ProblemNotes[i] : null));

    /// <summary>The run's own problem lines (no run, a person without a code), as <see cref="LookupProblemLines(PersonLookupOutcome)"/>.</summary>
    public static IEnumerable<string> LookupProblemLines(LookupRunReport report) =>
        report.Problems.Select((english, i) => LookupProblemLine(english, report.ProblemNotes.Count == report.Problems.Count ? report.ProblemNotes[i] : null));

    /// <summary>The question after the card's check when a fact set by hand differs from the register's fresh answer.</summary>
    public static string HandBackQuestion(string name, IReadOnlyList<LookupDisagreement> open)
    {
        var lines = open.Select(d => d.Kind == LookupKind.Residency
            ? $"Residentsus — register: {(d.Register ? "resident" : "mitteresident")}; käsitsi: {(d.ByHand == true ? "resident" : "mitteresident")}"
            : $"II sammas — {(d.Derived ? "mitteresidendina" : "Pensionikeskus")}: {Pension(d.Register, d.RegisterRate)}; käsitsi: {(d.ByHand is { } member ? Pension(member, d.ByHandRate) : Core.Report.ReportText.NotChecked)}");
        return $"{name}: käsitsi määratud andmed erinevad registri vastusest.{Environment.NewLine}{Environment.NewLine}{string.Join(Environment.NewLine, lines)}{Environment.NewLine}{Environment.NewLine}Kas võtta registri vastus? Jah — andmed on edaspidi registri omad. Ei — käsitsi määratu jääb.";

        static string Pension(bool member, decimal? rate) => member ? $"liitunud, määr {Percent(rate)}" : Core.Report.ReportText.PensionNotMember;
    }

    public const string NonResidentsMenu = "Mitteresidendid kõigis ettevõtetes…";
    public const string NonResidentsTitle = "Mitteresidendid kõigis ettevõtetes";
    public const string NonResidentsOnlyExemption = "Ainult maksuvabaga";
    public const string NonResidentsSave = "Salvesta xlsx…";
    public const string XlsxFilter = "Exceli töövihik (*.xlsx)|*.xlsx";

    public static string NonResidentsSummary(int companies, int persons, int appliedCompanies, int applied, int usedCompanies, int used) =>
        persons == 0 && used == 0 ? "Andmebaasis ei ole ühtegi mitteresidenti."
        : $"Mitteresidente: {persons}, ettevõtteid: {companies}. Maksuvaba avaldus märgitud: {applied} (ettevõtteid: {appliedCompanies}). "
          + $"Maksuvaba kasutatud ridadel, kus isik oli mitteresident: isikuid {used} (ettevõtteid: {usedCompanies}).";

    public static string NonResidentsLogged(int companies, int persons, int applied, int used) =>
        $"{NonResidentsTitle}: isikuid {persons}, ettevõtteid {companies}; maksuvaba avaldus märgitud {applied}, maksuvaba kasutatud ridadel {used}";

    public const string ExemptionOffNonResidents = "Eemalda mitteresidentide maksuvaba avaldus";
    public const string ExemptionOffNone = "Selle kuu isikute seas ei ole mitteresidenti, kellel oleks maksuvaba avaldus märgitud.";

    public static string ExemptionOffQuestion(int count) =>
        (count == 1 ? "Maksuvaba avaldus on märgitud 1 mitteresidendil. Eemaldada märge temalt?"
            : $"Maksuvaba avaldus on märgitud {count} mitteresidendil. Eemaldada märge neilt kõigilt?")
        + " Muud andmed ei muutu; üksiku isiku märke saab kaardil tagasi panna.";

    public const string ExemptionOnNonResidents = "Märgi mitteresidentidele maksuvaba avaldus";
    public const string ExemptionOnLeftOut = "jäetud vahele (kaardil EMP-väline residentsusriik)";

    public static string ExemptionOnNone(int outsideEea) =>
        "Selle kuu isikute seas ei ole mitteresidenti, kellele maksuvaba avalduse saaks märkida."
        + (outsideEea > 0 ? $" {outsideEea} mitteresidendi kaardil on EMP-väline residentsusriik; neile seda ei märgita." : "");

    public static string ExemptionOnQuestion(int count, int outsideEea) =>
        (count == 1 ? "Maksuvaba avaldus ei ole märgitud 1 mitteresidendil. Märkida see talle?"
            : $"Maksuvaba avaldus ei ole märgitud {count} mitteresidendil. Märkida see neile kõigile?")
        + (outsideEea > 0 ? $" {outsideEea} mitteresidenti, kelle kaardil on EMP-väline residentsusriik, jäetakse vahele." : "")
        + " Mitteresident saab maksuvaba tulu ainult EMP riigi residendina residentsustõendiga. Oma kuusummat ei taastata:"
        + " kehtib seaduse järgi maksuvaba tulu, kuni kaardile sisestatakse muu summa. Muud andmed ei muutu.";

    public const string LookupsLive = "Päringud lähevad EMTA ja Pensionikeskuse avalikele lehtedele, üks isik korraga. Jätkata?";
    public const string UnsavedPerson = "Isiku andmetes on salvestamata muudatused. Salvestada need?";
    public const string BusyClosing = "Töö käib. Oota, kuni see lõpeb, või katkesta see enne sulgemist.";
    public const string StoreMissing = "Andmebaasi faili ei leitud:";

    // the start without its database: what went wrong, and the ways on
    public const string StoreMissingTitle = "Andmebaas";
    public const string NoStoreChosen = "Andmebaasi ei ole veel valitud. Ava olemasolev andmebaasi fail, loo uus andmebaas või jätka ilma andmebaasita (menüüst „Andmebaas“ saab selle avada ka hiljem).";

    public static string StoreNotFound(string path, bool again) =>
        (again ? "Andmebaasi faili ei leitud ka seekord:" : "Andmebaasi faili ei leitud:") + $"\n{path}\n\n"
        + "Fail võib olla teisaldatud, ümber nimetatud või kustutatud, või on see võrgukettal (näiteks S:), mis ei ole veel ühendatud."
        + " Kui võrguketas ühendub, vali „Proovi uuesti“. Ilma andmebaasita jätkates jääb meelde sama fail; menüüst „Andmebaas“ saab andmebaasi avada ka hiljem.";

    public static string StoreNotOpened(string path) =>
        $"Andmebaasi fail on nüüd olemas, kuid seda ei saanud avada:\n{path}\n\n"
        + "Fail võib olla kahjustatud, mõne teise programmi poolt lukus või ei ole see andmebaas. Vali „Proovi uuesti“, kui lukk vabaneb, või otsi õige fail.";

    public const string StoreTryAgain = "Proovi uuesti";
    public const string StoreFind = "Otsi fail…";
    public const string StoreOpenChoice = "Ava andmebaas…";
    public const string StoreCreate = "Loo uus andmebaas…";
    public const string StoreContinueWithout = "Jätka ilma andmebaasita";

    // the offer after an import: the search, and the registers with it
    public const string ImportOfferTitle = "Pärast importi";
    public const string ImportOfferSearchAndCheck = "Otsi maksed ja kontrolli registrid";
    public const string ImportOfferSearchOnly = "Ainult otsi maksed";
    public const string ImportOfferNotNow = "Mitte praegu";

    /// <param name="anythingNew">A file came in new; none did when the files were all there before (the month not yet searched).</param>
    /// <param name="someRefused">A file of the same import was refused (its line in the log says why).</param>
    public static string ImportOffer(bool lookups, bool replaces, bool anythingNew, bool someRefused) =>
        (anythingNew
            ? someRefused ? "Osa faile on imporditud, osa jäi importimata (vaata logi)." : "Failid on imporditud."
            : someRefused ? "Uusi faile ei imporditud (vaata logi), kuid kuu makseid pole veel otsitud." : "Failid olid juba imporditud, kuid kuu makseid pole veel otsitud.")
        + " Kas otsida nüüd maksed?\n\n"
        + "Otsing leiab failidest isikutele tehtud maksed."
        + (replaces ? " Kuu varasem otsingu tulemus asendatakse uuega; tehtud otsused jäävad alles." : "")
        + (lookups ? "\n\nRegistrite kontroll küsib iga isiku kohta EMTA ja Pensionikeskuse avalikelt lehtedelt, üks isik korraga; see algab pärast otsingut." : "");

    // the summary of a search or a register check: the problems found, counted by kind
    public const string LastSummary = "Viimane kokkuvõte…";
    public const string SummaryTitle = "Kokkuvõte";
    public const string NoSummaryYet = "Selle ettevõtte kohta pole veel kokkuvõtet.";
    public const string SummaryUnreadable = "Selle ettevõtte viimast kokkuvõtet ei saa lugeda.";
    public const string SummaryNone = "Probleeme ei leitud.";
    public const string SummarySearch = "Otsing";
    public const string SummaryPayments = "Maksed";
    public const string SummaryRegisters = "Registrid";
    public const string SummarySectionColumn = "Osa";
    public const string SummaryCodeColumn = "Kood";
    public const string SummaryKindColumn = "Probleem";
    public const string SummaryCountColumn = "Arv";
    public const string SummaryWhoColumn = "Kes / kus";
    public const string SummaryDetailColumn = "Selgitus";
    public const string SummaryHint = "Vali probleem, et näha, keda või milliseid ridu see puudutab. Topeltklõps isikul valib ta põhiaknas.";
    public const string SummaryCheckNotCompleted = "Registrite kontroll ei jõudnud lõpuni (katkestati või ebaõnnestus): kokkuvõte näitab, mis selleks ajaks oli teada.";
    public const string SummaryNotKept = "Kokkuvõtet ei saanud andmebaasi salvestada: „Viimane kokkuvõte…“ seda hiljem ei ava.";
    public const string SummaryNoPerson = "kuu failid";
    public const string SummaryWholeCheck = "kontroll tervikuna";
    public const string SummaryPersonGone = "Seda isikut ei ole kuu vaates enam.";
    public const string SummaryOtherCompany = "Kokkuvõte on teise ettevõtte kohta: vali enne see ettevõte.";

    /// <summary>A month as the summary names it: 10.2026.</summary>
    public static string SummaryMonth(int year, int month) => $"{month:00}.{year}";

    public static string SummaryMonthMissing(int year, int month) => $"Kokkuvõtte kuud {SummaryMonth(year, month)} ei saa põhiaknas valida.";

    /// <summary>The summary's header: whose, which month, when, what it covers and how many.</summary>
    public static string SummaryHeader(Core.Review.RunSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var runs = summary.Searched && summary.CheckedRegisters ? $"„{RunMatch}“ ja „{RunLookups}“" : summary.Searched ? $"„{RunMatch}“" : $"„{RunLookups}“";
        var made = summary.MadeAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        var lines = new List<string>
        {
            $"{summary.CompanyName} ({summary.RegistryCode}), kuu {SummaryMonth(summary.Year, summary.Month)}",
            $"Tehtud {made}: {runs}",
            summary.Total == 0 ? SummaryNone : $"Probleeme kokku: {summary.Total}",
        };
        if (summary.CheckedRegisters && !summary.CheckCompleted)
        {
            lines.Add(SummaryCheckNotCompleted);
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>A kind of problem in a few words, by its code — every code the search, the review, the payments' checks, the
    /// register check and the undeclared income can be said under; null for a code not known here (the code alone is shown).</summary>
    public static string? ShortTitle(string code) => ShortTitles.TryGetValue(code, out var title) ? title : null;

    private static readonly Dictionary<string, string> ShortTitles = new(StringComparer.Ordinal)
    {
        // the search's and the review's problems
        [MatchProblem.AmbiguousMask] = "Otsingumask võib tähendada teist isikut",
        [MatchProblem.SharedRow] = "Rida leiti mitmele isikule",
        [MatchProblem.NoFilters] = "Paigutusel pole kategooriate filtreid",
        [MatchProblem.NoEmploymentInMonth] = "Töötamine ei kata kuud",
        [MatchProblem.SeveralEntries] = "Isik on TÖR-is mitu korda",
        [MatchProblem.NoName] = "TÖR-i real puudub nimi",
        [MatchProblem.HeaderRow] = "Päiserida andmete seas",
        [MatchProblem.NotDone] = "Tehing ei liigutanud raha",
        [MatchProblem.BadAmount] = "Summat ei saanud lugeda",
        [MatchProblem.DuplicateRow] = "Rida on teise rea koopia",
        [MatchProblem.PayeeAmbiguous] = "Saaja nimed on kahel isikul",
        [MatchProblem.PayeeHandLine] = "Saajal on sama summaga käsitsi rida",
        [MatchProblem.PayeeCodeElsewhere] = "Rea isikukood on teisel isikul",
        [MatchProblem.NamesakeOtherCode] = "Samanimeline isik teise koodiga",
        [MatchProblem.DecisionMoved] = "Otsus viidi originaalreale",
        [MatchProblem.DecisionOnCopy] = "Otsus on koopia real",
        [MatchProblem.OtherEmployer] = "Teise tööandja TÖR-i read",
        [MatchProblem.IncludedWithoutRow] = "„Kaasa“, kuid TÖR-is ei ole",
        [MatchProblem.OutsideTorSalary] = "Palk isikule, keda TÖR ei loetle",
        [MatchProblem.OldReading] = "Fail on loetud vanal viisil",
        [MatchProblem.StateUnread] = "Tehingu olek on lugemata",
        [MatchProblem.RowsBelowLastPayee] = "Read viimasest saajast allpool",
        [MatchProblem.NameContained] = "Nimi sisaldub teise isiku nimes",

        // the payments' refusals and notices
        [ReviewChecks.NoPerson] = "Maksel ei ole isikut",
        [ReviewChecks.NoPayeeId] = "Isikul ei ole koodi",
        [ReviewChecks.ResidencyNotChecked] = "Residentsus on kontrollimata",
        [ReviewChecks.NoType] = "Väljamakse liik puudub",
        [ReviewChecks.ForeignA1Needed] = "Vaja välisriigi A1 tõendit",
        [ReviewChecks.OutsideTor] = "Isik ei ole TÖR-is",
        [ReviewChecks.TwoContracts] = "Kaks eri liiki töötamist",
        [ReviewChecks.UnknownType] = "Tundmatu väljamakse liik",
        [ReviewChecks.NotInPeriod] = "Makse on teises kuus",
        [ReviewChecks.NotCounted] = "Makse ei lähe arvesse",
        [ReviewChecks.ForeignCurrency] = "Summa ei ole eurodes",
        [ReviewChecks.MoneyIn] = "Laekumine, mitte väljamakse",
        [ReviewChecks.CodeInvalid] = "Kood ei ole Eesti isikukood",
        [ReviewChecks.TypeTaxesDiffer] = "Käsitsi valitud liigiga muutuvad maksud",
        [ReviewChecks.PayOnFreeWork] = "Palk tasuta töötamise kandele",
        [PaymentChecks.RatesExceedOne] = "Maksumäärad kokku 100 % või üle",
        [PaymentChecks.NegativeAmount] = "Negatiivne summa",
        [PaymentChecks.ZeroNotAllowed] = "Summa 0 ei ole lubatud",
        [PaymentChecks.TypePayeeMismatch] = "Liik ei sobi residentsusega",
        [PaymentChecks.ThirdPillarNotComputed] = "III samba maksuvaba osa ei arvutata",
        [PaymentChecks.A1CountriesMissing] = "A1 riigid puuduvad",
        [PaymentChecks.A1CountriesInconsistent] = "A1 riigid ei sobi kokku",
        [PaymentChecks.PensionNotChecked] = "II sammas on kontrollimata",
        [PaymentChecks.PensionRateUnknown] = "II samba määr on teadmata",
        [PaymentChecks.PensionRateInvalid] = "II samba määr ei sobi",
        [PaymentChecks.ExemptionWithoutApplication] = "Maksuvaba ilma avalduseta",
        [PaymentChecks.ExemptionAboveLegal] = "Maksuvaba üle seaduse piiri",
        [PaymentChecks.ExemptionSubCent] = "Maksuvaba ei ole täissentides",
        [PaymentChecks.MinimumShareInvalid] = "Miinimumi kuu osa ei sobi",
        [PaymentChecks.WorkAbilityApplicationWithoutStatus] = "Töötukassa taotlus ilma töövõime märketa",
        [PaymentChecks.TreatyRateInvalid] = "Maksulepingu määr ei sobi",
        [PaymentChecks.ExemptionNotEea] = "Maksuvaba EMP-välise riigi residendile",

        // the register check
        [LookupRunner.Skipped] = "Registritest ei küsitud",
        [LookupRunner.Failed] = "Registri päring ebaõnnestus",
        [LookupRunner.Disagrees] = "Register erineb käsitsi määratust",
        [LookupRunner.RateUnknown] = "II samba määr jäi teadmata",
        [LookupRunner.Reset] = "II samba andmed on teadmata",
        [LookupNote.NoRun] = "Kuu makseid ei ole otsitud",

        // why income is not declared, as the Probleemid column says it
        [Core.Review.Undeclared.Excluded] = "Isik on deklaratsioonist välja jäetud",
        [Core.Review.Undeclared.Refused] = "Rida ei saa koostada",
        [Core.Review.Undeclared.Unticked] = "„Deklareeri“ on maha võetud",
        [Core.Review.Undeclared.NotDeclared] = "Ei lähe deklaratsiooni",
        [Core.Review.Undeclared.Held] = "Liik ootab keeldutud makset",
        [Core.Review.Undeclared.TypeHeld] = "Liik ootab keeldutud makset",
    };
    public const string NoPeriodYet = "periood on tühi — impordi failid";
    public const string Cancelled = "katkestatud";
    public const string Done = "valmis";
    public const string NoCompany = "Vali või lisa ettevõte.";

    public const string ImportNeedsEmployer = "Failide hulgas ei ole TÖR väljavõtet, mis nimetaks tööandja (registrikood ja nimi). Vali ettevõte või lisa failide hulka TÖR väljavõte.";
    public const string ImportOtherEmployer = "Failide TÖR väljavõte nimetab teise tööandja:";
    public const string ImportSwitchToExisting = "Vahetada ettevõte selle vastu ja importida failid sinna?";
    public const string ImportSwitchToNew = "Luua see ettevõte, vahetada selle vastu ja importida failid sinna?";
    public const string ImportLockedMonth = "Faile ei imporditud: selle ettevõtte valitud kuu ei ole enam mustand.";
    public const string CompanyCreated = "Ettevõte loodi TÖR väljavõtte järgi";
    public const string CompanyTaken = "Ettevõte valiti TÖR väljavõtte järgi";
    public const string Locked = "lukus";
    public const string Editable = "muudetav";
    public static string RowsLeftOut(int count) => count == 1 ? "1 tühi rida jäeti kõrvale" : $"{count} tühja rida jäeti kõrvale";
    public const string CloseCompany = "Sulge ettevõte";
    public const string CompanyClosed = "Ettevõte suleti: järgmised failid imporditakse nende TÖR väljavõttes nimetatud tööandjale";
    public const string CompanyClosedHint = "Ettevõte on suletud: vali see loendist või impordi failid";
    public const string Problem = "Viga";

    // the menus (with their Alt keys) and the grids' tools
    public const string MenuStore = "&Andmebaas";
    public const string MenuCompany = "&Ettevõte";
    public const string MenuPeriod = "&Periood";
    public const string MenuPerson = "&Isik";
    public const string MenuTools = "&Tööriistad";
    public const string Search = "Otsi:";
    public const string ScopeSelectedPerson = "Valitud isik";
    public const string ScopeAll = "Kõik";
    public const string ScopeConflict = "Kategooriate konflikt";
    public const string BadAmountMark = "?";
    public const string MenuView = "&Vaade";
    public const string LogHeightOneRow = "Logiaken: üks rida";
    public const string LogHeightOneRowTip = "Märgitud: logiaken on ühe rea kõrgune ja näitab viimast rida. Märkimata: logiaken on tavakõrgusega.";
    public const string PersonsSalaryOnly = "Isikute tabel: allikate veerud peidetud, ainult PALK NETO KOKKU";
    public const string MenuSettings = "&Seaded";
    public const string DeclareEachPayment = "Deklaratsioon: iga makse eraldi reana"; // the simulation's choice; the menu has one toggle
    public const string DeclareTotalsPerType = "Deklaratsioon: koond väljamakse liigi kaupa, viimase kuupäevaga";
    public const string DeclareTotalsPerTypeTip = "Märgitud: üks rida väljamakse liigi kohta, kuu viimase makse kuupäevaga. Märkimata: iga makse eraldi reana. Read muutuvad järgmisel „Koosta read“.";
    public const string DeclareModeChanged = "Deklaratsiooni read koostatakse nüüd";
    public const string TwoContractsMenu = "Palk kahe kehtiva lepingu korral";
    public const string TwoContractsSalary = "Palk töölepingu järgi";
    public const string TwoContractsAsk = "Küsi liik iga makse juures";
    public const string TwoContractsLatest = "Palk hiliseimana alanud lepingu järgi";
    public const string TwoContractsChanged = TwoContractsMenu + ":";
    public const string TwoContractsNote = "TÖR";

    /// <summary>The check column's word on an unticked "TÖR-väline palgad" row refused as OUTSIDE_TOR.</summary>
    public const string OutsideTorNote = "TÖR-väline";

    /// <summary>The check cell's tip on a "TÖR-väline palgad" row refused as OUTSIDE_TOR, ticked or not.</summary>
    public static string OutsideTorTip(bool noRegister) => noRegister
        ? "Palgamustriga makse isikule, keda TÖR ei loetle: kuu otsiti ilma TÖR-failita. Lisa kuu TÖR-fail ja otsi uuesti või anna reale kategooria (Palgad) või väljamakse liik. Kuni seda pole, makset ei deklareerita ja see ei ole Deklareerimata summas."
        : "Palgamustriga makse isikule, keda kuu TÖR ei loetle: registreeri töötamine TÖR-is ja otsi uuesti või anna reale kategooria või väljamakse liik. Kuni seda pole, makset ei deklareerita ja see ei ole Deklareerimata summas.";

    /// <summary>The check cell's tip on a salary whose type the Seaded rule picked from two contracts in force that day.</summary>
    public static string TwoContractsPickedTip(IReadOnlyList<string> types, string? typeCode) =>
        $"Makse päeval kehtib kaks eri liiki töötamist (TÖR {string.Join(", ", types)}); liik {typeCode ?? "–"} on valitud seadete järgi (Seaded → {TwoContractsMenu}). Kontrolli, et see sobib.";

    /// <summary>The taxes a payment type carries, by name, in the matrix's order (the notice TYPE_TAXES_DIFFER compares them).</summary>
    private static List<string> TaxNames(Core.Tax.PaymentType type)
    {
        var names = new List<string>();
        if (type.SocialTax)
        {
            names.Add("sotsiaalmaks");
        }

        if (type.SocialTaxMinimum)
        {
            names.Add("sotsiaalmaksu miinimum");
        }

        if (type.UnemploymentInsurance)
        {
            names.Add("töötuskindlustusmakse");
        }

        if (type.FundedPension)
        {
            names.Add("kogumispension");
        }

        if (type.IncomeTaxRate is { } rate)
        {
            names.Add($"tulumaks {(rate * 100m).ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("et-EE"))} %");
        }

        return names;
    }

    /// <summary>The check cell's tip, and the simulation's line, for a notice of a declared payment (shown only: it refuses
    /// nothing and changes no figure).</summary>
    public static string NoticeTip(Core.Review.ReviewPayment p, string code)
    {
        switch (code)
        {
            case Core.Review.ReviewChecks.TypeTaxesDiffer
                when p.TypeCode is { } hand && p.DefaultTypeCode is { } byDefault
                    && Core.Tax.PaymentTypes.TryGet(hand, out var handType) && Core.Tax.PaymentTypes.TryGet(byDefault, out var defaultType):
                var handTaxes = TaxNames(handType!);
                var defaultTaxes = TaxNames(defaultType!);
                var dropped = string.Join(", ", defaultTaxes.Except(handTaxes));
                var added = string.Join(", ", handTaxes.Except(defaultTaxes));
                return $"{code}: Väljamakse liik {hand} on valitud käsitsi; TÖR-i töötamise liigi järgi oleks liik {byDefault}. Liigiga {hand} "
                    + (dropped.Length > 0 ? $"jääb ära: {dropped}" : "ei jää ühtki maksu ära")
                    + (added.Length > 0 ? $"; lisandub: {added}" : "")
                    + $". Kui see on töölepingu alusel makstud palk, vali liik {byDefault} või {Default} (SMS § 2, TKindlS, KoPS). Arvutus ega deklaratsioon sellest ei muutu.";
            case Core.Review.ReviewChecks.PayOnFreeWork:
                return $"{code}: Palk on makstud isikule, kelle TÖR-i kanne on 501 Tasuta töötamine. Makse deklareeritakse liigiga {p.TypeCode ?? "–"} nagu palk, sotsiaalmaksu miinimumita (otsus 48). Kui tööd tehakse tasu eest, paranda töötamise liik TÖR-is (MKS § 25¹).";
            default:
                return code;
        }
    }

    public static string TwoContractsText(Core.Review.TwoContractsRule rule) => rule switch
    {
        Core.Review.TwoContractsRule.Ask => TwoContractsAsk,
        Core.Review.TwoContractsRule.LatestStart => TwoContractsLatest,
        _ => TwoContractsSalary,
    };
    public const string LayoutsImport = "Impordi failist…";
    public const string Layouts = "Väljavõtte paigutused…";
    public const string LayoutsTitle = "Väljavõtte paigutused (omad)";
    public const string LayoutFilter = "Paigutuste fail (*.json)|*.json|Kõik failid (*.*)|*.*";
    public const string LayoutId = "ID";
    public const string LayoutBank = "Pank";
    public const string LayoutHeaders = "Päiseid";
    public const string LayoutStanding = "Seis";
    public const string LayoutOverrides = "oma, asendab sisseehitatud";
    public const string LayoutNew = "oma, uus";
    public const string LayoutBuiltIn = "sisseehitatud";
    public const string DeleteLayout = "Kustuta paigutus";
    public const string SaveLayouts = "Salvesta paigutused faili…";
    public const string NoOwnerLayouts = "Omi paigutusi pole: failid loetakse sisseehitatud paigutustega. Alustuseks salvesta paigutused faili, muuda sobivat ja impordi.";
    public static string LayoutsSaved(string path, int count) => $"{SaveLayouts.TrimEnd('…')}: {count} paigutust kirjutatud faili {path}.";
    public static string DeleteLayoutConfirm(string id) => $"Kustutada paigutus „{id}“? Sama ID-ga sisseehitatud paigutus (kui on) tuleb tagasi; salvestatud failid jäävad loetuks, nagu nad on.";
    public static string LayoutsImported(IReadOnlyList<string> ids) => $"{Layouts.TrimEnd('…')}: imporditud {ids.Count} paigutus{(ids.Count == 1 ? "" : "t")} ({string.Join(", ", ids)}); failid loetakse nüüd nendega — salvestatud fail loetakse uuesti „Loe uuesti“ nupuga.";

    /// <summary>A refusal of the owner's layouts file, in Estonian by its code (decisions 136, 183, 211); null for any other.
    /// The Core's detail lines follow: they name the layouts' ids and the JSON fields, English in the file too.</summary>
    public static string? LayoutsRefused(Exception refusal)
    {
        var subject = Core.Store.LayoutsRefusal.SubjectOf(refusal);
        var detail = Core.Store.LayoutsRefusal.DetailOf(refusal);
        var said = Core.Store.LayoutsRefusal.CodeOf(refusal) switch
        {
            Core.Store.LayoutsRefusal.NotJson => $"Fail ei ole loetav paigutuste JSON{(subject is null ? "" : $" (rida {subject})")}.",
            Core.Store.LayoutsRefusal.NoLayout => "Failis ei ole ühtegi paigutust.",
            Core.Store.LayoutsRefusal.NullEntry => "Failis on paigutuste seas tühi kirje (null).",
            Core.Store.LayoutsRefusal.Unusable => $"Paigutust ei saa kasutada ({subject}): igal paigutusel peab olema ID, pank, päised ja kuupäeva, saaja ja summa veerg päiste seas; kuupäevamustrid peavad olema kasutatavad ja olekute loendis ei tohi olla tühja kirjet.",
            Core.Store.LayoutsRefusal.RepeatedId => $"Failis on sama ID mitu korda ({subject}): üks paigutus ühe ID kohta.",
            Core.Store.LayoutsRefusal.Collides when subject == Core.Store.LayoutsRefusal.OnDelete =>
                "Paigutust ei saa kustutada: tagasi tulev sisseehitatud paigutus kattuks teise salvestatud paigutuse päisereaga. Kustuta enne see teine paigutus.",
            Core.Store.LayoutsRefusal.Collides => "Paigutuse päiserida on juba teisel paigutusel: faili lehte ei saaks neist eristada. Muuda päiseid või kasuta sisseehitatud paigutuse ID-d.",
            _ => null,
        };
        var nothing = subject == Core.Store.LayoutsRefusal.OnDelete ? " Midagi ei kustutatud." : " Midagi ei salvestatud.";
        return said is null ? null : said + nothing + (detail is null ? "" : Environment.NewLine + detail);
    }
    public const string ModeEach = "iga makse eraldi reana";
    public const string ModeTotals = "koondina liigi kaupa";
    public const string RatesMissing = "Selle aasta maksumäärad ja kuumäär ei ole programmis: kuu ridu ei saa koostada enne, kui uus versioon need toob.";
    public const string RunStale = "maksed otsimata või failid muutusid pärast otsingut (Otsi maksed)";
    public const string RunStaleRefused = "Perioodi maksed on otsimata või failid muutusid pärast otsingut: read koostatakse alles otsingu järel. Vajuta „Otsi maksed“ (kahe TÖR-lehe korral eemalda enne Failid-aknas vana otsing ja vana TÖR).";
    public const string RunStaleExport = "Perioodi maksed on otsimata või failid muutusid pärast otsingut: read ei sisalda kõiki makseid. Vajuta enne eksporti „Otsi maksed“ ja „Koosta read“ (kahe TÖR-lehe korral eemalda enne Failid-aknas vana otsing ja vana TÖR).";
    public const string StoreSetAside = "kõrvale pandud";
    public const string DeclareModeRefused = "Selle kuu read on e-MTA-s juba teises lõikes (iga makse eraldi või koond liigi kaupa). Vali Seaded menüüst sama lõige või võta enne read deklaratsioonist välja.";

    // the declaration tab
    public const string ReviewTab = "Ülevaatus";
    public const string DeclarationTab = "Deklaratsioon";
    public const string BuildLines = "Koosta read";
    public const string Export = "Ekspordi XML…";
    public const string Withdraw = "Tühista eksport";
    public const string MarkUploaded = "Märgi üles laaditud";
    public const string ReadFeedback = "Loe tagasiside…";
    public const string MarkConfirmed = "Märgi kinnitatuks";
    public const string Reopen = "Ava parandamiseks";
    // the reopen undone while nothing was sent since the confirmation (the fifth audit, item 8)
    public const string UndoReopen = "Loobu parandamisest";
    public const string UndoReopenConfirm = "Loobuda parandamisest? Read koostatakse uuesti; kui midagi ei ole vaja saata, läheb periood tagasi olekusse, milles see enne avamist oli (kinnitatud, üles laaditud või eksporditud; e-MTA-s ei muutu midagi).";

    /// <summary>The log's line of an undone reopen: the state the period is back in.</summary>
    public static string UndoReopenDone(PeriodState state) => "Loobu parandamisest: periood on taas " + state switch
    {
        Core.Store.PeriodState.Confirmed => "kinnitatud.",
        Core.Store.PeriodState.Uploaded => "üles laaditud (kinnitamata).",
        _ => "eksporditud (üles laadimata).",
    };

    /// <param name="exportedOnly">The month would go back to exported, never marked uploaded: the lines differ from the file
    /// written, and nothing is known to be in e-MTA.</param>
    public static string UndoReopenHasDrafts(int count, bool exportedOnly = false) => exportedOnly
        ? $"Ülevaatus erineb eksporditud failist: {count} rida tuleb uuesti eksportida. Periood jääb mustandiks; ekspordi read või muuda ülevaatus tagasi ja vajuta uuesti „{UndoReopen}“."
        : $"Ülevaatus erineb e-MTA-s olevast: {count} rida tuleb saata parandusena. Periood jääb mustandiks; ekspordi read või muuda ülevaatus tagasi ja vajuta uuesti „{UndoReopen}“.";

    /// <summary>The log's line of the same refusal: <see cref="UndoReopenHasDrafts"/> in its keyed form.</summary>
    public static string UndoReopenDraftsLog(IReadOnlyList<string> keys, bool exportedOnly = false) => exportedOnly
        ? $"Ülevaatus erineb eksporditud failist: {keys.Count} rida tuleb uuesti eksportida ({string.Join(", ", keys)})."
        : $"Ülevaatus erineb e-MTA-s olevast: {keys.Count} rida tuleb saata parandusena ({string.Join(", ", keys)}).";

    public const string ResolveAccepted = "Kinnita rida käsitsi";
    public const string ResolveRejected = "Lükka rida tagasi";
    public const string Calculator = "Kalkulaator…";
    public const string Key = "Võti";
    public const string Action = "Toiming";
    public const string Status = "Olek";
    public const string Type = "Liik";
    public const string Net = "Neto";
    public const string Gross = "Bruto";
    public const string SocialTax = "SM";
    public const string MinimumIncrease = "SM suurendus";
    // TSD_OPTV_SM_VAHENDUS: the part of the base Töötukassa pays the social tax on, shown beside the minimum's increase
    public const string WorkAbilityReduction = Core.Report.ReportText.WorkAbilityReduction;
    public const string FundedPension = "KP";
    public const string UnemploymentEmployee = "TK";
    public const string UnemploymentEmployer = "TTK";
    public const string IncomeTax = "TM";
    public const string ExemptionUsed = "Maksuvaba";
    public const string SourceColumn = "Allikas";
    public const string Feedback = "Tagasiside";
    public const string Version = "Versioon";
    public const string File = "Fail";
    public const string Time = "Aeg";
    public const string UniqueId = "uniqueID";
    public const string Result = "Tulemus";
    public const string ExportFolder = "Vali kaust, kuhu XML-fail salvestada";
    public const string FeedbackFilter = "Tagasisideraport (*.xml;*.zip)|*.xml;*.zip|Kõik failid (*.*)|*.*";
    public const string WithdrawConfirm = "Võtta eksport tagasi? Read saavad uuesti mustandiks. Kirjutatud fail nimetatakse ümber (lõpp .tagasi-voetud), kui see on leitav, et seda kogemata üles ei laaditaks; logi ütleb, mis failist sai. Ainult faili puhul, mida e-MTA-sse ei laaditud.";

    /// <summary>What became of a withdrawn export's file, for the log.</summary>
    public static string WithdrawnFileNote(Core.Declare.WithdrawnFile file) => file.State switch
    {
        Core.Declare.WithdrawnFileState.SetAside => $"Tagasi võetud fail nimetati ümber: {file.Path}",
        Core.Declare.WithdrawnFileState.NotFound => $"Tagasi võetud faili ei leitud ({file.Path}); kui see on mujal alles, ära seda e-MTA-sse laadi.",
        Core.Declare.WithdrawnFileState.Unreachable => $"Tagasi võetud faili kaust ei ole praegu kättesaadav ({file.Path}); fail võib seal oma nime all alles olla — nimeta see ümber või kustuta, ära seda e-MTA-sse laadi.",
        Core.Declare.WithdrawnFileState.FolderUnknown => $"Tagasi võetud faili {file.FileName} asukoht ei ole teada; kustuta see või ära seda e-MTA-sse laadi.",
        _ => $"Tagasi võetud faili ei saanud ümber nimetada ({file.Path}: {file.Error}); kustuta see käsitsi või ära seda e-MTA-sse laadi.",
    };
    public const string ConfirmForce = "Mõni rida ootab veel vastust, vajab parandust või lükati e-MTA-s tagasi (pole serveris või serveris on alles varasem rida). Märkida periood siiski kinnitatuks?";

    /// <summary>The feedback's note on the keys whose correction or cancellation MTA refused over a line on the server (decision 205).</summary>
    public static string RefusedOverNote(IReadOnlyList<string> keys) =>
        $"e-MTA lükkas tagasi paranduse või tühistuse: {string.Join(", ", keys)}. Serveris on alles varasem rida; „Ava parandamiseks“ ja „Koosta read“ saadavad ülevaate oma uuesti.";

    /// <summary>The tip of a line whose correction or cancellation MTA refused: the figures shown are the server's.</summary>
    public static string RefusedOverTip(StoredPayment refused, StoredPayment standing) =>
        $"Versioon {refused.Version} ({ActionText(refused.Action)}) lükati e-MTA-s tagasi; serveris on versioon {standing.Version} – read näitavad selle summasid.";

    /// <summary>The confirmation's refusal in the window's own words (decision 183): drafts never sent stop it (a force does
    /// not pass them); lines open or refused are named, and the question follows.</summary>
    public static string ConfirmRefused(Core.Declare.DeclareException refusal)
    {
        if (refusal.DraftLines.Count > 0)
        {
            return $"Saatmata mustandread: {string.Join(", ", refusal.DraftLines)}. Ekspordi need või vajuta uuesti „{BuildLines}“, siis märgi periood kinnitatuks.";
        }

        var parts = new List<string>();
        if (refusal.OpenLines.Count > 0)
        {
            parts.Add($"Vastust ootavad või parandust vajavad read: {string.Join(", ", refusal.OpenLines)}.");
        }

        if (refusal.RefusedLines.Count > 0)
        {
            parts.Add($"e-MTA tagasi lükatud read (pole serveris): {string.Join(", ", refusal.RefusedLines)}. Mida ülevaade veel deklareerib, saadab uuesti „Ava parandamiseks“ ja „Koosta read“.");
        }

        if (refusal.RefusedOverLines.Count > 0)
        {
            parts.Add($"e-MTA tagasi lükatud parandused või tühistused (serveris on alles varasem rida): {string.Join(", ", refusal.RefusedOverLines)}. Mida ülevaade deklareerib, saadab uuesti „Ava parandamiseks“ ja „Koosta read“.");
        }

        return string.Join(Environment.NewLine, parts);
    }

    /// <summary>A refusal of the person card's save (or of a person added) in Estonian, by its code; null for any other.</summary>
    public static string? CardRefused(ReviewException refusal) => refusal.Code switch
    {
        ReviewWorkspace.PersonExists => $"Ettevõtte teisel isikul on juba isikukood {refusal.Subject}.",
        ReviewWorkspace.CodeInvalid => $"„{refusal.Subject}“ ei ole Eesti isikukood (üksteist numbrit, päris sünnikuupäevaga); välismaalane nimetatakse MTA mitteresidendi koodiga.",
        ReviewWorkspace.NameMissing => "Isikul peab olema eesnimi ja perekonnanimi.",
        ReviewWorkspace.MrrInvalid => $"„{refusal.Subject}“ ei ole MTA mitteresidendi kood: selles on ainult ladina tähed A–Z ja numbrid, ilma tühikute ja muude märkideta.",
        ReviewWorkspace.PeriodNotDraft => "Kuu ei ole enam mustand – seda ei saa muuta.",
        _ => null,
    };

    /// <summary>A refusal to take a file out, read it again or unlink it, in Estonian by its code; null for any other.</summary>
    public static string? FileRefused(Exception refusal)
    {
        var count = ImportRefusal.CountOf(refusal);
        var subject = ImportRefusal.SubjectOf(refusal);
        return ImportRefusal.CodeOf(refusal) switch
        {
            ImportRefusal.Declared => $"Selle faili ridadest on deklareeritud {count} rida (serveris või ootab vastust); fail jääb nende allikaks.",
            ImportRefusal.Drafts => $"{count} mustandrida viitab selle faili ridadele; vajutage enne uuesti „Koosta read“.",
            ImportRefusal.Totals => $"Serveris olevad {count} kokkuvõtterida liidavad selle faili ridu; fail jääb nende allikaks.",
            ImportRefusal.MonthsPastDraft when subject is not null => $"Fail on kuu(de) {subject} failide hulgas, mis on juba eksporditud või deklareeritud; see jääb nende dokumendiks.",
            ImportRefusal.MonthsPastDraft => $"{count} juba eksporditud või deklareeritud kuu failide hulgas on see fail; see jääb nende dokumendiks.",
            ImportRefusal.Runs => "Otsingu tulemus kasutab seda faili; kustutage enne otsingu tulemus (või otsige uuesti ilma selle failita).",
            ImportRefusal.Decisions => $"{count} ülevaate otsust viitab selle faili ridadele; eemaldage fail enne selle perioodide failide hulgast.",
            ImportRefusal.OtherMonths => $"Faili loetlevad ka teised mustandkuud ({subject}); eemaldage see enne nende failide hulgast.",
            ImportRefusal.NotListed => "Fail ei ole enam selle perioodi failide hulgas; sulgege aken ja avage see uuesti.",
            ImportRefusal.BytesMissing => $"Faili{(subject is null ? "" : $" „{subject}“")} sisu ei ole andmebaasis; faili ei saa uuesti lugeda. Importige fail uuesti.",
            ImportRefusal.NotFound => "Faili ei ole enam andmebaasis; sulgege aken ja avage see uuesti.",
            ImportRefusal.OtherCompany => "Fail kuulub teisele ettevõttele kui see periood.",
            ImportRefusal.PeriodNotDraft => "Kuu ei ole enam mustand: selle failid jäävad kuu dokumendiks.",
            ImportRefusal.PeriodNotFound => "Perioodi ei ole enam andmebaasis; valige periood uuesti.",
            _ => null,
        };
    }

    /// <summary>A refusal the window words itself (the card's save, a file's removal), or null: the exception's own text then.</summary>
    public static string? Worded(Exception refusal) => refusal is ReviewException review ? CardRefused(review) ?? ReviewRefused(review)
        : RunRefused(refusal) ?? DeclareRefused(refusal) ?? FeedbackRefused(refusal) ?? BuildRefused(refusal) ?? ReaderRefused(refusal) ?? PhrasesRefused(refusal) ?? LayoutsRefused(refusal) ?? FileRefused(refusal);

    /// <summary>A refusal of a file's reading (the reader, the employer's check, the same bytes stored already) or a reading that
    /// recognised nothing, in Estonian by its code (decision 229, the owner's option A); null for any other. No file name: every
    /// line it goes on starts with it. The library's own text of a damaged workbook stays in the Core's English the log keeps.</summary>
    /// <summary>The time an earlier import was stored, which the store keeps in UTC, as the files dialog shows it: local time,
    /// dd.MM.yyyy HH:mm (the review of the Estonian texts). A text that does not read is shown as it is.</summary>
    private static string? LocalStamp(string? utc) =>
        utc is not null && DateTime.TryParseExact(utc, "yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var at)
            ? at.ToLocalTime().ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture)
            : utc;

    public static string? ReaderRefused(Exception refusal)
    {
        var subject = Core.Import.ReaderRefusal.SubjectOf(refusal);
        var detail = Core.Import.ReaderRefusal.DetailOf(refusal);
        return Core.Import.ReaderRefusal.CodeOf(refusal) switch
        {
            Core.Import.ReaderRefusal.SpreadsheetXml => "Fail on Excel 2003 XML-tabel (SpreadsheetML), mida programm ei loe. Ava see Excelis ja salvesta .xlsx-failina, siis impordi uuesti.",
            Core.Import.ReaderRefusal.WebPage => "Fail on Exceli nime all salvestatud veebileht (HTML), mitte Exceli fail. Ava see Excelis ja salvesta .xlsx-failina, siis impordi uuesti.",
            Core.Import.ReaderRefusal.UnknownFormat => "Fail ei ole Exceli fail ega tekstifail (CSV); seda ei saa importida.",
            Core.Import.ReaderRefusal.NoExcelReader => "Exceli faili ei saa lugeda: programmis puudub Exceli lugeja.",
            Core.Import.ReaderRefusal.NotExcel => $"Fail ei ole Exceli töövihik{(subject is null ? "" : $" ({subject})")}.",
            Core.Import.ReaderRefusal.ExcelUnopened => "Faili ei saanud Exceli töövihikuna avada: fail võib olla kahjustatud või parooliga kaitstud. Ava see Excelis ja salvesta uuesti .xlsx-failina.",
            Core.Import.ReaderRefusal.SheetUnreadable => $"Faili {subject ?? "?"}. lehte ei saanud lugeda (leht võib olla kahjustatud); faili ei loetud, teised failid loetakse.",
            Core.Import.ReaderRefusal.NothingRecognised => "Failis ei tuntud ära ühtegi lehte (ei TÖR väljavõtet ega pangaväljavõtet); midagi ei salvestatud. Põhjused on logis lehtede kaupa.",
            Core.Import.ReaderRefusal.SeveralEmployers => $"Failide TÖR väljavõtted nimetavad mitut tööandjat ({subject}); impordi korraga ühe tööandja failid.",
            Core.Import.ReaderRefusal.AlreadyImported => $"Sama fail on juba imporditud {LocalStamp(subject)}{(detail is null ? "" : $" nimega „{detail}“")}.",
            _ => null,
        };
    }

    /// <summary>A file of the selection that could not be opened at all (in use, no access): the system's own reason after it.</summary>
    public static string FileUnreadable(string reason) => $"Faili ei saanud avada: {reason}";

    /// <summary>An import log line of a file not imported (decision 229, the owner's option A): the refusal in Estonian when the
    /// window words it, the Core's English after it in brackets; the English alone otherwise.</summary>
    public static string ImportRefusedLine(string fileName, string? worded, string? english) =>
        worded is null ? $"{fileName}: {english}" : $"{fileName}: {worded}{(string.IsNullOrEmpty(english) ? "" : $" ({english})")}";

    /// <summary>What a file holds, in words (the import log, the files dialog's „Liik“ column).</summary>
    public static string ImportKindText(Core.Import.ImportKind? kind) => kind switch
    {
        Core.Import.ImportKind.Statement => "pangaväljavõte",
        Core.Import.ImportKind.Tor => "TÖR väljavõte",
        Core.Import.ImportKind.Mixed => "TÖR ja pangaväljavõte",
        _ => "",
    };

    /// <summary>The import log's line of a file imported (or found imported before and linked).</summary>
    public static string ImportedLine(string fileName, bool already, Core.Import.ImportKind? kind) =>
        $"{fileName}: {(already ? "juba imporditud, seotud perioodiga" : "imporditud")} ({ImportKindText(kind)})";

    /// <summary>A sheet as the import log names it: „Leht „x““, or "2. leht" for a sheet without a name.</summary>
    public static string SheetLabel(string? sheet, int sheetNo) => sheet is { Length: > 0 } ? $"Leht „{sheet}“" : $"{sheetNo}. leht";

    /// <summary>A note of the import (decision 135) in Estonian, "CODE: Leht „x“: …" with the code verbatim (decision 46), by its
    /// code and the figures it carries; null for a code (or a note without its figures) this does not know, which is shown in
    /// the Core's English as before. The Estonian alone: the headers, codes and names in it are the file's own.</summary>
    public static string? SheetNoteText(Core.Import.ImportNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        var body = note.Code switch
        {
            Core.Import.SheetNotes.Empty => "tühi leht, sealt ei loetud midagi.",
            Core.Import.SheetNotes.NoLayout => "päiserida ei vasta ühelegi väljavõtte paigutusele; lehte ei loetud.",
            Core.Import.SheetNotes.TorLacks when note.Args.Count > 0 => $"sarnaneb TÖR väljavõttega, kuid sellel puuduvad veerud {string.Join(", ", note.Args.Select(h => $"„{h}“"))}; lehte ei loetud.",
            Core.Import.SheetNotes.NearestLayout when note.Args is [var id] && note.Differences.Count > 0 =>
                $"lehte ei tuntud ära. Lähim paigutus on „{id}“: {string.Join("; ", note.Differences.Select(HeaderDifferenceText))}.",
            Core.Import.SheetNotes.Hidden => "töövihikus peidetud, loeti siiski.",
            Core.Import.SheetNotes.RowProblems when note.Count is { } rows => $"probleemiga ridu {rows} ({string.Join(", ", note.Args)}).",
            Core.Import.SheetNotes.UnclosedQuote when note.Args is [var record] => $"{record}. kirjes avatud jutumärk jääb sulgemata: kõik sellele järgnev loeti üheks väljaks.",
            _ => ImportProblemBody(note.Code, note.Args, note.RowNo),
        };
        if (body is null)
        {
            return null;
        }

        return note.SheetNo > 0 ? $"{note.Code}: {SheetLabel(note.Sheet, note.SheetNo)}: {body}" : $"{note.Code}: {Capital(body)}";
    }

    /// <summary>A note as the import log, the re-read and the simulation show it: the Estonian, or the Core's English for one not worded.</summary>
    public static string SheetNoteLine(Core.Import.ImportNote note) => SheetNoteText(note) ?? note.English;

    /// <summary>How a sheet's header row differs from the nearest layout's, in Estonian.</summary>
    public static string HeaderDifferenceText(Core.Import.HeaderDifference d) => d.Column is { } column
        ? $"{d.RowNo}. rea {column}. veerg on „{d.Found}“, paigutuses „{d.Expected}“"
        : $"{d.RowNo}. real on päiseveerge {d.Width}, paigutusel „{d.LayoutId}“ {d.LayoutWidth}";

    /// <summary>A problem of a sheet or of one of its rows in Estonian, by its code and figures; null for a code this does not
    /// know, or a problem stored before its figures were kept (its English then).</summary>
    public static string? ImportProblemText(Core.Import.ImportProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        return ImportProblemBody(problem.Code, problem.Args, problem.RowNo) is { } body ? Capital(body) : null;
    }

    private static string? ImportProblemBody(string code, IReadOnlyList<string>? args, int? rowNo) => code switch
    {
        Core.Import.ImportProblem.AboveHeader when rowNo is { } r => $"rida {r} on päiserea kohal ja näeb välja nagu makse; päiserea kohal olevaid ridu ei loeta.",
        Core.Import.ImportProblem.NoRows => "lehel ei ole ühtegi rida, kus oleks saaja nimi.",
        Core.Import.ImportProblem.BadDate when rowNo is { } r => args switch
        {
            [] => $"real {r} ei ole kuupäeva.",
            [var text] => $"rea {r} kuupäeva „{text}“ ei saanud lugeda.",
            [var header, var text] => $"rea {r} veeru „{header}“ väärtust „{text}“ ei saanud lugeda.",
            _ => null,
        },
        Core.Import.ImportProblem.NoAmount when rowNo is { } r => $"real {r} ei ole summat.",
        Core.Import.ImportProblem.BadAmount when rowNo is { } r && args is [var text] => $"rea {r} summat „{text}“ ei saanud lugeda.",
        Core.Import.ImportProblem.BadMarker when rowNo is { } r && args is [var marker, var debit, var credit] => $"rea {r} suund „{marker}“ ei ole „{debit}“ ega „{credit}“.",
        Core.Import.TorParser.NoCode when rowNo is { } r && args is [var first, var last] => $"real {r} ({first} {last}) ei ole isikukoodi.",
        Core.Import.TorParser.NoName when rowNo is { } r && args is [var personal, var which] => $"real {r} ({personal}) ei ole {(which == "first" ? "eesnime" : "perekonnanime")}.",
        Core.Import.TorParser.SeveralEmployers when args is [var list] => $"TÖR väljavõte nimetab mitut tööandjat: {list}.",
        Core.Import.TorParser.EmployerMismatch when args is [var foreign, var own] => $"TÖR väljavõte nimetab teist tööandjat ({foreign}), mitte seda ettevõtet ({own}). Kontrolli, et fail kuulub sellele ettevõttele.",
        Core.Import.TorParser.NoEmployerCode => args switch
        {
            [] => $"TÖR väljavõttes ei ole tööandja registrikoodi (veerg „{Core.Import.TorHeaders.EmployerCode}“ puudub või on tühi): väljavõtet ei saa ettevõttega võrrelda ja see imporditakse valitud ettevõttele.",
            [var rows] => $"tööandja registrikoodita ridu {rows}: neid ei saa ettevõttega võrrelda ja need loetakse ettevõtte omaks.",
            _ => null,
        },
        Core.Import.TorParser.ExtraColumns when args is [var headers] => $"lehel on veerge, mida töötamise register ei kirjuta ({headers}): see võib olla töövihiku tehtud koopia või uuem väljavõte. Kontrolli, et fail on registrist alla laaditud.",
        _ => null,
    };

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpper(text[0], System.Globalization.CultureInfo.CurrentCulture) + text[1..];

    /// <summary>A problem the run or the review names in Estonian, by its code and figures; null for a code this does not
    /// word, or a problem stored before its figures were kept (its English then). The persons grid's tooltip, the „Otsi
    /// maksed“ log and the simulation put the code before it (<see cref="ProblemLine"/>).</summary>
    public static string? MatchProblemText(MatchProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        // one row is said in the singular (the review of the Estonian texts)
        var one = problem.Subject is { } listed && !listed.Contains(',', StringComparison.Ordinal);
        var StateUnread = one
            ? "loeti programmi varasema versiooniga, mis ei lugenud tehingu olekut: rida {0} ei liigutanud raha (tühistatud, tagasi lükatud, ebaõnnestunud või ootel), kuid on loetud makseks. "
            : "loeti programmi varasema versiooniga, mis ei lugenud tehingu olekut: read {0} ei liigutanud raha (tühistatud, tagasi lükatud, ebaõnnestunud või ootel), kuid on loetud makseteks. ";
        var ByHand = one ? $"võta see rida käsitsi deklaratsioonist välja (märge „{Declare}“ maha)." : $"võta need read käsitsi deklaratsioonist välja (märge „{Declare}“ maha).";
        var a = problem.Args;
        string Span(string first, string last) => first == last ? $"rida {first}" : $"read {first}–{last}";
        return problem.Code switch
        {
            MatchProblem.StateUnread when problem.Source is { } source && problem.Subject is { } rows => $"{source} " + string.Format(System.Globalization.CultureInfo.InvariantCulture, StateUnread, rows)
                + (problem.Variant == MatchProblem.StateUnreadLocked
                    ? $"Faili ei saa uuesti lugeda (see on juba eksporditud või deklareeritud kuu failide hulgas või sellest koostatud rida on olnud serveris): {ByHand}"
                    : $"Loe fail uuesti ({Files.TrimEnd('…')} › {Reread}) või {ByHand}"),
            MatchProblem.HeaderRow when a is [var source, var row] => $"{source} [{row}]: andmete seas on päiserida; see jäeti välja.",
            MatchProblem.RowsBelowLastPayee when a is [var source, var first, var last, var count] && Whole(count) is { } n => problem.Variant == MatchProblem.NoPayeeRow
                ? $"{source} [{Span(first, last)}]: ükski rida ei nimeta saajat, seega {(n == 1 ? "jäi ainus rida" : $"jäid kõik {n} rida")} loetlemata – kontrolli paigutuse saaja veergu."
                : $"{source} [{Span(first, last)}]: viimasest saajaga reast allpool jäi {n} rida loetlemata – {(n == 1 ? "see võib olla" : "nende seas võib olla")} teenustasu või lõppsaldo; vaata väljavõtet.",
            MatchProblem.DuplicateRow when a is [var reference, var original] => $"{reference} on sama makse kui {original}; rida on loetletud, kuid arvesse ei lähe.",
            MatchProblem.NoFilters when a is [var source, var layout] => $"{source}: paigutusel „{layout}“ ei ole kategooriate filtreid; selle väljavõtte ridadele kategooriat ei määrata.",
            MatchProblem.SeveralEntries when a is [var name, var count, var rows] => $"{name}: TÖR loetleb isikut {count} korda (read {rows}).",
            MatchProblem.NoEmploymentInMonth when a is [var name, var year, var month] && Whole(year) is { } y && Whole(month) is { } m =>
                $"{name}: ükski TÖR-i töötamine ei kata kuud {m:00}.{y}.",
            MatchProblem.NoName when a is [var row] => $"TÖR-i rida {row}: eesnimi või perekonnanimi puudub; isikut ei otsitud.",
            MatchProblem.AmbiguousMask when a is [var name, var last, var first, var other, var count, var source, var rows] && Whole(count) is { } n =>
                $"{name}: otsingumaskid „{last} {first}“ võivad tähendada ka isikut {other}; väljavõttel {source} jäi {n} rida isikuta ({(n == 1 ? "rida" : "read")} {rows}).",
            MatchProblem.NameContained when a is [var source, var row, var owner, var shorter, var count] => Whole(count) == 1
                ? $"{source} [{row}] leiti isikule {owner} ja isikule {shorter}, kelle nimi sisaldub tema nimes; makse arvestati isikule {owner}."
                : $"{source} [{row}] leiti isikule {owner} ja isikutele {shorter}, kelle nimed sisalduvad tema nimes; makse arvestati isikule {owner}.",
            MatchProblem.SharedRow when a is [var source, var row, var names] => $"{source} [{row}] leiti mitmele isikule ({names}); kellelegi seda ei arvestatud – vali real isik.",
            MatchProblem.NotDone when a is [var source, var row, var payee, var amount] =>
                $"{source} [{row}]: {payee}{(Figure(amount) is { } sum ? " " + Money(sum) : "")} – tehingu oleku järgi raha ei liikunud (tühistatud, tagasi lükatud, ebaõnnestunud või ootel); rida jäeti välja, see ei ole makse.",
            MatchProblem.BadAmount when a is [var reference] => $"{reference}: summat ei saanud lugeda; makse ei lähe arvesse.",
            MatchProblem.OtherEmployer when a is [var shown, var more, var total, var employers, var code] && Whole(more) is { } extra && Whole(total) is { } all => all == 1
                ? $"TÖR-i rida {shown}: teine tööandja ({employers}), mitte ettevõte {code} – jäeti välja, see ei ole selle ettevõtte isik."
                : $"TÖR-i read {shown}{(extra > 0 ? $" ja veel {extra}" : "")}: teine tööandja ({employers}), mitte ettevõte {code} – jäeti välja, need ei ole selle ettevõtte isikud.",
            MatchProblem.NamesakeOtherCode when a is [var code] => $"Andmebaasis on sama nimega isik teise isikukoodiga ({code}), keda selle kuu TÖR ei loetle. Kui register parandas koodi, "
                + $"on sellel kaardil vaikimisi andmed, mitte teise kaardi valikud (maksuvaba, A1 tõend, {DeclarationLabel}) – võrdle kaht kaarti.",
            MatchProblem.OldReading when a is [var source] => $"{source} luges programmi varasem versioon, mis võttis kaardiväljavõtte iga summa laekumiseks: selle väljamaksed jäävad välja. "
                + $"Loe fail uuesti ({Files.TrimEnd('…')} › {Reread}).",
            MatchProblem.DecisionOnCopy when problem.Variant == MatchProblem.CopyParts && a is [var copy, var original, var state] =>
                $"{copy} on rea {original} koopia ja sellelt on makse osi jagatud; osad jäävad oma kohale – {(state == "draft" ? "jaga originaal ja eemalda koopia osad summaga 0" : "kontrolli originaali")}.",
            MatchProblem.DecisionOnCopy when a is [var copy, var original] && problem.Variant switch
                {
                    MatchProblem.CopyPastDraft => "kuu ei ole enam mustand",
                    MatchProblem.CopySplit => "sellelt on makse osi jagatud",
                    MatchProblem.CopyOriginalOwn => "originaalil on oma otsus",
                    _ => null,
                } is { } why => $"{copy} on rea {original} koopia ja sellel on otsus; {why}, seega jääb otsus oma kohale – kontrolli originaali.",
            MatchProblem.DecisionMoved when a is [var mark] => $"{mark}: koopial tehtud otsus viidi originaalreale; kontrolli seda ja kustuta siis märge veerust „{Note}“.",
            MatchProblem.IncludedWithoutRow => $"Kaardil on {DeclarationLabel} „{DeclarationInclude}“, kuid kuu TÖR isikut ei loetle ja makset ei leitud: sel kuul ei koostata midagi. "
                + $"Kui töötamine on lõppenud, vali kaardil „{DeclarationAuto}“.",
            MatchProblem.PayeeAmbiguous when a is [var reference] => $"{reference}: ettevõtte kahel isikul on saaja nimed; vali real õige isik.",
            MatchProblem.PayeeCodeElsewhere when a is [var reference] => $"{reference}: rea isikukood kuulub ettevõtte teisele isikule; rida kuulub seotud nimede järgi saajale – kontrolli mõlemat kaarti.",
            MatchProblem.PayeeHandLine when a is [var reference] => $"{reference}: isikul on sel kuul sama summaga käsitsi lisatud rida, seega rida vaikimisi ei deklareerita (märgi „{Declare}“, kui mõlemad on päris maksed).",
            MatchProblem.OutsideTorSalary when a is [var count, var sum] && Whole(count) is { } n && Figure(sum) is { } euros =>
                $"{n} palgamustriga {(n == 1 ? "makse" : "makset")} ({Money(euros)} €) isikule, keda TÖR ei loetle, jääb deklareerimata: registreeri töötamine TÖR-is või anna reale kategooria või väljamakse liik.",
            _ => null,
        };
    }

    /// <summary>A problem of the run or the review as the window shows it: its code verbatim (decision 46), then the Estonian,
    /// or the Core's English for one not worded.</summary>
    public static string ProblemLine(MatchProblem problem) => $"{problem.Code}: {MatchProblemText(problem) ?? problem.Message}";

    /// <summary>A whole number as the Core's figures carry it (invariant), or null.</summary>
    private static int? Whole(string text) => int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;

    /// <summary>A decimal figure as the Core's figures carry it (invariant), or null.</summary>
    private static decimal? Figure(string text) => decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;

    /// <summary>A refusal of a payment (the review's own and the engine's, <see cref="ReviewChecks"/>, <see cref="PaymentChecks"/>)
    /// in Estonian by its code and figures; null for a code this does not word (its English then). The check cell's tooltip
    /// and the simulation put the code before it (<see cref="RefusalLine"/>).</summary>
    public static string? RefusalText(Refusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        var et = System.Globalization.CultureInfo.GetCultureInfo("et-EE");
        var a = refusal.Args;
        string Pct(decimal percent) => percent.ToString("0.####", et) + " %";
        string Res(string residency) => residency == nameof(Core.Model.Residency.NonResident) ? "mitteresident" : "resident";
        return refusal.Code switch
        {
            ReviewChecks.ForeignCurrency when a is [var currency] => $"Summa on valuutas {currency}, mitte eurodes: deklareeri summa eurodes käsitsi lisatud real („{AddLine}“).",
            ReviewChecks.MoneyIn => "Rida on laekumine, mitte väljamakse, ja seda siin ei deklareerita; tagasi makstud palk on selle väljamakse parandus, mis tehakse e-MTA-s.",
            ReviewChecks.DuplicateRow => "Rida on varasema väljavõtte rea koopia (sama makse kaks korda): arvesse läheb originaal, see rida mitte kunagi.",
            ReviewChecks.NoPerson => "Maksel ei ole isikut: vali isik või jäta makse deklareerimata.",
            ReviewChecks.NoPayeeId => "Isikul ei ole Eesti isikukoodi ega MTA mitteresidendi koodi.",
            ReviewChecks.CodeInvalid when a is [var code] => $"Isiku kood „{code}“ ei ole Eesti isikukood (üksteist numbrit, päris sünnikuupäevaga): paranda see kaardil või kustuta see ja sisesta MTA mitteresidendi kood. "
                + "Käsitsi määratud residentsus seda ei asenda.",
            ReviewChecks.ResidencyNotChecked => $"Isiku residentsust ei ole kontrollitud – ei registrist ega käsitsi („{RunLookups}“ või kaardil „{ConfirmByHand}“).",
            ReviewChecks.NotInPeriod => "Makse kuupäev ei ole perioodi kuus; see kuulub teise kuu faili.",
            ReviewChecks.NotCounted => "Summat ei saanud lugeda või leiti reale kaks isikut; makse ei lähe arvesse.",
            ReviewChecks.TwoContracts when a is { Count: > 0 } => $"Makse päeval kehtib kaks eri liiki töötamist (TÖR {string.Join(", ", a)}): vali veerus „{PaymentType}“ makse liik või märgi kaardil „{TypeFromCard}“.",
            ReviewChecks.ForeignA1Needed when a is [var torType] =>
                $"Registri töötamise liik {torType} tähendab, et sotsiaalmaks makstakse välisriigis: märgi isiku kaardil A1 juhuks välisriigi A1 tõend (töö Eestis) koos riikidega või vali veerus „{PaymentType}“ makse liik käsitsi. Seni makset ei deklareerita.",
            ReviewChecks.NoType => "Väljamakse liik puudub: kategoorial ei ole selle isiku jaoks koodi või töötamise liik on teadmata.",
            ReviewChecks.UnknownType when a is [var type] => $"Väljamakse liiki {type} ei ole liikide tabelis.",
            ReviewChecks.OutsideTor when a is [var reason] => reason switch
            {
                nameof(OutsideTorReason.NoRegister) => OutsideTorTip(true),
                nameof(OutsideTorReason.NotListed) => OutsideTorTip(false),
                _ => null,
            },
            PaymentChecks.NegativeAmount => "Tagasimakseid (negatiivseid summasid) ei toetata.",
            PaymentChecks.ZeroNotAllowed when a is [var type] => $"Väljamakse liik {type} ei luba summat 0.",
            PaymentChecks.ThirdPillarNotComputed when a is [var type] =>
                $"Väljamakse liigil {type} on maksuvaba osa (MVT_640: kuni 15 % isikule aasta jooksul tehtud tulumaksuga maksustatud väljamaksetest, kõige rohkem 6000 € aastas), mida programm ei arvuta – "
                + $"ilma selleta tuleksid maksud liiga suured. Seda makset programm ei deklareeri: võta real märge „{Declare}“ maha "
                + "(kui programm on selle makse rea juba varem esitanud, tühistab ta selle siis T-reaga) ja deklareeri makse e-MTA-s käsitsi.",
            PaymentChecks.TypePayeeMismatch when a is [var type, var payee, var residency] =>
                $"Väljamakse liik {type} on mõeldud {(payee == nameof(Core.Model.Residency.NonResident) ? "mitteresidendile" : "residendile")}, kuid isik on {Res(residency)}: vali teise residentsuse liik või kontrolli isiku residentsust.",
            PaymentChecks.A1CountriesMissing => $"A1 tõendi korral on isiku kaardil vaja nii „{A1Country}“ kui ka „{WorkCountry}“.",
            PaymentChecks.A1CountriesInconsistent when a is [var a1] => a1 switch
            {
                nameof(Core.Model.A1Case.EstonianA1WorkAbroad) => "Eesti A1 tõendi korral peab A1 riik olema EE ja töö riik muu kui EE (KR_YLD2_09, KR_YLD2_07).",
                nameof(Core.Model.A1Case.ForeignA1WorkInEstonia) => "Välisriigi A1 tõendi korral peab A1 riik olema muu kui EE ja töö riik EE (KR_YLD2_07).",
                _ => null,
            },
            PaymentChecks.PensionNotChecked when a is [var type] => $"Liigiga {type} peetakse kinni II samba makse, kuid isiku II samba staatust ei ole kontrollitud – vajuta „{RunLookups}“ või kinnita see isiku kaardil.",
            PaymentChecks.PensionRateUnknown when a is [var type] => $"Liigiga {type} peetakse kinni II samba makse, kuid isiku II samba määr on teadmata.",
            PaymentChecks.PensionRateInvalid when a is [var rate] && Figure(rate) is { } r => $"II samba määr {Pct(r * 100m)} ei sobi: see peab olema üle 0 % ja alla 100 % (2 %, 4 % või 6 %).",
            PaymentChecks.ExemptionWithoutApplication => $"„{ExemptionMonthly}“ on täidetud, kuid isik ei ole sellele väljamaksjale maksuvaba tulu avaldust esitanud: märgi „{ExemptionApplied}“ või tühjenda summa.",
            PaymentChecks.ExemptionAboveLegal when a is [var applied, var legal] && Figure(applied) is { } given && Figure(legal) is { } most =>
                $"Avalduse maksuvaba summa ({Money(given)}) peab olema vahemikus 0 kuni {Money(most)} kuus (seaduse järgi).",
            // the sub-cent shown whole: rounded to cents it would look right
            PaymentChecks.ExemptionSubCent when a is [var applied] && Figure(applied) is { } given => $"Avalduse maksuvaba summa ({given.ToString("#,##0.00########", et)}) peab olema täissentides.",
            PaymentChecks.ExemptionNotEea when a is [var country] => $"„{ExemptionApplied}“ on märgitud, kuid kaardi maksuresidentsuse riik ({country}) on väljaspool EMP-d. "
                + "Mitteresident saab maksuvaba tulu ainult EMP riigi residendina residentsustõendiga (MTA lükkab selle tagasi, KR_MVT_06). Eemalda avalduse märge või paranda riik.",
            PaymentChecks.TreatyRateInvalid when a is [var treaty, var own] && Figure(treaty) is { } t && Figure(own) is { } o =>
                $"Kaardi „{TreatyRate}“ ({Pct(t)}) peab olema täisprotsent vahemikus 0 kuni liigi oma määr {Pct(o)}.",
            PaymentChecks.MinimumShareInvalid when a is [var share] && Figure(share) is { } s => $"SM miinimumkohustuse kuu osa ({s.ToString("0.####", et)}) peab olema üle 0 ja kuni 1.",
            PaymentChecks.WorkAbilityApplicationWithoutStatus => $"„{StatePaysSocialTax}“ on märgitud, kuid „{ReducedWorkAbility}“ ei ole: Töötukassa maksab sotsiaalmaksu ainult osalise või puuduva töövõimega isiku eest (SMS § 6 lg 1 p 5). Märgi isiku kaardil töövõime või eemalda taotluse märge.",
            PaymentChecks.RatesExceedOne => "Kinnipeetavate maksude määrad on kokku 100 % või rohkem: netosummast ei saa brutosummat arvutada.",
            _ => null,
        };
    }

    /// <summary>A refusal as the check cell's tooltip and the simulation show it: its code verbatim (decision 46), then the
    /// Estonian, or the Core's English for one not worded.</summary>
    public static string RefusalLine(Refusal refusal) => $"{refusal.Code}: {RefusalText(refusal) ?? refusal.Message}";

    /// <summary>A review refusal the card's own wording (<see cref="CardRefused"/>) does not know, in Estonian by its code
    /// and case; null for any other. LINE_MISSING says which line, as the amount's refusal does.</summary>
    public static string? ReviewRefused(ReviewException refusal) => refusal.Code switch
    {
        ReviewWorkspace.LineMissing => refusal.Variant switch
        {
            ReviewException.HandLine => HandLineMissing,
            ReviewException.Part => PartMissing,
            _ => "Seda rida ei ole enam andmebaasis – vaade laaditakse uuesti.",
        },
        _ => null,
    };

    private const string HandLineMissing = "Seda käsitsi lisatud rida ei ole enam andmebaasis – vaade laaditakse uuesti.";
    private const string PartMissing = "Seda osa ei ole enam andmebaasis – vaade laaditakse uuesti.";

    /// <summary>A refusal of the month's search („Otsi maksed“) in Estonian by its code; null for any other.</summary>
    public static string? RunRefused(Exception refusal) => PhrasesRefusal.CodeOf(refusal) switch
    {
        MatchRunner.NoFiles => "Kuuga ei ole seotud ühtegi faili: impordi enne TÖR väljavõte ja pangaväljavõtted.",
        MatchRunner.SeveralTorSheets => $"Kuu failides on {PhrasesRefusal.SubjectOf(refusal) ?? "mitu"} TÖR-i lehte, otsing kasutab ainult ühte: eemalda teised Failid-aknas kuu failide hulgast.",
        _ => null,
    };

    /// <summary>A step of a person's register check in Estonian, by its code and figures; null for one this does not word.</summary>
    public static string? LookupStep(LookupNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        return note.Code switch
        {
            LookupNote.Asked => note.Kind switch
            {
                LookupKind.Residency => "küsiti EMTA-lt residentsust",
                LookupKind.Pension => "küsiti Pensionikeskuselt II sammast",
                _ => null,
            },
            LookupNote.Reused when note.At is { } at && note.Kind is { } kind =>
                $"{(kind == LookupKind.Residency ? "residentsus" : "II sammas")}: kasutati {at.ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture)} saadud vastust",
            LookupNote.ResidencyKept => "residentsus on määratud käsitsi ja jääb",
            LookupNote.ResidencyBack => "residentsus võetakse taas registrist",
            LookupNote.ResidencySet when note.Value is { } resident => $"residentsus: {(resident ? "resident" : "mitteresident")}",
            LookupNote.ExemptionOff => "maksuvaba tulu avalduse märge eemaldati: mitteresident",
            LookupNote.PensionDerived => "II sammast ei küsitud: mitteresident",
            LookupNote.PensionBack => "II sammas võetakse taas registrist",
            LookupNote.PensionKept => "II sammas on määratud käsitsi ja jääb",
            LookupNote.PensionSet when note.Value is { } member => member ? $"II sammas: liitunud, määr {Percent(note.Rate)}" : "II sammas: ei ole liitunud",
            _ => null,
        };
    }

    /// <summary>A problem of a register check as the log shows it: its code verbatim (decision 46), then the Estonian; null
    /// for one this does not word (the Core's English then). A register's failure keeps its own detail in brackets (decision 242).</summary>
    public static string? LookupProblem(LookupNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        const string Stand = "andmed jäävad, nagu olid";
        const string HandStands = "käsitsi määratu jääb";
        string Res(bool resident) => resident ? "resident" : "mitteresident";
        string Member(bool member, decimal? rate) => member ? $"liitunud, määr {Percent(rate)}" : "ei ole liitunud";
        var failure = note.Failure is null ? "vastust ei saadud" : LookupFailure(note.Failure);
        var said = note.Code switch
        {
            LookupRunner.Skipped => note.Variant switch
            {
                LookupNote.NoCode => "kehtiv isikukood puudub, registritest ei ole midagi küsida.",
                LookupNote.RegisterNoCode when note.Name is { } name => $"{name}: isikukood puudub nii TÖR-is kui ka kaardil, registritest ei ole midagi küsida.",
                LookupNote.PaidNoCode when note.Name is { } name => $"{name}: isikule on sel kuul makstud, kuid isikukood puudub, registritest ei ole midagi küsida – määra residentsus kaardil.",
                _ => null,
            },
            LookupRunner.Failed => (note.Kind, note.Variant) switch
            {
                (LookupKind.Residency, LookupNote.FactsStand) when failure is not null => $"residentsuse päring ebaõnnestus – {failure}; {Stand}.",
                (LookupKind.Residency, LookupNote.HandUsed) when failure is not null && note.Value is { } hand =>
                    $"residentsuse päring ebaõnnestus – {failure}; kasutatakse käsitsi määratud residentsust ({Res(hand)}).",
                (LookupKind.Pension, LookupNote.FactsStand) when failure is not null => $"II samba päring ebaõnnestus – {failure}; {Stand}.",
                (LookupKind.Pension, LookupNote.MembershipUnknown) when note.Day is { } day =>
                    $"II samba päring ebaõnnestus – isik on liitunud, kuid leht ei nimeta kinnipidamise algust{(note.Ended is { } ended ? $", ainult et sissemaksed lõpevad {Day(ended)} ehk pärast {Day(day)}" : "")}: "
                    + $"ei ole teada, kas isik oli {Day(day)} liitunud; {Stand}.",
                _ => null,
            },
            LookupRunner.Disagrees => note.Kind switch
            {
                LookupKind.Residency when note.Value is { } register && note.Day is { } day && note.ByHand is { } hand =>
                    $"registri järgi on isik {Day(day)} seisuga {Res(register)}, käsitsi on määratud {Res(hand)}; {HandStands}.",
                LookupKind.Pension when note.Value is { } member && note.Day is { } day && note.ByHand is { } hand =>
                    (note.Derived == true ? "mitteresidendina ei ole liitunud (Pensionikeskuselt ei küsitud)" : $"Pensionikeskuse järgi {Day(day)} seisuga {Member(member, note.Rate)}")
                    + $", käsitsi on määratud: {Member(hand, note.ByHandRate)}; {HandStands}.",
                _ => null,
            },
            LookupRunner.RateUnknown when note.Day is { } day => $"isik on {Day(day)} seisuga liitunud, kuid ükski määra rida ei kata seda päeva; määr jääb teadmata (vaikimisi 2 % ei võeta).",
            LookupRunner.Reset => "II samba andmed tulenesid mitteresidentsusest; nüüd on isik resident ja andmed on teadmata, kuni Pensionikeskus vastab.",
            _ => null,
        };
        // the run's own problem has no code on screen, as it had none before
        return note.Code == LookupNote.NoRun ? $"Perioodi makseid ei ole otsitud („{RunMatch}“): kontrollitakse kõiki ettevõtte isikuid."
            : said is null ? null : $"{note.Code}: {said}";
    }

    /// <summary>A problem line of a register check: the Estonian of its note, or the Core's English (no note, or one not worded).</summary>
    public static string LookupProblemLine(string english, LookupNote? note) => (note is null ? null : LookupProblem(note)) ?? english;

    /// <summary>A register's failure in Estonian by its fixed prefix, the site's or the library's own detail after it in
    /// brackets (decision 242); null for a failure without a known prefix (the line keeps its English then).</summary>
    public static string? LookupFailure(string english)
    {
        ArgumentNullException.ThrowIfNull(english);
        const string Timeout = "request timeout or access denied: ";
        const string Content = "content error: ";
        return english == "no answer" ? "vastust ei saadud"
            : english == "data not found" ? "registri leht ei andnud andmeid (leht võib olla hoolduses)"
            : english == "table size mismatch" ? "registri lehe tabel oli ootamatu kujuga"
            : english.StartsWith(Timeout, StringComparison.Ordinal) ? $"päring aegus või ligipääs keelati ({english[Timeout.Length..]})"
            : english.StartsWith(Content, StringComparison.Ordinal) ? $"registri lehe vastust ei tuntud ära ({english[Content.Length..]})"
            : null;
    }

    /// <summary>A refusal of the build the main window words itself — the simulation says it as „Koosta read“ does, in
    /// Estonian (the fifth audit, item 19); null for any other.</summary>
    public static string? DeclareRefused(Exception refusal) => refusal is Core.Declare.DeclareException declare ? declare.Code switch
    {
        Core.Declare.PeriodDeclarer.RatesMissing => RatesMissingFor(declare.Subject),
        Core.Declare.PeriodDeclarer.RunStale => declare.Variant switch
        {
            "notSearched" => "Perioodi maksed on otsimata: read koostatakse alles otsingu järel. Vajuta „Otsi maksed“.",
            "changed" => "Perioodi failid muutusid pärast otsingut: read koostatakse alles uue otsingu järel. Vajuta uuesti „Otsi maksed“ (kahe TÖR-lehe korral eemalda enne Failid-aknas vana otsing ja vana TÖR).",
            _ => RunStaleRefused,
        },
        Core.Declare.PeriodDeclarer.ModeChanged => declare.Subject is not { } key ? DeclareModeRefused : declare.Variant switch
        {
            "perPayment" => $"Rida {key} on e-MTA-s ühe makse rida: kuu on deklareeritud iga makse eraldi reana. Vali Seaded menüüst sama lõige või võta enne read deklaratsioonist välja.",
            "totals" => $"Rida {key} on e-MTA-s väljamakse liigi koondrida: kuu on deklareeritud koondina liigi kaupa. Vali Seaded menüüst sama lõige või võta enne read deklaratsioonist välja.",
            _ => DeclareModeRefused,
        },
        ReviewWorkspace.PeriodNotDraft => Enum.TryParse<Core.Store.PeriodState>(declare.Subject, out var state) && Enum.IsDefined(state) && !int.TryParse(declare.Subject, out _)
            ? $"Kuu ei ole enam mustand ({PeriodState(state)}) – ridu ei saa koostada."
            : "Kuu ei ole enam mustand – ridu ei saa koostada.",
        _ => SubmitRefused(declare),
    } : null;

    /// <summary>A refusal of the export, a withdrawal, a mark or a reopen (<see cref="Core.Declare.PeriodSubmitter"/>) in
    /// Estonian by its code and case; null for a code or case this does not know, which is shown in the Core's English.</summary>
    public static string? SubmitRefused(Core.Declare.DeclareException refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        var state = StateOf(refusal.Subject);
        return refusal.Code switch
        {
            Core.Declare.PeriodSubmitter.NoDrafts => $"Perioodil ei ole mustandridu, mida eksportida. Vajuta enne eksporti „{BuildLines}“.",
            Core.Declare.PeriodSubmitter.LinesStale when refusal.Subject is { } key && refusal.Detail is { } rule =>
                $"Rida {key} ({(refusal.Section is { } s ? SectionShort(s) + ", " : "")}{(Enum.TryParse<LineAction>(refusal.Variant, out var action) && Enum.IsDefined(action) ? ActionText(action) : refusal.Variant)}) "
                + $"ei sobi enam võtme varasemate versioonidega: {LineKeyText(rule) ?? rule}. Vajuta enne eksporti uuesti „{BuildLines}“.",
            Core.Declare.PeriodSubmitter.ReasonNeeded => refusal.Variant switch
            {
                Core.Declare.PeriodSubmitter.ReasonNone => "Deklaratsioon on kinnitatud: seda muutev fail peab nimetama paranduse põhjuse (PARANDUSPOHJUS_1–4).",
                Core.Declare.PeriodSubmitter.ReasonUnknown => $"„{refusal.Subject}“ ei ole paranduse põhjuse kood (PARANDUSPOHJUS_1–4).",
                Core.Declare.PeriodSubmitter.ReasonMissing when refusal.Section is { } s =>
                    $"Osale {SectionShort(s)} ei ole paranduse põhjust valitud. Fail kannab selle osa ridu ja iga osa vajab oma põhjust.",
                Core.Declare.PeriodSubmitter.ReasonMisfit when refusal.Section is { } s && refusal.Subject is { } reason =>
                    $"Põhjus „{ReasonText(reason)}“ ei sobi osale {SectionShort(s)}. Vali sellele osale sobiv põhjus.",
                _ => null,
            },
            Core.Declare.PeriodSubmitter.InvalidFile => refusal.Variant switch
            {
                Core.Declare.PeriodSubmitter.FileStructure when refusal.Problems.Count > 0 =>
                    "XML-faili ei kirjutatud, sest kontroll leidis vead:" + Environment.NewLine
                    + string.Join(Environment.NewLine, refusal.Problems.Select(p => DeclarationProblemText(p) ?? p.Message)),
                Core.Declare.PeriodSubmitter.FileSchema when refusal.SchemaProblems.Count > 0 =>
                    "Koostatud XML-fail ei vasta MTA skeemile, seega seda ei kirjutatud. Teata sellest programmi omanikule." + Environment.NewLine
                    + string.Join(Environment.NewLine, refusal.SchemaProblems.Select(p => $"rida {p.Line}, veerg {p.Position}: {p.Detail}")),
                _ => null,
            },
            Core.Declare.PeriodSubmitter.SchemasMissing =>
                $"MTA XBRL GL skeeme ei leitud programmi kaustast{(refusal.Subject is null ? "" : $" ({refusal.Subject})")}. Faili ei saa kontrollida, seega seda ei kirjutatud. Paigalda programm uuesti või teata sellest programmi omanikule.",
            Core.Declare.PeriodSubmitter.WrongState => refusal.Variant switch
            {
                Core.Declare.PeriodSubmitter.StateWithdraw when state is { } st =>
                    $"Periood on olekus „{PeriodState(st)}“. Eksporti saab tühistada („{Withdraw}“) ainult failil, mida ei ole veel üles laadituks märgitud.",
                Core.Declare.PeriodSubmitter.StateWithdrawNotLatest when refusal.Subject is { } file =>
                    $"Tühistada saab ainult viimase faili eksporti ({file}); varasem fail jääb ajalukku.",
                Core.Declare.PeriodSubmitter.StateUpload when state is { } st =>
                    $"Periood on olekus „{PeriodState(st)}“. Üles laadituks saab märkida ainult eksporditud faili.",
                Core.Declare.PeriodSubmitter.StateConfirm when state is { } st =>
                    $"Periood on olekus „{PeriodState(st)}“. Kinnitatuks saab märkida ainult eksporditud või üles laaditud perioodi.",
                Core.Declare.PeriodSubmitter.StateReopen => "Periood on juba mustand ja muudetav; parandamiseks pole seda vaja avada.",
                Core.Declare.PeriodSubmitter.StateUndoReopen when state is { } st =>
                    $"Periood on olekus „{PeriodState(st)}“. Parandamisest saab loobuda ainult siis, kui periood on avatud parandamiseks ja sellel on kinnitus või eksporditud fail, mille juurde tagasi minna.",
                _ => null,
            },
            Core.Declare.PeriodSubmitter.LinesPending => refusal.Variant == Core.Declare.PeriodSubmitter.StateUndoReopen
                ? UndoReopenDraftsLog(refusal.DraftLines, refusal.Subject == nameof(Core.Store.PeriodState.Exported))
                : refusal.DraftLines.Count + refusal.OpenLines.Count + refusal.RefusedLines.Count + refusal.RefusedOverLines.Count > 0 ? ConfirmRefused(refusal) : null,
            _ => null,
        };

        static Core.Store.PeriodState? StateOf(string? subject) =>
            Enum.TryParse<Core.Store.PeriodState>(subject, out var state) && Enum.IsDefined(state) && !int.TryParse(subject, out _) ? state : null;
    }

    /// <summary>A problem the file's own check found (<see cref="Core.Xbrl.DeclarationValidator"/>), in Estonian by its code
    /// and figures; null for a code this does not know, which is shown in the Core's English.</summary>
    public static string? DeclarationProblemText(Core.Xbrl.DeclarationProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        var where = problem.Section is { } s ? $"{SectionShort(s)}{(problem.Key is null ? "" : $", rida {problem.Key}")}" : problem.Key is null ? "" : $"Rida {problem.Key}";
        const string CompanyCard = "Vali ettevõtte kaardil (Ettevõte → Muuda ettevõtet…) koodi õige liik; kui kood ise on vale, loo ettevõte uuesti.";
        const string MrrShape = "selles võivad olla ainult ladina tähed A–Z ja numbrid, ilma tühikute ja muude märkideta.";
        return problem.Code switch
        {
            Core.Xbrl.DeclarationValidator.PayerEmpty => "Maksja registrikood on tühi.",
            Core.Xbrl.DeclarationValidator.PayerNotRegistry => $"Maksja kood „{problem.Subject}“ on failis märgitud äriregistri koodiks (ARIREGISTRIKOOD), kuid see ei ole 8-kohaline. {CompanyCard}",
            Core.Xbrl.DeclarationValidator.PayerNotPersonal => $"Maksja kood „{problem.Subject}“ on failis märgitud isikukoodiks (ISIKUKOOD), kuid see ei ole 11-kohaline. {CompanyCard}",
            Core.Xbrl.DeclarationValidator.PayerMrrInvalid => $"Maksja kood „{problem.Subject}“ ei ole MTA mitteresidendi kood: {MrrShape} Ettevõtte koodi ei saa muuta: loo ettevõte uuesti koodiga, nagu MTA selle annab.",
            Core.Xbrl.DeclarationValidator.NoSection => "Failis ei ole ühtegi andmeosa. MTA skeem nõuab vähemalt üht osa (entryHeader).",
            Core.Xbrl.DeclarationValidator.SectionTwice => $"Osa {where} on failis kaks korda; iga osa võib failis olla ainult üks kord.",
            Core.Xbrl.DeclarationValidator.KeyEmpty => $"{where}: ühel real puudub võti (lineNumber).",
            Core.Xbrl.DeclarationValidator.KeyRepeated => $"{where}: võti kordub selles osas (KR_XBRL_VALJAMAKSED_2).",
            Core.Xbrl.DeclarationValidator.CounterInvalid => $"{where}: rea järjekorranumber (lineNumberCounter) peab olema positiivne täisarv.",
            Core.Xbrl.DeclarationValidator.TypeMissing => $"{where}: R- või M-real peab olema väljamakse liik.",
            Core.Xbrl.DeclarationValidator.TypeWrongSection when problem.OtherSection is { } right =>
                $"{where}: väljamakse liik {problem.Subject} kuulub osasse {SectionShort(right)}, mitte siia (KR_VM_05).",
            Core.Xbrl.DeclarationValidator.AmountMissing => $"{where}: R- või M-real peab olema summa (0, kui midagi ei makstud).",
            Core.Xbrl.DeclarationValidator.PayeeMissing => $"{where}: R- või M-real peab olema saaja (isikukood või MTA mitteresidendi kood).",
            Core.Xbrl.DeclarationValidator.PayeeMrrInvalid => $"{where}: „{problem.Subject}“ ei ole MTA mitteresidendi kood: {MrrShape} Paranda kood isiku kaardil.",
            Core.Xbrl.DeclarationValidator.DateMissing => $"{where}: R- või M-real peab olema väljamakse kuupäev.",
            Core.Xbrl.DeclarationValidator.DateOutsideMonth when problem.Day is { } day && problem.Month is { } month =>
                $"{where}: väljamakse kuupäev {Day(day)} jääb väljapoole faili kuud {month:MM.yyyy} (KR_XBRL_VALJAMAKSED_17).",
            _ => null,
        };
    }

    /// <summary>A refusal of the store, the feedback reader or the system on the submission and feedback path
    /// (<see cref="Core.Declare.SubmissionRefusal"/>), in Estonian by its code; null for any other. The system's own reason
    /// (a file in use, a share offline) is said after the Estonian, as Windows gives it.</summary>
    public static string? FeedbackRefused(Exception refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        var subject = Core.Declare.SubmissionRefusal.SubjectOf(refusal);
        var items = Core.Declare.SubmissionRefusal.ItemsOf(refusal);
        const string AnotherFile = "raport kuulub teisele failile. Vali saadetud failide loendist fail, millele raport vastab, ja loe tagasiside uuesti. Midagi ei salvestatud.";
        return Core.Declare.SubmissionRefusal.CodeOf(refusal) switch
        {
            Core.Declare.SubmissionRefusal.NotDraft => "Mõni rida ei ole enam mustand (teine aken võis selle juba eksportida): eksport jäi tegemata. Vaade on värskendatud; vaata read üle.",
            Core.Declare.SubmissionRefusal.WithdrawAnswered => $"Failil on juba vastus (tagasiside või käsitsi kinnitatud või tagasi lükatud rida): fail on üles laaditud ja selle eksporti ei saa tühistada. Kasuta „{MarkUploaded}“.",
            Core.Declare.SubmissionRefusal.NotPending => Enum.TryParse<PaymentStatus>(subject, out var status) && Enum.IsDefined(status) && !int.TryParse(subject, out _)
                ? $"Käsitsi saab otsustada ainult saadetud või otsust ootava versiooni üle; selle versiooni olek on „{PaymentStatusText(status)}“. Vaade on värskendatud."
                : "Käsitsi saab otsustada ainult saadetud või otsust ootava versiooni üle. Vaade on värskendatud.",
            Core.Declare.SubmissionRefusal.WriteFailed => $"XML-faili ei saanud kausta {subject} kirjutada. Kontrolli, kas kaust on kättesaadav (kas võrguketas on ühendatud), ja ekspordi uuesti. Windowsi põhjus: {refusal.Message}",
            Core.Declare.SubmissionRefusal.FileUnopened => FileUnreadable(refusal.Message),
            Core.Declare.SubmissionRefusal.Unreadable => "Faili ei saanud tagasisideraportina lugeda: see ei ole loetav XML-fail ega ZIP-arhiiv. Laadi raport e-MTA-st uuesti alla.",
            Core.Declare.SubmissionRefusal.ZipEmpty => "ZIP-arhiivis ei ole ühtegi XML-raportit. Vali e-MTA-st alla laaditud tagasisideraport.",
            Core.Declare.SubmissionRefusal.NoRoot => "Fail ei ole e-MTA tagasisideraport: XML-is puudub juurelement.",
            Core.Declare.SubmissionRefusal.NotReport => $"Fail ei ole e-MTA tagasisideraport: juurelement on <{subject}>, tagasisideraportil on see <processing>. Kas valisid kogemata eksporditud XML-faili?",
            Core.Declare.SubmissionRefusal.NoEntry => $"Tagasisideraportis (tulemus „{subject}“) ei ole ühtegi osa (entryHeader); MTA skeem nõuab vähemalt üht. Raport on poolik; laadi see e-MTA-st uuesti alla.",
            Core.Declare.SubmissionRefusal.LineNoId => "Tagasisideraportis on reaviide ilma rea võtmeta (lineInternalId); raportit ei saa ridadele rakendada.",
            Core.Declare.SubmissionRefusal.Confirmation => "See on kinnitamise raport, mitte faili tagasiside: ridade kohta see midagi ei ütle.",
            Core.Declare.SubmissionRefusal.NoLines => "Valitud failil ei ole andmebaasis ühtegi rida (või faili ei ole enam andmebaasis): raportit ei saa sellele rakendada. Vaade on värskendatud.",
            Core.Declare.SubmissionRefusal.OtherFile => $"Raport nimetab ridu, mida see fail ei kandnud ({string.Join(", ", items.Select(ReportKeyText))}): {AnotherFile}",
            Core.Declare.SubmissionRefusal.PlacedElsewhere => $"Raport paigutab read teisiti kui see fail ({string.Join("; ", items.Select(PlacedText))}): {AnotherFile}",
            _ => null,
        };

        static string PlacedText(string item) => item.Split('|') is [var key, var said, var own]
            ? $"{ReportKeyText(key)}: raportis {said}. rida, failis {own}. rida"
            : item;
    }

    /// <summary>A line a report names ("EE0201001/12") in the window's words: the key, its section after it.</summary>
    private static string ReportKeyText(string item) => item.LastIndexOf('/') is var cut and > 0
        && Enum.TryParse<Core.Model.Section>(item[..cut], out var section) && Enum.IsDefined(section) && !int.TryParse(item[..cut], out _)
        ? $"{item[(cut + 1)..]} ({SectionShort(section)})"
        : item;

    /// <summary>The year without rates, said with the year when the refusal names it.</summary>
    public static string RatesMissingFor(string? year) => year is null ? RatesMissing
        : $"Programmi selles versioonis ei ole {year}. aasta maksumäärasid ega kuumäära: kuu ridu ei saa koostada enne, kui uus versioon need toob.";

    /// <summary>A note of the build („Koosta read“, the simulation) in Estonian, "CODE: …" with the code verbatim (decision
    /// 46), by its code and case and the figures it carries; null for a code or case this does not know, which is shown in
    /// the Core's English as before. The English stays the note's own text for the tests and support.</summary>
    public static string? BuildNoteText(Core.Declare.BuildNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        var said = BuildNoteBody(note);
        return said is null ? null : $"{note.Code}: {said}";
    }

    /// <summary>A note as the log and the simulation show it: the Estonian, or the Core's English for one not worded.</summary>
    public static string BuildNoteLine(Core.Declare.BuildNote note) => BuildNoteText(note) ?? note.Text;

    private static string? BuildNoteBody(Core.Declare.BuildNote n)
    {
        const string Kaasa = $"kaardil {DeclarationLabel} „kaasa“";
        const string ZeroDraftGoes = "Nullrea mustand kustutatakse, serveris olev rida jääb.";
        const string DraftsGo = "kuu maksete mustandid kustutatakse, serveris olevad read jäävad.";
        const string NoMinimum = "selle kuu maksetelt miinimumi ei arvestata";
        string Ref(string? reference) => Core.Report.ReportText.SourceText(reference);
        string Refs(IReadOnlyList<string> references) => string.Join(", ", references.Select(r => Ref(Core.Declare.PeriodDeclarer.ShownToken(r))));
        var one = n.Items.Count == 1;
        return n.Code switch
        {
            Core.Declare.PeriodDeclarer.Included => n.Variant switch
            {
                "noRowNoPay" => $"{n.Subject}: {Kaasa}, kuid kuu TÖR-is pole isiku kannet ja makset ei leitud; isikule ridu ei koostata (varasem mustand kustutatakse ja serveris olevale reale koostatakse tühistus nagu ikka).",
                "noValid" => $"deklareeritakse nagu kehtiv töötamine ({Kaasa}); registris pole sel kuul kehtivat töötamist."
                    + (n.Count is { } paid
                        ? $" Kuul on isikule {Count(paid, "makse", "makset")}, mida ei deklareerita ({string.Join(", ", n.Items)}): anna reale kategooria või väljamakse liik"
                          + (n.AskCardType ? $" või märgi nullrea jaoks kaardile „{TorType}“ ja „{TypeFromCard}“" : "") + "."
                        : "")
                    + n.Unmet switch
                    {
                        "noTypeNoneDeclared" => $" Nullrida ega makse rida ei koostata: töötamise liik on teadmata (seda pole registris ega kaardi väljal „{TorType}“).",
                        "noType" => $" Nullrida ei koostata: töötamise liik on teadmata (seda pole registris ega kaardi väljal „{TorType}“).",
                        "typeNoMinimum" => $" Nullrida ei koostata: töötamise liik {n.TypeId} ei kanna sotsiaalmaksu miinimumi.",
                        "a1" => " Nullrida ei koostata: isiku A1 juhul Eestis sotsiaalmaksu ei maksta.",
                        _ => "",
                    },
                "paused" => $"deklareeritakse nagu kehtiv töötamine ({Kaasa}); registris on töötamine peatatud alates {Day(n.Day)} ja miinimum arvestab ka peatamise päevi.",
                _ => null,
            },
            Core.Declare.PeriodDeclarer.Excluded => n.Count is null or 0 ? "isik on deklaratsioonist välja jäetud."
                : $"isik on deklaratsioonist välja jäetud; {(n.Count == 1 ? "1 deklareerimiseks märgitud makse" : $"{n.Count} deklareerimiseks märgitud makset")} jääb deklareerimata.",
            Core.Declare.PeriodDeclarer.Refused => $"{Ref(n.Reference)}: makset ei deklareerita – ülevaatus keeldub ({string.Join(", ", n.Items)}). Selle mustand kustutatakse, serveris olev rida jääb.",
            ReviewChecks.ResidencyNotChecked when n.Variant == "zeroLine" =>
                $"miinimumi nullrida (väljamakse liik {n.PaymentType}) jääb koostamata: isiku residentsust ei ole kontrollitud (registrist ega käsitsi). {ZeroDraftGoes}",
            Core.Declare.PeriodDeclarer.MinimumHeld => n.Variant switch
            {
                "zeroLine" => $"nullrida (väljamakse liik {n.PaymentType}) ei koostata: "
                    + (one ? $"kuu makse {Refs(n.Items)} kannab miinimumi, kuid ülevaatus keeldus sellest. Miinimum ootab, kuni makse on parandatud või deklaratsioonist välja võetud; "
                        : $"kuu maksed {Refs(n.Items)} kannavad miinimumi, kuid ülevaatus keeldus neist. Miinimum ootab, kuni maksed on parandatud või deklaratsioonist välja võetud; ")
                    + "nullrea mustand kustutatakse, serveris olev rida jääb.",
                "topUp" => one
                    ? $"kuu makse {Refs(n.Items)} kannab miinimumi, kuid ülevaatus keeldus sellest; isiku teised maksed koostatakse miinimumi suurenduseta, kuni see makse on parandatud või deklaratsioonist välja võetud."
                    : $"kuu maksed {Refs(n.Items)} kannavad miinimumi, kuid ülevaatus keeldus neist; isiku teised maksed koostatakse miinimumi suurenduseta, kuni need maksed on parandatud või deklaratsioonist välja võetud.",
                _ => null,
            },
            Core.Tax.PaymentChecks.PensionNotChecked or Core.Tax.PaymentChecks.PensionRateUnknown or Core.Tax.PaymentChecks.PensionRateInvalid when n.Variant == "zeroLine" =>
                $"miinimumi nullrida (väljamakse liik {n.PaymentType}) jääb koostamata: "
                + (n.Code == Core.Tax.PaymentChecks.PensionNotChecked ? "isiku II samba staatus on kontrollimata"
                    : n.Code == Core.Tax.PaymentChecks.PensionRateUnknown ? "isiku II samba määr on teadmata" : "isiku II samba määr ei ole lubatud")
                + $" – vajuta „{RunLookups}“ või kinnita see isiku kaardil. {ZeroDraftGoes}",
            Core.Declare.PeriodDeclarer.ZeroLine => n.Variant switch
            {
                "valid" or "ended" or "paused" or "unpaidLeave" => $"kuul kehtib töötamine (TÖR liik {n.TypeId}"
                    + (n.Variant == "ended" ? $"; registris lõpetatud {Day(n.Day)}" : n.Variant == "paused" ? $"; registris peatatud alates {Day(n.Day)}"
                        : n.Variant == "unpaidLeave" ? "; registris peatatud tasustamata puhkuseks" + (n.Day is null ? "" : $" alates {Day(n.Day)}") + ", mille ajal on miinimum endiselt tasuda" : "")
                    + (n.FromCard ? "; liik kaardilt kõigile maksetele" : "")
                    + $"), kuid kuu jooksul pole miinimumi kandvat makset: miinimumi kannab nullrida väljamakse liigiga {n.PaymentType}.",
                "asIfEmployed" => $"deklareeritakse nagu kehtiv töötamine ({Kaasa}), TÖR liik {n.TypeId}; kuu jooksul pole miinimumi kandvat makset: miinimumi kannab nullrida väljamakse liigiga {n.PaymentType}.",
                _ => null,
            },
            Core.Declare.PeriodDeclarer.NoPayee => $"isikul pole isikukoodi ega MTA mitteresidendi koodi; ridu ei koostata – {DraftsGo}",
            ReviewChecks.CodeInvalid => $"isiku kood „{n.Subject}“ ei ole Eesti isikukood; selle all ridu ei koostata – mustandid kustutatakse, serveris olevad read jäävad. Paranda kood isiku kaardil.",
            Core.Declare.PeriodDeclarer.FactsOfAnotherMonth => n.Variant switch
            {
                "lookedUpFor" => $"residentsus või II sammas on viimati päritud seisuga {Day(n.Day)} (teine kuu); kui need võisid muutuda, vajuta selle kuu jaoks „{RunLookups}“.",
                "checkedOn" => $"residentsust või II sammast kontrolliti {Day(n.Day)}, enne seda kuud; kui need võisid muutuda, vajuta selle kuu jaoks „{RunLookups}“.",
                _ => null,
            },
            Core.Declare.PeriodDeclarer.FactsOfAnotherDay =>
                $"residentsus ja II sammas on päritud seisuga {Day(n.Day)}, ülevaatuse järgi on isiku kontrollpäev nüüd {Day(n.OtherDay)}; vajuta uuesti „{RunLookups}“.",
            Core.Declare.PeriodDeclarer.NoResidenceCountry =>
                $"mitteresident deklareeritakse ilma maksuresidentsuse riigita (RR_MAKSURESIDENTSUSE_RIIK); MTA võrdleb seda oma registriga (KR_ISIK_6, punane viga) – sisesta isiku kaardile „{ResidenceCountry}“, kui see on teada.",
            Core.Declare.PeriodDeclarer.UnpaidLeave =>
                "registris on töötamine peatatud tasustamata puhkuseks" + (n.Day is null ? "" : $" alates {Day(n.Day)}") + "; miinimum arvestatakse ka peatamise päevade eest.",
            Core.Declare.PeriodDeclarer.NoEmploymentInMonth => n.Variant switch
            {
                "notCovered" => $"ükski registri töötamine ei kata seda kuud; {NoMinimum}.",
                "notListed" => $"kuu TÖR-is isikut ei ole; {NoMinimum} (kui isik kuul töötas, vali kaardil {DeclarationLabel} „kaasa“).",
                "paused" => $"töötamine on kogu kuu peatatud (või register ei ütle, millest alates); {NoMinimum}. Kui peatamise põhjus on tasustamata puhkus, mida register nii ei nimeta, on miinimum siiski tasuda: vali {Kaasa}.",
                "endedNoEnd" => $"miinimumi kandev registri kanne on lõpetatud (kande olek 4), kuid lõppkuupäeva register ei anna, nii et ükski kuu päev ei lähe arvesse; {NoMinimum}.",
                "notInForce" => $"ükski miinimumi kandev registri kanne ei kehti kuul ühelgi päeval (kande olek „{n.Subject}“, mida programm ei tunne); {NoMinimum}.",
                _ => null,
            },
            Core.Declare.PeriodDeclarer.MinimumWithPension =>
                $"kaardil on märgitud „{OnPension}“ ja „{SocialTaxMinimum}“; riikliku pensioni saaja eest miinimumi ei maksta (SMS § 2 lg 4 p 6) – kui pensioni makstakse, eemalda kaardilt märge „{SocialTaxMinimum}“.",
            Core.Declare.PeriodDeclarer.WorkAbilityNotDeducted when n.Items.Count > 0 && n.Variant == "a1" =>
                $"kaardil on märgitud „{StatePaysSocialTax}“, kuid isiku Eesti A1 korral ei saa kuu {(n.Items.Count == 1 ? "makse liigiga" : "maksed liikidega")} {string.Join(", ", n.Items)} vähendust "
                + $"(Eesti A1 rida maatriksis seda ei luba), {(n.Items.Count == 1 ? "sellelt" : "neilt")} maksab tööandja sotsiaalmaksu täies ulatuses.",
            Core.Declare.PeriodDeclarer.WorkAbilityNotDeducted when n.Items.Count > 0 =>
                $"kaardil on märgitud „{StatePaysSocialTax}“, kuid kuu {(n.Items.Count == 1 ? "makse liigiga" : "maksed liikidega")} {string.Join(", ", n.Items)} ei luba vähendust: "
                + $"Töötukassa maksab sotsiaalmaksu ainult töötasult (liigid {string.Join(", ", PaymentTypes.All.Where(t => t.WorkAbilityReductionAllowed).Select(t => t.Code))}; KR_SM_05), {(n.Items.Count == 1 ? "sellelt" : "neilt")} maksab tööandja sotsiaalmaksu täies ulatuses.",
            Core.Declare.PeriodDeclarer.NotAllocated => n.Variant switch
            {
                "keptPools" => (one ? $"makse, millest ülevaatus keeldus, rida {string.Join(", ", n.Items)} on serveris ja kasutab"
                        : $"maksete, millest ülevaatus keeldus, read {string.Join(", ", n.Items)} on serveris ja kasutavad")
                    + " kuu maksuvaba tulu, miinimumi või Töötukassa makstava sotsiaalmaksu vähendust; paranda makse (või võta see deklaratsioonist välja), enne kui isiku teised maksed koostatakse. Teiste maksete mustandid kustutatakse.",
                "engine" when n.Cause is { } cause => $"kuu makseid ei saanud arvutada: {CauseText(cause)}. Ridu ei koostata – {DraftsGo}",
                _ => null,
            },
            Core.Store.LineKeyRules.KeyDead or Core.Store.LineKeyRules.CancellationPending or Core.Store.LineKeyRules.DraftPending
                or Core.Store.LineKeyRules.KeyExists or Core.Store.LineKeyRules.KeyUnknown when n.Variant == "keyRule" =>
                $"{Ref(n.Reference)}: {LineKeyText(n.Code)}.",
            Core.Declare.PeriodDeclarer.NotBuilt when n.Cause is { } cause => $"{Ref(n.Reference)}: rida ei saa koostada: {CauseText(cause)}. Selle mustand kustutatakse, serveris olev rida jääb.",
            Core.Declare.PeriodDeclarer.CancellationPending => n.Variant switch
            {
                "reconcile" => $"{Ref(n.Reference)}: rea {n.Key} tühistus ootab MTA vastust; uus rida koostatakse, kui vastus on käes.",
                "retire" => $"{Ref(n.Reference)}: rea {n.Key} tühistus ootab MTA vastust.",
                _ => null,
            },
            Core.Declare.PeriodDeclarer.SectionChanged when n.Section is { } section =>
                $"{Ref(n.Reference)}: väljamakse liik kuulub nüüd osasse {SectionShort(section)}; rida {n.Key} tühistatakse enne (T), uus rida koostatakse, kui MTA tühistuse vastu võtab.",
            _ => null,
        };
    }

    /// <summary>A line's note in the build's log: its own code verbatim, the two notes in words in Estonian.</summary>
    public static string? LineNoteText(string? note) => note switch
    {
        Core.Declare.BuiltLine.NothingToCancel => "serveris pole midagi tühistada",
        Core.Declare.BuiltLine.CancellationDrafted => "tühistus on koostatud",
        _ => note,
    };

    /// <summary>The key's rule a line broke (<see cref="Core.Store.LineKeyRules"/>), in words without a full stop; null for another code.</summary>
    public static string? LineKeyText(string code) => code switch
    {
        Core.Store.LineKeyRules.KeyDead => "võti on tühistatud (T vastu võetud) ega ole enam kasutatav – makse vajab uut võtit ja uut rida (R)",
        Core.Store.LineKeyRules.CancellationPending => "selle võtme tühistus ootab MTA vastust; märgi enne MTA vastus",
        Core.Store.LineKeyRules.DraftPending => "selle võtme mustand ootab saatmist; muuda või kustuta see, teist mustandit ei lisata",
        Core.Store.LineKeyRules.KeyExists => "võti on juba serveris; uus R lükataks tagasi (KR_XBRL_VALJAMAKSED_4) – saada muudatus (M)",
        Core.Store.LineKeyRules.KeyUnknown => "võtit pole serveris; M või T lükataks tagasi (M puhul KR_XBRL_VALJAMAKSED_5, T puhul _6) – saada uue reana (R)",
        _ => null,
    };

    /// <summary>The engine's, the line factory's or the store's refusal met by a build (<see cref="Core.Tax.TaxRefusal"/>), in
    /// words without a full stop; null for one without a known code.</summary>
    public static string? TaxRefusedText(Exception refusal)
    {
        var subject = Core.Tax.TaxRefusal.SubjectOf(refusal);
        var figure = Core.Tax.TaxRefusal.FigureOf(refusal);
        var other = Core.Tax.TaxRefusal.OtherOf(refusal);
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        return Core.Tax.TaxRefusal.CodeOf(refusal) switch
        {
            Core.Tax.PaymentChecks.ZeroNotAllowed => $"väljamakse liik {subject} ei luba summat 0",
            Core.Tax.PaymentChecks.ExemptionWithoutApplication => $"„{ExemptionMonthly}“ on antud, kuid „{ExemptionApplied}“ ei ole märgitud – märgi avaldus või tühjenda summa",
            Core.Tax.PaymentChecks.ExemptionAboveLegal => $"avalduse maksuvaba summa ({Money(figure ?? 0m)}) peab olema 0 kuni seaduse järgi {Money(other ?? 0m)} kuus (KR_KOOND_02/03)",
            Core.Tax.PaymentChecks.ExemptionSubCent => $"avalduse maksuvaba summa ({figure?.ToString(culture)}) peab olema täissentides",
            Core.Tax.PaymentChecks.MinimumShareInvalid => $"miinimumkohustuse kuu osa ({figure?.ToString("0.####", culture)}) peab olema üle 0 ja kuni 1",
            Core.Tax.PaymentChecks.WorkAbilityApplicationWithoutStatus => $"„{StatePaysSocialTax}“ on märgitud, kuid „{ReducedWorkAbility}“ ei ole – Töötukassa maksab sotsiaalmaksu ainult osalise või puuduva töövõimega isiku eest (SMS § 6 lg 1 p 5)",
            Core.Tax.PaymentChecks.PensionNotChecked => $"väljamakse liik {subject} peab kinni kogumispensioni, kuid isiku II samba staatus on kontrollimata",
            Core.Tax.PaymentChecks.PensionRateUnknown => $"väljamakse liik {subject} peab kinni kogumispensioni, kuid isiku II samba määr on teadmata",
            Core.Tax.PaymentChecks.PensionRateInvalid => $"II samba määr {(figure is { } rate ? (rate * 100m).ToString("0.####", culture) + " %" : "")} ei ole lubatud (peab olema üle 0 % ja alla 100 %)",
            Core.Tax.PaymentChecks.ThirdPillarNotComputed => $"väljamakse liigi {subject} maksuvaba osa (MVT_640) programm ei arvuta; võta makselt märge „{Declare}“ maha ja deklareeri see e-MTA-s käsitsi",
            Core.Tax.PaymentChecks.TypePayeeMismatch => Core.Tax.TaxRefusal.DetailOf(refusal) == nameof(Core.Model.Residency.NonResident)
                ? $"väljamakse liik {subject} on residendile, isik on mitteresident"
                : $"väljamakse liik {subject} on mitteresidendile, isik on resident",
            Core.Tax.PaymentChecks.A1CountriesMissing => $"A1 juhul on vaja nii „{A1Country}“ kui ka „{WorkCountry}“ (isiku kaardil)",
            Core.Tax.PaymentChecks.A1CountriesInconsistent => "A1 juht ja selle riigid isiku kaardil ei sobi kokku (KR_YLD2_07/09)",
            Core.Tax.PaymentChecks.NegativeAmount => "tagasimakseid (negatiivseid summasid) ei toetata",
            Core.Tax.TaxRefusal.CalcDateAfterPayment => "arvestuse kuupäev (TSD01) ei tohi olla hilisem kui väljamakse kuupäev (KR_YLD_25)",
            Core.Tax.TaxRefusal.RowTaken => subject?.Split(' ') is [var section, var key] && Enum.TryParse<Core.Model.Section>(section, out var s)
                ? $"pangarida on juba rea {key} ({SectionShort(s)}) allikas; üht pangarida deklareeritakse üks kord"
                : $"pangarida on juba {(subject is null ? "teise rea" : $"rea {subject}")} allikas; üht pangarida deklareeritakse üks kord",
            Core.Tax.TaxRefusal.KeyOtherRow => "võti on juba seotud teise pangareaga; võtme kõik versioonid jäävad sama rea juurde",
            Core.Tax.TaxRefusal.KeyOtherLine => "võti on juba seotud teise käsitsi lisatud reaga; võtme kõik versioonid jäävad sama rea juurde",
            Core.Tax.TaxRefusal.RowOtherCompany => "pangarida kuulub teise ettevõtte failile",
            Core.Tax.TaxRefusal.RowNotPayment => $"pangarida ei ole makserida ({SkipText(subject)}); sellest ei saa deklareeritud rida koostada",
            Core.Tax.TaxRefusal.NotDraft => Enum.TryParse<PaymentStatus>(subject, out var status) && Enum.IsDefined(status) && !int.TryParse(subject, out _)
                ? $"ainult mustandit saab asendada, see versioon on {PaymentStatusText(status)} (programmi viga)"
                : "ainult mustandit saab kustutada, saadetud versioon jääb ajalukku (programmi viga)",
            _ => null,
        };

        static string SkipText(string? skip) => skip switch
        {
            Core.Import.SkipReason.Blank => "tühi rida",
            Core.Import.SkipReason.Header => "päiserea koopia",
            Core.Import.SkipReason.NotDone => "tehing jäi tegemata",
            _ => skip ?? "?",
        };
    }

    /// <summary>The refusal behind a build's note in words: by its code, else „programmi viga“ with the Core's English in
    /// brackets for support (decision 242's way), an argument's parameter name left out.</summary>
    public static string CauseText(Exception cause)
    {
        ArgumentNullException.ThrowIfNull(cause);
        if ((cause is Core.Store.LineKeyException key ? LineKeyText(key.Code) : TaxRefusedText(cause)) is { } said)
        {
            return said;
        }

        var english = cause.Message;
        if (cause is ArgumentException { ParamName: { } name } && english.EndsWith($" (Parameter '{name}')", StringComparison.Ordinal))
        {
            english = english[..^$" (Parameter '{name}')".Length];
        }

        return $"programmi viga ({english.TrimEnd('.')})";
    }

    /// <summary>A refusal met by the build that escaped it (a key's rule, the store's guard): a sentence; null for any other.</summary>
    public static string? BuildRefused(Exception refusal)
    {
        var said = refusal is Core.Store.LineKeyException key ? LineKeyText(key.Code) : TaxRefusedText(refusal);
        return said is null ? null : char.ToUpper(said[0], System.Globalization.CultureInfo.CurrentCulture) + said[1..] + ".";
    }

    /// <summary>A refusal of the category phrases' file, in Estonian by its code (decision 202); null for any other.</summary>
    public static string? PhrasesRefused(Exception refusal)
    {
        var subject = PhrasesRefusal.SubjectOf(refusal);
        var said = PhrasesRefusal.CodeOf(refusal) switch
        {
            PhrasesRefusal.NotJson => "Fail ei ole loetav JSON.",
            PhrasesRefusal.NotPhrases => $"Fail ei ole kategooriate fraaside fail (\"kind\": \"{MatchCatalog.PhrasesKind}\" ja \"sets\").",
            PhrasesRefusal.UnknownSet => $"Failis on filtrikomplekt „{subject}“, mida rakendus ei tunne. Komplektid on sisseehitatud; alustage salvestatud failist.",
            PhrasesRefusal.UnknownCategory => $"Failis on kategooria, mida rakendus ei tunne ({subject}). Kategooriad on sisseehitatud; nime saab muuta ainult fraasides.",
            // the pattern's problem worded here from its kind, never the Core's English (the fifth audit, item 27)
            PhrasesRefusal.BadCell when subject is not null && PhrasesRefusal.ProblemOf(refusal) is { } problem => $"Fraasid ei ole loetavad ({subject}): {PatternProblemText(problem)}",
            PhrasesRefusal.BadCell => "Fraasid ei ole koos sisseehitatud kataloogiga kasutatavad.",
            _ => null,
        };
        return said is null ? null : said + " Midagi ei salvestatud.";
    }

    public static string PatternProblemText(PatternProblem problem) => problem.Kind switch
    {
        PatternProblemKind.Unclosed => $"mustris „{problem.Pattern}“ jääb {problem.Position}. märgil olev „[“ sulgemata.",
        _ => $"mustris „{problem.Pattern}“ on vahemik {problem.Range} kirjutatud tagurpidi (suurem märk enne väiksemat).",
    };

    public const string Phrases = "Kategooriate fraasid…";
    public const string PhrasesTitle = "Kategooriate fraasid";
    public const string PhrasesSet = "Filtrikomplekt";
    public const string PhrasesCategory = "Kategooria";
    public const string PhrasesInclude = "Sisaldab";
    public const string PhrasesExclude = "Välja arvatud";
    public const string PhrasesImport = "Impordi failist…";
    public const string PhrasesSave = "Salvesta faili…";
    public const string PhrasesReset = "Taasta sisseehitatud";
    public const string PhrasesFilter = "Fraaside fail (*.json)|*.json|Kõik failid (*.*)|*.*";
    public const string PhrasesBuiltIn = "Kehtivad sisseehitatud fraasid.";
    public const string PhrasesOwn = "Kehtivad omad fraasid (imporditud failist).";
    // the month's search named by the command's own name, so it can be found (the fifth audit, item 18)
    public const string PhrasesResetConfirm = $"Taastada sisseehitatud fraasid? Omad fraasid kustutatakse; kuu otsing kasutab neid järgmisel „{RunMatch}“ käivitamisel.";
    public static string PhrasesSaved(string path) => $"{PhrasesTitle}: salvestatud {path}";
    public static string PhrasesImported(int changed) => $"{PhrasesTitle}: imporditud, muudetud lahtreid {changed}. Kuu otsing kasutab uusi fraase järgmisel „{RunMatch}“ käivitamisel.";
    public const string PhrasesResetDone = $"{PhrasesTitle}: taastatud sisseehitatud fraasid. Kuu otsing kasutab neid järgmisel „{RunMatch}“ käivitamisel.";
    public const string CutChangedSinceBuild = "Deklaratsiooni lõige on pärast „Koosta read“ muudetud (Seaded): mustandid on teises lõikes. Eksportida need siiski? (Muidu vajuta uuesti „Koosta read“.)";
    public const string RunStaleReport = "Perioodi maksed on otsimata või failid muutusid pärast otsingut: aruanne ei sisalda kõiki makseid. Koostada aruanne siiski?";
    public static string HistoryRejectedBy(int version) => $"tagasi lükatud: hilisem versioon v{version} sai KR_XBRL_VALJAMAKSED_5 või _6 (võti ei ole serveris)";
    public const string ReviewChangedSinceBuild ="Ülevaatus on pärast „Koosta read“ muutunud ja read ei kajasta seda. Eksportida siiski? (Muidu vajuta uuesti „Koosta read“.)";
    public const string ReviewNotChecked = "Ei saanud kontrollida, kas read vastavad ülevaatusele.";
    public static string MarkedButLeftOutAtExport(int payments, decimal sum) => $"Deklareerimiseks märgitud makseid, mida read ei sisalda: {payments}, kokku {Money(sum)}. Ülevaatus keeldus neist või hoiab neid sama liigi keeldutud makse tõttu kinni (vt veerg „{Undeclared}“ ja logi); fail läheb ilma nendeta. Eksportida siiski? (Muidu paranda need ja vajuta uuesti „{BuildLines}“.)";
    public static string MarkedButLeftOutLog(int payments, decimal sum) => $"{Count(payments, "märgitud makse", "märgitud makset")} (kokku {Money(sum)}) ei ole ridades";
    public const string ReopenConfirm = "Avada periood parandamiseks? Ülevaatus muutub taas muudetavaks; järgmine eksport kannab muudatused (M) ja tühistused (T) võtmetele, mida e-MTA juba teab. e-MTA-s tuleb deklaratsioon samuti muudatusteks avada. (Kui faili ei laaditudki üles, kasuta „Tühista eksport“.)";
    public const string ApplyReportTo = "Rakendada raport failile";
    public const string ResolveAcceptedConfirm = "Märkida rida e-MTA-s vastu võetuks (sinu sõna, tagasisideraportita)?";
    public const string ResolveRejectedConfirm = "Märkida rida e-MTA-s tagasi lükatuks (sinu sõna)?";
    public const string CorrectionReasonTitle = "Paranduse põhjus";
    public const string CorrectionReasonPrompt = "Deklaratsioon on kinnitatud; muutev fail nimetab põhjuse (PARANDUSPOHJUS).";
    public const string CorrectionReasonPromptPerSection = "Deklaratsioon on kinnitatud; muutev fail nimetab põhjuse (PARANDUSPOHJUS) iga osa kohta, mille ridu ta kannab.";

    /// <summary>A declaration section in few words, for a field's label.</summary>
    public static string SectionShort(Core.Model.Section section) => section switch
    {
        Core.Model.Section.EE0201001 => "Töötasud (01)",
        Core.Model.Section.EE0201002 => "Hüvitised jm (02)",
        _ => section.ToString(),
    };
    public const string ChecklistTitle = "Fail on valmis — üleslaadimine e-MTA-s";
    public const string OpenFolder = "Ava kaust";
    public const string ChecklistSteps =
        "1. Logi e-MTA-sse isikuna, kellel on kliendi esindusõigus (pakett I või õigus „Väljamakseandmete esitamine“).\n" +
        "2. Ava väljamaksete vaade ja vali SAMA maksustamisperiood, mis failil (KR_VALIDATE_XBRL_FILE_8).\n" +
        "3. Kui deklaratsioon on juba kinnitatud, ava see e-MTA-s enne muudatusteks (muidu lükatakse fail tagasi).\n" +
        "4. Laadi XBRL GL fail üles; e-MTA arvutab maksud ja näitab vead pärast töötlemist.\n" +
        "5. Siin: „Märgi üles laaditud“. Kui raport on allalaaditav, „Loe tagasiside…“; muidu iga rea kohta „Kinnita rida käsitsi“ / „Lükka rida tagasi“.\n" +
        "6. Kinnita deklaratsioon e-MTA-s tervikuna koos lisadega (selleks on vaja paketti I või õigust „TSD esitamine“), siis siin „Märgi kinnitatuks“.\n" +
        "Tähtaeg: väljamaksekuule järgneva kuu 10. kuupäev.";

    public static string ChangeText(Core.Declare.LineChange change) => change switch
    {
        Core.Declare.LineChange.New => "uus",
        Core.Declare.LineChange.Replaced => "asendatud",
        Core.Declare.LineChange.Changed => "muudatus",
        Core.Declare.LineChange.Cancelled => "tühistus",
        Core.Declare.LineChange.Kept => "jäi",
        Core.Declare.LineChange.Deleted => "kustutatud",
        _ => change.ToString(),
    };
    public const string Rules = "Reeglite tabel…";
    public static string RulesTitle(int year) => $"Reeglite tabel {year}: väljamakse liigid ja maksud";
    public const string RulesYearMissing = "Selle aasta määrasid rakendus ei tunne; tabel näitab 2026. aasta omi.";
    public const string RulesSearch = "Otsi (kood, nimetus, TÖR liik):";
    public const string RulesOnlyCase = "Ainult selle residentsuse liigid";
    public const string RulesPensionMember = "II samba liige";
    public const string RulesPensionAge = "Pensioniea aasta";
    public const string RulesA1None = "A1 puudub";
    public const string RulesA1Estonian = "Eesti A1 (töö välismaal)";
    public const string RulesA1Foreign = "Välisriigi A1 (töö Eestis)";
    public const string RulesTreaty = "Lepingumäär, %:";
    public const string RulesCode = "Kood";
    public const string RulesName = "Nimetus";
    public const string RulesSection = "Osa";
    public const string RulesPayee = "Saaja";
    public const string RulesTor = "TÖR liik";
    public const string RulesIncomeTax = "Tulumaks";
    public const string RulesExemption = "Maksuvaba";
    public const string RulesSocialTax = "Sotsiaalmaks";
    public const string RulesMinimum = "SM miinimum";
    public const string RulesWorkAbility = "SM vähendus (O)";
    public static string RulesWorkAbilityApplied(decimal kuumaar) => $"kuni {kuumaar.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("et-EE"))} €";
    public const string RulesUnemploymentEmployee = "TK kindlustatu";
    public const string RulesUnemploymentEmployer = "TK tööandja";
    public const string RulesFundedPension = "II sammas";
    public const string RulesZero = "Null lubatud";
    public const string RulesTreatyAllowed = "Lepingumäär";
    public const string RulesThirdPillar = "MVT_640";
    public const string RulesNone = "—";
    public const string RulesTreatyRefused = "keelatud lepingumäär";
    public const string RulesTreatyRefusedTip = "Lepingumäär peab olema täisprotsent 0-st kuni liigi oma määrani; muu määraga makse arvutus keelab (TREATY_RATE_INVALID).";
    public const string RulesAllowed = "lubatud";
    public const string RulesNotComputed = "ei arvutata";
    public const string RulesOtherPayee = "Teise residentsusega saaja kood: arvutus keelab selle valitud isikule (TYPE_PAYEE_MISMATCH).";
    public const string RulesRatesTitle = "Määrad ja summad";
    public const string RulesRateName = "Maks või summa";
    public const string RulesRateValue = "Kehtib";
    public const string RulesRateField = "TSD väli";
    public const string RulesNotesTitle = "Muud tingimused";
    public static string RulesShown(int shown, int all) => $"Näidatud {shown} liiki {all}-st.";

    /// <summary>The rates and amounts in force of a year, worded: name, figure, the TSD field (decision 203).</summary>
    public static IReadOnlyList<(string Name, string Value, string Field)> RatesInForce(TaxRates rates)
    {
        var et = System.Globalization.CultureInfo.GetCultureInfo("et-EE");
        string Percent(decimal fraction) => (fraction * 100m).ToString("0.##", et) + " %";
        string Euro(decimal amount) => amount.ToString("#,##0.00", et) + " €";
        var own = PaymentTypes.All.Where(t => t.IncomeTaxRate is { } r && r != rates.IncomeTax).GroupBy(t => t.IncomeTaxRate!.Value)
            .Select(g => $"{Percent(g.Key)} liigil {string.Join(", ", g.Select(t => t.Code))}").ToList();
        var minimum = Math.Round(rates.SocialTaxMinimumBase * rates.SocialTax, 2, MidpointRounding.AwayFromZero);
        return
        [
            ("Tulumaks", Percent(rates.IncomeTax) + (own.Count > 0 ? "; " + string.Join("; ", own) : ""), "TSD_TM"),
            ("Sotsiaalmaks", Percent(rates.SocialTax), "TSD_SM"),
            ("Töötuskindlustusmakse, kindlustatu", Percent(rates.UnemploymentEmployee), "TSD_TK"),
            ("Töötuskindlustusmakse, tööandja", Percent(rates.UnemploymentEmployer), "TSD_TTK"),
            ("Kohustuslik kogumispension (II sammas)", "2 %, 4 % või 6 % (isiku oma määr)", "TSD_KP"),
            ("Sotsiaalmaksu kuumäär (miinimumi alus)", Euro(rates.SocialTaxMinimumBase) + " kuus", "SM_KUUMAAR"),
            ("Sotsiaalmaksu miinimum", $"{Euro(minimum)} kuus ({Euro(rates.SocialTaxMinimumBase)} × {Percent(rates.SocialTax)}); kuu osa = päevad / 30; osalise või puuduva töövõimega isikul ei ole", "TSD_KUUMSUURENDUS"),
            ("Töötukassa makstav sotsiaalmaks (osaline või puuduv töövõime, taotlus esitatud)", $"kuni {Euro(rates.SocialTaxMinimumBase)} kuus töötasult, kuupäevade järjekorras; tööandja maksab ülejäänult", "TSD_OPTV_SM_VAHENDUS"),
            ("Maksuvaba tulu", Euro(rates.BasicExemptionMonth) + " kuus", "MVT_610"),
            ("Maksuvaba tulu pensioniea aastal", Euro(rates.PensionAgeExemptionMonth) + " kuus", "MVT_650"),
        ];
    }

    /// <summary>The conditions the matrix's toggles do not show, in words (decision 203).</summary>
    public static IReadOnlyList<string> RulesNotes() =>
    [
        $"SM miinimum kehtib TÖR liikidel {string.Join(", ", EmploymentTypes.All.Where(e => e.SocialTaxMinimum).Select(e => $"{e.TorTypeId} {e.Name}"))}; kuu osa on päevad / 30. Kui isik on kuu registris puudu, miinimumi ei ole (NO_EMPLOYMENT_IN_MONTH).",
        $"SM miinimum ei kehti SMS § 2 lg 4 juhtudel (riikliku pensioni saaja, osalise või puuduva töövõimega isik, alla 3-aastase lapse või kolme alla 19-aastase lapse vanem, õpilane või üliõpilane jt) ega teisel tööandjal, kui miinimumi kannab maksuvaba tulu arvestav tööandja; märge on isiku kaardil. Märge „{ReducedWorkAbility}“ võtab miinimumi maha ise.",
        $"Osalise või puuduva töövõimega isiku eest maksab Töötukassa sotsiaalmaksu kuni ühe kuumäära ulatuses kuus, kui tööandja on esitanud taotluse (SMS § 6 lg 1 p 5, lg 3; kaardil „{StatePaysSocialTax}“): see osa on TSD_OPTV_SM_VAHENDUS ja SM arvutatakse ülejäänult. "
            + $"Vähendus on lubatud ainult töötasu liikidel (maatriksi veerg O: {string.Join(", ", PaymentTypes.All.Where(t => t.WorkAbilityReductionAllowed).Select(t => t.Code))}; KR_SM_05); kuumäär jagatakse kuu maksete vahel kuupäevade järjekorras, iga makse kuni oma SM aluseni. "
            + "Puue üksi vähendust ei anna. MTA võtab vähenduse vastu ainult siis, kui Töötukassa on isiku sama või eelmise kuu deklaratsioonis deklareerinud (KR_SM_03); programm seda kontrollida ei saa. Töötuskindlustusmakset, II sammast, tulumaksu ja maksuvaba tulu märge ei muuda.",
        "Mitteresident saab maksuvaba tulu ainult EMP riigi residendina residentsustõendiga (KR_MVT_06/07); kaardil EMP-väline riik keelab selle (EXEMPTION_NOT_EEA).",
        "Maksuvaba tulu ei ületa makset, millest on maha arvatud II sammas ja töötuskindlustusmakse (KR_MVT_08); kuu maksuvaba jagatakse isiku maksete vahel.",
        "Vanadus- või paindliku vanaduspensioni saajalt kindlustatu töötuskindlustusmakset ei peeta kinni (KR_TK_2), samuti pensioniea saabumise kuule järgnevast kuust (TKindlS § 42 lg 1 p 1; kuu isikukoodi sünniaja ja kaardi pensioniea aasta järgi).",
        "Välisriigi A1 tõendiga töötaja Eestis: sotsiaalmaks, töötuskindlustusmakse ja II sammas puuduvad. Eesti A1 tõendiga töö välismaal: tulumaks puudub. A1 mõjub ainult liikidel, millel on A1 variant.",
        $"Lepingumäär asendab tulumaksu määra ainult mitteresidendile residentsustõendiga, liikidel {string.Join(", ", PaymentTypes.All.Where(t => t.TreatyRateAllowed).Select(t => t.Code))}; täisprotsent, mitte üle liigi oma määra (TREATY_RATE_INVALID).",
        $"MVT_640 (tööandja III samba sissemakse maksuvaba osa: kuni 15 % aasta maksustatud väljamaksetest, kõige rohkem 6000 € aastas) – programm seda ei arvuta; liiki {string.Join(", ", PaymentTypes.All.Where(t => t.ThirdPillarExemptionAllowed).Select(t => t.Code))} ei deklareerita (THIRD_PILLAR_NOT_COMPUTED): makselt võetakse märge „{Declare}“ maha ja see deklareeritakse e-MTA-s käsitsi.",
        $"TÖR liik 504 (sotsiaalmaks välisriigis) annab palgale liigi {EmploymentTypes.DefaultCode("504", Core.Model.Residency.Resident)}/{EmploymentTypes.DefaultCode("504", Core.Model.Residency.NonResident)} ainult siis, kui kaardil on välisriigi A1 tõend (töö Eestis); muidu makset vaikimisi ei deklareerita (FOREIGN_A1_NEEDED).",
        "Sotsiaalmaksu kuumäär 886 € on töövihiku ja MTA kalkulaatori oma; MTA ärinäide kasutab 820 €, küsimus on lahtine.",
    ];

    public const string Simulation = "Simulatsioon…";
    public const string SimTitle = "Simulatsioon: näidisisiku deklaratsioon";
    public const string SimMonth = "Kuu:";
    public const string SimRegisterLabel = "TÖR:";
    public const string SimCut = "Lõige:";
    public const string SimListed = "registris, allolevate töötamistega";
    public const string SimNotListed = "kuu registris isikut ei ole";
    public const string SimNoRegister = "kuul registrit ei ole";
    public const string SimCardTitle = "Isiku kaart";
    public const string SimFirstName = "Eesnimi";
    public const string SimLastName = "Perenimi";
    public const string SimCode = "Isikukood";
    public const string SimMrr = "MRR kood";
    public const string SimCountry = "Residentsuse riik";
    public const string SimUnchecked = "kontrollimata";
    public const string SimPensionAgeYear = "Pensioniea aasta";
    public const string SimA1Country = "A1 riik";
    public const string SimWorkCountry = "Töö riik";
    public const string SimCardType = "Kaardi TÖR liik";
    public const string SimNoCardType = "(puudub)";
    public const string SimTypeFromCard = "Kaardi töötamise liik kõigile maksetele";
    public const string SimEmploymentsTitle = "Töötamised registris";
    public const string SimStart = "Algus";
    public const string SimEnd = "Lõpp";
    public const string SimPauseStart = "Peatamise algus";
    public const string SimPauseEnd = "Peatamise lõpp";
    public const string SimRowsTitle = "Pangaread (väljamaksed)";
    public const string SimDate = "Kuupäev";
    public const string SimPayee = "Saaja";
    public const string SimDescription = "Selgitus";
    public const string SimCurrency = "Valuuta";
    public const string SimMoneyIn = "Laekumine";
    public const string SimBySearch = "(otsingu järgi)";
    public const string SimTypeCode = "Liik";
    public const string SimDeclare = "Deklareeri";
    public const string SimRun = "Käivita";
    public const string SimOpen = "Ava…";
    public const string SimSave = "Salvesta…";
    public const string SimLinesTab = "Read";
    public const string SimReviewTab = "Ülevaatus";
    public const string SimXmlTab = "XML";
    public const string SimLineKey = "Rida";
    public const string SimAction = "Toiming";
    public const string SimNet = "Neto";
    public const string SimGross = "Bruto";
    public const string SimIncrease = "SM lisa";
    public const string SimReduction = "SM vähendus";
    public const string SimSource = "Allikas";
    public const string SimNameMissing = "Isiku ees- ja perenimi on vajalikud.";
    public const string SimNoRows = "Lisa vähemalt üks pangarida.";
    public const string SimNotAScenario = "Fail ei ole selle rakenduse salvestatud simulatsioon.";
    public const string SimFilter = "Simulatsioon (*.json)|*.json|Kõik failid (*.*)|*.*";
    public static string SimNotANumber(string label, string text) => $"{label}: „{text}“ ei ole arv.";
    public static string SimNotADay(string label, string text) => $"{label}: „{text}“ ei ole kuupäev (pp.kk.aaaa).";
    public static string SimEmploymentIncomplete(int row) => $"Töötamise rida {row}: liik ja algus on vajalikud.";
    public static string SimRowIncomplete(int row) => $"Pangarida {row}: kuupäev ja summa on vajalikud.";
    public const string SimTreaty = "Lepingumäär, %";
    public const string SimPensionYearInvalid = "Pensioniea aasta peab olema aastaarv (1900–2200).";
    public const string SimNoLines = "Isikule ei koostatud ühtegi rida: faili ei ole, mida kirjutada.";
    public const string SimOwnPhrases = "Kategooriad otsiti omade fraasidega (imporditud failist).";
    public const string SimBuiltInPhrases = "Kategooriad otsiti sisseehitatud fraasidega.";
    public static string SimImportRefused(string reason) => "Näidist ei saanud lugeda: " + reason;
    public const string SimImportUnread = "Näidist ei saanud lugeda.";

    /// <summary>Why the simulation stopped before the build, worded as the main window words the same refusal; null when it
    /// did not stop. The import's refusal is worded as the import log words it (its code known), else in its English.</summary>
    public static string? SimStop(SimResult result) =>
        result.ImportStopped
            ? (result.RefusalError is { } ie ? SimImportRefused(Worded(ie) ?? ie.Message) : result.Refusal is { } said ? SimImportRefused(said) : SimImportUnread)
        : result.RefusalError is { } e ? Worded(e) ?? e.Message
        : result.Refusal is { } reason ? SimImportRefused(reason)
        : null;

    /// <summary>The XML tab's text: the file, or why there is none.</summary>
    public static string SimXmlText(SimResult result) =>
        result.Xml
        ?? (result.XmlRefusalError is { } e ? Worded(e) ?? e.Message : null)
        ?? SimStop(result)
        ?? SimNoLines;

    /// <summary>The month's TSD_OPTV_SM_VAHENDUS in words, as the simulation and the build's log say it: the kuumäär the
    /// state pays the social tax on, and how much of it the month's wages took.</summary>
    public static string WorkAbilityMonthText(Core.Declare.PersonBuild build) =>
        $"Töötukassa SM vähendus (TSD_OPTV_SM_VAHENDUS): kuumäär {Money(build.WorkAbilityReductionOffered)}, kasutatud {Money(build.WorkAbilityReduction)}, alles {Money(build.WorkAbilityReductionOffered - build.WorkAbilityReduction)}.";

    /// <summary>A simulation's review in words (decision 204): the steps, the refusal, every payment with what blocks it,
    /// the payments left out and why, the build's problems and the minimum, the totals.</summary>
    public static string SimReport(SimResult result)
    {
        var et = System.Globalization.CultureInfo.GetCultureInfo("et-EE");
        string Euro(decimal amount) => amount.ToString("#,##0.00", et) + " €";
        var text = new System.Text.StringBuilder();
        if (SimStop(result) is { } refusal)
        {
            text.AppendLine("PEATUS: " + refusal).AppendLine();
        }

        text.AppendLine(result.OwnPhrases ? SimOwnPhrases : SimBuiltInPhrases);
        text.AppendLine("Sammud:");
        foreach (var note in result.ImportNotes)
        {
            text.AppendLine("  " + SheetNoteLine(note));
        }

        foreach (var problem in result.RunProblems)
        {
            text.AppendLine("  " + ProblemLine(problem));
        }

        if (result.Review is { } review)
        {
            text.AppendLine().AppendLine("Maksed:");
            foreach (var payment in review.Payments)
            {
                text.AppendLine($"  [{payment.RowNo}] {payment.Date?.ToString("dd.MM.yyyy", et)} {Euro(payment.Amount)} {payment.Description} — {payment.Category ?? NoCategory}, liik {payment.TypeCode ?? RulesNone}, {(payment.Declare ? "deklareeritakse" : "ei deklareerita")}");
                foreach (var blocker in payment.Blockers)
                {
                    text.AppendLine($"      {RefusalLine(blocker)}");
                }

                foreach (var notice in payment.Notices)
                {
                    text.AppendLine($"      {NoticeTip(payment, notice)}");
                }
            }

            if (review.Undeclared.Count > 0)
            {
                text.AppendLine().AppendLine("Deklareerimata:");
                foreach (var left in review.Undeclared)
                {
                    text.AppendLine($"  [{left.Payment.RowNo}] {Euro(left.Payment.Amount)}: {UndeclaredReason(left)}");
                }
            }
        }

        if (result.Build is { } build)
        {
            text.AppendLine().AppendLine($"SM miinimum: kohustus {Euro(build.MinimumDue)} (kuu osa {build.MinimumShare.ToString("0.####", et)}), puudu {Euro(build.MinimumShortfall)}{(build.TopUpKey is { } key ? $", lisarida {Core.Report.ReportText.SourceText(key)}" : "")}.");
            text.AppendLine($"Maksuvaba: pakutud {Euro(build.ExemptionOffered)}, alles {Euro(build.ExemptionLeft)}.");
            if (build.WorkAbilityReductionOffered > 0m)
            {
                text.AppendLine(WorkAbilityMonthText(build));
            }
        }

        // the build's notes in Estonian, each once (by the Core's English: the person's and the period's may say the same)
        var problems = (result.Build?.Notes ?? []).Concat(result.BuildNotes).DistinctBy(n => n.Text, StringComparer.Ordinal).ToList();
        if (problems.Count > 0)
        {
            text.AppendLine().AppendLine("Probleemid:");
            foreach (var problem in problems)
            {
                text.AppendLine("  " + BuildNoteLine(problem));
            }
        }

        var t = result.Totals;
        text.AppendLine().AppendLine($"Kokku {t.Lines} rida: neto {Euro(t.Net)}, TM {Euro(t.IncomeTax)}, SM {Euro(t.SocialTax)}, TK {Euro(t.UnemploymentEmployee)}, TTK {Euro(t.UnemploymentEmployer)}, KP {Euro(t.FundedPension)}; maksud kokku {Euro(t.Taxes)}.");
        if (result.Xml is null && result.Refusal is null)
        {
            text.AppendLine().AppendLine("XML puudub: " + SimXmlText(result));
        }

        return text.ToString();
    }

    public const string CalculatorTitle = "Kalkulaator";
    public const string AmountIsNet = "neto (väljamakstud)";
    public const string AmountIsGross = "bruto";
    public const string Compute = "Arvuta";
    public const string EmployerCost = "Tööandja kulu";
    public const string MinimumShare = "Kuu osa (päevad/30)";
    // the calculator's pension-age tick and what it cannot use, said before anything is computed (the fifth audit, item 13)
    public const string PensionAgeThisYearTick = "Pensioniealine sel aastal (MVT_650)";

    // the calculator's third tick: the age reached in a month before the payment's, so no employee's unemployment premium
    public const string PensionAgeReachedTick = "Pensioniiga saabunud enne makse kuud (TK 1,6 % ei peeta kinni)";

    public static string CalculatorExemptionOutOfRange(decimal legal) => $"{ExemptionMonthly} peab olema 0 kuni {Money(legal)} (seaduse järgi).";

    public static string CalculatorShareInvalid(string text) => $"{MinimumShare}: „{text}“ ei ole arv vahemikus üle 0 kuni 1.";

    public const string CalculatorExemptionWithoutApplication = $"{ExemptionMonthly} on antud, kuid „{ExemptionApplied}“ ei ole märgitud: märgi avaldus või tühjenda summa.";

    public static string CalculatorExemptionSubCent(string text) => $"{ExemptionMonthly}: „{text}“ peab olema täissentides.";

    public static string PaymentStatusText(PaymentStatus status) => Core.Report.ReportText.PaymentStatusText(status);

    public static string ActionText(LineAction action) => Core.Report.ReportText.ActionText(action);

    public static string NextActionText(Core.Feedback.NextAction action) => action switch
    {
        Core.Feedback.NextAction.ReviewWarning => "Hoiatus: vaata üle; rida on serveris.",
        Core.Feedback.NextAction.CorrectWithChange => "Rida on serveris vigasena: paranda ja saada sama võtmega uuesti (M).",
        Core.Feedback.NextAction.FixAndResendAsNew => "Rida ei ole serveris: paranda ja saada uuesti uue reana (R).",
        Core.Feedback.NextAction.Review => "Otsusta teate järgi (vt e-MTA juhend).",
        _ => "",
    };

    private static readonly (string Code, string Name, Func<Core.Tax.TaxResult, decimal>? Ours)[] ServerTaxNames =
    [
        ("TSD_SM", "sotsiaalmaks", r => r.SocialTax),
        ("TSD_KP", "kogumispension", r => r.FundedPension),
        ("TSD_TK", "töötuskindlustus (töötaja)", r => r.UnemploymentEmployee),
        ("TSD_TTK", "töötuskindlustus (tööandja)", r => r.UnemploymentEmployer),
        ("TSD_KTM", "tulumaks (kinnipeetud)", r => r.IncomeTax),
    ];

    /// <summary>The tooltip of a feedback cell: the latest report's messages in full, the advice, and the server's taxes
    /// beside ours — the cell itself keeps its short "Severity: codes" text (the grid sorts, searches and exports it).</summary>
    public static string FeedbackDetail(LineFeedback feedback, Core.Tax.TaxResult? ours)
    {
        var lines = new List<string> { $"Viimane raport ({feedback.RecordedAt.ToLocalTime():dd.MM.yyyy HH:mm}): {Core.Report.ReportText.SeverityText(feedback.Severity)}" };
        lines.AddRange(feedback.Messages.Select(m => $"{m.Code} ({Core.Report.ReportText.SeverityText(m.Severity)}): {m.Info}"));
        if (NextActionText(feedback.NextAction) is { Length: > 0 } advice)
        {
            lines.Add(advice);
        }

        if (feedback.ServerTaxes.Count > 0)
        {
            lines.Add("Serveri maksud (meie arvutus):");
            foreach (var (code, name, pick) in ServerTaxNames)
            {
                if (feedback.ServerTaxes.TryGetValue(code, out var server))
                {
                    lines.Add(ours is not null && pick is not null ? $"  {name}: {Money(server)} ({Money(pick(ours))})" : $"  {name}: {Money(server)}");
                }
            }

            lines.AddRange(feedback.ServerTaxes.Where(kv => ServerTaxNames.All(n => n.Code != kv.Key)).Select(kv => $"  {kv.Key}: {Money(kv.Value)}"));
        }

        lines.AddRange(feedback.Unclassified.Select(u => $"{u.Code}: {u.Info}"));
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>A report's finding that points at no line: about the file, or a whole section.</summary>
    public static string GeneralText(Core.Feedback.GeneralValidation general) =>
        $"{EntryText(general.EntryNumber)}: {Core.Report.ReportText.LevelText(general.Validation.Level)} {general.Validation.Code} — {general.Validation.Description}";

    /// <summary>A report's section (entryNumber) in the window's words; the raw text for one it does not know, „fail“ for none.</summary>
    private static string EntryText(string? entry) =>
        entry is null ? "fail"
        : Enum.TryParse<Core.Model.Section>(entry, out var section) && Enum.IsDefined(section) && !int.TryParse(entry, out _) ? SectionShort(section)
        : entry;

    // MTA's answers in Estonian, never the enum's English name (the fifth audit, item 29)
    public static string ProcessingResultText(Core.Feedback.ProcessingResult result) => result switch
    {
        Core.Feedback.ProcessingResult.Ok => "korras",
        Core.Feedback.ProcessingResult.Warning => "hoiatustega",
        Core.Feedback.ProcessingResult.Error => "vigadega",
        Core.Feedback.ProcessingResult.PartlyError => "osaliselt vigadega",
        _ => "teadmata",
    };

    public static string ConfirmationText(Core.Feedback.ConfirmationOutcome outcome) => outcome switch
    {
        Core.Feedback.ConfirmationOutcome.Confirmed => "kinnitatud",
        Core.Feedback.ConfirmationOutcome.StillProcessing => "andmed on veel töötlemisel — alusta kinnitamist hiljem uuesti",
        Core.Feedback.ConfirmationOutcome.WithErrors => "deklaratsioonil on vigu",
        Core.Feedback.ConfirmationOutcome.WithDiscrepancies => "andmetes on vastuolusid",
        Core.Feedback.ConfirmationOutcome.CannotConfirm => "kinnitada ei saanud",
        _ => "teadmata vastus",
    };

    public static string ConfirmationReportLine(string source, Core.Feedback.ConfirmationOutcome outcome) => $"{ReadFeedback} {source}: kinnitamise raport — {ConfirmationText(outcome)}";

    // the key's history
    public const string History = "Ajalugu…";
    public const string HistoryTitle = "Võtme ajalugu";
    public const string CreatedAt = "Loodud";
    public const string Close = "Sulge";

    // the report workbook
    public const string ExportReport = "Ekspordi aruanne…";
    public const string ReportFolder = "Vali kaust, kuhu aruanne salvestada";
    public const string ReportSaved = "Aruanne on salvestatud:";
    public const string OpenFolderQuestion = "Avada kaust?";

    // updates from the release folder
    public const string CheckUpdates = "Kontrolli uuendusi";
    public const string CheckingUpdates = "Kontrollin uuendusi…";
    public const string UpdateTitle = "Programmi uuendus";
    public const string UpdateAvailableTitle = "saadaval";
    public const string UpToDate = "Uuendusi ei ole: see on uusim versioon.";
    public const string NoRelease = "Uuenduste kaustas ei ole veel ühtegi versiooni.";
    public const string UpdatesUnreachable = "Uuenduste kausta ei saa lugeda (kas võrguketas S: on ühendatud?).";
    public const string UpdatesBad = "Uuenduste kausta latest.txt ei kõlba.";
    public const string UpdatesFailed = "Uuenduste kontroll ebaõnnestus.";
    public const string UpdateFolderLine = "Uuenduste kaust:";
    public const string UpdateBlockedBuild = "See koopia käivitati Visual Studio ehituskaustast; seda ei uuendata.";
    public const string UpdateBlockedNetwork = "See koopia käivitati võrgukettalt; asenda see käsitsi, kui kõik programmi aknad on suletud.";
    public const string UpdateOtherWindows = "Programmi teised aknad töötavad vana versiooniga, kuni need suletakse.";
    public const string UpdateNow = "Uuenda ja käivita uuesti";
    public const string Later = "Hiljem";
    public const string WaitMore = "Oota veel";
    public const string PutBack = "Taasta eelmine versioon";
    public const string UpdateChecking = "Kontrollin…";
    public const string UpdateDownloading = "Kopeerin…";
    public const string UpdateUnpacking = "Pakin lahti ja kontrollin…";
    public const string UpdateInstalling = "Paigaldan…";
    public const string UpdateStarting = "Käivitan uut versiooni…";
    public const string UpdateClosingNew = "Sulgen uut versiooni…";
    public const string UpdateAfterDialog = "Uuendus on saadaval: sulge avatud aken ja vali siis Andmebaas › Uuenda.";
    public const string UpdateCancelled = "Uuendamine katkestati.";
    public const string UpdateNoLongerOffered = "Seda versiooni enam ei pakuta.";
    public const string UpdateNewerInstalled = "Programmi teine aken on juba paigaldanud uuema versiooni. Sulge kõik aknad ja käivita programm uuesti.";
    public const string UpdateNoAnswer = "Uus versioon käivitus, kuid ei vastanud ühe minuti jooksul — võib-olla ootab see oma aknas vastust. Oota veel või taasta eelmine versioon (uus suletakse).";
    public const string UpdateNoAnswerNoWayBack = "Uus versioon käivitus, kuid ei vastanud ühe minuti jooksul — võib-olla ootab see oma aknas vastust. Oota veel või sulge see aken; uus versioon jääb tööle ja vana aken sulgub.";
    public const string UpdateStoreNote = "Kui uus versioon jõudis andmebaasi uuendada, pakub programm järgmisel avamisel andmebaasi varukoopia taastamist.";
    public const string StoreNewerRestore = "Andmebaasi on uuendanud programmi uuem versioon ja see versioon ei saa seda avada. Selle kõrval on varukoopia uuendamise eelsest seisust:";
    public const string StoreNewerRestoreQuestion = "Taastada varukoopia? Uuendatud andmebaas jääb kõrvale nimega";

    public static string StoreBackupMade(DateTime made) => $"Varukoopia on tehtud {made:dd.MM.yyyy HH:mm}; hilisem töö selles ei ole.";

    public const string StoreNewerUpdate = "Andmebaasi on uuendanud programmi uuem versioon ja see versioon ei saa seda avada. Uuenda programm (menüüst Andmebaas) ja ava andmebaas uuesti.";

    public const string StoreNewerDeclined = "Andmebaas jäi avamata: selle on uuendanud versioon, mis siin ei käivitunud. Ava andmebaas uuesti (Andmebaas → Ava andmebaas…), kui soovid varukoopia taastada, või oota parandatud versiooni.";
    public const string StoreRestored = "Andmebaasi varukoopia taastati";
    public const string UpdateNewExited = "Uus versioon ei käivitunud";
    public const string UpdateNotClosed = "Uut versiooni ei õnnestunud sulgeda: see jääb tööle ja see aken sulgub. Kui uus versioon ei tööta, sulge selle aken ja teata sellest programmi omanikule.";
    public const string UpdateKept = "Alles jäi versioon";
    public const string UpdateFailed = "Uuendamine ebaõnnestus:";
    public const string UpdateTellOwner = "Teata sellest programmi omanikule.";
    public const string UpdateReadFailed = "Uuendust ei õnnestunud lugeda.";
    public const string UpdateIncomplete = "Uuenduse fail on poolik — võib-olla seda alles kopeeritakse. Proovi hiljem.";
    public const string UpdateDamaged = "Uuenduse fail on rikutud (kontrollsumma ei klapi).";
    public const string UpdateNotProgram = "Uuenduse fail ei ole SatellesTSD programm.";
    public const string UpdateVersionMismatch = "Uuenduse faili versioon ei vasta latest.txt-le.";
    public const string UpdateSchemasMissing = "Uuendusest puudub skeemide kaust.";
    public const string UpdateNoWrite = "Programmi kausta ei saa kirjutada.";
    public const string UpdateBusy = "Uuenduse fail on teise programmi (viirusetõrje?) käes. Proovi uuesti.";
    public const string UpdateOtherWindowUpdating = "Programmi teine aken juba uuendab. Oota, kuni see lõpetab.";
    public const string UpdateFolderOverride = "Uuenduste kaust (failist SatellesTSD.update-folder.txt):";
    public const string UpdateSwapFailed = "Programmi ei õnnestunud asendada.";
    public const string UpdateRevertFailed = "Eelmist versiooni ei õnnestunud tagasi panna.";
    public const string UpdateBadZip = "Uuenduse faili ei õnnestunud lahti pakkida.";

    public static string UpdateDescription(string current, string offered, string? published, string folder, int otherWindows) =>
        $"Praegune versioon: {current}\nPakutav versioon: {offered}{(published is null ? "" : $" (avaldatud {published})")}\n{UpdateFolderLine} {folder}\n\n" +
        "Programm kopeerib uue versiooni enda kõrvale, kontrollib seda, asendab ennast ja käivitub uuesti. Andmebaas ja seaded jäävad puutumata." +
        (otherWindows > 0 ? $"\n\n{UpdateOtherWindows}" : "");

    public static string ReasonText(string code) => code switch
    {
        "PARANDUSPOHJUS_1" => "1 — vead väljamaksete või maksude arvestamisel",
        "PARANDUSPOHJUS_2" => "2 — maksukontrolli tulemusel",
        "PARANDUSPOHJUS_3" => "3 — maksukohustus teises riigis (ainult töötasud)",
        "PARANDUSPOHJUS_4" => "4 — tagasiulatuv hüvitis (ainult hüvitised)",
        _ => code,
    };

    public static string PeriodState(PeriodState state) => state switch
    {
        Core.Store.PeriodState.Draft => "Mustand",
        Core.Store.PeriodState.Exported => "Eksporditud",
        Core.Store.PeriodState.Uploaded => "Üles laaditud",
        Core.Store.PeriodState.Confirmed => "Kinnitatud",
        _ => state.ToString(),
    };

    // the grid and the report workbook say the same words (ReportText holds them)
    public static string ResidencyText(Person person) => Core.Report.ReportText.ResidencyText(person);

    public static string PensionText(Person person) => Core.Report.ReportText.PensionText(person);

    public static string Percent(decimal? rate) => Core.Report.ReportText.Percent(rate);

    public static string Source(LookupSource source) => source switch
    {
        LookupSource.Emta => "EMTA",
        LookupSource.Pensionikeskus => "Pensionikeskus",
        LookupSource.Derived => "tuletatud",
        LookupSource.Manual => "käsitsi",
        _ => "—",
    };

    public static string A1Text(A1Case a1) => a1 switch
    {
        A1Case.EstonianA1WorkAbroad => A1Estonian,
        A1Case.ForeignA1WorkInEstonia => A1Foreign,
        _ => A1None,
    };

    public static string Money(decimal amount) => amount.ToString("N2", System.Globalization.CultureInfo.CurrentCulture);

    public static string Day(DateOnly? day) => day?.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture) ?? "";
}
