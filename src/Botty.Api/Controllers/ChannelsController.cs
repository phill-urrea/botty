using Botty.Channels;
using Botty.Channels.Registry;
using Botty.Channels.WhatsApp;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Botty.Api.Services;

namespace Botty.Api.Controllers;

/// <summary>
/// API controller for managing messaging channels
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ChannelsController : ControllerBase
{
    private const string WhatsAppChannelId = "whatsapp";

    private readonly IChannelRegistry _channelRegistry;
    private readonly IChannelConfigRepository _configRepository;
    private readonly IConversationRepository _conversationRepository;
    private readonly IFeedBroadcastService _feedBroadcast;
    private readonly IWhatsAppBridgeClient _whatsAppBridge;
    private readonly ILogger<ChannelsController> _logger;

    public ChannelsController(
        IChannelRegistry channelRegistry,
        IChannelConfigRepository configRepository,
        IConversationRepository conversationRepository,
        IFeedBroadcastService feedBroadcast,
        IWhatsAppBridgeClient whatsAppBridge,
        ILogger<ChannelsController> logger)
    {
        _channelRegistry = channelRegistry;
        _configRepository = configRepository;
        _conversationRepository = conversationRepository;
        _feedBroadcast = feedBroadcast;
        _whatsAppBridge = whatsAppBridge;
        _logger = logger;
    }

    /// <summary>
    /// Get all registered channels
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ChannelDto>>> GetChannels(CancellationToken ct)
    {
        var channels = _channelRegistry.GetAllChannels();
        var configs = await _configRepository.GetAllAsync(ct);
        var configDict = configs.ToDictionary(c => c.ChannelId);

        var result = new List<ChannelDto>();
        
        foreach (var channel in channels)
        {
            var status = await channel.GetStatusAsync(ct);
            configDict.TryGetValue(channel.Id, out var config);

            // WhatsApp: use bridge status so Channels page matches WhatsApp page (bridge can be connected without channel "Connect" click)
            if (channel.Id == WhatsAppChannelId)
            {
                var bridgeStatus = await _whatsAppBridge.GetStatusAsync(ct);
                status = new ChannelStatus(
                    IsConnected: bridgeStatus.Connected,
                    AccountId: bridgeStatus.PhoneNumber,
                    AccountName: bridgeStatus.PhoneNumber,
                    ConnectedSince: ParseConnectedSince(bridgeStatus.LastSeen),
                    Error: status.Error
                );
            }
            
            result.Add(new ChannelDto
            {
                Id = channel.Id,
                Label = channel.Label,
                Description = channel.Description,
                IsEnabled = config?.Enabled ?? false,
                IsConnected = status.IsConnected,
                AccountId = status.AccountId,
                AccountName = status.AccountName,
                ConnectedSince = status.ConnectedSince,
                LastError = status.Error ?? config?.LastError,
                Capabilities = new ChannelCapabilitiesDto
                {
                    SupportsMedia = channel.Capabilities.SupportsMedia,
                    SupportsThreads = channel.Capabilities.SupportsThreads,
                    SupportsReactions = channel.Capabilities.SupportsReactions,
                    SupportsEdits = channel.Capabilities.SupportsEdits,
                    SupportsDeletes = channel.Capabilities.SupportsDeletes,
                    SupportsVoiceNotes = channel.Capabilities.SupportsVoiceNotes,
                    MaxMessageLength = channel.Capabilities.MaxMessageLength
                }
            });
        }
        
        return Ok(result);
    }

    /// <summary>
    /// Get a specific channel by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ChannelDetailDto>> GetChannel(string id, CancellationToken ct)
    {
        var channel = _channelRegistry.GetChannel(id);
        if (channel == null)
        {
            return NotFound(new { error = $"Channel '{id}' not found" });
        }

        var status = await channel.GetStatusAsync(ct);

        // WhatsApp: use bridge status so Channels page matches WhatsApp page
        if (id == WhatsAppChannelId)
        {
            var bridgeStatus = await _whatsAppBridge.GetStatusAsync(ct);
            status = new ChannelStatus(
                IsConnected: bridgeStatus.Connected,
                AccountId: bridgeStatus.PhoneNumber,
                AccountName: bridgeStatus.PhoneNumber,
                ConnectedSince: ParseConnectedSince(bridgeStatus.LastSeen),
                Error: status.Error
            );
        }

        var config = await _configRepository.GetByChannelIdAsync(id, ct);

        return Ok(new ChannelDetailDto
        {
            Id = channel.Id,
            Label = channel.Label,
            Description = channel.Description,
            IsEnabled = config?.Enabled ?? false,
            IsConnected = status.IsConnected,
            AccountId = status.AccountId,
            AccountName = status.AccountName,
            ConnectedSince = status.ConnectedSince,
            LastError = status.Error ?? config?.LastError,
            Capabilities = new ChannelCapabilitiesDto
            {
                SupportsMedia = channel.Capabilities.SupportsMedia,
                SupportsThreads = channel.Capabilities.SupportsThreads,
                SupportsReactions = channel.Capabilities.SupportsReactions,
                SupportsEdits = channel.Capabilities.SupportsEdits,
                SupportsDeletes = channel.Capabilities.SupportsDeletes,
                SupportsVoiceNotes = channel.Capabilities.SupportsVoiceNotes,
                MaxMessageLength = channel.Capabilities.MaxMessageLength
            },
            ConfigSchema = channel.ConfigSchema.Fields.Select(f => new ChannelConfigFieldDto
            {
                Key = f.Key,
                Label = f.Label,
                Description = f.Description,
                Type = f.Type.ToString().ToLowerInvariant(),
                IsSensitive = f.IsSensitive,
                IsRequired = f.IsRequired,
                DefaultValue = f.DefaultValue
            }).ToList(),
            Config = config?.Config ?? new Dictionary<string, string>()
        });
    }

    /// <summary>
    /// Get channel connection status
    /// </summary>
    [HttpGet("{id}/status")]
    public async Task<ActionResult<ChannelStatusDto>> GetChannelStatus(string id, CancellationToken ct)
    {
        var status = await _channelRegistry.GetStatusAsync(id, ct);

        if (id == WhatsAppChannelId)
        {
            var bridgeStatus = await _whatsAppBridge.GetStatusAsync(ct);
            status = new ChannelStatus(
                IsConnected: bridgeStatus.Connected,
                AccountId: bridgeStatus.PhoneNumber,
                AccountName: bridgeStatus.PhoneNumber,
                ConnectedSince: ParseConnectedSince(bridgeStatus.LastSeen),
                Error: status.Error
            );
        }
        
        return Ok(new ChannelStatusDto
        {
            ChannelId = id,
            IsConnected = status.IsConnected,
            AccountId = status.AccountId,
            AccountName = status.AccountName,
            ConnectedSince = status.ConnectedSince,
            Error = status.Error
        });
    }

    /// <summary>
    /// Connect/initialize a channel
    /// </summary>
    [HttpPost("{id}/connect")]
    public async Task<ActionResult<ChannelStatusDto>> ConnectChannel(string id, CancellationToken ct)
    {
        var channel = _channelRegistry.GetChannel(id);
        if (channel == null)
        {
            return NotFound(new { error = $"Channel '{id}' not found" });
        }

        try
        {
            if (_channelRegistry is ChannelRegistry registry)
            {
                await registry.InitializeChannelAsync(id, ct);
            }
            
            await _configRepository.UpdateLastConnectedAsync(id, ct);
            
            var status = await channel.GetStatusAsync(ct);
            
            _logger.LogInformation("Channel {ChannelId} connected successfully", id);
            
            return Ok(new ChannelStatusDto
            {
                ChannelId = id,
                IsConnected = status.IsConnected,
                AccountId = status.AccountId,
                AccountName = status.AccountName,
                ConnectedSince = status.ConnectedSince,
                Error = status.Error
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect channel {ChannelId}", id);
            await _configRepository.UpdateLastErrorAsync(id, ex.Message, ct);
            
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Disconnect a channel
    /// </summary>
    [HttpPost("{id}/disconnect")]
    public async Task<ActionResult> DisconnectChannel(string id, CancellationToken ct)
    {
        var channel = _channelRegistry.GetChannel(id);
        if (channel == null)
        {
            return NotFound(new { error = $"Channel '{id}' not found" });
        }

        try
        {
            if (_channelRegistry is ChannelRegistry registry)
            {
                await registry.DisconnectChannelAsync(id, ct);
            }
            
            _logger.LogInformation("Channel {ChannelId} disconnected", id);
            
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to disconnect channel {ChannelId}", id);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Update channel configuration
    /// </summary>
    [HttpPut("{id}/config")]
    public async Task<ActionResult<ChannelConfigEntity>> UpdateConfig(
        string id,
        [FromBody] UpdateChannelConfigRequest request,
        CancellationToken ct)
    {
        var channel = _channelRegistry.GetChannel(id);
        if (channel == null)
        {
            return NotFound(new { error = $"Channel '{id}' not found" });
        }

        var existingConfig = await _configRepository.GetByChannelIdAsync(id, ct);
        
        var configEntity = new ChannelConfigEntity
        {
            Id = existingConfig?.Id ?? Guid.NewGuid(),
            ChannelId = id,
            Enabled = request.Enabled ?? existingConfig?.Enabled ?? false,
            Config = request.Config ?? existingConfig?.Config ?? new Dictionary<string, string>(),
            CreatedAt = existingConfig?.CreatedAt ?? DateTime.UtcNow
        };

        var result = await _configRepository.UpsertAsync(configEntity, ct);
        
        _logger.LogInformation("Updated configuration for channel {ChannelId}", id);
        
        return Ok(result);
    }

    /// <summary>
    /// Send a test message through a channel
    /// </summary>
    [HttpPost("{id}/send")]
    public async Task<ActionResult<SendResultDto>> SendMessage(
        string id,
        [FromBody] SendMessageRequest request,
        CancellationToken ct)
    {
        var result = await _channelRegistry.SendToChannelAsync(
            id,
            new OutboundMessage(request.ChatId, request.Text, request.ReplyToMessageId, request.ThreadId),
            ct);

        if (result.Success)
        {
            var conversation = await _conversationRepository.GetOrCreateAsync(id, request.ChatId, Guid.Empty, null, ct);
            var message = await _conversationRepository.AppendMessageAsync(
                conversation.Id, MessageRole.Assistant, request.Text, null, result.MessageId, ct);
            _feedBroadcast.BroadcastNewMessage(new FeedMessageDto
            {
                Id = message.Id,
                ConversationId = conversation.Id,
                Source = conversation.Source,
                ExternalId = conversation.ExternalId,
                Role = message.Role.ToString().ToLowerInvariant(),
                Content = message.Content,
                SenderName = message.SenderName,
                CreatedAt = message.CreatedAt
            });
        }

        return Ok(new SendResultDto
        {
            Success = result.Success,
            MessageId = result.MessageId,
            Error = result.Error
        });
    }

    /// <summary>
    /// Webhook endpoint for WhatsApp bridge incoming messages
    /// </summary>
    [HttpPost("whatsapp/webhook")]
    public async Task<ActionResult> WhatsAppWebhook([FromBody] WhatsAppIncomingMessage message, CancellationToken ct)
    {
        var whatsApp = _channelRegistry.GetChannel("whatsapp") as WhatsAppChannelPlugin;
        if (whatsApp == null)
        {
            return BadRequest(new { error = "WhatsApp channel not registered" });
        }

        await whatsApp.HandleIncomingMessageAsync(message, ct);
        return Ok(new { received = true });
    }

    private static DateTime? ParseConnectedSince(string? lastSeen)
    {
        if (string.IsNullOrWhiteSpace(lastSeen)) return null;
        return DateTime.TryParse(lastSeen, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : null;
    }
}

#region DTOs

public class ChannelDto
{
    public required string Id { get; set; }
    public required string Label { get; set; }
    public required string Description { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsConnected { get; set; }
    public string? AccountId { get; set; }
    public string? AccountName { get; set; }
    public DateTime? ConnectedSince { get; set; }
    public string? LastError { get; set; }
    public required ChannelCapabilitiesDto Capabilities { get; set; }
}

public class ChannelDetailDto : ChannelDto
{
    public List<ChannelConfigFieldDto> ConfigSchema { get; set; } = [];
    public Dictionary<string, string> Config { get; set; } = new();
}

public class ChannelCapabilitiesDto
{
    public bool SupportsMedia { get; set; }
    public bool SupportsThreads { get; set; }
    public bool SupportsReactions { get; set; }
    public bool SupportsEdits { get; set; }
    public bool SupportsDeletes { get; set; }
    public bool SupportsVoiceNotes { get; set; }
    public int MaxMessageLength { get; set; }
}

public class ChannelConfigFieldDto
{
    public required string Key { get; set; }
    public required string Label { get; set; }
    public string? Description { get; set; }
    public required string Type { get; set; }
    public bool IsSensitive { get; set; }
    public bool IsRequired { get; set; }
    public string? DefaultValue { get; set; }
}

public class ChannelStatusDto
{
    public required string ChannelId { get; set; }
    public bool IsConnected { get; set; }
    public string? AccountId { get; set; }
    public string? AccountName { get; set; }
    public DateTime? ConnectedSince { get; set; }
    public string? Error { get; set; }
}

public class UpdateChannelConfigRequest
{
    public bool? Enabled { get; set; }
    public Dictionary<string, string>? Config { get; set; }
}

public class SendMessageRequest
{
    public required string ChatId { get; set; }
    public required string Text { get; set; }
    public string? ReplyToMessageId { get; set; }
    public string? ThreadId { get; set; }
}

public class SendResultDto
{
    public bool Success { get; set; }
    public string? MessageId { get; set; }
    public string? Error { get; set; }
}

#endregion
