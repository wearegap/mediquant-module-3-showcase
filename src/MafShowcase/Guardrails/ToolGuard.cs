using MafShowcase.Tools;
using MafShowcase.Ward;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace MafShowcase.Guardrails;

/// <summary>
/// Function calling middleware: the tool guardrail. It runs every time the agent is about to run a tool,
/// after any human approval, and checks the arguments the model chose. If a check fails, the tool does
/// not run and the model receives a plain reason it can relay, instead of an exception.
/// </summary>
public sealed class ToolGuard(WardContext ward, AdministrationValidator validator)
{
    public async ValueTask<object?> InvokeAsync(
        AIAgent agent,
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next,
        CancellationToken cancellationToken)
    {
        // Any tool that takes an MRN: the patient must be on this ward (allow-list, not block-list).
        if (context.Arguments.TryGetValue("mrn", out object? mrn) && !IsOnWard(mrn?.ToString()))
        {
            return Refuse(context, "That patient is not on this ward. Nothing was looked up or changed.");
        }

        // The write tool: the draft must match the real order, even if a nurse approved something else.
        if (context.Function.Name == RecordingTools.RecordAdministrationName)
        {
            var draft = AdministrationDraft.FromArguments(context.Arguments);
            IReadOnlyList<string> problems = validator.Validate(draft);
            if (problems.Count > 0)
            {
                return Refuse(context, "Not recorded. " + string.Join(" ", problems));
            }
        }

        return await next(context, cancellationToken);
    }

    private bool IsOnWard(string? mrn) =>
        ward.Queries.ListPatients(ward.WardCode).Any(p => string.Equals(p.Mrn, mrn, StringComparison.OrdinalIgnoreCase));

    private static string Refuse(FunctionInvocationContext context, string reason)
    {
        Console.WriteLine($"[tool guard] BLOCKED {context.Function.Name}: {reason}");
        return reason;
    }
}
