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

    [Fact]
    public void DropsStyleBlocksWithTheirContents()
    {
        Assert.Equal("before after",
            HtmlText.Strip("<p>before <style type=\"text/css\">.mw-parser-output .infobox{float:right}</style>after</p>"));
    }

    [Fact]
    public void DropsScriptBlocksWithTheirContents()
    {
        Assert.Equal("text", HtmlText.Strip("<p>text</p><SCRIPT>var x = 1;</SCRIPT>"));
    }

    [Fact]
    public void UnterminatedStyleBlockDropsTheRest()
    {
        Assert.Equal("kept", HtmlText.Strip("kept<style>.a{color:#fff}"));
    }
}
