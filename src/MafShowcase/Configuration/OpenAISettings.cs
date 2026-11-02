using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace MafShowcase.Configuration;

/// <summary>
/// Everything the AI layer needs from configuration, read from one "OpenAI" section.
/// The key comes from user-secrets; the models and reasoning effort come from appsettings.json.
/// Session 3 adds the embedding model: the same key serves chat and embeddings.
/// </summary>
public sealed record OpenAISettings(
    string ApiKey,
    string ChatModel,
    ReasoningEffort ReasoningEffort,
    string EmbeddingModel = "text-embedding-3-small")
{
    public const string SectionName = "OpenAI";

    public static OpenAISettings FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);

        var apiKey = section["ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "OpenAI:ApiKey is not set. Store it with user-secrets, never in a file in the repo: " +
                "dotnet user-secrets set \"OpenAI:ApiKey\" \"<key>\" --project src/MafShowcase");
        }

        var chatModel = section["ChatModel"];
        if (string.IsNullOrWhiteSpace(chatModel))
        {
            throw new InvalidOperationException("OpenAI:ChatModel is not set in appsettings.json.");
        }

        var embeddingModel = section["EmbeddingModel"];
        if (string.IsNullOrWhiteSpace(embeddingModel))
        {
            throw new InvalidOperationException("OpenAI:EmbeddingModel is not set in appsettings.json.");
        }

        var effort = Enum.TryParse<ReasoningEffort>(section["ReasoningEffort"], ignoreCase: true, out var parsed)
            ? parsed
            : ReasoningEffort.Low;

        return new OpenAISettings(apiKey, chatModel, effort, embeddingModel);
    }
}
