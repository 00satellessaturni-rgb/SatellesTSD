# APA TSD (andmepõhine TSD) — XBRL GL XML reference for EE0201001 / EE0201002

Scope: what a C# desktop app must write to report an ordinary Estonian äriühing's payments (salary, board fees, sick pay, rent, non-resident payments) in the new XBRL GL format, and what comes back. Section 5 of the developer guide (tax calculator) is out of scope. EE0201003 (ESD) is covered only where it explains structure.

## Sources

Short names used in this file:

| Short name | File (under `scratchpad\`) | Version / date |
|---|---|---|
| **Guide** | `dump\pdf\juhend_arendajale_20260130.txt` ("Juhend XBRL GL failiformaadis palgamaksete, hüvitiste ja erijuhtude sotsiaalmaksu andmete saatmiseks") | v1.1, 30.01.2026. Sections 1–4 are identical to v1.0 of 24.11.2025; v1.1 only adds §5. |
| **Tax.** | `dump\taksonoomia\sheets\*.txt` (taxonomy workbook `APA_TSD_TAKSONOOMIA_20251017.xlsx`) | Uuendused says "09.2025 vs 04.2025" |
| **Rules** | `dump\arireeglid\sheets\SUBMIT DATA ÄRIREEGLID.txt` + `Juhis.txt` | Juhis: v2.0, 24.07.2026 |
| **Ex-XLSX** | `dump\naide\sheets\äriettevõtte näide.txt`, `tehingu tühistamise näide.txt` | 20251017 |
| **Ex-XML** | `rules\Naited_ja_tehnilised_failid\APA_TSD_lahteandmed_ariuhingule_naide_XML_20251017.xml` | 20251017 |
| **Ex-T** | `...\APA_TSD_lahteandmed_ariuhingule_naide_tuhistamine_XML_20251017.xml` | 20251017 |
| **FB-XSD / FB-n** | `...\APA_TSD_20260330_feedbackReport.xsd`, the four `APA_TSD_20260330_tagasisideraport_*.xml`, `APA_TSD_tagasisideraporti_naidis_warning_red_blue_black_vigadega_20260910.xml` | 2026-03-30 / 2026-09-10 |
| **Conf** | `...\APA_TSD_20260326_kinnitamine_naidis.xml`, `rules\APA_TSD_XSD_naidised\confirmations-2026-09-23-apatsd.xsd` | 2026-03-26 / 2026-09-23 |
| **YAML** | `...\APA_TSD_20260603.yaml` (declarations API), `...\APA_TSD_payments_20260603.yaml` (payments API). Both are OpenAPI 3.0.3. | 2026-06-03 |
| **XSD** | `rules\APA_TSD_XSD_naidised\gl-*-apatsd.xsd`, `xbrl-instance-2003-12-31-apatsd.xsd` | 16.04.2026 |
| **XTEE** | `dump\pdf\xtee_20260720.txt` ("Juhend APA-TSD rakendusega liidestumiseks üle X-Tee"). Not on the reading list; consulted only for the feedback and confirmation semantics in §F, and cited where used. | v1.3, 20.07.2026 |

**Conventions.**
- **K / V / T** are the Guide's *Olemasolu* values: K = Kohustuslik (mandatory; must be present and non-empty), V = Valikuline (optional), T = Tingimuslik (optional by default, but business rules can make it mandatory).
- **XSD a..b** is the cardinality in the APA-modified XSD, which is stricter than the Guide in places.
- Rule severities: **BLACK** = MUST, **BLUE** = SININE, **RED** = PUNANE, **WARN** = KOLLANE. The feedback report calls them ERROR_BLACK, ERROR_BLUE, ERROR_RED and WARNING (see §F).

---

## A. File skeleton

### A.1 Annotated skeleton

Values are copied from Ex-XML; `{…}` marks a placeholder. All facts carry `contextRef="now"`.

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!-- The declaration appears in the Guide p.20 header example. It is absent in all MTA example instances,
     which start directly with "<xbrli:xbrl" and have no BOM. -->
<xbrli:xbrl xmlns:xbrli="http://www.xbrl.org/2003/instance"
    xmlns:xlink="http://www.w3.org/1999/xlink"
    xmlns:xbrll="http://www.xbrl.org/2003/linkbase"
    xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
    xmlns:gl-usk="http://www.xbrl.org/int/gl/usk/2015-03-25"
    xmlns:gl-cor="http://www.xbrl.org/int/gl/cor/2015-03-25"
    xmlns:gl-ext="http://www.xbrl.org/int/gl/ext/2015-03-25"
    xmlns:iso639="http://www.xbrl.org/2005/iso639"
    xmlns:iso4217="http://www.xbrl.org/2003/iso4217"
    xmlns:gl-plt="http://www.xbrl.org/int/gl/plt/2015-03-25"
    xmlns:gl-bus="http://www.xbrl.org/int/gl/bus/2015-03-25"
    xmlns:gl-muc="http://www.xbrl.org/int/gl/muc/2015-03-25"
    xsi:schemaLocation="http://www.xbrl.org/int/gl/plt/2015-03-25 https://www.xbrl.org/taxonomy/int/gl/2015-03-25/plt/case-c-b-m-u-t/gl-plt-all-2015-03-25.xsd">
    <!-- K 1. All three attributes are required. -->
    <xbrll:schemaRef xlink:href="../plt/case-c-b-m-e/gl-plt-2015-03-25.xsd" xlink:arcrole="http://www.w3.org/1999/xlink/properties/linkbase" xlink:type="simple"/>
    <!-- K 1. The XSD allows exactly ONE context. Its id is what every contextRef points at. -->
    <xbrli:context id="now">
        <xbrli:entity>
            <!-- K 1. The scheme attribute is required (anyURI, minLength 1). The value is the reporter's Äriregister code. -->
            <xbrli:identifier scheme="http://www.ariregister.rik.ee">{registrikood}</xbrli:identifier>
        </xbrli:entity>
        <xbrli:period>
            <!-- K 1. "Faili loomise kuupäev" (file creation date), yyyy-MM-dd. Equal to creationDate in every example. -->
            <xbrli:instant>{yyyy-MM-dd}</xbrli:instant>
        </xbrli:period>
    </xbrli:context>
    <!-- T 0..n. Required whenever numeric facts exist, which is always the case for TSD. The ids are free text,
         but each unitRef must match one. "eur" is for money; "NotUsed" (measure "pure") is for counters and rates. -->
    <xbrli:unit id="eur">
        <xbrli:measure>iso4217:EUR</xbrli:measure>
    </xbrli:unit>
    <xbrli:unit id="NotUsed">
        <xbrli:measure>pure</xbrli:measure>
    </xbrli:unit>
    <gl-cor:accountingEntries>                                                       <!-- K 1 -->
        <gl-cor:documentInfo>                                                        <!-- K 1 -->
            <gl-cor:entriesType contextRef="now">other</gl-cor:entriesType>          <!-- K, always "other" -->
            <gl-cor:uniqueID contextRef="now">{registrikood}-{yyyy-MM-dd}T{HH:mm:ss.fff}</gl-cor:uniqueID> <!-- K, format: see A.3 -->
            <gl-cor:language contextRef="now">iso639:et</gl-cor:language>            <!-- K, always iso639:et -->
            <gl-cor:creationDate contextRef="now">{yyyy-MM-dd}</gl-cor:creationDate> <!-- K -->
            <gl-bus:creator contextRef="now">{who compiled it}</gl-bus:creator>       <!-- K -->
            <gl-cor:entriesComment contextRef="now">{free text}</gl-cor:entriesComment> <!-- V 0..1 -->
            <gl-cor:periodCoveredStart contextRef="now">{1st day of tax month}</gl-cor:periodCoveredStart> <!-- K -->
            <gl-cor:periodCoveredEnd contextRef="now">{last day of tax month}</gl-cor:periodCoveredEnd>    <!-- K -->
            <gl-bus:sourceApplication contextRef="now">{software name}</gl-bus:sourceApplication>         <!-- K -->
            <gl-muc:defaultCurrency contextRef="now">iso4217:eur</gl-muc:defaultCurrency> <!-- K, always iso4217:eur -->
        </gl-cor:documentInfo>
        <gl-cor:entityInformation>                                                   <!-- K 1 -->
            <gl-bus:organizationIdentifiers>                                         <!-- K 1 -->
                <gl-bus:organizationIdentifier contextRef="now">{registrikood}</gl-bus:organizationIdentifier>        <!-- K 1 -->
                <gl-bus:organizationDescription contextRef="now">ARIREGISTRIKOOD</gl-bus:organizationDescription>    <!-- K 1 -->
            </gl-bus:organizationIdentifiers>
        </gl-cor:entityInformation>
        <gl-cor:entryHeader> ... </gl-cor:entryHeader>   <!-- K 1..n: one per section, see B -->
    </gl-cor:accountingEntries>
</xbrli:xbrl>
```

### A.2 Header element table

The element order shown is the order the XSD requires.

| Element | Guide | XSD | Type | Fixed value / rule | Source notes |
|---|---|---|---|---|---|
| `xbrli:xbrl` | root | root seq: schemaRef 1, context 1, unit 0..n, accountingEntries 1 | — | — | xbrl-instance-…-apatsd.xsd |
| `xbrll:schemaRef` | K 1 | 1 | empty | `xlink:href`, `xlink:arcrole`, `xlink:type="simple"` | The Guide p.20 header snippet uses href `../plt/case-c-b-m-u-t/gl-plt-all-2015-03-25.xsd`. Ex-XML, Ex-T and the Guide's own §3/§4 instance examples all use `../plt/case-c-b-m-e/gl-plt-2015-03-25.xsd`. **The sources disagree**; copy Ex-XML. |
| `xbrli:context @id` | K 1 | exactly 1 | — | id referenced by every `contextRef` | |
| `xbrli:entity/xbrli:identifier @scheme` | K 1 | 1 | token | scheme `http://www.ariregister.rik.ee`; value = Äriregister code | |
| `xbrli:period/xbrli:instant` | K 1 | 1 | date or dateTime | file creation date `aaaa-kk-pp` | |
| `xbrli:unit @id` + `xbrli:measure` | T 0..n, measure K 1 | 0..n | QName | `iso4217:EUR` for money, `pure` otherwise | |
| `gl-cor:accountingEntries` | K 1 | 1 | — | — | |
| `gl-cor:documentInfo` | K 1 | 1 | — | — | |
| `gl-cor:entriesType` | K 1 | 1 | enum | **`other`** ("TSD puhul alati 'other'") | |
| `gl-cor:uniqueID` | K 1 | 1 | string | see A.3 | |
| `gl-cor:language` | K 1 | 1 | QName | **`iso639:et`**. The XSD enumeration also accepts `iso639:ET`. | |
| `gl-cor:creationDate` | K 1 | 1 | date | `aaaa-kk-pp` | |
| `gl-bus:creator` | K 1 | 1 | string | "Aruande looja (üksus, isik, isikukood)". Tax. adds that this may be the contact person the authority can turn to. Free text. | |
| `gl-cor:entriesComment` | V 0..1 | 0..1 | string | free text, e.g. "Andmepõhise aruandluse väljamaksete andmed MTA-le" | |
| `gl-cor:periodCoveredStart` | K 1 | 1 | date | first day of the accounting month (*arvestuskuu*) | Rules: FILE_4/7/8 |
| `gl-cor:periodCoveredEnd` | K 1 | 1 | date | last day of the same month | Rules: FILE_7 |
| `gl-bus:sourceApplication` | K 1 | 1 | string | name of the software or system | |
| `gl-muc:defaultCurrency` | K 1 | 1 | QName | **`iso4217:eur`**. The XSD enumeration also accepts `iso4217:EUR`. | The generic part of Tax. "XML väljade info" says *Tingimuslik*; its TSD part and the Guide say mandatory. |
| `gl-cor:entityInformation` | K 1 | 1 | — | The XSD allows **only** `organizationIdentifiers`. Phone, e-mail and address are SA/PPA-only and not allowed. | |
| `gl-bus:organizationIdentifiers` | K 1 | 1 | — | — | |
| `gl-bus:organizationIdentifier` | K 1 | 1 | string | the taxpayer / payer code, e.g. `10235688` | Must equal the taxpayer in the X-Road message header (KR_VALIDATE_XBRL_FILE_6) |
| `gl-bus:organizationDescription` | K 1 | 1 | string | ORGANIZATIONDESCRIPTION_enum | see below |

**ORGANIZATIONDESCRIPTION_enum.** These values are allowed in EE0201001, EE0201002 and EE0201003, marked "Jah*", meaning "submit when that is the identifier type":
- `ARIREGISTRIKOOD` (Business registry code)
- `ISIKUKOOD` (personal ID code)
- `MRR_KOOD` (MTA non-resident registry or personal code)

`TOIMLA_KOOD` is used only in SA accommodation sections. For an äriühing, use `ARIREGISTRIKOOD`.

### A.3 `uniqueID` format — the sources disagree

- **Guide table:** `<Asutuse äriregistrikood>` + `'-'` + `<aruande kuupäev>(aaaa-kk-pp)` + `'-'` + `<kellaaeg>(ISO8601)`. Here the "asutus" is the one whose data is reported.
- **Tax. "Üldised andmeväljad":** company registry code + report date + time, with the time "per ISO 8601, any format the standard allows". Its example is `10002456-2016-01-08T15:16:44.554`.
- **Tax. "XML väljade info":** the generic example is `10002456-2023-12-15T09:36:51.554`, and the TSD example is `10002456-2026-09-09T09:36:51:554`.
- **Ex-XML / Ex-T / Guide examples:** `10235688-2026-10-02T09:36:51:554` and `10235688-2026-10-20T09:35:41:544`.

So the examples join date and time with **`T`**, not with the second `-` that the Guide table describes. The TSD examples separate milliseconds with **`:`**, which is not ISO 8601. The XSD types it as a plain string with no pattern. The ISO-conformant form `{regcode}-{yyyy-MM-dd}T{HH:mm:ss.fff}` matches "Üldised andmeväljad" and the generic example. The files say nothing about whether the server checks this format or whether it must be unique across submissions.

---

## B. `entryHeader` — one per data section

```xml
<gl-cor:entryHeader>                                              <!-- K, XSD 1..n -->
    <gl-cor:entryNumber contextRef="now">EE0201001</gl-cor:entryNumber>   <!-- K 1, APA_SEKTSIOONID_enum -->
    <gl-cor:entryComment contextRef="now">PARANDUSPOHJUS_1</gl-cor:entryComment> <!-- V 0..1, ENTRYCOMMENT_enum -->
    <gl-cor:entryDetail>…</gl-cor:entryDetail>                    <!-- T 0..n: one per payment -->
</gl-cor:entryHeader>
```

### B.1 `entryNumber` (APA_SEKTSIOONID_enum, MTA rows)

| Code | Name | What goes there | Deadline (column D of the enum) |
|---|---|---|---|
| `EE0201001` | Tööjõumaksude andmed Maksu- ja Tolliametile | "Väljamaksed, mis tulenevad töösuhtest, mille kohta on TÖR-s registreeritud töötamise kanne **sh VÕS ja juhatuse liikme töötasud**". Ex-XML also puts employer-paid sick pay (type 24) here. | 10th of the month after the payment month. If no payment was made, social tax on the monthly minimum is reported by the 10th of the month after the agreed pay month. |
| `EE0201002` | Hüvitisite, toetuste jm väljamaksete andmed | "Väljamaksed, mis ei tulene otsesest töösuhtest (nt üüritulu, muu tulu, toetused, hüvitised, pension, Tervisekassa haigushüvitis, intressid jne)". Ex-XML puts rent (type 50) here. | 10th of the month after the payment month |
| `EE0201003` | Erijuhtude sotsiaalmaksu ja kohustusliku kogumispensioni täiendava sissemakse andmed (ESD) | "riigi-, valla- või linnaasutused, avalik-õiguslik isik, loomeliit. Füüsilisest isikust ettevõtja oma abikaasa eest." Not used by an ordinary äriühing. | 10th of the following month |

Code structure (Tax. "Üldised andmeväljad"): `EEXXYYZZZ`.
- `EE` is the country.
- `XX` is the receiving authority (SA=01, MTA=02, RIK=03, KLIM=04, PPA=05).
- `YY` is the domain.
- `ZZZ` is the section.

**Number of entryHeaders.** XTEE §5.5 says "ühe andmeseksiooni kohta on failis üks entryHeader plokk", i.e. one block per section in a file. So all payments of a section go under one entryHeader. Tax. Juhend B5 says the minimum unit sent is one whole section, and "Võimalusel saata alati ühe valdkonna andmesektsioonid korraga" (if possible, send a domain's sections together). The files give no ordering rule between sections; Ex-XML has 01 then 02.

### B.2 `entryComment` (ENTRYCOMMENT_enum)

Guide: needed only "juhul, kui TSD sektsioonide osas on vaja esitada **kinnitatud** andmete muutmise põhjuseid", i.e. when correcting already-confirmed data. Cardinality differs: the Guide says V 0..1, Tax. "XML väljade info" says *Tingimuslik* in its generic part and *Valikuline* in its TSD part, and the XSD says 0..1. The comment sits at the section level, so one reason covers every payment in that section of the file.

| Value | Meaning | Allowed in |
|---|---|---|
| `PARANDUSPOHJUS_1` | Vead füüsilisele isikule tehtud väljamaksete ja/või maksude ja maksete arvestamisel (errors calculating payments or taxes) | EE0201001, EE0201002, EE0201003 |
| `PARANDUSPOHJUS_2` | Maksukontrolli tulemusel varem deklareeritud andmete muutmine või andmete lisamine (tax audit) | EE0201001, EE0201002 |
| `PARANDUSPOHJUS_3` | Maksukohustus teises riigis (tax liability in another country) | EE0201001 only |
| `PARANDUSPOHJUS_4` | Tagasiulatuvalt hüvitise (nt töövõimetuspensioni) määramine (retroactive benefit) | EE0201002 only |

### B.3 Empty section (no payments in the period)

- **Guide (table after `entryNumber`):** "Aruandeperioodil andmete puudumisel, pole järgnevad elemendid kohustuslikud ja piisab 'entryHeader' ja 'entryNumber' elemendist" (if the period has no data, the following elements are not required and entryHeader plus entryNumber suffice).
- **Tax. "XML väljade info" A98:** "Kui aruandeperioodil andmed puudusid… tuleb esitada vaid tühi päis (gl-cor:entryHeader + gl-cor:entryNumber)" (submit only an empty header).

```xml
<gl-cor:entryHeader>
    <gl-cor:entryNumber contextRef="now">EE0201002</gl-cor:entryNumber>
</gl-cor:entryHeader>
```

The files do **not** say whether a file is required at all for a month with no payments. The confirmation message (§F.4) carries `hasData=false` per section.

---

## C. `entryDetail` = one payment

The Guide defines a payment (*väljamakse*) as a transaction made on **one date** with **one payment type**. Each separately reported money amount gets its own `entryDetail`. Elements must appear in the order below; the Guide says "Elementide esitusjärjekord peab vastama alltoodud struktuurile", and the XSD enforces it as a sequence.

### C.1 Children in schema order

| # | Element | Guide | XSD | Type and attributes | Meaning and rules |
|---|---|---|---|---|---|
| 1 | `gl-cor:lineNumber` | K 1 | 1 | string; `contextRef` | Payment ID defined by the reporter. It must be unique per **taxpayer + tax period + section**, across all files ever sent (KR_XBRL_VALJAMAKSED_2/_4). It is the key for M/T and comes back in feedback as `lineInternalId`. Format is free; Ex-XML uses `Tehing_2111`. **Conflict:** Tax. "Üldised andmeväljad"/"XML väljade info" call it optional for periodic sections, while the Guide, the TSD part of "XML väljade info" (Jah) and rule _1 (BLACK) make it mandatory. |
| 2 | `gl-cor:lineNumberCounter` | K 1 | 1 | decimal; `contextRef`, `unitRef` → the `pure` unit (`NotUsed`); the examples have no `decimals` | Sequence number within the section ("Järjekorranumber ühe andmesektsiooni sees"). Ex-XML restarts at 1 in each section. Feedback references it. |
| 3 | `gl-cor:account` | K 1 | 1 | — | Contains **only** `accountSub` 1..n. The APA XSD has no `accountMainID`. |
| 3a | `gl-cor:accountSub` | K 1..n | 1..n | — | One classifier value each; see C.2. At least one is needed, because TOIMINGUTYYP is always present. |
| 3a.i | `gl-cor:accountSubID` | K 1 | 1 | string; `contextRef` | Classifier element code. For a compound classifier, this holds the main code (the country). |
| 3a.ii | `gl-cor:accountSubType` | K 1 | 1 | string; `contextRef` | Classifier designation, e.g. `VALJAMLIIK2023ap`. |
| 3a.iii | `gl-cor:segmentParentTuple` | T 0..1 | 0..1 | — | Only for compound classifiers (RTK2T2013ap, A1_RIIGID_enum, TL2015ap). |
| — `parentSubaccountCode` | | K 1 | 1 | string; `contextRef` | The role (RIIGIROLL2022ap code), or the ship IMO number for TL2015ap. |
| — `parentSubaccountType` | | T 0..1 | 0..1 | string; `contextRef` | `RIIGIROLL2022ap`. Used only when parentSubaccountCode is a RIIGIROLL2022ap value; omitted for TL2015ap. |
| 4 | `gl-cor:amount` | T 0..1 | 0..1 | decimal; `contextRef`, `unitRef="eur"`, `decimals="2"` | Payment amount, **gross** (Tax. Andmekoosseis: "Palgataksonoomia puhul märgitakse brutosumma"), "kahe komakoha täpsusega" (to two decimals). Mandatory in EE0201001/EE0201002; **"Kui väljamakset pole siis 0"** (0 if there is no payment). Omitted for T. Not allowed in EE0201003 (KR_YLD_09). Negative only for refunds (C.5). |
| 5 | `gl-bus:allocationCode` | V 0..1 | 0..1 | string; `contextRef` | Access-right code (*pääsuõiguse kood*), used when object-level access rights are set on the data. Ex-XML uses `TartuOsakond`. No business rule references it. |
| 6 | `gl-cor:identifierReference` | T 0..1 | 0..1 | — | Recipient. "Pole vajalik kui toimingu tüüp on tühistamine" (not needed for T). The XSD allows **only** the two children below. Contact names, birth date and `identifierCategory` are SA/TÖR-only and fail the XSD. |
| 6a | `gl-cor:identifierCode` | K 1 | 1 | string; `contextRef` | See C.3. |
| 6b | `gl-cor:identifierDescription` | K 1 | 1 | string; `contextRef` | IDENTIFIERDESCRIPTION_enum; see C.3. |
| 7 | `gl-cor:documentDate` | T 0..1 | 0..1 | date; `contextRef` | **Payment date** (*väljamakse teostamise kuupäev*), `aaaa-kk-pp`. Mandatory in EE0201001/EE0201002 (KR_YLD_11). Must fall inside periodCoveredStart..End (KR_XBRL_VALJAMAKSED_17, BLACK). |
| 8 | `gl-cor:confirmedDate` | — | 0..1 | date | The XSD allows it here, but the Guide, Tax. and the examples never mention or use it. Do not emit it. |
| 9 | `gl-bus:measurable` | T 0..n | 0..n | — | "Vajalikkus on tingitud esitatud väljamakse liigist" (need depends on the payment type). See C.4. |
| 9a | `gl-bus:measurableID` | T 0..1 | 0..1 | string; `contextRef` | SYNDMUS2017ap event code |
| 9b | `gl-bus:measurableIDSchema` | T 0..1 | 0..1 | string; `contextRef` | `SYNDMUS2017ap` whenever measurableID is used |
| 9c | `gl-bus:measurableDescription` | T 0..1 | 0..1 | string; `contextRef` | MEASURABLEDESCRIPTION_enum code |
| 9d | `gl-bus:measurableQuantity` | T 0..1 | 0..1 | decimal; `contextRef`, `decimals="2"`, `unitRef`: currency unit (`eur`) for money, else `pure` (also for a percentage such as 0.22) | value of 9c |
| 9e | `gl-bus:measurableStartDateTime` | T 0..1 | 0..1 | **date** (xbrli:dateItemType, despite the name); `contextRef` | Always together with SYNDMUS2017ap |
| 9f | `gl-bus:measurableEndDateTime` | T 0..1 | 0..1 | date; `contextRef` | Always together with SYNDMUS2017ap (ESM01 only in practice) |
| 10 | `gl-cor:taxes` | T 0..n | 0..n | — | One per tax type ("Iga maksuliik tuleb esitada eraldi"). See C.5. |
| 10a | `gl-cor:taxAmount` | K 1 | 1 | decimal; `contextRef`, `decimals="2"`, `unitRef="eur"` | Calculated or withheld tax amount |
| 10b | `gl-cor:taxPercentageRate` | T 0..1 | 0..1 | decimal; `contextRef`, `decimals="2"`, `unitRef` → `pure` | Rate as a fraction (`0.22`). Required for income tax withheld from **non-resident** payments (`taxCode`=`TSD_KTM`). |
| 10c | `gl-cor:taxCode` | K 1 | 1 | string; `contextRef` | TAXCODE_enum. **Comes last, after the amount.** |

**decimals.** In the XSD, `amount`, `taxAmount`, `taxPercentageRate` and `measurableQuantity` use APA types (`apatsdMonetaryItemType`, `apatsdPureItemType`, `apatsdDecimalItemType`) where **`decimals` is required and fixed to "2"** and **`precision` is prohibited**. `lineNumberCounter` uses a plain `decimalItemType`, so decimals is optional, and the examples omit it.

**Not allowed.** Elements that Tax. "XML väljade info" lists generically but that the APA XSD does **not** permit in `entryDetail`:
- `accountMainID`, `accountMainDescription`
- `debitCreditCode`
- `documentNumber`, `documentApplyToNumber`
- `detailComment`
- `identifierCategory`, `identifierContact*`
- `measurableUnitOfMeasure`, `measurableQualifier`
- `taxBasis`, `taxDescription` and every other `tax*` element

They belong to other sections (SA, TÖR, KMD). A file containing them fails KR_VALIDATE_XBRL_FILE_3 (BLACK).

### C.2 `accountSub` usage

| `accountSubType` | `accountSubID` | `segmentParentTuple` | When |
|---|---|---|---|
| `TOIMINGUTYYP2023ap` | `R` = Uus toiming (new); `M` = Olemasoleva toimingu muutmine (change); `T` = Olemasoleva toimingu tühistamine (cancel) | none | **Always**, on every payment (KR_XBRL_VALJAMAKSED_3, BLACK). Formerly `KANDETYYP2023ap` (Uuendused). |
| `VALJAMLIIK2023ap` | payment-type code. Ex-XML uses: `10` Palgatulu residendile, `24` Tööandja makstud haigushüvitis residendist töötajale, `120` Palgatulu mitteresidendile, `70` Laevapere liikme töötasu residendile, `50` Renditulu ning litsentsitasu … residendile | none | Required when TOIMINGUTYYP is R or M; omitted for T (KR_XBRL_VALJAMAKSED_7). The code list and the per-type tax matrix are in VALJAMLIIK2023ap and the "väljamakseliikide maatrikstabel", which are **not covered here**. |
| `RTK2T2013ap` | ISO country code (`FI`, `LV`, `EE`) | `parentSubaccountCode` = `RR_TOOKOHA_ASUKOHT` / `RR_LAEVAPERE_RIIK` / `RR_MAKSURESIDENTSUSE_RIIK`; `parentSubaccountType` = `RIIGIROLL2022ap` | "Vajalik esitada teatud VM liikide puhul" (needed for certain payment types). Tax. Andmekoosseis lists the possible roles: EE0201001 allows RR_A1, RR_LAEVAPERE_RIIK, RR_MAKSURESIDENTSUSE_RIIK and RR_TOOKOHA_ASUKOHT; EE0201002 allows only RR_MAKSURESIDENTSUSE_RIIK. |
| `A1_RIIGID_enum` | A1-issuing country (35 codes: AT AU BE BG BY CA CH CZ CY DE DK EE **EL** ES FI FR HR HU IE IS IT LI LT LU LV MT NL NO PL PT RO SE SI SK UA; AU, BY, CA and UA are marked "Sotsiaalkindlustusleping"). Greece is `EL`. | `parentSubaccountCode` = `RR_A1`; `parentSubaccountType` = `RIIGIROLL2022ap` | A1 / E101 certificate cases. Rules KR_YLD2_01..09 apply. **Conflict:** Tax. Juhend E54 says "Antud loendit eraldiseisvalt ei esitata" (this list is not submitted separately), yet the Guide and Ex-XML use it as `accountSubType`. Follow the Guide and Ex-XML. |
| `TL2015ap` | `1` (Meretransport) | `parentSubaccountCode` = ship **IMO number** (e.g. `9083043`); **no** `parentSubaccountType` | Seafarer special regime and tonnage payment types only (KR_LAEV_01/02) |

**RIIGIROLL2022ap codes relevant to TSD:**
- `RR_TOOKOHA_ASUKOHT`: country of the place of work.
- `RR_LAEVAPERE_RIIK`: crew member's citizenship, residence or social-security-agreement country.
- `RR_A1`: country that issued the A1 certificate, or the social-security-agreement country.
- `RR_MAKSURESIDENTSUSE_RIIK`: country of tax residence.

Use them **upper-case as in the classifier**. Prose in Tax. and Uuendused writes `RR_tookoha_asukoht` and `RR_maksuresidentsuse_riik`, but the classifier and Ex-XML use upper case.

**Compound-classifier change (Uuendused, applies from 01.01.2026).** The old comma form `<accountSubID>LV,RR_tookoha_asukoht</accountSubID><accountSubType>RTK2T2013ap,RIIGIROLL2022ap</accountSubType>` is replaced by `segmentParentTuple`. Tax. "Üldised andmeväljad" A30 still describes the comma form, so that text is stale.

**Order of accountSub entries.** The XSD repeats `accountSub` with no ordering among them. Ex-XML always writes VALJAMLIIK, then TOIMINGUTYYP, then A1, then RTK, then TL.

**A1 combinations.** These come from rules KR_YLD2_06..09. Which combinations are valid also depends on the payment type (KR_YLD2_02/03).
- An A1 country requires a work-location country (RED).
- A foreign work location requires an A1 country (RED), and that A1 country must be `EE` (BLUE).
- A1 = `EE` requires that the work location is not `EE` (RED).

Ex-XML has two valid patterns:
- *EE A1 + work in FI* (Tehing_21114). This keeps SM/KP/TK/TTK and has no KTM.
- *LV A1 + work in EE* (Tehing_21115). This has only KTM, with MVT_610.

### C.3 `identifierReference`

**identifierCode.** From the Guide and Tax. Andmekoosseis:
- For a natural person, use the personal code **prefixed with the two-letter country code** ("Isikukoodi ette on kohustuslik märkida kahekohaline riigi tähtkood (nt EE, LT, LV)"). Example: `EE48712219406`.
- If the person has no personal code, use the **MTA non-resident code without a country prefix**. Example: `A0003689779` with `MRR_KOOD`.
- A person without an Estonian code who works in Estonia for up to five days needs an MRR code, which the employer requests from MTA.
- For a legal person, use the Äriregister code, or the MRR code if it has none. The files do **not** say whether a registry code takes a country prefix, and no TSD example has a legal-person recipient. The generic Tax. example pairs `EE34801010100` (which looks like a personal code) with `ARIREGISTRIKOOD`, which is inconsistent.

**Server-side checks:**
- Structure: KR_XBRL_VALJAMAKSED_10 for "riigi kood ja isikukood" and _11 for the registry code, both BLACK. The exact pattern is not given.
- The code must exist in the MTA person register (KR_IH_1, BLUE).
- Using an MRR code for someone who has an Estonian personal code gives a warning (KR_ISIK_5).

**identifierDescription (IDENTIFIERDESCRIPTION_enum).** For EE0201001/02/03, each value is marked "Jah*", meaning "submit when the counterparty is of that type":

| Value | Meaning | Sections |
|---|---|---|
| `ISIKUKOOD` | Isikukood | 01, 02, 03 |
| `MRR_KOOD` | MTA mitteresidendi registri- või isikukood | 01, 02, 03 |
| `ARIREGISTRIKOOD` | Äriregistrikood | 01, 02 (not 03) |
| `KMKR_NUMBER` | VAT number | EE0203001 (KMD) only — not TSD |

### C.4 `measurable`

There are two kinds of measurable block. Each block holds one item.

**(a) Event or date blocks.** These use `measurableID` + `measurableIDSchema`=`SYNDMUS2017ap` + `measurableStartDateTime` (+ `measurableEndDateTime`).

| SYNDMUS2017ap | Meaning | TSD use |
|---|---|---|
| `TSD01` | Arvestuse kuupäev: the date the payment obligation and its taxes were calculated, used so that the right liability applies when residency changes | The SYNDMUS sheet marks it **"Jah"** (mandatory) for EE0201001 and EE0201002. Tax. Andmekoosseis H18/I18 instead says SYNDMUS is "tingimuslik, seotud väljamakse tagastusega" (conditional, refund-related). **Conflict.** Every example payment carries TSD01, and KR_YLD_25 requires TSD01 ≤ documentDate (BLUE). The example date is in the **previous** month (`2026-08-31` for a September file). |
| `TSD02` | Tagasimakse või tagastuse aluseks oleva väljamakse kuupäev: the date of the original payment that a refund relates to | Only on refunds of a payment received in an earlier period. Required then (KR_YLD_14), and it turns the payment into a refund (C.5 sign rule). Not allowed in EE0201003. Before 09.2025 this role was TSD01 (Uuendused). |
| `ESM01` | Special-case social tax liability start/end | EE0201003 only. Forbidden in 01/02 (KR_YLD_13). |
| `TOR01`, `TOR02` | Employment start / suspension | TÖR (EE0202001) and SA only — not TSD |
| `Algse_arve_kp` | Original invoice date | KMD only |

**(b) Amount blocks.** These use `measurableDescription` + `measurableQuantity unitRef="eur" decimals="2"`, with **no dates** (KR_YLD_22). Each description type may appear at most once per payment (KR_XBRL_VALJAMAKSED_18, BLACK). Every measurable must carry either an event type or a description type (_16, BLACK).

**MEASURABLEDESCRIPTION_enum** (all 17 values; "01" = EE0201001, "02" = EE0201002):

| Code | Meaning | Sections | When a company needs it |
|---|---|---|---|
| `TSD_SMVM` | Sotsiaalmaksuga maksustatav väljamakse (amount subject to social tax). For a seafarer, the gross amount within the SMS §2(4)p8 cap. | 01, 02 cond. | Every payment type subject to SM (KR_SM_01_1, RED). Must **equal `amount`** except for seafarer types (KR_SM_01_2). Forbidden on types without SM (KR_SM_01). |
| `TSD_OPTV_SM_VAHENDUS` | Part deducted from the SM base for a person with partial/no work capacity, on which Töötukassa pays SM | 01, 02 cond. | Only if Töötukassa declared the person (EE0201003, type 615) in the same or previous period (KR_SM_03). It must be ≤ TSD_SMVM and ≤ 2 × SM monthly rate, and cumulative per period ≤ 2 × SM monthly rate (KR_SM_05/06/07/10, KOOND_01). |
| `TSD_KUUMSUURENDUS` | Increase of the SM base for the **minimum monthly social tax obligation** | 01, 02 cond. | E.g. a board member or employee with no or low pay in the month. Ex-XML Tehing_21117 has amount 0 + TSD_KUUMSUURENDUS 820 → TSD_SM 270.60. It must be ≤ 2 × SM monthly rate, including cumulatively (KR_SM_11/12, KOOND_03). |
| `TSD_KUUMVAHENDUS` | Part deducted from the SM base on which SM was already declared in earlier months at the monthly rate | 01, 02 cond. | ≤ TSD_SMVM; cumulative ≤ 2 × monthly rate (KR_SM_08/09/10, KOOND_02) |
| `TSD_SMVMSK` | Decrease of a refunded or set-off SM base, so that the minimum obligation is kept | 01, 02 cond. | **Refunds (TSD02) only** (KR_SM_13), and only for types with a monthly minimum (KR_SM_14) |
| `MVT_610` | Üldine maksuvaba tulu (basic exemption), for a person **not** in old-age pension age | 01, 02 cond. | When the employee has applied for basic exemption at this payer. Not allowed from 1 Jan of the year the person reaches pension age (KR_MVT_04). Only for EEA residents holding a valid residency certificate (KR_MVT_06/07). Must be ≤ amount − TSD_KP − TSD_TK (KR_MVT_08). Monthly cap per payer is RED (KOOND_02); across payers it is a warning (KOOND_04). Ex-XML even gives it to the LV non-resident (Tehing_21116). |
| `MVT_640` | III samba sissemakse maksuvaba osa (tax-free part of 3rd-pillar contribution, incl. PEPP) | 01, 02 cond. | ≤ 15 % of income-taxed payments in the calendar year (KR_KOOND_01) and ≤ 6000 € per year (KR_KOOND_06) |
| `MVT_650` | Maksuvaba tulu vanaduspensioniealistele (tax-free income for those reaching pension age **in the current calendar year**) | 01, 02 cond. | Instead of MVT_610 for pension-age persons (KR_MVT_05). Ex-XML: Tehing_21113 (salary, 776) and Tehing_31111 (rent, 50). |
| `TOOAJAMAAR` | Tööajamäär (contractual working-time ratio 1, 0.5 …) | SA EE0101001 (and TÖR) | **Not TSD.** TSD has no working-time element. |
| `MOODETAV_AEG` | Mõõdetav aeg (hours or days) | SA EE0101001 | Not TSD |
| `VABAD_AMETIKOHAD` | Vacancies | SA EE0102001 | Not TSD |
| `ARVE_KOGUSUMMA` | Invoice total | KMD EE0203001 | Not TSD |
| `LASTE_ARV`, `MAJUTATUTE_ARV`, `OOBIMISTE_ARV`, `LAHTIOLEKUPAEVADE_ARV`, `TUBAOOPAEVADE_ARV` | Accommodation statistics | SA/PPA accommodation | Not TSD |

**Situations from the task brief:**
- **Tax-free income:** MVT_610, MVT_650 or MVT_640.
- **A1 certificate:** not a measurable. It goes in `accountSub` A1_RIIGID_enum + RR_A1 (C.2).
- **Work time:** not reported in TSD.
- **Pension:**
  - the II pillar withheld contribution is the **tax** `TSD_KP`;
  - the III pillar is `MVT_640`;
  - pension-age tax-free income is `MVT_650`;
  - a person on old-age or flexible pension must have no `TSD_TK` (KR_TK_2).

### C.5 `taxes` (TAXCODE_enum)

| Code | Name (ET / EN) | Kind | Sections (enum matrix) |
|---|---|---|---|
| `TSD_SM` | Sotsiaalmaks / Social tax | employer-side, calculated (*arvestatud*) — by the name; the enum does not say "kinnipeetud" | 01, 02: "Jah* tingimuslik — teatud väljamakseliikide puhul"; 03: Jah |
| `TSD_KP` | Kinnipeetud kohustusliku kogumispensioni makse / mandatory funded pension contribution withheld | withheld (*kinnipeetud*) | 01, 02 conditional; 03 only for the additional contribution |
| `TSD_TK` | Kinnipeetud töötuskindlustusmakse / unemployment insurance premium withheld | withheld | **01 only** |
| `TSD_TTK` | Tööandja töötuskindlustusmakse / employer's unemployment insurance premium | employer-side, calculated | **01 only** |
| `TSD_KTM` | Kinnipeetud tulumaks / income tax withheld | withheld | 01, 02 conditional |

**Element rules:**
- Each tax code appears at most once per payment (KR_XBRL_VALJAMAKSED_19, BLACK).
- The code must be present and be in the enum (KR_YLD_35/36, BLUE).
- `taxPercentageRate` must equal the rate the system determines (KR_TM_3, RED).
- If the applicable income-tax rate is 0 %, MVT is not needed (KR_MVT_09, WARN).
- The XSD allows **only** `taxAmount`, `taxPercentageRate` and `taxCode`. `taxBasis`, `taxDescription` and the rest are not allowed; the tax base is conveyed by the measurables (TSD_SMVM, MVT_*).

**Server cross-check formulas.** These are taken from the rule message texts. All apply "lubatud hälbe piires" (within an allowed tolerance), and the tolerance is **not given**.
- **TSD_SM** = (TSD_SMVM − TSD_OPTV_SM_VAHENDUS − TSD_KUUMVAHENDUS + TSD_KUUMSUURENDUS − TSD_SMVMSK) × SM rate (KR_SM_04)
- **TSD_TK** = amount × TK rate (KR_TK_3)
- **TSD_TTK** = amount × TTK rate (KR_TTK_2)
- **TSD_KP** = TSD_SMVM × the person's rate (KR_KOP_03)
- **TSD_KTM** = (amount − TSD_KP − TSD_TK − MVT) × applicable income-tax rate (KR_TM_4)

The files state no rates except the 0.22 sample for `taxPercentageRate`. The arithmetic of Ex-XML matches SM 33 %, TK 1.6 %, TTK 0.8 %, KP 2 % and income tax 22 %. For example, Tehing_2111 gives (1000 − 20 − 16 − 700) × 0.22 = 58.08. Tehing_21118 (seafarer) does not follow the general formulas: SM 150 and TK 12 are computed on 750, not on the amount.

**Sign and zero rules:**
- Outside refunds, all amounts must be 0 or positive (KR_YLD_30, BLUE).
- For a refund (TSD02), all amounts must be 0 or **negative**: amount, measurables and taxes (KR_YLD_24, BLUE).
- Amount 0 is refused for payment types that don't allow it (KR_YLD2_05, BLUE).
- Ex-XML writes an explicit `TSD_KTM` of `0` for the seafarer, and omits taxes that don't apply elsewhere. The files say nothing general about zero-valued tax lines.

### C.6 Example payments (Ex-XML)

All ten example payments:

| lineNumber | Section | Type | Situation (Ex-XLSX col. D) | Recipient | amount | Measurables | Taxes | Extra accountSub |
|---|---|---|---|---|---|---|---|---|
| Tehing_2111 | 01 | 10 | salary to resident | ISIKUKOOD | 1000 | TSD01, MVT_610 700, SMVM 1000 | SM 330, KP 20, TK 16, TTK 8, KTM 58.08 | — |
| Tehing_2112 | 01 | 24 | employer-paid sick pay to resident | ISIKUKOOD | 100 | TSD01 | KTM 22 | — |
| Tehing_21113 | 01 | 10 | salary to pension-age resident | ISIKUKOOD | 900 | TSD01 (2026-08-03), MVT_650 776, SMVM 900 | SM 297, TTK 7.2, KTM 27.28 (no TK, no KP) | — |
| Tehing_21114 | 01 | 10 | resident, Estonian A1, works abroad ("endine vml 11") | ISIKUKOOD | 1000 | TSD01, SMVM 1000 | SM 330, KP 20, TK 16, TTK 8 (no KTM) | A1 EE/RR_A1, RTK FI/RR_TOOKOHA_ASUKOHT |
| Tehing_21115 | 01 | 10 | resident, foreign A1, works in Estonia ("endine vml 12") | ISIKUKOOD | 1000 | TSD01, MVT_610 700 (no SMVM) | KTM 66 | A1 LV/RR_A1, RTK EE/RR_TOOKOHA_ASUKOHT |
| Tehing_21116 | 01 | 120 | salary to non-resident | MRR_KOOD | 1000 | TSD01, MVT_610 700, SMVM 1000 | SM 330, TK 16, TTK 8, KTM 62.48 @0.22 | RTK LV/RR_MAKSURESIDENTSUSE_RIIK |
| Tehing_21117 | 01 | 10 | no payment; minimum-SM obligation only | ISIKUKOOD | **0** | TSD01, TSD_KUUMSUURENDUS 820 | SM 270.60 | — |
| Tehing_21118 | 01 | 70 | seafarer special regime | ISIKUKOOD | 1000 | TSD01, SMVM 750 | SM 150, KP 15, TK 12, TTK 6, KTM **0** | RTK EE/RR_LAEVAPERE_RIIK, TL 1/IMO 9083043 |
| Tehing_11111 | 01 | 10 | refund of an earlier-period salary | ISIKUKOOD | **−100** | TSD01 2026-08-25, **TSD02 2026-07-05**, SMVM −100 | SM −33, KP −2, TK −1.6, TTK −0.8, KTM −21.21 | (no allocationCode) |
| Tehing_31111 | **02** (counter 1) | 50 | rent to pension-age resident | ISIKUKOOD | 100 | TSD01, MVT_650 50 | KTM 11 | — |

No example covers a **board-member fee**, a **dividend** or a legal-person recipient. The section for board-member fees is EE0201001 (B.1). Their payment-type codes are in VALJAMLIIK2023ap, which is outside this file.

#### C.6.1 Ordinary salary with all five taxes and a tax-free measurable (verbatim, Ex-XML)

```xml
            <gl-cor:entryDetail>
                <gl-cor:lineNumber contextRef="now">Tehing_2111</gl-cor:lineNumber>
                <gl-cor:lineNumberCounter contextRef="now" unitRef="NotUsed">1</gl-cor:lineNumberCounter>
                <gl-cor:account>
                    <gl-cor:accountSub>
                        <gl-cor:accountSubID contextRef="now">10</gl-cor:accountSubID>
                        <gl-cor:accountSubType contextRef="now">VALJAMLIIK2023ap</gl-cor:accountSubType>
                    </gl-cor:accountSub>
                    <gl-cor:accountSub>
                        <gl-cor:accountSubID contextRef="now">R</gl-cor:accountSubID>
                        <gl-cor:accountSubType contextRef="now">TOIMINGUTYYP2023ap</gl-cor:accountSubType>
                    </gl-cor:accountSub>
                </gl-cor:account>
                <gl-cor:amount contextRef="now" decimals="2" unitRef="eur">1000</gl-cor:amount>
                <gl-bus:allocationCode contextRef="now">TartuOsakond</gl-bus:allocationCode>
                <gl-cor:identifierReference>
                    <gl-cor:identifierCode contextRef="now">EE48712219406</gl-cor:identifierCode>
                    <gl-cor:identifierDescription contextRef="now">ISIKUKOOD</gl-cor:identifierDescription>
                </gl-cor:identifierReference>
                <gl-cor:documentDate contextRef="now">2026-09-10</gl-cor:documentDate>
                <gl-bus:measurable>
                    <gl-bus:measurableID contextRef="now">TSD01</gl-bus:measurableID>
                    <gl-bus:measurableIDSchema contextRef="now">SYNDMUS2017ap</gl-bus:measurableIDSchema>
                    <gl-bus:measurableStartDateTime contextRef="now">2026-08-31</gl-bus:measurableStartDateTime>
                </gl-bus:measurable>
                <gl-bus:measurable>
                    <gl-bus:measurableDescription contextRef="now">MVT_610</gl-bus:measurableDescription>
                    <gl-bus:measurableQuantity contextRef="now" decimals="2" unitRef="eur">700</gl-bus:measurableQuantity>
                </gl-bus:measurable>
                <gl-bus:measurable>
                    <gl-bus:measurableDescription contextRef="now">TSD_SMVM</gl-bus:measurableDescription>
                    <gl-bus:measurableQuantity contextRef="now" decimals="2" unitRef="eur">1000</gl-bus:measurableQuantity>
                </gl-bus:measurable>
                <gl-cor:taxes>
                    <gl-cor:taxAmount contextRef="now" decimals="2" unitRef="eur">330</gl-cor:taxAmount>
                    <gl-cor:taxCode contextRef="now">TSD_SM</gl-cor:taxCode>
                </gl-cor:taxes>
                <gl-cor:taxes>
                    <gl-cor:taxAmount contextRef="now" decimals="2" unitRef="eur">20</gl-cor:taxAmount>
                    <gl-cor:taxCode contextRef="now">TSD_KP</gl-cor:taxCode>
                </gl-cor:taxes>
                <gl-cor:taxes>
                    <gl-cor:taxAmount contextRef="now" decimals="2" unitRef="eur">16</gl-cor:taxAmount>
                    <gl-cor:taxCode contextRef="now">TSD_TK</gl-cor:taxCode>
                </gl-cor:taxes>
                <gl-cor:taxes>
                    <gl-cor:taxAmount contextRef="now" decimals="2" unitRef="eur">8</gl-cor:taxAmount>
                    <gl-cor:taxCode contextRef="now">TSD_TTK</gl-cor:taxCode>
                </gl-cor:taxes>
                <gl-cor:taxes>
                    <gl-cor:taxAmount contextRef="now" decimals="2" unitRef="eur">58.08</gl-cor:taxAmount>
                    <gl-cor:taxCode contextRef="now">TSD_KTM</gl-cor:taxCode>
                </gl-cor:taxes>
            </gl-cor:entryDetail>
```

#### C.6.2 Non-resident salary: MRR code, residence-country compound classifier, withheld-tax rate (verbatim)

```xml
            <gl-cor:entryDetail>
                <gl-cor:lineNumber contextRef="now">Tehing_21116</gl-cor:lineNumber>
                <gl-cor:lineNumberCounter contextRef="now" unitRef="NotUsed">6</gl-cor:lineNumberCounter>
                <gl-cor:account>
                    <gl-cor:accountSub>
                        <gl-cor:accountSubID contextRef="now">120</gl-cor:accountSubID>
                        <gl-cor:accountSubType contextRef="now">VALJAMLIIK2023ap</gl-cor:accountSubType>
                    </gl-cor:accountSub>
                    <gl-cor:accountSub>
                        <gl-cor:accountSubID contextRef="now">R</gl-cor:accountSubID>
                        <gl-cor:accountSubType contextRef="now">TOIMINGUTYYP2023ap</gl-cor:accountSubType>
                    </gl-cor:accountSub>
                    <gl-cor:accountSub>
                        <gl-cor:accountSubID contextRef="now">LV</gl-cor:accountSubID>
                        <gl-cor:accountSubType contextRef="now">RTK2T2013ap</gl-cor:accountSubType>
                        <gl-cor:segmentParentTuple>
                            <gl-cor:parentSubaccountCode contextRef="now">RR_MAKSURESIDENTSUSE_RIIK</gl-cor:parentSubaccountCode>
                            <gl-cor:parentSubaccountType contextRef="now">RIIGIROLL2022ap</gl-cor:parentSubaccountType>
                        </gl-cor:segmentParentTuple>
                    </gl-cor:accountSub>
                </gl-cor:account>
                <gl-cor:amount contextRef="now" decimals="2" unitRef="eur">1000</gl-cor:amount>
                <gl-bus:allocationCode contextRef="now">TartuOsakond</gl-bus:allocationCode>
                <gl-cor:identifierReference>
                    <gl-cor:identifierCode contextRef="now">A0003689779</gl-cor:identifierCode>
                    <gl-cor:identifierDescription contextRef="now">MRR_KOOD</gl-cor:identifierDescription>
                </gl-cor:identifierReference>
                <gl-cor:documentDate contextRef="now">2026-09-10</gl-cor:documentDate>
                <gl-bus:measurable>
                    <gl-bus:measurableID contextRef="now">TSD01</gl-bus:measurableID>
                    <gl-bus:measurableIDSchema contextRef="now">SYNDMUS2017ap</gl-bus:measurableIDSchema>
                    <gl-bus:measurableStartDateTime contextRef="now">2026-08-31</gl-bus:measurableStartDateTime>
                </gl-bus:measurable>
                <gl-bus:measurable>
                    <gl-bus:measurableDescription contextRef="now">MVT_610</gl-bus:measurableDescription>
                    <gl-bus:measurableQuantity contextRef="now" decimals="2" unitRef="eur">700</gl-bus:measurableQuantity>
                </gl-bus:measurable>
                <gl-bus:measurable>
                    <gl-bus:measurableDescription contextRef="now">TSD_SMVM</gl-bus:measurableDescription>
                    <gl-bus:measurableQuantity contextRef="now" decimals="2" unitRef="eur">1000</gl-bus:measurableQuantity>
                </gl-bus:measurable>
                <gl-cor:taxes>
                    <gl-cor:taxAmount contextRef="now" decimals="2" unitRef="eur">330</gl-cor:taxAmount>
                    <gl-cor:taxCode contextRef="now">TSD_SM</gl-cor:taxCode>
                </gl-cor:taxes>
                <gl-cor:taxes>
                    <gl-cor:taxAmount contextRef="now" decimals="2" unitRef="eur">16</gl-cor:taxAmount>
                    <gl-cor:taxCode contextRef="now">TSD_TK</gl-cor:taxCode>
                </gl-cor:taxes>
                <gl-cor:taxes>
                    <gl-cor:taxAmount contextRef="now" decimals="2" unitRef="eur">8</gl-cor:taxAmount>
                    <gl-cor:taxCode contextRef="now">TSD_TTK</gl-cor:taxCode>
                </gl-cor:taxes>
                <gl-cor:taxes>
                    <gl-cor:taxAmount contextRef="now" decimals="2" unitRef="eur">62.48</gl-cor:taxAmount>
                    <gl-cor:taxPercentageRate contextRef="now" decimals="2" unitRef="NotUsed">0.22</gl-cor:taxPercentageRate>
                    <gl-cor:taxCode contextRef="now">TSD_KTM</gl-cor:taxCode>
                </gl-cor:taxes>
            </gl-cor:entryDetail>
```

#### C.6.3 A1 certificate, Estonian A1 + work in Finland (verbatim, `account` block only; the rest is like C.6.1 without KTM)

```xml
                <gl-cor:account>
                    <gl-cor:accountSub>
                        <gl-cor:accountSubID contextRef="now">10</gl-cor:accountSubID>
                        <gl-cor:accountSubType contextRef="now">VALJAMLIIK2023ap</gl-cor:accountSubType>
                    </gl-cor:accountSub>
                    <gl-cor:accountSub>
                        <gl-cor:accountSubID contextRef="now">R</gl-cor:accountSubID>
                        <gl-cor:accountSubType contextRef="now">TOIMINGUTYYP2023ap</gl-cor:accountSubType>
                    </gl-cor:accountSub>
                    <gl-cor:accountSub>
                        <gl-cor:accountSubID contextRef="now">EE</gl-cor:accountSubID>
                        <gl-cor:accountSubType contextRef="now">A1_RIIGID_enum</gl-cor:accountSubType>
                        <gl-cor:segmentParentTuple>
                            <gl-cor:parentSubaccountCode contextRef="now">RR_A1</gl-cor:parentSubaccountCode>
                            <gl-cor:parentSubaccountType contextRef="now">RIIGIROLL2022ap</gl-cor:parentSubaccountType>
                        </gl-cor:segmentParentTuple>
                    </gl-cor:accountSub>
                    <gl-cor:accountSub>
                        <gl-cor:accountSubID contextRef="now">FI</gl-cor:accountSubID>
                        <gl-cor:accountSubType contextRef="now">RTK2T2013ap</gl-cor:accountSubType>
                        <gl-cor:segmentParentTuple>
                            <gl-cor:parentSubaccountCode contextRef="now">RR_TOOKOHA_ASUKOHT</gl-cor:parentSubaccountCode>
                            <gl-cor:parentSubaccountType contextRef="now">RIIGIROLL2022ap</gl-cor:parentSubaccountType>
                        </gl-cor:segmentParentTuple>
                    </gl-cor:accountSub>
                </gl-cor:account>
```

**Bug in Ex-XML.** Tehing_21115 (LV A1) writes `<gl-cor:parentSubaccountType contextRef="now">RR_A1</gl-cor:parentSubaccountType>`. Its source row in Ex-XLSX (cell C124, "Riigi rolli klassifikaator", element `gl-cor:parentSubaccountType`) says `RIIGIROLL2022ap`, which is correct.

#### C.6.4 Refund of an earlier-period payment (verbatim)

```xml
            <gl-cor:entryDetail>
                <gl-cor:lineNumber contextRef="now">Tehing_11111</gl-cor:lineNumber>
                <gl-cor:lineNumberCounter contextRef="now" unitRef="NotUsed">9</gl-cor:lineNumberCounter>
                <gl-cor:account>
                    <gl-cor:accountSub>
                        <gl-cor:accountSubID contextRef="now">10</gl-cor:accountSubID>
                        <gl-cor:accountSubType contextRef="now">VALJAMLIIK2023ap</gl-cor:accountSubType>
                    </gl-cor:accountSub>
                    <gl-cor:accountSub>
                        <gl-cor:accountSubID contextRef="now">R</gl-cor:accountSubID>
                        <gl-cor:accountSubType contextRef="now">TOIMINGUTYYP2023ap</gl-cor:accountSubType>
                    </gl-cor:accountSub>
                </gl-cor:account>
                <gl-cor:amount contextRef="now" decimals="2" unitRef="eur">-100</gl-cor:amount>
                <gl-cor:identifierReference>
                    <gl-cor:identifierCode contextRef="now">EE36609159911</gl-cor:identifierCode>
                    <gl-cor:identifierDescription contextRef="now">ISIKUKOOD</gl-cor:identifierDescription>
                </gl-cor:identifierReference>
                <gl-cor:documentDate contextRef="now">2026-09-05</gl-cor:documentDate>
                <gl-bus:measurable>
                    <gl-bus:measurableID contextRef="now">TSD01</gl-bus:measurableID>
                    <gl-bus:measurableIDSchema contextRef="now">SYNDMUS2017ap</gl-bus:measurableIDSchema>
                    <gl-bus:measurableStartDateTime contextRef="now">2026-08-25</gl-bus:measurableStartDateTime>
                </gl-bus:measurable>
                <gl-bus:measurable>
                    <gl-bus:measurableID contextRef="now">TSD02</gl-bus:measurableID>
                    <gl-bus:measurableIDSchema contextRef="now">SYNDMUS2017ap</gl-bus:measurableIDSchema>
                    <gl-bus:measurableStartDateTime contextRef="now">2026-07-05</gl-bus:measurableStartDateTime>
                </gl-bus:measurable>
                <gl-bus:measurable>
                    <gl-bus:measurableDescription contextRef="now">TSD_SMVM</gl-bus:measurableDescription>
                    <gl-bus:measurableQuantity contextRef="now" decimals="2" unitRef="eur">-100</gl-bus:measurableQuantity>
                </gl-bus:measurable>
                <gl-cor:taxes>
                    <gl-cor:taxAmount contextRef="now" decimals="2" unitRef="eur">-33</gl-cor:taxAmount>
                    <gl-cor:taxCode contextRef="now">TSD_SM</gl-cor:taxCode>
                </gl-cor:taxes>
                <gl-cor:taxes>
                    <gl-cor:taxAmount contextRef="now" decimals="2" unitRef="eur">-2</gl-cor:taxAmount>
                    <gl-cor:taxCode contextRef="now">TSD_KP</gl-cor:taxCode>
                </gl-cor:taxes>
                <gl-cor:taxes>
                    <gl-cor:taxAmount contextRef="now" decimals="2" unitRef="eur">-1.6</gl-cor:taxAmount>
                    <gl-cor:taxCode contextRef="now">TSD_TK</gl-cor:taxCode>
                </gl-cor:taxes>
                <gl-cor:taxes>
                    <gl-cor:taxAmount contextRef="now" decimals="2" unitRef="eur">-0.8</gl-cor:taxAmount>
                    <gl-cor:taxCode contextRef="now">TSD_TTK</gl-cor:taxCode>
                </gl-cor:taxes>
                <gl-cor:taxes>
                    <gl-cor:taxAmount contextRef="now" decimals="2" unitRef="eur">-21.21</gl-cor:taxAmount>
                    <gl-cor:taxCode contextRef="now">TSD_KTM</gl-cor:taxCode>
                </gl-cor:taxes>
            </gl-cor:entryDetail>
```

#### C.6.5 Zero payment, minimum social tax only (verbatim)

This is the case for a board member or employee who was paid nothing in the month.

```xml
            <gl-cor:entryDetail>
                <gl-cor:lineNumber contextRef="now">Tehing_21117</gl-cor:lineNumber>
                <gl-cor:lineNumberCounter contextRef="now" unitRef="NotUsed">7</gl-cor:lineNumberCounter>
                <gl-cor:account>
                    <gl-cor:accountSub>
                        <gl-cor:accountSubID contextRef="now">10</gl-cor:accountSubID>
                        <gl-cor:accountSubType contextRef="now">VALJAMLIIK2023ap</gl-cor:accountSubType>
                    </gl-cor:accountSub>
                    <gl-cor:accountSub>
                        <gl-cor:accountSubID contextRef="now">R</gl-cor:accountSubID>
                        <gl-cor:accountSubType contextRef="now">TOIMINGUTYYP2023ap</gl-cor:accountSubType>
                    </gl-cor:accountSub>
                </gl-cor:account>
                <gl-cor:amount contextRef="now" decimals="2" unitRef="eur">0</gl-cor:amount>
                <gl-bus:allocationCode contextRef="now">TartuOsakond</gl-bus:allocationCode>
                <gl-cor:identifierReference>
                    <gl-cor:identifierCode contextRef="now">EE48911110122</gl-cor:identifierCode>
                    <gl-cor:identifierDescription contextRef="now">ISIKUKOOD</gl-cor:identifierDescription>
                </gl-cor:identifierReference>
                <gl-cor:documentDate contextRef="now">2026-09-10</gl-cor:documentDate>
                <gl-bus:measurable>
                    <gl-bus:measurableID contextRef="now">TSD01</gl-bus:measurableID>
                    <gl-bus:measurableIDSchema contextRef="now">SYNDMUS2017ap</gl-bus:measurableIDSchema>
                    <gl-bus:measurableStartDateTime contextRef="now">2026-08-31</gl-bus:measurableStartDateTime>
                </gl-bus:measurable>
                <gl-bus:measurable>
                    <gl-bus:measurableDescription contextRef="now">TSD_KUUMSUURENDUS</gl-bus:measurableDescription>
                    <gl-bus:measurableQuantity contextRef="now" decimals="2" unitRef="eur">820</gl-bus:measurableQuantity>
                </gl-bus:measurable>
                <gl-cor:taxes>
                    <gl-cor:taxAmount contextRef="now" decimals="2" unitRef="eur">270.60</gl-cor:taxAmount>
                    <gl-cor:taxCode contextRef="now">TSD_SM</gl-cor:taxCode>
                </gl-cor:taxes>
            </gl-cor:entryDetail>
```

There is no `TSD_SMVM` here, although KR_SM_01_1 requires it "kui väljamakse liigile arvestatakse sotsiaalmaksu" (if the payment type is subject to SM), and type 10 is. Ex-XLSX writes the code as `TSD_KuumSuurendus`; the enum and Ex-XML write `TSD_KUUMSUURENDUS`.

---

## D. Change (M) and cancellation (T)

Source: Guide §4, rules KR_XBRL_VALJAMAKSED_*, Ex-T.

**What lineNumber refers to.** It is the `lineNumber` of a previously sent payment of the **same taxpayer, same tax period (periodCovered month) and same section (entryNumber)**. A correction therefore goes in a file for the original month, under the original section.

### D.1 R — new payment

- `lineNumber` must never have been used before in that taxpayer, period and section (KR_XBRL_VALJAMAKSED_4, BLACK). The rule does not exclude cancelled lines, so a cancelled `lineNumber` cannot be reused.
- To send a corrected version of a cancelled payment, the Guide says to create a **new** `lineNumber` with `R` and send the full data.

### D.2 M — change

- Send **the whole payment again**, with the same `lineNumber`, `TOIMINGUTYYP2023ap` = `M` and `VALJAMLIIK2023ap`. The Guide: "muutmisel kirjutatakse eelnevalt saadetud väljamakse detailandmed üle" (a change overwrites the previously sent details).
- A non-cancelled payment with that `lineNumber` must exist (KR_XBRL_VALJAMAKSED_5, BLACK).

### D.3 T — cancel

- Send only `lineNumber` (the existing one), `lineNumberCounter` and `accountSub` TOIMINGUTYYP2023ap = `T`.
- No VALJAMLIIK (rule _7 exempts T), no amount, no recipient, no date.
- A non-cancelled payment with that `lineNumber` must exist (KR_XBRL_VALJAMAKSED_6, BLACK).
- After T, the `lineNumber` is final: "muutub … lõplikuks ning selle tunnuse kaudu ei saa enam esitada muudatusi ega uusi andmeid" (it becomes final; no further changes or new data can be sent under it).

Ex-T, verbatim payload (the header blocks are the same as in A, with dates 2026-10-20 and period 2026-09):

```xml
        <gl-cor:entryHeader>
            <gl-cor:entryNumber contextRef="now">EE0201001</gl-cor:entryNumber>
            <gl-cor:entryDetail>
                <gl-cor:lineNumber contextRef="now">Tehing_2111</gl-cor:lineNumber>
                <gl-cor:lineNumberCounter contextRef="now" unitRef="NotUsed">1</gl-cor:lineNumberCounter>
                <gl-cor:account>
                    <gl-cor:accountSub>
                        <gl-cor:accountSubID contextRef="now">T</gl-cor:accountSubID>
                        <gl-cor:accountSubType contextRef="now">TOIMINGUTYYP2023ap</gl-cor:accountSubType>
                    </gl-cor:accountSub>
                </gl-cor:account>
            </gl-cor:entryDetail>
        </gl-cor:entryHeader>
```

Ex-T contains only the section that has the cancellation. It does **not** repeat EE0201002, and it does **not** carry an `entryComment`.

### D.4 Open questions

- **lineNumberCounter on T.** The Guide says T needs "järjekorra numbrist" (the sequence number). It does not say whether that is the original payment's counter or the next number in the new file. In Ex-T both are `1`. XTEE §5.5 says the feedback's `lineNumberCounter` "pärineb andmeesitaja poolt saadetud failist" (comes from the submitted file). That suggests the counter is the position within the file being sent.
- **Mixing R, M and T in one file.** No file forbids it; the rules are evaluated per payment. Nothing explicitly allows it either.
- **Omitted sections.** No file says what it means when a later file leaves a section out.

### D.5 Correcting confirmed or locked periods

- If the TSD is confirmed and there is no "pooleli" (draft) version open for changes, every submission is refused (DECLARATION_STATUS_OPEN_DECLARATION, BLACK): "Väljamaksete lisamiseks tuleb e-MTA-s eelnevalt deklaratsioon muudatusteks avada" (open the declaration for changes in e-MTA first). XTEE §4 repeats this: "eeldusel et deklaratsioon on avatud andmete muutmiseks".
- A locked declaration refuses submissions (DECLARATION_STATUS_CLOSED, BLACK). So does a declaration that an MTA official created and has not confirmed (DECLARATION_LOCKED, BLACK).
- When correcting confirmed data, give the reason in `entryComment`, one per section, using PARANDUSPOHJUS_1..4 (B.2).

### D.6 Previous calendar years (cut-off 15.02)

- **T is refused (KR_XBRL_VALJAMAKSED_8, BLACK)** when all of the following hold:
  - the tax period is either earlier than the previous calendar year, or the previous calendar year with today's date past 15.02 (the rule text literally says "varasem või hilisem kui 15.02", i.e. "earlier or later than"; the message says "peale 15.02", after 15.02);
  - the recipient is not a third-country national;
  - the payment carries income tax.
- **Adding** income-tax payments to earlier calendar years after 15.02 is refused when the TSD was not confirmed before (KR_TM_5, RED).
- **Changing** a confirmed previous-year TSD after 15.02 in a way that changes a natural person's TSD_KTM is refused (KR_TM_2, RED).
- KR_TM_5 and KR_TM_2 do not apply if the recipient is a non-resident legal person or a third-country resident natural person.

---

## E. Business rules ("SUBMIT DATA ÄRIREEGLID", 135 rows, Juhis v2.0 of 24.07.2026)

**Columns.**
- **Sev.** is the severity, BLACK/BLUE/RED/WARN as in Sources, explained in §F.2.
- **Chg** is the change marker relative to v1.0: `UUS` = new, `kood` = rule code renamed (old code in brackets).
- **ET** gives the key Estonian words.

**Response Data.** "Tagastatakse Response Data's" is **EI** (not returned per transaction) for the 22 transport, file, declaration and taxpayer rules in E.1–E.4. It is **JAH** for every other rule.

**Applicability marks.**
- **[ESD]** = applies only to EE0201003, i.e. state or municipal agencies, public-law persons, creative unions, FIE for spouse.
- **[FIE/natural]** = applies only to a natural-person or FIE taxpayer.
- **[seafarer]** = applies only to seafarer payment types.

Unmarked rules can hit an ordinary äriühing.

### E.1 Transport and file (no XML element)

| Rule | Sev. | Condition (EN) | ET |
|---|---|---|---|
| KR_XTEE_VASTUS_1 | BLACK | Message header must contain a uuid (HTTP 400) | "Sõnumi päises peab olema määratud uuid" |
| KR_XTEE_VASTUS_2 | BLACK | If X-Road-Represented-Party ≠ X-Road-Client, the client needs the right to submit for the represented party (HTTP 403). **This is the accounting-firm case.** | "peab päringu teostajal olema õigus esindatava eest" |
| KR_XTEE_VASTUS_3 | BLACK | The feedback-query UUID must belong to a message of the client or its represented party (HTTP 404) | "Sisendis esitatud UUID peab viitama olemasolevale sõnumile" |
| KR_XML_FAIL_1 | BLACK | "DE07": if the submitter is not the taxpayer, an authorization to submit over X-Road is required (HTTP 403) | "peab väljamakse esitajal leiduma volitus" |
| KR_XML_FAIL_3 | BLACK | Extension must be XML or ZIP (HTTP 415) | "laiend peab olema kas XML või ZIP" |
| KR_XML_FAIL_4 | BLACK | Size ≤ limit (HTTP 413). YAML and XTEE give the limit as 50 MB. | "ei tohi ületada maksimaalselt lubatud suurust" |
| KR_VALIDATE_XBRL_FILE_1 | BLACK | Must be in XML format | "peab olema XML formaadis" |
| KR_VALIDATE_XBRL_FILE_2 | BLACK | A ZIP may contain only one XML | "ZIP failis võib olla ainult üks XML fail" |
| KR_VALIDATE_XBRL_FILE_3 | BLACK | Must validate against the XSD | "peab valideeruma vastu XSD skeemi" |

### E.2 `documentInfo` — periodCoveredStart/End (tax period)

| Rule | Sev. | Chg | Condition | ET |
|---|---|---|---|---|
| KR_VALIDATE_XBRL_FILE_4 | BLACK | | Tax period not earlier than the APA Payments start period | "ei saa olla varasem, kui APA Payments algusperiood" |
| KR_VALIDATE_XBRL_FILE_7 | BLACK | | Start and end in the same calendar month | "peavad olema samas kalendrikuus" |
| KR_VALIDATE_XBRL_FILE_8 | BLACK | UUS | Tax period must equal the UI tax period the file is loaded into | "peab kattuma kasutajaliidese maksustamisperioodiga" |

### E.3 `entityInformation` — organizationIdentifier (taxpayer)

| Rule | Sev. | Chg | Condition | ET |
|---|---|---|---|---|
| KR_VALIDATE_XBRL_FILE_6 | BLACK | | Registry code in the file = taxpayer in the message header | "peab kattuma sõnumi edastamisel määratud … registrikoodiga" |
| KR_MAKSUKOHUSTUSLANE_01 | BLACK | | Period not earlier than the natural-person taxpayer's birth month **[FIE/natural]** | "varasem kui isiku sünnikuupäev" |
| KR_MAKSUKOHUSTUSLANE_02 | BLACK | | Period not later than the natural-person taxpayer's death month **[FIE/natural]** | "hilisem kui isiku surmakuupäev" |
| KR_MAKSUKOHUSTUSLANE_03 | BLACK | | Period not before the start of activity in Äriregister | "varasem, kui … tegevuse alustamise kuupäev Äriregistris" |
| KR_MAKSUKOHUSTUSLANE_04 | BLACK | | Period not after the end-of-activity month in Äriregister | "hilisem, kui … tegevuse lõpetamise kuu" |
| KR_MAKSUKOHUSTUSLANE_05 | BLACK | UUS | Taxpayer status Suletud or Mitteaktiivne may not submit | "staatus on Suletud või Mitteaktiivne" |

### E.4 Declaration state (no element; depends on the TSD state in e-MTA)

| Rule | Sev. | Chg | Condition | ET |
|---|---|---|---|---|
| DECLARATION_STATUS_OPEN_DECLARATION | BLACK | | Confirmed TSD without an open draft version → submission refused; open it in e-MTA first | "deklaratsioon on kinnitatud ja puudub muutmiseks avatud 'pooleli'" |
| DECLARATION_STATUS_CLOSED | BLACK | | Locked TSD → refused | "deklaratsioon on lukustatud" |
| DECLARATION_LOCKED | BLACK | UUS | Declaration created by an MTA official and not yet confirmed → refused | "MTA ametniku poolt loodud ja kinnitamata" |

### E.5 `lineNumber`

| Rule | Sev. | Condition | ET |
|---|---|---|---|
| KR_XBRL_VALJAMAKSED_1 | BLACK | Must be present | "kirje unikaalne tunnus määratud" |
| KR_XBRL_VALJAMAKSED_2 | BLACK | Unique per taxpayer + tax period + section | "maksukohustuslase, maksustamisperioodi ja andmesektsiooni piires unikaalne" |

### E.6 `accountSub` TOIMINGUTYYP2023ap (R/M/T)

| Rule | Sev. | Condition | ET |
|---|---|---|---|
| KR_XBRL_VALJAMAKSED_3 | BLACK | Action type must be present | "toimingu tüüp olema määratud" |
| KR_XBRL_VALJAMAKSED_4 | BLACK | R: no earlier payment with the same lineNumber may exist (taxpayer + period + section) | "ei tohi eelnevalt leiduda sama kirje unikaalse tunnusega" |
| KR_XBRL_VALJAMAKSED_5 | BLACK | M: a non-cancelled payment with that lineNumber must exist | "peab leiduma … mittetühistatud väljamakse" |
| KR_XBRL_VALJAMAKSED_6 | BLACK | T: same as _5 | (as _5) |
| KR_XBRL_VALJAMAKSED_8 | BLACK | T refused for periods before the previous year, or the previous year after 15.02, when the recipient is not a third-country national and income tax applies (see D.6) | "tühistamine … ei ole peale 15.02 lubatud" |

### E.7 `accountSub` VALJAMLIIK2023ap (payment type)

| Rule | Sev. | Chg | Condition | ET |
|---|---|---|---|---|
| KR_XBRL_VALJAMAKSED_7 | BLACK | | Payment type required unless the action is T | "Kui … ei ole tühistamine (T), peab väljamakse liik olema määratud" |
| KR_XBRL_VALJAMAKSED_14 | BLACK | kood (KR-YLD_05) | Type must be a valid classifier value | "peab kuuluma klassifikaatoriga lubatud väärtuste hulka" |
| KR_YLD_04 | (RED) | DELETED | Duplicate of _7, removed | "DUBLIKAAT - KUSTUTATUD" |
| KR_VM_01 | BLUE | | Type not allowed for a legal-person taxpayer | "ei ole lubatud juriidilisest isikust maksukohustuslasele" |
| KR_VM_01_1 | BLUE | | Type allowed only for FIE taxpayers | "lubatud ainult … (FIE) maksukohustuslasele" |
| KR_VM_02 | BLUE | | Type must fit the recipient kind (legal or natural person) | "vastavat tüüpi väljamakse saajale" |
| KR_VM_03 | BLUE | | Resident-only type → recipient must be resident | "peab väljamakse saaja olema resident" |
| KR_VM_04 | BLUE | | Non-resident-only type → recipient must be non-resident | "peab väljamakse saaja olema mitteresident" |
| KR_VM_05 | BLUE | | Type must be allowed in this section | "peab olema määratud andmesektsioonis lubatud" |
| KR_VM_06 | BLUE | | Type allowed only for taxpayers with a special flag (*eritunnus*) | "ainult eritunnusega maksukohustuslasel" |

### E.8 `accountSub` country, role and transport (RTK2T2013ap / A1_RIIGID_enum / RIIGIROLL2022ap / TL2015ap)

| Rule | Sev. | Chg | Condition | ET |
|---|---|---|---|---|
| KR_XBRL_VALJAMAKSED_15 | BLACK | kood (KR-YLD_06) | If a country role is given, a country is required | "on määratud riigi roll, peab olema ka riik" |
| KR_YLD2_01 | BLUE | | The RR_A1 country must be in the A1 list | "peab leiduma A1 riikide klassifikaatoris" |
| KR_YLD2_02 | RED | | A1 not allowed for this payment type | "ei ole A1 riigi määramine lubatud" |
| KR_YLD2_03 | BLUE | | This kind of A1 country (Estonia vs foreign) not allowed for the type | "(Eesti vs välisriik)" |
| KR_YLD2_04 | BLUE | | RR_LAEVAPERE_RIIK required when the type needs it **[seafarer]** | "laevapere riik … olema määratud" |
| KR_YLD2_06 | RED | | A1 given → RR_TOOKOHA_ASUKOHT required | "peab olema ka töötamise asukoha riik" |
| KR_YLD2_07 | BLUE | | Work location foreign → A1 must be Estonia | "peab A1 riik … olema määratud Eesti" |
| KR_YLD2_08 | RED | | Work location foreign → A1 required | "peab A1 riik … olema määratud" |
| KR_YLD2_09 | RED | | A1 = Estonia → work location must not be Estonia | "ei tohi töötamise asukoha riik … olla … Eesti" |
| KR_ISIK_6 | RED | | RR_MAKSURESIDENTSUSE_RIIK must equal the residence in the MTA register. Submit the residency certificate to MTA first. | "peab kattuma MTA Isikute halduses oleva … riigiga" |
| KR_LAEV_01 | BLUE | | Seafarer types need the IMO number **[seafarer]** | "laeva IMO number … olema määratud" |
| KR_LAEV_02 | BLUE | | IMO must be in the state-aid ship list **[seafarer]** | "riigiabi laevade nimekirjas" |

### E.9 `amount`

| Rule | Sev. | Condition | ET |
|---|---|---|---|
| KR_YLD_12 | BLUE | Mandatory in EE0201001/EE0201002 | "peab väljamakse summa (Rahasumma) olema määratud" |
| KR_YLD_30 | BLUE | Not a refund (01/02) → all amounts 0 or positive | "peavad summalised väärtused olema (0) või Suuremad" |
| KR_YLD_24 | BLUE | Refund (01/02) → all amounts 0 or negative | "peavad summalised väärtused olema (0) või väiksemad (negatiivsed)" |
| KR_YLD2_05 | BLUE | Amount "0" not allowed when the type doesn't allow it | "ei tohi väljamakse summa … olla määratud '0'" |

### E.10 `identifierReference`

| Rule | Sev. | Chg | Condition | ET |
|---|---|---|---|---|
| KR_XBRL_VALJAMAKSED_12 | BLACK | kood (KR-YLD_01) | Recipient code required (isikukood, MRR or registry code) | "Väljamakse saaja kood … peab olema määratud" |
| KR_XBRL_VALJAMAKSED_13 | BLACK | kood (KR-YLD_02) | Code type required | "koodi tüüp peab olema määratud" |
| KR_XBRL_VALJAMAKSED_9 | BLACK | (KR_YLD_03) | Code type in the classifier | "koodi tüüp peab kuuluma klassifikaatoriga lubatud väärtuste hulka" |
| KR_XBRL_VALJAMAKSED_10 | BLACK | UUS | isikukood structure: country code + code | "riigi kood ja isikukood" |
| KR_XBRL_VALJAMAKSED_11 | BLACK | UUS | Registry-code structure | "Äriregistri kood peab vastama oodatud struktuurile" |
| KR_IH_1 | BLUE | | Recipient code must exist in MTA Isikute haldus (person register) | "peab leiduma MTA Isikute halduses" |
| KR_ISIK_1 | BLUE | | Recipient ≠ taxpayer | "ei tohi olla maksukohustuslane … ise" |
| KR_ISIK_3 | WARN | | Natural-person recipient deceased. Applies when dead as of the day before the first day of the month preceding the period; the rule's example is death on 15.03.2027 → warning from period 05.2027. | "on surnud" |
| KR_ISIK_4 | WARN | | Legal-person recipient deleted from the register at the start of the period | "on registrist kustutatud" |
| KR_ISIK_5 | WARN | | MRR code used but the person has an Estonian isikukood → use that | "tuleb kasutada Eesti isikukoodi" |

### E.11 `documentDate`

| Rule | Sev. | Chg | Condition | ET |
|---|---|---|---|---|
| KR_XBRL_VALJAMAKSED_17 | BLACK | kood (KR-YLD_15) | In 01/02, the payment date must fall within periodCoveredStart..End | "peab kuuluma maksustamisperioodi" |
| KR_YLD_11 | BLUE | | Mandatory in 01/02 | "peab väljamakse kuupäev … olema määratud" |
| KR_YLD_25 | BLUE | | TSD01 (calculation date) ≤ documentDate | "ei saa olla hilisem, kui väljamakse kuupäev" |

### E.12 `measurable` (generic and SYNDMUS)

| Rule | Sev. | Chg | Condition | ET |
|---|---|---|---|---|
| KR_XBRL_VALJAMAKSED_16 | BLACK | kood (KR-YLD_07) | Each measurable needs a description type or an event-date type | "maksuarvestuse alusandme tüüp või sündmuse kuupäeva tüüp" |
| KR_XBRL_VALJAMAKSED_18 | BLACK | kood (KR-YLD_33) | No two measurables of the same description type | "mitut samatüübilist maksuarvestuse alusandme kirjet" |
| KR_YLD_22 | BLUE | | An amount-type measurable must not carry start/end dates | "ei tohi olla määratud algus- ja lõppkuupäev" |
| KR_YLD_23 | BLUE | | Where an amount is mandatory, it must be given | "kus on summa kohustuslik, peab summa olema" |
| KR_YLD_37 | BLUE | UUS | Description type must be in the enum | "peab kuuluma klassifikaatori väärtuste hulka" |
| KR_YLD_13 | BLUE | | No ESM01 period in 01/02 | "sündmuse tüüp on ESM01" |
| KR_YLD_14 | BLUE | | Refund (TSD02, 01/02) → the original payment date (StartDateTime) is required | "tagastamise aluseks oleva algse väljamakse kuupäev" |

### E.13 Social-tax base measurables (TSD_SMVM, TSD_OPTV_SM_VAHENDUS, TSD_KUUM*, TSD_SMVMSK)

| Rule | Sev. | Chg | Condition | ET |
|---|---|---|---|---|
| KR_SM_01 | RED | | Type without SM → no TSD_SMVM | "ei tohi … TSD_SMVM olla määratud" |
| KR_SM_01_1 | RED | | Type with SM → TSD_SMVM required | "peab … TSD_SMVM olema määratud" |
| KR_SM_01_2 | RED | | Type with SM, not a seafarer type → TSD_SMVM = amount | "peab … olema võrdne väljamakse summaga" |
| KR_SM_05 | RED | | OPTV not allowed for this type | "TSD_OPTV_SM_VAHENDUS … ei tohi" |
| KR_SM_03 | RED | | OPTV only if Töötukassa declared the person (EE0201003, type 615) in the same or previous period | "Töötukassa kinnitatud deklaratsioonil … 615" |
| KR_SM_06 | RED | | OPTV ≤ TSD_SMVM | "ei tohi see olla suurem kui … TSD_SMVM" |
| KR_SM_07 | RED | | OPTV ≤ 2 × SM_KUUMAAR (monthly rate) | "kahekordne sotsiaalmaksu kuumäär" |
| KR_SM_08 | RED | | KUUMVAHENDUS not allowed for this type | "kuumäära vähendus … ei tohi" |
| KR_SM_09 | RED | | KUUMVAHENDUS ≤ TSD_SMVM | |
| KR_SM_10 | RED | | OPTV + KUUMVAHENDUS ≤ TSD_SMVM | "vähenduse summa kokku" |
| KR_SM_11 | RED | | KUUMSUURENDUS not allowed for this type. The text says "TSD_SUURENDUS", a typo. | "kuumäära suurendus … ei tohi" |
| KR_SM_12 | RED | | KUUMSUURENDUS ≤ 2 × SM_KUUMAAR | |
| KR_SM_13 | RED | | TSD_SMVMSK only on refunds (TSD02) | "Kui tegemist ei ole tagasimaksega (TSD02)" |
| KR_SM_14 | RED | | TSD_SMVMSK not when the type has no monthly minimum | "Väljamksel puudub sotsiaalmaksu miinimumkohustus" |
| KR_SM_KOOND_01 | RED | UUS | Per person per period: Σ OPTV ≤ 2 × monthly rate | "kumulatiivselt" |
| KR_SM_KOOND_02 | RED | UUS | Per person per period: Σ KUUMVAHENDUS ≤ 2 × monthly rate | |
| KR_SM_KOOND_03 | RED | UUS | Per person per period: Σ KUUMSUURENDUS ≤ 2 × monthly rate | |

### E.14 Tax-free income measurables (MVT_610 / MVT_640 / MVT_650)

| Rule | Sev. | Condition | ET |
|---|---|---|---|
| KR_MVT_01 | RED | MVT_610/MVT_650 not allowed for this type. FB-3 shows it fired for sick pay, type 24. | "ei ole maksuvaba tulu … lubatud" |
| KR_MVT_02 | RED | MVT_640 not allowed for this type | "III samba … ei ole … lubatud" |
| KR_MVT_04 | RED | Person reaches pension age this calendar year → no MVT_610 from 1 January of that year | "Isik on vanaduspensionieas" |
| KR_MVT_05 | RED | Person not reaching pension age this year → no MVT_650 | "Isik ei ole vanaduspensionieas" |
| KR_MVT_06 | RED | Recipient not an EEA tax resident → no MVT | "ei ole EMP riigi maksuresident" |
| KR_MVT_07 | RED | EEA resident without a valid EEA residency certificate → no MVT | "puudub kehtiv EMP riigi residentsustõend" |
| KR_MVT_08 | RED | MVT ≤ amount − TSD_KP − TSD_TK | "väljamakse summa … miinus … (TSD_KP ja TSD_TK)" |
| KR_MVT_09 | WARN | Income tax withheld at a 0 % rate → MVT not needed | "Maksuvaba tulu määramine ei ole vajalik" |
| KR_KOOND_01 | RED | MVT_640 ≤ 15 % of income-taxed payments to the employee. The rule text says per calendar year; the message says same tax period. | "15% töötajale makstud" |
| KR_KOOND_06 | RED | MVT_640 ≤ 6000 € per calendar year | "6000€" |
| KR_KOOND_02 | RED | MVT_610 ≤ the monthly legal limit, within this taxpayer | "maksukohustuslase piires" |
| KR_KOOND_03 | RED | MVT_650 ≤ the monthly legal limit, within this taxpayer | |
| KR_KOOND_04 | WARN | MVT_610 over the limit across all payers. Suggest that the person reduce their application. | "üle väljamakse tegijate" |
| KR_KOOND_05 | WARN | MVT_650 over the limit across all payers | |

### E.15 `taxes` (generic)

| Rule | Sev. | Chg | Condition | ET |
|---|---|---|---|---|
| KR_XBRL_VALJAMAKSED_19 | BLACK | kood (KR-YLD_34) | No duplicate tax code per payment | "mitut samatüübilist maksu liigi kirjet" |
| KR_YLD_35 | BLUE | UUS | Tax record must have a type | "maksukirje liik peab olema määratud" |
| KR_YLD_36 | BLUE | UUS | Tax type must be in TAXCODE_enum | "Tundmatu maksukirje liik" |

### E.16 TSD_SM

| Rule | Sev. | Condition | ET |
|---|---|---|---|
| KR_SM_02 | RED | Type without SM → no TSD_SM | "ei tohi sotsiaalmaksu summa olla määratud" |
| KR_SM_04 | RED | TSD_SM = (SMVM − OPTV − KUUMVAHENDUS + KUUMSUURENDUS − SMVMSK) × rate, within tolerance | "peab vastama süsteemi poolt arvutatud väärtusele" |

### E.17 TSD_TK and TSD_TTK

| Rule | Sev. | Condition | ET |
|---|---|---|---|
| KR_TK_1 | RED | Type without TK → no TSD_TK | "Väljamakselt ei peeta kinni töötuskindlustusmakset" |
| KR_TK_2 | RED | Recipient on flexible or old-age pension as of the calendar month before the payment date → no TSD_TK | "paindlikul või vanaduspensionil" |
| KR_TK_3 | RED | TSD_TK = amount × TK rate, within tolerance | |
| KR_TTK_1 | RED | Type without TTK → no TSD_TTK | |
| KR_TTK_2 | RED | TSD_TTK = amount × TTK rate, within tolerance | |

### E.18 TSD_KP

| Rule | Sev. | Condition | ET |
|---|---|---|---|
| KR_KOP_01 | RED | Type without KP → no TSD_KP | |
| KR_KOP_02 | RED | Recipient has no mandatory funded-pension obligation → no TSD_KP | "puudub kohustusliku kogumispensioni kohustus" |
| KR_KOP_03 | RED | TSD_KP = SMVM × the person's rate, within tolerance | "<väljamakse SMVM> *<isiku määr>%" |

### E.19 TSD_KTM and taxPercentageRate

| Rule | Sev. | Condition | ET |
|---|---|---|---|
| KR_TM_1 | RED | Type without income tax → no TSD_KTM | "Väljamakselt ei peeta kinni tulumaksu" |
| KR_TM_5 | RED | No adding payments with KTM to earlier calendar years after 15.02 if the TSD wasn't confirmed before. Exceptions: non-resident legal person; third-country resident natural person. | "pärast 15.02 … varasemasse kalendriaastasse" |
| KR_TM_2 | RED | No changes after 15.02 to the previous year's confirmed TSD that change a natural person's KTM. Same exceptions. | "muutub füüsilise isiku tulumaksu (TSD_KTM) summa" |
| KR_TM_3 | RED | taxPercentageRate = the system-determined rate | "peab vastama süsteemi poolt leitud tulumaksu määraga" |
| KR_TM_4 | RED | KTM = (amount − KP − TK − MVT) × rate, within tolerance | "lubatud hälbe piires" |

### E.20 [ESD] — EE0201003 only (state/municipal agencies, public-law persons, creative unions, FIE for spouse)

| Rule | Sev. | Condition |
|---|---|---|
| KR_YLD_08 | BLUE | ESM01 start and end dates required |
| KR_YLD_09 | BLUE | No TSD01, no documentDate, no amount |
| KR_YLD_31 | BLUE | SM and KP both positive or both negative |
| KR_YLD_32 | BLUE | No TSD02 |
| KR_ESM_01 | RED | SM required when the type allows only SM |
| KR_ESM_08 | RED | No TSD_KP when the type doesn't allow it |
| KR_ESM_02 | RED | SM or KP required when the type allows both |
| KR_ESM_03 | RED | ESM01 start ≤ end |
| KR_ESM_04 | RED | ESM01 within one calendar month |
| KR_ESM_05 | RED | Positive tax: ESM01 not in the future relative to the period |
| KR_ESM_06 | RED | Negative tax: ESM01 earlier than the period |
| KR_ESM_07 | RED | No duplicate non-cancelled payment (same recipient, type, tax code, tax amount, ESM01 period) |

**Calculator rules.** The sheet "KALKULEERIMISE LISAREEGLID" holds calculator-service-only rules: KR_KALK_XML_FAIL_01..05, KR_KALK_OIGUS_01, KR_KALK_YLD_01 and KR_KALK_KOOND_01. They are out of scope. Of note: KR_KALK_XML_FAIL_05 requires that the payment dates of all payments in a calculator file fall in one calendar month.

---

## F. Feedback report (*tagasisideraport*) and confirmation (*kinnitamine*)

### F.1 How the report is obtained

Sources: YAML payments API and XTEE §2/§4/§5.

1. `POST …/apa-tsd/payments/v1/xtee/submit-data` accepts one `multipart/form-data` file.
   - The file name must end in `.xml` or `.zip`; the part's Content-Type is `application/xml` or `application/zip`; the maximum is 50 MB.
   - A ZIP holds exactly one XML.
   - The headers are `X-Road-Client` (required), `X-Road-Id` and `X-Road-Represented-Party`. A firm submitting for a client sets `X-Road-Represented-Party` and needs an authorization (KR_XTEE_VASTUS_2, KR_XML_FAIL_1).
   - The response is `201 {"uuid": "…", "estimatedProcessingTime": "PT15S"}`.
2. `GET …/v1/xtee/return-data/{uuid}` returns 204 while processing. When done, it returns 200 with `application/xml` for one message, or `application/zip` for two or more.
   - HTTP-level errors come back as JSON `{id?, code?, message}`, where `code` is one of KR_XML_FAIL_1..4, KR_XTEE_VASTUS_1..3, UNSUPPORTED_FILE_FORMAT, INVALID_FILE or UNKNOWN_UUID.

Processing is asynchronous.

### F.2 Structure (FB-XSD; no namespace)

Element order is the XSD sequence.

```
processing  @result = OK | WARNING | ERROR | PARTLYERROR           (XTEE §5.5)
└─ entryHeader 1..n           — one per section, plus one with no entryNumber for file-level errors
   ├─ entryNumber 0..1        — your gl-cor:entryNumber
   ├─ validations 1..n
   │  └─ validation 1..n
   │     ├─ errorLevel        — OK | WARNING | ERROR
   │     ├─ errorCode         — "OK", "WARNING", "ERROR", or a rule code (e.g. KR_VALIDATE_XBRL_FILE_7)
   │     ├─ errorDescription  — ET text
   │     ├─ errorDescription_EN 0..1, errorDescription_RU 0..1
   │     └─ lineReferences 0..n — absent for file-level errors
   │        └─ lineReference 1..n
   │           ├─ lineInternalId     — your gl-cor:lineNumber
   │           └─ lineNumberCounter  — your gl-cor:lineNumberCounter (xs:integer)
   └─ responseData 0..1
      └─ entryDetail 1..n     — one per payment
         ├─ lineReferences 0..1
         └─ data 1..n
            ├─ code           — TAXCODE_enum value (tax line), or a rule code (message)
            ├─ description 0..1 — tax name, or severity: WARNING | ERROR_RED | ERROR_BLUE | ERROR_BLACK
            ├─ info           — tax amount, or ET message text
            └─ info_EN 0..1, info_RU 0..1
```

**`processing/@result`** (XTEE §5.5):
- **OK**: there are no errors.
- Otherwise the result is the highest level present: **ERROR** beats **WARNING**. "ERROR puhul tuleb andmed uuesti esitada" (on ERROR, resubmit the data).
- **PARTLYERROR**: some payments are clean and some have errors or warnings. Resubmit the erroneous payments, plus warned ones if the warning is fixable.

**`responseData`** carries the **calculated tax lines** for payments without errors (e.g. `<code>TSD_KTM</code><description>Kinnipeetud tulumaks</description><info>241,45</info>`) and the detailed rule messages.

### F.3 Severity colours and what they mean for the submitter

Sources: Juhis "VEA KATEGOORIAD" and XTEE §5.5.

| Rules sheet | Feedback `data/description` | Saved? | Taxes in the TSD summary form (*koondvorm*)? | TSD can be confirmed? | What the app should do |
|---|---|---|---|---|---|
| **MUST (black)** | `ERROR_BLACK` | **No.** A file-level black (XML structure etc.) aborts the whole file; a transaction-level black rejects that payment only. | — | — | The payment does not exist on the server, so fix it and send it again as **R** (inference: an M would fail KR_XBRL_VALJAMAKSED_5). From group KR_XBRL_VÄLJAMAKSED onwards, the rejected payment is still echoed in Response Data so the error can be tied to it. |
| **SININE (blue)** | `ERROR_BLUE` | Yes | **No** | (not stated) | "Tehinguandmestiku oluline puudus" (significant gap in the transaction data). The payment exists, so correct it with **M** (inference: R would fail _4). |
| **PUNANE (red)** | `ERROR_RED` | Yes | Yes | **No**, until fixed | Blocking. Correct with **M**. |
| **KOLLANE (yellow)** | `WARNING` | Yes | Yes | Yes | Advisory only (e.g. KR_ISIK_3 deceased, KR_MVT_09, KR_KOOND_04/05) |

**Two vocabularies.** `validation/errorLevel` uses only OK / WARNING / ERROR. The colour appears only in `responseData/entryDetail/data/description`. FB-3 (2026-03-30), which is older, uses plain `ERROR` there; the 2026-09-10 sample uses the four colour values.

**What the examples show:**
- **FB-1** (`faili_enda_veaga`): file-level error. `result="ERROR"`, an entryHeader **without** entryNumber, and `errorCode` = `" KR_VALIDATE_XBRL_FILE_7"`. Note the **leading space** in the code, which needs trimming.
- **FB-3** (`osaliselt_vigadega`): `result="PARTLYERROR"`.
  - Per-line OK / WARNING / ERROR validations, each with a lineReference.
  - Tax amounts with a **decimal comma** (`241,45`).
  - Rule messages KR_ISIK_3, KR_XBRL_VALJAMAKSED_3, KR_SM_02 and KR_MVT_01.
  - The EE0201002 entry has `lineNumberCounter` **5**, so the counter does not restart there.
- **2026-09-10 sample**:
  - `KR_ISIK_3` WARNING and `KR_VM_03` ERROR_BLUE on the same payment.
  - `KR_TM_4` ERROR_RED with message "… summa 106,04 (500 - 10 - 8 - 0 ) * 22%)".
  - `KR_XBRL_VALJAMAKSED_3` and `_14` ERROR_BLACK, whose `lineReference` has **only `lineInternalId`, no `lineNumberCounter`**. That violates FB-XSD, where the counter is required, so the parser must tolerate it.
- **FB-2 and FB-4** (`kinnitamine_error`, `kinnitamine_ok`) are **confirmation** feedback (F.4).

### F.4 Confirmation (*kinnitamine*)

Sending payments does **not** confirm the TSD; confirmation is a separate call.

**Endpoints. The sources disagree:**
- YAML: `POST …/apa-tsd/declarations/v1/xtee/confirm` and `GET …/v1/xtee/confirm-status/{uuid}`.
- XTEE v1.3 §2: `POST …/confirm` and `GET …/confirmations/{uuid}/status`.
- XTEE §6/§7: `POST …/confirmations` and `GET …/confirmations/{uuid}/status`.

**Input.** A **digitally signed or e-sealed ASiC-E container (`.asice`)** with exactly one XML (multipart field `file`, content type `application/vnd.etsi.asic-e+zip`). The response is `201 {"uuid": …}`; the GET returns 204 until done.

**Confirmation XML** (Conf XSD, no target namespace; sequence order):

| Element | Card. | Type / values |
|---|---|---|
| `confirmDeclaration` (root) | 1 | — |
| `personCode` | 0..1 | 11 digits. The confirmer's personal code, used when the XML is **e-sealed**. |
| `declaration` | 1 | string 1..500, e.g. "Kinnitan andmete õigsust" |
| `taxablePersonIdentifier` | 1 | string 1..50 (registry code) |
| `taxablePersonIdentifierType` | 1 | `ARIREGISTRIKOOD` / `ISIKUKOOD` / `MRR_KOOD` |
| `declarationType` | 1 | `TSD` / `ESD` / `XF11` / `XF12` |
| `month` | 1 | `0?[1-9]` or `1[0-2]` |
| `year` | 1 | `[0-9]{4}` |
| `sections/sectionItem` | 1 / 1..n | `section` + `hasData` (boolean: "true if the confirmer has submitted this data set and wants to confirm it"). For TSD, `section` ∈ `PAYMENTS`, `L3`, `L4`, `L5`, `L6`, `L7`, `L8`, `INF1` (the TSD annexes); for ESD ∈ `ESM_SM`, `ESM_KP`. XTEE §6.5 calls the element `sectionType`, while the XSD and the sample use `section`. |
| `taxSums/taxSum` | 1 / 1..n | `taxType` + `taxAmount` (decimal, 2 fraction digits, **≥ 0**). For TSD, `taxType` is one of the codes below. |

TSD `taxType` codes (names from FB-2/FB-4):

| Code | Name |
|---|---|
| `110` | Kinnipeetud tulumaks |
| `114` | Erijuhtude tulumaks |
| `115` | Sotsiaalmaks |
| `116` | Töötuskindlustusmakse |
| `117` | Kohustusliku kogumispensioni makse |
| `118` | Maksukohustus kokku |
| `119` | Kuulub tagastamisele |

The sample (`APA_TSD_20260326_kinnitamine_naidis.xml`) has `<?xml … encoding="UTF-8"?>`, `declarationType` TSD, month 10, year 2026, eight `sectionItem`s and seven `taxSum`s. The files do not say how `taxSums` is checked against the server's totals.

**Confirmation feedback** (same `processing` schema; XTEE §7.5):
- `result` is OK or ERROR. On ERROR, the confirmation must be started again.
- `errorCode` is `OK` or one of:
  - `CANNOT_CONFIRM_DECLARATION` (confirmation failed)
  - `DECLARATION_PROCESSING` (data still being processed)
  - `DECLARATION_WITH_ERRORS` (declaration has errors)
  - `DECLARATION_WITH_DISCREPANCIES` (contradictions in the data)
- `responseData/entryDetail/data` lists the declaration's tax sums with `code` 110…119, `description` = name, and `info` = amount with a decimal **dot** (e.g. `1433.64`). These appear both on OK and on DECLARATION_WITH_ERRORS.

---

## G. Gotchas

1. **The tax period is the payment month, not the accrual month.**
   - `documentDate` must fall inside `periodCoveredStart..End` (KR_XBRL_VALJAMAKSED_17, BLACK), and the deadline is keyed to the payment month.
   - Ex-XML reports an August calculation (TSD01 = 2026-08-31) paid on 2026-09-10 in the **September** file, sent on 2026-10-02.
   - TSD01 may lie in an earlier month; the only rule is TSD01 ≤ documentDate.

2. **`lineNumber` is a permanent business key.**
   - It is unique per taxpayer + period + section across all submissions.
   - R on an existing key is BLACK.
   - After T the key is dead forever.
   - Feedback echoes it as `lineInternalId`.
   - The app must store it per payment and never regenerate it.

3. **`lineNumberCounter`** is per section and per file (1..n in the examples). Its role on T (original or new position) is unclear (D.4).

4. **`taxCode` comes after `taxAmount`** (and after `taxPercentageRate`) inside `<gl-cor:taxes>`. All sequences are strict:
   - `entryDetail`: lineNumber, lineNumberCounter, account, amount, allocationCode, identifierReference, documentDate, (confirmedDate), measurable*, taxes*
   - `measurable`: ID, IDSchema, Description, Quantity, StartDateTime, EndDateTime

5. **`decimals="2"` is required and fixed** on `amount`, `taxAmount`, `taxPercentageRate` and `measurableQuantity`; `precision` is forbidden.
   - Values need **not** be padded: Ex-XML has `1000`, `7.2`, `270.60`, `-1.6`.
   - Use a dot in the XML.
   - Rates are fractions (`0.22`) with the `pure` unit, not percentages.

6. **Dates are `xs:date` (`yyyy-MM-dd`).** This includes `measurableStartDateTime`/`EndDateTime` despite their names. The source XLSX shows `09/10/2026 00:00:00` as an Excel artifact; do not copy that format.

7. **Signs.**
   - Non-refunds: every amount ≥ 0.
   - Refunds (TSD02 present): every amount ≤ 0, including the measurable quantities and all taxes.
   - The refund carries **two** SYNDMUS blocks (TSD01 + TSD02).

8. **Zero amounts.**
   - `amount` is mandatory in 01/02 and is "0 if no payment". Use this for the minimum-social-tax case (TSD_KUUMSUURENDUS).
   - Zero is refused for types that don't allow it (KR_YLD2_05).
   - Ex-XML writes an explicit `TSD_KTM` of `0` once (seafarer) and otherwise omits taxes that don't apply. No general rule is given.

9. **Exactly one `xbrli:context`.** Every fact needs `contextRef` pointing at it; every numeric fact also needs `unitRef`.

10. **The APA XSD is narrower than the taxonomy prose.** Generic XBRL GL elements listed in Tax. "XML väljade info" (contact names, identifierCategory, debitCreditCode, accountMainID, documentNumber, detailComment, measurableUnitOfMeasure, measurableQualifier, taxBasis, taxDescription, entity phone and e-mail) fail XSD validation in TSD (BLACK, whole file).

11. **The example XML has errors.**
    - Tehing_21115 uses `parentSubaccountType` = `RR_A1` instead of `RIIGIROLL2022ap`.
    - The schemaRef href differs between Guide p.20 and every instance example.
    - The uniqueID uses `:` before milliseconds.

12. **Code casing.**
    - Use classifier casing: `RR_TOOKOHA_ASUKOHT`, `RR_MAKSURESIDENTSUSE_RIIK`, `TSD_KUUMSUURENDUS`, `TSD_KTM`.
    - The prose has `RR_tookoha_asukoht`, `TSD_KuumSuurendus` (Ex-XLSX), `TSD_Ktm` (Tax. "XML väljade info") and `TSD_SUURENDUS` (KR_SM_11 text).
    - Mixed-case codes such as `RR_ostja` and `RR_elukohariik` genuinely exist in RIIGIROLL2022ap but are not used in TSD.
    - Greece in A1_RIIGID_enum is `EL`.

13. **Compound classifiers.** From 01.01.2026 they use `segmentParentTuple`, not comma-joined codes (Uuendused). Tax. "Üldised andmeväljad" A30 still shows the old style.

14. **Uuendused (09.2025 vs 04.2025):**
    - new section EE0201003;
    - SUMMALIIK2023ap deleted, its content moved to TAXCODE_enum and MEASURABLEDESCRIPTION_enum;
    - SYNDMUS2017ap gained ESM01, TSD01 now means *calculation date*, and the refund date moved from TSD01 to **TSD02**;
    - VALJAMAKSELIIK2023ap gained ESD subtypes `ESM_1`;
    - `KANDETYYP2023ap` was renamed `TOIMINGUTYYP2023ap`;
    - the compound-classifier structure changed.

    Code written against older samples will use the wrong TSD01/TSD02 meaning.

15. **Taxpayer identity.**
    - `organizationIdentifier` (and the context identifier) must equal the taxpayer in the X-Road header (FILE_6).
    - When the accounting firm submits, the firm is the X-Road client, the client company goes in `X-Road-Represented-Party`, and an authorization must exist (KR_XTEE_VASTUS_2 / KR_XML_FAIL_1).
    - `creator` is free text.

16. **Server-only checks.** The app cannot reproduce these locally because they read MTA registers:
    - residency (KR_ISIK_6, KR_MVT_06/07, KR_VM_03/04);
    - pension status (KR_TK_2, KR_MVT_04/05);
    - funded-pension obligation (KR_KOP_02);
    - deceased or deleted recipients (KR_ISIK_3/4);
    - Töötukassa OPTV (KR_SM_03);
    - cross-payer MVT totals (KR_KOOND_04/05).

    The tax-amount tolerances in KR_SM_04, KR_TK_3, KR_TTK_2, KR_KOP_03 and KR_TM_4 are not specified.

17. **Sections.**
    - Put all payments of a section in one entryHeader per file.
    - An empty section is `entryHeader` + `entryNumber` only.
    - `TSD_TK` and `TSD_TTK` are valid only in EE0201001.
    - In EE0201002 the only country role is RR_MAKSURESIDENTSUSE_RIIK.

18. **TSD01 status is contradictory.** It is "Jah" in SYNDMUS2017ap and conditional in Andmekoosseis. Every example payment has it, so always emit it on R/M.

19. **`TSD_SMVM` presence is ambiguous.**
    - KR_SM_01_1 says it is required whenever the type is subject to SM.
    - Ex-XML omits it on Tehing_21115 (foreign A1, no SM due) and Tehing_21117 (zero pay, KUUMSUURENDUS only).
    - The tax-type matrix (VALJAMLIIK2023ap / maatrikstabel, not covered here) is needed to decide.

20. **Previous-year changes.** After 15.02, previous-year cancellations or income-tax-changing corrections are refused (KR_XBRL_VALJAMAKSED_8 BLACK, KR_TM_2/KR_TM_5 RED), except for non-resident legal persons and third-country residents.

21. **Correcting a confirmed TSD.** The declaration must first be reopened in **e-MTA**; otherwise every file for that period is BLACK-refused. Put a PARANDUSPOHJUS_n in `entryComment` (one per section).

22. **Encoding and transport.**
    - UTF-8 (Estonian characters such as `õ` appear in `entriesComment`).
    - The example instances have no XML declaration and no BOM; the Guide p.20 snippet and all feedback files have `<?xml version="1.0" encoding="UTF-8"?>`.
    - Upload `.xml` or `.zip` (one XML), at most 50 MB.

23. **Feedback parsing.**
    - Expect both decimal comma (`241,45`, `106,04`) and dot (`1433.64`).
    - Expect leading spaces in codes.
    - Expect a missing `lineNumberCounter` on black errors.
    - Expect an `entryHeader` with no `entryNumber` for file-level errors.
    - With 2+ messages the download is a ZIP.

24. **Contradictory cardinalities in Tax. "XML väljade info".**
    - `lineNumber` is "Tingimuslik" (generic) but "Jah" (TSD).
    - `defaultCurrency` is "Tingimuslik" (generic) but "Jah" (TSD).
    - `entryComment` is "Tingimuslik" (generic) but "Valikuline" (TSD).
    - The TSD block says amount and documentDate are mandatory for "EE0102001, EE0102002"; this is a typo for EE0201001/EE0201002.
    - The second "measurableStartDateTime" row is really EndDateTime.

25. **Local XSD validation.** The `-apatsd.xsd` files `include`/`import` base files by their standard names, e.g. `../../cor/gl-cor-2015-03-25.xsd` and `gl-bus-content-2015-03-25.xsd`. Those are not in the folder. Per Guide §3, the XBRL GL 2015-03-25 package (gl-plt-2015-03-25.xsd) comes from `https://stat.ee/sites/default/files/2020-08/XBRL-GL-REC-2015-03-25.zip`.
