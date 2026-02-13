namespace Botty.Core.Models;

/// <summary>
/// Represents a conversation with the assistant.
/// </summary>
public class Conversation
{
    /// <summary>
    /// Unique identifier for the conversation.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// User ID this conversation belongs to.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Title or summary of the conversation.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Source of the conversation (whatsapp, api, admin, etc.).
    /// </summary>
    public required string Source { get; set; }

    /// <summary>
    /// External identifier (e.g., WhatsApp chat ID).
    /// </summary>
    public string? ExternalId { get; set; }

    /// <summary>
    /// Messages in this conversation.
    /// </summary>
    public List<Message> Messages { get; set; } = [];

    /// <summary>
    /// Whether to store memories from this conversation.
    /// </summary>
    public bool StoreMemories { get; set; } = true;

    /// <summary>
    /// When the conversation was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the conversation was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Represents a message in a conversation.
/// </summary>
public class Message
{
    /// <summary>
    /// Unique identifier for the message.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Conversation this message belongs to.
    /// </summary>
    public Guid ConversationId { get; set; }

    /// <summary>
    /// Navigation to the conversation (for EF include).
    /// </summary>
    public Conversation? Conversation { get; set; }

    /// <summary>
    /// Role of the message sender.
    /// </summary>
    public required MessageRole Role { get; set; }

    /// <summary>
    /// Content of the message.
    /// </summary>
    public required string Content { get; set; }

    /// <summary>
    /// Canonical sender id (phone/chat/user id) for channel-originated messages.
    /// </summary>
    public string? SenderId { get; set; }

    /// <summary>
    /// Name of the sender (for third-party messages in WhatsApp, etc.).
    /// </summary>
    public string? SenderName { get; set; }

    /// <summary>
    /// External identifier for the message.
    /// </summary>
    public string? ExternalId { get; set; }

    /// <summary>
    /// Metadata about the message.
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }

    /// <summary>
    /// When the message was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Role of a message sender.
/// </summary>
public enum MessageRole
{
    /// <summary>
    /// Message from the user.
    /// </summary>
    User,

    /// <summary>
    /// Message from the assistant.
    /// </summary>
    Assistant,

    /// <summary>
    /// System message.
    /// </summary>
    System,

    /// <summary>
    /// Message from a third party (e.g., in WhatsApp group).
    /// </summary>
    ThirdParty
}
