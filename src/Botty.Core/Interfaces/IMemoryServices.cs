using Botty.Core.Models;

namespace Botty.Core.Interfaces;

/// <summary>
/// Extracts durable memories from conversations.
/// </summary>
public interface IMemoryExtractor
{
    /// <summary>
    /// Extracts 0-5 potential memories from a conversation.
    /// </summary>
    /// <param name="conversation">The conversation to extract from.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of extracted memories (not yet persisted).</returns>
    Task<IList<ExtractedMemory>> ExtractMemoriesAsync(Conversation conversation, CancellationToken ct = default);
}

/// <summary>
/// A memory extracted from a conversation (before scoring/dedup).
/// </summary>
public class ExtractedMemory
{
    /// <summary>
    /// The content of the memory.
    /// </summary>
    public required string Content { get; set; }

    /// <summary>
    /// Suggested type for this memory.
    /// </summary>
    public required Enums.MemoryType Type { get; set; }

    /// <summary>
    /// Suggested sensitivity level.
    /// </summary>
    public Enums.MemorySensitivity Sensitivity { get; set; } = Enums.MemorySensitivity.Private;

    /// <summary>
    /// Suggested TTL in days (null for no expiration).
    /// </summary>
    public int? TtlDays { get; set; }

    /// <summary>
    /// Raw confidence from extraction (0-1).
    /// </summary>
    public decimal RawConfidence { get; set; } = 1.0m;
}

/// <summary>
/// Scores memories for relevance and durability.
/// </summary>
public interface IMemoryScorer
{
    /// <summary>
    /// Scores a memory for how durable/important it is (0.0 to 1.0).
    /// </summary>
    /// <param name="memory">The extracted memory to score.</param>
    /// <param name="existingMemories">Existing memories for context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Score between 0 and 1.</returns>
    Task<decimal> ScoreMemoryAsync(
        ExtractedMemory memory,
        IEnumerable<Memory> existingMemories,
        CancellationToken ct = default);
}

/// <summary>
/// Finds duplicate or contradictory memories.
/// </summary>
public interface IMemoryDeduplicator
{
    /// <summary>
    /// Checks if a memory is a duplicate or contradicts existing memories.
    /// </summary>
    /// <param name="memory">The new memory to check.</param>
    /// <param name="embedding">The embedding for the new memory.</param>
    /// <param name="existingMemories">Similar existing memories to compare against.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Deduplication result with recommended action.</returns>
    Task<DeduplicationResult> CheckDuplicateAsync(
        ExtractedMemory memory,
        float[] embedding,
        IEnumerable<Memory> existingMemories,
        CancellationToken ct = default);
}

/// <summary>
/// Result of deduplication check.
/// </summary>
public class DeduplicationResult
{
    /// <summary>
    /// Recommended action for this memory.
    /// </summary>
    public DeduplicationAction Action { get; set; }

    /// <summary>
    /// If merging or superseding, the existing memory to act on.
    /// </summary>
    public Memory? ExistingMemory { get; set; }

    /// <summary>
    /// If merging, the merged content to use.
    /// </summary>
    public string? MergedContent { get; set; }

    /// <summary>
    /// Similarity score with the most similar existing memory.
    /// </summary>
    public decimal SimilarityScore { get; set; }
}

/// <summary>
/// Actions for handling duplicate/similar memories.
/// </summary>
public enum DeduplicationAction
{
    /// <summary>
    /// Insert as a new memory.
    /// </summary>
    Insert,

    /// <summary>
    /// Merge with an existing memory (same fact, additional details).
    /// </summary>
    Merge,

    /// <summary>
    /// Supersede an existing memory (contradictory information).
    /// </summary>
    Supersede,

    /// <summary>
    /// Skip - memory is an exact duplicate.
    /// </summary>
    Skip
}

/// <summary>
/// Orchestrates the memory ingestion pipeline.
/// </summary>
public interface IMemoryIngestionService
{
    /// <summary>
    /// Processes a conversation and ingests relevant memories.
    /// </summary>
    /// <param name="conversation">The conversation to process.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of memories that were created or updated.</returns>
    Task<IList<Memory>> IngestFromConversationAsync(Conversation conversation, CancellationToken ct = default);

    /// <summary>
    /// Ingests a single memory directly (for manual entry).
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="content">Memory content.</param>
    /// <param name="type">Memory type.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created memory.</returns>
    Task<Memory> IngestManualMemoryAsync(
        Guid userId,
        string content,
        Enums.MemoryType type,
        CancellationToken ct = default);
}

/// <summary>
/// Retrieves memories for injection into LLM context.
/// </summary>
public interface IMemoryRetrievalService
{
    /// <summary>
    /// Retrieves a memory pack for injection into an LLM prompt.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="query">Current query/context for semantic search.</param>
    /// <param name="options">Retrieval options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Formatted memory pack (8-20 bullet points).</returns>
    Task<MemoryPack> RetrieveMemoryPackAsync(
        Guid userId,
        string query,
        MemoryRetrievalOptions? options = null,
        CancellationToken ct = default);
}

/// <summary>
/// Options for memory retrieval.
/// </summary>
public class MemoryRetrievalOptions
{
    /// <summary>
    /// Maximum number of memories to include.
    /// </summary>
    public int MaxMemories { get; set; } = 20;

    /// <summary>
    /// Number of top preferences to always include.
    /// </summary>
    public int TopPreferences { get; set; } = 5;

    /// <summary>
    /// Number of active projects to always include.
    /// </summary>
    public int ActiveProjects { get; set; } = 3;

    /// <summary>
    /// Number of semantic search results.
    /// </summary>
    public int SemanticSearchResults { get; set; } = 10;

    /// <summary>
    /// Sensitivity levels to include.
    /// </summary>
    public IList<Enums.MemorySensitivity> AllowedSensitivities { get; set; } = 
        [Enums.MemorySensitivity.Public, Enums.MemorySensitivity.Private];

    /// <summary>
    /// Boost factor for recent memories (0-1).
    /// </summary>
    public decimal RecencyBoost { get; set; } = 0.2m;
}

/// <summary>
/// A retrieved memory pack ready for LLM injection.
/// </summary>
public class MemoryPack
{
    /// <summary>
    /// The memories included in this pack.
    /// </summary>
    public IList<Memory> Memories { get; set; } = [];

    /// <summary>
    /// Formatted text for LLM injection.
    /// </summary>
    public required string FormattedText { get; set; }

    /// <summary>
    /// Number of memories included.
    /// </summary>
    public int Count => Memories.Count;
}

/// <summary>
/// Trust layer commands for user control over memories.
/// </summary>
public interface IMemoryTrustService
{
    /// <summary>
    /// Gets all memories the assistant remembers about the user.
    /// "What do you remember about me?"
    /// </summary>
    Task<IEnumerable<Memory>> GetRememberedMemoriesAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Searches memories for a specific topic.
    /// "What do you remember about X?"
    /// </summary>
    Task<IEnumerable<Memory>> SearchMemoriesAsync(
        Guid userId,
        string query,
        CancellationToken ct = default);

    /// <summary>
    /// Forgets a specific memory.
    /// "Forget that I like coffee"
    /// </summary>
    Task<bool> ForgetMemoryAsync(Guid userId, string query, CancellationToken ct = default);

    /// <summary>
    /// Forgets a memory by ID.
    /// </summary>
    Task<bool> ForgetMemoryByIdAsync(Guid memoryId, CancellationToken ct = default);

    /// <summary>
    /// Corrects a memory.
    /// "Actually, I prefer tea not coffee"
    /// </summary>
    Task<Memory?> CorrectMemoryAsync(
        Guid userId,
        string originalQuery,
        string correction,
        CancellationToken ct = default);

    /// <summary>
    /// Sets session privacy (don't store memories from this conversation).
    /// </summary>
    Task SetSessionPrivacyAsync(Guid conversationId, bool storeMemories, CancellationToken ct = default);
}
