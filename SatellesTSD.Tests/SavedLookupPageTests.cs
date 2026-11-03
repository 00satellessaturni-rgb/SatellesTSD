using SatellesTSD.Infrastructure.Lookups;

namespace SatellesTSD.Tests;

/// <summary>Pages of the two registers the owner saved from a browser into the test-data folder's <c>lookups</c>
/// subfolder (outside the repository, as they may name real persons): every <c>emta-*.html</c> must read as a
/// residency answer and every <c>pensionikeskus-*.html</c> as a pension answer. A file named <c>…-found…</c> must
/// give at least one period and <c>…-notfound…</c> none; <c>…-joined…</c> a member with a rate on the day of saving,
/// <c>…-ended…</c> an end date, <c>…-notjoined…</c> not a member. Without the folder the tests pass with nothing checked.</summary>
public class SavedLookupPageTests
{
    private static IEnumerable<string> Pages(string prefix) =>
        RealDataTests.Folder is { } folder && Directory.Exists(Path.Combine(folder, "lookups"))
            ? Directory.GetFiles(Path.Combine(folder, "lookups"), prefix + "*.html")
            : [];

    [Fact]
    public void Every_saved_emta_page_reads_as_a_residency_answer()
    {
        foreach (var file in Pages("emta-"))
        {
            var name = Path.GetFileName(file);
            var result = EmtaResidencyParser.Parse(File.ReadAllText(file), DateOnly.FromDateTime(File.GetLastWriteTime(file)));
            Assert.True(result.Succeeded, $"{name}: {result.Failure}");
            if (name.Contains("notfound", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Empty(result.Finding!.Periods);
            }
            else if (name.Contains("found", StringComparison.OrdinalIgnoreCase))
            {
                Assert.NotEmpty(result.Finding!.Periods);
            }
        }
    }

    [Fact]
    public void Every_saved_pensionikeskus_page_reads_as_a_pension_answer()
    {
        foreach (var file in Pages("pensionikeskus-"))
        {
            var name = Path.GetFileName(file);
            var savedOn = DateOnly.FromDateTime(File.GetLastWriteTime(file));
            var result = PensionikeskusParser.Parse(File.ReadAllText(file));
            Assert.True(result.Succeeded, $"{name}: {result.Failure}");
            var finding = result.Finding!;
            if (name.Contains("notjoined", StringComparison.OrdinalIgnoreCase))
            {
                Assert.False(finding.MemberOn(savedOn), name);
            }
            else if (name.Contains("ended", StringComparison.OrdinalIgnoreCase))
            {
                Assert.NotNull(finding.ContributionsEnded);
            }
            else if (name.Contains("joined", StringComparison.OrdinalIgnoreCase))
            {
                Assert.True(finding.MemberOn(savedOn), name);
                Assert.NotNull(finding.RateOn(savedOn));
            }
        }
    }
}
