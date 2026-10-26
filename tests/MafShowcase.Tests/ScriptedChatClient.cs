using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace MafShowcase.Tests;

/// <summary>
/// A fake IChatClient that replays a fixed script of responses and records what it was sent.
/// Unit tests use this instead of OpenAI: no network, no cost, same result every run.
/// </summary>
public sealed class ScriptedChatClient(params ChatResponse[] script) : IChatClient
{
    private readonly Queue<ChatResponse> _script = new(script);

    public List<List<ChatMessage>> Requests { get; } = [];

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Requests.Add(messages.ToList());
        if (_script.Count == 0)
        {
            throw new InvalidOperationException("The scripted chat client ran out of responses.");
        }
        return Task.FromResult(_script.Dequeue());
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ChatResponse response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (ChatResponseUpdate update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    public static ChatResponse ToolCall(string callId, string toolName, Dictionary<string, object?> arguments) =>
        new(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(callId, toolName, arguments)]));

    public static ChatResponse Text(string text) =>
        new(new ChatMessage(ChatRole.Assistant, text));
}
