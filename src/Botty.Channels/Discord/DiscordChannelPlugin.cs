using Botty.Channels.Base;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Botty.Channels.Discord;

/// <summary>
/// Discord channel plugin using Discord.Net
/// </summary>
public class DiscordChannelPlugin : BaseChannelPlugin
{
    private readonly DiscordOptions _options;
    private DiscordSocketClient? _client;
    private string? _botUserId;
    private string? _botUserName;
    
    public override string Id => "discord";
    public override string Label => "Discord";
    public override string Description => "Discord server and DM messaging";
    
    public override ChannelCapabilities Capabilities => new()
    {
        SupportsMedia = true,
        SupportsThreads = true,
        SupportsReactions = true,
        SupportsEdits = true,
        SupportsDeletes = true,
        SupportsVoiceNotes = false,
        SupportsTypingIndicator = true,
        SupportsReadReceipts = false,
        MaxMessageLength = 2000,
        SupportedMediaTypes = ["image/jpeg", "image/png", "image/gif", "image/webp", "video/mp4", "audio/mpeg", "application/pdf"]
    };
    
    public override ChannelConfigSchema ConfigSchema => new()
    {
        ChannelId = Id,
        Fields =
        [
            new ChannelConfigField
            {
                Key = "bot_token",
                Label = "Bot Token",
                Description = "Discord Bot token from Discord Developer Portal",
                Type = ChannelConfigFieldType.Secret,
                IsSensitive = true,
                IsRequired = true
            },
            new ChannelConfigField
            {
                Key = "allowed_guilds",
                Label = "Allowed Guilds",
                Description = "Comma-separated list of allowed guild IDs (empty = allow all)",
                Type = ChannelConfigFieldType.String
            }
        ]
    };
    
    public DiscordChannelPlugin(
        IOptions<DiscordOptions> options,
        ILogger<DiscordChannelPlugin> logger)
        : base(logger)
    {
        _options = options.Value;
    }
    
    protected override async Task DoInitializeAsync(ChannelConfig config, CancellationToken ct)
    {
        var botToken = await config.GetRequiredSecretAsync("bot_token", ct);
        
        var socketConfig = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | 
                            GatewayIntents.DirectMessages | GatewayIntents.MessageContent |
                            GatewayIntents.GuildMessageReactions
        };
        
        _client = new DiscordSocketClient(socketConfig);
        
        // Set up event handlers
        _client.MessageReceived += HandleMessageReceived;
        _client.ReactionAdded += HandleReactionAdded;
        _client.ReactionRemoved += HandleReactionRemoved;
        _client.Ready += HandleReady;
        _client.Disconnected += HandleDisconnected;
        _client.Log += HandleLog;
        
        // Login and start
        await _client.LoginAsync(TokenType.Bot, botToken);
        await _client.StartAsync();
        
        // Wait for ready state (with timeout)
        var readyTcs = new TaskCompletionSource<bool>();
        _client.Ready += () => { readyTcs.TrySetResult(true); return Task.CompletedTask; };
        
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        
        try
        {
            await readyTcs.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException("Discord connection timed out");
        }
    }
    
    protected override async Task DoDisconnectAsync(CancellationToken ct)
    {
        if (_client != null)
        {
            _client.MessageReceived -= HandleMessageReceived;
            _client.ReactionAdded -= HandleReactionAdded;
            _client.ReactionRemoved -= HandleReactionRemoved;
            _client.Ready -= HandleReady;
            _client.Disconnected -= HandleDisconnected;
            _client.Log -= HandleLog;
            
            await _client.StopAsync();
            await _client.LogoutAsync();
            await _client.DisposeAsync();
            _client = null;
        }
        
        _botUserId = null;
        _botUserName = null;
    }
    
    protected override string? GetAccountId() => _botUserId;
    protected override string? GetAccountName() => _botUserName;
    
    public override async Task<SendResult> SendTextAsync(OutboundMessage message, CancellationToken ct = default)
    {
        if (_client == null)
        {
            return SendResult.Failed("Channel not initialized");
        }
        
        try
        {
            if (!ulong.TryParse(message.ChatId, out var channelId))
            {
                return SendResult.Failed("Invalid channel ID");
            }
            
            var channel = await _client.GetChannelAsync(channelId);
            
            if (channel is not IMessageChannel messageChannel)
            {
                return SendResult.Failed("Channel is not a message channel");
            }
            
            MessageReference? reference = null;
            if (!string.IsNullOrEmpty(message.ReplyToMessageId) && 
                ulong.TryParse(message.ReplyToMessageId, out var replyToId))
            {
                reference = new MessageReference(replyToId);
            }
            
            var sentMessage = await messageChannel.SendMessageAsync(
                text: message.Text,
                messageReference: reference);
            
            return SendResult.Ok(sentMessage.Id.ToString());
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error sending Discord message to {ChannelId}", message.ChatId);
            return SendResult.Failed(ex.Message);
        }
    }
    
    protected override async Task<SendResult> DoSendMediaAsync(OutboundMediaMessage message, CancellationToken ct)
    {
        if (_client == null)
        {
            return SendResult.Failed("Channel not initialized");
        }
        
        try
        {
            if (!ulong.TryParse(message.ChatId, out var channelId))
            {
                return SendResult.Failed("Invalid channel ID");
            }
            
            var channel = await _client.GetChannelAsync(channelId);
            
            if (channel is not IMessageChannel messageChannel)
            {
                return SendResult.Failed("Channel is not a message channel");
            }
            
            var sentMessage = await messageChannel.SendFileAsync(
                stream: message.MediaStream,
                filename: message.FileName ?? "file",
                text: message.Caption);
            
            return SendResult.Ok(sentMessage.Id.ToString());
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error sending Discord media to {ChannelId}", message.ChatId);
            return SendResult.Failed(ex.Message);
        }
    }
    
    protected override async Task<SendResult> DoSendReactionAsync(string chatId, string messageId, string emoji, CancellationToken ct)
    {
        if (_client == null)
        {
            return SendResult.Failed("Channel not initialized");
        }
        
        try
        {
            if (!ulong.TryParse(chatId, out var channelId) ||
                !ulong.TryParse(messageId, out var msgId))
            {
                return SendResult.Failed("Invalid channel or message ID");
            }
            
            var channel = await _client.GetChannelAsync(channelId);
            
            if (channel is not IMessageChannel messageChannel)
            {
                return SendResult.Failed("Channel is not a message channel");
            }
            
            var msg = await messageChannel.GetMessageAsync(msgId);
            if (msg is not IUserMessage userMessage)
            {
                return SendResult.Failed("Message not found");
            }
            
            // Parse emoji - could be Unicode emoji or custom emote
            IEmote emote;
            if (Emote.TryParse(emoji, out var customEmote))
            {
                emote = customEmote;
            }
            else
            {
                emote = new Emoji(emoji);
            }
            
            await userMessage.AddReactionAsync(emote);
            return SendResult.Ok();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error adding Discord reaction");
            return SendResult.Failed(ex.Message);
        }
    }
    
    private Task HandleMessageReceived(SocketMessage message)
    {
        // Ignore bot's own messages
        if (message.Author.Id.ToString() == _botUserId)
        {
            return Task.CompletedTask;
        }
        
        // Ignore system messages
        if (message is not SocketUserMessage userMessage)
        {
            return Task.CompletedTask;
        }
        
        var incomingMessage = new IncomingMessage
        {
            MessageId = message.Id.ToString(),
            ChatId = message.Channel.Id.ToString(),
            SenderId = message.Author.Id.ToString(),
            SenderName = message.Author.Username,
            Text = message.Content,
            Timestamp = message.Timestamp.UtcDateTime,
            ChannelId = Id,
            Type = GetMessageType(message),
            MediaUrl = message.Attachments.FirstOrDefault()?.Url,
            ReplyToMessageId = message.Reference?.MessageId.ToString(),
            ThreadId = message.Channel is SocketThreadChannel thread ? thread.Id.ToString() : null,
            Metadata = new Dictionary<string, object>
            {
                ["guildId"] = (message.Channel as SocketGuildChannel)?.Guild.Id.ToString() ?? "",
                ["guildName"] = (message.Channel as SocketGuildChannel)?.Guild.Name ?? "",
                ["channelName"] = message.Channel.Name,
                ["isBot"] = message.Author.IsBot
            }
        };
        
        OnMessageReceived(incomingMessage);
        return Task.CompletedTask;
    }
    
    private Task HandleReactionAdded(
        Cacheable<IUserMessage, ulong> message,
        Cacheable<IMessageChannel, ulong> channel,
        SocketReaction reaction)
    {
        if (reaction.UserId.ToString() == _botUserId)
        {
            return Task.CompletedTask;
        }
        
        OnReactionReceived(new MessageReaction
        {
            MessageId = message.Id.ToString(),
            ChatId = channel.Id.ToString(),
            UserId = reaction.UserId.ToString(),
            UserName = reaction.User.IsSpecified ? reaction.User.Value.Username : "Unknown",
            Emoji = reaction.Emote.Name,
            ChannelId = Id,
            Timestamp = DateTime.UtcNow
        });
        
        return Task.CompletedTask;
    }
    
    private Task HandleReactionRemoved(
        Cacheable<IUserMessage, ulong> message,
        Cacheable<IMessageChannel, ulong> channel,
        SocketReaction reaction)
    {
        if (reaction.UserId.ToString() == _botUserId)
        {
            return Task.CompletedTask;
        }
        
        OnReactionReceived(new MessageReaction
        {
            MessageId = message.Id.ToString(),
            ChatId = channel.Id.ToString(),
            UserId = reaction.UserId.ToString(),
            UserName = reaction.User.IsSpecified ? reaction.User.Value.Username : "Unknown",
            Emoji = reaction.Emote.Name,
            ChannelId = Id,
            Timestamp = DateTime.UtcNow,
            IsRemoval = true
        });
        
        return Task.CompletedTask;
    }
    
    private Task HandleReady()
    {
        if (_client?.CurrentUser != null)
        {
            _botUserId = _client.CurrentUser.Id.ToString();
            _botUserName = _client.CurrentUser.Username;
            
            Logger.LogInformation(
                "Discord bot ready: {Username}#{Discriminator} ({Id})",
                _client.CurrentUser.Username,
                _client.CurrentUser.Discriminator,
                _client.CurrentUser.Id);
        }
        
        return Task.CompletedTask;
    }
    
    private Task HandleDisconnected(Exception exception)
    {
        Logger.LogWarning(exception, "Discord client disconnected");
        SetError($"Disconnected: {exception.Message}");
        OnEventReceived(ChannelEvent.Disconnected(Id, exception.Message));
        return Task.CompletedTask;
    }
    
    private Task HandleLog(LogMessage log)
    {
        var level = log.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            LogSeverity.Debug => LogLevel.Trace,
            _ => LogLevel.Information
        };
        
        Logger.Log(level, log.Exception, "[Discord] {Source}: {Message}", log.Source, log.Message);
        return Task.CompletedTask;
    }
    
    private static MessageType GetMessageType(SocketMessage message)
    {
        var attachment = message.Attachments.FirstOrDefault();
        if (attachment == null)
        {
            return MessageType.Text;
        }
        
        var contentType = attachment.ContentType?.ToLowerInvariant() ?? "";
        
        if (contentType.StartsWith("image/"))
            return MessageType.Image;
        if (contentType.StartsWith("video/"))
            return MessageType.Video;
        if (contentType.StartsWith("audio/"))
            return MessageType.Audio;
        
        return MessageType.Document;
    }
}
