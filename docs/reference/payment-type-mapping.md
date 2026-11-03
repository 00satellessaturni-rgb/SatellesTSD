# Payment types and tax rules: old TSD Lisa 1/2 workbook → APA TSD (VALJAMLIIK2023ap / TAXCODE_enum)

Written 2026-11-03 for the design of a C# replacement of the Excel VBA workbook. Everything below comes from the local files listed here. Codes, element names and Estonian terms are verbatim. "Not in sources" means none of the files read says it. Where I derived something (simple arithmetic, or matching column titles), it is marked **derived**.

## Sources and short names used below

Root = `C:\Users\satelles-it\AppData\Local\Temp\claude\C--Users-satelles-it-source-claude\6e54ce9b-f5c9-4a76-8b2a-b8ac18e85c74\scratchpad\`

| Short | File (relative to root) | Notes |
|---|---|---|
| **M** | `dump\matrix\sheets\Väljamakseliigid v.a laevapere.txt` | MTA payment-type matrix, sheet rows 3–127. "M r12" = sheet row 12. |
| **M-c** | `rules\Juhendid_ja_arireeglid\APA_TSD_valjamakseliikide maatrikstabel_20251118.xlsx` → `xl/comments1.xml` | Column comments of the matrix (the dump omits them). The legend says they explain how to read each column. I read them from the original xlsx. |
| **MT** | `dump\matrix\sheets\Maksulepingu määrad_tabel.txt` | Tax-treaty rate table |
| **V** | `dump\taksonoomia\sheets\VALJAMLIIK2023ap.txt` | Payment-type classifier |
| **TC / MD / TT** | `dump\taksonoomia\sheets\TAXCODE_enum.txt`, `MEASURABLEDESCRIPTION_enum.txt`, `TOIMINGUTYYP2023ap.txt` | |
| **G** | `dump\pdf\juhend_arendajale_20260130.txt` | Developer guide (dated 30.01.2026). "G:1259" = line 1259. |
| **X** | `dump\pdf\xtee_20260720.txt` | X-tee interface guide v1.3, 20.07.2026. Not on the brief's list; read because it is the newest calculator spec and settles the transport question. |
| **Y-pay / Y-decl** | `rules\Naited_ja_tehnilised_failid\APA_TSD_payments_20260603.yaml`, `APA_TSD_20260603.yaml` | OpenAPI files |
| **K0915 / K0707 / K0130** | `rules\Naited_ja_tehnilised_failid\APA_TSD_kalkulaator_naide_20260915.xml`, `rules\Juhendid_ja_arireeglid\Arhiiv\APA_TSD_kalkulaator_naide_20260707.xml`, `...\Arhiiv\kalkulaator_näide_20260130.xml` | Calculator input examples |
| **KR** | `dump\arireeglid\sheets\KALKULEERIMISE LISAREEGLID.txt` | Calculator business rules |
| **SR** | `dump\arireeglid\sheets\SUBMIT DATA ÄRIREEGLID.txt` | Submission business rules, same workbook (v2.0, 24.07.2026). Extra: it holds the only formulas for each tax. |
| **C** | `dump\palgad\sheets\CONFIG.txt` | Old workbook's CONFIG sheet |
| **VBA** | `dump\palgad\vba\cTsdTable.cls`, `cCalc2026.cls` | Extra. Used only to confirm the column constants and the 2026 tax-free handling. |
| **EX** | `rules\Naited_ja_tehnilised_failid\APA_TSD_lahteandmed_ariuhingule_naide_XML_20251017.xml` | Extra: MTA's business-company submission example. It shows filled tax lines for more cases than G does. |
| **XSD** | `rules\APA_TSD_XSD_naidised\gl-cor-content-2015-03-25-apatsd.xsd` | Extra: checked for `taxBasis` |

---

## A. What the old workbook actually does

### A.1 Payment categories (`{category_config}`, C rows 78–87)

Bank-statement lines are put into categories by the text filters in `{category_filters}` (C rows 56–77; one filter set per bank: Swedbank, LHV, SEB, Coop, Luminor, Kassa …).

| Category (row 79) | Töötajapõhine (r81) | Palk (r82) | TOR/Kategooria kood (r87) | Effect |
|---|---|---|---|---|
| Palgad | y | **y** | – (the code comes from the employee's TÖR employment type) | Goes to Lisa 1/2 through the TÖR id |
| Dividendid | – | n | – | No TSD code configured |
| Üür | – | n | `uur` | Lisa 1 **57**, Lisa 2 **205** |
| Kommunaalkulud | – | n | – | No TSD code configured |
| Kompensatsioon | y | n | – | No TSD code configured |
| Haigusleht | y | n | `haigus` | Lisa 1 **24**, Lisa 2 **129** |
| Laenud | – | n | – | No TSD code configured |
| TÖR-väline palgad | n | n | – | No TSD code configured |
| Trahvid | – | n | – | No TSD code configured (row 85 "Saaja kontroll" = y) |

All categories are D (debit, row 80). The config does not say whether the VBA reports categories that have no code. I did not check that in the VBA.

### A.2 Lisa 1 (residents): `{tsdl1-tor-cat_matrix}` (C rows 107–117)

Flags are y/n. "SM min" = row "Kasuta sotsiaalmaksu minimaalse kohustuse aluseks olev kuumäär".

| TÖR id / category (r108) | Nimetus (r109) | Lisa 1 code (r110) | Tulumaks | Kindlustatu TK | Tööandja TK | Sotsiaalmaks | SM min | Kogumispension | Maksuvaba summa |
|---|---|---|---|---|---|---|---|---|---|
| `1` | Tööleping | **10** | y | y | y | y | **y** | y | y |
| `600` | VÕS leping | **17** | y | y | y | y | n | y | y |
| `507` | Viisa alusel - tööleping | **10** | y | y | y | y | **y** | y | y |
| `602` | Viisa alusel -VÕS leping | **17** | y | y | y | y | n | y | y |
| `uur` | Üür | **57** | y | n | n | n | n | n | **n** |
| `haigus` | Haigusleht | **24** | y | n | n | n | n | n | **n** |
| `601` | Juhtimis-, kontrollorgani liige | **21** | y | n | n | y | n | y | y |
| `autorent` | Autorent | **50** | y | n | n | n | n | n | **n** |
| `501` | Tasuta töötamine | **10** | y | y | y | y | **n** | y | y |
| `a1t` | A1 tööleping (a1t) | **11** | **n** | y | y | y | y | y | n |
| `a1v` | A1 võs leping (a1v) | **19** | **n** | y | y | y | n | y | n |

### A.3 Lisa 2 (non-residents): `{tsdl2-tor-cat_matrix}` (C rows 118–128)

| TÖR id / category (r119) | Nimetus (r120) | Lisa 2 code (r121) | Tulumaks | Kindlustatu TK | Tööandja TK | Sotsiaalmaks | SM min | Kogumispension | Maksuvaba summa |
|---|---|---|---|---|---|---|---|---|---|
| `1` | Tööleping | **120** | y | y | y | y | y | n | n |
| `600` | VÕS leping | **123** | y | y | y | y | n | n | n |
| `507` | Viisa alusel - tööleping | **120** | y | y | y | y | y | n | n |
| `602` | Viisa alusel -VÕS leping | **123** | y | y | y | y | n | n | n |
| `501` | Tasuta töötamine | **120** | y | y | y | y | **n** | n | n |
| `haigus` | Haigusleht | **129** | y | n | n | n | n | n | n |
| `a1t` | A1 tööleping (a1t) | **122** | n | y | y | y | y | n | n |
| `a1v` | A1 võs leping (a1v) | **125** | n | y | y | y | n | n | n |
| `uur` | Üür | **205** | y | n | n | n | n | n | n |

The Lisa 2 matrix has **no entry for `601` (board member) or `autorent`**. A non-resident board member or car-rent payee has no code in the old workbook.

So the old workbook used these codes: **Lisa 1: 10, 11, 17, 19, 21, 24, 50, 57. Lisa 2: 120, 122, 123, 125, 129, 205.**

### A.4 `{taxes_config}` (C rows 104–106) and the cTsdTable column constants

Column constants are confirmed in VBA `cTsdTable.cls` lines 79–89. Sheet column B = index 2, and so on.

| Index | cTsdTable constant | Header (C r105) | Value (C r106) | Meaning / derived monthly figure |
|---|---|---|---|---|
| 2 | `colIncTaxRate` | Tulumaks | **0.22** | Income tax rate 22 % |
| 3 | `colUnEmInsRatePers` | Kindlustatu töötuskindlustusmakse määr | **0.016** | Employee UI 1.6 % |
| 4 | `colUnEmInsRateComp` | Tööandja töötuskindlustusmakse määr | **0.008** | Employer UI 0.8 % |
| 5 | `colSocialTaxRate` | Sotsiaalmaks | **0.33** | Social tax 33 % |
| 6 | `colSocialMinSumMonth` | Sotsiaalmaksu minimaalse kohustuse aluseks olev kuumäär | **886** | Monthly base for the social-tax minimum (€) |
| 7 | `colPensRate` | Kogumispension | **0.02** | II pillar 2 % (one rate for everyone) |
| 8 | `colTaxFreeSumYearCommon` | Maksuvaba tulu aastas | **8400** | Basic exemption per year; derived 700 €/month |
| 9 | `colTaxFreeTier1Year` | Aastatulu piir 1 | **14400** | Tier-1 annual income limit (tapering) |
| 10 | `colTaxFreeTier2Year` | Aastatulu piir 2 | **25200** | Tier-2 annual income limit |
| 11 | `colTaxFreeT2Divider` | Aastatulu jagaja | **10800** | Taper divider |
| 12 | `colTaxFreeSumYearRetired` | Vanaduspensioniiga maksuvaba tulu aastas | **9312** | Retirement-age exemption per year; derived 776 €/month |

Facts from the VBA that matter for the port:
- `cCalc2026.cls` line 391 jumps straight to the flat calculation, with the comment "according to 2026 fixed tax free sum". The taper code is commented out (lines 393–399), so in 2026 the tier parameters (9–11) are read but **not used**.
- The old net→gross formula is in `cCalc2026.cls` lines 405 and 412: gross = (net − r_inc·MVT) / (1 − p − u + r_inc·(p + u − 1)), where p = pension rate and u = employee UI rate. Without tax-free income it is net / (1 − p − u). `curCustomPensRate` (lines 227, 337) lets the 2 % default be overridden per calculation.

### A.5 The old CSV columns (`{tsdl1_config}` C rows 94–98, `{tsdl2_config}` C rows 99–103)

"Arv" = the workbook computes the value (row "Arvutatav (y/n)"). The new-element column is **derived** by matching titles to G and MD.

| Lisa 1 | Lisa 2 | Old title (shortened) | Arv | New APA element |
|---|---|---|---|---|
| 1000 | 2000 | Väljamakse saaja isiku(-/registri)kood | n | `gl-cor:identifierCode` + `identifierDescription` (Estonian isikukood gets an `EE` prefix, G:861–877) |
| 1010 | 2010 | Nimi | n | **None.** The G entryDetail table has no name element. |
| – | 2020 | Väljamakse saaja residendiriik | n | `accountSub` RTK2T2013ap + parent `RR_MAKSURESIDENTSUSE_RIIK` |
| 1020 | 2030 | Väljamakse liigi kood | n | `accountSub` VALJAMLIIK2023ap |
| 1030 | 2040 | Väljamakse summa | n | `gl-cor:amount` (**gross**, see E) |
| 1050 | 2060 | A1/E101 riik | n | `accountSub` A1_RIIGID_enum + parent `RR_A1`, **plus a new required** RTK2T2013ap + `RR_TOOKOHA_ASUKOHT` |
| 1060 | 2070 | Sotsiaalmaksuga maksustatav väljamakse | y | measurable `TSD_SMVM` |
| 1070 | 2080 | …töövõimetuspensionäri tasu osa… | n | measurable `TSD_OPTV_SM_VAHENDUS` |
| 1080 | 2090 | …deklareeritud eelmistel kuudel kuumääralt | n | measurable `TSD_KUUMVAHENDUS` |
| 1090 | 2100 | …suurendus seoses SM miinimum kohustusega | n | measurable `TSD_KUUMSUURENDUS` |
| 1100 | 2110 | Sotsiaalmaks | y | taxes `TSD_SM` |
| 1110 | – | Kinnipeetud kogumispensioni makse | y | taxes `TSD_KP` |
| 1120 | 2120 | Töötuskindlustusmaksega maksustatav väljamakse | y | **None** (no base element; see C) |
| 1130 | 2130 | Kinnipeetud töötuskindlustusmakse | y | taxes `TSD_TK` |
| 1140 | 2140 | Tööandja töötuskindlustusmakse | y | taxes `TSD_TTK` |
| – | 2150 | Tulumaksuga maksustatav väljamakse | y | **None** |
| – | 2160 | Kinnipeetud tulumaksu määr | y | `gl-cor:taxPercentageRate` on the `TSD_KTM` line (required for non-residents) |
| 1170 | 2170 | Kinnipeetud tulumaks | y | taxes `TSD_KTM` |
| 1160_610 | 2154_610 | MVT liik 610 | n | measurable `MVT_610` |
| 1160_640 | – | MVT liik 640 | n | measurable `MVT_640` |
| 1160_650 | 2154_650 | MVT liik 650 | n | measurable `MVT_650` |

New in APA with no old column: `lineNumber` (a unique id per payment), TOIMINGUTYYP2023ap (`R` new / `M` change / `T` cancel, TT r8–10), `documentDate` (payment date, G:883), the SYNDMUS2017ap `TSD01` "Arvestuse kuupäev" measurable (shown in every G/EX example; SR KR_YLD_25 says it may not be later than the payment date), `RR_TOOKOHA_ASUKOHT`, and the section `entryNumber` (EE0201001 / EE0201002).

### A.6 `{system_data}` (C rows 129–134)

This block holds only system texts ("kogumispensioni sissemaksed lõppenud", "on lõppenud") and first-name alias lists used for name matching. It has no tax parameters.

---

## B. New mapping (old code → VALJAMLIIK2023ap) with the matrix's tax conditions

How to read the matrix (from the M-c comments):
- **J** `RR_A1` country: EE = Estonia, V = a foreign A1 country (A1_RIIGID_enum), EI = not given.
- **K** `RR_TOOKOHA_ASUKOHT`: work-location country (RTK2T2013ap).
- **L** SM rate: a number, or EI = no social tax.
- **M** Sotsiaalmaks kuumääralt: the monthly minimum obligation applies. No comment explains M; its meaning comes from the header.
- **N** amount "0" allowed.
- **O** `TSD_OPTV_SM_VAHENDUS` (partial work-capacity reduction) allowed.
- **P** `TSD_KUUMVAHENDUS` / `TSD_KUUMSUURENDUS` allowed.
- **Q** `TSD_TTK` / `TSD_TK` computed.
- **R** `TSD_KP` computed.
- **S** income-tax rate, or EI.
- **T** treaty rate allowed: "JAH_viide tabelile" = see MT.
- **U** treaty article.
- **V** `MVT_610` / `MVT_650` allowed.
- **W** `MVT_640` allowed.

Every payment type with social tax also needs the `TSD_SMVM` measurable (SR KR_SM_01_1).

Sect: 1 = EE0201001 (tööjõumaksudega seotud), 2 = EE0201002 (hüvitised, toetused jm). "Calc" = listed as supported in the calculator (G:1283–1361). WB = the old workbook emits this old code.

| Old | **New** | VALJAMLIIK2023ap name_ET | validFrom (V) | Sect | Who | J A1 | K work | L SM | M min | N 0 | O | P | Q TK/TTK | R KP | S TM | T treaty | U art | V MVT 610/650 | W 640 | Calc | WB | M row |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 10 | **10** | Palgatulu residendile | 01/01/2015 | 1 | R FÜS | EI | EI | .33 | JAH | JAH | JAH | JAH | JAH | JAH | .22 | EI | – | JAH | EI | yes | **WB** | 3 |
| 11 | **10** + RR_A1=EE + work=V | (same) | | 1 | R | EE | V | .33 | JAH | JAH | JAH | JAH | JAH | JAH | **EI** | EI | – | EI | EI | code yes, A1 no | **WB** | 4 |
| 12 | **10** + RR_A1=V + work=EE | (same) | | 1 | R | V | EE | EI | EI | EI | EI | EI | EI | EI | .22 | EI | – | JAH | EI | code yes, A1 no | | 5 |
| 16 | **16** | Muu väljamakse residendist töötajale ja ametnikule, mida maksustatakse ainult TM | 01/01/2015 | 1 | R | EI | EI | EI | EI | EI | EI | EI | EI | EI | .22 | EI | – | JAH | EI | yes | | 9 |
| 17 | **17** | VÕS lepingu alusel makstud töö- või teenustasu residendile | 01/01/2015 | 1 | R | EI | EI | .33 | EI | EI | EI | EI | JAH | JAH | .22 | EI | – | JAH | EI | yes | **WB** | 11 |
| 19 | **17** + RR_A1=EE + work=V | (same) | | 1 | R | EE | V | .33 | EI | EI | EI | EI | JAH | JAH | **EI** | EI | – | EI | EI | code yes, A1 no | **WB** | 12 |
| 20 | **17** + RR_A1=V + work=EE | (same) | | 1 | R | V | EE | EI | EI | EI | EI | EI | EI | EI | .22 | EI | – | JAH | EI | code yes, A1 no | | 13 |
| 24 | **24** | Tööandja makstud haigushüvitis residendist töötajale | 01/01/2015 | 1 | R | EI | **V** (sic) | EI | EI | EI | EI | EI | EI | EI | .22 | EI | – | JAH | EI | yes | **WB** | 14 |
| 25 | **25** | Töötaja või ametniku eest tasutud III samba või PEPP-i lepingu sissemakse residendile | 01/01/2015 | 1 | R | EI | EI | .33 | EI | EI | EI | EI | JAH | JAH | .22 | EI | – | JAH | **JAH** | no | | 15 |
| 33 | **33** | Töötajale töölepingu ülesütlemisel … makstud hüvitis residendile | 01/01/2015 | 1 | R | EI | EI | .33 | JAH | EI | JAH | JAH | **EI** | JAH | .22 | EI | – | JAH | EI | yes | | 19 |
| (none) | **37** | töötasu väljamakse residendile, mida maksustatakse ainult sotsiaalmaksuga | **not in V** | 1 | R | EI | EI | .33 | JAH | JAH | JAH | JAH | JAH | JAH | EI | EI | – | EI | EI | yes | | 23 |
| (none) | **38** | VÕS lepingu alusel väljamakse residendile, mida maksustatakse ainult sotsiaalmaksuga | **not in V** | 1 | R | EI | EI | .33 | EI | EI | EI | EI | JAH | JAH | EI | EI | – | EI | EI | yes | | 24 |
| 21 | **21** | Juriidilise isiku juhtimis- või kontrollorgani liikme tasu residendile | 01/01/2015 | 1 | R | EI | EI | .33 | EI | EI | EI | EI | **EI** | JAH | .22 | EI | – | JAH | EI | yes | **WB** | 64 |
| 22 | **21** + RR_A1=EE + work=V | (same) | | 1 | R | EE | V | .33 | EI | EI | EI | EI | EI | JAH | EI | EI | – | EI | EI | code yes, A1 no | | 65 |
| 23 | **21** + RR_A1=V + work=EE | (same) | | 1 | R | V | EE | EI | EI | EI | EI | EI | EI | EI | .22 | EI | – | JAH | EI | code yes, A1 no | | 66 |
| 156 | **156** | Juriidilise isiku juhtimis- või kontrollorgani liikme tasu mitteresidendile | 01/01/2015 | 1 | MR FÜS | EI | EI | .33 | EI | EI | EI | EI | EI | EI | .22 | EI | 16 | JAH | EI | yes | (no WB mapping) | 70 |
| 157 | **156** + RR_A1=V | (same) | | 1 | MR | V | **EI** | EI | EI | EI | EI | EI | EI | EI | .22 | EI | 16 | JAH | EI | code yes, A1 no | | 71 |
| 120 | **120** | Palgatulu mitteresidendile | 01/01/2015 | 1 | MR FÜS | EI | EI | .33 | JAH | JAH | JAH | JAH | JAH | **EI** | .22 | EI | 15 | JAH | EI | yes | **WB** | 27 |
| 121 | **120** + RR_A1=V + work=EE | (same) | | 1 | MR | V | EE | EI | EI | EI | EI | EI | EI | EI | .22 | EI | 15 | JAH | EI | code yes, A1 no | | 28 |
| 122 | **120** + RR_A1=EE + work=V | (same) | | 1 | MR | EE | V | .33 | JAH | JAH | JAH | JAH | JAH | EI | **EI** | EI | – | EI | EI | code yes, A1 no | **WB** | 29 |
| 123 | **123** | VÕS lepingu alusel makstud töö- või teenustasu mitteresidendile | 01/01/2015 | 1 | MR FÜS | EI | EI | .33 | EI | EI | EI | EI | JAH | EI | .22 | **JAH_viide tabelile** | 14 | JAH | EI | yes | **WB** | 30 |
| 124 | **123** + RR_A1=V + work=EE | (same) | | 1 | MR | V | EE | EI | EI | EI | EI | EI | EI | EI | .22 | JAH_viide tabelile | 14 | JAH | EI | code yes, A1 no | | 31 |
| 125 | **123** + RR_A1=EE + work=V | (same) | | 1 | MR | EE | V | .33 | EI | EI | EI | EI | JAH | EI | **EI** | EI | – | EI | EI | code yes, A1 no | **WB** | 32 |
| 129 | **129** | Tööandja makstud haigushüvitis mitteresidendist töötajale | 01/01/2015 | 1 | MR FÜS | EI | EI | EI | EI | EI | EI | EI | EI | EI | .22 | EI | 15 | JAH | EI | yes | **WB** | 36 |
| 138 | **138** | Töölepingu ülesütlemisel töötajale makstav hüvitis mitteresidendile | 01/01/2015 | 1 | MR | EI | EI | .33 | JAH | JAH | JAH | JAH | EI | EI | .22 | EI | 15 | JAH | EI | yes | | 43 |
| (none) | **167** | töötasu väljamakse mitteresidendile, mida maksustatakse ainult sotsiaalmaksuga | **not in V** | 1 | MR | EI | EI | .33 | JAH | JAH | JAH | JAH | JAH | EI | EI | EI | – | EI | EI | yes | | 61 |
| (none) | **168** | VÕS lepingu alusel väljamakse mitteresidendile, mida maksustatakse ainult sotsiaalmaksuga | **not in V** | 1 | MR | EI | EI | .33 | EI | EI | EI | EI | JAH | EI | EI | EI | – | EI | EI | yes | | 62 |
| 50 | **50** | Renditulu ning litsentsitasu residendile | 01/01/2016 | 2 | R FÜS | EI | EI | EI | EI | EI | EI | EI | EI | EI | .22 | EI | – | JAH | EI | yes | **WB** (autorent) | 101 |
| 54 | **54** | Intressid residendile | 01/01/2015 | 2 | R FÜS | EI | EI | EI | EI | EI | EI | EI | EI | EI | .22 | EI | – | JAH | EI | yes | | 102 |
| 56 | **56** | Põllumajandusmaa renditulu residendile | 01/01/2015 | 2 | R FÜS | EI | EI | EI | EI | EI | EI | EI | EI | EI | .22 | EI | – | JAH | EI | yes | | 104 |
| 57 | **57** | Eluruumi üüritulu residendile | 01/01/2016 | 2 | R FÜS | EI | EI | EI | EI | EI | EI | EI | EI | EI | .22 | EI | – | JAH | EI | yes | **WB** (Üür) | 105 |
| 185 | **185** | Üüri- või renditasu mitteresidendile Eestis asuva eluruumi kasutamisest … | 01/01/2015 | 2 | MR FÜS+JUR | EI | EI | EI | EI | EI | EI | EI | EI | EI | .22 | EI | 6 | JAH | EI | **no** | | 107 |
| 205 | **205** | Üüri- või renditasu mitteresidendile Eestis asuva kinnisasja kasutamisest … | 01/01/2021 | 2 | MR FÜS+JUR | EI | EI | EI | EI | EI | EI | EI | EI | EI | .22 | EI | 6 | JAH | EI | **no** | **WB** (Üür) | 108 |
| 186 | **186** | Üüri- või renditasu mitteresidendile, kui vara (va kinnisvara) on Eesti registris | 01/01/2015 | 2 | MR FÜS+JUR | EI | EI | EI | EI | EI | EI | EI | EI | EI | .22 | JAH_viide tabelile | 21 | JAH | EI | **no** | | 109 |
| 180 | **180** | Tasu mitteresidendist juriidilisele isikule Eestis osutatud teenuse eest | 01/01/2015 | 2 | MR **JUR** | EI | EI | EI | EI | EI | EI | EI | EI | EI | **.1** | JAH_viide tabelile | 7 | EI | EI | **no** | | 124 |
| Dividends | **none** | Not in M, not in V. The calculator says "/puudub/ - DIVIDEND" (G:1361) and that taxable dividends cannot be sent through the new service for now (G:1275). | | | | | | | | | | | | | | | | | | **no** | category "Dividendid" has no code | |

The "Calc" column follows G §5, which lists 10, 120, 17, 123, 21, 156, 24, 129, 14, 15, 16, 33, 138, 37, 167, 38, 168, 50, 54, 55, 56, 57. No A1 variant is supported: the calculator assumes taxes are paid in Estonia (G:1234–1237, X:191–193). The matrix's own payment-type table has no "used in calculator" column. G:223 says the matrix contains one, but it is missing from this 20251118 version.

### B.1 Per-row notes and conditions

1. **A1 cases are no longer separate codes.** Old 11/19/22/122/125 (Estonian A1, work abroad) and 12/20/23/121/124/157 (foreign A1, work in Estonia) map to the base code. You then add two `accountSub` entries (EX lines 183–207):
   - A1_RIIGID_enum with `segmentParentTuple` `RR_A1` / `RIIGIROLL2022ap`;
   - RTK2T2013ap with `RR_TOOKOHA_ASUKOHT`.

   The V explanations for 10, 17, 21, 25, 33, 120, 123, 126, 132, 138, 144, 147, 150, 152, 156, 158, 164 say the same. The rules that apply are SR KR_YLD2_01–09:
   - A1 set → work location required;
   - foreign work location → A1 must be set and must be EE;
   - A1 = EE → work location must not be EE.
2. **Matrix rows that conflict with those rules.** M r71 (old 157 → 156), r73 (old 159 → 158) and r95 (old 165 → 164) have J = V but K = EI, so an A1 country with no work location. That contradicts KR_YLD2_06. M r14 (24, employer sick pay) has J = EI but K = V, which looks like a data slip in the matrix. Ask MTA.
3. **Non-residents (MR_1).** Every MR payment must also carry RTK2T2013ap with `RR_MAKSURESIDENTSUSE_RIIK` (V r47 explanation; EX lines 297–311). SR KR_ISIK_6 checks it against MTA's register. For MR `TSD_KTM`, `taxPercentageRate` is required, e.g. 0.22 (G:943–951; EX line 347).
4. **Treaty rates.** These new codes have T = "JAH_viide tabelile": 123, 144, 147, 150, 152, 160, 162, 164, 166, 171–176, 178, 179, 180, 182–184, 186–194, 196–199, 201–203, 207. Exceptions: the A1 variants for old 125/146/149/154 have T = EI; 185 and 205 have T = EI. The rate comes from MT by country and payment-type column:
   - C8 "123 VÕS leping, art 14";
   - D8 "180 teenustasu püsiva tegevuskohata, art 7";
   - … V8 "196,198 intress".

   It applies only with a residency certificate. Without one, the TuMS rate in MT row 9 applies (MT A2–A3, B5–B6). The old workbook has no treaty table, so it always used 22 %.
5. **37 / 38 / 167 / 168.** These are new SM-only salary types. The matrix marks them "Tänases TSD-s väljamakseliiki ei ole" (old code 0), and the calculator lists them (G:1333–1346). They are **absent from the VALJAMLIIK2023ap dump**, which lists 10…74, 120…273 and 611…6213. This is a conflict between sources.
6. **Social-tax minimum (M) vs. "0 allowed" (N).** Among the codes the WB used:
   - 10 and 120 allow a 0 amount and carry the minimum;
   - 11 and 122 carry the minimum too;
   - 17, 19, 21, 123, 125, 24, 129, 50, 57, 205 have neither.

   SR KR_SM_11 / KR_SM_14 forbid `TSD_KUUMSUURENDUS` and `TSD_SMVMSK` on types with no minimum.
7. **Unemployment insurance** is one matrix column (Q) for both `TSD_TK` and `TSD_TTK`, whereas the WB has two flags; they have the same value on every WB row. SR KR_TK_2 drops the **employee** `TSD_TK` when the payee is on flexible or old-age pension as of the month before the payment. EX lines 160–177 show this: a retired payee gets `TSD_TTK` but no `TSD_TK` and no `TSD_KP`.
8. **Funded pension.** Q/R are JAH for residents' 10/17/21/33. For every MR row in the table, R is EI (non-residents never have `TSD_KP`), which matches the WB Lisa 2 "Kogumispension = n". SR KR_KOP_02 forbids `TSD_KP` when the payee has no II-pillar obligation.
9. **Basic exemption on non-residents** (V = JAH on 120/123/129/138/156/185/205 …). It is allowed only when the payee is an EEA tax resident with a valid EEA residency certificate (SR KR_MVT_06 / KR_MVT_07).

### B.2 Old workbook vs. matrix: where they disagree

The WB flags match M on every row it used except these:
- **Basic exemption.** The WB gives `n` on 57 (Üür), 24 (Haigusleht), 50 (Autorent) and on all of Lisa 2 (120, 123, 129, 205). M V says JAH for all of these. The matrix allows it; whether to apply it depends on the person's application, and SR KR_KOOND_04 talks of the "maksuvaba tulu arvestamise avaldus". The WB rule is therefore stricter than the law. Accountant question.
- **501 Tasuta töötamine → 10 / 120 with SM min = n.** M says code 10/120 carries the minimum (M = JAH). The matrix gives the minimum per payment type, not per person category.
- **Üür.** Residents get 57 (eluruumi üüritulu), but non-residents get 205 (kinnisasja). The dwelling counterpart for non-residents is 185, which the calculator does not support. 205 is not supported either.
- **No Lisa 2 mapping** for board members (→ 156) or car rent (→ 186 is the matrix row for "vara (va kinnisvara) on Eesti registris"; picking it for car rent is my inference, so ask the accountant).
- **123:** treaty rate possible (T), but the WB has no treaty data.

---

## C. The taxes block per payment

### C.1 TAXCODE_enum (TC rows 8–12; all valid from 01/01/2015, unit EUR)

| Code | name_ET / name_EN | Withheld or employer's (from the name) | Allowed in sections (TC Z/AA/AB) | Amount rule (SR) | Where the base is |
|---|---|---|---|---|---|
| `TSD_SM` | Sotsiaalmaks / Social tax | Employer-calculated (not "kinnipeetud") | EE0201001*, EE0201002*, EE0201003 | KR_SM_04: (`TSD_SMVM` − `TSD_OPTV_SM_VAHENDUS` − `TSD_KUUMVAHENDUS` + `TSD_KUUMSUURENDUS` − `TSD_SMVMSK`) × SM rate | Measurable **`TSD_SMVM`**, which must equal `gl-cor:amount` except for seafarers (KR_SM_01_2), plus the adjustment measurables |
| `TSD_KP` | Kinnipeetud kohustusliku kogumispensioni makse | Withheld | 1*, 2*, 3* (3 only for the extra contribution) | KR_KOP_03: `TSD_SMVM` × the person's rate ("isiku määr") | Same `TSD_SMVM`. No separate base. |
| `TSD_TK` | Kinnipeetud töötuskindlustusmakse | Withheld | 1* only | KR_TK_3: payment amount × TK rate | `gl-cor:amount`. No base element. |
| `TSD_TTK` | Tööandja töötuskindlustusmakse | Employer-calculated | 1* only | KR_TTK_2: payment amount × TTK rate | `gl-cor:amount` |
| `TSD_KTM` | Kinnipeetud tulumaks | Withheld | 1*, 2* | KR_TM_4: (amount − `TSD_KP` − `TSD_TK` − maksuvaba tulu) × rate | Computed, not reported. Rate in `taxPercentageRate` (required for MR). |

`*` = "Jah* tingimuslik", i.e. required only for some payment types.

### C.2 Element structure

- Per G:935–957 and XSD lines 546–552, each `gl-cor:taxes` holds only `taxAmount` (1, decimals 2, unit eur), `taxPercentageRate` (0..1, unit pure, e.g. 0.22) and `taxCode` (1).
- **There is no `taxBasis` element.** The XSD keeps only an unused `taxBasisItemType` (line 581). Social tax is therefore **not** reported with its base inside the tax line. The base is the `TSD_SMVM` **measurable** (`gl-bus:measurableDescription` + `measurableQuantity` in eur; G:886–916).
- **The basic exemption is a measurable** (`MVT_610`, `MVT_650`, `MVT_640`), not a tax line (MD r22–24; G example lines 1031–1110).
- Each tax code may appear at most once per payment (SR KR_XBRL_VALJAMAKSED_19), and likewise each measurable type (…_18).
- Amounts are ≥ 0, or ≤ 0 for a refund marked with the SYNDMUS2017ap `TSD02` date (SR KR_YLD_30 / KR_YLD_24 / KR_YLD_14; EX lines 463–501).
- The rules accept a deviation from the system's own calculation ("lubatud hälbe piires"), but **the size of that tolerance is not in the sources**.

### C.3 Worked figures from the sources (all checked arithmetically)

| Case | Amount | Measurables | Tax lines |
|---|---|---|---|
| G:1031–1110 / EX 55–101: code 10 | 1000 | MVT_610 700, TSD_SMVM 1000 | SM 330, KP 20, TK 16, TTK 8, KTM 58.08 = (1000−20−16−700)×0.22 |
| EX 139–177: code 10, retired | 900 | MVT_650 776, TSD_SMVM 900 | SM 297, TTK 7.2, KTM 27.28 = (900−776)×0.22. No TK, no KP. |
| EX 185–239: code 10, A1=EE, work=FI | 1000 | TSD_SMVM 1000 | SM 330, KP 20, TK 16, TTK 8. **No KTM** (matrix S = EI). |
| EX 247–289: code 10, A1=LV, work=EE | 1000 | MVT_610 700 | Only KTM 66 = (1000−700)×0.22 |
| EX 297–348: code 120, residency LV | 1000 | MVT_610 700, TSD_SMVM 1000 | SM 330, TK 16, TTK 8, KTM 62.48 at `taxPercentageRate` 0.22. No KP. |
| EX 356–382: code 10, amount 0 | 0 | TSD_KUUMSUURENDUS **820** | SM 270.60 = 820×0.33. Has **no TSD_SMVM**, although KR_SM_01_1 requires it: an inconsistency. |
| EX 109–131: code 24 | 100 | – | KTM 22 |
| EX 512–538: code 50 | 100 | MVT_650 50 | KTM 11 |

---

## D. The calculator service ("väljamakse maksude kalkulaator")

**What it does.** It computes taxes from either the **net or the gross** amount of each payment (G:1234–1235). It assumes the taxes are paid to Estonia; the special rules for A1 countries and work-location countries are not included (G:1235–1237; X:191–193). It replaces the old TSD2 bank salary-payment calculator and extends it to ERP systems (G:1238–1240).
- Changes from TSD2 (G:1271–1276): a separate field for the social-tax monthly minimum; the tax-free amount must be an exact sum, not true/false; taxable dividends cannot be sent for now.

**Transport: X-tee only, as far as the sources go.**
- Y-pay calls itself "Public X-Road REST API". Its only `servers` URL is `https://{securityserver}/r1/{xRoadInstance}/{memberClass}/{memberCode}/{subsystemCode}` (the X-Road security server host). The `X-Road-Client` header is required, and a 401 means "Not authenticated at X-Road layer".
- X:70–72 says the document describes MTA's **X-Tee** interfaces, including the calculator. X:108–109 says calculator processing is synchronous (submissions are asynchronous).
- G §5 says nothing about transport.
- **No source mentions any non-X-tee (public HTTPS, e-MTA) access to the calculator. Using it without X-tee: not in sources.** The only documented option for a company without its own security server is to go through a service provider's security server with `X-Road-Represented-Party`. The provider must hold MTA authorization (X:165–182; KR KR_KALK_OIGUS_01). That route is still X-tee.

**Endpoint: the sources conflict.**
- Y-pay (03.06.2026): subsystem `apa-tsd/payments`, `POST /v1/xtee/calculate-taxes`.
- X v1.3 (20.07.2026, "Uuendatud X-Tee teenuste alamsüsteemide ja otspunktide nimetused"): subsystem **`apa-tsd/calculator`**, **`POST /v1/xtee/tax-calculations`** (X:128, 136, 190, curl X:220–226).
- Environments: `EE` production, `ee-test`, `ee-dev` (X:150–154).

**Request.** `multipart/form-data` with one `file` part (`application/xml` or `text/xml`) holding an XML document. Root element: `taxCalculation` in K0707/K0915, `message` in K0130 (a conflict). Fields:

| Element | Type / count | Meaning | Notes |
|---|---|---|---|
| `representativeIdentifier` | string 1 | Taxpayer's representative code | |
| `taxablePersonIdentifier` | string 1 | The payer (taxpayer) | Must match the message header (KR_KALK_XML_FAIL_04) |
| `sourceApplication` | string 1 | Calling software | Only in X v1.3 (X:281) and K0707/K0915; missing from G |
| `language` | 0..1 | EST/ENG/RUS, default EST | Examples use `et`: a conflict |
| `payment` | 1..n | | |
| ↳ `identifier` | 0..1 | Payment id | Generated if absent (X v1.3) |
| ↳ `payeeIdentifier` | 1 | Isikukood (with `EE` prefix in examples) or MRR / registry code | |
| ↳ `paymentType` | 1 | VALJAMLIIK code from the restricted list (§B "Calc") | KR_KALK_YLD_01, SININE |
| ↳ `paymentAmount` | decimal 1 | The amount | |
| ↳ `netPayment` | boolean 1 | true = `paymentAmount` is net, false = gross | |
| ↳ `socialTaxBaseAmount` | decimal 0..1 | G:1259: "Sotsiaalmaksu summa aluseks olev kuumäär". X:300–303: the **increase** due to the minimum obligation (= `TSD_KUUMSUURENDUS`). | Conflict. K0130/K0707 put 900 next to 900 (base reading); K0915 has 500 net + **386** and 0 + **886** (increase reading). |
| ↳ `taxExemptionAmount` | decimal 0..1 | The exact tax-free sum wanted | Examples: 700 and 776 (and 50 on code 50) |
| ↳ `executionDate` | date 1 | Payment date | |
| ↳ `accountingDate` | date 0..1 | If empty, the current date | |

There is no input field for the person's pension rate, pension status or residency. The calculator presumably uses MTA's register; the sources do not say so.

**Response.** HTTP 200, `application/xml`: an XBRL GL document containing the payments with the computed taxes (Y-pay; X:196–197, 246–247). G:1277–1281 says the output is suitable for submitting payment data and for reporting tax amounts. It "does not persist payments nor trigger declaration confirmation" (Y-pay). **No response-body example exists in the sources**; X §3.3 shows only the headers.

**Limits and errors.**
- Synchronous, "limited payload size". The maximum size is the system parameter `uploadXRoadFileMaxSize`, whose value is not in the sources.
- KR checks:

  | Rule | Check | Result |
  |---|---|---|
  | KR_KALK_XML_FAIL_01 | File must be XML | HTTP 415 |
  | KR_KALK_XML_FAIL_02 | Size limit | HTTP 413 |
  | KR_KALK_XML_FAIL_03 | Must validate against an XSD | The calculator XSD is not in the sources |
  | KR_KALK_XML_FAIL_04 | Taxpayer must match the header | |
  | KR_KALK_XML_FAIL_05 | **All payments in one file must fall in the same calendar month** | |
  | KR_KALK_OIGUS_01 | Authorization to represent | HTTP 403 |
  | KR_KALK_YLD_01 | Payment type must be in the allowed list | SININE |
  | KR_KALK_KOOND_01 | Tax-free sum must not exceed the person's limit within this payer | PUNANE |

- Y-pay lists 400 / 401 / 403 / 500 for the calculator.
- Unsupported: A1 and work-location specifics, dividends, and every type not on the G §5 list. That includes 185, 186 and 205 (non-resident rent), 25 (III pillar) and 180.

---

## E. Net to gross: who grosses up?

- **The calculator accepts net.** `netPayment` (xs:boolean, required) says whether `paymentAmount` is net or gross (G:1261–1262; X:305–306). The K0915 example sends `netPayment` true with 1000 and `taxExemptionAmount` 700.
- **The declaration (submit-data XBRL) carries gross amounts plus the taxes.** `gl-cor:amount` is the payment amount (G:850–852).
  - It is gross because SR KR_SM_01_2 requires `TSD_SMVM` = `amount`.
  - KR_TM_4 computes income tax as (amount − KP − TK − MVT) × rate.
  - KR_MVT_08 caps MVT at amount − KP − TK.
  - The G example has 1000 with taxes 330/20/16/8/58.08, which is only consistent with 1000 being gross.
  - No source describes a net field in the submission.
- **Must the submitter supply the taxes?**
  - The G taxes block is "Tingimuslik 0..n" (G:935).
  - The SR checks are all of the form "if a tax is given, it must equal the system's value within tolerance" (KR_SM_04, KR_TK_3, KR_TTK_2, KR_KOP_03, KR_TM_4), or "must not be given if the type has no such tax".
  - The only presence requirement is `TSD_SMVM` (KR_SM_01_1); for EE0201003 there are also KR_ESM_01 / KR_ESM_02.
  - **The sheets read have no rule requiring `TSD_SM` / `TSD_KTM` etc. to be present for EE0201001, and nothing says MTA fills in taxes that are left out.** The Juhis sheet says "Maksusummad koondatakse TSD koondvormile" without saying whose figures are used. → Open question G-8.
- **Practical consequence for a bank-statement (net) workflow.** Either call the calculator (net in, XBRL with gross and taxes out; X-tee required), or keep the old local gross-up (`cCalc2026.cls` line 412) and submit gross plus computed taxes. Locally, A1 cases (old 11/19/122/125) **must** be computed by you, because the calculator excludes them.

---

## F. Rates and thresholds (2026) found in the sources

| Item | Value | Source | Remarks |
|---|---|---|---|
| Income tax, standard | **22 %** | M col S (most rows); C B106; MT row 9 "TuMS määr"; G example (58.08); EX `taxPercentageRate` 0.22 | No other standard rate appears for 2026 |
| Income tax, reduced | **10 %** | M S, new codes: 48, 49, 160, 207 (pillar payouts in pension age); 150, 152, 183, 184 (performers; old 151/153 map to 150/152); 180, 182, 187–194 | |
| Treaty rates | per country | MT | Only with a residency certificate |
| Social tax | **33 %** | M col L; C E106; G example 330/1000 | 13 % on 41 and 173 (Töötukassa töötuskindlustushüvitis), M r76 / r82 |
| SM monthly minimum base | **886 €** | C F106; K0130/K0707/K0915 `socialTaxBaseAmount` 886 (payments dated 2026-09/10) | **Conflict:** EX (period 2026-09) uses `TSD_KUUMSUURENDUS` **820**. SR refers to `SM_KUUMAAR` from a classifier "MTA.CALCULATION_BASIS" whose value is not in the sources. |
| SM caps | `TSD_OPTV_SM_VAHENDUS`, `TSD_KUUMVAHENDUS`, `TSD_KUUMSUURENDUS` each ≤ **2 × SM_KUUMAAR** per payment and per month cumulatively | SR KR_SM_07/12, KR_SM_KOOND_01–03 | |
| Unemployment insurance, employee | **1.6 %** | C C106; G example 16/1000 | Not in M. Not withheld for flexible / old-age pensioners (KR_TK_2). |
| Unemployment insurance, employer | **0.8 %** | C D106; G example 8/1000 | |
| Funded pension (II pillar) | **2 %** | C G106; G example 20/1000; EX 15/750 on code 70 | SR KR_KOP_03 uses "<isiku määr>" (a per-person rate). **4 % and 6 %: not in sources.** No rate field in the calculator input. |
| Basic exemption (`MVT_610`) | **8400 €/yr** (700 €/month) | C H106; 700 in G, EX, K* examples | Tier parameters 14400 / 25200 / 10800 are still in C but bypassed in `cCalc2026.cls` line 391 ("according to 2026 fixed tax free sum"). **The 2026 law change itself is not in sources**; the VBA comment is the only hint. |
| Retirement-age exemption (`MVT_650`) | **9312 €/yr** (776 €/month) | C L106; 776 in EX, K0707, K0130 | MD r24: for a person who reaches old-age pension age during the calendar year. SR KR_MVT_04/05: from 1 January of that year 610 is forbidden and 650 required. **The pension age itself is not in sources.** |
| III-pillar exemption (`MVT_640`) | ≤ **15 %** of income-taxed payments in the year, and ≤ **6000 €/yr** | SR KR_KOOND_01, KR_KOOND_06 | |
| MVT per payment | ≤ amount − KP − TK | SR KR_MVT_08 | Monthly legal limits checked per payer (KR_KOOND_02/03) and across payers (warning, KR_KOOND_04/05). Calculator: KR_KALK_KOOND_01. |
| MVT for non-residents | Only EEA residents with a valid residency certificate | SR KR_MVT_06/07 | |
| Late changes | No adding or changing prior-year payments with income tax after 15.02 once the TSD is confirmed | SR KR_TM_5, KR_TM_2, KR_XBRL_VALJAMAKSED_8 | Exceptions: MR legal persons, third-country residents |

Not in sources: declaration and payment deadlines, the actual 2026 pension age, the tolerance and rounding rule, and the value of `uploadXRoadFileMaxSize`.

---

## G. Questions to put to MTA (tax authority) or the accountant

**MTA**
1. Is the calculator reachable **only via X-tee**? Is there any public HTTPS or e-MTA route? If not, is going through an ERP or service provider's security server (`X-Road-Represented-Party`) the expected path for a small company?
2. Which endpoint is current: `apa-tsd/payments` `/v1/xtee/calculate-taxes` (YAML 2026-06-03) or `apa-tsd/calculator` `/v1/xtee/tax-calculations` (X-tee guide v1.3)? Please supply the updated OpenAPI file and the **calculator input XSD** (KR_KALK_XML_FAIL_03). The root element differs between examples (`taxCalculation` vs `message`), as does `language` (`EST` vs `et`).
3. Is `socialTaxBaseAmount` the monthly base or the increase? Examples 900/900 vs 500/386 vs 0/886.
4. Please supply a **sample calculator response**. Can it be submitted to `submit-data` unchanged?
5. With `netPayment=true`, which pension rate (2/4/6 %), pension status and residency does the calculator use? None of them is an input.
6. Codes 37/38/167/168 are in the matrix and the calculator list but **missing from VALJAMLIIK2023ap**. From when are they valid?
7. Matrix data issues:
   - 24 has K = V without J;
   - 157/159/165 have A1 = V without a work location, which breaks KR_YLD2_06;
   - the matrix version has no "calculator" column, although G says it does.
8. Can a submission leave out the `taxes` lines and let MTA compute them? Whose figures are aggregated into the TSD form? What is the allowed deviation ("lubatud hälbe piires") and the rounding rule?
9. What is `SM_KUUMAAR` for 2026: 886 or 820 (EX)? Will MTA.CALCULATION_BASIS be published?
10. KR_KALK_XML_FAIL_05 allows only one calendar month per calculator file. Confirm.
11. What should `TSD01` (arvestuse kuupäev) be when only the bank-statement date is known? It may not be later than `documentDate`.
12. Where are dividends declared now? They are absent from VALJAMLIIK2023ap; the calculator says DIVIDEND is unavailable "for now".

**Accountant**

13. Non-resident rent: 205 (kinnisasi) or 185 (eluruum)? Residents use 57 (eluruum). Neither 185 nor 205 is supported by the calculator.
14. Non-resident car rent (old: no mapping): 186? Non-resident board member (old: no mapping): 156?
15. Basic exemption on rent / sick pay / car rent and for EEA non-residents: the matrix allows it; the old workbook never applied it. Keep "never", or follow the person's application?
16. `501` Tasuta töötamine: should the social-tax minimum apply (the matrix says the type carries it; the old workbook said no)?
17. Per-employee II-pillar rate (2/4/6 %) and "contributions ended" status: where is it recorded? The old workbook had a single 2 % plus a per-calculation override.
18. Treaty rates for 123 (art 14) and the other T = JAH types: which payees have residency certificates?
19. Categories with no TSD code (Kompensatsioon, TÖR-väline palgad, Kommunaalkulud, Laenud, Trahvid, Dividendid): confirm none needs to be declared in TSD Lisa 1/2 or EE0201001/2.
20. A1 cases (old 11/19/122/125): the calculator will not compute them. Confirm the local rules: no income tax, SM + TK + TTK, KP for residents only, and the SM minimum only for 10/120.
