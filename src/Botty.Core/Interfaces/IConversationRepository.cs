using Botty.Core.Models;

namespace Botty.Core.Interfaces;

/// <summary>
/// Repository for conversation and message persistence (admin + channel threads).
/// </summary>
public interface IConversationRepository
{
    /// <summary>
    /// Gets or creates a conversation by source and external id.
    /// </summary>
    Task<Conversation> GetOrCreateAsync(
        string source,
        string? externalId,
        Guid userId = default,
        string? title = null,
        CancellationToken ct = default);

    /// <summary>
    /// Appends a message to a conversation.
    /// </summary>
    Task<Message> AppendMessageAsync(
        Guid conversationId,
        MessageRole role,
        string content,
        string? senderName = null,
        string? externalId = null,
        string? senderId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Lists all conversations.
    /// </summary>
    Task<IReadOnlyList<Conversation>> ListConversationsAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets all messages from all conversations merged and ordered by CreatedAt (for feed).
    /// </summary>
    Task<IReadOnlyList<Message>> GetMergedFeedAsync(
        DateTime? since = null,
        int limit = 500,
        CancellationToken ct = default);

    /// <summary>
    /// Gets a conversation by id.
    /// </summary>
    Task<Conversation?> GetByIdAsync(Guid conversationId, CancellationToken ct = default);

    /// <summary>
    /// Gets messages for a conversation in chronological order (oldest first).
    /// Returns the most recent up to limit messages.
    /// </summary>
    Task<IReadOnlyList<Message>> GetMessagesAsync(Guid conversationId, int limit = 100, CancellationToken ct = default);

    /// <summary>
    /// Updates the content of an existing message.
    /// </summary>
    Task UpdateMessageContentAsync(Guid messageId, string content, CancellationToken ct = default);
}
