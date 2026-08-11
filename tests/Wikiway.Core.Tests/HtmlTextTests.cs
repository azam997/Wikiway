using Wikiway.Core.Wiki;
using Xunit;

namespace Wikiway.Core.Tests;

public class HtmlTextTests
{
    [Fact]
    public void StripsTagsAndKeepsText()
    {
        Assert.Equal("attune to all aether currents",
            HtmlText.Strip("attune to all <span class=\"searchmatch\">aether currents</span>"));
    }

    [Fact]
    public void DecodesCommonEntities()
    {
        Assert.Equal("Ul'dah & the \"Jewel\"", HtmlText.Strip("Ul&#39;dah &amp; the &quot;Jewel&quot;"));
    }

    [Fact]
    public void CollapsesBlankLines()
    {
        Assert.Equal("one\ntwo", HtmlText.Strip("<p>one</p>\n\n\n<p>two</p>"));
    }
}
