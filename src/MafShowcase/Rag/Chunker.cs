using System.Text.RegularExpressions;

namespace MafShowcase.Rag;

/// <summary>A piece of one page, small enough to embed and to quote. It carries its page's metadata.</summary>
public sealed record TextChunk(string Key, SourcePage Source, int Index, string Text)
{
    public string DocumentId => Source.DocumentId;
    public int PageNumber => Source.PageNumber;
}

/// <summary>
/// Step 2 of ingestion, written by hand on purpose so every rule is visible:
/// - Respect sentences: PDF lines wrap mid-sentence, so join the lines and cut only between sentences.
/// - Cap the size: one chunk should hold one idea, not a whole page.
/// - Overlap: each chunk starts with the last sentence(s) of the previous one, so a rule
///   that straddles a boundary is still whole in at least one chunk.
/// - Never cross a page: the page number is part of the citation.
/// </summary>
public static partial class Chunker
{
    public const int DefaultMaxChars = 800;
    public const int DefaultOverlapChars = 150;

    public static IReadOnlyList<TextChunk> Chunk(
        IEnumerable<SourcePage> pages, int maxChars = DefaultMaxChars, int overlapChars = DefaultOverlapChars) =>
        pages.SelectMany(page => ChunkPage(page, maxChars, overlapChars)).ToList();

    public static IReadOnlyList<TextChunk> ChunkPage(SourcePage page, int maxChars, int overlapChars)
    {
        var pieces = new List<string>();
        var current = new List<string>();
        int added = 0;   // sentences added since the last cut (the overlap does not count)

        foreach (string sentence in Sentences(page.Text, maxChars))
        {
            if (added > 0 && Length(current) + 1 + sentence.Length > maxChars)
            {
                pieces.Add(string.Join(' ', current));
                current = Overlap(current, overlapChars, maxChars);
                added = 0;
            }
            current.Add(sentence);
            added++;
        }

        if (added > 0)
        {
            pieces.Add(string.Join(' ', current));
        }

        return pieces
            .Select((text, i) => new TextChunk($"{page.DocumentId}-p{page.PageNumber}-c{i + 1}", page, i + 1, text))
            .ToList();
    }

    /// <summary>
    /// Joins the PDF's wrapped lines, then splits after '.', '!' or '?'. A sentence longer than a whole chunk
    /// (rare) is cut on word boundaries.
    /// </summary>
    private static IEnumerable<string> Sentences(string text, int maxChars) =>
        SentenceEnd().Split(Whitespace().Replace(text, " ").Trim())
            .Where(s => s.Length > 0)
            .SelectMany(s => s.Length <= maxChars ? new[] { s } : Words(s, maxChars));

    /// <summary>
    /// The last whole sentences of a chunk, up to overlapChars. If the last sentence alone is longer,
    /// it is still repeated when it is at most half a chunk: a split rule is worse than a longer chunk.
    /// </summary>
    private static List<string> Overlap(List<string> sentences, int overlapChars, int maxChars)
    {
        var tail = new List<string>();
        for (int i = sentences.Count - 1; i >= 0 && Length(tail) + sentences[i].Length + 1 <= overlapChars; i--)
        {
            tail.Insert(0, sentences[i]);
        }
        if (tail.Count == 0 && sentences[^1].Length <= maxChars / 2)
        {
            tail.Add(sentences[^1]);
        }
        return tail;
    }

    private static IEnumerable<string> Words(string text, int maxChars)
    {
        var part = new List<string>();
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Count > 0 && Length(part) + 1 + word.Length > maxChars)
            {
                yield return string.Join(' ', part);
                part.Clear();
            }
            part.Add(word);
        }
        if (part.Count > 0)
        {
            yield return string.Join(' ', part);
        }
    }

    private static int Length(List<string> parts) => parts.Count == 0 ? 0 : parts.Sum(p => p.Length) + parts.Count - 1;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceEnd();
}
