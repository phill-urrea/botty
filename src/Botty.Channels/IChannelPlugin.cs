namespace Botty.Channels;

/// <summary>
/// Interface for a messaging channel plugin.
/// Each channel (WhatsApp, Telegram, Slack, Discord, etc.) implements this interface.
/// </summary>
public interface IChannelPlugin
{
    /// <summary>
    /// Unique identifier for this channel (e.g., "telegram", "slack", "discord")
    /// </summary>
    string Id { get; }
    
    /// <summary>
    /// Human-readable name for display
    /// </summary>
    string Label { get; }
    
    /// <summary>
    /// Description of the channel
    /// </summary>
    string Description { get; }
    
    /// <summary>
    /// Channel capabilities (media support, threads, reactions, etc.)
    /// </summary>
    ChannelCapabilities Capabilities { get; }
    
    /// <summary>
    /// Configuration schema for this channel
    /// </summary>
    ChannelConfigSchema ConfigSchema { get; }
    
    /// <summary>
    /// Initialize the channel connection
    /// </summary>
    Task InitializeAsync(ChannelConfig config, CancellationToken ct = default);
    
    /// <summary>
    /// Get current connection status
    /// </summary>
    Task<ChannelStatus> GetStatusAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Disconnect from the channel
    /// </summary>
    Task DisconnectAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Send a text message
    /// </summary>
    Task<SendResult> SendTextAsync(OutboundMessage message, CancellationToken ct = default);
    
    /// <summary>
    /// Send a media message (image, video, document, etc.)
    /// </summary>
    Task<SendResult> SendMediaAsync(OutboundMediaMessage message, CancellationToken ct = default);
    
    /// <summary>
    /// Send a reaction to a message
    /// </summary>
    Task<SendResult> SendReactionAsync(string chatId, string messageId, string emoji, CancellationToken ct = default);

    /// <summary>
    /// Send a poll message
    /// </summary>
    Task<SendResult> SendPollAsync(OutboundPollMessage message, CancellationToken ct = default);

    /// <summary>
    /// Send a typing indicator to a chat
    /// </summary>
    Task SendTypingIndicatorAsync(string chatId, CancellationToken ct = default);
    
    /// <summary>
    /// Event fired when a message is received
    /// </summary>
    event EventHandler<IncomingMessage>? MessageReceived;
    
    /// <summary>
    /// Event fired when a reaction is received
    /// </summary>
    event EventHandler<MessageReaction>? ReactionReceived;
    
    /// <summary>
    /// Event fired for general channel events (connected, disconnected, errors)
    /// </summary>
    event EventHandler<ChannelEvent>? EventReceived;
}
