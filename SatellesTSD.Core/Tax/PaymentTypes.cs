using SatellesTSD.Core.Model;

namespace SatellesTSD.Core.Tax;

/// <summary>The payment types a private company declares, with the tax flags of MTA's matrix table
/// (docs/reference/payment-type-mapping.md, section B; the A1 columns read from the matrix rows 4–5, 12–13, 16–17,
/// 20–21, 28–29, 31–32, 44–45, 65–66, 71). Codes 37/38/167/168 are in the matrix and the calculator list but missing
/// from the VALJAMLIIK2023ap dump of 17.10.2025 — kept here because the matrix carries them.</summary>
public static class PaymentTypes
{
    private const Section S1 = Section.EE0201001;
    private const Section S2 = Section.EE0201002;
    private const Residency R = Residency.Resident;
    private const Residency N = Residency.NonResident;
    private const decimal Tm22 = 0.22m;
    private const decimal Tm10 = 0.10m;
    private const A1Kinds No = A1Kinds.None;
    private const A1Kinds Both = A1Kinds.Both;
    private const A1Kinds Foreign = A1Kinds.Foreign;

    //                                                                                                                                        SM     SMmin  zero   UI     KP     TM    MVT    640    treaty A1       SMmin+EE-A1
    public static readonly IReadOnlyList<PaymentType> All =
    [
        new("10",  "Palgatulu residendile",                                                                                           S1, R, true,  true,  true,  true,  true,  Tm22, true,  false, false, Both,    true),
        new("16",  "Muu väljamakse residendist töötajale ja ametnikule, mida maksustatakse ainult tulumaksuga",                      S1, R, false, false, false, false, false, Tm22, true,  false, false, No,      false),
        new("17",  "VÕS lepingu alusel makstud töö- või teenustasu residendile",                                                       S1, R, true,  false, false, true,  true,  Tm22, true,  false, false, Both,    false),
        new("21",  "Juriidilise isiku juhtimis- või kontrollorgani liikme tasu residendile",                                          S1, R, true,  false, false, false, true,  Tm22, true,  false, false, Both,    false),
        new("24",  "Tööandja makstud haigushüvitis residendist töötajale",                                                             S1, R, false, false, false, false, false, Tm22, true,  false, false, No,      false),
        new("25",  "Töötaja või ametniku eest tasutud III samba või PEPP-i lepingu sissemakse residendile",                            S1, R, true,  false, false, true,  true,  Tm22, true,  true,  false, Both,    false),
        new("33",  "Töötajale töölepingu ülesütlemisel või ametnikule teenistusest vabastamisel makstud hüvitis residendile",         S1, R, true,  true,  false, false, true,  Tm22, true,  false, false, Both,    false),
        new("37",  "Töötasu väljamakse residendile, mida maksustatakse ainult sotsiaalmaksuga",                                        S1, R, true,  true,  true,  true,  true,  null, false, false, false, No,      false),
        new("38",  "VÕS lepingu alusel väljamakse residendile, mida maksustatakse ainult sotsiaalmaksuga",                             S1, R, true,  false, false, true,  true,  null, false, false, false, No,      false),
        new("50",  "Renditulu ning litsentsitasu residendile",                                                                         S2, R, false, false, false, false, false, Tm22, true,  false, false, No,      false),
        new("54",  "Intressid residendile",                                                                                            S2, R, false, false, false, false, false, Tm22, true,  false, false, No,      false),
        new("56",  "Põllumajandusmaa renditulu residendile",                                                                           S2, R, false, false, false, false, false, Tm22, true,  false, false, No,      false),
        new("57",  "Eluruumi üüritulu residendile",                                                                                    S2, R, false, false, false, false, false, Tm22, true,  false, false, No,      false),
        new("120", "Palgatulu mitteresidendile",                                                                                       S1, N, true,  true,  true,  true,  false, Tm22, true,  false, false, Both,    true),
        new("123", "VÕS lepingu alusel makstud töö- või teenustasu mitteresidendile",                                                  S1, N, true,  false, false, true,  false, Tm22, true,  false, true,  Both,    false),
        new("129", "Tööandja makstud haigushüvitis mitteresidendist töötajale",                                                        S1, N, false, false, false, false, false, Tm22, true,  false, false, No,      false),
        new("138", "Töölepingu ülesütlemisel töötajale makstav hüvitis mitteresidendile",                                              S1, N, true,  true,  true,  false, false, Tm22, true,  false, false, Both,    true),
        new("156", "Juriidilise isiku juhtimis- või kontrollorgani liikme tasu mitteresidendile",                                     S1, N, true,  false, false, false, false, Tm22, true,  false, false, Foreign, false),
        new("167", "Töötasu väljamakse mitteresidendile, mida maksustatakse ainult sotsiaalmaksuga",                                   S1, N, true,  true,  true,  true,  false, null, false, false, false, No,      false),
        new("168", "VÕS lepingu alusel väljamakse mitteresidendile, mida maksustatakse ainult sotsiaalmaksuga",                        S1, N, true,  false, false, true,  false, null, false, false, false, No,      false),
        new("180", "Tasu mitteresidendist juriidilisele isikule Eestis osutatud teenuse eest",                                        S2, N, false, false, false, false, false, Tm10, false, false, true,  No,      false),
        new("185", "Üüri- või renditasu mitteresidendile Eestis asuva eluruumi kasutamisest",                                          S2, N, false, false, false, false, false, Tm22, true,  false, false, No,      false),
        new("186", "Üüri- või renditasu mitteresidendile, kui vara (v.a kinnisvara) on Eesti registris",                               S2, N, false, false, false, false, false, Tm22, true,  false, true,  No,      false),
        new("205", "Üüri- või renditasu mitteresidendile Eestis asuva kinnisasja kasutamisest",                                        S2, N, false, false, false, false, false, Tm22, true,  false, false, No,      false),
    ];

    private static readonly Dictionary<string, PaymentType> ByCode = All.ToDictionary(t => t.Code, StringComparer.Ordinal);

    public static PaymentType Get(string code) =>
        ByCode.TryGetValue(code, out var type)
            ? type
            : throw new KeyNotFoundException($"Payment type {code} is not in the table.");

    public static bool TryGet(string code, out PaymentType? type) => ByCode.TryGetValue(code, out type);
}
