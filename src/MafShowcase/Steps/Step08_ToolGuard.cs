using MafShowcase.Agents;
using MafShowcase.Guardrails;
using MafShowcase.Tools;
using MafShowcase.Ward;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace MafShowcase.Steps;

// Step 08: the tool guard checks the arguments the model chose, every time, after any approval.
public static class Step08_ToolGuard
{
    public static async Task RunAsync(WardAssistantFactory factory)
    {
        var ward = WardContext.Demo();
        AIAgent agent = GuardedWardAssistantFactory.Create(factory.CreateChatClient(), ward);

        // The pseudonymiser already turns TEST-0099 into MRN_UNKNOWN on the way out; the tool guard is the second line.
        Console.WriteLine("--- 1. A patient from another ward (TEST-0099 is on 7A) ---");
        Console.WriteLine((await agent.RunAsync("What is due for the patient with MRN TEST-0099?")).Text);
        Console.WriteLine();

        Console.WriteLine("--- 2. A wrong dose, and a careless 'approve' ---");
        AgentSession session = await agent.CreateSessionAsync();
        AgentResponse response = await agent.RunAsync(
            "Paracetamol for bed 4 was just given, but 2 g instead of the usual dose. Draft the record with 2 g.", session);

        // The model may refuse, or quietly correct the dose (persuasion working this time). Either is fine to show.
        // The deterministic proof is the unit test Approved_invalid_draft_is_blocked_by_the_tool_guard.
        var validator = new AdministrationValidator(ward);
        foreach (ToolApprovalRequestContent request in ApprovalRequests(response))
        {
            var draft = AdministrationDraft.FromArguments(((FunctionCallContent)request.ToolCall).Arguments!);
            IReadOnlyList<string> problems = validator.Validate(draft);
            Console.WriteLine($"Draft: {draft}");
            Console.WriteLine($"Validator: {(problems.Count > 0 ? string.Join(" ", problems) : "matches the order")}");

            if (problems.Count == 0)
            {
                Console.WriteLine("The model corrected the dose itself. Rejecting, to keep the demo about the guard.");
                response = await agent.RunAsync(new ChatMessage(ChatRole.User, [request.CreateResponse(false, "Demo only.")]), session);
                continue;
            }

            // The demo approves on purpose, to prove that even a human approval cannot push an invalid record through.
            Console.WriteLine("Approving anyway...");
            response = await agent.RunAsync(new ChatMessage(ChatRole.User, [request.CreateResponse(true)]), session);
        }

        Console.WriteLine(response.Text);
        Console.WriteLine($"Records written: {((FakeAdministrationRecorder)ward.Recorder).Recorded.Count}");
    }

    private static IEnumerable<ToolApprovalRequestContent> ApprovalRequests(AgentResponse response) =>
        response.Messages.SelectMany(m => m.Contents).OfType<ToolApprovalRequestContent>().ToList();
}
