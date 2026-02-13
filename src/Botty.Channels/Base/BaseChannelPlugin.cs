using Microsoft.Extensions.Logging;

namespace Botty.Channels.Base;

/// <summary>
/// Base implementation of IChannelPlugin with common functionality
/// </summary>
public abstract class BaseChannelPlugin : IChannelPlugin
{
    protected readonly ILogger Logger;
    protected ChannelConfig? Config;
    protected bool IsInitialized;
    protected DateTime? ConnectedSince;
    protected string? LastError;
    
    protected BaseChannelPlugin(ILogger logger)
    {
        Logger = logger;
    }
    
    // Abstract properties that must be implemented by each channel
    public abstract string Id { get; }
    public abstract string Label { get; }
    public abstract string Description { get; }
    public abstract ChannelCapabilities Capabilities { get; }
    public abstract ChannelConfigSchema ConfigSchema { get; }
    
    // Events
    public event EventHandler<IncomingMessage>? MessageReceived;
    public event EventHandler<MessageReaction>? ReactionReceived;
    public event EventHandler<ChannelEvent>? EventReceived;
    
    /// <summary>
    /// Initialize the channel with configuration
    /// </summary>
    public virtual async Task InitializeAsync(ChannelConfig config, CancellationToken ct = default)
    {
        Config = config;
        Logger.LogInformation("Initializing channel {ChannelId}", Id);
        
        try
        {
            await DoInitializeAsync(config, ct);
            IsInitialized = true;
            ConnectedSince = DateTime.UtcNow;
            LastError = null;
            OnEventReceived(ChannelEvent.Connected(Id));
            Logger.LogInformation("Channel {ChannelId} initialized successfully", Id);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Logger.LogError(ex, "Failed to initialize channel {ChannelId}", Id);
            throw;
        }
    }
    
    /// <summary>
    /// Channel-specific initialization logic
    /// </summary>
    protected abstract Task DoInitializeAsync(ChannelConfig config, CancellationToken ct);
    
    /// <summary>
    /// Get current connection status
    /// </summary>
    public virtual Task<ChannelStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var status = new ChannelStatus(
            IsConnected: IsInitialized && string.IsNullOrEmpty(LastError),
            AccountId: GetAccountId(),
            AccountName: GetAccountName(),
            ConnectedSince: ConnectedSince,
            Error: LastError
        );
        return Task.FromResult(status);
    }
    
    /// <summary>
    /// Override to provide account ID
    /// </summary>
    protected virtual string? GetAccountId() => null;
    
    /// <summary>
    /// Override to provide account name
    /// </summary>
    protected virtual string? GetAccountName() => null;
    
    /// <summary>
    /// Disconnect from the channel
    /// </summary>
    public virtual async Task DisconnectAsync(CancellationToken ct = default)
    {
        Logger.LogInformation("Disconnecting channel {ChannelId}", Id);
        
        try
        {
            await DoDisconnectAsync(ct);
            IsInitialized = false;
            ConnectedSince = null;
            OnEventReceived(ChannelEvent.Disconnected(Id));
            Logger.LogInformation("Channel {ChannelId} disconnected", Id);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error disconnecting channel {ChannelId}", Id);
            throw;
        }
    }
    
    /// <summary>
    /// Channel-specific disconnect logic
    /// </summary>
    protected abstract Task DoDisconnectAsync(CancellationToken ct);
    
    /// <summary>
    /// Send a text message
    /// </summary>
    public abstract Task<SendResult> SendTextAsync(OutboundMessage message, CancellationToken ct = default);
    
    /// <summary>
    /// Send a media message
    /// </summary>
    public virtual Task<SendResult> SendMediaAsync(OutboundMediaMessage message, CancellationToken ct = default)
    {
        if (!Capabilities.SupportsMedia)
        {
            return Task.FromResult(SendResult.Failed("This channel does not support media messages"));
        }
        
        return DoSendMediaAsync(message, ct);
    }
    
    /// <summary>
    /// Channel-specific media sending logic
    /// </summary>
    protected virtual Task<SendResult> DoSendMediaAsync(OutboundMediaMessage message, CancellationToken ct)
    {
        return Task.FromResult(SendResult.Failed("Media sending not implemented for this channel"));
    }
    
    /// <summary>
    /// Send a reaction
    /// </summary>
    public virtual Task<SendResult> SendReactionAsync(string chatId, string messageId, string emoji, CancellationToken ct = default)
    {
        if (!Capabilities.SupportsReactions)
        {
            return Task.FromResult(SendResult.Failed("This channel does not support reactions"));
        }
        
        return DoSendReactionAsync(chatId, messageId, emoji, ct);
    }
    
    /// <summary>
    /// Channel-specific reaction sending logic
    /// </summary>
    protected virtual Task<SendResult> DoSendReactionAsync(string chatId, string messageId, string emoji, CancellationToken ct)
    {
        return Task.FromResult(SendResult.Failed("Reactions not implemented for this channel"));
    }

    /// <summary>
    /// Send a poll
    /// </summary>
    public virtual Task<SendResult> SendPollAsync(OutboundPollMessage message, CancellationToken ct = default)
    {
        if (!Capabilities.SupportsPolls)
        {
            return Task.FromResult(SendResult.Failed("This channel does not support polls"));
        }

        return DoSendPollAsync(message, ct);
    }

    /// <summary>
    /// Channel-specific poll sending logic
    /// </summary>
    protected virtual Task<SendResult> DoSendPollAsync(OutboundPollMessage message, CancellationToken ct)
    {
        return Task.FromResult(SendResult.Failed("Polls not implemented for this channel"));
    }
    
    /// <summary>
    /// Send a typing indicator to a chat
    /// </summary>
    public virtual Task SendTypingIndicatorAsync(string chatId, CancellationToken ct = default)
    {
        if (!Capabilities.SupportsTypingIndicator)
            return Task.CompletedTask;
        return DoSendTypingIndicatorAsync(chatId, ct);
    }

    /// <summary>
    /// Channel-specific typing indicator logic
    /// </summary>
    protected virtual Task DoSendTypingIndicatorAsync(string chatId, CancellationToken ct)
        => Task.CompletedTask;

    /// <summary>
    /// Raise the MessageReceived event
    /// </summary>
    protected void OnMessageReceived(IncomingMessage message)
    {
        Logger.LogDebug("Message received on {ChannelId}: {MessageId}", Id, message.MessageId);
        MessageReceived?.Invoke(this, message);
    }
    
    /// <summary>
    /// Raise the ReactionReceived event
    /// </summary>
    protected void OnReactionReceived(MessageReaction reaction)
    {
        Logger.LogDebug("Reaction received on {ChannelId}: {Emoji} on {MessageId}", Id, reaction.Emoji, reaction.MessageId);
        ReactionReceived?.Invoke(this, reaction);
    }
    
    /// <summary>
    /// Raise the EventReceived event
    /// </summary>
    protected void OnEventReceived(ChannelEvent channelEvent)
    {
        Logger.LogDebug("Event on {ChannelId}: {EventType}", Id, channelEvent.Type);
        EventReceived?.Invoke(this, channelEvent);
    }
    
    /// <summary>
    /// Set an error state
    /// </summary>
    protected void SetError(string error)
    {
        LastError = error;
        OnEventReceived(ChannelEvent.Error(Id, error));
    }
    
    /// <summary>
    /// Clear error state
    /// </summary>
    protected void ClearError()
    {
        LastError = null;
    }
}
