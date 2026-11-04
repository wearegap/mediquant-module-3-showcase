using MafShowcase.Ward;
using MafShowcase.Workflows;
using Microsoft.Agents.AI.Workflows;

namespace MafShowcase.Steps;

// Step 16: fan-out to three independent checks, a barrier fans them back in, shared state carries the draft.
// Watch the superstep lines: the three checks run in the same superstep, the aggregator in the next one.
public static class Step16_FanOutFanIn
{
    public static async Task RunAsync()
    {
        foreach (string bed in new[] { "4", "1" })   // bed 1: amoxicillin with a penicillin allergy on record
        {
            Console.WriteLine($"=== Bed {bed} ===");
            Workflow workflow = RoundWorkflows.ParallelChecks(WardContext.Demo());

            int aggregatorCalls = 0;
            await using StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, bed);
            await foreach (WorkflowEvent evt in run.WatchStreamAsync())
            {
                switch (evt)
                {
                    case SuperStepStartedEvent:
                        Console.WriteLine("-- superstep --");
                        break;
                    // The barrier releases the three results together, but the aggregator handles them one message at a time.
                    case ExecutorInvokedEvent { ExecutorId: "ReviewAggregator" }:
                        aggregatorCalls++;
                        break;
                    case ExecutorCompletedEvent { Data: null }:
                        break;   // handlers that only send or yield complete with no data of their own
                    case ExecutorCompletedEvent done:
                        Console.WriteLine($"   [{done.ExecutorId}] {done.Data}");
                        break;
                    case WorkflowOutputEvent { Data: ReviewCard card }:
                        Console.WriteLine($"   [ReviewAggregator] called {aggregatorCalls} times, once per result; the card comes from the last call");
                        Console.WriteLine();
                        Console.WriteLine($"Review card for {card.Draft}");
                        foreach (CheckResult check in card.Checks)
                        {
                            Console.WriteLine($"   {(check.Passed ? "PASS" : "FAIL")}  {check.Check,-9} {check.Detail}");
                        }
                        Console.WriteLine($"   => {(card.AllPassed ? "ready for sign-off" : "not ready")}");
                        break;
                    case WorkflowErrorEvent error:
                        Console.WriteLine($"Error: {error.Exception?.Message}");
                        break;
                }
            }
            Console.WriteLine();
        }
    }
}
