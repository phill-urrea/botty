using Botty.Core.Models;

namespace Botty.Core.Interfaces;

/// <summary>
/// Result from vector search including distance score.
/// </summary>
public record VectorSearchResult(Memory Memory, double CosineDistance);

/// <summary>
/// Repository interface for memory operations.
/// </summary>
public interface IMemoryRepository
{
    /// <summary>
    /// Gets a memory by ID.
    /// </summary>
    Task<Memory?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Gets all active memories for a user.
    /// </summary>
    Task<IEnumerable<Memory>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Searches memories by vector similarity.
    /// </summary>
    /// <param name="userId">User to search for.</param>
    /// <param name="embedding">Query embedding vector.</param>
    /// <param name="topK">Number of results to return.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IEnumerable<Memory>> SearchByEmbeddingAsync(
        Guid userId,
        float[] embedding,
        int topK = 10,
        CancellationToken ct = default);

    /// <summary>
    /// Creates a new memory.
    /// </summary>
    Task<Memory> CreateAsync(Memory memory, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing memory.
    /// </summary>
    Task<Memory> UpdateAsync(Memory memory, CancellationToken ct = default);

    /// <summary>
    /// Soft deletes a memory (sets IsActive to false).
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Gets memories that need to be expired.
    /// </summary>
    Task<IEnumerable<Memory>> GetExpiredMemoriesAsync(CancellationToken ct = default);

    /// <summary>
    /// Finds similar memories for deduplication.
    /// </summary>
    Task<IEnumerable<Memory>> FindSimilarAsync(
        Guid userId,
        float[] embedding,
        decimal similarityThreshold = 0.9m,
        CancellationToken ct = default);

    /// <summary>
    /// Searches memories using PostgreSQL full-text search with ts_rank_cd scoring.
    /// </summary>
    Task<IEnumerable<MemorySearchResult>> SearchByFullTextAsync(
        Guid userId,
        string query,
        int limit = 20,
        CancellationToken ct = default);

    /// <summary>
    /// Searches memories by vector similarity and returns results with cosine distance scores.
    /// </summary>
    Task<IEnumerable<VectorSearchResult>> SearchByEmbeddingWithScoresAsync(
        Guid userId,
        float[] embedding,
        int topK = 10,
        CancellationToken ct = default);
}
