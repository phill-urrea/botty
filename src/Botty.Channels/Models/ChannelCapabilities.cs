namespace Botty.Channels;

/// <summary>
/// Describes the capabilities of a messaging channel
/// </summary>
public class ChannelCapabilities
{
    /// <summary>
    /// Whether the channel supports sending/receiving media (images, videos, files)
    /// </summary>
    public bool SupportsMedia { get; init; } = true;
    
    /// <summary>
    /// Whether the channel supports threaded conversations
    /// </summary>
    public bool SupportsThreads { get; init; } = false;
    
    /// <summary>
    /// Whether the channel supports message reactions (emoji)
    /// </summary>
    public bool SupportsReactions { get; init; } = false;
    
    /// <summary>
    /// Whether the channel supports editing sent messages
    /// </summary>
    public bool SupportsEdits { get; init; } = false;
    
    /// <summary>
    /// Whether the channel supports deleting messages
    /// </summary>
    public bool SupportsDeletes { get; init; } = false;
    
    /// <summary>
    /// Whether the channel supports voice notes/audio messages
    /// </summary>
    public bool SupportsVoiceNotes { get; init; } = false;
    
    /// <summary>
    /// Whether the channel supports typing indicators
    /// </summary>
    public bool SupportsTypingIndicator { get; init; } = false;
    
    /// <summary>
    /// Whether the channel supports read receipts
    /// </summary>
    public bool SupportsReadReceipts { get; init; } = false;
    
    /// <summary>
    /// Maximum message length in characters
    /// </summary>
    public int MaxMessageLength { get; init; } = 4096;
    
    /// <summary>
    /// Supported media MIME types
    /// </summary>
    public string[] SupportedMediaTypes { get; init; } = ["image/jpeg", "image/png", "image/gif"];

    /// <summary>
    /// Whether the channel supports sending polls.
    /// </summary>
    public bool SupportsPolls { get; init; } = false;

    /// <summary>
    /// Maximum number of poll options supported. 0 means no limit.
    /// </summary>
    public int MaxPollOptions { get; init; } = 0;
}
