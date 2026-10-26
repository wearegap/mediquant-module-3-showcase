using MafShowcase.Agents;
using MafShowcase.Tools;
using MafShowcase.Ward;
using Microsoft.Agents.AI;

namespace MafShowcase.Steps;

// Step 04: the follow-up question only works if the agent remembers the first one.
public static class Step04_MultiTurnSession
{
    public static async Task RunAsync(WardAssistantFactory factory)
    {
        var tools = new WardTools(new FakeWardQueries(), TimeProvider.System, wardCode: "4B");
        AIAgent agent = factory.Create(factory.CreateChatClient(), tools);

        Console.WriteLine("--- Without a session: each call starts from nothing ---");
        await AskAsync(agent, null, "Who is in bed 4?");
        await AskAsync(agent, null, "What is due for them?");

        Console.WriteLine("--- With a session: the second question has context ---");
        AgentSession session = await agent.CreateSessionAsync();
        await AskAsync(agent, session, "Who is in bed 4?");
        await AskAsync(agent, session, "What is due for them?");
    }

    private static async Task AskAsync(AIAgent agent, AgentSession? session, string question)
    {
        Console.WriteLine($"> {question}");
        Console.WriteLine(await agent.RunAsync(question, session));
        Console.WriteLine();
    }
}
