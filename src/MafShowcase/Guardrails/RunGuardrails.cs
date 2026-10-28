using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace MafShowcase.Guardrails;

/// <summary>
/// Agent run middleware: wraps the whole run. Input guard before the agent, output guard after it.
/// Registered with agent.AsBuilder().Use(runFunc: RunAsync, runStreamingFunc: RunStreamingAsync).
/// </summary>
public sealed class RunGuardrails(InputGuard input, OutputGuard output)
{
    public async Task<AgentResponse> RunAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        AIAgent innerAgent,
        CancellationToken cancellationToken)
    {
        GuardResult check = await CheckInputAsync(messages, cancellationToken);
        if (check.Blocked)
        {
            return Reply(check.Message);
        }

        AgentResponse response = await innerAgent.RunAsync(messages, session, options, cancellationToken);

        if (output.FindViolation(response.Text) is { } violation)
        {
            Console.WriteLine($"[output guard] WITHDRAWN: {violation}");
            return Reply(OutputGuard.Withdrawn);
        }

        return response;
    }

    public async IAsyncEnumerable<AgentResponseUpdate> RunStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        AIAgent innerAgent,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        GuardResult check = await CheckInputAsync(messages, cancellationToken);
        if (check.Blocked)
        {
            yield return new AgentResponseUpdate(ChatRole.Assistant, check.Message);
            yield break;
        }

        // Streaming trade-off: words reach the nurse before the whole answer exists, so the output guard
        // checks the text so far after every update and withdraws the answer the moment a check fails.
        // The alternative, buffering the whole answer before releasing it, is fail-closed but loses streaming.
        var soFar = new StringBuilder();
        await foreach (AgentResponseUpdate update in innerAgent.RunStreamingAsync(messages, session, options, cancellationToken))
        {
            soFar.Append(update.Text);
            if (output.FindViolation(soFar.ToString()) is { } violation)
            {
                Console.WriteLine($"[output guard] WITHDRAWN: {violation}");
                yield return new AgentResponseUpdate(ChatRole.Assistant, Environment.NewLine + OutputGuard.Withdrawn)
                {
                    AdditionalProperties = new() { ["withdrawn"] = true },
                };
                yield break;
            }

            yield return update;
        }
    }

    private Task<GuardResult> CheckInputAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken)
    {
        // Only the newest user text is checked. A run that carries only an approval response has no text: let it through.
        string? text = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text;
        return string.IsNullOrWhiteSpace(text) ? Task.FromResult(GuardResult.Allow) : input.CheckAsync(text, cancellationToken);
    }

    private static AgentResponse Reply(string text) => new(new ChatMessage(ChatRole.Assistant, text));
}
