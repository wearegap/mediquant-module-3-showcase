using MafShowcase.Agents;
using MafShowcase.Guardrails;
using MafShowcase.Tools;
using MafShowcase.Ward;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace MafShowcase.Steps;

// Step 09: the model proposes, the nurse decides. Run it twice: approve once, reject once.
public static class Step09_ToolApproval
{
    public static async Task RunAsync(WardAssistantFactory factory)
    {
        var ward = WardContext.Demo();
        var validator = new AdministrationValidator(ward);
        AIAgent agent = GuardedWardAssistantFactory.Create(factory.CreateChatClient(), ward);
        AgentSession session = await agent.CreateSessionAsync();

        const string request = "I just gave the paracetamol to the patient in bed 4. Please draft the record.";
        Console.WriteLine($"> {request}");
        AgentResponse response = await agent.RunAsync(request, session);

        // The run stops at the write tool and hands the decision back to us. Loop until nothing is pending.
        List<ToolApprovalRequestContent> pending;
        while ((pending = Pending(response)).Count > 0)
        {
            var replies = new List<AIContent>();
            foreach (ToolApprovalRequestContent approval in pending)
            {
                var draft = AdministrationDraft.FromArguments(((FunctionCallContent)approval.ToolCall).Arguments!);
                IReadOnlyList<string> problems = validator.Validate(draft);

                Console.WriteLine();
                Console.WriteLine("+-- Draft for approval -------------------------------");
                Console.WriteLine($"| Patient    {draft.Mrn}");
                Console.WriteLine($"| Medication {draft.Medication} {draft.DoseValue} {draft.DoseUnit} {draft.Route}");
                Console.WriteLine($"| Order      {draft.OrderId}");
                Console.WriteLine($"| Checks     {(problems.Count == 0 ? "match the order" : string.Join(" ", problems))}");
                Console.WriteLine("+----------------------------------------------------");

                // An invalid draft is never offered for approval: it is rejected with the validator's reasons.
                bool approved = problems.Count == 0 && Ask("Approve this record? (y/n) ");
                string? reason = approved ? null : problems.Count > 0 ? string.Join(" ", problems) : "The nurse rejected the draft.";
                replies.Add(approval.CreateResponse(approved, reason));
            }

            // The decision goes back as a new run in the same session. The input guard lets it through (no text).
            response = await agent.RunAsync(new ChatMessage(ChatRole.User, replies), session);
        }

        Console.WriteLine();
        Console.WriteLine(response.Text);
    }

    private static List<ToolApprovalRequestContent> Pending(AgentResponse response) =>
        response.Messages.SelectMany(m => m.Contents).OfType<ToolApprovalRequestContent>().ToList();

    private static bool Ask(string question)
    {
        Console.Write(question);
        return Console.ReadLine()?.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase) == true;
    }
}
