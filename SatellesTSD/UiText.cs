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
    public const string Residency = "Residentsus";
    public const string Pension = "Pension";
    public const string CheckedOn = "Kontrollitud";
    public const string EmploymentType = "Töötamise liik";
    public const string EntryState = "Kande olek";
    public const string Problems = "Probleemid";
    public const string UnmatchedRow = "— leidmata read —";

    // the payments grid (the Otsingu kontroll's headers)
    public const string Document = "Dokument";
    public const string Date = "Kuupäev";
    public const string PayeeOnStatement = "Nimi väljavõttel";
    public const string Description = "Selgitus";
    public const string Amount = "Summa";
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

    // the person's facts as the workbook wrote them
    public const string Resident = "EE";
    public const string NonResident = "mitteresident";
    public const string NotChecked = "kontrollimata";
    public const string PensionMember = "II sammas";
    public const string PensionNotMember = "ei ole liitunud";
    public const string Unknown = "teadmata";
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
    public const string A1 = "A1 tõend";
    public const string A1None = "puudub";
    public const string A1Estonian = "Eesti A1, töö välismaal";
    public const string A1Foreign = "välisriigi A1, töö Eestis";
    public const string A1Country = "A1 riik";
    public const string WorkCountry = "Töö riik";
    public const string TreatyRate = "Maksulepingu TM määr";
    public const string TorType = "TÖR töötamise liik";
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
    public const string Locked = "lukus";
    public const string Editable = "muudetav";
    public const string RowsLeftOut = "rida ilma saajata jäeti kõrvale";
    public const string Problem = "Viga";

    public static string PeriodState(PeriodState state) => state switch
    {
        Core.Store.PeriodState.Draft => "Mustand",
        Core.Store.PeriodState.Exported => "Eksporditud",
        Core.Store.PeriodState.Uploaded => "Üles laaditud",
        Core.Store.PeriodState.Confirmed => "Kinnitatud",
        _ => state.ToString(),
    };

    public static string ResidencyText(Person person) =>
        person.ResidencyCheckedOn is null ? NotChecked : person.Residency == Core.Model.Residency.Resident ? Resident : NonResident;

    public static string PensionText(Person person) => person.FundedPensionMember switch
    {
        null => NotChecked,
        true => $"{PensionMember} {Percent(person.PensionRate)}",
        false => PensionNotMember,
    };

    public static string Percent(decimal? rate) => rate is { } r ? (r * 100).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " %" : Unknown;

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
