using MafShowcase.Agents;
using MafShowcase.Guardrails;
using MafShowcase.Tools;
using MafShowcase.Ward;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace MafShowcase.Steps;

// Step 06: the same question twice. Watch the yellow lines: that is what OpenAI receives.
public static class Step06_PhiRedaction
{
    private const string Question = "Who is in bed 4, and what is due for them?";

    public static async Task RunAsync(WardAssistantFactory factory)
    {
        var ward = WardContext.Demo();
        var tools = new WardTools(ward.Queries, ward.Time, ward.WardCode);
        IChatClient wire = new WireTapChatClient(factory.CreateChatClient());

        Console.WriteLine("--- Session 1 assistant: real names and MRNs go to the provider ---");
        AIAgent plain = factory.Create(wire, tools);
        Console.WriteLine((await plain.RunAsync(Question)).Text);
        Console.WriteLine();

        Console.WriteLine("--- With the pseudonymiser: the provider sees placeholders, the nurse sees real names ---");
        var map = PseudonymMap.ForPatients(ward.Queries.ListPatients(ward.WardCode), Mrn.Pattern);
        AIAgent redacted = factory.Create(new PseudonymisingChatClient(wire, map), tools);
        Console.WriteLine((await redacted.RunAsync(Question)).Text);
    }
}
