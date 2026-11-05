using System.Globalization;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Store;
using SatellesTSD.Core.Xbrl;

namespace SatellesTSD.Core.Report;

/// <summary>The texts of the report workbook: the workbook's own header spellings (a later reader locates columns
/// by them, so they stay verbatim — "Kuupaev" without the diacritic, "Otsing*" with the star) and the words the
/// review shows for a person's facts and a line's state, in Estonian. The window's texts delegate here so that the
/// grid and the workbook say the same.</summary>
public static class ReportText
{
    // the sheets
    public const string InterimSheet = "Vahearuanne";
    public const string TorSheet = "TOR kontroll";
    public const string SearchSheet = "Otsingu kontroll";
    public const string UnknownCategorySheet = "Tundmatu kategooria";
    public const string DeclarationSheet = "Deklaratsioon";

    // Vahearuanne
    public const string PersonalCode = "Töötaja isikukood";
    public const string LastName = "Perekonnanimi";
    public const string FirstName = "Eesnimi";
    public const string SalaryTotal = "PALK NETO KOKKU";
    public const string TaxFree = "Maksuvaba";
    public const string Residency = "Residentsus";
    public const string Pension = "Pension";
    public const string ControlDate = "Kontroll-kp";
    public const string EmploymentStart = "Töötamise alguse aeg";
    public const string EmploymentType = "Töötamise liik";
    public const string EntryState = "Kande olek";
    public const string EmploymentEnd = "Töötamise lõpu aeg";
    public const string Total = "Kokku";
    public const string ByHandSource = "käsitsi";

    // Otsingu kontroll and the category sheets
    public const string SearchPersonalCode = "Isikukood";
    public const string Document = "Dokument";
    public const string Date = "Kuupaev";
    public const string PayeeOnStatement = "Nimi valjavote lehel";
    public const string Masks = "Otsing*";
    public const string DebitCredit = "D/C";
    public const string Amount = "Summa";
    public const string Description = "Selgitus";
    public const string Verdict = "Kontroll/Kategooria";
    public const string Decision = "Otsus";

    // the statement copies
    public const string MatchFound = "Vaste leitud";

    // Deklaratsioon
    public const string Section = "Osa";
    public const string Key = "Võti";
    public const string Action = "Toiming";
    public const string Status = "Olek";
    public const string Person = "Isik";
    public const string PayeeId = "Saaja kood";
    public const string Type = "Liik";
    public const string DocumentDate = "Kuupäev";
    public const string Net = "Neto";
    public const string Gross = "Bruto";
    public const string SocialTax = "SM";
    public const string MinimumIncrease = "SM suurendus";
    public const string FundedPension = "KP";
    public const string UnemploymentEmployee = "TK";
    public const string UnemploymentEmployer = "TTK";
    public const string IncomeTax = "TM";
    public const string ExemptionUsed = "Maksuvaba";
    public const string Source = "Allikas";
    public const string Feedback = "Tagasiside";
    public const string Version = "Versioon";
    public const string Minimum = "miinimum";

    /// <summary>A totals line's source: "koond, liik 10: LHV pank *1757 [2]; käsitsi".</summary>
    public static string TotalsSource(string typeCode, string references) => $"koond, liik {typeCode}: {references.Replace(Review.ReviewWorkspace.ByHandSource, ByHandSource, StringComparison.Ordinal)}";

    /// <summary>What a line stands for, in the accountant's words: the minimum, a hand line, a totals line, a bank row.</summary>
    public static string SourceText(string? sourceRef) =>
        sourceRef == Declare.PeriodDeclarer.MinimumSource ? Minimum
        : sourceRef == Review.ReviewWorkspace.ByHandSource ? ByHandSource
        : Declare.PeriodDeclarer.TotalsOf(sourceRef) is { } totals ? TotalsSource(totals.TypeCode, totals.References)
        : sourceRef ?? "";

    // the person's facts, as the review grid shows them
    public const string Resident = "EE";
    public const string NonResident = "mitteresident";
    public const string NotChecked = "kontrollimata";
    public const string PensionMember = "II sammas";
    public const string PensionNotMember = "ei ole liitunud";
    public const string Unknown = "teadmata";

    // the decision column
    public const string Nobody = "keegi";
    public const string NoCategory = "puudub";
    public const string Declared = "deklareeri";
    public const string NotDeclared = "ei deklareeri";
    public const string Shared = "jagatud:";

    public static string ResidencyText(Store.Person person) =>
        person.ResidencyCheckedOn is null ? NotChecked : person.Residency == Model.Residency.Resident ? Resident : NonResident;

    public static string PensionText(Store.Person person) => person.FundedPensionMember switch
    {
        null => NotChecked,
        true => $"{PensionMember} {Percent(person.PensionRate)}",
        false => PensionNotMember,
    };

    public static string Percent(decimal? rate) => rate is { } r ? (r * 100).ToString("0.##", CultureInfo.InvariantCulture) + " %" : Unknown;

    public static string PaymentStatusText(PaymentStatus status) => status switch
    {
        PaymentStatus.Draft => "mustand",
        PaymentStatus.Sent => "saadetud",
        PaymentStatus.Accepted => "vastu võetud",
        PaymentStatus.NeedsChange => "vajab parandust",
        PaymentStatus.Review => "ootab otsust",
        PaymentStatus.Rejected => "tagasi lükatud",
        PaymentStatus.Cancelled => "tühistatud",
        _ => status.ToString(),
    };

    public static string ActionText(LineAction action) => action switch
    {
        LineAction.R => "R uus",
        LineAction.M => "M muudatus",
        LineAction.T => "T tühistus",
        _ => action.ToString(),
    };
}
