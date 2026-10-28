using MafShowcase.Guardrails;
using MafShowcase.Tools;
using MafShowcase.Ward;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace MafShowcase.Agents;

/// <summary>
/// Session 2: the ward assistant with every guardrail in place. Read it top to bottom as the order
/// a request travels through:
///
///   run middleware      input guard (classifier, structured output) ... output guard
///   ChatClientAgent     instructions, read tools, record_administration (approval required)
///   function middleware tool guard, after any approval, before the tool runs
///   chat client         pseudonymiser, on every model call
///   OpenAI
/// </summary>
public static class GuardedWardAssistantFactory
{
    public const string Instructions = """
        You are the ward assistant for nurses on one hospital ward.
        You answer questions about the ward's patients and the medications due for them,
        and you can draft a medication administration record for the nurse to approve.
        Patients and MRNs appear as placeholders such as PATIENT_1 and MRN_1. Use them exactly
        as written and never invent one.
        Use the tools for every fact. Never guess. If a tool returns nothing, say that no data was found.
        Always state a dose as a value and a unit together.
        To draft a record, call record_administration with the values exactly as get_due_medications
        returned them. Nothing is recorded until the nurse approves the draft.
        """;

    /// <param name="chatClient">The plain chat client from WardAssistantFactory.CreateChatClient(), or a fake in tests.</param>
    public static AIAgent Create(IChatClient chatClient, WardContext ward)
    {
        IReadOnlyList<PatientSummary> patients = ward.Queries.ListPatients(ward.WardCode);

        // 1. Chat client layer: every model call, including the guard's own, sees placeholders only.
        var map = PseudonymMap.ForPatients(patients, Mrn.Pattern);
        IChatClient redacted = new PseudonymisingChatClient(chatClient, map);

        // 2. The guards.
        var classifier = new ChatClientAgent(redacted, new ChatClientAgentOptions
        {
            Name = "ScopeClassifier",
            ChatOptions = new ChatOptions { Instructions = InputGuard.ClassifierInstructions },
        });
        var runGuardrails = new RunGuardrails(
            new InputGuard(classifier),
            new OutputGuard(map, Mrn.Pattern, patients.Select(p => p.Mrn)));
        var validator = new AdministrationValidator(ward);
        var toolGuard = new ToolGuard(ward, validator);

        // 3. The agent: read tools plus one write tool that requires approval.
        var wardTools = new WardTools(ward.Queries, ward.Time, ward.WardCode);
        var recordingTools = new RecordingTools(ward);

        AIAgent agent = new ChatClientAgent(redacted, new ChatClientAgentOptions
        {
            Name = "WardAssistant",
            Description = "Answers questions about ward patients and due medications, and drafts administration records for approval.",
            ChatOptions = new ChatOptions
            {
                Instructions = Instructions,
                Tools = [.. wardTools.AsAITools(), .. recordingTools.AsAITools()],
            },
        });

        // 4. Wrap it: run middleware (input and output guards) and function middleware (tool guard).
        return agent
            .AsBuilder()
            .Use(runFunc: runGuardrails.RunAsync, runStreamingFunc: runGuardrails.RunStreamingAsync)
            .Use(toolGuard.InvokeAsync)
            .Build();
    }
}
