using System.Text.Json.Serialization;
using Botty.Channels;
using Botty.Core.Enums;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Botty.Tools.Base;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Botty.Tools.Channels;

/// <summary>
/// Exposes channel conversation discovery and outbound messaging tools.
/// </summary>
public class ChannelMessagingTool : BaseTool
{
    private const int DefaultRecentSenderHoursBack = 72;
    private const int DefaultLimit = 50;
    private readonly IChannelRegistry _channelRegistry;
    private readonly IServiceScopeFactory _scopeFactory;

    public ChannelMessagingTool(
        IChannelRegistry channelRegistry,
        IServiceScopeFactory scopeFactory,
        ILogger<ChannelMessagingTool> logger)
        : base(logger)
    {
        _channelRegistry = channelRegistry;
        _scopeFactory = scopeFactory;
    }

    public override string Id => "channel_messaging";
    public override string Name => "Channel Messaging";
    public override string Description => "Discover channel conversations/senders and send channel messages.";
    public override ToolConfigSchema ConfigSchema => new()
    {
        ToolId = Id,
        Fields = []
    };

    public override IEnumerable<LlmTool> GetTools()
    {
        return
        [
            new LlmTool
            {
                Name = "channel_list_conversations",
                Description = "List known conversations for a channel, including chat IDs for follow-up sends.",
                ParametersSchema = """
                {
                  "type": "object",
                  "properties": {
                    "channel_id": { "type": "string", "description": "Channel to filter by. Defaults to 'whatsapp'." },
                    "limit": { "type": "integer", "description": "Maximum number of conversations to return (default 50)." }
                  }
                }
                """
            },
            new LlmTool
            {
                Name = "channel_list_recent_senders",
                Description = "List recently active senders with channel/chat targets so you can quickly reply.",
                ParametersSchema = """
                {
                  "type": "object",
                  "properties": {
                    "channel_id": { "type": "string", "description": "Channel to filter by. Defaults to 'whatsapp'." },
                    "hours_back": { "type": "integer", "description": "How far back to look for sender activity (default 72)." },
                    "limit": { "type": "integer", "description": "Maximum number of senders to return (default 50)." }
                  }
                }
                """
            },
            new LlmTool
            {
                Name = "channel_send_message",
                Description = "Send a message through a channel plugin to a specific chat ID.",
                ParametersSchema = """
                {
                  "type": "object",
                  "properties": {
                    "channel_id": { "type": "string", "description": "Target channel id (e.g. 'whatsapp'). Defaults to 'whatsapp'." },
                    "chat_id": { "type": "string", "description": "Destination channel chat id." },
                    "message": { "type": "string", "description": "Text message body to send." },
                    "reply_to_message_id": { "type": "string", "description": "Optional message id to reply to." }
                  },
                  "required": ["chat_id", "message"]
                }
                """
            }
        ];
    }

    protected override async Task<ToolResult> OnExecuteAsync(ToolContext context, CancellationToken ct)
    {
        return context.ToolName switch
        {
            "channel_list_conversations" => await ListConversationsAsync(context.Arguments, ct),
            "channel_list_recent_senders" => await ListRecentSendersAsync(context.Arguments, ct),
            "channel_send_message" => await SendMessageAsync(context.Arguments, ct),
            _ => ToolResult.Fail($"Unknown tool: {context.ToolName}")
        };
    }

    private async Task<ToolResult> ListConversationsAsync(string arguments, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var conversationRepository = scope.ServiceProvider.GetRequiredService<IConversationRepository>();

        var args = ParseArguments<ListConversationsArgs>(arguments) ?? new ListConversationsArgs();
        var channelId = NormalizeChannelId(args.ChannelId);
        var limit = NormalizeLimit(args.Limit);

        var conversations = await conversationRepository.ListConversationsAsync(ct);
        var results = conversations
            .Where(c => string.Equals(c.Source, channelId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.UpdatedAt)
            .Take(limit)
            .Select(c => new
            {
                channelId = c.Source,
                chatId = c.ExternalId,
                title = c.Title,
                type = c.ExternalId?.EndsWith("@g.us", StringComparison.OrdinalIgnoreCase) == true ? "group" : "direct",
                lastSeen = c.UpdatedAt.ToString("o")
            })
            .ToList();

        return ToolResult.Ok(ToJson(new
        {
            channelId,
            count = results.Count,
            conversations = results
        }));
    }

    private async Task<ToolResult> ListRecentSendersAsync(string arguments, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var conversationRepository = scope.ServiceProvider.GetRequiredService<IConversationRepository>();

        var args = ParseArguments<ListRecentSendersArgs>(arguments) ?? new ListRecentSendersArgs();
        var channelId = NormalizeChannelId(args.ChannelId);
        var hoursBack = args.HoursBack.GetValueOrDefault(DefaultRecentSenderHoursBack);
        if (hoursBack <= 0)
        {
            hoursBack = DefaultRecentSenderHoursBack;
        }

        var limit = NormalizeLimit(args.Limit);
        var since = DateTime.UtcNow.AddHours(-hoursBack);

        var feed = await conversationRepository.GetMergedFeedAsync(since, limit * 20, ct);
        var recentSenders = feed
            .Where(m =>
                m.Conversation != null &&
                string.Equals(m.Conversation.Source, channelId, StringComparison.OrdinalIgnoreCase) &&
                m.Role != MessageRole.Assistant &&
                (!string.IsNullOrWhiteSpace(m.SenderId) || !string.IsNullOrWhiteSpace(m.SenderName)) &&
                !string.IsNullOrWhiteSpace(m.Conversation.ExternalId))
            .GroupBy(m => new
            {
                SenderId = m.SenderId ?? string.Empty,
                SenderName = m.SenderName ?? string.Empty,
                ChatId = m.Conversation!.ExternalId!
            })
            .Select(g =>
            {
                var last = g.OrderByDescending(m => m.CreatedAt).First();
                var displayName = !string.IsNullOrWhiteSpace(g.Key.SenderName)
                    ? g.Key.SenderName
                    : g.Key.SenderId;

                return new
                {
                    channelId = channelId,
                    chatId = g.Key.ChatId,
                    senderId = string.IsNullOrWhiteSpace(g.Key.SenderId) ? null : g.Key.SenderId,
                    senderName = string.IsNullOrWhiteSpace(g.Key.SenderName) ? null : g.Key.SenderName,
                    displayName,
                    lastSeen = last.CreatedAt.ToString("o")
                };
            })
            .OrderByDescending(x => x.lastSeen)
            .Take(limit)
            .ToList();

        return ToolResult.Ok(ToJson(new
        {
            channelId,
            count = recentSenders.Count,
            recipients = recentSenders
        }));
    }

    private async Task<ToolResult> SendMessageAsync(string arguments, CancellationToken ct)
    {
        var args = ParseArguments<SendMessageArgs>(arguments);
        if (args == null || string.IsNullOrWhiteSpace(args.ChatId) || string.IsNullOrWhiteSpace(args.Message))
        {
            return ToolResult.Fail("Missing required parameters: 'chat_id' and 'message'.");
        }

        var channelId = NormalizeChannelId(args.ChannelId);
        var channel = _channelRegistry.GetChannel(channelId);
        if (channel == null)
        {
            return ToolResult.Fail($"Unknown channel '{channelId}'.");
        }

        var outbound = new OutboundMessage(args.ChatId.Trim(), args.Message.Trim(), args.ReplyToMessageId);
        var result = await _channelRegistry.SendToChannelAsync(channelId, outbound, ct);
        if (!result.Success)
        {
            return ToolResult.Fail($"Failed to send message: {result.Error ?? "unknown error"}");
        }

        return ToolResult.Ok(ToJson(new
        {
            status = "sent",
            channelId,
            chatId = args.ChatId.Trim(),
            messageId = result.MessageId
        }));
    }

    private static string NormalizeChannelId(string? channelId)
    {
        return string.IsNullOrWhiteSpace(channelId) ? "whatsapp" : channelId.Trim().ToLowerInvariant();
    }

    private static int NormalizeLimit(int? limit)
    {
        var value = limit.GetValueOrDefault(DefaultLimit);
        return Math.Clamp(value <= 0 ? DefaultLimit : value, 1, 200);
    }

    private sealed class ListConversationsArgs
    {
        [JsonPropertyName("channel_id")]
        public string? ChannelId { get; set; }

        [JsonPropertyName("limit")]
        public int? Limit { get; set; }
    }

    private sealed class ListRecentSendersArgs
    {
        [JsonPropertyName("channel_id")]
        public string? ChannelId { get; set; }

        [JsonPropertyName("hours_back")]
        public int? HoursBack { get; set; }

        [JsonPropertyName("limit")]
        public int? Limit { get; set; }
    }

    private sealed class SendMessageArgs
    {
        [JsonPropertyName("channel_id")]
        public string? ChannelId { get; set; }

        [JsonPropertyName("chat_id")]
        public string? ChatId { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("reply_to_message_id")]
        public string? ReplyToMessageId { get; set; }
    }
}
