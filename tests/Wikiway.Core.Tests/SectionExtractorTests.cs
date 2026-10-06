using Wikiway.Core.Abstractions;
using Wikiway.Core.Models;
using Wikiway.Core.Wiki;
using Xunit;

namespace Wikiway.Core.Tests;

public class SectionExtractorTests
{
    private static WikiSection Section(string index, string title) => new(index, title, 1);

    [Fact]
    public void MatchesCommonItemHeadingVariants()
    {
        var sections = new[]
        {
            Section("1", "Acquisition"),
            Section("2", "Obtained From"),
            Section("3", "Dropped By"),
            Section("4", "Lore"),
        };

        var picked = SectionExtractor.SelectSections(QueryIntent.Acquisition, sections);

        Assert.Equal(2, picked.Count);
        Assert.DoesNotContain(picked, s => s.Title == "Lore");
        Assert.DoesNotContain(picked, s => s.Title == "Acquisition");
    }

    [Fact]
    public void SheetCoveredSectionsAreNotPicked()
    {
        var sections = new[]
        {
            Section("2", "Purchase"),
            Section("3", "Crafting Recipe"),
            Section("4", "Treasure Hunt"),
            Section("5", "Exchange"),
            Section("6", "Gathering"),
        };

        var picked = SectionExtractor.SelectSections(QueryIntent.Acquisition, sections);

        Assert.Equal("Treasure Hunt", Assert.Single(picked).Title);
    }

    // The Aithon Whistle shape: a machine-generated item page whose only
    // drop info sits in a "Duties" subsection under the umbrella heading.
    [Fact]
    public void DutiesSubsectionIsPickedWithoutTheUmbrella()
    {
        var sections = new[]
        {
            Section("1", "Acquisition"),
            Section("2", "Duties"),
            Section("3", "Exchange"),
            Section("4", "Used For"),
        };

        var picked = SectionExtractor.SelectSections(QueryIntent.Acquisition, sections);

        Assert.Equal("Duties", Assert.Single(picked).Title);
    }

    [Fact]
    public void UnrelatedHeadingsAreIgnored()
    {
        var sections = new[] { Section("1", "Lore"), Section("2", "Gallery"), Section("3", "Patches") };

        Assert.Empty(SectionExtractor.SelectSections(QueryIntent.Acquisition, sections));
    }

    [Fact]
    public void CapsAtThreeSections()
    {
        var sections = Enumerable.Range(1, 6)
            .Select(i => Section(i.ToString(), $"Obtained From {i}"))
            .ToArray();

        Assert.Equal(3, SectionExtractor.SelectSections(QueryIntent.Acquisition, sections).Count);
    }

    [Fact]
    public void BareNameSharesItemKeywords()
    {
        var sections = new[] { Section("1", "Gathering"), Section("2", "Dropped By") };

        var picked = SectionExtractor.SelectSections(QueryIntent.Unknown, sections);

        Assert.Equal("Dropped By", Assert.Single(picked).Title);
    }

    [Fact]
    public void UnlockIntentUsesUnlockKeywords()
    {
        var sections = new[] { Section("1", "Unlocking"), Section("2", "Lore") };

        var picked = SectionExtractor.SelectSections(QueryIntent.Unlock, sections);

        Assert.Equal("Unlocking", Assert.Single(picked).Title);
    }

    [Fact]
    public void TranscludedIndicesAreSkipped()
    {
        var sections = new[] { Section("T-1", "Obtained From"), Section("2", "Dropped By") };

        var picked = SectionExtractor.SelectSections(QueryIntent.Acquisition, sections);

        Assert.Single(picked);
        Assert.Equal("2", picked[0].Index);
    }

    [Theory]
    [InlineData(QueryIntent.Location)]
    [InlineData(QueryIntent.General)]
    public void LocationAndGeneralIntentsPickNothing(QueryIntent intent)
    {
        Assert.False(SectionExtractor.AppliesTo(intent));
        Assert.Empty(SectionExtractor.SelectSections(intent, [Section("1", "Dropped By")]));
    }
}
