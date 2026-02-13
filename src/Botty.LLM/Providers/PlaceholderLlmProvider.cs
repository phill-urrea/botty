using Botty.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Botty.LLM.Providers;

/// <summary>
/// A placeholder LLM provider that returns canned responses.
/// Used for development/testing until a real provider is configured.
/// </summary>
public class PlaceholderLlmProvider : ILlmProvider
{
    private readonly ILogger<PlaceholderLlmProvider> _logger;

    public PlaceholderLlmProvider(ILogger<PlaceholderLlmProvider> logger)
    {
        _logger = logger;
    }

    public string Name => "Placeholder";

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        _logger.LogWarning("Using placeholder LLM provider - replace with real provider for production");

        // Check if this is a memory extraction request
        var isMemoryExtraction = request.Messages
            .Any(m => m.Content?.Contains("memory extraction", StringComparison.OrdinalIgnoreCase) == true);

        string responseContent;
        
        if (isMemoryExtraction)
        {
            // Return an empty array for memory extraction
            responseContent = "[]";
        }
        else if (request.Messages.Any(m => m.Content?.Contains("SAME|ADDITION|CONTRADICTION|DIFFERENT", StringComparison.OrdinalIgnoreCase) == true))
        {
            // Memory deduplication check
            responseContent = """{"relationship": "DIFFERENT", "merged": null}""";
        }
        else
        {
            // Default response
            responseContent = "This is a placeholder response. Please configure a real LLM provider.";
        }

        return Task.FromResult(new LlmResponse
        {
            Content = responseContent,
            FinishReason = "stop",
            Usage = new TokenUsage
            {
                PromptTokens = 0,
                CompletionTokens = 0
            }
        });
    }

    public Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        // LLM providers that support embeddings can implement this
        // For Claude, we'd use a separate embedding model
        throw new NotSupportedException("Use IEmbeddingProvider for embeddings");
    }

    public async IAsyncEnumerable<StreamDelta> StreamCompleteAsync(
        LlmRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var response = await CompleteAsync(request, ct);
        yield return new StreamDelta { Type = "text", Text = response.Content };
        yield return new StreamDelta
        {
            Type = "done",
            FinishReason = response.FinishReason,
            Usage = response.Usage
        };
    }
}
