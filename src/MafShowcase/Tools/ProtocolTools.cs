using System.ComponentModel;
using MafShowcase.Rag;
using Microsoft.Extensions.AI;

namespace MafShowcase.Tools;

/// <summary>What the model receives: the passages, each with its citation, or an explicit "nothing found".</summary>
public sealed record ProtocolSearchResult(bool Found, string Note, IReadOnlyList<ProtocolCitation> Passages);

public sealed record ProtocolCitation(string Cite, string Title, string Text, double Similarity);

/// <summary>
/// Retrieval as an ordinary tool, the same pattern as the Session 1 tools: the model decides when a question
/// needs the protocols, our code decides what comes back. Read-only, like every search.
/// The ward filter comes from the server (the conversation's ward), never from a tool argument.
/// </summary>
public sealed class ProtocolTools(ProtocolIndex index, string wardCode, int top = 4, double? minSimilarity = null)
{
    public const string SearchWardProtocolsName = "search_ward_protocols";
    public const int MaxQueryLength = 300;

    [Description("Searches this ward's protocols and policies (how medicines are given, checked, monitored and recorded). " +
                 "Returns passages with a citation such as [WP-4B-01 p.1]. Returns Found = false when the protocols do not cover the question.")]
    public async Task<ProtocolSearchResult> SearchWardProtocols(
        [Description("What to look for, in plain words, for example 'maximum daily paracetamol dose for a patient under 50 kg'.")] string query,
        CancellationToken cancellationToken = default)
    {
        // The query is model-chosen input like any other argument.
        if (string.IsNullOrWhiteSpace(query) || query.Length > MaxQueryLength)
        {
            return new ProtocolSearchResult(false, $"The query must be between 1 and {MaxQueryLength} characters.", []);
        }

        IReadOnlyList<ProtocolPassage> passages = await index.SearchAsync(query, top, minSimilarity, ward: wardCode, cancellationToken);

        return passages.Count == 0
            ? new ProtocolSearchResult(false, $"No Ward {wardCode} protocol covers this. Say so; do not answer from general knowledge.", [])
            : new ProtocolSearchResult(true, "Answer only from these passages and cite each fact.",
                passages.Select(p => new ProtocolCitation($"[{p.DocumentId} p.{p.PageNumber}]", p.Title, p.Text, p.Similarity)).ToList());
    }

    public IList<AITool> AsAITools() =>
    [
        AIFunctionFactory.Create(SearchWardProtocols, name: SearchWardProtocolsName),
    ];
}
