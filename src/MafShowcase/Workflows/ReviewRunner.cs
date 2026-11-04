using System.Collections.Concurrent;
using Microsoft.Agents.AI.Workflows;

namespace MafShowcase.Workflows;

/// <summary>What an endpoint returns: either a card waiting for a signature (with its id), or the final result.</summary>
public sealed record ReviewOutcome(string? PendingId, ReviewCard? Card, string? Result);

/// <summary>
/// How a web API runs a workflow that waits for a person: the non-streaming Run stops when it is idle or
/// waiting for a response. The paused run is kept in memory under an id the browser gets back; the decision
/// request finds it by that id and resumes it. Lost on restart (checkpoints are the durable version).
/// In MedTrack this is a singleton behind two endpoints: start and decide.
/// </summary>
public sealed class ReviewRunner
{
    private readonly ConcurrentDictionary<string, (Run Run, ExternalRequest Request)> _pending = new();

    public async Task<ReviewOutcome> StartAsync(Workflow workflow, string message, CancellationToken cancellationToken = default)
    {
        Run run = await InProcessExecution.RunAsync(workflow, message);
        return await NextAsync(run);
    }

    /// <summary>Returns null when the id is unknown or was already decided (the endpoint answers 404).</summary>
    public async Task<ReviewOutcome?> DecideAsync(string pendingId, NurseDecision decision, CancellationToken cancellationToken = default)
    {
        if (!_pending.TryRemove(pendingId, out var entry))
        {
            return null;
        }
        await entry.Run.ResumeAsync([entry.Request.CreateResponse(decision)], cancellationToken);
        return await NextAsync(entry.Run);
    }

    private async Task<ReviewOutcome> NextAsync(Run run)
    {
        foreach (WorkflowEvent evt in run.NewEvents)
        {
            switch (evt)
            {
                case RequestInfoEvent request when request.Request.TryGetDataAs<ReviewCard>(out ReviewCard? card):
                    string id = Guid.NewGuid().ToString("N");
                    _pending[id] = (run, request.Request);
                    return new ReviewOutcome(id, card, null);
                case WorkflowOutputEvent output:
                    await run.DisposeAsync();
                    return new ReviewOutcome(null, null, output.Data?.ToString());
                case WorkflowErrorEvent error:
                    await run.DisposeAsync();
                    throw new InvalidOperationException("The review workflow failed.", error.Exception);
            }
        }
        await run.DisposeAsync();
        return new ReviewOutcome(null, null, "The workflow ended without a result.");
    }
}
