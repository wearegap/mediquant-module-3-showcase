using MafShowcase.Agents;
using MafShowcase.Configuration;
using MafShowcase.Tools;
using MafShowcase.Ward;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Xunit;

namespace MafShowcase.Tests;

public class WardAssistantTests
{
    private static readonly OpenAISettings Settings = new("not-used-in-tests", "not-used-in-tests", ReasoningEffort.Low);

    [Fact]
    public void Tool_returns_dose_as_value_and_unit()
    {
        var tools = new WardTools(new FakeWardQueries(), TimeProvider.System, wardCode: "4B");

        var due = tools.GetDueMedications("TEST-0004");

        Assert.Contains(due, d => d.Medication == "Paracetamol" && d.DoseValue == 1 && d.DoseUnit == "g");
    }

    [Fact]
    public void Tool_returns_nothing_for_a_patient_on_another_ward()
    {
        var tools = new WardTools(new FakeWardQueries(), TimeProvider.System, wardCode: "7A");

        Assert.Empty(tools.GetDueMedications("TEST-0004"));
    }

    [Fact]
    public async Task Agent_runs_the_tool_the_model_asks_for_and_returns_the_final_answer()
    {
        var chat = new ScriptedChatClient(
            ScriptedChatClient.ToolCall("call-1", "get_due_medications", new() { ["mrn"] = "TEST-0004" }),
            ScriptedChatClient.Text("Paracetamol 1 g oral is due next."));

        var tools = new WardTools(new FakeWardQueries(), TimeProvider.System, wardCode: "4B");
        AIAgent agent = new WardAssistantFactory(Settings).Create(chat, tools);

        AgentResponse response = await agent.RunAsync("What is due for bed 4?");

        Assert.Equal("Paracetamol 1 g oral is due next.", response.Text);

        // The second request to the model must carry the tool result back to it.
        var toolResult = chat.Requests[1]
            .SelectMany(m => m.Contents)
            .OfType<FunctionResultContent>()
            .Single();
        Assert.Contains("Paracetamol", toolResult.Result?.ToString());
    }
}
