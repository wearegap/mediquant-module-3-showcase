using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace MafShowcase.Guardrails;

/// <summary>
/// Chat client middleware: the last layer before the model provider.
/// It sits inside the agent's tool loop, so it runs on EVERY model call: the nurse's message,
/// the history, and every tool result on its way back to the model.
/// Out: real names and MRNs become placeholders. In: placeholders become real values again,
/// in the text the nurse reads and in the tool arguments our code runs with.
/// </summary>
public sealed class PseudonymisingChatClient(IChatClient innerClient, PseudonymMap map)
    : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        ChatResponse response = await base.GetResponseAsync(Pseudonymise(messages), options, cancellationToken);

        foreach (AIContent content in response.Messages.SelectMany(m => m.Contents))
        {
            Restore(content, map.Restore);
        }

        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // A placeholder can be split across chunks, so streamed text goes through a restorer that holds back partial ones.
        var restorer = new StreamingRestorer(map);
        ChatResponseUpdate? last = null;

        await foreach (ChatResponseUpdate update in base.GetStreamingResponseAsync(Pseudonymise(messages), options, cancellationToken))
        {
            foreach (AIContent content in update.Contents)
            {
                Restore(content, restorer.Push);
            }

            last = update;
            yield return update;
        }

        string rest = restorer.Flush();
        if (rest.Length > 0)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, rest)
            {
                MessageId = last?.MessageId,
                ResponseId = last?.ResponseId,
            };
        }
    }

    // ---- Outgoing: build new objects, so the history the agent keeps still holds real values ----

    private List<ChatMessage> Pseudonymise(IEnumerable<ChatMessage> messages) =>
        messages.Select(message => new ChatMessage(message.Role, message.Contents.Select(PseudonymiseContent).ToList())
        {
            AuthorName = message.AuthorName,
            MessageId = message.MessageId,
        }).ToList();

    private AIContent PseudonymiseContent(AIContent content) => content switch
    {
        TextContent text => new TextContent(map.Pseudonymise(text.Text)),
        FunctionCallContent call => new FunctionCallContent(call.CallId, call.Name, MapStrings(call.Arguments, map.Pseudonymise)),
        FunctionResultContent result => new FunctionResultContent(result.CallId, map.Pseudonymise(Serialize(result.Result))),

        // Other content (reasoning items, approval bookkeeping) passes through unchanged.
        // Rehearsal check: confirm no other content type carries patient data to the provider.
        _ => content,
    };

    // ---- Incoming: restore in place, in the text the nurse reads and in the arguments the tools receive ----

    private void Restore(AIContent content, Func<string, string> restoreText)
    {
        switch (content)
        {
            case TextContent text:
                text.Text = restoreText(text.Text);
                break;

            // The model says MRN_3; the tool must run with the real MRN. Arguments arrive whole, so no streaming restorer.
            case FunctionCallContent call:
                call.Arguments = MapStrings(call.Arguments, map.Restore);
                break;
        }
    }

    /// <summary>Applies a mapping to string arguments only; numbers and other values pass through unchanged.</summary>
    private static Dictionary<string, object?>? MapStrings(IDictionary<string, object?>? arguments, Func<string, string> mapping) =>
        arguments?.ToDictionary(
            a => a.Key,
            a => a.Value switch
            {
                string s => mapping(s),
                JsonElement { ValueKind: JsonValueKind.String } e => mapping(e.GetString() ?? ""),
                _ => a.Value,
            });

    private static string Serialize(object? value) => value switch
    {
        null => "",
        string s => s,
        _ => JsonSerializer.Serialize(value, AIJsonUtilities.DefaultOptions),
    };
}
