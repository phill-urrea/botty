using Botty.Core.Interfaces;
using Botty.Infrastructure.Data;
using Botty.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MemoryModel = Botty.Core.Models.Memory;

namespace Botty.Memory.Services;

/// <summary>
/// Trust layer commands for user control over memories.
/// </summary>
public class MemoryTrustService : IMemoryTrustService
{
    private readonly MemoryRepository _repository;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IHybridSearchService _hybridSearch;
    private readonly BottyDbContext _context;
    private readonly ILogger<MemoryTrustService> _logger;

    public MemoryTrustService(
        MemoryRepository repository,
        IEmbeddingProvider embeddingProvider,
        IHybridSearchService hybridSearch,
        BottyDbContext context,
        ILogger<MemoryTrustService> logger)
    {
        _repository = repository;
        _embeddingProvider = embeddingProvider;
        _hybridSearch = hybridSearch;
        _context = context;
        _logger = logger;
    }

    public async Task<IEnumerable<MemoryModel>> GetRememberedMemoriesAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        _logger.LogInformation("User {UserId} requested all remembered memories", userId);

        var memories = await _repository.GetByUserIdAsync(userId, ct);
        return memories.OrderByDescending(m => m.Type).ThenByDescending(m => m.UpdatedAt);
    }

    public async Task<IEnumerable<MemoryModel>> SearchMemoriesAsync(
        Guid userId,
        string query,
        CancellationToken ct = default)
    {
        _logger.LogInformation("User {UserId} searching memories for: {Query}", userId, query);

        // Use hybrid search (vector + full-text) for combined ranking
        var embedding = await _embeddingProvider.GetEmbeddingAsync(query, ct);
        var hybridResults = await _hybridSearch.SearchAsync(userId, query, embedding, 15, ct);

        var merged = hybridResults.Select(r => r.Memory).ToList();

        _logger.LogDebug("Found {Count} memories matching query", merged.Count);
        return merged;
    }

    public async Task<bool> ForgetMemoryAsync(
        Guid userId,
        string query,
        CancellationToken ct = default)
    {
        _logger.LogInformation("User {UserId} requested to forget: {Query}", userId, query);

        // Find matching memories
        var matches = await SearchMemoriesAsync(userId, query, ct);
        var matchList = matches.ToList();

        if (matchList.Count == 0)
        {
            _logger.LogWarning("No memories found matching query: {Query}", query);
            return false;
        }

        // If there's a strong match (high similarity), forget the top one
        var topMatch = matchList.First();
        
        _logger.LogInformation("Forgetting memory {Id}: {Content}",
            topMatch.Id,
            topMatch.Content.Length > 50 ? topMatch.Content[..50] + "..." : topMatch.Content);

        await _repository.DeleteAsync(topMatch.Id, ct);
        return true;
    }

    public async Task<bool> ForgetMemoryByIdAsync(Guid memoryId, CancellationToken ct = default)
    {
        _logger.LogInformation("Forgetting memory by ID: {Id}", memoryId);

        var memory = await _repository.GetByIdAsync(memoryId, ct);
        if (memory == null)
        {
            _logger.LogWarning("Memory not found: {Id}", memoryId);
            return false;
        }

        await _repository.DeleteAsync(memoryId, ct);
        return true;
    }

    public async Task<MemoryModel?> CorrectMemoryAsync(
        Guid userId,
        string originalQuery,
        string correction,
        CancellationToken ct = default)
    {
        _logger.LogInformation("User {UserId} correcting memory. Original: {Original}, Correction: {Correction}",
            userId, originalQuery, correction);

        // Find the memory to correct
        var matches = await SearchMemoriesAsync(userId, originalQuery, ct);
        var matchList = matches.ToList();

        if (matchList.Count == 0)
        {
            _logger.LogWarning("No memories found to correct for query: {Query}", originalQuery);
            return null;
        }

        var memoryToCorrect = matchList.First();

        // Generate new embedding for the correction
        var newEmbedding = await _embeddingProvider.GetEmbeddingAsync(correction, ct);

        // Update the memory
        memoryToCorrect.Content = correction;
        memoryToCorrect.Embedding = new Pgvector.Vector(newEmbedding);
        memoryToCorrect.Source = "correction";

        var updated = await _repository.UpdateAsync(memoryToCorrect, ct);

        _logger.LogInformation("Corrected memory {Id}: {Content}",
            updated.Id,
            updated.Content.Length > 50 ? updated.Content[..50] + "..." : updated.Content);

        return updated;
    }

    public async Task SetSessionPrivacyAsync(
        Guid conversationId,
        bool storeMemories,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Setting session privacy for conversation {Id}: StoreMemories={Store}",
            conversationId, storeMemories);

        var conversation = await _context.Conversations
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct);

        if (conversation != null)
        {
            conversation.StoreMemories = storeMemories;
            await _context.SaveChangesAsync(ct);
        }
        else
        {
            _logger.LogWarning("Conversation not found: {Id}", conversationId);
        }
    }
}
