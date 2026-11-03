using System.Diagnostics;
using System.Text.RegularExpressions;
using SatellesTSD.Core.Matching;

namespace SatellesTSD.Tests;

/// <summary>VBA's Like as the workbook used it: whole-text, case-insensitive, with the workbook's own patterns.</summary>
public class LikePatternTests
{
    [Theory]
    [InlineData("Palk", "*palk*", true)]
    [InlineData("PALK oktoober", "*palk*", true)]
    [InlineData("tasu", "tasu", true)]
    [InlineData("Tasu", "tasu", true)]
    [InlineData("töötasu", "tasu", false)] // a pattern without stars equals the whole text
    [InlineData("töötasu", "*t??tasu*", true)]
    [InlineData("tootasu", "*t??tasu*", true)]
    [InlineData("tддtasu", "*t??tasu*", true)] // two garbled letters are still two characters
    [InlineData("Laenu tagastus", "*laen*", true)]
    [InlineData("Laen 25/09/2026", "*/*", true)]
    [InlineData("TVL 10-2026", "TVL*", true)]
    [InlineData("hl", "hl", true)]
    [InlineData("hl 10/2026", "*hl *", true)]
    [InlineData("üür oktoober", "üür*", true)]
    [InlineData("Korteri üür", "* üür*", true)]
    [InlineData("Korteriüür", "* üür*", false)]
    [InlineData("Pärnu mnt 12-3", "*[a-z][a-z] [0-9][0-9]-[0-9]*", true)]
    [InlineData("Olevi 29-15 (augustil)", "*[a-z][a-z] [0-9][0-9]-[0-9][0-9]*", true)]
    [InlineData("Linnamäe 1-7", "*[a-z][a-z] [0-9]-[0-9]*", false)] // the workbook's own report: ä lies outside a–z
    [InlineData("Mahtra tn 24-51 üür", "*[a-z][a-z] [0-9][0-9]-[0-9][0-9]*", true)] // and t does not
    [InlineData("arve 12-34", "arve [0-9][0-9]-[0-9][0-9]", true)]
    [InlineData("arve 12-345", "arve [0-9][0-9]-[0-9][0-9]", false)]
    [InlineData(@"a\b", @"*\*", true)]
    [InlineData("a", "", false)]
    [InlineData("", "", true)]
    [InlineData("", "*", true)]
    [InlineData("x", "?", true)]
    [InlineData("xy", "?", false)]
    [InlineData("5", "#", true)]
    [InlineData("a", "#", false)]
    [InlineData("b", "[!a-c]", false)]
    [InlineData("d", "[!a-c]", true)]
    [InlineData("]", "[]]", true)]
    [InlineData("-", "[a-]", true)]
    [InlineData("ab", "a[]b", true)] // an empty list stands for nothing
    [InlineData("ab", "ab[]", true)]
    [InlineData("", "[]", true)]
    [InlineData("a", "[]", false)]
    [InlineData("a[b", "a[b", true)] // never closed: a literal bracket
    [InlineData("A1", "a#", true)]
    [InlineData("PARNU MNT 12-3", "*[a-z][a-z] [0-9][0-9]-[0-9]*", true)] // a range folds the case too
    [InlineData("]", "[!]a]", false)]
    [InlineData("b", "[!]a]", true)]
    public void Matches_as_vba_did(string text, string pattern, bool expected) => Assert.Equal(expected, LikePattern.IsMatch(text, pattern));

    [Fact]
    public void A_null_text_is_empty() => Assert.True(LikePattern.IsMatch(null, "*"));

    [Fact]
    public void Problems_name_an_unclosed_bracket_and_a_downward_range()
    {
        Assert.Contains("never closed", Assert.Single(LikePattern.Problems("a[b")));
        Assert.Contains("downwards", Assert.Single(LikePattern.Problems("[z-a]")));
        Assert.Empty(LikePattern.Problems("*[a-z][a-z] [0-9]-[0-9]*;*/*"));
        Assert.Empty(LikePattern.Problems(""));
    }

    [Fact]
    public void Agrees_with_a_regex_oracle_on_random_patterns()
    {
        // the oracle: a straight translation to a regex, over the constructs a regex shares with Like (the ones it does not — an empty
        // list, an unclosed "[", a "]" as the first item — are pinned by the rows above)
        var random = new Random(20261103);
        const string alphabet = "ab1-";
        for (var i = 0; i < 4000; i++)
        {
            var pattern = RandomPattern(random);
            var text = new string(Enumerable.Range(0, random.Next(0, 7)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
            var expected = Regex.IsMatch(text, "\\A" + ToRegex(pattern) + "\\z", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            Assert.True(expected == LikePattern.IsMatch(text, pattern), $"'{text}' Like '{pattern}' should be {expected}");
        }
    }

    [Fact]
    public void A_long_run_of_wildcards_and_spaces_answers_at_once()
    {
        var text = "1" + new string(' ', 4000) + "x";
        var pattern = string.Concat(Enumerable.Repeat("*?", 30)) + "*y";
        var watch = Stopwatch.StartNew();
        Assert.False(LikePattern.IsMatch(text, pattern));
        Assert.True(LikePattern.IsMatch(text, "1*x"));
        Assert.True(LikePattern.IsMatch(text, "*[a-z]"));
        Assert.True(watch.ElapsedMilliseconds < 1000, $"took {watch.ElapsedMilliseconds} ms");
    }

    private static string RandomPattern(Random random)
    {
        var parts = new[] { "a", "b", "1", "*", "?", "#", "[ab]", "[!a]", "[a-b]", "-" };
        return string.Concat(Enumerable.Range(0, random.Next(0, 6)).Select(_ => parts[random.Next(parts.Length)]));
    }

    private static string ToRegex(string pattern)
    {
        var sb = new System.Text.StringBuilder();
        var i = 0;
        while (i < pattern.Length)
        {
            var c = pattern[i];
            switch (c)
            {
                case '*':
                    sb.Append(".*");
                    i++;
                    break;
                case '?':
                    sb.Append('.');
                    i++;
                    break;
                case '#':
                    sb.Append("[0-9]");
                    i++;
                    break;
                case '[':
                    var end = pattern.IndexOf(']', i + 1);
                    var body = pattern[(i + 1)..end];
                    sb.Append('[').Append(body.StartsWith('!') ? "^" + body[1..] : body).Append(']');
                    i = end + 1;
                    break;
                default:
                    sb.Append(Regex.Escape(c.ToString()));
                    i++;
                    break;
            }
        }

        return sb.ToString();
    }
}
