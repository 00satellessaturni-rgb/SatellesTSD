using SatellesTSD.Core.Lookups;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Store;

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
    public const string Residency = "Residentsus";
    public const string Pension = "Pension";
    public const string CheckedOn = "Kontrollitud";
    public const string EmploymentType = "Töötamise liik";
    public const string EntryState = "Kande olek";
    public const string EmploymentEnd = "Töötamise lõpu aeg";
    public const string PauseStart = "Peatamise algus";
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
    public const string RemoveLineConfirm = "Eemaldada käsitsi lisatud rida?";
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
    public const string BadAmountMark = "?";
    public const string MenuView = "&Vaade";
    public const string LogHeightDefault = "Logiaken: tavakõrgus";
    public const string LogHeightOneRow = "Logiaken: üks rida";
    public const string MenuSettings = "&Seaded";
    public const string DeclareEachPayment = "Deklaratsioon: iga makse eraldi reana";
    public const string DeclareTotalsPerType = "Deklaratsioon: koond väljamakse liigi kaupa, viimase kuupäevaga";
    public const string DeclareModeChanged = "Deklaratsiooni read koostatakse nüüd";
    public const string ModeEach = "iga makse eraldi reana";
    public const string ModeTotals = "koondina liigi kaupa";
    public const string RatesMissing = "Selle aasta maksumäärad ja kuumäär ei ole programmis: kuu ridu ei saa koostada enne, kui uus versioon need toob.";
    public const string RunStale = "failid muutusid pärast otsingut (Otsi maksed uuesti)";
    public const string RunStaleRefused = "Perioodi failid muutusid pärast maksete otsingut: read koostatakse alles uue otsingu järel. Vajuta „Otsi maksed“ (kahe TÖR-lehe korral eemalda enne Failid-aknas vana otsing ja vana TÖR).";
    public const string RunStaleExport = "Perioodi failid muutusid pärast maksete otsingut: read ei sisalda kõiki makseid. Vajuta enne eksporti „Otsi maksed“ ja „Koosta read“ (kahe TÖR-lehe korral eemalda enne Failid-aknas vana otsing ja vana TÖR).";
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
    public const string WithdrawConfirm = "Võtta eksport tagasi? Read saavad uuesti mustandiks. Ainult faili puhul, mida e-MTA-sse ei laaditud.";
    public const string ConfirmForce = "Mõni rida ootab veel vastust, vajab parandust või lükati e-MTA-s tagasi (pole serveris). Märkida periood siiski kinnitatuks?";
    public const string ReviewChangedSinceBuild = "Ülevaatus on pärast „Koosta read“ muutunud ja read ei kajasta seda. Eksportida siiski? (Muidu vajuta uuesti „Koosta read“.)";
    public const string ReviewNotChecked = "Ei saanud kontrollida, kas read vastavad ülevaatusele.";
    public const string ReopenConfirm = "Avada periood parandamiseks? Ülevaatus muutub taas muudetavaks; järgmine eksport kannab muudatused (M) ja tühistused (T) võtmetele, mida e-MTA juba teab. e-MTA-s tuleb deklaratsioon samuti muudatusteks avada. (Kui faili ei laaditudki üles, kasuta „Tühista eksport“.)";
    public const string ApplyReportTo = "Rakendada raport failile";
    public const string ResolveAcceptedConfirm = "Märkida rida e-MTA-s vastu võetuks (sinu sõna, tagasisideraportita)?";
    public const string ResolveRejectedConfirm = "Märkida rida e-MTA-s tagasi lükatuks (sinu sõna)?";
    public const string CorrectionReasonTitle = "Paranduse põhjus";
    public const string CorrectionReasonPrompt = "Deklaratsioon on kinnitatud; muutev fail nimetab põhjuse (PARANDUSPOHJUS).";
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
        ("TSD_KTM", "TSD_KTM", null), // no figure of ours stands for it; the server's is shown alone
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
    public const string UpdateCancelled = "Uuendamine katkestati.";
    public const string UpdateNoLongerOffered = "Seda versiooni enam ei pakuta.";
    public const string UpdateNewerInstalled = "Programmi teine aken on juba paigaldanud uuema versiooni. Sulge kõik aknad ja käivita programm uuesti.";
    public const string UpdateNoAnswer = "Uus versioon käivitus, kuid ei vastanud ühe minuti jooksul — võib-olla ootab see oma aknas vastust. Oota veel või taasta eelmine versioon (uus suletakse).";
    public const string UpdateNoAnswerNoWayBack = "Uus versioon käivitus, kuid ei vastanud ühe minuti jooksul — võib-olla ootab see oma aknas vastust. Oota veel või sulge see aken; uus versioon jääb tööle.";
    public const string UpdateStoreNote = "Kui uus versioon jõudis andmebaasi uuendada, pakub programm järgmisel avamisel andmebaasi varukoopia taastamist.";
    public const string StoreNewerRestore = "Andmebaasi on uuendanud programmi uuem versioon ja see versioon ei saa seda avada. Selle kõrval on varukoopia uuendamise eelsest seisust:";
    public const string StoreNewerRestoreQuestion = "Taastada varukoopia? Uuendatud andmebaas jääb kõrvale nimega";
    public const string StoreRestored = "Andmebaasi varukoopia taastati";
    public const string UpdateNewExited = "Uus versioon ei käivitunud";
    public const string UpdateNotClosed = "Uut versiooni ei õnnestunud sulgeda. Sulge selle aken ja käivita programm uuesti.";
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
