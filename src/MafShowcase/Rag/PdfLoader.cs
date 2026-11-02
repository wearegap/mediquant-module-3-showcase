using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace MafShowcase.Rag;

/// <summary>
/// One page of one source document, with its text cleaned for indexing, plus the metadata that travels with it.
/// Ward, DocumentId and Topic come from the file name; Title from the PDF; PageNumber from the page.
/// </summary>
public sealed record SourcePage(
    string DocumentId, string Ward, string Topic, string Title, string FileName, int PageNumber, string Text);

/// <summary>
/// Step 1 of ingestion: get the text out, page by page, and keep where it came from.
/// The page number travels with the text all the way to the citation.
/// </summary>
public static partial class PdfLoader
{
    public static IReadOnlyList<SourcePage> LoadFolder(string folder) =>
        Directory.EnumerateFiles(folder, "*.pdf")
            .Order(StringComparer.OrdinalIgnoreCase)
            .SelectMany(Load)
            .ToList();

    public static IEnumerable<SourcePage> Load(string path)
    {
        using PdfDocument document = PdfDocument.Open(path);

        // Metadata from the file name: WP-<ward>-<number>-<topic>.pdf, e.g. WP-6R-01-paracetamol.pdf.
        // The naming convention IS the metadata schema, so a file that does not follow it is rejected, not guessed.
        string fileName = Path.GetFileName(path);
        Match name = FileNamePattern().Match(fileName);
        if (!name.Success)
        {
            throw new InvalidDataException($"'{fileName}' does not follow WP-<ward>-<number>-<topic>.pdf.");
        }
        string ward = name.Groups["ward"].Value;
        string documentId = $"WP-{ward}-{name.Groups["number"].Value}";
        string topic = name.Groups["topic"].Value.Replace('-', ' ');

        // The title written into the PDF, e.g. "WP-4B-01 Paracetamol administration (oral and IV)".
        string title = document.Information.Title ?? documentId;

        var pages = new List<SourcePage>();
        foreach (Page page in document.GetPages())
        {
            // ContentOrderTextExtractor gives reading order; page.Text gives the internal drawing order.
            string text = Clean(ContentOrderTextExtractor.GetText(page));
            if (text.Length > 0)
            {
                pages.Add(new SourcePage(documentId, ward, topic, title, fileName, page.Number, text));
            }
        }
        return pages;
    }

    /// <summary>
    /// Clean before you chunk: drop the lines repeated on every page (banner, footer, page numbers)
    /// and on every document (the hospital / version / owner line under each title).
    /// Left in, they end up in many chunks and make those chunks look alike to the embedding model:
    /// in rehearsal, "hospital" in that line pulled a cafeteria question above the threshold.
    /// </summary>
    public static string Clean(string text)
    {
        var kept = text
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(line => line.TrimEnd())
            .Where(line => !BoilerplatePattern().IsMatch(line));
        return MultipleBlankLines().Replace(string.Join('\n', kept), "\n\n").Trim();
    }

    [GeneratedRegex(@"^WP-(?<ward>[0-9A-Z]+)-(?<number>\d{2})-(?<topic>[a-z0-9-]+)\.pdf$", RegexOptions.IgnoreCase)]
    private static partial Regex FileNamePattern();

    [GeneratedRegex(@"^\s*(FICTIONAL TRAINING DOCUMENT\..*|Page \d+ of \d+|Example General Hospital - Ward \w+ - .*|Example General Hospital, Ward \w+\. Version .*)\s*$")]
    private static partial Regex BoilerplatePattern();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultipleBlankLines();
}
