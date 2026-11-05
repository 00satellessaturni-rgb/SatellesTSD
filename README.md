# SatellesTSD

A Windows desktop app for an accounting firm's monthly **andmepõhine TSD** (APA TSD): it reads the TÖR
export and the bank statements of a company, finds the salary payments per employee, lets the accountant
review them, computes the taxes, and writes the XBRL GL XML file for e-MTA — the work the firm's Excel macro
workbook did for the old Lisa 1 / Lisa 2 CSVs, for the regime that took over on 1 October 2026.

The design and every decision made while building it are in [docs/SatellesTSD-plan.md](docs/SatellesTSD-plan.md);
the reference material (the XBRL GL format, the payment-type mapping, the submission channels, the workbook's
VBA) is in [docs/reference](docs/reference).

## Requirements

| On the PC | Why |
|---|---|
| Windows 10 / 11, 64-bit | The release is a self-contained win-x64 build: no .NET runtime to install. |
| A folder for the store file | One SQLite file per firm, chosen on the first run; one user at a time. |
| Internet, for the register checks | EMTA's residency form and Pensionikeskus's membership page, one person per query. |
| An e-MTA account with the client's representation right | The XML file is uploaded by hand in e-MTA (X-tee comes later). |

## Install and update

1. Unpack `SatellesTSD-v<version>.zip` (or copy the folder) anywhere the user can write — not a network
   drive, not a build folder. Run `SatellesTSD.exe`.
2. First run: the window asks for the store file. Cancel the picker and use **Uus andmebaas…** to create one
   (the suggested name is `SatellesTSD.db`); put it in a folder that is backed up. The choice is remembered
   in `%AppData%\SatellesTSD\settings.json`, with the company and the month last shown.
3. Updates come by themselves. At every start the program looks in the release folder on the office NAS
   (`S:\2. DOKUMENDID JA FAILID\IT\SatellesTsdUpdate`); when a newer version is there, the title says so
   ("SatellesTSD v0.10.0 — saadaval v0.11.0") and the Andmebaas menu's item reads "Uuenda v0.11.0…".
   The update window also opens by itself once, while the window is idle; there Enter means "Hiljem". The
   update copies the new version beside the program, checks it (size, checksum, that it is this program at
   that version, with its schemas), swaps the exe and the schemas folder in, starts the new version and closes
   the old one. The store and the settings are untouched; before the first upgrade of the store's own format
   a copy is left beside it (`firma.db.before-v<N>`). Should the new version not answer within a minute, the
   window asks whether to wait or to put the previous version back. "Kontrolli uuendusi" asks the folder again.
   Not offered for a copy run from a build folder or from a network drive (replace those by hand), and other
   open windows keep the old version until closed. A text file `SatellesTSD.update-folder.txt` beside the exe,
   naming another folder on its first line, is for trying a release on one PC.
4. If the window ever fails, it writes `%AppData%\SatellesTSD\viga.log`; send that file along.

Command-line switches: `--store <file>` opens that store instead of the remembered one; `--settings <file>`
reads and writes the settings there instead of `%AppData%` (both take a path after them).

## A month, step by step

The commands sit in the menus — **Andmebaas** (the store, the updates, Sulge), **Ettevõte**, **Periood**
(import, search, the register lookups, Katkesta), **Isik** (a new person, a line by hand), **Tööriistad**
(the report, the calculator); the toolbar keeps the company and the month. Every table sorts on a header
click and has an **Otsi:** box that hides the rows without the text. The payments table has tabs: the
person picked, every payment of the month, one per category, and the payments without a category.

1. **Periood**: pick the payment month (the previous month is offered). The company need not be picked
   when the files hold the TÖR export: it names the employer, and the program takes the company up or
   creates it. **Ettevõte** and **Uus ettevõte…** are for a month without a TÖR export. **Sulge
   ettevõte** puts the company shown away, so that the next files go to their own employer without a
   question; the choice holds until a company is picked again.
2. **Impordi failid…**: the TÖR export (`search-export…`) and every bank statement of the month
   (CSV or the bank's Excel; a file imported before is found again, never stored twice). When the export
   names another employer than the company shown, the program asks before switching to it. **Failid…**
   lists what the period uses.
3. **Otsi maksed**: the search over the statements — the persons grid (the Vahearuanne) and, per person,
   the payments found (the Otsingu kontroll) with the category, the payment type and whether it is declared.
   A statement row without a payee name (a fee, a card payment, a balance line) is listed too, under
   "— leidmata read —" with no category, so that every transaction is in front of you; it is never
   searched for a person, but you can name one.
4. **Kontrolli registrid**: residency from EMTA, II-pillar membership and rate from Pensionikeskus, one
   person at a time; the person panel shows what was found and lets you confirm or set the facts by hand.
5. Review: change a payment's person, category, type or declaration in the grid, add a payment by hand
   (**Lisa rida…**), fix a person's facts in the panel (the exemption, the social-tax minimum). The grid's
   checks say what would be refused.
6. **Deklaratsioon › Koosta read**: the declared lines with their taxes, one per payment, plus the zero
   line for a valid employment without pay. Build again after every change; it reconciles in place.
   The **Seaded** menu chooses the cut, and remembers it: one line per payment (the default), or one
   line per person and payment type with the month's payments added up and dated by the latest of
   them. Once a month's lines are on the server in one cut, the other is refused for that month.
7. **Ekspordi XML…**: one file for the month into the folder you pick; the checklist that follows says
   how to upload it in e-MTA (the same payment month; a confirmed declaration opened for changes first).
8. In e-MTA: upload the file, wait for the processing, read the outcome per line. Here: **Märgi üles
   laaditud**, then **Loe tagasiside…** with the feedback report if e-MTA gives it, or **Kinnita rida
   käsitsi** / **Lükka rida tagasi** per line. **Ajalugu…** shows every version of a key, when a sent
   line hides beneath a later draft.
9. **Märgi kinnitatuks** once the declaration is confirmed in e-MTA. A correction later: **Ava
   parandamiseks**, edit, **Koosta read** (M and T lines on the same keys), **Ekspordi XML…** with the
   PARANDUSPOHJUS.
10. **Ekspordi aruanne…**: the report-details workbook (Vahearuanne with its sub-tables, TOR kontroll,
    Otsingu kontroll, the category sheets, a copy of every statement with "Vaste leitud", the declared
    lines) for the file.

Until 31 December 2027 the old Lisa 1 / Lisa 2 CSV upload still exists in e-MTA; the first months are run
in both, and the figures compared.

## Build

- .NET 10 SDK; Visual Studio 2026 or `dotnet` alone.
- `dotnet build SatellesTSD.sln` builds the app (`SatellesTSD`, WinForms), the core (`SatellesTSD.Core`:
  the tax engine, the XBRL GL writer, the imports, the matching, the review, the declaring, the report),
  the infrastructure (`SatellesTSD.Infrastructure`: the SQLite store, the Excel reader and writer, the
  register clients) and the two test projects.
- `dotnet test SatellesTSD.sln` runs the core tests (`SatellesTSD.Tests`, no window) and the window tests
  (`SatellesTSD.WindowTests`, which open the real window on a temporary store — they need a desktop session).
- The real test data (statements and reports of real companies) lives outside the repository and is never
  committed; the real-data tests pass vacuously without it.

## Publish a release

```powershell
powershell -ExecutionPolicy Bypass -File tools\Publish-Release.ps1
```

(Windows PowerShell's default policy does not run scripts; the switch allows this one run.) The script refuses
a dirty or unpushed tree, reads the version from `SatellesTSD\SatellesTSD.csproj` (`<Version>`, the one the
window title shows), publishes a self-contained win-x64 single-file build with the `schemas\apa-tsd` folder
beside the exe, puts `release\SatellesTSD-v<version>\` with a `release.txt` (version, commit, SHA-256) and a
zip next to it, and then sends the zip to the release folder on the NAS with a `latest.txt` naming it, written
last. Every copy of the program offers the new version at its next start. Raise `<Version>` before every
release; a version not newer than the one on offer is refused. `-LocalOnly` stops before the NAS;
`-ReleaseFolder` names another folder (the usual one is the constant in `Updater.cs`); `-Withdraw` puts the
previous release back on offer; `-OutputFolder` sends the local output elsewhere. `-AllowDirty` is for trying
the script only: the output is then named `-dirty` and says so, and never goes to the NAS.
