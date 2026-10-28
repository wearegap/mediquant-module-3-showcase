using Microsoft.Extensions.AI;

namespace MafShowcase.Guardrails;

/// <summary>
/// Showcase only: prints what actually leaves the process. Placed between the pseudonymiser and OpenAI,
/// it shows the room exactly what the model provider receives.
/// </summary>
public sealed class WireTapChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Print(messages);
        return base.GetResponseAsync(messages, options, cancellationToken);
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Print(messages);
        return base.GetStreamingResponseAsync(messages, options, cancellationToken);
    }

    private static void Print(IEnumerable<ChatMessage> messages)
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine("  [sent to OpenAI]");
        foreach (ChatMessage message in messages)
        {
            foreach (AIContent content in message.Contents)
            {
                string? line = content switch
                {
                    TextContent t => $"{message.Role}: {t.Text}",
                    FunctionCallContent c => $"tool call: {c.Name}({string.Join(", ", c.Arguments?.Select(a => $"{a.Key}={a.Value}") ?? [])})",
                    FunctionResultContent r => $"tool result: {r.Result}",
                    _ => null,
                };
                if (line is not null)
                {
                    Console.WriteLine($"    {line}");
                }
            }
        }
        Console.ResetColor();
    }
}
