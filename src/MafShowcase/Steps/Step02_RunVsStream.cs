using System.Diagnostics;
using MafShowcase.Agents;
using Microsoft.Agents.AI;

namespace MafShowcase.Steps;

// Step 02: same question, two ways. Watch the time to the first visible word.
public static class Step02_RunVsStream
{
    private const string Question = "Explain to a new nurse, in about 120 words, why a dose is always written as a value and a unit.";

    public static async Task RunAsync(WardAssistantFactory factory)
    {
        AIAgent agent = factory.Create(factory.CreateChatClient());

        Console.WriteLine("--- RunAsync: nothing appears until the whole answer is ready ---");
        var timer = Stopwatch.StartNew();
        AgentResponse response = await agent.RunAsync(Question);
        Console.WriteLine(response.Text);
        Console.WriteLine($"[first word after {timer.ElapsedMilliseconds} ms, total {timer.ElapsedMilliseconds} ms]");
        Console.WriteLine();

        Console.WriteLine("--- RunStreamingAsync: words appear as they are generated ---");
        timer.Restart();
        long? firstWordAt = null;
        await foreach (AgentResponseUpdate update in agent.RunStreamingAsync(Question))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                firstWordAt ??= timer.ElapsedMilliseconds;
                Console.Write(update.Text);
            }
        }
        Console.WriteLine();
        Console.WriteLine($"[first word after {firstWordAt} ms, total {timer.ElapsedMilliseconds} ms]");
    }
}
