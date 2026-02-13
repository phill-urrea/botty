using Botty.Core.Enums;
using Pgvector;

namespace Botty.Core.Models;

/// <summary>
/// Represents a memory stored in the memory system.
/// </summary>
public class Memory
{
    /// <summary>
    /// Unique identifier for the memory.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The user this memory belongs to.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Type of memory (preference, project, artifact, episode, etc.).
    /// </summary>
    public MemoryType Type { get; set; }

    /// <summary>
    /// The content/text of the memory.
    /// </summary>
    public required string Content { get; set; }

    /// <summary>
    /// Vector embedding for semantic search (stored separately in pgvector).
    /// </summary>
    public Vector? Embedding { get; set; }

    /// <summary>
    /// Confidence score for this memory (0.0 to 1.0).
    /// </summary>
    public decimal Confidence { get; set; } = 1.0m;

    /// <summary>
    /// Sensitivity level of this memory.
    /// </summary>
    public MemorySensitivity Sensitivity { get; set; } = MemorySensitivity.Private;

    /// <summary>
    /// Source of the memory (conversation ID, manual entry, etc.).
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// If this memory supersedes another, the ID of the superseded memory.
    /// </summary>
    public Guid? SupersedesId { get; set; }

    /// <summary>
    /// When the memory was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the memory was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// When the memory expires (null for no expiration).
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Whether this memory is active (soft delete support).
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Full-text search vector (auto-populated by database trigger).
    /// </summary>
    public NpgsqlTypes.NpgsqlTsVector? ContentTsv { get; set; }

    /// <summary>
    /// The embedding provider that generated this memory's embedding (e.g. "openai", "gemini").
    /// </summary>
    public string? EmbeddingProvider { get; set; }

    /// <summary>
    /// The model used to generate the embedding (e.g. "text-embedding-3-small").
    /// </summary>
    public string? EmbeddingModel { get; set; }

    /// <summary>
    /// The dimensionality of the embedding vector.
    /// </summary>
    public int? EmbeddingDimensions { get; set; }
}
