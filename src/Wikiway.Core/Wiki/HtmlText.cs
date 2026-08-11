using System.Text;

namespace Wikiway.Core.Wiki;

public static class HtmlText
{
    // Good enough for wiki snippets and lead sections; not a general HTML parser.
    public static string Strip(string html)
    {
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
}
