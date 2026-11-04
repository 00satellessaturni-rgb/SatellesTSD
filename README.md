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
3. Update: close the app, unpack the newer `SatellesTSD-v<version>.zip` beside the older folder and point
   the shortcut at the new folder's `SatellesTSD.exe` (the old folder can then go). The store and the
   settings live outside the folder, so nothing is lost. A store opened by a newer version cannot be
   opened by an older one.
4. If the window ever fails, it writes `%AppData%\SatellesTSD\viga.log`; send that file along.

Command-line switches: `--store <file>` opens that store instead of the remembered one; `--settings <file>`
reads and writes the settings there instead of `%AppData%` (both take a path after them).

## A month, step by step

1. **Ettevõte** and **Periood**: pick the company (or **Uus ettevõte…**) and the payment month.
2. **Impordi failid…**: the TÖR export (`search-export…`) and every bank statement of the month
   (CSV or the bank's Excel; a file imported before is found again, never stored twice). **Failid…** lists
   what the period uses.
3. **Otsi maksed**: the search over the statements — the persons grid (the Vahearuanne) and, per person,
   the payments found (the Otsingu kontroll) with the category, the payment type and whether it is declared.
4. **Kontrolli registrid**: residency from EMTA, II-pillar membership and rate from Pensionikeskus, one
   person at a time; the person panel shows what was found and lets you confirm or set the facts by hand.
5. Review: change a payment's person, category, type or declaration in the grid, add a payment by hand
   (**Lisa rida…**), fix a person's facts in the panel (the exemption, the social-tax minimum). The grid's
   checks say what would be refused.
6. **Deklaratsioon › Koosta read**: the declared lines with their taxes, one per payment, plus the zero
   line for a valid employment without pay. Build again after every change; it reconciles in place.
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
beside the exe, and puts `release\SatellesTSD-v<version>\` with a `release.txt` (version, commit, SHA-256) and
a zip next to it. Raise `<Version>` before every release; a folder already there for the version is refused
unless `-Force`. `-OutputFolder` sends it elsewhere. `-AllowDirty` is for trying the script only: the output
is then named `-dirty` and says so, never the release of its commit.
