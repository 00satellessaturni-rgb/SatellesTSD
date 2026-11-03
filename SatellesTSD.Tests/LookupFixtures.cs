namespace SatellesTSD.Tests;

/// <summary>Pages of the two registers for the parser and client tests: two captured live on 2026-11-04 with a
/// personal code that belongs to nobody (50001010006), the others shaped after the workbook's parsing code. No real
/// person is in any of them.</summary>
internal static class LookupFixtures
{
    public const string NobodysCode = "50001010006";

    /// <summary>EMTA, nobody found — captured.</summary>
    public const string EmtaNotFound = """
        <html><body><div id="wrapper"><div id="content-inner"> <h1>Mitteresidentsuse päring</h1> <div> <p>Otsingutulemustele vastavaid isikuid ei leitud. Isiku andmed puuduvad Maksu- ja Tolliameti andmebaasist või on ta sinna märgitud Eesti residendina.</p> <p><a class="icon-link" href="../nonresident"><span class="icon icon01"></span><span class="link">Tagasi otsingu lehele</span> </a> </p> </div> </div></div></body></html>
        """;

    /// <summary>EMTA, found: two periods, the second open — shaped after cEmployee.IsResident (a data row's first cell is empty).</summary>
    public const string EmtaFoundTwoPeriods = """
        <html><body><div id="content-inner"><h1>Mitteresidentsuse päring</h1><div>
          <p>Mitteresidentide registrist leitud isikud seisuga 04.11.2026</p>
          <div class="alt-block">
            <table>
              <thead><tr><th>Kood</th><th>Algus</th><th>Lõpp</th></tr></thead>
              <tbody>
                <tr><td></td><td> 01.01.2024 </td><td> 31.12.2025 </td></tr>
                <tr><td></td><td>01.03.2026</td><td></td></tr>
              </tbody>
            </table>
          </div>
          <div class="alt-block"><p>Residentsustõend: Soome, kehtiv 01.01.2026 - 31.12.2026</p></div>
        </div></div></body></html>
        """;

    /// <summary>EMTA, found, the block built of divs rather than a table.</summary>
    public const string EmtaFoundDivs = """
        <html><body><div id="content-inner"><h1>Mitteresidentsuse päring</h1><div>
          <p>Mitteresidentide registrist leitud isikud seisuga 04.11.2026</p>
          <div class="alt-block">
            <div><span>Kood</span><span>Algus</span><span>Lõpp</span></div>
            <div><span></span><span>15.06.2026</span><span></span></div>
          </div>
        </div></div></body></html>
        """;

    public const string EmtaFoundBadDate = """
        <html><body><div id="content-inner"><h1>Mitteresidentsuse päring</h1><div>
          <p>Mitteresidentide registrist leitud isikud seisuga 04.11.2026</p>
          <div class="alt-block"><table><tr><td></td><td>eile</td><td></td></tr></table></div>
        </div></div></body></html>
        """;

    public const string EmtaFoundWideRow = """
        <html><body><div id="content-inner"><h1>Mitteresidentsuse päring</h1><div>
          <p>Mitteresidentide registrist leitud isikud seisuga 04.11.2026</p>
          <div class="alt-block"><table><tr><td></td><td>01.01.2024</td><td>31.12.2025</td><td>Soome</td></tr></table></div>
        </div></div></body></html>
        """;

    public const string EmtaCsrfRefused = """
        <html><body><div id="content-inner"><p>Bad or missing CSRF value</p></div></body></html>
        """;

    public const string EmtaForm = """
        <html><body><div id="content-inner"><h1>Mitteresidentsuse päring</h1>
          <form action="/saqu/public/nonresident/query" method="post">
            <input type="text" name="personCode"><input type="text" name="regCode"><input type="text" name="vatNr">
            <input type="submit" name="p_submit" value="Otsi">
            <input type="hidden" name="CSRFToken" value="5e2a1c6e-6d55-4e3b-9c3a-7b1f0d2a9e11">
          </form></div></body></html>
        """;

    /// <summary>Pensionikeskus, obliged to join by birth year, one rate row — captured.</summary>
    public const string PensionObliged = """
        <html><body><div class="custom-main-col"> <h2 class="mb-4">Liitumiskontroll</h2> <form action="" method="POST"> <h4>Päring isikukoodiga "50001010006"</h4> <div class="mt-3 mb-2"><b>Vastus:</b></div> <div class="my-2">Isik on sünniaasta järgi kohustatud II sambaga liituma.</div><div class="font-weight-bold my-2">Maksete kinnipidamise algus: 01.01.2019</div> <div class="mt-4 mb-3"> Maksemäära info: </div> <table class="table mb-3"> <thead> <tr> <th>Periood</th> <th>Selgitus</th> <th>Määr</th> </tr> </thead> <tbody> <tr> <td> Alates 01.01.2019 </td> <td> Vaikimisi määr </td> <td>2%</td> </tr> </tbody> </table> <div class="my-2 text-secondary" style="font-size: 14px"> <div class="my-1"><b>Vaikimisi määr</b> — isiku kehtiv määr, mida ei ole maksemäära muutmise avalduse alusel muudetud.</div> </div><div class="text-nb my-2">1983. aastal ja hiljem sündinud isikutele on pensioni II sambaga liitumine kohustuslik. Nende isikute kontrollimisel annab päring vastuseks maksete kinnipidamise kohustuse alguskuupäeva.<br>Päring ei kontrolli isiku residentsust. Makse tegemise kohustus on AINULT Eesti residendist füüsilisel isikul.</div> <div class="row mb-3"> <div class="col-md-6"> <label for="personCode" class="form-label">Sisesta isikukood</label> <input type="text" id="personCode" name="personCode" value="50001010006" class="form-control"> </div> </div> <button class="btn btn-primary" type="submit">Kontrolli</button> </form> </div></body></html>
        """;

    /// <summary>Pensionikeskus, joined by application, the rate raised twice — shaped after the captured page.</summary>
    public const string PensionJoinedTwoRates = """
        <html><body><div class="custom-main-col"><h2>Liitumiskontroll</h2><form action="" method="POST">
          <h4>Päring isikukoodiga "37001010001"</h4>
          <div><b>Vastus:</b></div>
          <div class="my-2">Isik on kohustusliku kogumispensioniga liitunud.</div>
          <div class="font-weight-bold my-2">Maksete kinnipidamise algus: 01.09.2010</div>
          <div>Maksemäära info:</div>
          <table class="table mb-3"><thead><tr><th>Periood</th><th>Selgitus</th><th>Määr</th></tr></thead><tbody>
            <tr><td>01.01.2025 - 31.12.2025</td><td>Avalduse alusel</td><td>4%</td></tr>
            <tr><td>Alates 01.01.2026</td><td>Avalduse alusel</td><td>6%</td></tr>
            <tr><td>Kuni 31.12.2024</td><td>Vaikimisi määr</td><td>2%</td></tr>
          </tbody></table>
        </form></div></body></html>
        """;

    /// <summary>Pensionikeskus, contributions ended — shaped after the workbook's phrase.</summary>
    public const string PensionEnded = """
        <html><body><div class="custom-main-col"><form action="" method="POST">
          <h4>Päring isikukoodiga "49001010009"</h4>
          <div><b>Vastus:</b></div>
          <div class="my-2">Isik on kohustusliku kogumispensioniga liitunud.</div>
          <div class="font-weight-bold my-2">Alates 01.08.2026 on isiku kogumispensioni sissemaksed lõppenud.</div>
          <table class="table mb-3"><thead><tr><th>Periood</th><th>Selgitus</th><th>Määr</th></tr></thead><tbody>
            <tr><td>Alates 01.01.2019</td><td>Vaikimisi määr</td><td>2%</td></tr>
          </tbody></table>
        </form></div></body></html>
        """;

    public const string PensionNotJoined = """
        <html><body><div class="custom-main-col"><form action="" method="POST">
          <h4>Päring isikukoodiga "36001010002"</h4>
          <div><b>Vastus:</b></div>
          <div class="my-2">Isik ei ole liitunud kohustusliku kogumispensioniga.</div>
        </form></div></body></html>
        """;

    public const string PensionUnknownPage = """
        <html><body><div class="custom-main-col"><h2>Liitumiskontroll</h2><p>Teenus ei ole hetkel kättesaadav.</p></div></body></html>
        """;

    public const string PensionHome = """
        <html><head><title>Pensionikeskus</title></head><body><div class="custom-main-col">Tere</div></body></html>
        """;
}
