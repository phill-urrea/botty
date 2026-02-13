namespace Botty.Channels;

/// <summary>
/// Registry for managing channel plugins
/// </summary>
public interface IChannelRegistry
{
    /// <summary>
    /// Register a channel plugin
    /// </summary>
    void Register(IChannelPlugin plugin);
    
    /// <summary>
    /// Unregister a channel plugin by ID
    /// </summary>
    void Unregister(string channelId);
    
    /// <summary>
    /// Get a channel plugin by ID
    /// </summary>
    IChannelPlugin? GetChannel(string channelId);
    
    /// <summary>
    /// Get all registered channel plugins
    /// </summary>
    IEnumerable<IChannelPlugin> GetAllChannels();
    
    /// <summary>
    /// Get all connected channel plugins
    /// </summary>
    IEnumerable<IChannelPlugin> GetConnectedChannels();
    
    /// <summary>
    /// Initialize all enabled channels
    /// </summary>
    Task InitializeAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Initialize a specific channel
    /// </summary>
    Task InitializeChannelAsync(string channelId, CancellationToken ct = default);
    
    /// <summary>
    /// Get status for a specific channel
    /// </summary>
    Task<ChannelStatus> GetStatusAsync(string channelId, CancellationToken ct = default);
    
    /// <summary>
    /// Send a message to a specific channel
    /// </summary>
    Task<SendResult> SendToChannelAsync(string channelId, OutboundMessage message, CancellationToken ct = default);
    
    /// <summary>
    /// Event fired when any channel receives a message
    /// </summary>
    event EventHandler<ChannelMessageEventArgs>? MessageReceived;
}

/// <summary>
/// Event args for channel message events
/// </summary>
public class ChannelMessageEventArgs : EventArgs
{
    public required string ChannelId { get; init; }
    public required IncomingMessage Message { get; init; }
}
