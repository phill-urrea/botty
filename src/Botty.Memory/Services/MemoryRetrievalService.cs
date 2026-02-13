using System.Text;
using Botty.Core.Enums;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Botty.Infrastructure.Repositories;
using Microsoft.Extensions.Logging;
using MemoryModel = Botty.Core.Models.Memory;

namespace Botty.Memory.Services;

/// <summary>
/// Retrieves and formats memories for LLM injection.
/// </summary>
public class MemoryRetrievalService : IMemoryRetrievalService
{
    private readonly MemoryRepository _repository;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IHybridSearchService _hybridSearch;
    private readonly ILogger<MemoryRetrievalService> _logger;

    public MemoryRetrievalService(
        MemoryRepository repository,
        IEmbeddingProvider embeddingProvider,
        IHybridSearchService hybridSearch,
        ILogger<MemoryRetrievalService> logger)
    {
        _repository = repository;
        _embeddingProvider = embeddingProvider;
        _hybridSearch = hybridSearch;
        _logger = logger;
    }

    public async Task<MemoryPack> RetrieveMemoryPackAsync(
        Guid userId,
        string query,
        MemoryRetrievalOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new MemoryRetrievalOptions();

        _logger.LogDebug("Retrieving memory pack for user {UserId} with query: {Query}",
            userId, query.Length > 50 ? query[..50] + "..." : query);

        // Stage 1: Deterministic fetch (always include)
        var deterministicMemories = await GetDeterministicMemoriesAsync(userId, options, ct);

        // Stage 2: Semantic search based on query
        var semanticMemories = await GetSemanticMemoriesAsync(userId, query, options, ct);

        // Merge and dedupe
        var allMemories = MergeAndRank(deterministicMemories, semanticMemories, options);

        // Filter by sensitivity
        var filteredMemories = allMemories
            .Where(m => options.AllowedSensitivities.Contains(m.Sensitivity))
            .Take(options.MaxMemories)
            .ToList();

        // Format for LLM injection
        var formattedText = FormatMemoryPack(filteredMemories);

        _logger.LogInformation("Retrieved {Count} memories for user {UserId}",
            filteredMemories.Count, userId);

        return new MemoryPack
        {
            Memories = filteredMemories,
            FormattedText = formattedText
        };
    }

    private async Task<IList<MemoryModel>> GetDeterministicMemoriesAsync(
        Guid userId,
        MemoryRetrievalOptions options,
        CancellationToken ct)
    {
        var memories = new List<MemoryModel>();

        // Top preferences (always include)
        var preferences = await _repository.GetTopByTypeAsync(
            userId, MemoryType.Preference, options.TopPreferences, ct);
        memories.AddRange(preferences);

        // Active projects
        var projects = await _repository.GetActiveProjectsAsync(
            userId, options.ActiveProjects, ct);
        memories.AddRange(projects);

        return memories;
    }

    private async Task<IList<MemoryModel>> GetSemanticMemoriesAsync(
        Guid userId,
        string query,
        MemoryRetrievalOptions options,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        // Generate embedding for the query
        var queryEmbedding = await _embeddingProvider.GetEmbeddingAsync(query, ct);

        // Use hybrid search (vector + full-text) instead of vector-only
        var hybridResults = await _hybridSearch.SearchAsync(
            userId, query, queryEmbedding, options.SemanticSearchResults, ct);

        return hybridResults.Select(r => r.Memory).ToList();
    }

    private IList<MemoryModel> MergeAndRank(
        IList<MemoryModel> deterministic,
        IList<MemoryModel> semantic,
        MemoryRetrievalOptions options)
    {
        // Combine and remove duplicates
        var combined = deterministic
            .Concat(semantic)
            .DistinctBy(m => m.Id)
            .ToList();

        // Calculate scores for ranking
        var now = DateTime.UtcNow;
        var scored = combined.Select(m => new
        {
            Memory = m,
            Score = CalculateRetrievalScore(m, deterministic.Contains(m), now, options)
        });

        // Sort by score descending
        return scored
            .OrderByDescending(x => x.Score)
            .Select(x => x.Memory)
            .ToList();
    }

    private static decimal CalculateRetrievalScore(
        MemoryModel memory,
        bool isDeterministic,
        DateTime now,
        MemoryRetrievalOptions options)
    {
        var score = memory.Confidence;

        // Boost deterministic memories
        if (isDeterministic)
        {
            score += 0.3m;
        }

        // Type weight
        score *= GetTypeWeight(memory.Type);

        // Recency boost
        var ageInDays = (now - memory.UpdatedAt).TotalDays;
        var recencyMultiplier = 1.0m - (decimal)Math.Min(ageInDays / 365.0, 1.0) * options.RecencyBoost;
        score *= recencyMultiplier;

        return score;
    }

    private static decimal GetTypeWeight(MemoryType type) => type switch
    {
        MemoryType.Preference => 1.0m,
        MemoryType.Fact => 0.95m,
        MemoryType.Project => 0.9m,
        MemoryType.Relationship => 0.85m,
        MemoryType.Episode => 0.7m,
        MemoryType.Artifact => 0.75m,
        _ => 0.5m
    };

    private static string FormatMemoryPack(IList<MemoryModel> memories)
    {
        if (memories.Count == 0)
        {
            return "No relevant memories.";
        }

        var sb = new StringBuilder();
        sb.AppendLine("## What I Remember About You");
        sb.AppendLine();

        // Group by type for better organization
        var grouped = memories.GroupBy(m => m.Type);

        foreach (var group in grouped)
        {
            var typeName = group.Key switch
            {
                MemoryType.Preference => "Preferences",
                MemoryType.Fact => "Facts",
                MemoryType.Project => "Current Projects",
                MemoryType.Relationship => "People You Know",
                MemoryType.Episode => "Recent Events",
                MemoryType.Artifact => "Things You've Created",
                _ => "Other"
            };

            sb.AppendLine($"### {typeName}");
            foreach (var memory in group)
            {
                sb.AppendLine($"- {memory.Content}");
            }
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }
}
