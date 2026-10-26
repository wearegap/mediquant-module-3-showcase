using MafShowcase.Configuration;
using MafShowcase.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;

namespace MafShowcase.Agents;

/// <summary>
/// The one place that knows how the ward assistant is built.
/// Callers receive an AIAgent; they never touch OpenAI types.
/// </summary>
public sealed class WardAssistantFactory(OpenAISettings settings)
{
    public const string Instructions = """
        You are the ward assistant for nurses on one hospital ward.
        You answer questions about the ward's patients and the medications due for them.
        Use the tools for every fact about patients or medications. Never guess.
        If a tool returns nothing, say that no data was found.
        Always state a dose as a value and a unit together.
        You cannot record, change or cancel anything. If asked to, say so.
        """;

    /// <summary>Builds the IChatClient once: OpenAI Responses API, model and reasoning effort from config.</summary>
    public IChatClient CreateChatClient() =>
        new OpenAIClient(settings.ApiKey)
            .GetResponsesClient()
            .AsIChatClient(settings.ChatModel)
            .AsBuilder()
            .ConfigureOptions(options => options.Reasoning = new() { Effort = settings.ReasoningEffort })
            .Build();

    public AIAgent Create(IChatClient chatClient, WardTools? tools = null) =>
        new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = "WardAssistant",
            Description = "Answers questions about ward patients and due medications. Read-only.",
            ChatOptions = new ChatOptions
            {
                Instructions = Instructions,
                Tools = tools?.AsAITools(),
            },
        });
}
