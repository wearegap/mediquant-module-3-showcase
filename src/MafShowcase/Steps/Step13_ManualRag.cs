using MafShowcase.Agents;
using MafShowcase.Rag;
using MafShowcase.Ward;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace MafShowcase.Steps;

// Step 13: RAG by hand. Our own search tool, our own citation format, inside the guarded assistant from Session 6.
public static class Step13_ManualRag
{
    private static readonly string[] Questions =
    [
        "What is the maximum daily paracetamol dose for a patient who weighs 45 kg on this ward?",   // covered: cites WP-4B-01
        "What is the maximum daily dose of ibuprofen on this ward?",                                // not covered: must say so
        "What is due for bed 4, and which of those medicines needs a second-nurse check?",          // two tools in one answer
    ];

    public static async Task RunAsync(WardAssistantFactory factory)
    {
        using var index = new ProtocolIndex(RagPaths.DatabaseFile, factory.CreateEmbeddingGenerator());
        AIAgent agent = GuardedWardAssistantFactory.Create(factory.CreateChatClient(), WardContext.Demo(), index);

        foreach (string question in Questions)
        {
            Console.WriteLine($"> {question}");
            AgentResponse response = await agent.RunAsync(question);

            foreach (FunctionCallContent call in response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>())
            {
                Console.WriteLine($"  [tool: {call.Name}({string.Join(", ", call.Arguments?.Select(a => $"{a.Key}={a.Value}") ?? [])})]");
            }

            Console.WriteLine(response.Text);
            Console.WriteLine();
        }
    }
}
