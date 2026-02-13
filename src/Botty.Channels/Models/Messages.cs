namespace Botty.Channels;

/// <summary>
/// Message types
/// </summary>
public enum MessageType
{
    Text,
    Image,
    Video,
    Audio,
    Voice,
    Document,
    Location,
    Contact,
    Sticker
}

/// <summary>
/// An outbound text message
/// </summary>
public record OutboundMessage(
    string ChatId,
    string Text,
    string? ReplyToMessageId = null,
    string? ThreadId = null
);

/// <summary>
/// An outbound media message
/// </summary>
public record OutboundMediaMessage(
    string ChatId,
    Stream MediaStream,
    string MediaType,
    string? FileName = null,
    string? Caption = null,
    string? ReplyToMessageId = null,
    string? ThreadId = null
);

/// <summary>
/// An incoming message from a channel
/// </summary>
public record IncomingMessage
{
    public required string MessageId { get; init; }
    public required string ChatId { get; init; }
    public required string SenderId { get; init; }
    public required string SenderName { get; init; }
    public required string Text { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string ChannelId { get; init; }
    public MessageType Type { get; init; } = MessageType.Text;
    public string? MediaUrl { get; init; }
    public string? MediaType { get; init; }
    public string? ReplyToMessageId { get; init; }
    public string? ThreadId { get; init; }
    
    /// <summary>
    /// Additional metadata specific to the channel
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// A reaction to a message
/// </summary>
public record MessageReaction
{
    public required string MessageId { get; init; }
    public required string ChatId { get; init; }
    public required string UserId { get; init; }
    public required string UserName { get; init; }
    public required string Emoji { get; init; }
    public required string ChannelId { get; init; }
    public required DateTime Timestamp { get; init; }
    public bool IsRemoval { get; init; } = false;
}

/// <summary>
/// Result of sending a message
/// </summary>
public record SendResult
{
    public required bool Success { get; init; }
    public string? MessageId { get; init; }
    public string? Error { get; init; }
    
    public static SendResult Ok(string? messageId = null) =>
        new() { Success = true, MessageId = messageId };
    
    public static SendResult Failed(string error) =>
        new() { Success = false, Error = error };
}
