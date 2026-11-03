# APA TSD (andmepõhine TSD): submission channels and switch-over timeline

Research notes, compiled 2026-11-03 (the new solution went live on 1 October 2026, about a month before these notes). Written for a C# desktop app that builds the XBRL GL payment file (former TSD lisa 1 / lisa 2 + ESD) for small private companies and today uploads TSD lisa 1 / lisa 2 as CSV in e-MTA.

## How to read these notes

Each claim carries a tag and a source:

- **[DOC]**: a local MTA document says it. The source is given as file plus line number. "PDF p." is the `===== PAGE n =====` marker in the extracted text. The printed page numbers inside the PDF are one lower, and pages 19 and 25 print "32".
- **[WEB]**: a live web page says it. All pages were retrieved 2026-11-03. Each citation gives the URL and the page's own "Viimati uuendatud" date. The line numbers point into the text snapshots under `scratchpad\web\`.
- **[INF]**: my own inference. It is not stated by MTA.

Quotes are kept short and are in Estonian. Technical identifiers (paths, headers, codes, limits) are copied exactly.

### Source key

Local (paths relative to the scratchpad root):

| Key | File | What it is |
|---|---|---|
| X | `dump\pdf\xtee_20260720.txt` (PDF: `rules\Juhendid_ja_arireeglid\APA_TSD_juhend_rakendusega_liidestumiseks_yle_X-Tee_20260720.pdf`) | "Juhend APA-TSD rakendusega liidestumiseks üle X-Tee", v1.3, 20.07.2026 |
| X12 / X11 | `work\xtee_old\…20260526.txt`, `…20260327.txt` (from `rules\Juhendid_ja_arireeglid\Arhiiv\`) | X-tee guide v1.2 (26.05.2026) and v1.1 (27.03.2026). Used only to trace endpoint renames |
| DG | `dump\pdf\juhend_arendajale_20260130.txt` | "Juhend XBRL GL failiformaadis palgamaksete … saatmiseks", v1.1, 30.01.2026 |
| BR | `dump\arireeglid\sheets\SUBMIT DATA ÄRIREEGLID.txt` (from `rules\Juhendid_ja_arireeglid\APA_TSD_arireegid_20260724.xlsx`, sheet "SUBMIT DATA ÄRIREEGLID") | Ärireeglid v2.0, 24.07.2026. Cited by line plus cell, e.g. E14 |
| BR-J / BR-K | `dump\arireeglid\sheets\Juhis.txt` / `KALKULEERIMISE LISAREEGLID.txt` | The same workbook: the instruction sheet and the calculator-rules sheet |
| YP / YD | `rules\Naited_ja_tehnilised_failid\APA_TSD_payments_20260603.yaml` / `APA_TSD_20260603.yaml` | OpenAPI files dated 03.06.2026 |
| CK | `rules\Naited_ja_tehnilised_failid\APA_TSD_20260326_kinnitamine_naidis.xml`, `APA_ESD_20260326_kinnitamine_naidis.xml` | Confirmation input samples |
| CX | `rules\APA_TSD_XSD_naidised\confirmations-2026-09-23-apatsd.xsd` | Confirmation XSD (newest file in the share) |
| FR | `rules\Naited_ja_tehnilised_failid\APA_TSD_20260330_feedbackReport.xsd` and the `…tagasisideraport…xml` samples | Feedback report schema and samples |
| KAL | `rules\Naited_ja_tehnilised_failid\APA_TSD_kalkulaator_naide_20260915.xml` | Calculator input sample |

Web (all retrieved 2026-11-03):

| Key | URL | Updated | Snapshot |
|---|---|---|---|
| W-APA | https://emta.ee/ariklient/maksud-ja-tasumine/tulumaks-ja-sotsiaalmaks/deklaratsiooni-tsd-esitamine/deklaratsiooni-tsd-esitamine-muutub-andmepohiseks | 18.08.2026 | `web\apa.txt` |
| W-EN | https://www.emta.ee/en/business-client/taxes-and-payment/income-and-social-taxes/submission-declaration-form-tsd | – | `web\en.txt` |
| W-FILE | https://emta.ee/ariklient/maksud-ja-tasumine/tulumaks-ja-sotsiaalmaks/deklaratsiooni-tsd-esitamine/valjamakseandmete-esitamine-xml-voi-csv-failiga | 07.10.2026 | `web\xmlcsv.txt` |
| W-XTV | …/deklaratsiooni-tsd-esitamine/valjamakseandmete-esitamine-majandustarkvara-ja-x-tee-kaudu | 07.10.2026 | `web\xtee.txt` |
| W-R-EMTA | …/deklaratsiooni-tsd-esitamine/e-mta-kasutamiseks-vajalikud-paasuoigused | 30.09.2026 | `web\r1695.txt` |
| W-R-XT | …/deklaratsiooni-tsd-esitamine/x-tee-teenuste-kasutamiseks-vajalikud-paasuoigused | 30.09.2026 | `web\r1698.txt` |
| W-R-CONF | …/deklaratsiooni-tsd-esitamine/tsd-kinnitamiseks-vajalikud-paasuoigused | 30.09.2026 | `web\r1700.txt` |
| W-XTS | https://emta.ee/ariklient/e-teenused-koolitused/e-teenuste-kasutamine/x-tee-teenused | 20.10.2026 | `web\xteeteen.txt` |
| W-TECH | https://emta.ee/ariklient/e-teenused-koolitused/e-teenuste-kasutamine/teenuste-tehniline-info (anchors #tsd, #mitteresidentsusandmed) | – | `web\tehinfo.txt` |
| W-AA | https://emta.ee/ariklient/amet-uudised-ja-kontakt/arendustegevus/andmepohine-aruandlus | 15.10.2026 | `web\apaar.txt` |
| W-NEWS | https://emta.ee/uudised/ettevotja-koormus-vaheneb-palgavaljamaksete-deklareerimine-muutus-lihtsamaks | news of 05.10.2026 | `web\uud_koorm.txt` |
| W-VOL | https://emta.ee/ariklient/e-teenused-koolitused/e-teenuste-kasutamine/paasuoiguste-andmine-ja-lopetamine | 07.10.2026 | `web\volit.txt` |
| W-TEST | https://emta.ee/ariklient/andmepohiste-lahenduste-testimine | – | `web\testim.txt` |
| W-DEV | …/deklaratsiooni-tsd-esitamine/andmepohise-majandustarkvara-arendajad | 25.09.2026 | `web\arendajad.txt` |
| W-PRINC | …/deklaratsiooni-tsd-esitamine/deklaratsiooni-tsd-taitmise-pohimotted | 24.05.2024 (legacy text) | `web\pohimotted.txt` |
| W-KP | https://emta.ee/ariklient/maksud-ja-tasumine/tulumaks-ja-sotsiaalmaks/kohustusliku-kogumispensioni-makse | – | `web\emta_kp.txt` |
| ERP0324 | MTA "ERP infopäeva materjalid 24.03.2026", https://ncfailid.emta.ee/s/pQKPoS5LmjZkz5F | slides | `web\dl_infopaev\pQKPoS5LmjZkz5F.txt` (cp1252) |
| BANK1211 | MTA "Pankade infopäeva materjalid 11.12.2025", https://ncfailid.emta.ee/s/8cW8BAi43na6YY8 | slides | `web\dl_infopaev\8cW8BAi43na6YY8.txt` |
| NC | MTA public share "Andmepõhise TSD tehniline dokumentatsioon", https://ncfailid.emta.ee/s/TiG3ajyNdETfLK4 (listed over WebDAV) | – | – |
| XCAT | X-tee catalogue API: https://x-tee.ee/api/catalogue/v1/services?servicingMember=70000349&subsystemCode=apa-tsd, plus the attached OpenAPI files `/api/catalogue/v1/file/67312`, `/66752`, `/67028` | live | `web\xcat_EE.json`, `web\dl_xcat\` |
| NR | https://apps.emta.ee/saqu/public/nonresident/ | – | `web\nonres.html` |
| PK-L / PK-M / PK-A | https://www.pensionikeskus.ee/raamatupidajale/ii-sammas/liitumiskontrolli-lihtparing/, …/liitumiskontrolli-massparing/, …/automaatne-liitumiskontroll/ | – | `web\pk_lihtp.txt`, `web\pk_mass.txt`, `web\pk_auto.txt` |
| PK-RATE | https://www.pensionikeskus.ee/en/ii-pillar/contributions/changing-the-rate-of-contributions-to-mandatory-funded-pensions/ | – | `web\pk_rate_en.txt` |
| RIA | https://abi.ria.ee/xtee/x-tee-kasutamisvoimaluste-ja-kulude-ulevaade | – | `web\ria_kasut.html` |
| STAT | https://www.stat.ee/et/aruandlus-30 | – | `web\stat.html` |

The local document set matches the live MTA share exactly. The WebDAV listing of NC on 2026-11-03 shows the same files and dates, the newest being `confirmations-2026-09-23-apatsd.xsd`. Nothing newer has been published.

---

## 1. Channels

### 1.1 Short answer

- **[WEB] You do not need X-tee to submit.**
  - The XBRL GL file can be uploaded by hand in e-MTA, in the new "väljamaksete vaade" (payments view).
  - MTA's access-rights table has a row headed "XBRL-GL- või CSV-faili laadimine e-MTA väljamaksete vaates" (W-R-EMTA line 43–44).
  - The change page says data can be uploaded "uut XBRL GL failivormingus XML-formaati" (W-APA line 52).
  - There is a video on loading payment data "e-MTAsse XML- või CSV-formaadis failiga" (W-FILE line 30).
  - MTA's launch news confirms that payment data can still be loaded from a file, entered by hand, corrected and confirmed in e-MTA (W-NEWS line 22).
- **[WEB] Machine-to-machine (M2M) submission is X-tee only.**
  - MTA's X-tee page describes M2M data exchange with companies as going through X-tee: "toimub andmevahetuskihi X-tee kaudu" (W-XTS line 3).
  - An MTA slide puts it as "Andmeid tuleb saata läbi turvaserveri" (ERP0324 line 201).
  - [INF] I found no public HTTPS API, API-key scheme or Smart-ID/ID-card-authenticated REST endpoint for APA TSD. The X-tee guide, both OpenAPI files and the live X-tee catalogue all describe X-tee REST only, and MTA's site offers no alternative.
- **Other non-X-tee options MTA names:**
  - [WEB] Manual entry in e-MTA (W-APA line 81).
  - [WEB] The SEB or LHV bank payroll module (W-APA line 79; banks have moved to APA, W-DEV line 54).
  - [WEB] RIK's e-arveldaja (W-APA line 80).
  - [WEB] Paper, for up to 5 rows. This sentence still appears in the page header (W-APA line 12). Whether it applies to the new payments view is not stated (see open questions).

### 1.2 Channel matrix

| Channel | Transport and authentication | What is sent | Feedback | Confirmation (kinnitamine) | Status | Source |
|---|---|---|---|---|---|---|
| e-MTA, XBRL GL upload in "väljamaksete vaade" | Interactive e-MTA login as a person holding rights for the client | XBRL GL XML (EE0201001/2/3 sections) | Shown in the e-MTA UI after background processing; video covers fixing "andmeid, mille töötlemisel tuvastati vead" | In e-MTA, by a person with raamatupidaja / isiku esindamise pakett or the right "TSD esitamine" | Live from the October 2026 period | [WEB] W-R-EMTA 43–44; W-APA 52; W-FILE 30, 36; W-R-CONF 30–38 |
| e-MTA, legacy CSV upload (lisa 1 / lisa 2 "tänasel kujul") | Same | The current CSV format | Same | Same | Temporary, until end of 2027 | [WEB] W-APA 52; W-AA 97; W-TECH 934–937 ("alates 01.01.2023 kuni 31.12.2027") |
| e-MTA, manual entry in the payments view | Same | – | UI | Same | Stays | [WEB] W-APA 51, 81 |
| e-MTA, old TSD XML (`tsd_vorm`) for lisa 1/2 | – | – | – | – | **Ended 1.10.2026 for lisa 1/2** | [WEB] W-AA 97; W-APA 52 |
| X-tee REST `EE/GOV/70000349/apa-tsd/payments` | Own or hosted X-tee security server, a registered subsystem, the service opened by MTA, and a client authorisation in e-MTA | One XML or ZIP (one XML inside), max 50 MB | Asynchronous: poll `GET …/return-data/{uuid}` | X-tee `apa-tsd/declarations` with a signed or sealed ASiC-E, or in e-MTA | Live since 1.10.2026 | [DOC] X lines 120–144; [WEB] W-XTS 57–63 |
| X-tee SOAP `EE/GOV/70000349/mkrliides/uploadMime/v1` | X-tee | Lisa 3–8 and INF 1 in the old XML | – | – | Still listed for lisa 3–8 and INF 1 | [WEB] W-XTS 64–65; W-TECH 947–953 |
| Bank payroll (SEB, LHV) | The bank's own X-tee link, plus the company's contract with the bank | Payments made through the bank | In the bank | – | Live | [WEB] W-APA 79; W-DEV 54; [DOC] BANK1211 193–197 |

### 1.3 What X-tee requires (prerequisites)

**[DOC] The X-tee guide itself is thin on prerequisites.** It:

- names RIA's joining pages: "Juriidilise isiku liitumine X-Teega, https://abi.ria.ee/xtee/x-tee-kasutusele-votmiseks" (X line 87, PDF p.4);
- says the "X-Road-Client" header holds the ERP's subsystem in the form `<keskkond>/<liikmeklass>/<registrikood>/<alamsüsteem>`, which "Täidab ERPi turvaserver" (X lines 147–149, PDF p.6);
- lists the X-tee environments as `EE` (tootekeskkond), `ee-test` and `ee-dev` (X lines 150–154);
- lists the member classes `GOV`, `COM`, `NGO` and `NEE` (X lines 158–163, PDF p.7);
- makes authorisation a precondition (X lines 171–172): "X-Road-Client-l peab olema MTA-s õigus esindada X-Road-Represented-Party-t".

**[WEB] MTA's X-tee services page fills in the rest:**

- Joining: company managers join X-tee through the x-tee.ee self-service ("X-tee iseteeninduskeskkonna") (W-XTS line 7).
- MTA opens a service only for the subsystem of the organisation that actually uses it (W-XTS line 8).
- Opening a production service: apply in x-tee.ee with the user's registry code and name, the service name, the subsystem and the environment `EE` (W-XTS lines 12–17).
- Contract: an andmevahetusleping is normally required, but "Erandina ei vaja andmevahetuslepingu sõlmimist majandustarkvara teenused" (W-XTS lines 20–21). The TSD services are in that list, which closes with "Loetletud teenused ei vaja andmevahetuslepingu sõlmimist" and says the e-MTA business-client terms of use apply (W-XTS lines 57–76).
- Cost: "Maksu- ja Tolliamet X-tee päringute tegemise eest tasu ei võta" (W-XTS line 3).
- Test access: `ee-test` / `ee-dev` access is requested in x-tee.ee with a testing period (W-XTS lines 30–36).

**[WEB] The client's authorisation in e-MTA (W-R-XT lines 30–43).** The client gives the "turvaserveri teenusepakkuja" an X-tee volitus in e-MTA, in one of two forms:

- the single right "Väljamakseandmete edastamine X-tee kaudu"; or
- the package "Masin-masin liidese volituse pakett (KMD, TÖR, TSD)" (code XT_MM). Existing XT_MM holders got the new right added automatically.

**[WEB] X-tee infrastructure options and costs (RIA).** There are four ways to use X-tee: lihtliikmesus (own security server), majutatud turvaserver (hosted), teenuste vahendamine (through an intermediary) and infosüsteem teenusena (SaaS). Indicative yearly costs for running your own security server:

- timestamping: about €430 per server;
- OCSP: about €450 per certificate;
- certificates: AUTH €90 and SIGN €150;
- administration: about €1,500.

Hosting or an intermediary moves some of these costs to the provider (RIA, "X-tee kasutamise võimalused" and Tabel 3.2).

### 1.4 Service names: guide, OpenAPI and live catalogue compared

**[DOC] X lines 124–144 (PDF p.5–6).** The provider subsystemCode values are:

- `apa-tsd/payments`: väljamaksete esitamine and väljamaksete vastussõnumi allalaadimine;
- `apa-tsd/calculator`: väljamaksele maksude arvutamine;
- `apa-tsd/declarations`: deklaratsiooni kinnitamise algatamine and the vastussõnumi allalaadimine.

The full URL form is `https://{securityserver}/r1/{xRoadInstance}/{memberClass}/{memberCode}/{subsystemCode}/...` (X lines 121–123).

| Function | Guide v1.3 (20.07.2026), section body | Guide v1.3, summary list on PDF p.6 | OpenAPI YD / YP (03.06.2026) = guide v1.2 | Live X-tee catalogue (2026-11-03) |
|---|---|---|---|---|
| Submit payments | `POST apa-tsd/payments/v1/xtee/submit-data` (X 316, 347–349) | same (X 137–138) | `POST /v1/xtee/submit-data` (YP 41) | `POST /v1/xtee/submit-data` |
| Fetch payment feedback | `GET …/payments/v1/xtee/return-data/{uuid}` (X 445, 465–467) | same (X 139–140) | `GET /v1/xtee/return-data/{uuid}` (YP 113) | `GET /v1/xtee/return-data/{uuid}` |
| Tax calculator | `POST apa-tsd/calculator/v1/xtee/tax-calculations` (X 190, 221–223) | same (X 135–136) | `POST /v1/xtee/calculate-taxes` in **payments** (YP 187) | `POST /v1/xtee/tax-calculations` (service `calculator`) |
| Start confirmation | `POST /v1/xtee/confirmations` (X 666, 692–694) | `POST apa-tsd/declarations/v1/xtee/confirm` (X 141–142) | `POST /v1/xtee/confirm` (YD 36) | `POST /v1/xtee/confirmations` |
| Fetch confirmation result | `GET /v1/xtee/confirmations/{uuid}/status` (X 821, 839–842) | same (X 143–144) | `GET /v1/xtee/confirm-status/{uuid}` (YD 106) | `GET /v1/xtee/confirmations/{uuid}/status` |

**[WEB] (XCAT).** The catalogue lists subsystem `apa-tsd` with service codes `payments`, `calculator` and `declarations`, all REST, status OK. The long codes are `EE/GOV/70000349/apa-tsd/payments/` and so on. W-XTS lines 57–63 list the same three long codes.

**[INF] Conclusions from the comparison:**

- The live catalogue and the v1.3 section bodies agree.
- The OpenAPI YAMLs in the MTA share are stale (they reflect v1.2).
- The "…/confirm" entry in v1.3's own summary list is a leftover.
- Paths changed in every guide version: v1.1 used `/confirm-declaration` (X11 lines 559, 581). Read the live catalogue's OpenAPI before coding.
- In X-tee REST terms, the subsystem is `apa-tsd` and the service codes are `payments` / `calculator` / `declarations`. The guide writes them joined as "apa-tsd/payments".

### 1.5 Headers

[DOC] Every service takes the following headers (X 3.1, 4.1, 5.1, 6.1, 7.1):

- `X-Road-Client` (Kohustuslik);
- `X-Road-Represented-Party` (Valikuline);
- `X-Road-Id` (Valikuline; a UUID is recommended, and one is generated if absent).

`X-Road-Represented-Party` takes the form `<liikmeklass>/<registrikood>` (X line 178), for example `COM/70000272` (YP line 276).

If `X-Road-Represented-Party` is absent, MTA assumes the client submits, and fetches feedback, only for itself (X lines 173–177).

### 1.6 Message size and ZIP

- **[DOC] Submit:** the field `file` must be "multipart/form-data", and the file name "peab lõppema laiendiga .xml või .zip" (X lines 317–318).
  - The size limit is "Maksimaalne lubatud faili suurus on 50MB" (X lines 341–342).
  - Exceeding it returns HTTP 413 "Uploaded file exceeds the limit of 50MB" (X lines 411–413).
  - A wrong format returns 415 `UNSUPPORTED_FILE_FORMAT`; a schema or content failure returns 422 `INVALID_FILE` (X lines 415–425).
  - YP adds that the part's Content-Type must be `application/xml` or `application/zip` (YP lines 45–47).
- **[DOC] ZIP:** the data goes "XML-failina või ZIP-failis sisalduva XML-failina" (X lines 112–113). The developer guide adds "ZIP-failis võib korraga olla üks XML-fail" (DG lines 474–477, PDF p.18). The business rule `KR_VALIDATE_XBRL_FILE_2` enforces this (BR line 62, cell E9).
- **[DOC] Calculator and confirmation:** the limit is set by the system parameter `uploadXRoadFileMaxSize` (X lines 213–216, 687–690). The confirmation's 413 text still says 50 MB (X line 744).
- **[DOC] One month per file:** all payments in a file must fall in one calendar month (`KR_VALIDATE_XBRL_FILE_7`, BR lines 89–92; sample `…tagasisideraport_faili_enda_veaga.xml`).

### 1.7 Synchronous or asynchronous

**[DOC] X lines 101–109 (PDF p.5):**

- "APA-TSD pakub X-Teel REST-teenuseid. HTTP-päringud on sünkroonsed (request–response), kuid äriline töötlus on asünkroonne".
- The upload returns at once with `estimatedProcessingTime` and `uuid`. The client then polls ("pollida, GET /return-data/{uuid}").
- The calculator is the exception: "Erandiks on väljamaksele maksude arvutamise kalkulaatoriga edastatud andmete töötlemine, mis toimub sünkroonselt".

**[DOC] Example 201 response** (X lines 357–368): `{"uuid": "404f1e04-…", "estimatedProcessingTime": "PT15S"}`. YP defines the time as an ISO-8601 duration (YP lines 368–370).

**[DOC] One leftover:** X line 119 also mentions "GET /return-data" without a uuid. No spec defines it, and the live catalogue lists only `/return-data/{uuid}` [WEB XCAT]. Treat it as non-existent.

### 1.8 Fetching and reading the feedback report (tagasisideraport)

**[DOC] Fetching** (X 5.1–5.4, PDF p.15–18):

- Call `GET /v1/xtee/return-data/{uuid}` with "Accept: application/zip, application/xml".
- `204 No Content` means "Üleslaetud andmete töötlemine pole veel lõpuni jõudnud".
- `200` returns the XML ("Content-Disposition: attachment").
- `404 UNKNOWN_UUID` means the uuid matches no upload.
- YP adds that one message comes back as `application/xml` and two or more as `application/zip` (YP lines 17, 119).
- Rule `KR_XTEE_VASTUS_3`: the uuid must belong to the requesting client or its represented party (BR lines 26–30).

**[DOC] Structure** (X 5.5, PDF p.19–24; FR XSD):

- The root is `processing`, with attribute `result`: OK, WARNING, ERROR or PARTLYERROR. On ERROR the data must be resubmitted. On PARTLYERROR the erroneous payments are resubmitted, plus warned payments if the warning can be fixed (X lines 536–552).
- Results are grouped per `entryHeader` / `entryNumber` (EE0201001 and so on). Errors at file level go under an `entryHeader` with no `entryNumber` (X lines 557–569).
- Each `validation` carries `errorLevel` (OK / WARNING / ERROR), `errorCode`, `errorDescription` (plus `_EN` and `_RU`) and `lineReferences` (`lineInternalId` = the submitter's `gl-cor:lineNumber`, `lineNumberCounter`) (X lines 576–603).
- `responseData` / `entryDetail` / `data` (`code`, `description`, `info`) carry either the computed tax per payment (a TAXCODE_enum value) or a detailed error (X lines 608–661).

**[DOC] Error categories per payment** (X lines 640–651; BR-J lines 37–41, where they are called MUST / SININE / PUNANE / KOLLANE):

| Category | Saved? | Taxes in the TSD koondvorm? | Blocks confirmation? |
|---|---|---|---|
| WARNING (KOLLANE) | yes | yes | no |
| ERROR_RED (PUNANE) | yes | yes | yes, until fixed |
| ERROR_BLUE (SININE) | yes | no | – |
| ERROR_BLACK (MUST) | no | – | – |

The sample `APA_TSD_tagasisideraporti_naidis_warning_red_blue_black_vigadega_20260910.xml` shows all four categories.

**[DOC] Corrections and cancellations.** These are sent again through the same `POST /submit-data`, "eeldusel et deklaratsioon on avatud andmete muutmiseks" (X lines 319–321). The developer guide's action codes are R (new), M (change: resend the whole payment) and T (cancel: reference `lineNumber` only). After T, that `lineNumber` is final (DG lines 1114–1148, PDF p.37–38).

### 1.9 How confirmation (kinnitamine) works

**[DOC] The confirmation service** (X 6, PDF p.25–29):

- It "võimaldab kliendil läbi ERPi või panga TSD deklaratsiooni kinnitada".
- Input: "digiallkirjastatud või -tembeldatud XML-faili, st .asice formaadis faili, mis sisaldab ühte XML faili" (X lines 667–669).
- YD names the part type `application/vnd.etsi.asic-e+zip` (YD line 54).
- It returns 201 with `{"uuid": …}` (X lines 704–715).
- The result is polled with `GET /v1/xtee/confirmations/{uuid}/status`. Status 204 means "Deklaratsiooni kinnitamine pole veel lõpuni jõudnud" (X lines 821, 868–869).

**[DOC] Input XML fields** (X lines 773–815; CX; CK):

- `personCode` (0..1): "Kinnitaja isikukood, kui XML-fail on digitembeldatud";
- `declaration`: the text, e.g. "Kinnitan andmete õigsust";
- `taxablePersonIdentifier` with `taxablePersonIdentifierType`: ARIREGISTRIKOOD, ISIKUKOOD or MRR_KOOD;
- `declarationType`: TSD, ESD, XF11 or XF12;
- `month` and `year`;
- the sections being confirmed:
  - TSD: PAYMENTS, L3, L4, L5, L6, L7, L8, INF1, each with `hasData`;
  - ESD: ESM_SM, ESM_KP;
- the declared `taxSums`: for TSD the codes 110, 114, 115, 116, 117, 118, 119.

**[DOC] Element names: follow the samples and the XSD, not the guide's table.** The sample and CX use `<sections><sectionItem><section>`, with `<taxSums><taxSum>` as a sibling of `sections`. The guide's table names the element `sectionType` and nests `taxSums` under `sections` (CK lines 10–73; CX lines 115–160).

**[DOC] Confirmation result** (X 7.5, PDF p.33–36):

- `processing result` is OK or ERROR. On ERROR, "Kinnitamine tuleb uuesti algatada".
- `errorCode` is one of `CANNOT_CONFIRM_DECLARATION`, `DECLARATION_PROCESSING`, `DECLARATION_WITH_ERRORS` or `DECLARATION_WITH_DISCREPANCIES`.
- `responseData` returns the confirmed tax sums (samples `…kinnitamine_ok.xml` / `…_error.xml`).
- YD notes these codes sit inside the XML, not in HTTP errors (YD lines 315–320).

**[WEB] Confirmation rights** (W-R-CONF lines 30–41):

- The TSD is confirmed "tervikdeklaratsioonina koos kõigi lisadega"; parts cannot be confirmed alone.
- Over X-tee the input "peab olema digiallkirjastatud ASiC-E konteiner". The signer must hold the raamatupidaja or isiku esindamise pakett, or the right "TSD esitamine".
- The X-tee right "Väljamakseandmete edastamine X-tee kaudu" lets you *send* the confirmation input but "ei asenda TSD kinnitamise pääsuõigust".
- In e-MTA, confirmation needs the same pakett or the single right "TSD esitamine".

**[DOC] Reopening a confirmed declaration is done in e-MTA.** Rule `DECLARATION_STATUS_OPEN_DECLARATION`: once a declaration is confirmed and has no open ("pooleli") version, new payments are refused. To add them, "tuleb e-MTA-s eelnevalt deklaratsioon muudatusteks avada" (BR lines 104–107, cells E15/G15). A declaration created and locked by an MTA official cannot take client payments (`DECLARATION_LOCKED`, BR lines 117–122). [INF] No X-tee service to reopen a declaration is documented, so a correction after confirmation always needs one e-MTA step.

### 1.10 Inconsistencies found in the documents

- **Confirmation endpoint.** The v1.3 summary list (X 141–144) disagrees with the section text (X 666, 821) and with the YAMLs (YD 36, 106). The live catalogue settles it: `/confirmations` and `/confirmations/{uuid}/status`.
- **Calculator location.** It moved from `apa-tsd/payments/v1/xtee/calculate-taxes` (YP 187, X12 line 154) to `apa-tsd/calculator/v1/xtee/tax-calculations` (X 135–136; XCAT).
- **Confirmation XML element names.** The guide table (`sectionType`) differs from CK and CX (`sectionItem/section`).
- **Calculator input.** The table in X p.11 has `sourceApplication` and `identifier`; the January developer guide's table lacks them (DG lines 1244–1270). KAL uses both.

---

## 2. Timeline

| Date or period | Event | Tag and source |
|---|---|---|
| Oct–Nov 2025 | First technical guides published (taxonomy, X-tee guide v1.0 of 19.11.2025, developer guide v1.0 of 24.11.2025) | [DOC] X lines 10–11; DG lines 10–11 |
| 13.04.2026 to end of Aug 2026 | Testing in MTA's koolituskeskkond (https://koolitus.emta.ee/) and X-tee `ee-dev` | [WEB] W-TEST lines 3, 8; [DOC] ERP0324 lines 651–652 |
| **1.10.2026** | Go-live: "Alates 1. oktoobrist 2026 saab TSD palgaväljamaksete andmeid … esitada … otse majandustarkvarast" | [WEB] W-APA line 30; W-NEWS line 13 |
| September 2026 TSD | Still filed in October through the old solution; deadline **12.10.2026**, because the 10th is a Saturday | [WEB] W-APA line 31; W-NEWS line 17 |
| October 2026 payments | First period in the new solution; deadline **10.11.2026** | [WEB] W-APA line 32 |
| From 1.10.2026 | Lisa 1/2 can no longer be sent or uploaded as the old XML: "ei ole võimalik TSD lisa 1 ja lisa 2 andmeid alates 1. oktoobrist 2026 saata või üles laadida XML-kujul" | [WEB] W-AA line 97; W-APA line 52 ("Senine XML-formaat enam ei kehti") |
| From 10/2026 | MTA slide: "vana teenusega jätkamine ei ole võimalik alates 10/2026"; scope is "tänased TSD lisa 1 ja lisa 2 ja ESD" | [DOC] ERP0324 lines 166, 170 |
| Until 30.09.2026 | The old lisa 1/2 väljamakseliikide tables are valid ("alates 01.01.2025 kuni 30.09.2026") | [WEB] W-TECH lines 907–912 |
| **Until end of 2027** | Legacy CSV upload of lisa 1/2 into e-MTA is a temporary bridge: "Ajutise üleminekulahendusena toetame 2027. aasta lõpuni ka praeguse CSV-faili üleslaadimist". CSV samples are marked "kuni 31.12.2027" | [WEB] W-APA line 52; W-AA line 97; W-TECH lines 934–937; W-FILE line 37 |
| 2027 Q2 (1.04.2027) | KMD and TÖR become data-based | [WEB] W-APA line 83; W-AA line 41 |
| 2027–2028 (news says 2028) | Lisa 3–8 and INF 1 move to the data-based solution. Until then they stay on the old form, the old XML and the X-tee `uploadMime` | [WEB] W-AA lines 98, 102; W-NEWS line 23; W-XTS lines 64–65; [DOC] ERP0324 line 190 |

**Deadline**

- [WEB] TSD is due by the 10th of the month after the payment: "esitatakse iga kuu 10. kuupäevaks" (W-APA line 3). The English page says "by the 10th day of the month following the month the payment was made" (W-EN line 3).
- [WEB] A deadline falling on a public holiday or rest day moves to the next working day. This comes from the legacy principles page (W-PRINC, under "Maksustamisperiood"), and the October 2026 example matches it.
- [INF] MTA says data may be sent "jooksvalt" (continuously) right after each payment (W-NEWS line 19). The filing obligation itself is not restated. Treat confirming the TSD by the 10th as what fulfils the deadline (see open questions).

**Is the new channel mandatory?**

- [WEB] No: "Kuigi andmepõhisele aruandlusele üleminek ei ole kohustuslik", the changed format and views still apply to e-MTA users (W-AA line 113; ERP0324 line 222).
- [WEB] Manual entry in e-MTA stays (W-APA line 81).

**How data-based and form-based submission coexist** (as stated; [INF] where marked):

- **Lisa 1/2 and ESD (payments):** data-based only. Channels: X-tee, XBRL GL upload in e-MTA, legacy CSV upload until 31.12.2027, manual entry in the payments view, or a bank. One unified view replaces lisa 1 and lisa 2: resident and non-resident payments and refunds together, with no separate A and B parts (W-APA line 51; W-AA lines 99–100).
- **Lisa 3–8 and INF 1:** still form-based. Since 01.10.2026, MTA's sample TSD XML (the old `tsd_vorm` format) contains only INF1, L4, L5, L6, L7 and L8, with no L1/L2 (W-TECH lines 924–925; file `web\dl_xmlnaide\xml_naide_01.10.2026.xml`). [INF] The old `tsd_vorm` XML import therefore lives on for lisa 3–8 and INF 1.
- **Confirmation covers the whole declaration:** PAYMENTS plus L3–L8 plus INF1 together (W-R-CONF line 32; X lines 795–796).
- **The XBRL period cannot predate the APA start period** (`KR_VALIDATE_XBRL_FILE_4`, BR lines 76–78). [INF] Corrections to September 2026 and earlier must use the old route (see open questions).
- **The file's period must equal the UI period chosen when uploading:** "kasutajaliidese maksustamisperioodiga, kuhu faili soovitakse laadida" (`KR_VALIDATE_XBRL_FILE_8`, new in v2.0, BR lines 96–100). [INF] This confirms that the e-MTA upload asks for a period.
- **Prior-year rule:** after 15.02, payments with withheld income tax (TSD_KTM) cannot be added to an unconfirmed prior-year TSD, and a confirmed prior-year TSD cannot be changed so that KTM changes. Non-resident legal persons and third-country individuals are exempt (BR lines 951–958, E133/E134).

---

## 3. What an accounting firm needs (representing many clients)

**[DOC] One sender for many clients is the design case.** When the services are used through a service provider, the message must carry `X-Road-Represented-Party` naming the client. The example is an ERP vendor whose own security server sends for all its clients, with the client's identifier in every message (X lines 164–172, PDF p.7). The same rule applies to fetching feedback (X lines 175–177).

**[DOC] Rights checks:**

| Rule | Check | HTTP error | Source |
|---|---|---|---|
| `KR_XTEE_VASTUS_2` | If the represented party differs from the client, the client must have the right to send payments for it | 403 "Kontrollige volituse olemasolu" | BR lines 13–18 |
| `KR_XML_FAIL_1` / DE07 | A sender who is not the taxpayer needs a "volitus esindatava eest üle x-tee väljamakseid esitada" | 403 | BR lines 31–36 |
| `KR_VALIDATE_XBRL_FILE_6` | The registry code in the file must equal the taxpayer in the message header | – | BR lines 82–85 |

**[WEB] e-MTA authorisations, from the client to the sender** (W-R-XT lines 30–43). The client grants the "turvaserveri teenusepakkuja" an "X-tee volitus" in e-MTA, either:

- the single right "Väljamakseandmete edastamine X-tee kaudu"; or
- the package XT_MM ("Masin-masin liidese volituse pakett (KMD, TÖR, TSD)").

Existing XT_MM authorisations were extended automatically. If the same provider continues, no new volitus is needed. A new provider needs a new volitus. Clients using "Andmesild" as their security-server provider must also authorise Andmesild.

**[WEB] An accountant can now hand out the M2M authorisation** (W-VOL lines 89–112):

- The company representative gives the accountant the packages **I** (raamatupidaja pakett) and **XT_MM**, with delegation allowed. The accountant can then authorise security-server providers with XT_MM on the company's behalf.
- "Kui raamatupidamisteenust osutab äriühing": when the packages are given to an accounting company, delegation is allowed by default. The firm's representative can pass on:
  - both I and XT_MM to an individual accountant; or
  - I to an individual accountant and XT_MM to the security-server provider.
- When the accountant's packages end, everything delegated from them ends too, including XT_MM given to security-server providers.
- Background: as of March 2026 only a management-board member could grant this, and MTA aimed to ship accountant delegation by 1.10.2026 (ERP0324 lines 113–149). The October 2026 W-VOL page shows it is in place.

**[WEB] Rights for e-MTA work** (W-R-EMTA lines 30–48):

- The raamatupidaja pakett or isiku esindamise pakett already covers viewing, adding, changing and cancelling payments, lisa 3–8 and INF 1, and confirming the TSD.
- Without a package, a user needs "Väljamakseandmete esitamine" or "Väljamakseandmete esitamine juriidilise isiku esindajana". This includes uploading an "XBRL-GL- või CSV-faili … e-MTA väljamaksete vaates".
- An auditor-style read-only user needs "Väljamakseandmete vaatamine".

**[WEB] Confirmation over X-tee needs a person with rights** (W-R-CONF lines 39–41). Each client's confirmation ASiC-E must be signed by a person who holds, for that client, the raamatupidaja or esindaja pakett or "TSD esitamine".

**[INF] What this means for a small accounting firm:**

- Over X-tee, the firm can run **one** subsystem for all clients (its own or a hosted security server) and send `X-Road-Represented-Party: COM/<client regcode>` per message.
- Each client needs a one-off e-MTA volitus to the firm: XT_MM or "Väljamakseandmete edastamine X-tee kaudu".
- MTA must open the three `apa-tsd` services for the firm's subsystem (W-XTS line 8: the subsystem "X-tee teenust tegelikult kasutava asutuse").
- Confirmation can be signed per client by the accountant personally with ID-card or Smart-ID-based signing, if available, as long as the accountant holds pakett I for that client. Or it can be done by hand in e-MTA.
- Without X-tee, the same accountant needs only pakett I per client to upload the XBRL GL file and confirm in e-MTA.

---

## 4. The calculator service (väljamakse maksude kalkulaator)

**Transport: X-tee only**

- [DOC] Defined only as an X-tee REST service: `POST apa-tsd/calculator/v1/xtee/tax-calculations`. It is synchronous: XML in, XBRL GL XML out ("tagastab sünkroonselt väljamaksete ja arvutatud maksudega XBRL-faili") (X lines 189–197, PDF p.8).
- [DOC] It replaces the old TSD2 bank payroll calculator and extends it to ERPs (X lines 194–195; DG lines 1238–1240).
- [WEB] The live catalogue lists `EE/GOV/70000349/apa-tsd/calculator/` with path `/v1/xtee/tax-calculations` (XCAT). MTA's slides call it a "REST teenus" (BANK1211 line 132).
- [INF] No public, non-X-tee version was found.

**Rights**

- [DOC] Rule `KR_KALK_OIGUS_01`: a caller who is not the taxpayer needs the same "volitus esindatava eest üle x-tee väljamakseid esitada" (BR-K lines 41–48, sheet "KALKULEERIMISE LISAREEGLID").
- [DOC] Banks: the calculator user needs the e-MTA right to submit payments, and the bank needs an X-tee contract with MTA (BANK1211 lines 171, 193–197).

**Scope** [DOC] (X line 193; DG lines 1234–1276):

- Estonian taxation only (no A1 / country-of-work cases).
- Accepts net or gross amounts.
- Tax-free income (maksuvaba tulu) must be sent as an exact amount.
- Social-tax monthly-minimum top-up goes in a separate field.
- No taxable dividends.
- All payments in a file must fall in one calendar month (BR-K E6).
- [DOC] The older OpenAPI says the calculator "does not persist payments nor trigger declaration confirmation" (YP lines 191–193). That path is stale, but the meaning holds.

**Does e-MTA calculate or preview on upload?**

- [WEB] MTA says the new solution gives "kohe tagasiside esitatud andmete ja maksude kohta" (W-APA line 35), and that "Deklaratsiooni loob ja maksukohustuse arvutab MTA" from the data (W-NEWS line 16).
- [DOC] Payments with no (or only warning or red) errors are stored and their taxes summed into the TSD koondvorm. The feedback `responseData` returns the computed tax per payment (X lines 609–612; BR-J lines 36–41).
- [DOC] MTA's checks recompute taxes and show the formula in the message, e.g. `KR_TM_4` with "(500 - 10 - 8 - 0 ) * 22%" in the sample report (FR sample 20260910 line 66).
- [INF] So e-MTA does compute taxes on upload, but there is **no documented dry-run or preview**. An upload *stores* the payments in an open ("pooleli") declaration, and nothing is final until kinnitamine. The only side-effect-free way to compute is the X-tee calculator.

---

## 5. Related public lookups the app needs

### 5a. Non-resident register check (mitteresidentsuse päring)

**[WEB] The public form at apps.emta.ee** (NR; the same page also lives on `maasikas.emta.ee/saqu/public/nonresident`):

- It is a plain HTML form, "Mitteresidentsuse päring", posting to `/saqu/public/nonresident/query`.
- Inputs: the Estonian isikukood, registrikood or mitteresidendi kood; the residence-country personal or registry code; or the residence-country VAT number. "%" wildcards are allowed.
- The page itself steers bulk checks to X-tee: for many persons, use the X-tee service "Mitteresidentsusandmete avalik päring", and "Teenuse kasutamiseks tuleb Maksu- ja Tolliametiga sõlmida leping".

**[WEB] The official machine service** (W-TECH lines 730–755):

- Technical name: `EE/GOV/70000349/persons/PublicNonResidencyInquiry/v1`. It is SOAP: "Teenuse WSDL | 07.03.2022", plus the PDF "Mitteresidentsusandmete x-tee spetsifikatsioon".
- Input: the MRR code, a personal or registry code with its country, a VAT number with its country, and the as-of date.
- Output:
  - the person type and the name;
  - records of type EMPLOYER, MAIN, PERMANENT_ESTABLISHMENT or PROOF_OF_RESIDENCE;
  - the residence country, and start and end dates.

[INF] It is not in W-XTS's list of contract-free software services, so a contract with MTA is needed, as NR says.

**[DOC] APA TSD checks residency on the server itself.** [INF] So the feedback report is a second safety net after the app's own lookup. The rules:

| Rule | Check | Category | Source |
|---|---|---|---|
| `KR_VM_03` | A resident-only payment type was used for a non-resident | SININE | BR lines 519–523 (E72) |
| `KR_VM_04` | A non-resident-only payment type was used for a resident | SININE | BR lines 527–530 (E73) |
| `KR_ISIK_6` | The declared tax-residence country (RR_MAKSURESIDENTSUSE_RIIK) must match MTA's Isikute haldus | PUNANE | BR lines 589–593 (E82) |
| (MVT rules) | Tax-free income needs EEA residency and a valid EEA residentsustõend | – | BR lines 874–883 (E122–E123) |

The sample report shows `KR_VM_03` as ERROR_BLUE (FR sample 20260910 lines 49–53).

### 5b. II-pillar (kohustuslik kogumispension) membership and rate

**[WEB] Three official Pensionikeskus channels:**

- **Liitumiskontrolli lihtpäring.** The web form the app scrapes today; it "töötab ühe isikukoodi kaupa" (PK-L line 23). It answers whether the person has joined, the withholding start date, and historic special cases. The legal basis and purpose limit are "EVKS § 7 lg 6¹ ja KoPS § 11 ning § 27²"; the query must serve the employer's statutory checking duty (PK-L, footer text).
- **Liitumiskontrolli masspäring** (PK-M lines 17–29):
  - Pensionikeskus sends back a file with an answer per personal code.
  - It costs €30 per query (no VAT) for up to 4,000 codes.
  - It needs an ID-card-signed application (form `Liitumisparingu_vorm_ASPensionikeskus_2026.pdf`).
  - Codes go in a TXT or XLS file, one per line, encrypted to the Pensionikeskus AS certificate. The result comes back by e-mail as TXT or XLS, with an e-invoice.
- **Automaatne liitumiskontroll**, the official machine option for payroll software (PK-A lines 17–35):
  - It uses "spetsiaalset XML päringuformaati" and answers inside the payroll program.
  - It covers rate information: "Alates 2024. aastast saab XML päringuga infot ka II samba uute maksemäärade kohta", and since 01.12.2024 the person's chosen rate. A III-pillar XML query has existed since November 2024.
  - It costs a joining fee for a query package plus a yearly maintenance fee. The amounts are not published.
  - It does not check tax residency. Non-residents must not have the payment withheld.
  - [INF] The XML spec is not public; it comes from AS Pensionikeskus on joining.

**[WEB] Rates in 2026: 2 %, 4 % or 6 % of gross salary; 2 % is the default.**

- [WEB] W-KP line 5: "Kohustusliku kogumispensioni makse määr on 2%, 4% või 6%".
- [WEB] PK-RATE lines 12–19:
  - The state's 4 % from social tax is unchanged by the choice (W-KP line 32 too).
  - A new rate always starts in January and can be changed once a year.
  - An application by 30 November applies from 1 January next year. One filed in December applies from January of the year after next.
- [WEB] The rate is personal and the same at all of the person's employers. Employers check it on Pensionikeskus's site, or by masspäring if they are large (W-KP line 30).

**[WEB] How and when an employer learns the rate:**

- The obligation start and end change three times a year (1 January, 1 May, 1 September). The employer must check in December, April and August (W-KP line 11). It does so through the liht- or masspäring (W-KP line 12).
- The rate must be checked once a year (W-KP line 31).
- [INF] The practical rule is to re-check every employee in December (obligation plus rate for January), April and August.

**[DOC] MTA validates both membership and rate in APA TSD:**

| Rule | Check | Category | Source |
|---|---|---|---|
| `KR_KOP_02` | No TSD_KP may be withheld if the person has no obligation | PUNANE | BR lines 830–834 |
| `KR_KOP_03` | The withheld TSD_KP must equal MTA's value, shown as "(<väljamakse SMVM> *<isiku määr>%)" | PUNANE | BR lines 837–841 |

[DOC] The calculator's input has no rate field (X lines 274–313; KAL). [INF] So MTA applies the registry rate itself, and the calculator and feedback report give an authoritative cross-check of the 2/4/6 % choice. It is not a replacement for the employer's duty to check.

---

## 6. Bookmarks

**MTA, process and rules**

- Andmepõhine TSD, the overview and change page: W-APA. English version: W-EN.
- Upload by file (XBRL GL / CSV) in e-MTA: W-FILE. Videos: https://youtu.be/watch?v=AAJxE-djaws (XML 00:49, CSV 03:18).
- Software plus X-tee process video: W-XTV (https://youtu.be/watch?v=3A1eSuixjp4).
- Access rights: W-R-EMTA, W-R-XT, W-R-CONF. Delegating XT_MM: W-VOL, section "Masin-masin liidese volituste paketi (KMD, TÖR, TSD) haldamine".
- X-tee services list and how to open them: W-XTS.
- Technical info, with the old TSD XML/CSV formats, the uploadMime WSDL and the non-residency service: W-TECH (#tsd, #mitteresidentsusandmed).
- Andmepõhine aruandlus FAQ and timeline: W-AA. Newsletter sign-up: W-AA#uudiskiri.
- Testing page and application form: W-TEST (https://ncfailid.emta.ee/s/DzamGcWHBmdHij3/download/taotlus_apa_tsd_testimiseks.pdf).

**MTA, technical documents**

- Full technical documentation share: https://ncfailid.emta.ee/s/TiG3ajyNdETfLK4. Guides and business rules only: https://ncfailid.emta.ee/s/x5z9rqLzrpTsZ3m. Examples only: https://ncfailid.emta.ee/s/qkY9rm7fdjXDCM5.
- Live OpenAPI per service, through the X-tee catalogue: https://x-tee.ee/et/service-catalog/subsystems (member 70000349, subsystem `apa-tsd`).

**Other bodies**

- Statistikaamet APA taxonomy (newer: "APA taksonoomia (10.2026)") and the feedback-report structure PDF: STAT and https://stat.ee/sites/default/files/2025-04/APA_tagasisideraporti_struktuur_versioon_II_20250402.pdf.
- RIA X-tee options and costs: RIA. Joining: https://x-tee.ee (self-service) and https://abi.ria.ee/xtee/.
- Pensionikeskus for employers: https://www.pensionikeskus.ee/raamatupidajale/ii-sammas/ (PK-L, PK-M, PK-A); rate rules: PK-RATE.
- Non-residency: NR (form) and W-TECH#mitteresidentsusandmed (X-tee).

### Open questions

1. **The e-MTA XBRL GL upload: limits and behaviour.** Not found:
   - the size limit;
   - whether ZIP is accepted;
   - whether the feedback report can be downloaded as XML from the UI.

   `KR_VALIDATE_XBRL_FILE_8` implies a period is chosen before upload.
2. **Who exactly receives the volitus when the security server is hosted.** MTA's pages say "turvaserveri teenusepakkuja". The X-tee guide says the `X-Road-Client` member must hold the right. If an accounting firm uses a hosted security server, is the volitus given to the firm's registry code (the X-Road-Client) or to the hosting company? Ask MTA (teenindusabi@emta.ee).
3. **What "Andmesild"** (W-R-XT line 43) is: a specific intermediary security-server service. Its operator and terms were not found.
4. **Corrections to September 2026 and earlier periods after go-live.** Through the old TSD form or XML in e-MTA? XBRL is refused for pre-APA periods (`KR_VALIDATE_XBRL_FILE_4`).
5. **Legacy CSV after 1.10.2026.** Does it keep the old lisa 1/2 payment-type codes (tables valid only until 30.09.2026), with MTA mapping them to the new codes, or must it use the new codes?
6. **Deadline semantics.** Does unconfirmed data in an open declaration on the 10th count as filed? Is there automatic confirmation or a reminder? The page "teenusega-seotud-teavitused" returned HTTP 403 to automated fetches, so this is unverified.
7. **Test access after go-live.** Can a new developer get koolituskeskkond and `ee-dev` access? W-TEST still says testing ran until the end of August 2026.
8. **E-seal confirmation.** Can an accounting firm confirm with its own e-seal (digitembeldatud, with `personCode` naming the accountant), or must the accountant sign personally? Is the CX `personCode` pattern Estonian-only?
9. **Paper filing** (≤5 rows): does it still apply to the payments part after October 2026?
10. **Pensionikeskus automaatne liitumiskontroll:** price, contract and XML spec are not public. Also the legal position of scraping the lihtpäring from software (the purpose-limit text).
11. **The non-residency X-tee service:** contract terms and fees. Also the terms of use for automating the apps.emta.ee form (none found on the page).
12. **`GET /return-data` without uuid** (X line 119): mentioned once, specified nowhere.

---

## 7. Summary (10 lines)

1. APA TSD went live on 1.10.2026. October 2026 is the first period, due 10.11.2026; September 2026 used the old system, due 12.10.2026.
2. The scope is the payment data of former TSD lisa 1, lisa 2 and ESD. Lisa 3–8 and INF 1 stay on the old form and XML (and X-tee `uploadMime`) until 2027–2028.
3. The old TSD XML for lisa 1/2 has been dead since 1.10.2026. The legacy lisa 1/2 CSV upload in e-MTA survives only until 31.12.2027.
4. The XBRL GL file can be uploaded by hand in the e-MTA "väljamaksete vaade", by anyone with raamatupidaja pakett I or "Väljamakseandmete esitamine". No X-tee is needed.
5. Machine submission is X-tee only: subsystem `apa-tsd`, services `payments`, `calculator` and `declarations`, member `EE/GOV/70000349`. No public API-key or Smart-ID REST endpoint exists.
6. Submit is POST `/v1/xtee/submit-data` (XML or ZIP with one XML, ≤50 MB, returns 201 with uuid and estimate). Feedback is fetched by polling `GET /v1/xtee/return-data/{uuid}` (204 until ready).
7. Confirmation is a signed or sealed ASiC-E posted to `/v1/xtee/confirmations` and polled at `/confirmations/{uuid}/status`. The signer needs pakett I or "TSD esitamine". Reopening a confirmed TSD is done in e-MTA.
8. An accounting firm can use one subsystem for all clients with `X-Road-Represented-Party`. Each client gives an e-MTA volitus: XT_MM, or "Väljamakseandmete edastamine X-tee kaudu". Accountants can now delegate XT_MM.
9. The tax calculator is synchronous and X-tee-only. e-MTA computes taxes on upload, but has no documented dry-run.
10. Non-residency has an official X-tee SOAP service (contract needed). Pension membership and rate (2/4/6 %, changes effective 1 January) come from Pensionikeskus masspäring (€30 per file) or the paid XML automaatne liitumiskontroll, and MTA re-checks both (`KR_KOP_02/03`, `KR_VM_03`, `KR_ISIK_6`).

**Recommendation:** build the app to generate a valid XBRL GL file and plan for manual upload and confirmation in e-MTA now, because it needs no setup. Design the same file and feedback parser for later X-tee submission through a hosted security server or an intermediary, and do not invest in the CSV route, which ends 31.12.2027.
