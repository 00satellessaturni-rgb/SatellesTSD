using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using AngleSharp.Html.Parser;
using SatellesTSD.Core.Lookups;

namespace SatellesTSD.Infrastructure.Lookups;

/// <summary>What both clients share: one HttpClient with a cookie jar, the app's own identity, Estonian first, and
/// the workbook's patience (five tries, a second apart).</summary>
public static class LookupHttp
{
    public static readonly string UserAgent = $"SatellesTSD/{ProgramVersion()} (accounting software; one query per person)"; // headers must be ASCII
    public const int Tries = 5;
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>The program's own version (the entry assembly's informational version without its build suffix), "0" in a
    /// test host; letters and digits only, as a header wants.</summary>
    private static string ProgramVersion()
    {
        var version = System.Reflection.Assembly.GetEntryAssembly()?.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0";
        var plus = version.IndexOf('+');
        version = plus >= 0 ? version[..plus] : version;
        var ascii = new string(version.Where(c => c < 128 && !char.IsWhiteSpace(c)).ToArray());
        return ascii.Length == 0 ? "0" : ascii;
    }

    /// <summary>A client of its own, with a cookie jar, for one site.</summary>
    public static HttpClient Create(TimeSpan? timeout = null)
    {
        var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), UseCookies = true, AllowAutoRedirect = true, AutomaticDecompression = DecompressionMethods.All };
        var client = new HttpClient(handler) { Timeout = timeout ?? Timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("et,en;q=0.5");
        return client;
    }

    internal static string Reason(Exception e) => e switch
    {
        TaskCanceledException => "request timeout or access denied: the site did not answer in time",
        HttpRequestException h => $"request timeout or access denied: {h.Message}",
        _ => $"request timeout or access denied: {e.Message}",
    };
}

/// <summary>EMTA's public non-resident query (apps.emta.ee/saqu/public/nonresident): the form page gives a session
/// cookie and a CSRF token, the query is a form post with them; a refused token is fetched again, as the workbook
/// did, up to five times.</summary>
public sealed class EmtaResidencyClient : IResidencyLookup, IDisposable
{
    public const string FormUrl = "https://apps.emta.ee/saqu/public/nonresident/";
    public const string QueryUrl = "https://apps.emta.ee/saqu/public/nonresident/query";
    public const string CsrfRefusal = "Bad or missing CSRF value";

    private static readonly HtmlParser Parser = new();
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly TimeProvider _clock;
    private string? _token;

    public EmtaResidencyClient(HttpClient http, TimeProvider? clock = null, bool ownsClient = false)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _clock = clock ?? TimeProvider.System;
        _ownsClient = ownsClient;
    }

    public static EmtaResidencyClient Create() => new(LookupHttp.Create(), ownsClient: true);

    public async Task<LookupResult<ResidencyFinding>> QueryAsync(string personalCode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(personalCode);
        try
        {
            for (var attempt = 1; attempt <= LookupHttp.Tries; attempt++)
            {
                if (_token is null)
                {
                    var failure = await FetchTokenAsync(cancellationToken).ConfigureAwait(false);
                    if (failure is not null)
                    {
                        return LookupResult<ResidencyFinding>.Failed(failure);
                    }
                }

                using var request = new HttpRequestMessage(HttpMethod.Post, QueryUrl)
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["personCode"] = personalCode.Trim(),
                        ["regCode"] = "",
                        ["vatNr"] = "",
                        ["p_submit"] = "Otsi",
                        ["CSRFToken"] = _token!,
                    }),
                };
                request.Headers.Referrer = new Uri(FormUrl);
                using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (body.Contains(CsrfRefusal, StringComparison.Ordinal))
                {
                    _token = null;
                    await Task.Delay(LookupHttp.RetryDelay, _clock, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if ((int)response.StatusCode >= 400)
                {
                    return LookupResult<ResidencyFinding>.Failed($"request timeout or access denied: HTTP {(int)response.StatusCode}");
                }

                return EmtaResidencyParser.Parse(body, DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime));
            }

            return LookupResult<ResidencyFinding>.Failed($"request timeout or access denied: the CSRF token was refused {LookupHttp.Tries} times");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return LookupResult<ResidencyFinding>.Failed(LookupHttp.Reason(e));
        }
    }

    /// <summary>GETs the form page: the session cookie lands in the jar, the token in the form.</summary>
    private async Task<string?> FetchTokenAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(FormUrl, cancellationToken).ConfigureAwait(false);
        if ((int)response.StatusCode >= 400)
        {
            return $"request timeout or access denied: HTTP {(int)response.StatusCode} on the form page";
        }

        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var token = Parser.ParseDocument(html).QuerySelector("input[name=CSRFToken]")?.GetAttribute("value");
        if (string.IsNullOrWhiteSpace(token))
        {
            return "request timeout or access denied: the form page carries no CSRF token";
        }

        _token = token;
        return null;
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }
}

/// <summary>Pensionikeskus's membership check (liitumiskontroll): the home page first, for the site's session
/// cookie, then the query by personal code; an empty answer means the session is gone — the cookie is fetched again,
/// as the workbook did, up to five times.</summary>
public sealed class PensionikeskusClient : IPensionLookup, IDisposable
{
    public const string HomeUrl = "https://www.pensionikeskus.ee/";
    public const string QueryUrl = "https://www.pensionikeskus.ee/raamatupidajale/ii-sammas/liitumiskontroll/";

    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly TimeProvider _clock;
    private bool _sessionReady;

    public PensionikeskusClient(HttpClient http, TimeProvider? clock = null, bool ownsClient = false)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _clock = clock ?? TimeProvider.System;
        _ownsClient = ownsClient;
    }

    public static PensionikeskusClient Create() => new(LookupHttp.Create(), ownsClient: true);

    public async Task<LookupResult<PensionFinding>> QueryAsync(string personalCode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(personalCode);
        try
        {
            for (var attempt = 1; attempt <= LookupHttp.Tries; attempt++)
            {
                if (!_sessionReady)
                {
                    using var home = await _http.GetAsync(HomeUrl, cancellationToken).ConfigureAwait(false);
                    if ((int)home.StatusCode >= 400)
                    {
                        return LookupResult<PensionFinding>.Failed($"request timeout or access denied: HTTP {(int)home.StatusCode} on the home page");
                    }

                    _sessionReady = true;
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, QueryUrl + "?personCode=" + Uri.EscapeDataString(personalCode.Trim()));
                request.Headers.Referrer = new Uri(HomeUrl);
                using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if ((int)response.StatusCode >= 400)
                {
                    return LookupResult<PensionFinding>.Failed($"request timeout or access denied: HTTP {(int)response.StatusCode}");
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(body))
                {
                    _sessionReady = false;
                    await Task.Delay(LookupHttp.RetryDelay, _clock, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                return PensionikeskusParser.Parse(body);
            }

            return LookupResult<PensionFinding>.Failed($"request timeout or access denied: an empty answer {LookupHttp.Tries} times");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return LookupResult<PensionFinding>.Failed(LookupHttp.Reason(e));
        }
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }
}
