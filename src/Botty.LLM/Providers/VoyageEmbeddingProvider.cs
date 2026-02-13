using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Botty.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Botty.LLM.Providers;

/// <summary>
/// Voyage AI embedding provider using voyage-4-large (1024 dimensions).
/// </summary>
public class VoyageEmbeddingProvider : IEmbeddingProvider
{
    private const string DefaultModel = "voyage-4-large";
    private const int DefaultDimensions = 1024;
    private const string ApiUrl = "https://api.voyageai.com/v1/embeddings";

    private readonly HttpClient _httpClient;
    private readonly ILogger<VoyageEmbeddingProvider> _logger;
    private readonly string _model;

    public VoyageEmbeddingProvider(
        string apiKey,
        ILogger<VoyageEmbeddingProvider> logger,
        string? model = null)
    {
        _logger = logger;
        _model = string.IsNullOrEmpty(model) ? DefaultModel : model;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    }

    public int Dimensions => DefaultDimensions;
    public string ProviderId => "voyage";
    public string ModelName => _model;

    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var results = await GetEmbeddingsAsync([text], ct);
        return results[0];
    }

    public async Task<IList<float[]>> GetEmbeddingsAsync(IList<string> texts, CancellationToken ct = default)
    {
        var request = new { model = _model, input = texts };
        var response = await _httpClient.PostAsJsonAsync(ApiUrl, request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<VoyageEmbeddingResponse>(ct)
            ?? throw new InvalidOperationException("Failed to deserialize Voyage embedding response");

        _logger.LogDebug("Voyage embeddings generated for {Count} texts using {Model}", texts.Count, _model);

        return result.Data
            .OrderBy(d => d.Index)
            .Select(d => EmbeddingNormalizer.NormalizeEmbedding(d.Embedding))
            .ToList();
    }

    private sealed class VoyageEmbeddingResponse
    {
        [JsonPropertyName("data")]
        public List<VoyageEmbeddingData> Data { get; set; } = [];
    }

    private sealed class VoyageEmbeddingData
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("embedding")]
        public float[] Embedding { get; set; } = [];
    }
}
