using Wikiway.Core.Models;
using Wikiway.Core.Pipeline;
using Xunit;

namespace Wikiway.Core.Tests;

public class QueryNormalizerTests
{
    private readonly QueryNormalizer normalizer = new();

    [Fact]
    public void WhereIsPhraseYieldsLocationIntent()
    {
        var q = normalizer.Normalize("Where is Momodi?");

        Assert.Equal("momodi", q.Term);
        Assert.Equal(QueryIntent.Location, q.Intent);
    }

    [Fact]
    public void HowDoIUnlockYieldsUnlockIntent()
    {
        var q = normalizer.Normalize("How do I unlock the gold saucer");

        Assert.Equal("the gold saucer", q.Term);
        Assert.Equal(QueryIntent.Unlock, q.Intent);
    }

    [Fact]
    public void HowToGetYieldsAcquisitionIntent()
    {
        var q = normalizer.Normalize("how to get iron ingot");

        Assert.Equal("iron ingot", q.Term);
        Assert.Equal(QueryIntent.Acquisition, q.Intent);
    }

    [Fact]
    public void BareNameHasUnknownIntent()
    {
        var q = normalizer.Normalize("momodi");

        Assert.Equal("momodi", q.Term);
        Assert.Equal(QueryIntent.Unknown, q.Intent);
    }

    [Fact]
    public void PunctuationAndCasingAreStripped()
    {
        var q = normalizer.Normalize("  The ULTIMATE Weapon?!  ");

        Assert.Equal("the ultimate weapon", q.Term);
    }

    [Fact]
    public void IntentPhraseMustBeAWholeWord()
    {
        // "unlocking chains" starts with "unlock" but isn't an unlock question.
        var q = normalizer.Normalize("unlocking chains");

        Assert.Equal("unlocking chains", q.Term);
        Assert.Equal(QueryIntent.Unknown, q.Intent);
    }

    [Fact]
    public void RawInputIsPreserved()
    {
        var q = normalizer.Normalize("Where is Momodi?");

        Assert.Equal("Where is Momodi?", q.Raw);
    }

    [Fact]
    public void CategorySuppliesDefaultIntent()
    {
        var q = normalizer.Normalize("iron ingot", SearchCategory.Items);

        Assert.Equal(QueryIntent.Acquisition, q.Intent);
        Assert.Equal(SearchCategory.Items, q.Category);
    }

    [Fact]
    public void LeadingPhraseBeatsCategoryDefault()
    {
        var q = normalizer.Normalize("where is momodi", SearchCategory.Items);

        Assert.Equal(QueryIntent.Location, q.Intent);
        Assert.Equal(SearchCategory.Items, q.Category);
    }

    [Fact]
    public void NoCategoryLeavesIntentUnknown()
    {
        var q = normalizer.Normalize("momodi", SearchCategory.Other);

        Assert.Equal(QueryIntent.Unknown, q.Intent);
        Assert.Equal(SearchCategory.Other, q.Category);
    }
}
