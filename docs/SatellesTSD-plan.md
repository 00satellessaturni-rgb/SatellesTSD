# SatellesTSD — plan

Status: 2026-11-03. Section 1 is the as-is map of the workbook `PALGAD-TABLES-v045.xlsm` (read from its VBA and CONFIG sheet); 2 is what the 2026 regime demands; 3 the gap; 4 timeline, channel and the recommended shape; 5 the fifteen questions (the owner said "start S1 on defaults" — the bold picks stand until overruled); 6 the slices; **7 what S1 landed and the decisions made inside it, for veto**.

## 1. What the workbook does today (as-is)

One Excel macro workbook, one CONFIG sheet, two buttons on it: **Run main** (formMain) and **Calculator** (formSalCalcV2). Everything below is driven by named blocks on CONFIG (`{employee_define}`, `{payout_define}`, `{category_filters}`, `{category_config}`, `{report_config}`, `{tsdl1_config}`, `{tsdl2_config}`, `{taxes_config}`, `{tsdl1-tor-cat_matrix}`, `{tsdl2-tor-cat_matrix}`, `{system_data}`).

### 1.1 Inputs

| Input | How it is recognised | What is read |
|---|---|---|
| **TÖR export** (Töötamise register, sheet "Töötajate loend", 35 columns) | exact header row: TÖR ID, Töötaja isikukood, Perekonnanimi, Eesnimi, Töötamise alguse aeg, Töötamise liigi ID, Töötamise liik, Kande oleku ID, Kande olek, Töötamise lõpu aeg, Lõpetamise aluse ID, Lõpetamise alus, Leping, Ameti ID, Amet, Aadressi ID, Aadress, Riigi kood, Peatamise …, Otsus …, Tööandja registrikood või ID, Tööandja /Tegeliku tööandja nimetus, RKA märge, TKA märge, Viimase muutmise …, Tööaja määr, IMO | first name, last name, isikukood, Riigi kood, start, end, Töötamise liigi ID (1 tööleping, 600 VÕS, 507 viisa-tööleping, 602 viisa-VÕS, 501 tasuta töötamine), Kande oleku ID (2 kehtiv, 3 peatatud, 4 lõpetatud), employer name |
| **Bank statements / payroll sheets**, up to 20 layouts | exact header row per layout (column count + every header text, incl. mojibake headers as exported) | per layout the config names the columns: account (for the `*1234` suffix in the report), date, counterparty name, amount, description, D/C, and the debit/credit marker values |

Layouts configured today: Swedbank (ET), Kassa (an internal payroll sheet: lehe_nimetus, lehe_kuup, arvestuskuu, lehe_liik, lehe_tüüp, töötaja, iban, bruto, töötuskindlustusmakse, kogum_pens, maksuvaba, tulumaks, neto), LHV (3 ET variants, RU, EN), SEB, Coop Pank (RU, ET), Luminor (ET, RU-mojibake), Paysera (2), "Source1" (a card/e-money export: Date started (UTC) … Spend program), CustomTable1 (hand-made: Name, Surname, Income tax, Social tax, Amount to declare, Travel …), TransferWise/Wise.

Files are picked with a multi-select dialog from `Downloads\Palgad`. The TÖR sheet gives the default company name. Cyrillic look-alike letters in names are mapped back to Baltic letters (Д→Ä, Р→Š, …) before searching.

### 1.2 Pass 1 — "Report" (mdlTables.GenerateReport)

Inputs: period (a month; the control date is its last day), company name, `useHttp` flag.

For every TÖR row:
1. Duplicate code/name → row painted yellow, skipped.
2. If `useHttp`: **residency** from the EMTA non-resident register (scrapes `apps.emta.ee/saqu/public/nonresident`, CSRF token + cookie, parses the HTML table of non-residency periods) → "EE" or "mitteresident"; **II pillar** from `pensionikeskus.ee/.../liitumiskontroll/?personCode=` (scrapes the page; finds "liitunud", the start date, "sissemaksed lõppenud", and the rate row valid on the control date) → "II sammas"/"ei ole liitunud"/"kogumispensioni sissemaksed lõppenud" and the rate (2/4/6 % → stored as the text the parser later re-reads).
3. **Name search** in every statement's name column: substring on the last name, then token match (`Like`) for last and first name; if nothing, one character at a time is wild-carded (`*ast`, `L*st`, …) on the last name, then the first name; first-name aliases from `{system_data}` (SERHII,SERGII,SERGEI,…; PAVEL,PAVLO; …). A masked hit must not also match another TÖR person (IsUniqueResult), else it is dropped.
4. Each hit: date must be inside the period month, else noted "kuupaev ei kuulu perioodi". **Category** from the description with per-bank `Like` pattern lists (`{category_filters}`: include list, `;` exclude list); for Trahvid also the payee name. D/C must match the category's side (all are D = outgoing). Two categories matching = error row.
5. Category effects (`{category_config}`): **Palgad** (Palk=Y) adds the absolute amount into the interim report column of that statement; Dividendid, Üür, Kommunaalkulud, Kompensatsioon, Haigusleht, Laenud, TÖR-väline palgad, Trahvid get a sub-table under the main table (Lisatabel=Y) and their own sheet; unmatched → sheet "Tundmatu kategooria". Töötajapõhine marks Palgad/Kompensatsioon/Haigusleht as employee-only; TÖR-väline palgad is explicitly non-employee.
6. After the employee loop, every statement row that no employee matched is categorised on description alone (employee-based categories excluded) and appended to the sub-tables under the payee's raw name.

Output workbook `report-details-<company>-<yyyy-MM-dd_HH-mm-ss>.xlsx` next to the TÖR file: sheet **Vahearuanne** (columns: Töötaja isikukood, Perekonnanimi, Eesnimi, Maksuvaba, [one net-sum column per statement, named `<bank> *1234`], PALK NETO KOKKU (=SUM), Residentsus, Pension, Kontroll-kp, Töötamise alguse aeg, Töötamise liik, Kande olek, Töötamise lõpu aeg; then the sub-tables), **TOR kontroll** (TÖR copy with the found descriptions per statement), **Otsingu kontroll** (every hit), one sheet per category, and a copy of every statement with a "Vaste leitud" column.

**The accountant then edits this file**: fixes names/codes, adds people by hand (with Töötamise liik text), fills **Maksuvaba** (a number = the basic exemption to apply), corrects amounts.

### 1.3 Pass 2 — "TSD" (mdlTables.GenerateTsd)

Input: the edited report (Vahearuanne + TOR kontroll), period, `useHttp`.

Main rows (one per person): TÖR lookup by code, else by names; a person not in TÖR but with a Töötamise liik text is mapped through the matrix name row (partial match) to a TÖR code. Residency/pension are parsed back from the report strings, and fetched if missing and `useHttp`. "lisatulu" = this person's sums in sub-tables whose category carries a TSD code (Üür→57, Haigusleht→24) — it only influences the tax-free decision. Then `cTsdTable.CreateRecord`.

Sub-table rows whose category has a code (Üür, Haigusleht) create their own records with that code; Dividendid, Kommunaalkulud, Kompensatsioon, Laenud, Trahvid create none.

`CreateRecord`: errors if TÖR unresolved / residency unchecked / pension unchecked (residents) / isikukood invalid → yellow row with the text, file name gets "(vead)". Resident → Lisa 1, else Lisa 2. Kande olek 3/4 with net 0 → silently skipped. The matrix column for the TÖR code gives the **TSD code** and seven flags:

| TÖR/cat code | name | Lisa 1 code | TM | TK pers | TK comp | SM | SM min | KP | maksuvaba |
|---|---|---|---|---|---|---|---|---|---|
| 1 | Tööleping | 10 | y | y | y | y | y | y | y |
| 600 | VÕS leping | 17 | y | y | y | y | n | y | y |
| 507 | Viisa – tööleping | 10 | y | y | y | y | y | y | y |
| 602 | Viisa – VÕS | 17 | y | y | y | y | n | y | y |
| uur | Üür | 57 | y | n | n | n | n | n | n |
| haigus | Haigusleht | 24 | y | n | n | n | n | n | n |
| 601 | Juhtimis-/kontrollorgani liige | 21 | y | n | n | y | n | y | y |
| autorent | Autorent | 50 | y | n | n | n | n | n | n |
| 501 | Tasuta töötamine | 10 | y | y | y | y | n | y | y |
| a1t / a1v | A1 tööleping / A1 VÕS | 11 / 19 | n | y | y | y | y/n | y | n |

Lisa 2 (non-residents): 1→120, 600→123, 507→120, 602→123, 501→120, haigus→129, a1t→122, a1v→125, uur→205; no KP, no maksuvaba; SM min only for 1/507/a1t.

`{taxes_config}` (2026 values in the file): TM 0.22; TK 0.016 / 0.008; SM 0.33; SM min base 886/month; KP 0.02 (overridden by the person's own rate when known); maksuvaba 8400/yr (= 700/month, fixed — the 14400/25200/10800 tiers are still in the sheet but the 2026 class skips them); pensioniealine 9312/yr (not used by the TSD path).

`cCalc2026` grosses **net → gross**: gross = (net − TM×maksuvaba) / (1 − KP − TK − TM×(1 − KP − TK)) when net exceeds the exemption, else gross = net/(1 − KP − TK); re-rounds so that gross − KP − TK − TM = net exactly; tax-free capped at net; SM base = gross (+ the increase to the 886 minimum when the flag is on); employer UI; "company expense".

Row written per person (Lisa 1): 1000 code, 1010 name, 1020 payment code, 1030 gross, 1050 A1 country (empty), 1060 SM base, 1070/1080 "0,00", 1090 SM min increase, 1100 SM, 1110 KP, 1120 UI base (= gross), 1130/1140 UI, 1170 TM, 1160_610 exemption used, 640/650 "0,00". Lisa 2: 2000…2170 likewise, 2020 country left EMPTY, 2160 rate.

Outputs: `tsd-details-<company>-<ts>.xlsx` (TSD Lisa 1/2 kontroll + eksport sheets, the report copy) and `tsd-<company>-lisa1.csv` / `-lisa2.csv` (one line per row, values quoted and `;`-separated, sums as "0,00", no header) for the e-MTA Lisa 1 / Lisa 2 import; "tsd(vead)-" when any row has errors.

### 1.4 Calculator (formSalCalcV2)
Gross⇄net for one month with toggles: maksuvaba on/off or a custom amount, SM minimum, employer/employee UI, pension; shows TM, UI, KP, SM, company cost. Dividends are not covered.

### 1.5 Known gaps the workbook itself lists (CONFIG!A3, in Russian)
Done: people added by hand with a contract type go into TÖR; company name fallback. Open: CSV statement import and SEB headers; a better name-verification algorithm.

### 1.6 Facts to carry over unchanged
- Everything is **per person per month** from NET bank amounts; the gross-up is the firm's own arithmetic, not the bank's.
- Residency and II-pillar facts come from two public web pages, scraped; both parsers are brittle (fixed CSS selectors, cookie names, Estonian phrases).
- Statement layouts are matched by exact header text; several layouts exist only because banks export mojibake or Russian/English headers.
- The accountant's review happens in Excel between the two passes; the report file IS the hand-off.

## 2. What changes in 2026 (andmepõhine TSD / APA TSD)

Sources: the zip `Andmepohine_TSD_2026.zip` (developer guide v1.1 of 30.01.2026, X-tee guide v1.3 of 20.07.2026, business rules v2.0 of 24.07.2026, payment-type matrix of 18.11.2025, taxonomy of 17.10.2025, MTA examples). The two reader notes `apa-xml-reference.md` and `payment-type-mapping.md` (scratchpad, this session) cite file and line for every claim below.

### 2.1 The file
- **One XBRL GL XML instance per company per calendar month** (the month the money was PAID, not the accrual month). Fixed header: one `context` (äriregistrikood + file date), units `eur` and `pure`, `documentInfo` (entriesType=other, uniqueID = regcode-date-time, language=iso639:et, creator, periodCoveredStart/End = the month, sourceApplication, defaultCurrency=iso4217:eur), `entityInformation` (organizationIdentifier + ARIREGISTRIKOOD).
- **Two sections** matter for a private company: `EE0201001` (töötasud: salary 10, VÕS 17, board member 21, employer sick pay 24, severance 33, non-resident 120/123/129/156/138 …) and `EE0201002` (rent 57/50/54/56, non-resident rent 185/205/186, other income 55). An empty section is just `entryHeader` + `entryNumber`. `EE0201003` (erijuhtude SM) is for state bodies and not ours.
- **One `entryDetail` = one payment** (one date, one type, one payee), in strict element order: `lineNumber` (a permanent key, unique per company+month+section across ALL files ever sent), `lineNumberCounter`, `account/accountSub` (TOIMINGUTYYP R/M/T + VALJAMLIIK code + optional country/A1 entries through `segmentParentTuple`), `amount` (**GROSS**), `identifierReference` (`EE`+isikukood, ISIKUKOOD; or the MTA non-resident MRR code), `documentDate` (the payment date, inside the month), `measurable*` (SYNDMUS `TSD01` = calculation date on every payment; `TSD_SMVM` social-tax base; `MVT_610`/`MVT_650`/`MVT_640` exemption used; `TSD_KUUMSUURENDUS` the minimum-SM increase; refunds carry `TSD02` and negative amounts), `taxes*` (`taxAmount`, optional `taxPercentageRate` — required for TSD_KTM on non-residents —, `taxCode` ∈ TSD_SM, TSD_KP, TSD_TK, TSD_TTK, TSD_KTM). `decimals="2"` on every number, dot decimal, dates `yyyy-MM-dd`, UTF-8. **No names are sent.**
- **The submitter supplies gross and the computed taxes.** The rules check each tax against MTA's own figure "within a tolerance" (size unpublished) and refuse taxes that a type cannot carry. No rule found that requires the tax lines to be present or says MTA fills them in (open question for MTA).
- **A1 cases are no longer separate codes** (old 11/19/122/125 die): base code 10/17/21/120/123 + two `accountSub` entries (A1_RIIGID_enum under `RR_A1`, RTK2T2013ap under `RR_TOOKOHA_ASUKOHT`). Non-residents always carry RTK2T2013ap under `RR_MAKSURESIDENTSUSE_RIIK`; tax-treaty rates exist for some types (123 art 14 …) with a residency certificate; EEA residents may get the basic exemption.
- Changes: `M` resends the whole payment under the same `lineNumber`; `T` sends only the key + counter + T and kills the key forever; a confirmed TSD must first be reopened in e-MTA; `entryComment` carries PARANDUSPOHJUS_1..4; previous-year income-tax changes are refused after 15.02.

### 2.2 The round trip
- **Submit**: `POST …/apa-tsd/payments/v1/xtee/submit-data` (multipart, `.xml` or a `.zip` with one XML, ≤ 50 MB, headers `X-Road-Client` + `X-Road-Represented-Party` for a client company) → `{uuid}`; then `GET …/return-data/{uuid}` (204 while processing) → the **feedback report** XML (`processing@result` OK | WARNING | ERROR | PARTLYERROR; per-payment messages tagged WARNING / ERROR_RED / ERROR_BLUE / ERROR_BLACK; the server's own tax figures echoed for clean payments). Black = not saved (resend as R); blue = saved but taxes not counted; red = counted but blocks confirmation; yellow = advisory. Parser traps: decimal commas and dots mixed, a leading space in one error code, a missing counter on black errors, a ZIP when 2+ messages.
- **Confirm** (a separate step): a digitally signed or e-sealed **`.asice`** holding a `confirmDeclaration` XML (regcode, TSD, month, year, which sections have data, the tax totals 110/114–119) → `POST …/confirmations`, poll status; errors DECLARATION_WITH_ERRORS / _DISCREPANCIES / _PROCESSING.
- **Calculator** (`apa-tsd/calculator`, `POST /v1/xtee/tax-calculations`, synchronous): net OR gross in (`netPayment`), one month per file, XBRL GL with taxes out; **excludes A1 cases and dividends**; takes no pension rate/residency input (uses MTA registers). Every documented path is **X-tee only** (security server or a service provider's, with MTA authorisation). Whether a non-X-tee route exists is the subject of the third reader (section 4).

### 2.3 Rates and parameters in the sources (2026)
TM 22 % (10 % for a few types); SM 33 %; SM monthly minimum base 886 (the MTA example uses 820 — ask); TK 1.6 % / TTK 0.8 % (TK dropped for old-age/flexible pensioners); KP = the person's own rate (only 2 % appears in the files); MVT_610 700/month (8400/yr, flat — the 14400/25200 taper is gone), MVT_650 776/month (9312/yr) from 1 January of the year the person reaches pension age; MVT ≤ amount − KP − TK per payment and ≤ the legal monthly total per payer; MVT_640 (III pillar) ≤ 15 % and ≤ 6000/yr. Codes 37/38/167/168 (SM-only pay) exist in the matrix and the calculator list but not in the VALJAMLIIK2023ap dump. Dividends are in neither (they stay on the TSD form's own annex, outside this file).

### 2.4 What stays the same
The TÖR export and the bank statements are still the only inputs the firm has; residency and II-pillar facts still come from the two public web pages (no API found in the files — the third reader checks the web); the accountant still reviews before anything is sent; the gross-up from net is still the firm's own arithmetic (the official calculator cannot do A1 cases and is X-tee-only).

## 3. Gap analysis — workbook vs the 2026 regime

| # | Workbook today | 2026 needs | Consequence for the app |
|---|---|---|---|
| G1 | Output = Lisa 1 / Lisa 2 CSV, one row per person per month | XBRL GL XML, one `entryDetail` per **payment** with its date | New writer; the unit of work changes from person-month to payment. The statement already gives one line per payment, so the natural model is: every matched bank line = one payment line. |
| G2 | Stateless: nothing remembered between months | `lineNumber` is a permanent key per company+month+section; M/T need the original; R on a known key is a hard error | The app needs a **store** (per company: payments sent, their keys, the feedback). Keys must be generated once and never regenerated (e.g. `<regcode>-<yyyyMM>-<section>-<n>` or a GUID, stored). |
| G3 | Exemption 700 applied once per person-month on the summed net | MVT is per payment; the monthly total per payer is checked | An **allocation rule** across a person's payments in the month (first payment takes the exemption up to its cap `amount − KP − TK`, the rest spills to the next). Same for the SM minimum increase (one `TSD_KUUMSUURENDUS`, on one payment, or a zero-amount line). |
| G4 | Names in the CSV; country column left empty | No names; `EE`+isikukood or an MRR code; non-residents need their residence country + rate | **Person master data** per company: isikukood, name (for the UI only), residency + country (+ MRR code), II-pillar status/rate with dates, pension-age flag, A1 certificate country + work country, treaty certificate. Lookups cached with their check date. |
| G5 | A1 via codes 11/19/122/125; no non-resident board member / car rent | A1 = base code + two country entries; 156 for non-resident board members; treaty rates | Matrix becomes a code table in the app with the per-type flags from MTA's matrix (not the workbook's narrower flags — see the mapping note B.2 for the four places they differ; owner decides). |
| G6 | Residency = "EE"/"mitteresident" text; pension = text + rate parsed back from the report | Same facts, but also: pensioner (no TK), pension-age (MVT_650), deceased (warning from the server) | Keep the two scrapers as adapters behind interfaces; add manual override + "checked on" date; keep the texts as today's accountants know them. |
| G7 | Review happens in Excel between pass 1 and pass 2 | Still needed, plus: per-payment type override, exemption amount, A1/country facts, "do not declare" | Either an in-app review grid, or export → Excel → import back. (Owner's call, section 4.) |
| G8 | `useHttp` toggles the two lookups | Plus: submit, poll feedback, confirm (signed container), or the manual e-MTA route | Channel adapter(s) behind one interface; the file is always produced locally first. |
| G9 | Calculator form (net⇄gross) | Same arithmetic, now per payment type with the matrix flags; optionally compare against MTA's calculator | Port `cCalc2026` as a pure, unit-tested engine; the form reuses it. |
| G10 | Statement layouts = exact header rows on CONFIG | Unchanged | Port the layout table to a settings file (JSON) the owner can extend without a rebuild; keep the mojibake variants. |
| G11 | Categories by `Like` patterns per bank | Unchanged, but categories now map to VALJAMLIIK codes and sections, and some (Dividendid, Kompensatsioon, Laenud, Trahvid, Kommunaalkulud) are "found but not declared" | Same pattern engine (VBA `Like` → .NET regex/glob); category table gains `code`, `section`, `declare y/n`. |
| G12 | Errors = yellow rows + "(vead)" in the file name | Server feedback with four colours per payment | A feedback view that ties each message to the payment line, and marks which payments must be resent (R), corrected (M) or are fine. |
## 4. Timeline, channel and design

### 4.1 Timeline (verified on emta.ee 2026-11-03, see `docs/reference/submission-channels.md`)
- APA TSD is **live since 1.10.2026**. October 2026 is the first new-format period, **due 10.11.2026**. September 2026 went the old way (due 12.10.2026).
- The old Lisa 1/2 **XML** import died on 1.10.2026. The old Lisa 1/2 **CSV** upload in e-MTA (what the workbook produces) is a bridge **until 31.12.2027** ("Ajutise üleminekulahendusena … 2027. aasta lõpuni"). Open: whether that CSV must now carry the new codes (MTA question).
- Lisa 3–8 and INF 1 stay form-based until 2027–2028; dividends are outside this file.
- So: **no emergency**. The workbook's CSV keeps the firm legal for 14 months; the app replaces it before 2028.

### 4.2 Channel: three ways in, in this order
1. **e-MTA upload by hand** of the XBRL GL file in the new "väljamaksete vaade" (package I / right "Väljamakseandmete esitamine"). No X-tee, no setup. MTA computes taxes on upload, shows errors in the UI, and the accountant confirms there. This is how the app should ship first.
2. **X-tee REST** (`EE/GOV/70000349` · `apa-tsd` · payments / calculator / declarations): needs an X-tee member + security server (own ≈ €2.5k/yr per RIA, or hosted, or an intermediary), MTA opens the services, each client grants the firm a volitus (XT_MM or "Väljamakseandmete edastamine X-tee kaudu"); the firm sends with `X-Road-Represented-Party`. Fully machine-driven: submit → poll feedback → confirm with a signed/sealed `.asice`. The app's file and parsers are the same; only the transport is added later.
3. Legacy CSV — not built; the workbook covers the bridge.

### 4.3 Shape of the app (recommendation)
- **Three projects**: `SatellesTSD.Core` (models, tax engine, XBRL GL writer/reader, feedback parser, TÖR + statement parsers, matching, category engine — no UI, no database; plain file reads and writes only), `SatellesTSD.Infrastructure` (SQLite store, HTTP lookups, Excel export), `SatellesTSD` (WinForms). `SatellesTSD.Tests` (xUnit) on Core from day one; the official MTA example file is a test fixture the writer must reproduce.
- **Store**: one SQLite file for the firm (all client companies) in a folder the owner picks. Tables: Company, Person (per company: isikukood, names, residency + country + MRR code, II-pillar status/rate + dates, pension-age, A1 + work country, treaty certificate, overrides with "checked on"), StatementLayout (JSON), Category (patterns + code + section + declare), Period (company+month: state draft/exported/uploaded/confirmed), Payment (one per matched bank line: date, payee, net, category, type code, gross + taxes + measurables, `lineNumber`, action R/M/T, feedback colour/messages), Submission (file, time, uuid, result).
- **Pipeline per company-month**: import TÖR + statements → match & categorise → **review grid in the app** (fix names/codes, set type, exemption, "don't declare", add a line by hand) → compute gross + taxes per payment (exemption and SM-minimum allocated across the person's payments in the month) → write XML (+ XSD check) → accountant uploads in e-MTA → outcome recorded (manual, or from the feedback XML when X-tee arrives) → corrections become M/T lines in the next file.
- **Excel stays as an output**: the report-details workbook the accountants know (Vahearuanne, sub-tables, Otsingu kontroll, statement copies) is exported from the same data for checking and filing; import-back is a later slice if the office wants it.
- **Lookups** behind interfaces with cache + manual override: residency (public EMTA form, as today) and II pillar (Pensionikeskus single query, as today); the paid Pensionikeskus XML/mass query and the X-tee SOAP residency service are later options.
- **Tax engine**: a pure class per the 2026 rules (port of `cCalc2026` + MTA's matrix flags per VALJAMLIIK code), net→gross and gross→net, unit-tested against the eight worked figures in MTA's examples and the workbook's formulas; the Calculator form reuses it.
- **UI language**: Estonian, because every term the accountant sees in e-MTA is Estonian (open question 5).

### 4.4 Rejected / deferred
- Building on the legacy CSV (dies 31.12.2027).
- Calling MTA's calculator instead of own arithmetic (X-tee only; no A1; no pension-rate input).
- App-side e-seal signing of the confirmation (needs a signing library + certificate; the accountant confirms in e-MTA).
- Excel as the editing medium between passes (loses the stored keys; two sources of truth).

## 5. Questions for the owner (answer by number; "your recommendation" = the bold one)

1. Channel first: **e-MTA upload by hand** now, X-tee later through a hosted security server — or X-tee from the start?
2. Review medium: **in-app grid + Excel export** — or keep the Excel round-trip (export, edit, import back) as the only review?
3. Granularity: **one declared line per bank payment** (what the regime defines; monthly exemption and SM-minimum allocated first-payment-first) — or one summed line per person-month with the last payment's date?
4. Store: **one SQLite file for the whole firm** in a folder you pick (single user at a time) — or one file per client company?
5. UI language: **Estonian** — or Russian (like PrintHelper) / English?
6. Where the workbook is stricter than MTA's matrix (no exemption on rent 57, sick pay 24, car rent 50, any non-resident; SM minimum off for 501 tasuta töötamine): **follow MTA's matrix with a per-person "apply exemption" switch** — or keep the workbook's rules?
7. Accountant's codes for cases the workbook never had: non-resident board member → 156; non-resident car rent → 186; non-resident rent 205 (kinnisasi) vs 185 (eluruum)?
8. II-pillar rate (2/4/6 %) and "contributions ended": **kept per person in the app, with a "checked on" date, filled from the Pensionikeskus single query** — or the paid mass/XML query from the start?
9. Residency: **keep scraping the public EMTA form with a manual override** — or wait for the X-tee SOAP service?
10. Categories with no code (Dividendid, Kompensatsioon, Laenud, Trahvid, Kommunaalkulud, TÖR-väline palgad): **report-only, never declared** — confirm.
11. Statement layouts: **carry over all 14** — or only the banks in use today (name them)?
12. Confirmation: **in e-MTA by the accountant** — confirm.
13. The old Calculator window: **keep, on the new engine** — confirm.
14. A1 payments (old 11/19/122/125): the app computes them locally (no TM, SM + TK + TTK, KP for residents, SM minimum only for 10/120) — confirm with the accountant.
15. First slice: **S1 the core engine** (below) — or S4 imports first so you can see your real files flow?

## 6. Slices (one per "go next"; hours are build + review + your test pass)

| # | Slice | What done looks like | h |
|---|---|---|---|
| S0 | Solution layout | Core / Infrastructure / App / Tests projects, SQLite + ClosedXML + xUnit packages, settings file, git init | 2 |
| S1 | **Core engine** — BUILT 2026-11-03, see section 7 | Tax engine (2026 rules, matrix table per VALJAMLIIK code, A1 and non-resident variants, allocation across a month) + XBRL GL writer/reader + file validator + XSD validation against the APA-restricted schema set (`schemas/apa-tsd`, assembled from the stat.ee package, the xbrl.org XBRL 2.1 schemas and MTA's restricted XSDs); 82 tests reproduce MTA's example file (read → write identical, schema-valid) and its eight worked lines | 12 |
| S2 | Feedback + confirmation — BUILT 2026-11-03, see section 7 | Feedback report parser (both vocabularies, the four colours, the parser traps) → per-payment outcome with the resend advice; `confirmDeclaration` XML builder (unsigned) + its XSD; tests on MTA's seven samples | 5 |
| S3 | Store | SQLite schema + repositories; `lineNumber` registry; period states; migrations. Also the review's point: a `Check` that returns refusals with stable codes per payment (today the engine throws on a mis-coded row, which a grid cannot list) | 6 |
| S4 | Imports | TÖR parser; statement layouts from JSON (all 14, incl. mojibake variants); CSV statements; the "Kassa" payroll sheet; tests on anonymised real exports you provide | 8 |
| S5 | Matching + categories | Port of the name search (masks, aliases, uniqueness), category patterns (`Like` → regex), period filter, the second pass over unmatched lines; tests with the workbook's own CONFIG patterns | 8 |
| S6 | Lookups | Residency + II-pillar adapters (HttpClient + HTML parsing), cache, manual override, "checked on" | 6 |
| S7 | UI 1 | Main window: company + period, file pick, run; **review grid** (persons × payments, category/type/exemption/declare edits, add line); person panel | 14 |
| S8 | UI 2 | Payments & taxes grid, XML export, upload checklist, outcome recording, corrections → M/T; Calculator form | 8 |
| S9 | Excel export | report-details workbook as today (Vahearuanne, sub-tables, Otsingu kontroll, statement copies, TOR kontroll) | 6 |
| S10 | Publish | Build + release recipe like PrintHelper's; first real month in parallel with the CSV | 3 |
| later | X-tee client | submit-data / return-data / confirmations over a hosted security server; `.asice` signing | 12+ |

Total before X-tee ≈ 78 h.

## 7. S1 — what landed (2026-11-03)

`SatellesTSD.Core` (net10.0, no packages) and `SatellesTSD.Tests` (xUnit, 82 tests, all green); the WinForms project references Core. Committed as `867e9c9` on `master` (2026-11-03, on the owner's "commit and push"); no GitHub remote existed at that moment — the push follows once `00satellessaturni-rgb/SatellesTSD` is created.

- `Tax/PaymentTypes` — 24 VALJAMLIIK codes with MTA's matrix flags (SM, minimum, zero, UI, KP, TM rate, MVT, 640, treaty) plus the A1 variant rows per kind (`A1Kinds`; 156 has only the foreign one; the Estonian-A1 row keeps the minimum only on 10/120/138).
- `Tax/TaxEngine` — per payment, gross→net or net→gross (the workbook's `cCalc2026` tiers and re-derivation), A1 and non-resident modifiers, the exemption capped at amount − KP − TK; refuses a refund, a type/payee mismatch, a minimum increase where none applies, an unchecked II-pillar status or unknown rate where the pension is withheld.
- `Tax/MonthAllocator` — a person's month: the exemption (the application's amount or 700/776) spent in date order, the social-tax minimum topped up on the last qualifying payment or on a zero line; returns the month's figures (offered, left, base total, shortfall, which line carries the top-up); per-payment facts may override residency/A1/treaty only.
- `Xbrl/*` — `Declaration` model, `XbrlGlWriter` (the exact header, strict element order, `decimals="2"`, UTF-8 no BOM), `XbrlGlReader` (strict XML decimals), `DeclarationValidator` (no sections, repeated section or key, date outside the month, type in the wrong section, missing amount/payee/date → the writer refuses), `DeclarationLineFactory` (classifier entries, TSD01, measurables, tax lines), counters assigned per file, `XbrlGlValidator` (XSD validation against `schemas/apa-tsd`, offline, cached; what MTA refuses as KR_VALIDATE_XBRL_FILE_3).
- `schemas/apa-tsd/` — MTA's restricted XSDs renamed into the `case-c-b-m-e` palette their own imports expect, the base cor/bus/muc modules from the stat.ee package and the XBRL 2.1 linkbase/xl/xlink schemas from xbrl.org (both downloaded 2026-11-03 with the owner's OK), MTA's restricted gen and instance schemas, a written palette entry point, and one marked stub: the never-referenced `gl-ext` module. Provenance in its README.
- Review: three lenses + five refuters (all Opus) ran before this pass; their confirmed findings are folded in (A1 per type and kind, the exemption amount, the validator, strict number parsing, the pool checks, the killer tests). The refuted parts: none outright; two overstatements corrected.

**Decisions made inside S1 (announced for veto)**
1. Rounding is half away from zero (määrus nr 60 lisa 1 märkus 8 is the only rule MTA ever wrote; the workbook's VBA `Round` on doubles followed no mode reliably). Parameter on the engine.
2. `TSD_SMVM` is written on every line subject to social tax, also at 0 on a minimum-only line (rule KR_SM_01_1 outranks MTA's older example).
3. The minimum base counts only the pay received as an employee or official (SMS § 2 lg 2 with lg 1 p 1; the types MTA's matrix marks M = JAH: 10, 33, 37, 120, 138, 167). A board fee (21/156) or a VÕS fee (17/123) never fills it, and MTA forbids the top-up on those types (KR_SM_11). A month with no such pay needs a zero line to carry the top-up. A part month owes the minimum in proportion (`SocialTaxMinimumShare`, days/30 per määrus nr 17 § 6); the lg 4 exemptions and the "several employers" rule are the per-person switch `SocialTaxMinimumApplies` (the accountant decides; the other-employer certificate reduction is not modelled).
4. A1 facts of a person are ignored on a type that has no such variant (rent, sick pay); no error.
5. An unknown pension rate is a refusal, not a 2 % default.
6. A person's exemption pool and minimum are fixed for the month; a payment's own facts may differ only in residency, A1 and treaty.

### S2 — what landed (2026-11-03, 118 tests green, not yet committed)

- `Feedback/FeedbackReader` reads MTA's tagasisideraport (submission or confirmation): namespace-tolerant, trims the stray spaces in MTA's codes, accepts a missing counter on black errors, a header without a section, decimal commas and dots; refuses a report without any entryHeader; a ZIP (two or more messages) is recognised by its bytes, not its name, and each entry keeps its name as `Source`.
- `Feedback/FeedbackReport.Outcomes(submittedActions)` — one outcome per payment line: the server's verdict (errorLevel), the worst colour, the messages worst-first, the server's own tax figures, anything unrecognised kept in `Unclassified`, and the **resend advice**: blue/red → M; black → depends on what we sent (R → R again, M → M again, T or unknown → review); rule `KR_XBRL_VALJAMAKSED_4` (R on an existing key) → M, `_5` (M/T on an unknown key) → R, `KR_VM_05` (wrong type or section) → review. Section-level validations without a line surface as `GeneralValidations`; a confirmation report answers `ConfirmationResult()` (Confirmed / StillProcessing / WithErrors / WithDiscrepancies / CannotConfirm) and `Totals()`.
- `Confirmation/ConfirmationWriter` writes `confirmDeclaration` exactly as MTA's sample and its XSD (reproduced element for element); `ConfirmDeclaration.ForTsd(...)` takes the accountant's facts — which sections hold data (PAYMENTS plus any annex filed by hand in e-MTA) and all seven totals, refusing a missing one; `DeclaredTaxTotals.OfFile` adds up ONE file's taxes as a cross-check only.
- `Xbrl/SchemaValidation` validates against one self-contained XSD; both validators now count .NET's "unknown element" warnings as problems (an undeclared root used to pass silently).
- `schemas/apa-tsd/feedback` and `/confirmation` hold MTA's XSDs; MTA's own 2026-09 colour sample fails its 2026-03 feedback XSD (no counter on black lines) — the reader tolerates it on purpose.

**Decisions made inside S2 (for veto)**
7. Red ranks above blue (red stops the month's confirmation; blue hides a line's taxes); the sources do not rank them.
8. A black error on a line whose sent action the caller does not give is "review", never "resend as R".
9. The confirmation's sections and totals are the accountant's input from e-MTA's summary form; the app never guesses an annex or a total. The ASiC-E signing is not in the app (plan 4.4).
10. On DECLARATION_PROCESSING a new confirmation is started later; the old one is not polled again (XTEE §7.5).

**Settled by the refuters' research (emta.ee, riigiteataja.ee, 2026-11-03)**
- SM kuumäär 2026 = **886** (minimum SM 292.38); 820 was the 2025 value, MTA's example XML Tehing_21117 is simply out of date.
- The minimum compares employee/official pay only (decision 3); EMTA's own TSD examples never let a board fee reach it.

**Open with MTA / the accountant (new since the plan)**
- Whether MTA will state in words that a board fee does not fill the minimum (the law and the matrix say so; no MTA sentence does).
- The tolerance on tax figures ("lubatud hälbe piires") — size unpublished.
- Confirm rounding by sending gross 1000.50 to the calculator in ee-test once X-tee exists (expect SM 330.17).
- (done 2026-11-03) The XBRL 2.1 linkbase/xl/xlink schemas are in place; `schemaRef`'s xlink attributes are now type-checked.

### Reference material in this repo
- `docs/reference/apa-xml-reference.md` — element-by-element XBRL GL format, business rules, feedback, confirmation, gotchas.
- `docs/reference/payment-type-mapping.md` — old codes → VALJAMLIIK2023ap with MTA's per-type tax flags; rates; calculator; open MTA/accountant questions.
- `docs/reference/submission-channels.md` — channels, X-tee prerequisites, timeline, access rights, lookups, bookmarks.
- `docs/reference/workbook-vba/` — the workbook's VBA modules and CONFIG sheet as extracted (the behaviour this app replaces).
- MTA's zip `Andmepohine_TSD_2026.zip` (Downloads) is the primary source; not copied into the repo.
