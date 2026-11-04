using MafShowcase.Ward;
using MafShowcase.Workflows;
using Microsoft.Agents.AI.Workflows;

namespace MafShowcase.Steps;

// Step 15: a workflow with no AI at all. Three executors, two edges, one output. The graph is the program.
public static class Step15_ExecutorsAndEdges
{
    public static async Task RunAsync()
    {
        Workflow workflow = RoundWorkflows.Sequential(WardContext.Demo());

        // The same graph, as a diagram you can paste into any Mermaid viewer (or a slide).
        Console.WriteLine(workflow.ToMermaidString());
        Console.WriteLine();

        await using StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, "4");
        await foreach (WorkflowEvent evt in run.WatchStreamAsync())
        {
            switch (evt)
            {
                case ExecutorCompletedEvent done:
                    Console.WriteLine($"[{done.ExecutorId}] done: {done.Data}");
                    break;
                case WorkflowOutputEvent output:
                    Console.WriteLine();
                    Console.WriteLine($"Output: {output.Data}");
                    break;
                case WorkflowErrorEvent error:
                    Console.WriteLine($"Error: {error.Exception?.Message}");
                    break;
            }
        }
    }
}
