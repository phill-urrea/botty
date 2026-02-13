using Botty.Core.Enums;
using Botty.Core.Interfaces;
using Microsoft.Extensions.Logging;
using MemoryModel = Botty.Core.Models.Memory;

namespace Botty.Memory.Services;

/// <summary>
/// Scores memories for durability and importance.
/// </summary>
public class MemoryScorer : IMemoryScorer
{
    private readonly ILogger<MemoryScorer> _logger;

    // Weight factors for scoring
    private static readonly Dictionary<MemoryType, decimal> TypeWeights = new()
    {
        { MemoryType.Preference, 0.9m },    // Preferences are highly durable
        { MemoryType.Fact, 0.85m },         // Facts about the user are important
        { MemoryType.Relationship, 0.8m },  // Relationships are valuable
        { MemoryType.Project, 0.7m },       // Projects may be temporary
        { MemoryType.Episode, 0.5m },       // Episodes are less durable
        { MemoryType.Artifact, 0.6m }       // Artifacts have medium durability
    };

    public MemoryScorer(ILogger<MemoryScorer> logger)
    {
        _logger = logger;
    }

    public Task<decimal> ScoreMemoryAsync(
        ExtractedMemory memory,
        IEnumerable<MemoryModel> existingMemories,
        CancellationToken ct = default)
    {
        // Start with the raw confidence from extraction
        var score = memory.RawConfidence;

        // Apply type weight
        var typeWeight = TypeWeights.GetValueOrDefault(memory.Type, 0.5m);
        score *= typeWeight;

        // Boost for specificity (longer content tends to be more specific)
        var specificityBoost = CalculateSpecificityBoost(memory.Content);
        score += specificityBoost * 0.1m;

        // Boost for identity/fact statements (name, pronouns) so they are not outcompeted by longer preferences
        if (memory.Type == MemoryType.Fact && IsIdentityFact(memory.Content))
            score += 0.15m;

        // Penalize if similar memories already exist (diminishing returns)
        var noveltyPenalty = CalculateNoveltyPenalty(memory.Content, existingMemories);
        score *= (1m - noveltyPenalty);

        // Clamp final score
        score = Math.Clamp(score, 0m, 1m);

        _logger.LogDebug("Scored memory '{Content}' at {Score:F2} (type={Type}, typeWeight={TypeWeight}, novelty={Novelty})",
            memory.Content.Length > 50 ? memory.Content[..50] + "..." : memory.Content,
            score, memory.Type, typeWeight, noveltyPenalty);

        return Task.FromResult(score);
    }

    /// <summary>
    /// Detects identity-related facts (name, what to call user, pronouns) that should be prioritized.
    /// </summary>
    private static bool IsIdentityFact(string content)
    {
        var lower = content.ToLowerInvariant();
        return lower.Contains("name is") || lower.Contains("call me") || lower.Contains("i'm ") ||
               lower.Contains("i am ") || lower.Contains("pronouns") || lower.Contains("prefer to be called");
    }

    private static decimal CalculateSpecificityBoost(string content)
    {
        // Boost for content that is specific (not too short, not too long)
        var wordCount = content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        
        return wordCount switch
        {
            < 3 => 0m,          // Too short - probably not specific
            < 8 => 0.5m,        // Good length
            < 15 => 1m,         // Ideal specificity
            < 25 => 0.8m,       // Getting verbose
            _ => 0.5m           // Too long
        };
    }

    private static decimal CalculateNoveltyPenalty(string content, IEnumerable<MemoryModel> existingMemories)
    {
        if (!existingMemories.Any())
        {
            return 0m; // No existing memories, no penalty
        }

        // Simple text similarity check (more sophisticated would use embeddings)
        var contentWords = content.ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3) // Skip small words
            .ToHashSet();

        if (contentWords.Count == 0)
        {
            return 0m;
        }

        var maxOverlap = 0m;

        foreach (var existing in existingMemories)
        {
            var existingWords = existing.Content.ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 3)
                .ToHashSet();

            if (existingWords.Count == 0) continue;

            var intersection = contentWords.Intersect(existingWords).Count();
            var union = contentWords.Union(existingWords).Count();
            
            var jaccard = (decimal)intersection / union;
            maxOverlap = Math.Max(maxOverlap, jaccard);
        }

        // Apply penalty based on overlap (high overlap = high penalty)
        return maxOverlap * 0.5m; // Max 50% penalty
    }
}
