using System.Globalization;
using ClosedXML.Excel;
using SatellesTSD.Core.Declare;
using SatellesTSD.Core.Import;
using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Review;
using SatellesTSD.Core.Store;
using SatellesTSD.Core.Tax;

namespace SatellesTSD.Infrastructure;

/// <summary>Runs a simulation (decision 204) the way the app runs a month, on a store of its own in memory that is
/// thrown away after: the example's register and bank rows written as a workbook and imported through the real
/// readers, the search run, the person's card saved with the residency and the pension confirmed by hand, the review's
/// word on each row put, the lines built, and the file written and checked against the schemas in a folder of its own.
/// The store in use is never opened.</summary>
public static class Simulator
{
    /// <summary>The company the example is declared for: no real company's code (the employer the register names).</summary>
    public const string RegistryCode = "10000000";
    public const string CompanyName = "Simulatsioon OÜ";

    /// <summary>The stand-in employee of a register that does not list the example person.</summary>
    public const string StandInCode = "39001010008";

    /// <summary>The stand-in's code when the example person's own is <see cref="StandInCode"/>: the register must not list
    /// them.</summary>
    public const string OtherStandInCode = "39001010022";

    private const string Account = "EE000000000000000000";
    private const string LhvLayout = "lhv-et-short";

    /// <param name="schemaRoot">The APA TSD schemas' folder the file is checked against (as the app's own export); null
    /// leaves the check out.</param>
    /// <param name="savedCatalog">The store in use's saved catalog document (the owner's phrases, decision 202), so the
    /// search finds the rows as the app's own does; null = the built-in catalog.</param>
    public static SimResult Run(SimScenario scenario, string? schemaRoot = null, TimeProvider? clock = null, string? savedCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        IReadOnlyList<MatchProblem> runProblems = [];
        using var store = SqliteStore.InMemory();
        if (savedCatalog is not null)
        {
            store.SaveMatchCatalog(savedCatalog);
        }

        var ownPhrases = savedCatalog is not null;
        var rates = TaxRates.For(scenario.Year);
        var company = store.AddCompany(RegistryCode, CompanyName);
        var period = store.GetOrCreatePeriod(company.Id, scenario.Year, scenario.Month);
        var pipeline = new PeriodPipeline(store, new Importer(LayoutCatalog.Default, new ExcelTableReader()), clock: clock);

        var bytes = Workbook(scenario);
        var imported = pipeline.ImportFile(period.Id, "simulatsioon.xlsx", bytes);
        var notes = imported.Notes; // the import's own word, worded by the window as the import log words it
        if (!imported.Succeeded)
        {
            return new SimResult { ImportNotes = notes, RunProblems = runProblems, OwnPhrases = ownPhrases, ImportStopped = true, Refusal = imported.Error, RefusalError = imported.Refusal };
        }

        try
        {
            var run = pipeline.RunMatch(period.Id);
            runProblems = run.Problems; // by code: the window words them as the "Otsi maksed" log does
        }
        catch (Exception e) when (e is MatchException or InvalidOperationException)
        {
            return new SimResult { ImportNotes = notes, RunProblems = runProblems, OwnPhrases = ownPhrases, Refusal = e.Message, RefusalError = e };
        }

        var workspace = new ReviewWorkspace(store, rates, clock) { TwoContracts = scenario.TwoContracts };
        Person person;
        try
        {
            person = Review(scenario, store, workspace, company.Id, period.Id);
        }
        catch (Exception e) when (e is ReviewException or ArgumentException or InvalidOperationException)
        {
            // the card or a row's word refused as the review refuses it: said, nothing built
            return new SimResult { ImportNotes = notes, RunProblems = runProblems, OwnPhrases = ownPhrases, Refusal = e.Message, RefusalError = e };
        }

        var declarer = new PeriodDeclarer(store, rates, clock) { TwoContracts = scenario.TwoContracts };
        BuildReport report;
        try
        {
            report = declarer.Build(period.Id, scenario.Mode);
        }
        catch (Exception e) when (e is DeclareException or ReviewException or InvalidOperationException)
        {
            return new SimResult { ImportNotes = notes, RunProblems = runProblems, OwnPhrases = ownPhrases, Refusal = e.Message, RefusalError = e, Review = ReviewOf(workspace, period.Id, person.Id) };
        }

        var lines = declarer.Lines(period.Id).Where(l => l.Payment.PersonId == person.Id).Select(l => l.Payment).ToList();
        string? xml = null;
        Exception? xmlRefusal = null;
        if (lines.Count > 0)
        {
            var folder = Path.Combine(Path.GetTempPath(), "SatellesTSD-simulatsioon-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(folder);
                var exported = new PeriodSubmitter(store, schemaRoot, clock).Export(period.Id, folder);
                xml = File.ReadAllText(exported.Path);
            }
            catch (Exception e) when (e is DeclareException or InvalidOperationException or IOException or ArgumentException)
            {
                xmlRefusal = e;
            }
            finally
            {
                try
                {
                    Directory.Delete(folder, true);
                }
                catch (IOException)
                {
                    // a temporary folder left behind is no failure of the simulation
                }
            }
        }

        return new SimResult
        {
            ImportNotes = notes,
            RunProblems = runProblems,
            OwnPhrases = ownPhrases,
            Review = ReviewOf(workspace, period.Id, person.Id),
            Build = report.Persons.FirstOrDefault(p => p.PersonId == person.Id),
            BuildNotes = report.Notes,
            Lines = lines,
            Totals = DeclarationTotals.Of(lines),
            Xml = xml,
            XmlRefusal = xmlRefusal?.Message,
            XmlRefusalError = xmlRefusal,
        };
    }

    /// <summary>The review of the example: the person found or added, every bank row given to them, the card saved with the
    /// residency and the pension confirmed by hand, the review's own word on each row put.</summary>
    private static Person Review(SimScenario scenario, SqliteStore store, ReviewWorkspace workspace, Guid companyId, Guid periodId)
    {
        var view = workspace.Load(periodId);
        var card = scenario.Person;
        var person = Find(store, companyId, card)
            ?? workspace.AddPerson(companyId, card.FirstName, card.LastName, Blank(card.PersonalCode), Blank(card.TorTypeId), card.Residency);

        // every bank row of the example is the person's: a row the search did not give them is given by hand, as the
        // accountant gives a row of a person the register does not list (decision 176)
        foreach (var payment in view.Persons.Where(p => p.PersonId != person.Id).SelectMany(p => p.Payments).Where(p => p.RowId is not null))
        {
            workspace.SetPerson(payment, person.Id);
        }

        var stored = store.Persons(companyId).Single(p => p.Id == person.Id);
        workspace.SavePerson(stored with
        {
            FirstName = card.FirstName.Trim(),
            LastName = card.LastName.Trim(),
            PersonalCode = Blank(card.PersonalCode),
            MrrCode = Blank(card.MrrCode),
            Residency = card.Residency,
            ResidenceCountry = Blank(card.ResidenceCountry),
            FundedPensionMember = card.FundedPensionMember,
            PensionRate = card.FundedPensionMember == true ? card.PensionRate : null,
            OnOldAgeOrFlexiblePension = card.OnOldAgeOrFlexiblePension,
            PensionAgeYear = card.PensionAgeYear,
            BasicExemptionApplied = card.BasicExemptionApplied,
            BasicExemptionMonthly = card.BasicExemptionApplied ? card.BasicExemptionMonthly : null,
            SocialTaxMinimumApplies = card.SocialTaxMinimumApplies,
            ReducedWorkAbility = card.ReducedWorkAbility,
            StatePaysSocialTax = card.ReducedWorkAbility && card.StatePaysSocialTax,
            A1 = card.A1,
            A1Country = Blank(card.A1Country),
            WorkCountry = Blank(card.WorkCountry),
            TreatyIncomeTaxRate = card.TreatyIncomeTaxRate,
            TorEmploymentTypeId = Blank(card.TorTypeId) ?? stored.TorEmploymentTypeId,
            TypeFromCard = card.TypeFromCard,
            Declaration = card.Declaration,
        }, confirmResidency: true, confirmPension: card.FundedPensionMember is not null, presented: stored);

        // the review's own word on a row, over the search's
        var payments = Ours(workspace, periodId, person.Id);
        for (var i = 0; i < scenario.Rows.Count; i++)
        {
            var row = scenario.Rows[i];
            if (payments.FirstOrDefault(p => p.RowNo == i + 2) is not { } payment)
            {
                continue;
            }

            if (row.Category is { } category)
            {
                workspace.SetCategory(payment, category);
                payment = Ours(workspace, periodId, person.Id).First(p => p.RowNo == i + 2);
            }

            if (row.TypeCode is { } typeCode)
            {
                workspace.SetTypeCode(payment, typeCode);
                payment = Ours(workspace, periodId, person.Id).First(p => p.RowNo == i + 2);
            }

            if (row.Declare is { } declare)
            {
                workspace.SetDeclare(payment, declare);
            }
        }

        return person;
    }

    /// <summary>The example as the workbook the app would be handed: the register's sheet (unless the month has none) and
    /// an LHV statement of the bank rows, the person's code beside each of them.</summary>
    public static byte[] Workbook(SimScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        using var workbook = new XLWorkbook();
        if (scenario.Register != SimRegister.NoRegister)
        {
            var tor = workbook.AddWorksheet("Töötajate loend");
            for (var c = 0; c < TorHeaders.All.Count; c++)
            {
                tor.Cell(1, c + 1).Value = TorHeaders.All[c];
            }

            var person = scenario.Person;
            var entries = scenario.Register == SimRegister.Listed
                ? scenario.Employments.Select(e => (Code: Blank(person.PersonalCode), Last: person.LastName, First: person.FirstName, Employment: e)).ToList()
                : [(Blank(person.PersonalCode) == StandInCode ? OtherStandInCode : StandInCode, "TEINE", "TÖÖTAJA", new SimEmployment("1", new DateOnly(scenario.Year, scenario.Month, 1).AddYears(-1)))];
            for (var i = 0; i < entries.Count; i++)
            {
                var (code, last, first, employment) = entries[i];
                var r = i + 2;
                void Set(string header, object? value) => Put(tor.Cell(r, Column(header)), value);
                var ended = employment.End is not null;
                var paused = !ended && employment.PauseStart is not null;
                Set(TorHeaders.TorId, (i + 1).ToString(CultureInfo.InvariantCulture));
                Set(TorHeaders.PersonalCode, code is not null && code.All(char.IsAsciiDigit) ? double.Parse(code, CultureInfo.InvariantCulture) : code);
                Set(TorHeaders.LastName, last.Trim().ToUpperInvariant());
                Set(TorHeaders.FirstName, first.Trim().ToUpperInvariant());
                Set(TorHeaders.StartDate, employment.Start);
                Set(TorHeaders.TypeId, int.TryParse(employment.TypeId, NumberStyles.None, CultureInfo.InvariantCulture, out var typeId) ? typeId : employment.TypeId);
                Set(TorHeaders.Type, EmploymentTypes.Find(employment.TypeId)?.Name ?? "Muu");
                Set(TorHeaders.StateId, ended ? 4 : paused ? 3 : 2);
                Set(TorHeaders.State, ended ? "Lõpetatud" : paused ? "Peatatud" : "Kehtiv");
                Set(TorHeaders.EndDate, employment.End);
                Set(TorHeaders.Country, "EE");
                Set(TorHeaders.PauseStart, employment.PauseStart);
                Set(TorHeaders.PauseReason, employment.PauseStart is null ? null : "Peatamine");
                Set(TorHeaders.PauseEnd, employment.PauseStart is null ? null : employment.PauseEnd);
                Set(TorHeaders.EmployerCode, double.Parse(RegistryCode, CultureInfo.InvariantCulture));
                Set(TorHeaders.EmployerName, CompanyName);
            }
        }

        var headers = LayoutCatalog.Default.Find(LhvLayout)!.Headers;
        var bank = workbook.AddWorksheet("LHV");
        for (var c = 0; c < headers.Count; c++)
        {
            bank.Cell(1, c + 1).Value = headers[c];
        }

        int BankColumn(string header) => headers.ToList().IndexOf(header) + 1;
        for (var i = 0; i < scenario.Rows.Count; i++)
        {
            var row = scenario.Rows[i];
            var r = i + 2;
            void Set(string header, object? value) => Put(bank.Cell(r, BankColumn(header)), value);
            Set("Kliendi konto", Account);
            Set("Dokumendi number", i + 1);
            Set("Kuupäev", row.Date);
            Set("Saaja/maksja konto", "EE00" + (i + 1).ToString("D16", CultureInfo.InvariantCulture));
            Set("Saaja/maksja nimi", row.Payee);
            Set("Deebet/Kreedit (D/C)", row.MoneyIn ? "C" : "D");
            Set("Summa", (double)(row.MoneyIn ? Math.Abs(row.Amount) : -Math.Abs(row.Amount)));
            Set("Selgitus", row.Description);
            Set("Valuuta", string.IsNullOrWhiteSpace(row.Currency) ? "EUR" : row.Currency.Trim().ToUpperInvariant());
            Set("Isikukood või registrikood", Blank(scenario.Person.PersonalCode) is { } code && code.All(char.IsAsciiDigit) ? double.Parse(code, CultureInfo.InvariantCulture) : Blank(scenario.Person.PersonalCode));
            Set("Kande viide", $"SIM{scenario.Year:D4}{scenario.Month:D2}{i + 1:D6}");
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static int Column(string header) => TorHeaders.All.ToList().IndexOf(header) + 1;

    private static void Put(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                break;
            case string s:
                cell.Value = s;
                break;
            case double d:
                cell.Value = d;
                break;
            case int n:
                cell.Value = n;
                break;
            case DateOnly day:
                cell.Value = day.ToDateTime(TimeOnly.MinValue);
                cell.Style.DateFormat.Format = "dd.MM.yyyy";
                break;
            default:
                cell.Value = Convert.ToString(value, CultureInfo.InvariantCulture);
                break;
        }
    }

    private static Person? Find(SqliteStore store, Guid companyId, SimPerson card) =>
        Blank(card.PersonalCode) is { } code
            ? store.FindPerson(companyId, code)
            : store.Persons(companyId).FirstOrDefault(p => string.Equals(p.LastName, card.LastName.Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(p.FirstName, card.FirstName.Trim(), StringComparison.OrdinalIgnoreCase));

    private static List<ReviewPayment> Ours(ReviewWorkspace workspace, Guid periodId, Guid personId) =>
        workspace.Load(periodId).Persons.Where(p => p.PersonId == personId).SelectMany(p => p.Payments).ToList();

    private static ReviewPerson? ReviewOf(ReviewWorkspace workspace, Guid periodId, Guid personId) =>
        workspace.Load(periodId).Persons.FirstOrDefault(p => p.PersonId == personId);

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
