using Botty.Core.Interfaces;
using Botty.Core.Models;
using Botty.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Botty.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for memory operations using Entity Framework Core.
/// </summary>
public class MemoryRepository : IMemoryRepository
{
    private readonly BottyDbContext _context;

    public MemoryRepository(BottyDbContext context)
    {
        _context = context;
    }

    public async Task<Memory?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Memories
            .FirstOrDefaultAsync(m => m.Id == id, ct);
    }

    public async Task<IEnumerable<Memory>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        return await _context.Memories
            .Where(m => m.UserId == userId && m.IsActive)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<Memory>> SearchByEmbeddingAsync(
        Guid userId,
        float[] embedding,
        int topK = 10,
        CancellationToken ct = default)
    {
        var vector = new Vector(embedding);
        
        return await _context.Memories
            .Where(m => m.UserId == userId && m.IsActive && m.Embedding != null)
            .OrderBy(m => m.Embedding!.CosineDistance(vector))
            .Take(topK)
            .ToListAsync(ct);
    }

    public async Task<Memory> CreateAsync(Memory memory, CancellationToken ct = default)
    {
        memory.Id = Guid.NewGuid();
        memory.CreatedAt = DateTime.UtcNow;
        memory.UpdatedAt = DateTime.UtcNow;

        _context.Memories.Add(memory);
        await _context.SaveChangesAsync(ct);

        return memory;
    }

    public async Task<Memory> UpdateAsync(Memory memory, CancellationToken ct = default)
    {
        memory.UpdatedAt = DateTime.UtcNow;
        
        _context.Memories.Update(memory);
        await _context.SaveChangesAsync(ct);

        return memory;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var memory = await GetByIdAsync(id, ct);
        if (memory != null)
        {
            memory.IsActive = false;
            memory.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
        }
    }

    public async Task<IEnumerable<Memory>> GetExpiredMemoriesAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _context.Memories
            .Where(m => m.IsActive && m.ExpiresAt != null && m.ExpiresAt <= now)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<Memory>> FindSimilarAsync(
        Guid userId,
        float[] embedding,
        decimal similarityThreshold = 0.9m,
        CancellationToken ct = default)
    {
        var vector = new Vector(embedding);
        
        // Cosine distance threshold: 1 - similarity
        // For 0.9 similarity, distance should be <= 0.1
        var distanceThreshold = 1.0 - (double)similarityThreshold;

        return await _context.Memories
            .Where(m => m.UserId == userId && m.IsActive && m.Embedding != null)
            .Where(m => m.Embedding!.CosineDistance(vector) <= distanceThreshold)
            .OrderBy(m => m.Embedding!.CosineDistance(vector))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Gets top memories by type for deterministic fetch.
    /// </summary>
    public async Task<IEnumerable<Memory>> GetTopByTypeAsync(
        Guid userId,
        Core.Enums.MemoryType type,
        int limit = 5,
        CancellationToken ct = default)
    {
        return await _context.Memories
            .Where(m => m.UserId == userId && m.IsActive && m.Type == type)
            .OrderByDescending(m => m.Confidence)
            .ThenByDescending(m => m.UpdatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Gets active project memories.
    /// </summary>
    public async Task<IEnumerable<Memory>> GetActiveProjectsAsync(
        Guid userId,
        int limit = 5,
        CancellationToken ct = default)
    {
        return await _context.Memories
            .Where(m => m.UserId == userId 
                && m.IsActive 
                && m.Type == Core.Enums.MemoryType.Project)
            .OrderByDescending(m => m.UpdatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Gets memories matching a text search (for "what do you remember about X").
    /// </summary>
    public async Task<IEnumerable<Memory>> SearchByTextAsync(
        Guid userId,
        string searchTerm,
        int limit = 20,
        CancellationToken ct = default)
    {
        var lowerSearch = searchTerm.ToLowerInvariant();

        return await _context.Memories
            .Where(m => m.UserId == userId
                && m.IsActive
                && m.Content.ToLower().Contains(lowerSearch))
            .OrderByDescending(m => m.UpdatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Searches memories using PostgreSQL full-text search with ts_rank_cd scoring.
    /// </summary>
    public async Task<IEnumerable<Core.Models.MemorySearchResult>> SearchByFullTextAsync(
        Guid userId,
        string query,
        int limit = 20,
        CancellationToken ct = default)
    {
        var tsQuery = BuildTsQuery(query);
        if (string.IsNullOrEmpty(tsQuery))
            return [];

        // Use raw SQL for ts_rank_cd scoring
        var sql = @"
            SELECT m.*, ts_rank_cd(m.content_tsv, to_tsquery('english', {1})) AS rank
            FROM memories m
            WHERE m.user_id = {0}
              AND m.is_active = true
              AND m.content_tsv @@ to_tsquery('english', {1})
            ORDER BY rank DESC
            LIMIT {2}";

        var results = await _context.Memories
            .FromSqlRaw(sql, userId, tsQuery, limit)
            .ToListAsync(ct);

        // We can't get the rank directly from EF, so re-query with rank
        // Use a simpler approach: return results with placeholder scores,
        // then the hybrid service will normalize
        return results.Select((m, i) => new Core.Models.MemorySearchResult
        {
            Memory = m,
            TextScore = 1.0 / (1.0 + i), // Approximation based on rank ordering
            VectorScore = 0,
            HybridScore = 0
        }).ToList();
    }

    /// <summary>
    /// Searches memories by vector similarity and returns results with cosine distance scores.
    /// </summary>
    public async Task<IEnumerable<Core.Interfaces.VectorSearchResult>> SearchByEmbeddingWithScoresAsync(
        Guid userId,
        float[] embedding,
        int topK = 10,
        CancellationToken ct = default)
    {
        var vector = new Vector(embedding);

        // Use raw SQL to get the cosine distance score
        var sql = @"
            SELECT m.*, m.embedding <=> {1}::vector AS distance
            FROM memories m
            WHERE m.user_id = {0}
              AND m.is_active = true
              AND m.embedding IS NOT NULL
            ORDER BY m.embedding <=> {1}::vector
            LIMIT {2}";

        var memories = await _context.Memories
            .FromSqlRaw(sql, userId, vector.ToString(), topK)
            .ToListAsync(ct);

        // Calculate cosine distance for each result
        return memories.Select(m =>
        {
            double distance = 0;
            if (m.Embedding != null)
            {
                distance = CosineDistance(embedding, m.Embedding.ToArray());
            }
            return new Core.Interfaces.VectorSearchResult(m, distance);
        }).ToList();
    }

    /// <summary>
    /// Builds a PostgreSQL tsquery string from a natural language query.
    /// Tokenizes, quotes, and joins with &amp; operator.
    /// </summary>
    private static string BuildTsQuery(string query)
    {
        var tokens = query
            .Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 1)
            .Select(t => t.Replace("'", "''"))
            .Select(t => $"'{t}'")
            .ToList();

        return tokens.Count > 0 ? string.Join(" & ", tokens) : string.Empty;
    }

    private static double CosineDistance(float[] a, float[] b)
    {
        var len = Math.Min(a.Length, b.Length);
        double dotProduct = 0, normA = 0, normB = 0;
        for (var i = 0; i < len; i++)
        {
            dotProduct += a[i] * (double)b[i];
            normA += a[i] * (double)a[i];
            normB += b[i] * (double)b[i];
        }
        var denominator = Math.Sqrt(normA) * Math.Sqrt(normB);
        if (denominator == 0) return 1.0;
        return 1.0 - dotProduct / denominator;
    }
}
