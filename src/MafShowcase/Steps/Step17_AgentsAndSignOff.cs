using MafShowcase.Agents;
using MafShowcase.Rag;
using MafShowcase.Ward;
using MafShowcase.Workflows;
using Microsoft.Agents.AI.Workflows;

namespace MafShowcase.Steps;

// Step 17: feature 0008 in the showcase. Two agent steps inside a fixed graph, and a pause for the nurse.
// The model drafts and summarises; the graph decides the order, the checks and who signs. Run it twice: y, then n.
public static class Step17_AgentsAndSignOff
{
    private const string Message = "I just gave the paracetamol to the patient in bed 4.";

    public static async Task RunAsync(WardAssistantFactory factory)
    {
        var ward = WardContext.Demo();
        using var index = new ProtocolIndex(RagPaths.DatabaseFile, factory.CreateEmbeddingGenerator());
        Workflow workflow = AdministrationReviewWorkflow.Create(factory.CreateChatClient(), index, ward);
        Console.WriteLine(workflow.ToMermaidString());

        Console.WriteLine($"> {Message}");
        string? lastDone = null;
        await using StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, Message);
        await foreach (WorkflowEvent evt in run.WatchStreamAsync())
        {
            switch (evt)
            {
                // The aggregator completes once per check result, and the sign-off port once when it raises
                // the request and once when it forwards the answer. Print each executor once in a row.
                case ExecutorCompletedEvent done when done.ExecutorId != lastDone:
                    Console.WriteLine($"[{done.ExecutorId}] done");
                    lastDone = done.ExecutorId;
                    break;

                // The RequestPort emitted a request: the workflow is paused until we answer.
                case RequestInfoEvent request when request.Request.TryGetDataAs<ReviewCard>(out ReviewCard? card):
                    ShowCard(card);
                    bool approved = Ask("Sign and record? (y/n) ");
                    await run.SendResponseAsync(request.Request.CreateResponse(
                        new NurseDecision(approved, ward.NurseId, approved ? null : "The nurse rejected the draft.")));
                    break;

                case WorkflowOutputEvent output:
                    Console.WriteLine();
                    Console.WriteLine(output.Data);
                    return;

                case WorkflowErrorEvent error:
                    Console.WriteLine($"Error: {error.Exception?.Message}");
                    return;
            }
        }
    }

    /// <summary>
    /// Part B: the same workflow the way MedTrack's two endpoints run it. Start returns a card and an id;
    /// the decision arrives later, in another request, with only that id.
    /// </summary>
    public static async Task RunAsWebApiAsync(WardAssistantFactory factory)
    {
        var ward = WardContext.Demo();
        using var index = new ProtocolIndex(RagPaths.DatabaseFile, factory.CreateEmbeddingGenerator());
        var runner = new ReviewRunner();   // a singleton in the API

        // POST /api/administration-reviews  { message }
        ReviewOutcome started = await runner.StartAsync(AdministrationReviewWorkflow.Create(factory.CreateChatClient(), index, ward), Message);
        if (started.PendingId is null)
        {
            Console.WriteLine(started.Result);
            return;
        }
        Console.WriteLine($"Start returned: pending id {started.PendingId}");
        ShowCard(started.Card!);

        // POST /api/administration-reviews/{id}/decision  { approved }   (the nurse id comes from the server)
        bool approved = Ask("Sign and record? (y/n) ");
        ReviewOutcome? decided = await runner.DecideAsync(started.PendingId, new NurseDecision(approved, ward.NurseId));
        Console.WriteLine($"Decision returned: {decided?.Result}");

        // The same id again: already decided, so the endpoint answers 404.
        Console.WriteLine($"Second decision with the same id: {(await runner.DecideAsync(started.PendingId, new NurseDecision(true, ward.NurseId)) is null ? "404, not found" : "accepted (wrong!)")}");
    }

    internal static void ShowCard(ReviewCard card)
    {
        Console.WriteLine();
        Console.WriteLine("+-- Review card ---------------------------------------");
        Console.WriteLine($"| {card.Draft}");
        foreach (CheckResult check in card.Checks)
        {
            Console.WriteLine($"| {(check.Passed ? "PASS" : "FAIL")} {check.Check}:");
            foreach (string line in check.Detail.Split('\n'))
            {
                Console.WriteLine($"|      {line}");
            }
        }
        Console.WriteLine("+-----------------------------------------------------");
    }

    internal static bool Ask(string question)
    {
        Console.Write(question);
        return string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase);
    }
}
