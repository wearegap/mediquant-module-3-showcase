using MafShowcase.Agents;
using MafShowcase.Rag;
using MafShowcase.Tools;
using MafShowcase.Ward;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Xunit;

namespace MafShowcase.Tests;

/// <summary>
/// Session 3. Ingestion and retrieval are deterministic code, so they are tested against the real PDFs
/// with a fake embedding model and a temporary SQLite file. The chat model is the scripted client, as always.
/// </summary>
public sealed class RagTests : IDisposable
{
    private static readonly string Pdfs = Path.Combine(AppContext.BaseDirectory, "protocols");
    private readonly string _database = Path.Combine(Path.GetTempPath(), $"protocols-test-{Guid.NewGuid():N}.db");

    // The fake model only matches shared words, so the tests use a low threshold. Real thresholds are tuned with the real model.
    private const double TestThreshold = 0.05;

    public void Dispose()
    {
        foreach (string file in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_database) + "*"))
        {
            try { File.Delete(file); } catch (IOException) { /* SQLite may still hold it on some machines */ }
        }
    }

    // ---------- Loading and chunking ----------

    [Fact]
    public void Loader_keeps_document_id_and_page_and_removes_the_repeated_banner()
    {
        IReadOnlyList<SourcePage> pages = PdfLoader.LoadFolder(Pdfs);

        Assert.Equal(11, pages.Select(p => p.DocumentId).Distinct().Count());
        Assert.Contains(pages, p => p.DocumentId == "WP-4B-01" && p.PageNumber == 2);
        Assert.DoesNotContain(pages, p => p.Text.Contains("FICTIONAL TRAINING DOCUMENT"));
        Assert.DoesNotContain(pages, p => p.Text.Contains("Example General Hospital"));
    }

    [Fact]
    public void Loader_reads_ward_document_and_topic_metadata_from_the_file_name()
    {
        SourcePage page = PdfLoader.LoadFolder(Pdfs).First(p => p.FileName == "WP-6R-01-paracetamol.pdf");

        Assert.Equal("6R", page.Ward);
        Assert.Equal("WP-6R-01", page.DocumentId);
        Assert.Equal("paracetamol", page.Topic);
    }

    [Fact]
    public void Chunks_cut_between_sentences_respect_the_size_limit_and_overlap_their_neighbours()
    {
        // Sentences wrapped across lines, as PdfPig returns them.
        string text = string.Join("\n", Enumerable.Range(1, 12).Select(i =>
            $"Rule {i}: give the dose only after the second check,\nand record it in MedTrack within {i} minutes of giving it."));
        var page = new SourcePage("WP-TEST", "XX", "test", "Test", "test.pdf", 3, text);

        IReadOnlyList<TextChunk> chunks = Chunker.ChunkPage(page, maxChars: 400, overlapChars: 80);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.Equal(3, c.PageNumber));
        // At most the limit plus the overlap carried over (at most half a chunk) and one space.
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 400 + 200 + 1, $"{c.Key} is {c.Text.Length} chars"));
        // Wrapped lines are joined, and every chunk ends at the end of a sentence.
        Assert.All(chunks, c => Assert.DoesNotContain("\n", c.Text));
        Assert.All(chunks, c => Assert.EndsWith(".", c.Text));
        for (int i = 1; i < chunks.Count; i++)
        {
            // Overlap: the next chunk starts with the last sentence of the previous one...
            string lastSentence = "Rule" + chunks[i - 1].Text.Split("Rule")[^1];
            Assert.StartsWith(lastSentence, chunks[i].Text);
            // ...exactly once: no sentence is repeated inside a chunk.
            string[] rules = chunks[i].Text.Split("Rule ", StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(rules.Length, rules.Distinct().Count());
        }
    }

    // ---------- Index and search ----------

    [Fact]
    public async Task Rebuild_indexes_every_document_and_search_returns_the_right_document_with_its_page()
    {
        var embeddings = new HashingEmbeddingGenerator();
        using var index = new ProtocolIndex(_database, embeddings);

        IndexReport report = await index.RebuildAsync(Pdfs);
        IReadOnlyList<ProtocolPassage> hits = await index.SearchAsync("enoxaparin creatinine clearance dose", top: 3, minSimilarity: TestThreshold, ward: "4B");

        Assert.Equal(11, report.Documents);
        Assert.True(report.Chunks >= report.Pages);
        Assert.Equal("WP-4B-02", hits[0].DocumentId);
        Assert.Equal(1, hits[0].PageNumber);
    }

    [Fact]
    public async Task Rebuild_twice_gives_the_same_index_not_a_duplicated_one()
    {
        using var index = new ProtocolIndex(_database, new HashingEmbeddingGenerator());

        IndexReport first = await index.RebuildAsync(Pdfs);
        IndexReport second = await index.RebuildAsync(Pdfs);
        IReadOnlyList<ProtocolPassage> hits = await index.SearchAsync("paracetamol", top: 50, minSimilarity: 0);

        Assert.Equal(first.Chunks, second.Chunks);
        Assert.True(hits.Count <= second.Chunks);
    }

    [Fact]
    public async Task Threshold_turns_unrelated_questions_into_no_results()
    {
        using var index = new ProtocolIndex(_database, new HashingEmbeddingGenerator());
        await index.RebuildAsync(Pdfs);

        IReadOnlyList<ProtocolPassage> hits = await index.SearchAsync("cafeteria opening times tomorrow", top: 4, minSimilarity: 0.3);

        Assert.Empty(hits);
    }

    [Fact]
    public async Task Search_tool_reports_not_found_explicitly()
    {
        using var index = new ProtocolIndex(_database, new HashingEmbeddingGenerator());
        await index.RebuildAsync(Pdfs);
        var tools = new ProtocolTools(index, "4B", minSimilarity: 0.3);

        ProtocolSearchResult result = await tools.SearchWardProtocols("cafeteria opening times tomorrow");

        Assert.False(result.Found);
        Assert.Empty(result.Passages);
    }

    [Fact]
    public async Task Ward_filter_returns_only_that_wards_protocols()
    {
        using var index = new ProtocolIndex(_database, new HashingEmbeddingGenerator());
        await index.RebuildAsync(Pdfs);
        const string query = "paracetamol maximum dose in 24 hours";

        IReadOnlyList<ProtocolPassage> all = await index.SearchAsync(query, top: 6, minSimilarity: 0);
        IReadOnlyList<ProtocolPassage> ward4B = await index.SearchAsync(query, top: 6, minSimilarity: 0, ward: "4B");

        Assert.Contains(all, p => p.Ward == "6R");       // without a filter both wards compete
        Assert.NotEmpty(ward4B);
        Assert.All(ward4B, p => Assert.Equal("4B", p.Ward));
    }

    // ---------- The grounded assistant ----------

    [Fact]
    public async Task Assistant_receives_passages_with_citations_from_the_search_tool()
    {
        using var index = new ProtocolIndex(_database, new HashingEmbeddingGenerator(), defaultMinSimilarity: TestThreshold);
        await index.RebuildAsync(Pdfs);

        var chat = new ScriptedChatClient(
            ScriptedChatClient.Text("""{"inScope":true,"category":"protocol_question","reason":"test"}"""),
            ScriptedChatClient.ToolCall("call-1", ProtocolTools.SearchWardProtocolsName, new() { ["query"] = "paracetamol maximum 24 hours weight 50 kg" }),
            ScriptedChatClient.Text("The maximum is 4 g in 24 hours for adults of 50 kg or more [WP-4B-01 p.1]."));
        AIAgent agent = GuardedWardAssistantFactory.Create(chat, WardContext.Demo(), index);

        AgentResponse response = await agent.RunAsync("What is the maximum daily paracetamol dose?");

        string toolResult = chat.Requests[^1].SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single().Result?.ToString() ?? "";
        Assert.Contains("[WP-4B-01 p.", toolResult);
        Assert.Contains("4 g in 24 hours", toolResult);
        Assert.Contains("[WP-4B-01 p.1]", response.Text);
    }
}
