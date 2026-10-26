using MafShowcase.Agents;
using MafShowcase.Tools;
using MafShowcase.Ward;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace MafShowcase.Steps;

// Step 05: everything together. You type the questions: tools, one session, streamed answers.
public static class Step05_ConsoleChat
{
    public static async Task RunAsync(WardAssistantFactory factory)
    {
        var tools = new WardTools(new FakeWardQueries(), TimeProvider.System, wardCode: "4B");
        AIAgent agent = factory.Create(factory.CreateChatClient(), tools);
        AgentSession session = await agent.CreateSessionAsync();

        Console.WriteLine("Ward assistant, ward 4B. Type a question.");
        Console.WriteLine("Commands: /new starts a new conversation, /exit (or an empty line) quits.");
        Console.WriteLine();

        while (true)
        {
            Console.Write("you > ");
            string? input = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(input) || input.Equals("/exit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (input.Equals("/new", StringComparison.OrdinalIgnoreCase))
            {
                session = await agent.CreateSessionAsync();
                Console.WriteLine("[new conversation: the assistant has forgotten everything]");
                Console.WriteLine();
                continue;
            }

            Console.Write("assistant > ");
            await foreach (AgentResponseUpdate update in agent.RunStreamingAsync(input, session))
            {
                // Show the loop as it happens: tool requests arrive in the stream before the text.
                foreach (FunctionCallContent call in update.Contents.OfType<FunctionCallContent>())
                {
                    Console.Write($"[tool: {call.Name}] ");
                }

                Console.Write(update.Text);
            }

            Console.WriteLine();
            Console.WriteLine();
        }
    }
}
