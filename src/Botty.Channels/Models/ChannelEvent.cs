namespace Botty.Channels;

/// <summary>
/// Types of channel events
/// </summary>
public enum ChannelEventType
{
    Connected,
    Disconnected,
    Reconnecting,
    Error,
    RateLimited,
    UserJoined,
    UserLeft,
    TypingStarted,
    TypingStopped,
    MessageEdited,
    MessageDeleted,
    Custom
}

/// <summary>
/// A channel event (connection changes, errors, etc.)
/// </summary>
public record ChannelEvent
{
    public required ChannelEventType Type { get; init; }
    public required string ChannelId { get; init; }
    public required DateTime Timestamp { get; init; }
    public string? Message { get; init; }
    public string? ChatId { get; init; }
    public string? UserId { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
    
    public static ChannelEvent Connected(string channelId) =>
        new()
        {
            Type = ChannelEventType.Connected,
            ChannelId = channelId,
            Timestamp = DateTime.UtcNow
        };
    
    public static ChannelEvent Disconnected(string channelId, string? reason = null) =>
        new()
        {
            Type = ChannelEventType.Disconnected,
            ChannelId = channelId,
            Timestamp = DateTime.UtcNow,
            Message = reason
        };
    
    public static ChannelEvent Error(string channelId, string error) =>
        new()
        {
            Type = ChannelEventType.Error,
            ChannelId = channelId,
            Timestamp = DateTime.UtcNow,
            Message = error
        };
}
