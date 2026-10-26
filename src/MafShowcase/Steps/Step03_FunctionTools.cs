using MafShowcase.Agents;
using MafShowcase.Tools;
using MafShowcase.Ward;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace MafShowcase.Steps;

// Step 03: give the agent tools, then look inside the response to see the loop it ran.
public static class Step03_FunctionTools
{
    public static async Task RunAsync(WardAssistantFactory factory)
    {
        // The server decides the ward (here: 4B). The model only decides which tool to call.
        var tools = new WardTools(new FakeWardQueries(), TimeProvider.System, wardCode: "4B");
        AIAgent agent = factory.Create(factory.CreateChatClient(), tools);

        AgentResponse response = await agent.RunAsync("Which medications are due for the patient in bed 4?");

        Console.WriteLine("--- What the agent did ---");
        foreach (ChatMessage message in response.Messages)
        {
            foreach (AIContent content in message.Contents)
            {
                switch (content)
                {
                    case FunctionCallContent call:
                        Console.WriteLine($"CALL   {call.Name}({string.Join(", ", call.Arguments?.Select(a => $"{a.Key}={a.Value}") ?? [])})");
                        break;
                    case FunctionResultContent result:
                        Console.WriteLine($"RESULT {result.Result}");
                        break;
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine("--- What the nurse sees ---");
        Console.WriteLine(response.Text);
    }
}
