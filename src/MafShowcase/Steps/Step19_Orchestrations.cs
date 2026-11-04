using MafShowcase.Agents;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace MafShowcase.Steps;

// Step 19: ready-made multi-agent patterns. No custom executors: the agents themselves are the nodes.
// Agents in a workflow collect messages and only run when a TurnToken arrives.
public static class Step19_Orchestrations
{
    // Fictional and without identifiers: a bed number and medicines only.
    private const string Facts = """
        Bed 4, shift handover. Paracetamol 1 g oral given 08:00 (next not before 12:00).
        Enoxaparin 40 mg subcutaneous due 18:00. Allergy status: no known allergies.
        """;

    public static async Task RunAsync(WardAssistantFactory factory)
    {
        IChatClient chat = factory.CreateChatClient();
        AIAgent writer = Agent(chat, "HandoverWriter", "Write a three-line shift handover from the facts. Facts only, no advice.");
        AIAgent reviewer = Agent(chat, "HandoverReviewer", "Review the handover above against the facts. List anything missing or wrong, or say 'Complete'.");
        AIAgent pharmacist = Agent(chat, "PharmacistView", "In two lines: what a ward pharmacist would check in these facts.");
        AIAgent nurseInCharge = Agent(chat, "NurseInChargeView", "In two lines: what a nurse in charge would check in these facts.");

        Console.WriteLine("=== Sequential: writer, then reviewer (each sees the conversation so far) ===");
        await RunAsync(AgentWorkflowBuilder.BuildSequential(writer, reviewer));

        Console.WriteLine();
        Console.WriteLine("=== Concurrent: two views at the same time, aggregated ===");
        await RunAsync(AgentWorkflowBuilder.BuildConcurrent([pharmacist, nurseInCharge]));

        Console.WriteLine();
        Console.WriteLine("Handoff and group chat use the same builder (CreateHandoffBuilderWith, CreateGroupChatBuilderWith): theory only today.");
    }

    private static async Task RunAsync(Workflow workflow)
    {
        List<ChatMessage> input = [new(ChatRole.User, Facts)];
        await using StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, input);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));   // without this, the agents wait forever

        // Collect each agent's streamed answer by executor, and print the answers when the workflow yields its output.
        // In the concurrent pattern the two agents stream at the same time, so printing as it streams would interleave them.
        var answers = new Dictionary<string, System.Text.StringBuilder>();
        await foreach (WorkflowEvent evt in run.WatchStreamAsync())
        {
            switch (evt)
            {
                case AgentResponseUpdateEvent update:
                    if (!answers.TryGetValue(update.ExecutorId, out var text))
                    {
                        answers[update.ExecutorId] = text = new System.Text.StringBuilder();
                    }
                    text.Append(update.Update.Text);
                    break;
                case WorkflowOutputEvent:
                    foreach (var (executorId, answer) in answers)
                    {
                        Console.WriteLine($"[{AgentName(executorId)}] {answer.ToString().Trim()}");
                    }
                    return;
                case ExecutorFailedEvent failed:
                    Console.WriteLine($"Executor {failed.ExecutorId} failed: {failed.Data}");
                    return;
                case WorkflowErrorEvent error:
                    Console.WriteLine($"Error: {error.Exception?.Message}");
                    return;
            }
        }
        Console.WriteLine("(the stream ended without a workflow output)");
    }

    // Hosted agents get executor ids derived from the agent name (for example "HandoverWriter_3f2a..."); show the name only.
    private static string AgentName(string executorId) =>
        executorId.Split('_')[0];

    private static AIAgent Agent(IChatClient chat, string name, string instructions) =>
        new ChatClientAgent(chat, new ChatClientAgentOptions { Name = name, ChatOptions = new ChatOptions { Instructions = instructions } });
}
