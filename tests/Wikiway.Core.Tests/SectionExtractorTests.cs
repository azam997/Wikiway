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

        var picked = SectionExtractor.SelectSections(SearchCategory.Items, sections);

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

        var picked = SectionExtractor.SelectSections(SearchCategory.Items, sections);

        Assert.Equal("Treasure Hunt", Assert.Single(picked).Title);
    }

    [Fact]
    public void UnrelatedHeadingsAreIgnored()
    {
        var sections = new[] { Section("1", "Lore"), Section("2", "Gallery"), Section("3", "Patches") };

        Assert.Empty(SectionExtractor.SelectSections(SearchCategory.Items, sections));
    }

    [Fact]
    public void CapsAtThreeSections()
    {
        var sections = Enumerable.Range(1, 6)
            .Select(i => Section(i.ToString(), $"Obtained From {i}"))
            .ToArray();

        Assert.Equal(3, SectionExtractor.SelectSections(SearchCategory.Items, sections).Count);
    }

    [Fact]
    public void GatheringCategorySharesItemKeywords()
    {
        var sections = new[] { Section("1", "Gathering"), Section("2", "Dropped By") };

        var picked = SectionExtractor.SelectSections(SearchCategory.Gathering, sections);

        Assert.Equal("Dropped By", Assert.Single(picked).Title);
    }

    [Fact]
    public void UnlocksCategoryUsesUnlockKeywords()
    {
        var sections = new[] { Section("1", "Unlocking"), Section("2", "Lore") };

        var picked = SectionExtractor.SelectSections(SearchCategory.Unlocks, sections);

        Assert.Equal("Unlocking", Assert.Single(picked).Title);
    }

    [Fact]
    public void TranscludedIndicesAreSkipped()
    {
        var sections = new[] { Section("T-1", "Obtained From"), Section("2", "Dropped By") };

        var picked = SectionExtractor.SelectSections(SearchCategory.Items, sections);

        Assert.Single(picked);
        Assert.Equal("2", picked[0].Index);
    }

    [Fact]
    public void OtherCategoryPicksNothing()
    {
        Assert.Empty(SectionExtractor.SelectSections(SearchCategory.Other, [Section("1", "Acquisition")]));
    }
}
