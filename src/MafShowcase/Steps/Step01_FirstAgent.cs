using MafShowcase.Agents;
using Microsoft.Agents.AI;

namespace MafShowcase.Steps;

// Step 01: the smallest agent. Instructions in, one call, one answer, and what it cost.
public static class Step01_FirstAgent
{
    public static async Task RunAsync(WardAssistantFactory factory)
    {
        AIAgent agent = factory.Create(factory.CreateChatClient());

        AgentResponse response = await agent.RunAsync("In one sentence, what can you help a ward nurse with?");

        Console.WriteLine(response.Text);
        Console.WriteLine();
        Console.WriteLine($"Tokens: input {response.Usage?.InputTokenCount}, output {response.Usage?.OutputTokenCount}");
    }
}
