using MafShowcase.Agents;
using MafShowcase.Rag;
using MafShowcase.Ward;
using MafShowcase.Workflows;
using Microsoft.Agents.AI.Workflows;

namespace MafShowcase.Steps;

// Step 18 (trainer only): the server "restarts" while the workflow waits for the nurse.
// A checkpoint is saved at the end of every superstep; a new workflow instance resumes from the last one,
// the pending sign-off request is raised again, and the agents do not run a second time.
public static class Step18_Checkpoints
{
    private const string Message = "I just gave the paracetamol to the patient in bed 4.";

    public static async Task RunAsync(WardAssistantFactory factory)
    {
        var ward = WardContext.Demo();
        using var index = new ProtocolIndex(RagPaths.DatabaseFile, factory.CreateEmbeddingGenerator());

        // In memory for the demo. A file store exists in the package source (FileSystemJsonCheckpointStore) but is not on Learn yet.
        CheckpointManager checkpoints = CheckpointManager.CreateInMemory();
        CheckpointInfo? last = null;
        string? lastDone = null;   // print each executor once in a row (the aggregator and the port complete more than once)

        Console.WriteLine("=== First run: stop while waiting for the nurse ===");
        await using (StreamingRun run = await InProcessExecution.RunStreamingAsync(
            AdministrationReviewWorkflow.Create(factory.CreateChatClient(), index, ward), Message, checkpoints))
        {
            await foreach (WorkflowEvent evt in run.WatchStreamAsync())
            {
                if (evt is SuperStepCompletedEvent step && step.CompletionInfo?.Checkpoint is { } checkpoint)
                {
                    last = checkpoint;
                    Console.WriteLine($"   checkpoint saved ({run.Checkpoints.Count} so far)");
                }
                if (evt is ExecutorCompletedEvent done && done.ExecutorId != lastDone)
                {
                    Console.WriteLine($"[{done.ExecutorId}] done");
                    lastDone = done.ExecutorId;
                }
                if (evt is RequestInfoEvent)
                {
                    Console.WriteLine("Sign-off requested. Simulating a restart: the run is disposed, nothing answered.");
                    break;
                }
            }
        }

        if (last is null)
        {
            Console.WriteLine("No checkpoint was saved.");
            return;
        }

        Console.WriteLine();
        Console.WriteLine("=== After the restart: a NEW workflow instance resumes from the last checkpoint ===");
        Workflow rebuilt = AdministrationReviewWorkflow.Create(factory.CreateChatClient(), index, ward);   // same topology, same ids
        await using StreamingRun resumed = await InProcessExecution.ResumeStreamingAsync(rebuilt, last, checkpoints);
        await foreach (WorkflowEvent evt in resumed.WatchStreamAsync())
        {
            switch (evt)
            {
                case ExecutorCompletedEvent done when done.ExecutorId != lastDone:
                    Console.WriteLine($"[{done.ExecutorId}] done");
                    lastDone = done.ExecutorId;
                    break;
                case RequestInfoEvent request when request.Request.TryGetDataAs<ReviewCard>(out ReviewCard? card):
                    Console.WriteLine("The pending sign-off came back from the checkpoint. No agent ran again.");
                    Step17_AgentsAndSignOff.ShowCard(card);
                    bool approved = Step17_AgentsAndSignOff.Ask("Sign and record? (y/n) ");
                    await resumed.SendResponseAsync(request.Request.CreateResponse(new NurseDecision(approved, ward.NurseId)));
                    break;
                case WorkflowOutputEvent output:
                    Console.WriteLine(output.Data);
                    return;
                case WorkflowErrorEvent error:
                    Console.WriteLine($"Error: {error.Exception?.Message}");
                    return;
            }
        }
    }
}
