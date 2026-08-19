using System.Text;
using System.Text.RegularExpressions;

namespace Wikiway.Core.Wiki;

public static partial class WikiMarkup
{
    private const int MinMeaningful = 30;

    public static string Clean(string text)
    {
        text = RemoveTemplates(text);
        text = LinkPattern().Replace(text, "$1");
        text = text.Replace("'''", "").Replace("''", "");
        text = RefPattern().Replace(text, "");
        text = RemoveCssRules(text);

        var kept = new List<string>();
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0 && !IsMarkupLine(trimmed))
                kept.Add(trimmed);
        }

        return string.Join("\n", kept);
    }

    // Redirects, disambiguations and template pages survive HTML stripping as
    // wikitext source; if nothing readable is left, the hit is markup, not prose.
    public static bool IsMarkupDominated(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith("#REDIRECT", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("{{", StringComparison.Ordinal))
            return true;

        var cleaned = Clean(text);
        if (cleaned.Length == 0)
            return trimmed.Length > 0;

        return trimmed.Length >= MinMeaningful && cleaned.Length < MinMeaningful;
    }

    private static string RemoveTemplates(string text)
    {
        if (!text.Contains("{{", StringComparison.Ordinal))
            return text;

        var sb = new StringBuilder(text.Length);
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (i + 1 < text.Length && text[i] == '{' && text[i + 1] == '{')
            {
                depth++;
                i++;
            }
            else if (depth > 0 && i + 1 < text.Length && text[i] == '}' && text[i + 1] == '}')
            {
                depth--;
                i++;
            }
            else if (depth == 0)
            {
                sb.Append(text[i]);
            }
        }

        return sb.ToString();
    }

    private static string RemoveCssRules(string text)
    {
        while (true)
        {
            var replaced = CssRulePattern().Replace(text, "");
            if (replaced == text)
                return replaced;

            text = replaced;
        }
    }

    // A brace surviving template and CSS removal is truncated markup, not prose.
    private static bool IsMarkupLine(string line) =>
        line.StartsWith('|') ||
        line.StartsWith('!') ||
        line.StartsWith("{|", StringComparison.Ordinal) ||
        line.StartsWith("#REDIRECT", StringComparison.OrdinalIgnoreCase) ||
        line.Contains('{') ||
        line.Contains('}');

    [GeneratedRegex(@"\[\[(?:[^\[\]]*\|)?([^\[\]]*)\]\]")]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"\[\d+\]")]
    private static partial Regex RefPattern();

    [GeneratedRegex(@"[^{}\n]*\{[^{}\n]*\}")]
    private static partial Regex CssRulePattern();
}
