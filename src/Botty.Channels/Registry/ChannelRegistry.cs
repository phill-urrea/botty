using System.Collections.Concurrent;
using Botty.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Botty.Channels.Registry;

/// <summary>
/// Default implementation of IChannelRegistry
/// </summary>
public class ChannelRegistry : IChannelRegistry
{
    private readonly ConcurrentDictionary<string, IChannelPlugin> _channels = new();
    private readonly ILogger<ChannelRegistry> _logger;
    private readonly ISecretStore _secretStore;
    private readonly IChannelConfigRepository _configRepository;
    
    public event EventHandler<ChannelMessageEventArgs>? MessageReceived;
    
    public ChannelRegistry(
        ILogger<ChannelRegistry> logger,
        ISecretStore secretStore,
        IChannelConfigRepository configRepository)
    {
        _logger = logger;
        _secretStore = secretStore;
        _configRepository = configRepository;
    }
    
    /// <summary>
    /// Register a channel plugin
    /// </summary>
    public void Register(IChannelPlugin plugin)
    {
        if (_channels.TryAdd(plugin.Id, plugin))
        {
            _logger.LogInformation("Registered channel plugin: {ChannelId}", plugin.Id);
            
            // Subscribe to plugin events
            plugin.MessageReceived += OnPluginMessageReceived;
            plugin.EventReceived += OnPluginEventReceived;
        }
        else
        {
            _logger.LogWarning("Channel plugin {ChannelId} is already registered", plugin.Id);
        }
    }
    
    /// <summary>
    /// Unregister a channel plugin
    /// </summary>
    public void Unregister(string channelId)
    {
        if (_channels.TryRemove(channelId, out var plugin))
        {
            // Unsubscribe from events
            plugin.MessageReceived -= OnPluginMessageReceived;
            plugin.EventReceived -= OnPluginEventReceived;
            
            _logger.LogInformation("Unregistered channel plugin: {ChannelId}", channelId);
        }
    }
    
    /// <summary>
    /// Get a channel by ID
    /// </summary>
    public IChannelPlugin? GetChannel(string channelId)
    {
        return _channels.TryGetValue(channelId, out var plugin) ? plugin : null;
    }
    
    /// <summary>
    /// Get all registered channels
    /// </summary>
    public IEnumerable<IChannelPlugin> GetAllChannels()
    {
        return _channels.Values;
    }
    
    /// <summary>
    /// Get all connected channels
    /// </summary>
    public IEnumerable<IChannelPlugin> GetConnectedChannels()
    {
        return _channels.Values
            .Where(c => c.GetStatusAsync().GetAwaiter().GetResult().IsConnected);
    }
    
    /// <summary>
    /// Initialize all enabled channels
    /// </summary>
    public async Task InitializeAllAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Initializing all enabled channels");
        
        foreach (var plugin in _channels.Values)
        {
            try
            {
                var dbConfig = await _configRepository.GetByChannelIdAsync(plugin.Id, ct);
                
                if (dbConfig is null || !dbConfig.Enabled)
                {
                    _logger.LogDebug("Channel {ChannelId} is not enabled, skipping", plugin.Id);
                    continue;
                }
                
                var config = new ChannelConfig(plugin.Id, dbConfig.Config, _secretStore);
                await plugin.InitializeAsync(config, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize channel {ChannelId}", plugin.Id);
            }
        }
    }
    
    /// <summary>
    /// Initialize a specific channel
    /// </summary>
    public async Task InitializeChannelAsync(string channelId, CancellationToken ct = default)
    {
        var plugin = GetChannel(channelId);
        if (plugin is null)
        {
            throw new InvalidOperationException($"Channel {channelId} not found");
        }
        
        var dbConfig = await _configRepository.GetByChannelIdAsync(channelId, ct);
        var configValues = dbConfig?.Config ?? new Dictionary<string, string>();
        
        var config = new ChannelConfig(channelId, configValues, _secretStore);
        await plugin.InitializeAsync(config, ct);
    }
    
    /// <summary>
    /// Disconnect a specific channel
    /// </summary>
    public async Task DisconnectChannelAsync(string channelId, CancellationToken ct = default)
    {
        var plugin = GetChannel(channelId);
        if (plugin is null)
        {
            throw new InvalidOperationException($"Channel {channelId} not found");
        }
        
        await plugin.DisconnectAsync(ct);
    }
    
    /// <summary>
    /// Get status of a channel
    /// </summary>
    public async Task<ChannelStatus> GetStatusAsync(string channelId, CancellationToken ct = default)
    {
        var plugin = GetChannel(channelId);
        if (plugin is null)
        {
            return ChannelStatus.WithError($"Channel {channelId} not found");
        }
        
        return await plugin.GetStatusAsync(ct);
    }
    
    /// <summary>
    /// Send a message to a channel
    /// </summary>
    public async Task<SendResult> SendToChannelAsync(string channelId, OutboundMessage message, CancellationToken ct = default)
    {
        var plugin = GetChannel(channelId);
        if (plugin is null)
        {
            return SendResult.Failed($"Channel {channelId} not found");
        }
        
        var status = await plugin.GetStatusAsync(ct);
        if (!status.IsConnected)
        {
            return SendResult.Failed($"Channel {channelId} is not connected");
        }
        
        return await plugin.SendTextAsync(message, ct);
    }
    
    private void OnPluginMessageReceived(object? sender, IncomingMessage message)
    {
        if (sender is IChannelPlugin plugin)
        {
            _logger.LogDebug("Forwarding message from channel {ChannelId}", plugin.Id);
            MessageReceived?.Invoke(this, new ChannelMessageEventArgs
            {
                ChannelId = plugin.Id,
                Message = message
            });
        }
    }
    
    private void OnPluginEventReceived(object? sender, ChannelEvent channelEvent)
    {
        if (sender is IChannelPlugin plugin)
        {
            _logger.LogInformation(
                "Channel event: {ChannelId} - {EventType}: {Message}",
                plugin.Id, channelEvent.Type, channelEvent.Message);
        }
    }
}
