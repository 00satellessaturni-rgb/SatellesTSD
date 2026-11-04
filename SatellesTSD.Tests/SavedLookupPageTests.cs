using SatellesTSD.Infrastructure.Lookups;

namespace SatellesTSD.Tests;

/// <summary>Pages of the two registers the owner saved from a browser anywhere under the test-data folder (outside
/// the repository, as they may name real persons), as <c>.htm</c> or <c>.html</c>: every <c>emta-*</c> must read
/// as a residency answer and every <c>pensionikeskus-*</c> as a pension answer. A file named <c>…-found…</c> must
/// give at least one period and <c>…-notfound…</c> none; <c>…-joined…</c> a member with a rate on the day of saving,
/// <c>…-ended…</c> an end date, <c>…-notjoined…</c> (or <c>…-not-joined…</c>) not a member. Without the folder the
/// tests pass with nothing checked.</summary>
public class SavedLookupPageTests
{
    private static IEnumerable<string> Pages(string prefix) =>
        RealDataTests.Folder is { } folder && Directory.Exists(folder)
            ? Directory.GetFiles(folder, prefix + "*.htm", SearchOption.AllDirectories).Concat(Directory.GetFiles(folder, prefix + "*.html", SearchOption.AllDirectories))
            : [];

    /// <summary>The file's name without the hyphens, for the shape words ("not-joined" = "notjoined").</summary>
    private static string Shape(string file) => Path.GetFileNameWithoutExtension(file).Replace("-", "", StringComparison.Ordinal);

    [Fact]
    public void Every_saved_emta_page_reads_as_a_residency_answer()
    {
        foreach (var file in Pages("emta-"))
        {
            var name = Shape(file);
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
            var name = Shape(file);
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
