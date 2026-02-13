using Botty.Core.Interfaces;
using Botty.Core.Models;
using Botty.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Botty.Infrastructure.Repositories;

/// <summary>
/// Repository for conversation and message persistence.
/// </summary>
public class ConversationRepository : IConversationRepository
{
    private readonly BottyDbContext _context;

    public ConversationRepository(BottyDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<Conversation> GetOrCreateAsync(
        string source,
        string? externalId,
        Guid userId = default,
        string? title = null,
        CancellationToken ct = default)
    {
        var normalizedExternalId = string.IsNullOrWhiteSpace(externalId) ? null : externalId.Trim();
        var existing = await _context.Conversations
            .FirstOrDefaultAsync(
                c => c.Source == source && c.ExternalId == normalizedExternalId,
                ct);

        if (existing != null)
        {
            return existing;
        }

        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Source = source,
            ExternalId = normalizedExternalId,
            Title = title,
            StoreMemories = source == "admin",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Conversations.Add(conversation);
        await _context.SaveChangesAsync(ct);
        return conversation;
    }

    /// <inheritdoc />
    public async Task<Message> AppendMessageAsync(
        Guid conversationId,
        MessageRole role,
        string content,
        string? senderName = null,
        string? externalId = null,
        string? senderId = null,
        CancellationToken ct = default)
    {
        var message = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = role,
            Content = content ?? string.Empty,
            SenderId = senderId,
            SenderName = senderName,
            ExternalId = externalId,
            CreatedAt = DateTime.UtcNow
        };

        _context.Messages.Add(message);

        await _context.Conversations
            .Where(c => c.Id == conversationId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(c => c.UpdatedAt, DateTime.UtcNow),
                ct);

        await _context.SaveChangesAsync(ct);
        return message;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Conversation>> ListConversationsAsync(CancellationToken ct = default)
    {
        return await _context.Conversations
            .OrderByDescending(c => c.UpdatedAt)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Message>> GetMergedFeedAsync(
        DateTime? since = null,
        int limit = 500,
        CancellationToken ct = default)
    {
        var query = _context.Messages.Include(m => m.Conversation).AsQueryable();

        if (since.HasValue)
        {
            query = query.Where(m => m.CreatedAt > since.Value);
        }

        var newestFirst = await query
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

        newestFirst.Reverse();
        return newestFirst;
    }

    /// <inheritdoc />
    public async Task<Conversation?> GetByIdAsync(Guid conversationId, CancellationToken ct = default)
    {
        return await _context.Conversations
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Message>> GetMessagesAsync(Guid conversationId, int limit = 100, CancellationToken ct = default)
    {
        var messages = await _context.Messages
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
        messages.Reverse();
        return messages;
    }

    /// <inheritdoc />
    public async Task UpdateMessageContentAsync(Guid messageId, string content, CancellationToken ct = default)
    {
        await _context.Messages
            .Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.Content, content),
                ct);
    }
}
