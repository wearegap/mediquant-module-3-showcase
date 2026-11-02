using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace MafShowcase.Tests;

/// <summary>
/// A fake embedding model for tests: each word is hashed into one of 1536 positions and the vector is normalised.
/// Texts that share words get similar vectors. No network, no key, same numbers on every run.
/// It knows nothing about meaning, so tests assert on retrieval plumbing, never on semantic quality.
/// </summary>
public sealed partial class HashingEmbeddingGenerator(int dimensions = 1536) : IEmbeddingGenerator<string, Embedding<float>>
{
    public int Calls { get; private set; }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        Calls++;
        var embeddings = new GeneratedEmbeddings<Embedding<float>>(values.Select(v => new Embedding<float>(Vectorise(v))));
        return Task.FromResult(embeddings);
    }

    private float[] Vectorise(string text)
    {
        var vector = new float[dimensions];
        foreach (Match word in Word().Matches(text.ToLowerInvariant()))
        {
            vector[(int)((uint)StableHash(word.Value) % (uint)dimensions)] += 1;
        }
        float norm = MathF.Sqrt(vector.Sum(x => x * x));
        return norm == 0 ? vector : vector.Select(x => x / norm).ToArray();
    }

    // string.GetHashCode is randomised per process; tests need the same numbers every run.
    private static int StableHash(string s)
    {
        unchecked
        {
            int hash = 23;
            foreach (char c in s) hash = hash * 31 + c;
            return hash;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    [GeneratedRegex(@"[a-z0-9]+")]
    private static partial Regex Word();
}
