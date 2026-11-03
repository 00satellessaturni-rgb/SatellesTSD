using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace SatellesTSD.Tests;

/// <summary>A canonical text form of an element tree for comparison: names with namespaces, attributes sorted
/// (namespace declarations dropped), leaf text trimmed and numbers normalised to two or more decimals, so that
/// "1000" and "1000.00" compare equal while order and structure must match exactly.</summary>
internal static class XmlCanon
{
    public static string Of(XElement element)
    {
        var sb = new StringBuilder();
        Append(sb, element, 0);
        return sb.ToString();
    }

    public static void AssertEqual(XElement expected, XElement actual)
    {
        var e = Of(expected).Split('\n');
        var a = Of(actual).Split('\n');
        for (int i = 0; i < Math.Max(e.Length, a.Length); i++)
        {
            var el = i < e.Length ? e[i] : "<end>";
            var al = i < a.Length ? a[i] : "<end>";
            if (el != al)
            {
                Assert.Fail($"First difference at canonical line {i + 1}:\n  expected: {el.Trim()}\n  actual:   {al.Trim()}");
            }
        }
    }

    private static void Append(StringBuilder sb, XElement e, int depth)
    {
        sb.Append(' ', depth * 2).Append(e.Name.NamespaceName).Append(':').Append(e.Name.LocalName);
        foreach (var attribute in e.Attributes()
                     .Where(x => !x.IsNamespaceDeclaration)
                     .OrderBy(x => x.Name.NamespaceName, StringComparer.Ordinal)
                     .ThenBy(x => x.Name.LocalName, StringComparer.Ordinal))
        {
            sb.Append(' ').Append(attribute.Name.LocalName).Append('=').Append(attribute.Value.Trim());
        }

        if (!e.HasElements)
        {
            var text = e.Value.Trim();
            // Only an XML decimal (sign, digits, one dot) is normalised; "58,08" stays text so a comma can never equal 5808.
            sb.Append(" = ").Append(
                decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
                    ? number.ToString("0.00####", CultureInfo.InvariantCulture)
                    : text);
        }

        sb.Append('\n');
        foreach (var child in e.Elements())
        {
            Append(sb, child, depth + 1);
        }
    }
}
