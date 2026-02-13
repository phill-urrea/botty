using Botty.Core.Enums;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MemoryModel = Botty.Core.Models.Memory;

namespace Botty.Memory.Services;

/// <summary>
/// Orchestrates the memory ingestion pipeline.
/// </summary>
public class MemoryIngestionService : IMemoryIngestionService
{
    private readonly IMemoryRepository _repository;
    private readonly IMemoryExtractor _extractor;
    private readonly IMemoryScorer _scorer;
    private readonly IMemoryDeduplicator _deduplicator;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly ILogger<MemoryIngestionService> _logger;
    private readonly MemoryIngestionOptions _options;

    public MemoryIngestionService(
        IMemoryRepository repository,
        IMemoryExtractor extractor,
        IMemoryScorer scorer,
        IMemoryDeduplicator deduplicator,
        IEmbeddingProvider embeddingProvider,
        ILogger<MemoryIngestionService> logger,
        IOptions<MemoryIngestionOptions> options)
    {
        _repository = repository;
        _extractor = extractor;
        _scorer = scorer;
        _deduplicator = deduplicator;
        _embeddingProvider = embeddingProvider;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<IList<MemoryModel>> IngestFromConversationAsync(
        Conversation conversation,
        CancellationToken ct = default)
    {
        if (!conversation.StoreMemories)
        {
            _logger.LogDebug("Skipping ingestion for conversation {Id} - StoreMemories is false",
                conversation.Id);
            return [];
        }

        _logger.LogInformation("Starting memory ingestion for conversation {Id}", conversation.Id);

        // Step 1: Extract potential memories
        var extractedMemories = await _extractor.ExtractMemoriesAsync(conversation, ct);
        
        if (extractedMemories.Count == 0)
        {
            _logger.LogDebug("No memories extracted from conversation {Id}", conversation.Id);
            return [];
        }

        // Get existing memories for scoring and deduplication
        var existingMemories = (await _repository.GetByUserIdAsync(conversation.UserId, ct)).ToList();

        var ingestedMemories = new List<MemoryModel>();

        foreach (var extracted in extractedMemories)
        {
            try
            {
                var memory = await ProcessExtractedMemoryAsync(
                    extracted,
                    conversation.UserId,
                    conversation.Id.ToString(),
                    existingMemories,
                    ct);

                if (memory != null)
                {
                    ingestedMemories.Add(memory);
                    existingMemories.Add(memory); // Add to list for subsequent dedup checks
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process extracted memory: {Content}",
                    extracted.Content.Length > 50 ? extracted.Content[..50] + "..." : extracted.Content);
            }
        }

        _logger.LogInformation("Ingested {Count} memories from conversation {Id}",
            ingestedMemories.Count, conversation.Id);

        return ingestedMemories;
    }

    public async Task<MemoryModel> IngestManualMemoryAsync(
        Guid userId,
        string content,
        MemoryType type,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Ingesting manual memory for user {UserId}: {Content}",
            userId, content.Length > 50 ? content[..50] + "..." : content);

        // Generate embedding
        var embedding = await _embeddingProvider.GetEmbeddingAsync(content, ct);

        var memory = new MemoryModel
        {
            UserId = userId,
            Content = content,
            Type = type,
            Embedding = new Pgvector.Vector(embedding),
            Confidence = 1.0m, // Manual entries have full confidence
            Sensitivity = MemorySensitivity.Private,
            Source = "manual"
        };

        return await _repository.CreateAsync(memory, ct);
    }

    private async Task<MemoryModel?> ProcessExtractedMemoryAsync(
        ExtractedMemory extracted,
        Guid userId,
        string source,
        IList<MemoryModel> existingMemories,
        CancellationToken ct)
    {
        // Step 2: Score the memory
        var score = await _scorer.ScoreMemoryAsync(extracted, existingMemories, ct);
        
        if (score < _options.MinimumScoreThreshold)
        {
            _logger.LogDebug("Memory scored below threshold ({Score:F2} < {Threshold}): {Content}",
                score, _options.MinimumScoreThreshold,
                extracted.Content.Length > 50 ? extracted.Content[..50] + "..." : extracted.Content);
            return null;
        }

        // Step 3: Generate embedding
        var embedding = await _embeddingProvider.GetEmbeddingAsync(extracted.Content, ct);

        // Step 4: Check for duplicates
        var dedupResult = await _deduplicator.CheckDuplicateAsync(
            extracted, embedding, existingMemories, ct);

        switch (dedupResult.Action)
        {
            case DeduplicationAction.Skip:
                _logger.LogDebug("Skipping duplicate memory: {Content}",
                    extracted.Content.Length > 50 ? extracted.Content[..50] + "..." : extracted.Content);
                return null;

            case DeduplicationAction.Merge:
                return await MergeMemoryAsync(dedupResult, embedding, score, source, ct);

            case DeduplicationAction.Supersede:
                return await SupersedeMemoryAsync(dedupResult, extracted, userId, embedding, score, source, ct);

            case DeduplicationAction.Insert:
            default:
                return await InsertMemoryAsync(extracted, userId, embedding, score, source, ct);
        }
    }

    private async Task<MemoryModel> MergeMemoryAsync(
        DeduplicationResult dedupResult,
        float[] embedding,
        decimal score,
        string source,
        CancellationToken ct)
    {
        var existing = dedupResult.ExistingMemory!;
        
        _logger.LogInformation("Merging memory with existing {Id}: {Content}",
            existing.Id,
            dedupResult.MergedContent?.Length > 50 
                ? dedupResult.MergedContent[..50] + "..." 
                : dedupResult.MergedContent);

        existing.Content = dedupResult.MergedContent ?? existing.Content;
        existing.Embedding = new Pgvector.Vector(embedding); // Update embedding for merged content
        existing.Confidence = Math.Max(existing.Confidence, score);
        existing.Source = source;

        return await _repository.UpdateAsync(existing, ct);
    }

    private async Task<MemoryModel> SupersedeMemoryAsync(
        DeduplicationResult dedupResult,
        ExtractedMemory extracted,
        Guid userId,
        float[] embedding,
        decimal score,
        string source,
        CancellationToken ct)
    {
        var superseded = dedupResult.ExistingMemory!;
        
        _logger.LogInformation("Superseding memory {Id} with new information: {Content}",
            superseded.Id,
            extracted.Content.Length > 50 ? extracted.Content[..50] + "..." : extracted.Content);

        // Soft delete the old memory
        superseded.IsActive = false;
        await _repository.UpdateAsync(superseded, ct);

        // Create new memory that supersedes the old one
        var memory = new MemoryModel
        {
            UserId = userId,
            Content = extracted.Content,
            Type = extracted.Type,
            Embedding = new Pgvector.Vector(embedding),
            Confidence = score,
            Sensitivity = extracted.Sensitivity,
            Source = source,
            SupersedesId = superseded.Id,
            ExpiresAt = extracted.TtlDays.HasValue 
                ? DateTime.UtcNow.AddDays(extracted.TtlDays.Value) 
                : null
        };

        return await _repository.CreateAsync(memory, ct);
    }

    private async Task<MemoryModel> InsertMemoryAsync(
        ExtractedMemory extracted,
        Guid userId,
        float[] embedding,
        decimal score,
        string source,
        CancellationToken ct)
    {
        _logger.LogInformation("Inserting new memory: {Content}",
            extracted.Content.Length > 50 ? extracted.Content[..50] + "..." : extracted.Content);

        var memory = new MemoryModel
        {
            UserId = userId,
            Content = extracted.Content,
            Type = extracted.Type,
            Embedding = new Pgvector.Vector(embedding),
            Confidence = score,
            Sensitivity = extracted.Sensitivity,
            Source = source,
            ExpiresAt = extracted.TtlDays.HasValue 
                ? DateTime.UtcNow.AddDays(extracted.TtlDays.Value) 
                : null
        };

        return await _repository.CreateAsync(memory, ct);
    }
}

/// <summary>
/// Options for memory ingestion.
/// </summary>
public class MemoryIngestionOptions
{
    /// <summary>
    /// Minimum score threshold for a memory to be ingested.
    /// </summary>
    public decimal MinimumScoreThreshold { get; set; } = 0.4m;

    /// <summary>
    /// Maximum memories to ingest from a single conversation.
    /// </summary>
    public int MaxMemoriesPerConversation { get; set; } = 5;
}
