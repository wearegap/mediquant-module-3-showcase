using System.Diagnostics;
using CommunityToolkit.VectorData.SqliteVec;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;

namespace MafShowcase.Rag;

/// <summary>
/// One record in the vector store: the chunk text, its metadata, and the text the store embeds.
/// The attributes are Microsoft.Extensions.VectorData; any provider (SQLite, Azure AI Search, Postgres...) reads them.
/// </summary>
public sealed class ProtocolChunk
{
    // text-embedding-3-small returns 1536 numbers per text. Changing the model means changing this and rebuilding.
    public const int Dimensions = 1536;

    [VectorStoreKey]
    public string Key { get; set; } = "";

    // Metadata: stored next to the text, returned with every hit, and usable in a filter.
    [VectorStoreData]
    public string Ward { get; set; } = "";

    [VectorStoreData]
    public string DocumentId { get; set; } = "";

    [VectorStoreData]
    public string Topic { get; set; } = "";

    [VectorStoreData]
    public string SourceFile { get; set; } = "";

    [VectorStoreData]
    public string Title { get; set; } = "";

    [VectorStoreData]
    public int PageNumber { get; set; }

    // The original text: what the model reads and what the citation points to.
    [VectorStoreData]
    public string Text { get; set; } = "";

    // Automatic embedding: the property holds TEXT, and the store calls the embedding generator on upsert and on search.
    // The vector is written to the database, but it cannot be read back through this property.
    [VectorStoreVector(Dimensions, DistanceFunction = DistanceFunction.CosineDistance)]
    public string? Embedding { get; set; }
}

/// <summary>What the search returns: a passage plus everything needed to cite it.</summary>
public sealed record ProtocolPassage(string Ward, string DocumentId, string Title, int PageNumber, string Text, double Similarity);

public sealed record IndexReport(int Documents, int Pages, int Chunks, TimeSpan Elapsed);

/// <summary>
/// The ward protocol index: a SQLite file with the sqlite-vec extension, in this process. Nothing else runs.
/// RebuildAsync is what the "Rebuild index" button calls: wipe, load, chunk, store (the store embeds).
/// </summary>
/// <param name="defaultMinSimilarity">The default threshold. Tuned for text-embedding-3-small; a different model needs a different value.</param>
public sealed class ProtocolIndex(
    string databasePath, IEmbeddingGenerator<string, Embedding<float>> embeddings, double defaultMinSimilarity = 0.35) : IDisposable
{
    public const string CollectionName = "ward_protocols";

    // The embedding generator is configured once, on the store. Pooling=false releases the file when the store is disposed.
    private readonly SqliteVectorStore _store = new(
        $"Data Source={databasePath};Pooling=false",
        new SqliteVectorStoreOptions { EmbeddingGenerator = embeddings });

    private VectorStoreCollection<string, ProtocolChunk> Collection =>
        _store.GetCollection<string, ProtocolChunk>(CollectionName);

    public async Task<IndexReport> RebuildAsync(string pdfFolder, CancellationToken cancellationToken = default)
    {
        var timer = Stopwatch.StartNew();

        IReadOnlyList<SourcePage> pages = PdfLoader.LoadFolder(pdfFolder);
        IReadOnlyList<TextChunk> chunks = Chunker.Chunk(pages);

        List<ProtocolChunk> records = chunks.Select(chunk => new ProtocolChunk
        {
            Key = chunk.Key,
            Ward = chunk.Source.Ward,
            DocumentId = chunk.Source.DocumentId,
            Topic = chunk.Source.Topic,
            SourceFile = chunk.Source.FileName,
            Title = chunk.Source.Title,
            PageNumber = chunk.Source.PageNumber,
            Text = chunk.Text,
            Embedding = chunk.Text,   // the store turns this into 1536 numbers during UpsertAsync
        }).ToList();

        // A fresh index every time: deleted documents disappear, changed ones are re-embedded.
        // Simple and correct for tens of documents; large corpora update one document at a time instead.
        var collection = Collection;
        await collection.EnsureCollectionDeletedAsync(cancellationToken);
        await collection.EnsureCollectionExistsAsync(cancellationToken);
        foreach (ProtocolChunk[] batch in records.Chunk(64))
        {
            await collection.UpsertAsync(batch, cancellationToken);   // one embedding request per batch
        }

        return new IndexReport(pages.Select(p => p.DocumentId).Distinct().Count(), pages.Count, records.Count, timer.Elapsed);
    }

    /// <summary>
    /// Take the top-k nearest chunks (optionally only one ward's), and drop the ones that are not close enough.
    /// An empty result is a real answer: "the protocols do not cover this".
    /// </summary>
    /// <param name="ward">Metadata filter. Set by the server from the conversation's ward, never by the model.</param>
    public async Task<IReadOnlyList<ProtocolPassage>> SearchAsync(
        string query, int top = 4, double? minSimilarity = null, string? ward = null, CancellationToken cancellationToken = default)
    {
        double threshold = minSimilarity ?? defaultMinSimilarity;
        var collection = Collection;
        if (!await collection.CollectionExistsAsync(cancellationToken))
        {
            return [];
        }

        // The filter runs in the database (SQLite supports equality filters only).
        var options = new VectorSearchOptions<ProtocolChunk>();
        if (ward is not null)
        {
            options.Filter = chunk => chunk.Ward == ward;
        }

        var passages = new List<ProtocolPassage>();
        // A string query: the store embeds it with the same generator it used for the chunks.
        await foreach (VectorSearchResult<ProtocolChunk> result in collection.SearchAsync(query, top, options, cancellationToken))
        {
            // SQLite reports cosine DISTANCE (0 = same direction). Convert to similarity (1 = same) so "higher is better".
            double similarity = 1 - (result.Score ?? 1);
            if (similarity >= threshold)
            {
                ProtocolChunk c = result.Record;
                passages.Add(new ProtocolPassage(c.Ward, c.DocumentId, c.Title, c.PageNumber, c.Text, Math.Round(similarity, 3)));
            }
        }
        return passages;
    }

    public void Dispose() => _store.Dispose();
}
