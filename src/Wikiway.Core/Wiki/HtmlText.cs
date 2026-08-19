using System.Text;

namespace Wikiway.Core.Wiki;

public static class HtmlText
{
    // Good enough for wiki snippets and lead sections; not a general HTML parser.
    public static string Strip(string html)
    {
        html = DropBlock(html, "style");
        html = DropBlock(html, "script");

        var sb = new StringBuilder(html.Length);
        var inTag = false;

        foreach (var c in html)
        {
            if (c == '<')
                inTag = true;
            else if (c == '>')
                inTag = false;
            else if (!inTag)
                sb.Append(c);
        }

        var text = sb.ToString()
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Replace("&#39;", "'")
            .Replace("&nbsp;", " ");

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join("\n", lines);
    }

    // Tag stripping alone would keep the inner text, and TemplateStyles CSS
    // arrives inside <style> on real wiki leads.
    private static string DropBlock(string html, string tag)
    {
        var open = "<" + tag;
        var close = "</" + tag;
        var start = html.IndexOf(open, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return html;

        var sb = new StringBuilder(html.Length);
        var pos = 0;
        while (start >= 0)
        {
            sb.Append(html, pos, start - pos);
            var end = html.IndexOf(close, start, StringComparison.OrdinalIgnoreCase);
            if (end < 0)
            {
                pos = html.Length;
                break;
            }

            var closeEnd = html.IndexOf('>', end);
            pos = closeEnd < 0 ? html.Length : closeEnd + 1;
            start = html.IndexOf(open, pos, StringComparison.OrdinalIgnoreCase);
        }

        sb.Append(html, pos, html.Length - pos);
        return sb.ToString();
    }
}
