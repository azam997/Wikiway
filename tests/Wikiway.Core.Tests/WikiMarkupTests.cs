using Wikiway.Core.Wiki;
using Xunit;

namespace Wikiway.Core.Tests;

public class WikiMarkupTests
{
    [Fact]
    public void RemovesTemplatesIncludingNested()
    {
        Assert.Equal("", WikiMarkup.Clean("{{disambig|name={{PAGENAME}}}}"));
    }

    [Fact]
    public void UnterminatedTemplateDropsTheRest()
    {
        Assert.Equal("prose first.", WikiMarkup.Clean("prose first. {{itembox\n| name = Iron Ingot"));
    }

    [Fact]
    public void DropsRedirectLines()
    {
        Assert.Equal("", WikiMarkup.Clean("#REDIRECT [[Momodi]]"));
    }

    [Fact]
    public void ReplacesLinksWithTheirLabels()
    {
        Assert.Equal("see the Jewel and Momodi",
            WikiMarkup.Clean("see [[Ul'dah|the Jewel]] and [[Momodi]]"));
    }

    [Fact]
    public void DropsInfoboxAndTableLines()
    {
        Assert.Equal("", WikiMarkup.Clean("{| class=\"table\"\n| name = Momodi\n! Header\n|}"));
    }

    [Fact]
    public void RemovesQuoteMarkupAndReferenceBrackets()
    {
        Assert.Equal("Momodi is nice, truly.",
            WikiMarkup.Clean("'''Momodi''' is ''nice''[1], truly.[23]"));
    }

    [Fact]
    public void RemovesCssRuleRuns()
    {
        var css = ".mw-parser-output div.infobox-n{float:right;position:relative;width:300px}" +
                  ".mw-parser-output div.infobox-n .icon{float:right;z-index:2}";
        Assert.Equal("", WikiMarkup.Clean(css));
    }

    [Fact]
    public void RemovesNestedMediaQueryRules()
    {
        Assert.Equal("", WikiMarkup.Clean("@media screen{.infobox{display:none}}"));
    }

    [Fact]
    public void KeepsProseUntouched()
    {
        var prose = "Momodi Modi is the proprietress of the Quicksand in Ul'dah.";
        Assert.Equal(prose, WikiMarkup.Clean(prose));
    }

    [Theory]
    [InlineData("#REDIRECT [[Momodi]]")]
    [InlineData("{{disambig|name=Letter to Momodi}}")]
    [InlineData("| name = Momodi\n| full-name = Momodi Modi")]
    [InlineData(".mw-parser-output div.infobox-n{float:right;width:300px;background:#fff}")]
    public void MarkupOnlyContentIsDominated(string text)
    {
        Assert.True(WikiMarkup.IsMarkupDominated(text));
    }

    [Theory]
    [InlineData("Momodi Modi is the proprietress of the Quicksand in Ul'dah.")]
    [InlineData("Momodi")]
    [InlineData("")]
    public void ProseIsNotDominated(string text)
    {
        Assert.False(WikiMarkup.IsMarkupDominated(text));
    }
}
