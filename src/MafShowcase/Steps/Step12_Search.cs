using System.Numerics.Tensors;
using MafShowcase.Agents;
using MafShowcase.Rag;
using Microsoft.Extensions.AI;

namespace MafShowcase.Steps;

// Step 12: retrieval on its own (no chat model). Meaning not words, keyword vs semantic, top-k and a threshold, metadata filters.
public static class Step12_Search
{
    private const string Question = "How much paracetamol can an adult have in one day?";

    private static readonly string[] Sentences =
    [
        "Maximum of 4 g in 24 hours for adults weighing 50 kg or more.",   // same meaning, different words
        "Paracetamol tablets are white and round.",                       // same word, different meaning
        "The hospital cafeteria opens at 7 in the morning.",                // unrelated
    ];

    private static readonly string[] Queries =
    [
        "maximum daily paracetamol dose for a patient who weighs 45 kg",
        "which medicines need a second nurse to check them",
        "maximum daily dose of ibuprofen",           // near miss: same topic, another drug. Passes the threshold.
        "where can visitors park their car",          // unrelated: everything dropped
    ];

    public static async Task RunAsync(WardAssistantFactory factory)
    {
        IEmbeddingGenerator<string, Embedding<float>> embeddings = factory.CreateEmbeddingGenerator();

        // A. Meaning, not words.
        GeneratedEmbeddings<Embedding<float>> vectors = await embeddings.GenerateAsync([Question, .. Sentences]);
        Console.WriteLine($"Each text is {vectors[0].Vector.Length} numbers. Cosine similarity to \"{Question}\":");
        for (int i = 0; i < Sentences.Length; i++)
        {
            Console.WriteLine($"  {TensorPrimitives.CosineSimilarity(vectors[0].Vector.Span, vectors[i + 1].Vector.Span):F3}  {Sentences[i]}");
        }

        using var index = new ProtocolIndex(RagPaths.DatabaseFile, embeddings);

        // B. Keyword vs semantic. "Acetaminophen" is the US name for paracetamol; no protocol uses the word.
        const string synonym = "acetaminophen maximum dose in 24 hours";
        int keywordHits = Chunker.Chunk(PdfLoader.LoadFolder(RagPaths.ProtocolFolder))
            .Count(c => c.Text.Contains("acetaminophen", StringComparison.OrdinalIgnoreCase));
        Console.WriteLine();
        Console.WriteLine($"> {synonym}");
        Console.WriteLine($"  Keyword search: {keywordHits} chunks contain \"acetaminophen\"");
        foreach (ProtocolPassage p in await index.SearchAsync(synonym, top: 2, minSimilarity: 0, ward: "4B"))
        {
            Console.WriteLine($"  Semantic: {p.Similarity:F3}  [{p.DocumentId} p.{p.PageNumber}]  {Preview(p.Text)}");
        }

        // C. The store always returns top-k; the threshold drops the unrelated, not the near misses. (Ward 4B only.)
        foreach (string query in Queries)
        {
            Console.WriteLine();
            Console.WriteLine($"> {query}");
            IReadOnlyList<ProtocolPassage> all = await index.SearchAsync(query, top: 4, minSimilarity: 0, ward: "4B");
            foreach (ProtocolPassage p in all)
            {
                string kept = p.Similarity >= 0.35 ? "KEPT   " : "dropped";
                Console.WriteLine($"  {kept} {p.Similarity:F3}  [{p.DocumentId} p.{p.PageNumber}]  {Preview(p.Text)}");
            }
            if (all.All(p => p.Similarity < 0.35))
            {
                Console.WriteLine("  => nothing close enough: the protocols do not cover this");
            }
        }

        // D. Metadata filter. Two wards, same medicine, different rules (4 g on Ward 4B, 3 g on Ward 6R).
        const string dose = "maximum paracetamol dose in 24 hours for an adult who weighs 70 kg";
        Console.WriteLine();
        Console.WriteLine($"> {dose}");
        Console.WriteLine("  No filter (every ward):");
        foreach (ProtocolPassage p in await index.SearchAsync(dose, top: 4))
        {
            Console.WriteLine($"    Ward {p.Ward,-3} {p.Similarity:F3}  [{p.DocumentId} p.{p.PageNumber}]  {Preview(p.Text)}");
        }
        Console.WriteLine("  Filter Ward == \"4B\" (set by the server from the nurse's ward, never by the model):");
        foreach (ProtocolPassage p in await index.SearchAsync(dose, top: 4, ward: "4B"))
        {
            Console.WriteLine($"    Ward {p.Ward,-3} {p.Similarity:F3}  [{p.DocumentId} p.{p.PageNumber}]  {Preview(p.Text)}");
        }
    }

    private static string Preview(string text)
    {
        string flat = text.Replace('\n', ' ');
        return flat.Length > 70 ? flat[..70] + "..." : flat;
    }
}
