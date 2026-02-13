using Botty.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Botty.LLM.Providers;

/// <summary>
/// A placeholder embedding provider that generates random embeddings.
/// Used for development/testing until a real provider is configured.
/// </summary>
public class PlaceholderEmbeddingProvider : IEmbeddingProvider
{
    private readonly ILogger<PlaceholderEmbeddingProvider> _logger;
    private readonly Random _random = new(42); // Fixed seed for reproducibility

    public PlaceholderEmbeddingProvider(ILogger<PlaceholderEmbeddingProvider> logger)
    {
        _logger = logger;
    }

    public int Dimensions => 1536;
    public string ProviderId => "placeholder";
    public string ModelName => "hash-based-fake";

    public Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        _logger.LogWarning("Using placeholder embedding provider - replace with real provider for production");
        
        // Generate a deterministic "embedding" based on text hash
        // This is NOT a real embedding, just for development/testing
        var hash = text.GetHashCode();
        var random = new Random(hash);
        
        var embedding = new float[Dimensions];
        for (var i = 0; i < Dimensions; i++)
        {
            embedding[i] = (float)(random.NextDouble() * 2 - 1);
        }
        
        // Normalize the vector
        var magnitude = Math.Sqrt(embedding.Sum(x => x * x));
        for (var i = 0; i < Dimensions; i++)
        {
            embedding[i] /= (float)magnitude;
        }
        
        return Task.FromResult(embedding);
    }

    public async Task<IList<float[]>> GetEmbeddingsAsync(IList<string> texts, CancellationToken ct = default)
    {
        var embeddings = new List<float[]>();
        foreach (var text in texts)
        {
            embeddings.Add(await GetEmbeddingAsync(text, ct));
        }
        return embeddings;
    }
}
