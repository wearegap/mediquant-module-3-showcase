using MafShowcase.Agents;
using MafShowcase.Rag;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DataIngestion.Chunkers;
using Microsoft.ML.Tokenizers;

namespace MafShowcase.Steps;

// Step 11: ingestion, the "Rebuild index" button. PDFs in, clean text plus metadata, overlapping chunks, one SQLite file out.
public static class Step11_Ingest
{
    public static async Task RunAsync(WardAssistantFactory factory)
    {
        // 1. Load and clean: text per page, banner and footer removed, page number kept.
        //    Metadata from the file name: WP-<ward>-<number>-<topic>.pdf.
        IReadOnlyList<SourcePage> pages = PdfLoader.LoadFolder(RagPaths.ProtocolFolder);
        Console.WriteLine($"--- {pages.Select(p => p.DocumentId).Distinct().Count()} documents, {pages.Count} pages ---");
        Console.WriteLine($"{"File",-46} {"Ward",-5} {"Document",-9} Topic");
        foreach (SourcePage first in pages.DistinctBy(p => p.DocumentId))
        {
            Console.WriteLine($"{first.FileName,-46} {first.Ward,-5} {first.DocumentId,-9} {first.Topic}");
        }
        Console.WriteLine();
        SourcePage paracetamol = pages.First(p => p.DocumentId == "WP-4B-01" && p.PageNumber == 1);
        Console.WriteLine(paracetamol.Text);

        // 2a. Chunk by hand: cut between sentences, size-capped, whole-sentence overlap, never across a page.
        IReadOnlyList<TextChunk> chunks = Chunker.Chunk(pages);
        Console.WriteLine();
        Console.WriteLine($"--- Hand-written chunker: {chunks.Count} chunks (max {Chunker.DefaultMaxChars} chars, overlap {Chunker.DefaultOverlapChars}) ---");
        foreach (TextChunk chunk in chunks.Where(c => c.Source == paracetamol))
        {
            Console.WriteLine($"{chunk.Key,-16} {chunk.Text.Length,4} chars   starts: {Start(chunk.Text)}");
        }

        // 2b. The same page with Microsoft.Extensions.DataIngestion (preview): fixed size in TOKENS, with overlap.
        //     About 200 tokens and 40 overlap, roughly our 800 and 150 characters.
        //     The tokenizer must match the embedding model, so it comes from the same setting (OpenAI:EmbeddingModel).
        Tokenizer tokenizer = TiktokenTokenizer.CreateForModel(factory.EmbeddingModel);
        var tokenChunker = new DocumentTokenChunker(new IngestionChunkerOptions(tokenizer) { MaxTokensPerChunk = 200, OverlapTokens = 40 });

        var document = new IngestionDocument(paracetamol.DocumentId);
        var section = new IngestionDocumentSection { PageNumber = paracetamol.PageNumber };
        section.Elements.Add(new IngestionDocumentParagraph(paracetamol.Text) { PageNumber = paracetamol.PageNumber });
        document.Sections.Add(section);

        Console.WriteLine();
        Console.WriteLine("--- DataIngestion DocumentTokenChunker, same page ---");
        int n = 0;
        await foreach (IngestionChunk<string> chunk in tokenChunker.ProcessAsync(document))
        {
            Console.WriteLine($"chunk {++n,-10} {tokenizer.CountTokens(chunk.Content),4} tokens  starts: {Start(chunk.Content)}");
        }
        Console.WriteLine("Fixed-size chunks can start and end mid-sentence; ours end on a sentence and repeat the last one. Both overlap.");

        // 3. Store: the same rebuild the reindex endpoint runs. Wipe, then write everything again.
        //    No embedding code here: the store calls the embedding model for every chunk during UpsertAsync.
        using var index = new ProtocolIndex(RagPaths.DatabaseFile, factory.CreateEmbeddingGenerator());
        IndexReport report = await index.RebuildAsync(RagPaths.ProtocolFolder);

        Console.WriteLine();
        Console.WriteLine($"--- Index rebuilt: {RagPaths.DatabaseFile} ---");
        Console.WriteLine($"Documents: {report.Documents}, pages: {report.Pages}, chunks stored: {report.Chunks}, " +
                          $"time: {report.Elapsed.TotalSeconds:F1} s, file: {new FileInfo(RagPaths.DatabaseFile).Length / 1024} KB");
    }

    private static string Start(string text)
    {
        string flat = text.Replace('\n', ' ').Trim();
        return flat.Length > 60 ? flat[..60] + "..." : flat;
    }
}
