using Botty.Core.Interfaces;
using Botty.Core.Models;
using Botty.Infrastructure.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Botty.Memory.Services;

/// <summary>
/// Configuration for hybrid search weighting.
/// </summary>
public class HybridSearchOptions
{
    /// <summary>Weight for vector similarity scores (0-1). Default: 0.7.</summary>
    public double VectorWeight { get; set; } = 0.7;

    /// <summary>Weight for full-text search scores (0-1). Default: 0.3.</summary>
    public double TextWeight { get; set; } = 0.3;

    /// <summary>Maximum results to return. Default: 20.</summary>
    public int MaxResults { get; set; } = 20;

    /// <summary>Minimum hybrid score threshold. Default: 0.0 (no filtering).</summary>
    public double MinScore { get; set; } = 0.0;
}

/// <summary>
/// Interface for hybrid search combining vector similarity and full-text search.
/// </summary>
public interface IHybridSearchService
{
    Task<IList<MemorySearchResult>> SearchAsync(
        Guid userId,
        string query,
        float[] queryEmbedding,
        int maxResults = 20,
        CancellationToken ct = default);
}

/// <summary>
/// Combines vector similarity search and PostgreSQL full-text search with weighted scoring.
/// Algorithm: hybridScore = vectorWeight * vectorScore + textWeight * textScore
/// </summary>
public class HybridSearchService : IHybridSearchService
{
    private readonly MemoryRepository _repository;
    private readonly HybridSearchOptions _options;
    private readonly ILogger<HybridSearchService> _logger;

    public HybridSearchService(
        MemoryRepository repository,
        IOptions<HybridSearchOptions> options,
        ILogger<HybridSearchService> logger)
    {
        _repository = repository;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IList<MemorySearchResult>> SearchAsync(
        Guid userId,
        string query,
        float[] queryEmbedding,
        int maxResults = 20,
        CancellationToken ct = default)
    {
        var limit = maxResults > 0 ? maxResults : _options.MaxResults;

        // IMPORTANT: MemoryRepository uses a scoped EF DbContext.
        // Running both queries in parallel on the same scope causes:
        // "A second operation was started on this context instance..."
        // Keep these sequential unless separate DbContext instances are introduced.
        var vectorResults = await _repository.SearchByEmbeddingWithScoresAsync(userId, queryEmbedding, limit, ct);
        var ftsResults = await _repository.SearchByFullTextAsync(userId, query, limit, ct);

        // Merge results by memory ID
        var merged = MergeHybridResults(vectorResults, ftsResults);

        // Filter and sort
        var filtered = merged
            .Where(r => r.HybridScore >= _options.MinScore)
            .OrderByDescending(r => r.HybridScore)
            .Take(limit)
            .ToList();

        _logger.LogDebug(
            "Hybrid search for user {UserId}: {VectorCount} vector + {FtsCount} FTS = {MergedCount} merged results",
            userId, vectorResults.Count(), ftsResults.Count(), filtered.Count);

        return filtered;
    }

    private IList<MemorySearchResult> MergeHybridResults(
        IEnumerable<VectorSearchResult> vectorResults,
        IEnumerable<MemorySearchResult> ftsResults)
    {
        var resultMap = new Dictionary<Guid, MemorySearchResult>();

        // Process vector results: convert cosine distance → similarity
        foreach (var vr in vectorResults)
        {
            var vectorScore = 1.0 - vr.CosineDistance;
            resultMap[vr.Memory.Id] = new MemorySearchResult
            {
                Memory = vr.Memory,
                VectorScore = Math.Max(0, vectorScore),
                TextScore = 0,
                HybridScore = 0
            };
        }

        // Process FTS results: normalize rank
        foreach (var fr in ftsResults)
        {
            var textScore = 1.0 / (1.0 + Math.Max(0, fr.TextScore));

            if (resultMap.TryGetValue(fr.Memory.Id, out var existing))
            {
                // Memory found in both searches — merge scores
                existing.TextScore = textScore;
            }
            else
            {
                resultMap[fr.Memory.Id] = new MemorySearchResult
                {
                    Memory = fr.Memory,
                    VectorScore = 0,
                    TextScore = textScore,
                    HybridScore = 0
                };
            }
        }

        // Calculate hybrid scores
        foreach (var result in resultMap.Values)
        {
            result.HybridScore = _options.VectorWeight * result.VectorScore
                               + _options.TextWeight * result.TextScore;
        }

        return resultMap.Values.ToList();
    }
}
