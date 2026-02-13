using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Botty.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Botty.LLM.Providers;

/// <summary>
/// OpenAI embedding provider using the text-embedding-3-small model (1536 dimensions).
/// </summary>
public class OpenAiEmbeddingProvider : IEmbeddingProvider
{
    private const string DefaultModel = "text-embedding-3-small";
    private const int DefaultDimensions = 1536;
    private const string ApiUrl = "https://api.openai.com/v1/embeddings";

    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAiEmbeddingProvider> _logger;
    private readonly string _model;

    public OpenAiEmbeddingProvider(
        string apiKey,
        ILogger<OpenAiEmbeddingProvider> logger,
        string? model = null)
    {
        _logger = logger;
        _model = string.IsNullOrEmpty(model) ? DefaultModel : model;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    }

    public int Dimensions => DefaultDimensions;
    public string ProviderId => "openai";
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

        var result = await response.Content.ReadFromJsonAsync<OpenAiEmbeddingResponse>(ct)
            ?? throw new InvalidOperationException("Failed to deserialize OpenAI embedding response");

        _logger.LogDebug("OpenAI embeddings generated for {Count} texts using {Model}", texts.Count, _model);

        return result.Data
            .OrderBy(d => d.Index)
            .Select(d => EmbeddingNormalizer.NormalizeEmbedding(d.Embedding))
            .ToList();
    }

    private sealed class OpenAiEmbeddingResponse
    {
        [JsonPropertyName("data")]
        public List<EmbeddingData> Data { get; set; } = [];
    }

    private sealed class EmbeddingData
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("embedding")]
        public float[] Embedding { get; set; } = [];
    }
}
