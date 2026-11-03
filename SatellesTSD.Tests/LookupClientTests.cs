using System.Net;
using SatellesTSD.Core.Lookups;
using SatellesTSD.Infrastructure.Lookups;

namespace SatellesTSD.Tests;

/// <summary>The two HTTP clients against a stub of each site: the token and cookie dances, the retries, the failures.</summary>
public class LookupClientTests
{
    /// <summary>A site in a box: answers by URL and method, remembers every request.</summary>
    private sealed class StubSite : HttpMessageHandler
    {
        public readonly List<(string Method, string Url, string? Body, string? Referer)> Requests = [];
        private readonly Func<HttpRequestMessage, string?, int, (HttpStatusCode Status, string Body)> _answer;

        public StubSite(Func<HttpRequestMessage, string?, int, (HttpStatusCode Status, string Body)> answer) => _answer = answer;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method.Method, request.RequestUri!.ToString(), body, request.Headers.Referrer?.ToString()));
            var (status, text) = _answer(request, body, Requests.Count);
            return new HttpResponseMessage(status) { Content = new StringContent(text, System.Text.Encoding.UTF8, "text/html") };
        }
    }

    private static HttpClient Client(StubSite site) => new(site) { Timeout = TimeSpan.FromSeconds(5) };

    [Fact]
    public async Task Emta_fetches_the_token_once_then_posts_the_form_as_the_workbook_did()
    {
        var site = new StubSite((request, body, _) => request.Method == HttpMethod.Get
            ? (HttpStatusCode.OK, LookupFixtures.EmtaForm)
            : (HttpStatusCode.OK, body!.Contains("CSRFToken=5e2a1c6e-6d55-4e3b-9c3a-7b1f0d2a9e11") ? LookupFixtures.EmtaFoundTwoPeriods : LookupFixtures.EmtaCsrfRefused));
        using var client = new EmtaResidencyClient(Client(site));

        var first = await client.QueryAsync(LookupFixtures.NobodysCode);
        var second = await client.QueryAsync("50001010006");

        Assert.True(first.Succeeded, first.Failure);
        Assert.Equal(2, first.Finding!.Periods.Count);
        Assert.True(second.Succeeded);
        Assert.Equal(["GET", "POST", "POST"], site.Requests.Select(r => r.Method)); // the token page once
        Assert.Equal(EmtaResidencyClient.FormUrl, site.Requests[0].Url);
        Assert.Equal(EmtaResidencyClient.QueryUrl, site.Requests[1].Url);
        Assert.Equal("personCode=50001010006&regCode=&vatNr=&p_submit=Otsi&CSRFToken=5e2a1c6e-6d55-4e3b-9c3a-7b1f0d2a9e11", site.Requests[1].Body);
        Assert.Equal(EmtaResidencyClient.FormUrl, site.Requests[1].Referer);
    }

    [Fact]
    public async Task Emta_fetches_the_token_again_when_the_site_refuses_it_and_gives_up_after_five_posts()
    {
        var posts = 0;
        var site = new StubSite((request, _, _) => request.Method == HttpMethod.Get
            ? (HttpStatusCode.OK, LookupFixtures.EmtaForm)
            : (HttpStatusCode.OK, ++posts < 3 ? LookupFixtures.EmtaCsrfRefused : LookupFixtures.EmtaNotFound));
        using var client = new EmtaResidencyClient(Client(site), new FastClock());

        var result = await client.QueryAsync(LookupFixtures.NobodysCode);

        Assert.True(result.Succeeded, result.Failure);
        Assert.Equal(["GET", "POST", "GET", "POST", "GET", "POST"], site.Requests.Select(r => r.Method));

        var stubborn = new StubSite((request, _, _) => request.Method == HttpMethod.Get ? (HttpStatusCode.OK, LookupFixtures.EmtaForm) : (HttpStatusCode.OK, LookupFixtures.EmtaCsrfRefused));
        using var client2 = new EmtaResidencyClient(Client(stubborn), new FastClock());
        var refused = await client2.QueryAsync(LookupFixtures.NobodysCode);
        Assert.False(refused.Succeeded);
        Assert.Contains("refused 5 times", refused.Failure);
        Assert.Equal(5, stubborn.Requests.Count(r => r.Method == "POST"));
    }

    [Fact]
    public async Task Emta_reports_an_http_failure_a_missing_token_and_a_site_that_does_not_answer()
    {
        var down = new StubSite((_, _, _) => (HttpStatusCode.ServiceUnavailable, "<html><body>Hooldus</body></html>"));
        using var client = new EmtaResidencyClient(Client(down));
        var result = await client.QueryAsync(LookupFixtures.NobodysCode);
        Assert.Contains("HTTP 503", result.Failure);

        var noToken = new StubSite((_, _, _) => (HttpStatusCode.OK, LookupFixtures.EmtaNotFound));
        using var client2 = new EmtaResidencyClient(Client(noToken));
        Assert.Contains("no CSRF token", (await client2.QueryAsync(LookupFixtures.NobodysCode)).Failure);

        var silent = new StubSite((_, _, _) => throw new HttpRequestException("No such host is known."));
        using var client3 = new EmtaResidencyClient(Client(silent));
        var unreachable = await client3.QueryAsync(LookupFixtures.NobodysCode);
        Assert.Contains("No such host", unreachable.Failure);
        Assert.StartsWith("request timeout or access denied", unreachable.Failure);
    }

    [Fact]
    public async Task Pensionikeskus_opens_the_home_page_once_for_the_session_then_queries_by_code()
    {
        var site = new StubSite((request, _, _) => request.RequestUri!.ToString() == PensionikeskusClient.HomeUrl
            ? (HttpStatusCode.OK, LookupFixtures.PensionHome)
            : (HttpStatusCode.OK, LookupFixtures.PensionObliged));
        using var client = new PensionikeskusClient(Client(site));

        var first = await client.QueryAsync(LookupFixtures.NobodysCode);
        var second = await client.QueryAsync(LookupFixtures.NobodysCode);

        Assert.True(first.Succeeded, first.Failure);
        Assert.Equal(PensionStatus.ObligedByBirthYear, first.Finding!.Status);
        Assert.True(second.Succeeded);
        Assert.Equal([PensionikeskusClient.HomeUrl, PensionikeskusClient.QueryUrl + "?personCode=50001010006", PensionikeskusClient.QueryUrl + "?personCode=50001010006"], site.Requests.Select(r => r.Url));
        Assert.Equal(PensionikeskusClient.HomeUrl, site.Requests[1].Referer);
    }

    [Fact]
    public async Task Pensionikeskus_renews_the_session_on_an_empty_answer_and_gives_up_after_five()
    {
        var queries = 0;
        var site = new StubSite((request, _, _) => request.RequestUri!.ToString() == PensionikeskusClient.HomeUrl
            ? (HttpStatusCode.OK, LookupFixtures.PensionHome)
            : (HttpStatusCode.OK, ++queries < 2 ? "" : LookupFixtures.PensionNotJoined));
        using var client = new PensionikeskusClient(Client(site), new FastClock());

        var result = await client.QueryAsync(LookupFixtures.NobodysCode);

        Assert.True(result.Succeeded, result.Failure);
        Assert.Equal(PensionStatus.NotJoined, result.Finding!.Status);
        Assert.Equal(["home", "query", "home", "query"], site.Requests.Select(r => r.Url == PensionikeskusClient.HomeUrl ? "home" : "query"));

        var empty = new StubSite((request, _, _) => (HttpStatusCode.OK, request.RequestUri!.ToString() == PensionikeskusClient.HomeUrl ? LookupFixtures.PensionHome : ""));
        using var client2 = new PensionikeskusClient(Client(empty), new FastClock());
        var gaveUp = await client2.QueryAsync(LookupFixtures.NobodysCode);
        Assert.Contains("empty answer 5 times", gaveUp.Failure);

        var forbidden = new StubSite((_, _, _) => (HttpStatusCode.Forbidden, "<html>blocked</html>"));
        using var client3 = new PensionikeskusClient(Client(forbidden));
        Assert.Contains("HTTP 403", (await client3.QueryAsync(LookupFixtures.NobodysCode)).Failure);
    }

    [Fact]
    public void The_shared_client_names_the_app_and_asks_in_estonian()
    {
        using var http = LookupHttp.Create(TimeSpan.FromSeconds(3));
        Assert.Equal(TimeSpan.FromSeconds(3), http.Timeout);
        Assert.Contains("SatellesTSD", http.DefaultRequestHeaders.UserAgent.ToString());
        Assert.StartsWith("et", http.DefaultRequestHeaders.AcceptLanguage.ToString());
    }

    /// <summary>A clock whose delays end at once but are remembered, so the retry tests do not wait the workbook's
    /// seconds yet prove they were asked for.</summary>
    private sealed class FastClock : TimeProvider
    {
        public readonly List<TimeSpan> Delays = [];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime > TimeSpan.Zero)
            {
                Delays.Add(dueTime);
            }

            return base.CreateTimer(callback, state, dueTime > TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : dueTime, period);
        }
    }

    [Fact]
    public async Task The_retries_wait_the_workbooks_second_between_tries()
    {
        var posts = 0;
        var site = new StubSite((request, _, _) => request.Method == HttpMethod.Get
            ? (HttpStatusCode.OK, LookupFixtures.EmtaForm)
            : (HttpStatusCode.OK, ++posts < 3 ? LookupFixtures.EmtaCsrfRefused : LookupFixtures.EmtaNotFound));
        var clock = new FastClock();
        using var client = new EmtaResidencyClient(Client(site), clock);
        await client.QueryAsync(LookupFixtures.NobodysCode);
        Assert.Equal([LookupHttp.RetryDelay, LookupHttp.RetryDelay], clock.Delays); // two refusals, two waits

        var queries = 0;
        var pension = new StubSite((request, _, _) => request.RequestUri!.ToString() == PensionikeskusClient.HomeUrl
            ? (HttpStatusCode.OK, LookupFixtures.PensionHome)
            : (HttpStatusCode.OK, ++queries < 2 ? "" : LookupFixtures.PensionNotJoined));
        var clock2 = new FastClock();
        using var client2 = new PensionikeskusClient(Client(pension), clock2);
        await client2.QueryAsync(LookupFixtures.NobodysCode);
        Assert.Equal([LookupHttp.RetryDelay], clock2.Delays);
        Assert.Equal(TimeSpan.FromSeconds(1), LookupHttp.RetryDelay);
    }
}
