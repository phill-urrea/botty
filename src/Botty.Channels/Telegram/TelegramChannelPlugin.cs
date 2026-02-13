using Botty.Channels.Base;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Botty.Channels.Telegram;

/// <summary>
/// Telegram channel plugin using Telegram.Bot SDK
/// </summary>
public class TelegramChannelPlugin : BaseChannelPlugin
{
    private readonly TelegramOptions _options;
    private TelegramBotClient? _client;
    private CancellationTokenSource? _receivingCts;
    private User? _botUser;
    
    public override string Id => "telegram";
    public override string Label => "Telegram";
    public override string Description => "Telegram Bot API messaging";
    
    public override ChannelCapabilities Capabilities => new()
    {
        SupportsMedia = true,
        SupportsThreads = true,
        SupportsReactions = true,
        SupportsEdits = true,
        SupportsDeletes = true,
        SupportsVoiceNotes = true,
        SupportsTypingIndicator = true,
        SupportsReadReceipts = false,
        MaxMessageLength = 4096,
        SupportedMediaTypes = [
            "image/jpeg", "image/png", "image/gif", "image/webp",
            "video/mp4",
            "audio/mpeg", "audio/ogg",
            "application/pdf"
        ]
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
                Description = "Telegram Bot API token from @BotFather",
                Type = ChannelConfigFieldType.Secret,
                IsSensitive = true,
                IsRequired = true
            },
            new ChannelConfigField
            {
                Key = "allowed_users",
                Label = "Allowed Users",
                Description = "Comma-separated list of allowed Telegram user IDs (empty = allow all)",
                Type = ChannelConfigFieldType.String
            },
            new ChannelConfigField
            {
                Key = "polling_timeout",
                Label = "Polling Timeout",
                Description = "Timeout for long polling in seconds",
                Type = ChannelConfigFieldType.Number,
                DefaultValue = "30"
            }
        ]
    };
    
    public TelegramChannelPlugin(
        IOptions<TelegramOptions> options,
        ILogger<TelegramChannelPlugin> logger)
        : base(logger)
    {
        _options = options.Value;
    }
    
    protected override async Task DoInitializeAsync(ChannelConfig config, CancellationToken ct)
    {
        var botToken = await config.GetRequiredSecretAsync("bot_token", ct);
        
        _client = new TelegramBotClient(botToken);
        
        // Get bot info
        _botUser = await _client.GetMe(ct);
        Logger.LogInformation(
            "Telegram bot connected: @{Username} (ID: {Id})",
            _botUser.Username, _botUser.Id);
        
        // Start receiving updates
        _receivingCts = new CancellationTokenSource();
        
        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [
                UpdateType.Message,
                UpdateType.EditedMessage,
                UpdateType.CallbackQuery,
                UpdateType.MessageReaction
            ],
            DropPendingUpdates = _options.DropPendingUpdates
        };
        
        _client.StartReceiving(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandleErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: _receivingCts.Token);
    }
    
    protected override Task DoDisconnectAsync(CancellationToken ct)
    {
        _receivingCts?.Cancel();
        _receivingCts?.Dispose();
        _receivingCts = null;
        _client = null;
        _botUser = null;
        
        return Task.CompletedTask;
    }
    
    protected override string? GetAccountId() => _botUser?.Id.ToString();
    protected override string? GetAccountName() => _botUser?.Username;
    
    public override async Task<SendResult> SendTextAsync(OutboundMessage message, CancellationToken ct = default)
    {
        if (_client == null)
        {
            return SendResult.Failed("Channel not initialized");
        }
        
        try
        {
            var chatId = long.Parse(message.ChatId);
            int? replyToMessageId = message.ReplyToMessageId != null 
                ? int.Parse(message.ReplyToMessageId) 
                : null;
            int? messageThreadId = message.ThreadId != null 
                ? int.Parse(message.ThreadId) 
                : null;
            
            var sentMessage = await _client.SendMessage(
                chatId: chatId,
                text: message.Text,
                replyParameters: replyToMessageId.HasValue 
                    ? new ReplyParameters { MessageId = replyToMessageId.Value } 
                    : null,
                messageThreadId: messageThreadId,
                cancellationToken: ct);
            
            return SendResult.Ok(sentMessage.MessageId.ToString());
        }
        catch (ApiRequestException ex)
        {
            Logger.LogError(ex, "Telegram API error sending message to {ChatId}", message.ChatId);
            return SendResult.Failed($"Telegram API error: {ex.Message}");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error sending Telegram message to {ChatId}", message.ChatId);
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
            var chatId = long.Parse(message.ChatId);
            var inputFile = InputFile.FromStream(message.MediaStream, message.FileName);
            
            Message sentMessage;
            
            if (message.MediaType.StartsWith("image/"))
            {
                sentMessage = await _client.SendPhoto(
                    chatId: chatId,
                    photo: inputFile,
                    caption: message.Caption,
                    cancellationToken: ct);
            }
            else if (message.MediaType.StartsWith("video/"))
            {
                sentMessage = await _client.SendVideo(
                    chatId: chatId,
                    video: inputFile,
                    caption: message.Caption,
                    cancellationToken: ct);
            }
            else if (message.MediaType.StartsWith("audio/"))
            {
                sentMessage = await _client.SendAudio(
                    chatId: chatId,
                    audio: inputFile,
                    caption: message.Caption,
                    cancellationToken: ct);
            }
            else
            {
                sentMessage = await _client.SendDocument(
                    chatId: chatId,
                    document: inputFile,
                    caption: message.Caption,
                    cancellationToken: ct);
            }
            
            return SendResult.Ok(sentMessage.MessageId.ToString());
        }
        catch (ApiRequestException ex)
        {
            Logger.LogError(ex, "Telegram API error sending media to {ChatId}", message.ChatId);
            return SendResult.Failed($"Telegram API error: {ex.Message}");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error sending Telegram media to {ChatId}", message.ChatId);
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
            await _client.SetMessageReaction(
                chatId: long.Parse(chatId),
                messageId: int.Parse(messageId),
                reaction: [new ReactionTypeEmoji { Emoji = emoji }],
                cancellationToken: ct);
            
            return SendResult.Ok();
        }
        catch (ApiRequestException ex)
        {
            Logger.LogError(ex, "Telegram API error setting reaction");
            return SendResult.Failed($"Telegram API error: {ex.Message}");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error setting Telegram reaction");
            return SendResult.Failed(ex.Message);
        }
    }
    
    private Task HandleUpdateAsync(ITelegramBotClient client, Update update, CancellationToken ct)
    {
        try
        {
            switch (update.Type)
            {
                case UpdateType.Message:
                    HandleMessage(update.Message!);
                    break;
                case UpdateType.EditedMessage:
                    HandleEditedMessage(update.EditedMessage!);
                    break;
                case UpdateType.MessageReaction:
                    HandleReaction(update.MessageReaction!);
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error handling Telegram update");
        }
        
        return Task.CompletedTask;
    }
    
    private void HandleMessage(Message message)
    {
        var incomingMessage = new IncomingMessage
        {
            MessageId = message.MessageId.ToString(),
            ChatId = message.Chat.Id.ToString(),
            SenderId = message.From?.Id.ToString() ?? "unknown",
            SenderName = GetSenderName(message.From),
            Text = message.Text ?? message.Caption ?? string.Empty,
            Timestamp = message.Date,
            ChannelId = Id,
            Type = MapMessageType(message),
            MediaUrl = GetMediaUrl(message),
            ReplyToMessageId = message.ReplyToMessage?.MessageId.ToString(),
            ThreadId = message.MessageThreadId?.ToString(),
            Metadata = new Dictionary<string, object>
            {
                ["chatType"] = message.Chat.Type.ToString(),
                ["chatTitle"] = message.Chat.Title ?? string.Empty,
                ["forwardFrom"] = message.ForwardFrom?.Username ?? string.Empty
            }
        };
        
        OnMessageReceived(incomingMessage);
    }
    
    private void HandleEditedMessage(Message message)
    {
        OnEventReceived(new ChannelEvent
        {
            Type = ChannelEventType.MessageEdited,
            ChannelId = Id,
            Timestamp = DateTime.UtcNow,
            ChatId = message.Chat.Id.ToString(),
            Metadata = new Dictionary<string, object>
            {
                ["messageId"] = message.MessageId,
                ["newText"] = message.Text ?? string.Empty
            }
        });
    }
    
    private void HandleReaction(MessageReactionUpdated reaction)
    {
        foreach (var newReaction in reaction.NewReaction)
        {
            if (newReaction is ReactionTypeEmoji emoji)
            {
                OnReactionReceived(new MessageReaction
                {
                    MessageId = reaction.MessageId.ToString(),
                    ChatId = reaction.Chat.Id.ToString(),
                    UserId = reaction.User?.Id.ToString() ?? "unknown",
                    UserName = GetSenderName(reaction.User),
                    Emoji = emoji.Emoji,
                    ChannelId = Id,
                    Timestamp = DateTime.UtcNow
                });
            }
        }
    }
    
    private Task HandleErrorAsync(ITelegramBotClient client, Exception exception, HandleErrorSource source, CancellationToken ct)
    {
        var errorMessage = exception switch
        {
            ApiRequestException apiEx => $"Telegram API Error: [{apiEx.ErrorCode}] {apiEx.Message}",
            _ => exception.Message
        };
        
        Logger.LogError(exception, "Telegram error from {Source}: {Message}", source, errorMessage);
        SetError(errorMessage);
        
        return Task.CompletedTask;
    }
    
    private static string GetSenderName(User? user)
    {
        if (user == null) return "Unknown";
        
        var name = user.FirstName;
        if (!string.IsNullOrEmpty(user.LastName))
        {
            name += $" {user.LastName}";
        }
        return name;
    }
    
    private static MessageType MapMessageType(Message message)
    {
        if (message.Photo != null) return MessageType.Image;
        if (message.Video != null) return MessageType.Video;
        if (message.Audio != null) return MessageType.Audio;
        if (message.Voice != null) return MessageType.Voice;
        if (message.Document != null) return MessageType.Document;
        if (message.Location != null) return MessageType.Location;
        if (message.Contact != null) return MessageType.Contact;
        if (message.Sticker != null) return MessageType.Sticker;
        return MessageType.Text;
    }
    
    private static string? GetMediaUrl(Message message)
    {
        // Telegram doesn't provide direct URLs - files must be downloaded via API
        // Return the file_id which can be used to download the file
        var fileId = message.Photo?.LastOrDefault()?.FileId
            ?? message.Video?.FileId
            ?? message.Audio?.FileId
            ?? message.Voice?.FileId
            ?? message.Document?.FileId
            ?? message.Sticker?.FileId;
        
        return fileId != null ? $"telegram://file/{fileId}" : null;
    }
}
