using System.Text.Json;
using Botty.Api.Controllers;
using Botty.Channels;
using Botty.Core.Enums;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Botty.Core.Services;
using Botty.LLM.Services;

namespace Botty.Api.Services;

/// <summary>
/// Subscribes to incoming channel messages and automatically generates LLM responses.
/// Only auto-replies in WhatsApp self-chat; all messages are still stored in the feed by the bridge.
/// </summary>
public class ChannelAutoReplyService : BackgroundService
{
    private const int MaxToolLoopIterations = 5;
    private const int MaxConversationContextMessages = 50;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly IChannelRegistry _registry;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFeedBroadcastService _feedBroadcast;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ChannelAutoReplyService> _logger;

    public ChannelAutoReplyService(
        IChannelRegistry registry,
        IServiceScopeFactory scopeFactory,
        IFeedBroadcastService feedBroadcast,
        IConfiguration configuration,
        ILogger<ChannelAutoReplyService> logger)
    {
        _registry = registry;
        _scopeFactory = scopeFactory;
        _feedBroadcast = feedBroadcast;
        _configuration = configuration;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _registry.MessageReceived += OnMessageReceived;
        return Task.CompletedTask;
    }

    public override void Dispose()
    {
        _registry.MessageReceived -= OnMessageReceived;
        base.Dispose();
    }

    private void OnMessageReceived(object? sender, ChannelMessageEventArgs e)
    {
        // Admin UI messages are handled by ChatController
        if (string.Equals(e.ChannelId, "admin", StringComparison.OrdinalIgnoreCase))
            return;

        // Only auto-reply in WhatsApp self-chat (messaging yourself)
        // Feed storage for all messages is handled separately by the bridge → POST /api/feed/incoming
        if (string.Equals(e.ChannelId, "whatsapp", StringComparison.OrdinalIgnoreCase))
        {
            var status = _registry.GetChannel("whatsapp")?.GetStatusAsync().GetAwaiter().GetResult();
            if (status is { IsConnected: true, AccountId: not null })
            {
                var selfChatId = $"{status.AccountId}@c.us";
                if (!string.Equals(e.Message.ChatId, selfChatId, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            else
            {
                // Can't determine self-chat — skip auto-reply
                return;
            }
        }

        _ = HandleMessageAsync(e.ChannelId, e.Message).ContinueWith(t =>
        {
            if (t.Exception != null)
                _logger.LogError(t.Exception, "Error handling auto-reply for channel={ChannelId} chat={ChatId}",
                    e.ChannelId, e.Message.ChatId);
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private async Task HandleMessageAsync(string channelId, IncomingMessage message)
    {
        using var scope = _scopeFactory.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<IConversationOrchestrator>();
        var conversationRepo = scope.ServiceProvider.GetRequiredService<IConversationRepository>();
        var toolRegistry = scope.ServiceProvider.GetRequiredService<IToolRegistry>();
        var approvalService = scope.ServiceProvider.GetRequiredService<IApprovalService>();

        // Determine self-chat ID for WhatsApp (used to provide cross-conversation tools)
        string? selfChatId = null;
        if (string.Equals(channelId, "whatsapp", StringComparison.OrdinalIgnoreCase))
        {
            var status = await _registry.GetStatusAsync("whatsapp");
            if (status is { IsConnected: true, AccountId: not null })
                selfChatId = $"{status.AccountId}@c.us";
        }

        // Get or create conversation (the user message is already persisted by the feed/incoming endpoint)
        var conversation = await conversationRepo.GetOrCreateAsync(
            channelId,
            message.ChatId,
            userId: Guid.Empty,
            title: !string.IsNullOrEmpty(message.SenderName) ? $"{channelId}: {message.SenderName}" : null);

        // Load conversation history (user message already stored by bridge → POST /api/feed/incoming)
        var historyMessages = await conversationRepo.GetMessagesAsync(conversation.Id, MaxConversationContextMessages);
        var fullMessages = historyMessages.Select(m => new ChatMessage
        {
            Role = m.Role switch
            {
                MessageRole.Assistant => "assistant",
                MessageRole.System => "system",
                _ => "user"
            },
            Content = m.SenderName != null && m.Role != MessageRole.Assistant
                ? $"[{m.SenderName}]: {m.Content}"
                : m.Content
        }).ToList();

        var tools = toolRegistry.GetAll().SelectMany(s => s.GetTools()).ToList();

        // In self-chat, append cross-conversation tools
        if (selfChatId != null)
            tools.AddRange(GetSelfChatTools());

        var request = new ConversationRequest
        {
            Messages = fullMessages,
            ConversationId = conversation.Id.ToString(),
            IncludeMemory = true,
            ExtractMemories = true,
            AvailableTools = tools
        };

        // Broadcast typing indicator to admin UI
        _feedBroadcast.BroadcastTypingIndicator(new TypingIndicatorDto
        {
            ConversationId = conversation.Id,
            Source = conversation.Source,
            IsTyping = true,
            Timestamp = DateTime.UtcNow
        });

        // Send typing indicator to the channel (e.g. WhatsApp)
        var plugin = _registry.GetChannel(channelId);
        if (plugin != null)
        {
            try { await plugin.SendTypingIndicatorAsync(message.ChatId); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to send typing indicator to {ChannelId}", channelId); }
        }

        ConversationResponse response;
        try
        {
            response = await RunToolLoopAsync(
                orchestrator,
                toolRegistry,
                conversationRepo,
                approvalService,
                selfChatId,
                request,
                conversation.Id,
                conversation.UserId,
                conversation.Source,
                conversation.ExternalId);
        }
        finally
        {
            _feedBroadcast.BroadcastTypingIndicator(new TypingIndicatorDto
            {
                ConversationId = conversation.Id,
                Source = conversation.Source,
                IsTyping = false,
                Timestamp = DateTime.UtcNow
            });
        }

        // Persist assistant response and broadcast to feed
        var assistantMessage = await conversationRepo.AppendMessageAsync(
            conversation.Id,
            MessageRole.Assistant,
            response.Content,
            senderName: null,
            externalId: null,
            senderId: null);
        _feedBroadcast.BroadcastNewMessage(new FeedMessageDto
        {
            Id = assistantMessage.Id,
            ConversationId = conversation.Id,
            Source = conversation.Source,
            ExternalId = conversation.ExternalId,
            Role = "assistant",
            Content = assistantMessage.Content,
            SenderId = assistantMessage.SenderId,
            SenderName = assistantMessage.SenderName,
            CreatedAt = assistantMessage.CreatedAt
        });

        // Send reply back through the channel
        var outbound = new OutboundMessage(
            ChatId: message.ChatId,
            Text: response.Content,
            ReplyToMessageId: message.MessageId);

        var result = await _registry.SendToChannelAsync(channelId, outbound);

        // If channel is not connected, try lazy initialization and retry once
        if (!result.Success && result.Error?.Contains("not connected") == true)
        {
            _logger.LogInformation("Channel {ChannelId} not connected, attempting lazy initialization...", channelId);
            try
            {
                await _registry.InitializeChannelAsync(channelId);
                result = await _registry.SendToChannelAsync(channelId, outbound);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lazy initialization of channel {ChannelId} failed", channelId);
            }
        }

        if (!result.Success)
            _logger.LogWarning("Failed to send auto-reply to {ChannelId}/{ChatId}: {Error}",
                channelId, message.ChatId, result.Error);
    }

    // ── Tool definitions for self-chat ────────────────────────────────────

    private static List<LlmTool> GetSelfChatTools() =>
    [
        new LlmTool
        {
            Name = "get_recent_messages",
            Description = "Fetch recent messages from other WhatsApp conversations (groups and DMs) for summarization. Does not include self-chat messages.",
            ParametersSchema = """
            {
                "type": "object",
                "properties": {
                    "hours_back": { "type": "integer", "description": "How many hours back to look (default 24)", "default": 24 },
                    "conversation_filter": { "type": "string", "enum": ["groups", "direct", "all"], "description": "Filter by conversation type (default all)", "default": "all" },
                    "limit": { "type": "integer", "description": "Maximum number of messages to return (default 100)", "default": 100 }
                }
            }
            """
        },
        new LlmTool
        {
            Name = "list_conversations",
            Description = "List all WhatsApp conversations (groups and DMs) with their chat IDs, titles, and last activity. Excludes self-chat. Useful for finding chat IDs before sending messages.",
            ParametersSchema = """
            {
                "type": "object",
                "properties": {}
            }
            """
        },
    ];

    // ── Custom tool execution ─────────────────────────────────────────────

    private async Task<ToolResult?> TryExecuteCustomToolAsync(
        string toolName, string arguments, IConversationRepository conversationRepo, string? selfChatId)
    {
        return toolName switch
        {
            "get_recent_messages" => await ExecuteGetRecentMessages(arguments, conversationRepo, selfChatId),
            "list_conversations" => await ExecuteListConversations(conversationRepo, selfChatId),
            _ => null
        };
    }

    private async Task<ToolResult> ExecuteGetRecentMessages(
        string arguments, IConversationRepository conversationRepo, string? selfChatId)
    {
        var args = JsonSerializer.Deserialize<GetRecentMessagesArgs>(arguments, JsonOptions) ?? new();
        var hoursBack = args.HoursBack > 0 ? args.HoursBack : 24;
        var limit = args.Limit > 0 ? args.Limit : 100;
        var filter = args.ConversationFilter ?? "all";

        var since = DateTime.UtcNow.AddHours(-hoursBack);
        var messages = await conversationRepo.GetMergedFeedAsync(since, limit);

        var filtered = messages.Where(m =>
        {
            // Only WhatsApp messages
            if (m.Conversation is null || !string.Equals(m.Conversation.Source, "whatsapp", StringComparison.OrdinalIgnoreCase))
                return false;
            // Exclude self-chat
            if (selfChatId != null && string.Equals(m.Conversation.ExternalId, selfChatId, StringComparison.OrdinalIgnoreCase))
                return false;
            // Exclude assistant messages (our own replies)
            if (m.Role == MessageRole.Assistant)
                return false;
            // Conversation type filter
            if (filter == "groups" && m.Conversation.ExternalId?.EndsWith("@g.us") != true)
                return false;
            if (filter == "direct" && m.Conversation.ExternalId?.EndsWith("@c.us") != true)
                return false;
            return true;
        });

        var results = filtered.Select(m => new
        {
            conversation = m.Conversation?.Title ?? m.Conversation?.ExternalId ?? "unknown",
            chat_id = m.Conversation?.ExternalId,
            sender = m.SenderName ?? "unknown",
            content = m.Content,
            timestamp = m.CreatedAt.ToString("o")
        });

        return ToolResult.Ok(JsonSerializer.Serialize(new { count = results.Count(), messages = results }, JsonOptions));
    }

    private async Task<ToolResult> ExecuteListConversations(
        IConversationRepository conversationRepo, string? selfChatId)
    {
        var conversations = await conversationRepo.ListConversationsAsync();

        var filtered = conversations.Where(c =>
        {
            if (!string.Equals(c.Source, "whatsapp", StringComparison.OrdinalIgnoreCase))
                return false;
            if (selfChatId != null && string.Equals(c.ExternalId, selfChatId, StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        });

        var results = filtered.Select(c => new
        {
            chat_id = c.ExternalId,
            title = c.Title,
            type = c.ExternalId?.EndsWith("@g.us") == true ? "group" : "direct",
            last_activity = c.UpdatedAt.ToString("o")
        });

        return ToolResult.Ok(JsonSerializer.Serialize(new { count = results.Count(), conversations = results }, JsonOptions));
    }

    // ── Tool loop ─────────────────────────────────────────────────────────

    private async Task<ConversationResponse> RunToolLoopAsync(
        IConversationOrchestrator orchestrator,
        IToolRegistry toolRegistry,
        IConversationRepository conversationRepo,
        IApprovalService approvalService,
        string? selfChatId,
        ConversationRequest request,
        Guid conversationId,
        Guid userId,
        string source,
        string? externalId)
    {
        var response = await orchestrator.ChatAsync(request);
        var iterations = 0;

        while (response.ToolCalls is { Count: > 0 } toolCalls && iterations < MaxToolLoopIterations)
        {
            iterations++;

            _feedBroadcast.BroadcastTypingIndicator(new TypingIndicatorDto
            {
                ConversationId = conversationId,
                Source = source,
                IsTyping = true,
                Timestamp = DateTime.UtcNow
            });

            var toolResults = new List<LlmToolResult>();
            foreach (var tc in toolCalls)
            {
                if (ToolApprovalPolicy.TryGetApprovalTaskType(tc.Name, out var taskType))
                {
                    var approvalTask = await CreateApprovalTaskForToolCallAsync(
                        tc,
                        taskType,
                        conversationId,
                        userId,
                        source,
                        externalId,
                        approvalService,
                        CancellationToken.None);
                    toolResults.Add(new LlmToolResult
                    {
                        ToolUseId = tc.Id,
                        Content = JsonSerializer.Serialize(new
                        {
                            status = "pending_approval",
                            taskId = approvalTask.Id,
                            toolName = tc.Name,
                            message = $"Execution of tool '{tc.Name}' is pending explicit approval."
                        }, JsonOptions)
                    });
                    continue;
                }

                // Try custom self-chat tools first, then fall back to tool registry
                var toolResult = await TryExecuteCustomToolAsync(tc.Name, tc.Arguments ?? "{}", conversationRepo, selfChatId)
                    ?? await toolRegistry.ExecuteToolAsync(tc.Name, tc.Arguments ?? "{}");

                if (!toolResult.Success)
                {
                    _logger.LogWarning("Tool {ToolName} failed in auto-reply. Error: {Error}",
                        tc.Name, toolResult.Error ?? "(none)");
                }
                var content = toolResult.Success ? (toolResult.Result ?? string.Empty) : $"Error: {toolResult.Error}";
                toolResults.Add(new LlmToolResult { ToolUseId = tc.Id, Content = content });
            }

            request.Messages.Add(new ChatMessage
            {
                Role = "assistant",
                Content = response.Content,
                ToolCalls = toolCalls
            });
            request.Messages.Add(new ChatMessage
            {
                Role = "user",
                Content = string.Empty,
                ToolResults = toolResults
            });

            response = await orchestrator.ChatAsync(request);
        }

        return response;
    }

    private async Task<KanbanTask> CreateApprovalTaskForToolCallAsync(
        LlmToolCall toolCall,
        TaskType taskType,
        Guid conversationId,
        Guid userId,
        string source,
        string? externalId,
        IApprovalService approvalService,
        CancellationToken ct)
    {
        var preview = $"{toolCall.Name}({toolCall.Arguments ?? "{}"})";
        if (preview.Length > 500)
        {
            preview = preview[..500] + "...";
        }

        var request = new ApprovalRequest
        {
            Title = $"Approve tool call: {toolCall.Name}",
            Description = "The assistant requested to execute a side-effecting tool call.",
            Type = taskType,
            Priority = TaskPriority.Normal,
            Action = new PendingAction
            {
                ActionType = "ExecuteSkillTool",
                Description = $"Execute tool '{toolCall.Name}' after approval.",
                Payload = new Dictionary<string, string>
                {
                    ["toolName"] = toolCall.Name,
                    ["arguments"] = toolCall.Arguments ?? "{}",
                    ["conversationId"] = conversationId.ToString(),
                    ["userId"] = userId.ToString(),
                    ["source"] = source
                },
                Preview = preview,
                RequiresApproval = true
            },
            Assignee = TaskAssignee.Assistant,
            ConversationId = conversationId,
            UserId = userId,
            Source = source,
            ExternalId = externalId
        };
        if (!string.IsNullOrWhiteSpace(externalId))
            request.Action.Payload!["externalId"] = externalId;

        return await approvalService.RequestApprovalAsync(request, ct);
    }

    // ── Argument DTOs ─────────────────────────────────────────────────────

    private sealed class GetRecentMessagesArgs
    {
        public int HoursBack { get; set; } = 24;
        public string? ConversationFilter { get; set; }
        public int Limit { get; set; } = 100;
    }

}
