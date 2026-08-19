using Wikiway.Core.Abstractions;
using Wikiway.Core.Models;
using Wikiway.Core.Pipeline;
using Xunit;
using static Wikiway.Core.Tests.TestData;

namespace Wikiway.Core.Tests;

public class ResultJoinerTests
{
    private static ProviderResult Detail(string id, params SearchResult[] results) =>
        new(id, results, ProviderStatus.Ok);

    [Fact]
    public void SameTitledSectionsFoldIntoTheCard()
    {
        var card = Card(Item(), 1.0);
        var sections = Sections("Iron Ingot", new WikiSectionText("Treasure Hunt", "Found in timeworn leather maps."));
        var detail = new[] { Detail("local-gamedata", card), Detail("consolegameswiki", sections) };

        var (results, providerDetail) = ResultJoiner.AttachWikiSections([sections, card], detail);

        var joined = Assert.IsType<EntityCardResult>(Assert.Single(results));
        Assert.Equal("Treasure Hunt", Assert.Single(joined.WikiSections).Heading);
        Assert.Equal(sections.PageUrl, joined.WikiUrl);

        Assert.Empty(providerDetail.Single(p => p.ProviderId == "consolegameswiki").Results);
        Assert.Single(providerDetail.Single(p => p.ProviderId == "local-gamedata").Results);
    }

    [Fact]
    public void DifferentTitlesStaySeparate()
    {
        var card = Card(Item("Iron Ingot"), 1.0);
        var sections = Sections("Iron Ore", new WikiSectionText("Mining", "Mined in La Noscea."));

        var (results, _) = ResultJoiner.AttachWikiSections([card, sections],
            [Detail("local-gamedata", card), Detail("consolegameswiki", sections)]);

        Assert.Equal(2, results.Count);
        Assert.Empty(Assert.IsType<EntityCardResult>(results[0]).WikiSections);
    }

    [Fact]
    public void TitleMatchIgnoresCase()
    {
        var card = Card(Item("Iron Ingot"), 1.0);
        var sections = Sections("iron ingot", new WikiSectionText("Treasure Hunt", "text"));

        var (results, _) = ResultJoiner.AttachWikiSections([card, sections], []);

        var joined = Assert.IsType<EntityCardResult>(Assert.Single(results));
        Assert.Single(joined.WikiSections);
    }

    [Fact]
    public void OnlyTheFirstCardWithATitleAbsorbsTheSections()
    {
        var npcCard = Card(Npc("Fenrir"), 1.0);
        var mountCard = Card(new MountEntity(5, "Fenrir"), 0.9);
        var sections = Sections("Fenrir", new WikiSectionText("Obtained From", "text"));

        var (results, _) = ResultJoiner.AttachWikiSections([npcCard, mountCard, sections], []);

        Assert.Equal(2, results.Count);
        Assert.Single(Assert.IsType<EntityCardResult>(results[0]).WikiSections);
        Assert.Empty(Assert.IsType<EntityCardResult>(results[1]).WikiSections);
    }

    [Fact]
    public void NoMatchesReturnsTheInputUntouched()
    {
        var results = new SearchResult[] { Card(Npc(), 1.0), WikiResult("Momodi", 0.9) };
        var detail = new[] { Detail("local-gamedata", results[0]) };

        var (joined, providerDetail) = ResultJoiner.AttachWikiSections(results, detail);

        Assert.Same(results, joined);
        Assert.Same(detail, providerDetail);
    }
}
