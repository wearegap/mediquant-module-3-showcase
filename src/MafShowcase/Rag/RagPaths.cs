namespace MafShowcase.Rag;

/// <summary>Where the source PDFs and the index file live. Run the showcase from the repository root.</summary>
public static class RagPaths
{
    public static string ProtocolFolder => Path.GetFullPath(Path.Combine("data", "protocols"));

    public static string DatabaseFile => Path.GetFullPath(Path.Combine("data", "protocols.db"));
}
