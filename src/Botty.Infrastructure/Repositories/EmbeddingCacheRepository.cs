using System.Security.Cryptography;
using System.Text;
using Botty.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector;

namespace Botty.Infrastructure.Repositories;

/// <summary>
/// Repository for caching embedding vectors to avoid redundant API calls.
/// </summary>
public class EmbeddingCacheRepository
{
    private readonly BottyDbContext _context;
    private readonly ILogger<EmbeddingCacheRepository> _logger;

    public EmbeddingCacheRepository(
        BottyDbContext context,
        ILogger<EmbeddingCacheRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Gets a cached embedding if it exists.
    /// </summary>
    public async Task<float[]?> GetAsync(
        string text,
        string provider,
        string model,
        CancellationToken ct = default)
    {
        var textHash = ComputeHash(text);

        var cached = await _context.EmbeddingCache
            .FirstOrDefaultAsync(e =>
                e.TextHash == textHash &&
                e.Provider == provider &&
                e.Model == model, ct);

        if (cached?.Embedding != null)
        {
            _logger.LogDebug("Embedding cache hit for provider={Provider} model={Model}", provider, model);
            return cached.Embedding.ToArray();
        }

        return null;
    }

    /// <summary>
    /// Stores an embedding in the cache.
    /// </summary>
    public async Task SetAsync(
        string text,
        string provider,
        string model,
        float[] embedding,
        CancellationToken ct = default)
    {
        var textHash = ComputeHash(text);

        var existing = await _context.EmbeddingCache
            .FirstOrDefaultAsync(e =>
                e.TextHash == textHash &&
                e.Provider == provider &&
                e.Model == model, ct);

        if (existing != null)
        {
            existing.Embedding = new Vector(embedding);
            existing.Dimensions = embedding.Length;
        }
        else
        {
            _context.EmbeddingCache.Add(new EmbeddingCacheEntry
            {
                TextHash = textHash,
                Provider = provider,
                Model = model,
                Embedding = new Vector(embedding),
                Dimensions = embedding.Length,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync(ct);
    }

    private static string ComputeHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexStringLower(bytes);
    }
}

/// <summary>
/// Entity for the embedding_cache table.
/// </summary>
public class EmbeddingCacheEntry
{
    public Guid Id { get; set; }
    public required string TextHash { get; set; }
    public required string Provider { get; set; }
    public required string Model { get; set; }
    public Vector? Embedding { get; set; }
    public int Dimensions { get; set; }
    public DateTime CreatedAt { get; set; }
}
