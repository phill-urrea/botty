using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Botty.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Botty.LLM.Providers;

/// <summary>
/// Google Gemini embedding provider using gemini-embedding-001 (768 dimensions).
/// </summary>
public class GeminiEmbeddingProvider : IEmbeddingProvider
{
    private const string DefaultModel = "gemini-embedding-001";
    private const int DefaultDimensions = 768;
    private const string ApiBase = "https://generativelanguage.googleapis.com/v1beta/models";

    private readonly HttpClient _httpClient;
    private readonly ILogger<GeminiEmbeddingProvider> _logger;
    private readonly string _model;
    private readonly string _apiKey;

    public GeminiEmbeddingProvider(
        string apiKey,
        ILogger<GeminiEmbeddingProvider> logger,
        string? model = null)
    {
        _logger = logger;
        _apiKey = apiKey;
        _model = string.IsNullOrEmpty(model) ? DefaultModel : model;
        _httpClient = new HttpClient();
    }

    public int Dimensions => DefaultDimensions;
    public string ProviderId => "gemini";
    public string ModelName => _model;

    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var url = $"{ApiBase}/{_model}:embedContent?key={_apiKey}";
        var request = new
        {
            model = $"models/{_model}",
            content = new { parts = new[] { new { text } } }
        };

        var response = await _httpClient.PostAsJsonAsync(url, request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GeminiEmbedContentResponse>(ct)
            ?? throw new InvalidOperationException("Failed to deserialize Gemini embedding response");

        _logger.LogDebug("Gemini embedding generated using {Model}", _model);

        return EmbeddingNormalizer.NormalizeEmbedding(result.Embedding.Values);
    }

    public async Task<IList<float[]>> GetEmbeddingsAsync(IList<string> texts, CancellationToken ct = default)
    {
        var url = $"{ApiBase}/{_model}:batchEmbedContents?key={_apiKey}";
        var requests = texts.Select(text => new
        {
            model = $"models/{_model}",
            content = new { parts = new[] { new { text } } }
        }).ToList();

        var batchRequest = new { requests };
        var response = await _httpClient.PostAsJsonAsync(url, batchRequest, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GeminiBatchEmbedResponse>(ct)
            ?? throw new InvalidOperationException("Failed to deserialize Gemini batch embedding response");

        _logger.LogDebug("Gemini batch embeddings generated for {Count} texts using {Model}", texts.Count, _model);

        return result.Embeddings
            .Select(e => EmbeddingNormalizer.NormalizeEmbedding(e.Values))
            .ToList();
    }

    private sealed class GeminiEmbedContentResponse
    {
        [JsonPropertyName("embedding")]
        public GeminiEmbedding Embedding { get; set; } = new();
    }

    private sealed class GeminiBatchEmbedResponse
    {
        [JsonPropertyName("embeddings")]
        public List<GeminiEmbedding> Embeddings { get; set; } = [];
    }

    private sealed class GeminiEmbedding
    {
        [JsonPropertyName("values")]
        public float[] Values { get; set; } = [];
    }
}
