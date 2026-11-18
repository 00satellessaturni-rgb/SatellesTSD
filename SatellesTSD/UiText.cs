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
        _ => "ei lähe deklaratsiooni: " + string.Join(", ", u.Codes),
    };

    public const string A1 = "A1 tõend";
    public const string A1None = "puudub";
    public const string A1Estonian = "Eesti A1, töö välismaal";
    public const string A1Foreign = "välisriigi A1, töö Eestis";
    public const string A1Country = "A1 riik";
    public const string WorkCountry = "Töö riik";
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
        ReviewWorkspace.LineMissing => byHand ? "Seda käsitsi lisatud rida ei ole enam andmebaasis – vaade laaditakse uuesti."
            : "Seda osa ei ole enam andmebaasis – vaade laaditakse uuesti.",
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
    public static string LookupLine(PersonLookupOutcome outcome) =>
        $"{outcome.Name}{(outcome.ControlDate is { } day ? $" (seisuga {Day(day)})" : "")}: {string.Join("; ", outcome.Steps)}";

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
    public const string TwoContractsMenu = "Kaks lepingut korraga";
    public const string TwoContractsMenuTip = "Mis liigi saab kategooria Palgad makse päeval, mil isikul kehtib kaks eri liiki töötamist (nt tööleping ja VÕS leping). Makse enda Väljamakse liik ja kaardi „Kaardi töötamise liik kõigile maksetele“ on alati ülimuslikud. Valik kehtib järgmisest „Koosta read“ ja simulatsioonist.";
    public const string TwoContractsSalary = "Palgad → tööleping (1/507)";
    public const string TwoContractsSalaryTip = "Palk saab päeval kehtiva töölepingu liigi (10, mitteresidendil 120). Sobib, kui palgad makstakse töölepingu alusel. NB: ka VÕS-tasu, mis on kategoorias Palgad, saab siis liigi 10 – kontrolli neid ridu (veerus „Kontroll“ on mõlemad TÖR liigid) ja vajadusel vali makse liik ise.";
    public const string TwoContractsAsk = "Küsi makse liiki";
    public const string TwoContractsAskTip = "Sellisele palgale liiki ei pakuta: makse on deklareerimiseks märgitud, kuid blokeeritud (TWO_CONTRACTS), kuni valid Väljamakse liigi või kaardi töötamise liigi kõigile maksetele. Seni ei lisata ka miinimumi nullrida. Kõige täpsem, kuid nõuab iga sellise rea juures üht valikut.";
    public const string TwoContractsLatest = "Hiliseim algus (endine reegel)";
    public const string TwoContractsLatestTip = "Palk saab päeval kehtiva lepingu liigi: kehtiv enne peatatut, siis hiliseima algusega (otsus 85). Kui VÕS leping algas pärast töölepingut, saab töölepingu palk liigi 17 ja töölepingule lisandub miinimumi nullrida – kasuta ainult siis, kui see on õige.";
    public const string TwoContractsChanged = "Kaks lepingut korraga:";
    public const string TwoContractsNote = "TÖR";

    /// <summary>The check cell's tip on a salary whose type the Seaded rule picked from two contracts in force that day.</summary>
    public static string TwoContractsPickedTip(IReadOnlyList<string> types, string? typeCode) =>
        $"Makse päeval kehtib kaks eri liiki töötamist (TÖR {string.Join(", ", types)}); liik {typeCode ?? "–"} on valitud seadete järgi (Seaded → {TwoContractsMenu}). Kontrolli, et see sobib.";

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
            Core.Store.LayoutsRefusal.Unusable => $"Paigutust ei saa kasutada ({subject}): igal paigutusel peab olema ID, pank, päised ja kuupäeva, saaja ja summa veerg päiste seas.",
            Core.Store.LayoutsRefusal.RepeatedId => $"Failis on sama ID mitu korda ({subject}): üks paigutus ühe ID kohta.",
            Core.Store.LayoutsRefusal.Collides => "Paigutuse päiserida on juba teisel paigutusel: faili lehte ei saaks neist eristada. Muuda päiseid või kasuta sisseehitatud paigutuse ID-d.",
            _ => null,
        };
        return said is null ? null : said + " Midagi ei salvestatud." + (detail is null ? "" : Environment.NewLine + detail);
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
            return $"Saatmata mustandread: {string.Join(", ", refusal.DraftLines)}. Eksportige need või vajutage uuesti „Koosta read“, siis märkige periood kinnitatuks.";
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
            _ => null,
        };
    }

    /// <summary>A refusal the window words itself (the card's save, a file's removal), or null: the exception's own text then.</summary>
    public static string? Worded(Exception refusal) => refusal is ReviewException review ? CardRefused(review) : PhrasesRefused(refusal) ?? LayoutsRefused(refusal) ?? FileRefused(refusal);

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
            PhrasesRefusal.BadCell when subject is not null => $"Fraasid ei ole loetavad ({subject}): {refusal.Message[(refusal.Message.IndexOf(": ", StringComparison.Ordinal) + 2)..]}",
            PhrasesRefusal.BadCell => "Fraasid ei ole loetavad: " + refusal.Message,
            _ => null,
        };
        return said is null ? null : said + " Midagi ei salvestatud.";
    }

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
    public const string PhrasesResetConfirm = "Taastada sisseehitatud fraasid? Omad fraasid kustutatakse; kuu otsing kasutab neid järgmisel „Sobita“ käivitamisel.";
    public static string PhrasesSaved(string path) => $"{PhrasesTitle}: salvestatud {path}";
    public static string PhrasesImported(int changed) => $"{PhrasesTitle}: imporditud, muudetud lahtreid {changed}. Kuu otsing kasutab uusi fraase järgmisel „Sobita“ käivitamisel.";
    public const string PhrasesResetDone = PhrasesTitle + ": taastatud sisseehitatud fraasid. Kuu otsing kasutab neid järgmisel „Sobita“ käivitamisel.";
    public const string CutChangedSinceBuild = "Deklaratsiooni lõige on pärast „Koosta read“ muudetud (Seaded): mustandid on teises lõikes. Eksportida need siiski? (Muidu vajuta uuesti „Koosta read“.)";
    public const string RunStaleReport = "Perioodi maksed on otsimata või failid muutusid pärast otsingut: aruanne ei sisalda kõiki makseid. Koostada aruanne siiski?";
    public static string HistoryRejectedBy(int version) => $"tagasi lükatud: hilisem versioon v{version} sai KR_XBRL_VALJAMAKSED_5 või _6 (võti ei ole serveris)";
    public const string ReviewChangedSinceBuild ="Ülevaatus on pärast „Koosta read“ muutunud ja read ei kajasta seda. Eksportida siiski? (Muidu vajuta uuesti „Koosta read“.)";
    public const string ReviewNotChecked = "Ei saanud kontrollida, kas read vastavad ülevaatusele.";
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
            ("Sotsiaalmaksu miinimum", $"{Euro(minimum)} kuus ({Euro(rates.SocialTaxMinimumBase)} × {Percent(rates.SocialTax)}); kuu osa = päevad / 30", "TSD_KUUMSUURENDUS"),
            ("Maksuvaba tulu", Euro(rates.BasicExemptionMonth) + " kuus", "MVT_610"),
            ("Maksuvaba tulu pensioniea aastal", Euro(rates.PensionAgeExemptionMonth) + " kuus", "MVT_650"),
        ];
    }

    /// <summary>The conditions the matrix's toggles do not show, in words (decision 203).</summary>
    public static IReadOnlyList<string> RulesNotes() =>
    [
        $"SM miinimum kehtib TÖR liikidel {string.Join(", ", EmploymentTypes.All.Where(e => e.SocialTaxMinimum).Select(e => $"{e.TorTypeId} {e.Name}"))}; kuu osa on päevad / 30. Kui isik on kuu registris puudu, miinimumi ei ole (NO_EMPLOYMENT_IN_MONTH).",
        "SM miinimum ei kehti SMS § 2 lg 4 juhtudel (riikliku pensioni saaja, vähenenud töövõimega isik, alla 3-aastase lapse või kolme alla 19-aastase lapse vanem, õpilane või üliõpilane jt) ega teisel tööandjal, kui miinimumi kannab maksuvaba tulu arvestav tööandja; märge on isiku kaardil.",
        "Mitteresident saab maksuvaba tulu ainult EMP riigi residendina residentsustõendiga (KR_MVT_06/07); kaardil EMP-väline riik keelab selle (EXEMPTION_NOT_EEA).",
        "Maksuvaba tulu ei ületa makset, millest on maha arvatud II sammas ja töötuskindlustusmakse (KR_MVT_08); kuu maksuvaba jagatakse isiku maksete vahel.",
        "Vanadus- või paindliku vanaduspensioni saajalt kindlustatu töötuskindlustusmakset ei peeta kinni (KR_TK_2), samuti pensioniea saabumise kuule järgnevast kuust (TKindlS § 42 lg 1 p 1; kuu isikukoodi sünniaja ja kaardi pensioniea aasta järgi).",
        "Välisriigi A1 tõendiga töötaja Eestis: sotsiaalmaks, töötuskindlustusmakse ja II sammas puuduvad. Eesti A1 tõendiga töö välismaal: tulumaks puudub. A1 mõjub ainult liikidel, millel on A1 variant.",
        $"Lepingumäär asendab tulumaksu määra ainult mitteresidendile residentsustõendiga, liikidel {string.Join(", ", PaymentTypes.All.Where(t => t.TreatyRateAllowed).Select(t => t.Code))}; täisprotsent, mitte üle liigi oma määra (TREATY_RATE_INVALID).",
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

    /// <summary>Why the simulation stopped before the build, worded as the main window words the same refusal; null when it
    /// did not stop.</summary>
    public static string? SimStop(SimResult result) =>
        result.RefusalError is { } e ? Worded(e) ?? e.Message
        : result.Refusal is { } reason ? SimImportRefused(reason)
        : null;

    /// <summary>The XML tab's text: the file, or why there is none.</summary>
    public static string SimXmlText(SimResult result) =>
        result.Xml
        ?? (result.XmlRefusalError is { } e ? Worded(e) ?? e.Message : null)
        ?? SimStop(result)
        ?? SimNoLines;

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
        foreach (var step in result.Steps)
        {
            text.AppendLine("  " + step);
        }

        if (result.Review is { } review)
        {
            text.AppendLine().AppendLine("Maksed:");
            foreach (var payment in review.Payments)
            {
                text.AppendLine($"  [{payment.RowNo}] {payment.Date?.ToString("dd.MM.yyyy", et)} {Euro(payment.Amount)} {payment.Description} — {payment.Category ?? NoCategory}, liik {payment.TypeCode ?? RulesNone}, {(payment.Declare ? "deklareeritakse" : "ei deklareerita")}");
                foreach (var blocker in payment.Blockers)
                {
                    text.AppendLine($"      {blocker.Code}: {blocker.Message}");
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
            text.AppendLine().AppendLine($"SM miinimum: kohustus {Euro(build.MinimumDue)} (kuu osa {build.MinimumShare.ToString("0.####", et)}), puudu {Euro(build.MinimumShortfall)}{(build.TopUpKey is { } key ? $", lisarida {key}" : "")}.");
            text.AppendLine($"Maksuvaba: pakutud {Euro(build.ExemptionOffered)}, alles {Euro(build.ExemptionLeft)}.");
        }

        var problems = (result.Build?.Problems ?? []).Concat(result.BuildProblems).Distinct().ToList();
        if (problems.Count > 0)
        {
            text.AppendLine().AppendLine("Probleemid:");
            foreach (var problem in problems)
            {
                text.AppendLine("  " + problem);
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
        var lines = new List<string> { $"Viimane raport ({feedback.RecordedAt.ToLocalTime():dd.MM.yyyy HH:mm}): {feedback.Severity}" };
        lines.AddRange(feedback.Messages.Select(m => $"{m.Code} ({m.Severity}): {m.Info}"));
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
        $"{general.EntryNumber ?? "fail"}: {general.Validation.Level} {general.Validation.Code} — {general.Validation.Description}";

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
