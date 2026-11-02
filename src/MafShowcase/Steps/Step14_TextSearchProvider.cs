using MafShowcase.Agents;
using MafShowcase.Guardrails;
using MafShowcase.Rag;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace MafShowcase.Steps;

// Step 14: the same retrieval with the framework's built-in context provider, TextSearchProvider.
// We still own the search (the same index); the framework owns when it runs and how results reach the model.
public static class Step14_TextSearchProvider
{
    private const string Question = "What is the maximum daily paracetamol dose for a patient who weighs 45 kg on this ward?";

    public static async Task RunAsync(WardAssistantFactory factory)
    {
        using var index = new ProtocolIndex(RagPaths.DatabaseFile, factory.CreateEmbeddingGenerator());

        // The only code we write: search this ward's protocols and turn the passages into the provider's result type.
        // SourceName becomes the citation.
        async Task<IEnumerable<TextSearchProvider.TextSearchResult>> SearchAdapter(string query, CancellationToken ct) =>
            (await index.SearchAsync(query, ward: "4B", cancellationToken: ct)).Select(p => new TextSearchProvider.TextSearchResult
            {
                SourceName = $"{p.DocumentId} p.{p.PageNumber}",
                Text = p.Text,
            });

        // The wire tap shows what the provider adds to the request.
        IChatClient chatClient = new WireTapChatClient(factory.CreateChatClient());

        Console.WriteLine("=== Mode 1: BeforeAIInvoke. Search runs before every model call; results are injected as a message ===");
        await AskAsync(chatClient, SearchAdapter, TextSearchProviderOptions.TextSearchBehavior.BeforeAIInvoke);

        Console.WriteLine();
        Console.WriteLine("=== Mode 2: OnDemandFunctionCalling. The provider adds a search tool; the model decides when to call it ===");
        await AskAsync(chatClient, SearchAdapter, TextSearchProviderOptions.TextSearchBehavior.OnDemandFunctionCalling);

        Console.WriteLine();
        Console.WriteLine("Compare with step13: same index, same answer. Here the framework formats the context and asks for citations;");
        Console.WriteLine("there, our tool did. The guarded assistant keeps the hand-written tool so every guard sees it as a normal tool call.");
    }

    private static async Task AskAsync(
        IChatClient chatClient,
        Func<string, CancellationToken, Task<IEnumerable<TextSearchProvider.TextSearchResult>>> search,
        TextSearchProviderOptions.TextSearchBehavior mode)
    {
        AIAgent agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = "ProtocolAssistant",
            ChatOptions = new ChatOptions
            {
                Instructions = "You answer questions about Ward 4B's protocols. Answer only from the provided context and cite the source. " +
                               "If the context does not cover the question, say so.",
            },
            AIContextProviders =
            [
                new TextSearchProvider(search, new TextSearchProviderOptions
                {
                    SearchTime = mode,
                    FunctionToolName = "search_ward_protocols",
                    FunctionToolDescription = "Searches Ward 4B's protocols and policies.",
                }),
            ],
        });

        Console.WriteLine($"> {Question}");
        AgentResponse response = await agent.RunAsync(Question);
        Console.WriteLine(response.Text);
    }
}
