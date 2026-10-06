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

        var (results, providerDetail) = ResultJoiner.AttachWikiSections([sections, card], detail, QueryIntent.Acquisition);

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
            [Detail("local-gamedata", card), Detail("consolegameswiki", sections)], QueryIntent.Acquisition);

        Assert.Equal(2, results.Count);
        Assert.Empty(Assert.IsType<EntityCardResult>(results[0]).WikiSections);
    }

    [Fact]
    public void TitleMatchIgnoresCase()
    {
        var card = Card(Item("Iron Ingot"), 1.0);
        var sections = Sections("iron ingot", new WikiSectionText("Treasure Hunt", "text"));

        var (results, _) = ResultJoiner.AttachWikiSections([card, sections], [], QueryIntent.Acquisition);

        var joined = Assert.IsType<EntityCardResult>(Assert.Single(results));
        Assert.Single(joined.WikiSections);
    }

    [Fact]
    public void OnlyTheFirstCardWithATitleAbsorbsTheSections()
    {
        var firstCard = Card(Item("Fenrir Horn"), 1.0);
        var secondCard = Card(Item("Fenrir Horn"), 0.9);
        var sections = Sections("Fenrir Horn", new WikiSectionText("Obtained From", "text"));

        var (results, _) = ResultJoiner.AttachWikiSections([firstCard, secondCard, sections], [], QueryIntent.Acquisition);

        Assert.Equal(2, results.Count);
        Assert.Single(Assert.IsType<EntityCardResult>(results[0]).WikiSections);
        Assert.Empty(Assert.IsType<EntityCardResult>(results[1]).WikiSections);
    }

    [Fact]
    public void ArticleMismatchedTitlesStillJoin()
    {
        var card = Card(Duty("Bozjan Southern Front"), 1.0);
        var sections = Sections("The Bozjan Southern Front", new WikiSectionText("Unlock", "Complete Where Eagles Nest."));
        var detail = new[] { Detail("local-gamedata", card), Detail("consolegameswiki", sections) };

        var (results, providerDetail) = ResultJoiner.AttachWikiSections([card, sections], detail, QueryIntent.Unlock);

        var joined = Assert.IsType<EntityCardResult>(Assert.Single(results));
        Assert.Equal("Unlock", Assert.Single(joined.WikiSections).Heading);
        Assert.Equal(sections.PageUrl, joined.WikiUrl);
        Assert.Empty(providerDetail.Single(p => p.ProviderId == "consolegameswiki").Results);
    }

    [Fact]
    public void ArticleStrippingAppliesToTheCardSideToo()
    {
        var card = Card(Duty("The Navel"), 1.0);
        var sections = Sections("Navel", new WikiSectionText("Unlock", "text"));

        var (results, _) = ResultJoiner.AttachWikiSections([card, sections], [], QueryIntent.Unlock);

        var joined = Assert.IsType<EntityCardResult>(Assert.Single(results));
        Assert.Single(joined.WikiSections);
    }

    [Fact]
    public void ExactMatchBeatsStrippedMatch()
    {
        var articleCard = Card(Item("The Diadem"), 1.0);
        var bareCard = Card(Item("Diadem"), 0.9);
        var sections = Sections("Diadem", new WikiSectionText("Overview", "text"));

        var (results, _) = ResultJoiner.AttachWikiSections([articleCard, bareCard, sections], [], QueryIntent.Acquisition);

        Assert.Equal(2, results.Count);
        Assert.Empty(Assert.IsType<EntityCardResult>(results[0]).WikiSections);
        Assert.Single(Assert.IsType<EntityCardResult>(results[1]).WikiSections);
    }

    [Fact]
    public void AmbiguousStrippedTitlesDoNotJoin()
    {
        var results = new SearchResult[]
        {
            Card(Item("A Realm Reborn"), 1.0),
            Card(Item("The Realm Reborn"), 0.9),
            Sections("Realm Reborn", new WikiSectionText("Overview", "text")),
        };

        var (joined, _) = ResultJoiner.AttachWikiSections(results, [], QueryIntent.Acquisition);

        Assert.Same(results, joined);
    }

    [Fact]
    public void BareNameSectionsSkipOtherCardsAndDropWhenUnjoined()
    {
        var npcCard = Card(Npc("Gold Saucer"), 1.0);
        var sections = Sections("Gold Saucer", new WikiSectionText("Rewards", "MGP prizes."));
        var detail = new[] { Detail("local-gamedata", npcCard), Detail("consolegameswiki", sections) };

        var (results, providerDetail) = ResultJoiner.AttachWikiSections([npcCard, sections], detail, QueryIntent.Unknown);

        var card = Assert.IsType<EntityCardResult>(Assert.Single(results));
        Assert.Empty(card.WikiSections);
        Assert.Empty(providerDetail.Single(p => p.ProviderId == "consolegameswiki").Results);
    }

    [Fact]
    public void BareNameSectionsStillJoinItemCards()
    {
        var npcCard = Card(Npc("Malachite"), 1.0);
        var itemCard = Card(Item("Malachite"), 1.0);
        var sections = Sections("Malachite", new WikiSectionText("Dropped By", "Sandworms."));

        var (results, _) = ResultJoiner.AttachWikiSections([npcCard, itemCard, sections], [], QueryIntent.Unknown);

        Assert.Equal(2, results.Count);
        Assert.Empty(Assert.IsType<EntityCardResult>(results[0]).WikiSections);
        Assert.Equal("Dropped By", Assert.Single(Assert.IsType<EntityCardResult>(results[1]).WikiSections).Heading);
    }

    [Fact]
    public void UnlockSectionsJoinTheDutyNotTheSameNamedNpc()
    {
        var npcCard = Card(Npc("Gold Saucer"), 1.0);
        var dutyCard = Card(Duty("The Gold Saucer", optional: true), 1.0);
        var sections = Sections("Gold Saucer", new WikiSectionText("Unlock", "Complete It Could Happen to You."));

        var (results, _) = ResultJoiner.AttachWikiSections([dutyCard, npcCard, sections], [], QueryIntent.Unlock);

        Assert.Equal(2, results.Count);
        Assert.Equal("Unlock", Assert.Single(Assert.IsType<EntityCardResult>(results[0]).WikiSections).Heading);
        Assert.Empty(Assert.IsType<EntityCardResult>(results[1]).WikiSections);
    }

    [Fact]
    public void ExplicitIntentKeepsUnjoinedSections()
    {
        var npcCard = Card(Npc("Iron Ingot"), 1.0);
        var sections = Sections("Iron Ingot", new WikiSectionText("Treasure Hunt", "text"));

        var (results, _) = ResultJoiner.AttachWikiSections([npcCard, sections], [], QueryIntent.Acquisition);

        Assert.Equal(2, results.Count);
        Assert.IsType<WikiSectionsResult>(results[1]);
    }

    [Fact]
    public void NoMatchesReturnsTheInputUntouched()
    {
        var results = new SearchResult[] { Card(Npc(), 1.0), WikiResult("Momodi", 0.9) };
        var detail = new[] { Detail("local-gamedata", results[0]) };

        var (joined, providerDetail) = ResultJoiner.AttachWikiSections(results, detail, QueryIntent.Acquisition);

        Assert.Same(results, joined);
        Assert.Same(detail, providerDetail);
    }
}
