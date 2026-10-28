using MafShowcase.Agents;
using MafShowcase.Ward;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace MafShowcase.Steps;

// Step 10 (trainer demo): one question, traced. Every model call and tool call becomes a span with timings and tokens.
public static class Step10_Observability
{
    private const string SourceName = "MafShowcase";

    public static async Task RunAsync(WardAssistantFactory factory)
    {
        // Console exporter: no Docker, no Azure. In production this would be an OTLP or Azure Monitor exporter.
        using TracerProvider tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(SourceName)
            .AddConsoleExporter()
            .Build();

        // Instrument the chat client UNDER the pseudonymiser (the factory adds it on top),
        // so even with sensitive data switched on, a trace could only ever hold placeholders.
        IChatClient tracedChatClient = factory.CreateChatClient()
            .AsBuilder()
            .UseOpenTelemetry(sourceName: SourceName, configure: c => c.EnableSensitiveData = false)
            .Build();

        AIAgent agent = GuardedWardAssistantFactory.Create(tracedChatClient, WardContext.Demo())
            .AsBuilder()
            .UseOpenTelemetry(sourceName: SourceName, configure: c => c.EnableSensitiveData = false)
            .Build();

        AgentResponse response = await agent.RunAsync("What is due for bed 4?");

        Console.WriteLine();
        Console.WriteLine(response.Text);
        Console.WriteLine();
        Console.WriteLine("Look for: invoke_agent, then one 'chat' span for the input guard's classifier, then the agent's");
        Console.WriteLine("'chat' and 'execute_tool' spans. Each chat span carries gen_ai.usage.input_tokens and output_tokens.");
    }
}
