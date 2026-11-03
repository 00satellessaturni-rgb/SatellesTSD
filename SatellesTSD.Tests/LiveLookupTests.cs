using SatellesTSD.Core.Lookups;
using SatellesTSD.Infrastructure.Lookups;

namespace SatellesTSD.Tests;

/// <summary>The two real sites, asked about a personal code that belongs to nobody — proof that the cookie and token
/// paths still work against the sites as they are today. Opt-in: set <c>SATELLESTSD_LIVE_LOOKUPS=1</c>; without it
/// these pass with nothing checked, so the suite never depends on the network.</summary>
public class LiveLookupTests
{
    private static bool Live => Environment.GetEnvironmentVariable("SATELLESTSD_LIVE_LOOKUPS") == "1";

    [Fact]
    public async Task Emta_answers_that_nobody_was_found()
    {
        if (!Live)
        {
            return;
        }

        using var client = EmtaResidencyClient.Create();
        var result = await client.QueryAsync(LookupFixtures.NobodysCode);

        Assert.True(result.Succeeded, result.Failure);
        Assert.Empty(result.Finding!.Periods);
        Assert.Contains("ei leitud", result.Page);
    }

    [Fact]
    public async Task Pensionikeskus_answers_obliged_by_birth_year_with_the_default_rate()
    {
        if (!Live)
        {
            return;
        }

        using var client = PensionikeskusClient.Create();
        var result = await client.QueryAsync(LookupFixtures.NobodysCode);

        Assert.True(result.Succeeded, result.Failure);
        Assert.Equal(PensionStatus.ObligedByBirthYear, result.Finding!.Status);
        Assert.Equal(new DateOnly(2019, 1, 1), result.Finding.WithholdingStart);
        Assert.Equal(0.02m, result.Finding.RateOn(new DateOnly(2026, 10, 31)));
    }
}
