using Botty.Core.Interfaces;
using Microsoft.Extensions.Logging;
using MemoryModel = Botty.Core.Models.Memory;

namespace Botty.Memory.Services;

/// <summary>
/// Checks for duplicate or contradictory memories.
/// </summary>
public class MemoryDeduplicator : IMemoryDeduplicator
{
    private readonly ILlmProvider _llmProvider;
    private readonly ILogger<MemoryDeduplicator> _logger;

    private const decimal ExactDuplicateThreshold = 0.95m;
    private const decimal SimilarThreshold = 0.85m;
    private const decimal ContradictionCheckThreshold = 0.7m;

    private const string ContradictionPrompt = """
        Compare these two statements and determine their relationship:

        Statement 1: {0}
        Statement 2: {1}

        Respond with exactly one of:
        - SAME: They express the same fact
        - ADDITION: Statement 2 adds new details to Statement 1
        - CONTRADICTION: Statement 2 contradicts Statement 1
        - DIFFERENT: They are about different topics

        If ADDITION or SAME, also provide a merged statement that combines both.

        Response format (JSON):
        {{"relationship": "SAME|ADDITION|CONTRADICTION|DIFFERENT", "merged": "merged statement if applicable"}}
        """;

    public MemoryDeduplicator(ILlmProvider llmProvider, ILogger<MemoryDeduplicator> logger)
    {
        _llmProvider = llmProvider;
        _logger = logger;
    }

    public async Task<DeduplicationResult> CheckDuplicateAsync(
        ExtractedMemory memory,
        float[] embedding,
        IEnumerable<MemoryModel> existingMemories,
        CancellationToken ct = default)
    {
        var memoryList = existingMemories.ToList();
        
        if (memoryList.Count == 0)
        {
            return new DeduplicationResult
            {
                Action = DeduplicationAction.Insert,
                SimilarityScore = 0
            };
        }

        // Find the most similar existing memory
        var (mostSimilar, similarity) = FindMostSimilar(memory.Content, memoryList);

        _logger.LogDebug("Most similar existing memory has similarity {Similarity:F2}: {Content}",
            similarity, mostSimilar?.Content?.Length > 50 
                ? mostSimilar?.Content?[..50] + "..." 
                : mostSimilar?.Content);

        // Exact duplicate - skip
        if (similarity >= ExactDuplicateThreshold)
        {
            _logger.LogInformation("Skipping exact duplicate memory: {Content}",
                memory.Content.Length > 50 ? memory.Content[..50] + "..." : memory.Content);
            
            return new DeduplicationResult
            {
                Action = DeduplicationAction.Skip,
                ExistingMemory = mostSimilar,
                SimilarityScore = similarity
            };
        }

        // Very similar - need to check if it's an addition or contradiction
        if (similarity >= ContradictionCheckThreshold && mostSimilar != null)
        {
            var relationship = await CheckRelationshipAsync(mostSimilar.Content, memory.Content, ct);
            
            return relationship.Relationship switch
            {
                "SAME" => new DeduplicationResult
                {
                    Action = DeduplicationAction.Skip,
                    ExistingMemory = mostSimilar,
                    SimilarityScore = similarity
                },
                "ADDITION" => new DeduplicationResult
                {
                    Action = DeduplicationAction.Merge,
                    ExistingMemory = mostSimilar,
                    MergedContent = relationship.Merged ?? memory.Content,
                    SimilarityScore = similarity
                },
                "CONTRADICTION" => new DeduplicationResult
                {
                    Action = DeduplicationAction.Supersede,
                    ExistingMemory = mostSimilar,
                    SimilarityScore = similarity
                },
                _ => new DeduplicationResult
                {
                    Action = DeduplicationAction.Insert,
                    SimilarityScore = similarity
                }
            };
        }

        // Not similar enough - insert as new
        return new DeduplicationResult
        {
            Action = DeduplicationAction.Insert,
            SimilarityScore = similarity
        };
    }

    private static (MemoryModel?, decimal) FindMostSimilar(string content, IList<MemoryModel> existingMemories)
    {
        if (existingMemories.Count == 0)
        {
            return (null, 0);
        }

        // Simple word-based similarity (embedding similarity would be better)
        var contentWords = content.ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2)
            .ToHashSet();

        MemoryModel? mostSimilar = null;
        decimal maxSimilarity = 0;

        foreach (var existing in existingMemories)
        {
            var existingWords = existing.Content.ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 2)
                .ToHashSet();

            if (existingWords.Count == 0 || contentWords.Count == 0)
            {
                continue;
            }

            var intersection = contentWords.Intersect(existingWords).Count();
            var union = contentWords.Union(existingWords).Count();
            var similarity = (decimal)intersection / union;

            if (similarity > maxSimilarity)
            {
                maxSimilarity = similarity;
                mostSimilar = existing;
            }
        }

        return (mostSimilar, maxSimilarity);
    }

    private async Task<RelationshipResult> CheckRelationshipAsync(
        string existing,
        string newMemory,
        CancellationToken ct)
    {
        try
        {
            var prompt = string.Format(ContradictionPrompt, existing, newMemory);

            var request = new LlmRequest
            {
                SystemPrompt = "You are a memory comparison system. Always respond with valid JSON.",
                Messages =
                [
                    new LlmMessage { Role = "user", Content = prompt }
                ],
                Parameters = new LlmParameters
                {
                    Temperature = 0.1,
                    MaxTokens = 256
                }
            };

            var response = await _llmProvider.CompleteAsync(request, ct);
            return ParseRelationshipResponse(response.Content);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check relationship between memories");
            return new RelationshipResult { Relationship = "DIFFERENT" };
        }
    }

    private RelationshipResult ParseRelationshipResponse(string response)
    {
        try
        {
            var startIndex = response.IndexOf('{');
            var endIndex = response.LastIndexOf('}');
            
            if (startIndex < 0 || endIndex < startIndex)
            {
                return new RelationshipResult { Relationship = "DIFFERENT" };
            }

            var json = response.Substring(startIndex, endIndex - startIndex + 1);
            var result = System.Text.Json.JsonSerializer.Deserialize<RelationshipResult>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return result ?? new RelationshipResult { Relationship = "DIFFERENT" };
        }
        catch
        {
            // Try to extract relationship from plain text
            var upperResponse = response.ToUpperInvariant();
            if (upperResponse.Contains("CONTRADICTION"))
                return new RelationshipResult { Relationship = "CONTRADICTION" };
            if (upperResponse.Contains("ADDITION"))
                return new RelationshipResult { Relationship = "ADDITION" };
            if (upperResponse.Contains("SAME"))
                return new RelationshipResult { Relationship = "SAME" };
            
            return new RelationshipResult { Relationship = "DIFFERENT" };
        }
    }

    private class RelationshipResult
    {
        public string Relationship { get; set; } = "DIFFERENT";
        public string? Merged { get; set; }
    }
}
