using Wikiway.Core.Models;

namespace Wikiway.Core.Abstractions;

public interface IAnswerSynthesizer
{
    bool IsConfigured { get; }

    Task<SynthesizedAnswer?> SynthesizeAsync(
        NormalizedQuery query,
        IReadOnlyList<RetrievedDocument> documents,
        CancellationToken ct);
}
