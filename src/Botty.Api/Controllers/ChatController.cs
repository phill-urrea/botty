using System.Text;
using System.Text.Json;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Botty.Core.Enums;
using Botty.Core.Services;
using Botty.LLM.Services;
using Microsoft.AspNetCore.Mvc;
using Botty.Api.Services;

namespace Botty.Api.Controllers;

/// <summary>
/// API controller for chat/conversation endpoints.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private static readonly Guid FallbackAdminUserId = new("00000000-0000-0000-0000-000000000001");

    private const int MaxToolLoopIterations = 5;

    private const int MaxConversationContextMessages = 50;

    private readonly IConversationOrchestrator _orchestrator;
    private readonly IConversationRepository _conversationRepository;
    private readonly IFeedBroadcastService _feedBroadcast;
    private readonly IToolRegistry _toolRegistry;
    private readonly IApprovalService _approvalService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        IConversationOrchestrator orchestrator,
        IConversationRepository conversationRepository,
        IFeedBroadcastService feedBroadcast,
        IToolRegistry toolRegistry,
        IApprovalService approvalService,
        IConfiguration configuration,
        ILogger<ChatController> logger)
    {
        _orchestrator = orchestrator;
        _conversationRepository = conversationRepository;
        _feedBroadcast = feedBroadcast;
        _toolRegistry = toolRegistry;
        _approvalService = approvalService;
        _configuration = configuration;
        _logger = logger;
    }

    private Guid GetDefaultAdminUserId()
    {
        var value = _configuration["Conversation:DefaultAdminUserId"];
        return Guid.TryParse(value, out var id) ? id : FallbackAdminUserId;
    }

    private async Task<KanbanTask> CreateApprovalTaskForToolCallAsync(
        LlmToolCall toolCall,
        TaskType taskType,
        Guid conversationId,
        Guid userId,
        string source,
        string? externalId,
        CancellationToken ct)
    {
        var payload = new Dictionary<string, string>
        {
            ["toolName"] = toolCall.Name,
            ["arguments"] = toolCall.Arguments ?? "{}",
            ["conversationId"] = conversationId.ToString(),
            ["userId"] = userId.ToString(),
            ["source"] = source
        };
        if (!string.IsNullOrWhiteSpace(externalId))
            payload["externalId"] = externalId;

        var preview = $"{toolCall.Name}({(toolCall.Arguments ?? "{}")})";
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
            Assignee = TaskAssignee.Assistant,
            ConversationId = conversationId,
            UserId = userId,
            Source = source,
            ExternalId = externalId,
            Action = new PendingAction
            {
                ActionType = "ExecuteSkillTool",
                Description = $"Execute tool '{toolCall.Name}' after approval.",
                Payload = payload,
                Preview = preview,
                RequiresApproval = true
            }
        };

        return await _approvalService.RequestApprovalAsync(request, ct);
    }

    /// <summary>
    /// Streams via the orchestrator and runs the tool execution loop, broadcasting deltas over WebSocket.
    /// Returns the accumulated content, final usage, and finish reason.
    /// </summary>
    private async Task<(string Content, TokenUsage? Usage, string? FinishReason, List<LlmToolCall>? ToolCalls)> RunStreamingToolLoopAsync(
        ConversationRequest request,
        Guid conversationId,
        Guid userId,
        Guid messageId,
        string source,
        string? externalId,
        CancellationToken ct)
    {
        var totalContent = new StringBuilder();
        TokenUsage? lastUsage = null;
        string? lastFinishReason = null;
        var iterations = 0;

        while (iterations <= MaxToolLoopIterations)
        {
            var pendingToolCalls = new List<LlmToolCall>();
            var turnContent = new StringBuilder();

            await foreach (var delta in _orchestrator.StreamChatAsync(request, ct))
            {
                switch (delta.Type)
                {
                    case "text" when !string.IsNullOrEmpty(delta.Text):
                        turnContent.Append(delta.Text);
                        _feedBroadcast.BroadcastAssistantDelta(new AssistantDeltaDto
                        {
                            ConversationId = conversationId,
                            MessageId = messageId,
                            Delta = delta.Text
                        });
                        break;

                    case "tool_use" when delta.ToolCall != null:
                        pendingToolCalls.Add(delta.ToolCall);
                        break;

                    case "done":
                        lastUsage = delta.Usage;
                        lastFinishReason = delta.FinishReason;
                        break;
                }
            }

            totalContent.Append(turnContent);

            if (pendingToolCalls.Count == 0)
                break;

            // Execute tools
            iterations++;

            // Re-broadcast typing indicator during tool execution
            _feedBroadcast.BroadcastTypingIndicator(new TypingIndicatorDto
            {
                ConversationId = conversationId,
                Source = source,
                IsTyping = true,
                Timestamp = DateTime.UtcNow
            });

            var toolResults = new List<LlmToolResult>();
            var pendingApprovalTasks = new List<(string ToolName, Guid TaskId)>();
            foreach (var tc in pendingToolCalls)
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
                        ct);
                    var approvalPayload = JsonSerializer.Serialize(new
                    {
                        status = "pending_approval",
                        taskId = approvalTask.Id,
                        toolName = tc.Name,
                        message = $"Execution of tool '{tc.Name}' is pending explicit approval."
                    });

                    toolResults.Add(new LlmToolResult
                    {
                        ToolUseId = tc.Id,
                        Content = approvalPayload
                    });

                    _logger.LogInformation(
                        "Tool {ToolName} requires approval. Created approval task {TaskId}.",
                        tc.Name,
                        approvalTask.Id);

                    pendingApprovalTasks.Add((tc.Name, approvalTask.Id));
                    continue;
                }

                var toolResult = await _toolRegistry.ExecuteToolAsync(tc.Name, tc.Arguments ?? "{}", ct);
                if (!toolResult.Success)
                {
                    _logger.LogWarning("Tool {ToolName} failed. Arguments: {Arguments}. Error: {Error}",
                        tc.Name, tc.Arguments ?? "(null)", toolResult.Error ?? "(none)");
                }
                var content = toolResult.Success ? (toolResult.Result ?? string.Empty) : $"Error: {toolResult.Error}";
                toolResults.Add(new LlmToolResult { ToolUseId = tc.Id, Content = content });
            }

            if (pendingApprovalTasks.Count > 0)
            {
                var summary = BuildPendingApprovalMessage(pendingApprovalTasks);
                totalContent.Clear();
                totalContent.Append(summary);
                lastFinishReason = "approval_required";
                break;
            }

            // Append assistant + tool results to conversation for next turn
            request.Messages.Add(new ChatMessage
            {
                Role = "assistant",
                Content = turnContent.ToString(),
                ToolCalls = pendingToolCalls
            });
            request.Messages.Add(new ChatMessage
            {
                Role = "user",
                Content = string.Empty,
                ToolResults = toolResults
            });
        }

        return (totalContent.ToString(), lastUsage, lastFinishReason, null);
    }

    private static string BuildPendingApprovalMessage(IReadOnlyList<(string ToolName, Guid TaskId)> approvals)
    {
        if (approvals.Count == 1)
        {
            var item = approvals[0];
            return $"I prepared `{item.ToolName}` but have not executed it yet. Please approve task `{item.TaskId}` in Kanban to send it.";
        }

        var ids = string.Join(", ", approvals.Select(a => $"`{a.TaskId}`"));
        return $"I prepared {approvals.Count} actions but have not executed them yet. Please approve these Kanban tasks: {ids}.";
    }

    /// <summary>
    /// Sends a message and gets a response. Streams deltas over WebSocket in real time.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ChatResponseDto>> Chat(
        [FromBody] ChatRequestDto request,
        CancellationToken ct)
    {
        if (request.Messages == null || request.Messages.Count == 0)
        {
            return BadRequest("Messages cannot be empty");
        }

        try
        {
            Guid? userId = null;
            if (!string.IsNullOrEmpty(request.UserId) && Guid.TryParse(request.UserId, out var parsedUserId))
            {
                userId = parsedUserId;
            }
            var effectiveUserId = userId ?? GetDefaultAdminUserId();

            // Resolve conversation: by id if client sent one, else get/create admin default
            Conversation conversation;
            if (!string.IsNullOrWhiteSpace(request.ConversationId) && Guid.TryParse(request.ConversationId, out var requestedConvId))
            {
                var existing = await _conversationRepository.GetByIdAsync(requestedConvId, ct);
                conversation = existing ?? await _conversationRepository.GetOrCreateAsync("admin", "default", effectiveUserId, "Admin", ct);
            }
            else
            {
                conversation = await _conversationRepository.GetOrCreateAsync("admin", "default", effectiveUserId, "Admin", ct);
            }

            // Load conversation history and prepend to this request so the LLM has full context
            var historyMessages = await _conversationRepository.GetMessagesAsync(conversation.Id, MaxConversationContextMessages, ct);
            var historyChatMessages = historyMessages.Select(m => new ChatMessage
            {
                Role = m.Role.ToString().ToLowerInvariant(),
                Content = m.Content
            }).ToList();
            var clientChatMessages = request.Messages!.Select(m => new ChatMessage { Role = m.Role, Content = m.Content }).ToList();
            var fullMessages = historyChatMessages.Concat(clientChatMessages).ToList();

            var conversationRequest = new ConversationRequest
            {
                Messages = fullMessages,
                ConversationId = conversation.Id.ToString(),
                UserId = effectiveUserId,
                IncludeMemory = request.IncludeMemory ?? true,
                ExtractMemories = request.ExtractMemories ?? true,
                AdditionalContext = request.AdditionalContext,
                AvailableTools = _toolRegistry.GetAll().SelectMany(s => s.GetTools()).ToList(),
                Parameters = request.Parameters != null ? new LlmParameters
                {
                    Model = request.Parameters.Model,
                    MaxTokens = request.Parameters.MaxTokens ?? 4096,
                    Temperature = request.Parameters.Temperature ?? 0.7f
                } : null
            };

            // Persist new user message(s) to conversation and broadcast
            foreach (var m in request.Messages!)
            {
                var role = string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase) ? MessageRole.User : MessageRole.System;
                var persisted = await _conversationRepository.AppendMessageAsync(
                    conversation.Id,
                    role,
                    m.Content,
                    senderName: null,
                    externalId: null,
                    senderId: null,
                    ct: ct);
                _feedBroadcast.BroadcastNewMessage(new FeedMessageDto
                {
                    Id = persisted.Id,
                    ConversationId = conversation.Id,
                    Source = conversation.Source,
                    ExternalId = conversation.ExternalId,
                    Role = persisted.Role.ToString().ToLowerInvariant(),
                    Content = persisted.Content,
                    SenderId = persisted.SenderId,
                    SenderName = persisted.SenderName,
                    CreatedAt = persisted.CreatedAt
                });
            }

            _feedBroadcast.BroadcastTypingIndicator(new TypingIndicatorDto
            {
                ConversationId = conversation.Id,
                Source = conversation.Source,
                IsTyping = true,
                Timestamp = DateTime.UtcNow
            });

            // Create placeholder assistant message and broadcast it
            var assistantMessage = await _conversationRepository.AppendMessageAsync(
                conversation.Id,
                MessageRole.Assistant,
                string.Empty,
                senderName: null,
                externalId: null,
                senderId: null,
                ct: ct);
            _feedBroadcast.BroadcastNewMessage(new FeedMessageDto
            {
                Id = assistantMessage.Id,
                ConversationId = conversation.Id,
                Source = conversation.Source,
                ExternalId = conversation.ExternalId,
                Role = assistantMessage.Role.ToString().ToLowerInvariant(),
                Content = string.Empty,
                SenderId = assistantMessage.SenderId,
                SenderName = assistantMessage.SenderName,
                CreatedAt = assistantMessage.CreatedAt
            });

            string finalContent;
            TokenUsage? finalUsage;
            string? finishReason;
            try
            {
                var result = await RunStreamingToolLoopAsync(
                    conversationRequest,
                    conversation.Id,
                    effectiveUserId,
                    assistantMessage.Id,
                    conversation.Source,
                    conversation.ExternalId,
                    ct);
                finalContent = result.Content;
                finalUsage = result.Usage;
                finishReason = result.FinishReason;
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

            // Update persisted message with final content
            await _conversationRepository.UpdateMessageContentAsync(assistantMessage.Id, finalContent, ct);

            // Broadcast assistant_done
            _feedBroadcast.BroadcastAssistantDone(new AssistantDoneDto
            {
                ConversationId = conversation.Id,
                MessageId = assistantMessage.Id,
                Content = finalContent,
                Usage = finalUsage != null ? new UsageDto
                {
                    PromptTokens = finalUsage.PromptTokens,
                    CompletionTokens = finalUsage.CompletionTokens,
                    TotalTokens = finalUsage.TotalTokens
                } : null,
                FinishReason = finishReason
            });

            return Ok(new ChatResponseDto
            {
                Content = finalContent,
                ConversationId = conversation.Id.ToString(),
                FinishReason = finishReason,
                Usage = finalUsage != null ? new UsageDto
                {
                    PromptTokens = finalUsage.PromptTokens,
                    CompletionTokens = finalUsage.CompletionTokens,
                    TotalTokens = finalUsage.TotalTokens
                } : null,
                MemoryExtractionTriggered = false,
                MemoriesInjected = 0
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing chat request");
            return StatusCode(500, new { error = "An error occurred processing your request" });
        }
    }

    /// <summary>
    /// Streams a chat response using Server-Sent Events.
    /// </summary>
    [HttpPost("stream")]
    public async Task StreamChat(
        [FromBody] ChatRequestDto request,
        CancellationToken ct)
    {
        if (request.Messages == null || request.Messages.Count == 0)
        {
            Response.StatusCode = 400;
            await Response.WriteAsync("Messages cannot be empty", ct);
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        try
        {
            Guid? userId = null;
            if (!string.IsNullOrEmpty(request.UserId) && Guid.TryParse(request.UserId, out var parsedUserId))
            {
                userId = parsedUserId;
            }
            var effectiveUserId = userId ?? GetDefaultAdminUserId();

            // Resolve conversation and load history (same as Chat) so stream has full context
            Conversation streamConversation;
            if (!string.IsNullOrWhiteSpace(request.ConversationId) && Guid.TryParse(request.ConversationId, out var requestedStreamConvId))
            {
                var existing = await _conversationRepository.GetByIdAsync(requestedStreamConvId, ct);
                streamConversation = existing ?? await _conversationRepository.GetOrCreateAsync("admin", "default", effectiveUserId, "Admin", ct);
            }
            else
            {
                streamConversation = await _conversationRepository.GetOrCreateAsync("admin", "default", effectiveUserId, "Admin", ct);
            }

            var streamHistoryMessages = await _conversationRepository.GetMessagesAsync(streamConversation.Id, MaxConversationContextMessages, ct);
            var streamHistoryChat = streamHistoryMessages.Select(m => new ChatMessage
            {
                Role = m.Role.ToString().ToLowerInvariant(),
                Content = m.Content
            }).ToList();
            var streamClientChat = request.Messages!.Select(m => new ChatMessage { Role = m.Role, Content = m.Content }).ToList();
            var streamFullMessages = streamHistoryChat.Concat(streamClientChat).ToList();

            var conversationRequest = new ConversationRequest
            {
                Messages = streamFullMessages,
                ConversationId = streamConversation.Id.ToString(),
                UserId = effectiveUserId,
                IncludeMemory = request.IncludeMemory ?? true,
                ExtractMemories = request.ExtractMemories ?? true,
                AdditionalContext = request.AdditionalContext,
                Parameters = request.Parameters != null ? new LlmParameters
                {
                    Model = request.Parameters.Model,
                    MaxTokens = request.Parameters.MaxTokens ?? 4096,
                    Temperature = request.Parameters.Temperature ?? 0.7f
                } : null
            };

            var sseJsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            await foreach (var delta in _orchestrator.StreamChatAsync(conversationRequest, ct))
            {
                var json = JsonSerializer.Serialize(delta, sseJsonOptions);
                await Response.WriteAsync($"data: {json}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming chat response");
            await Response.WriteAsync($"data: [ERROR] {ex.Message}\n\n", ct);
        }
    }

    /// <summary>
    /// Simple endpoint to test the assistant with a single message.
    /// </summary>
    [HttpPost("simple")]
    public async Task<ActionResult<ChatResponseDto>> SimpleChat(
        [FromBody] SimpleMessageDto request,
        CancellationToken ct)
    {
        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = request.Message }
        };

        var conversationRequest = new ConversationRequest
        {
            Messages = messages,
            IncludeMemory = request.IncludeMemory ?? true,
            ExtractMemories = false // Single messages don't warrant memory extraction
        };

        try
        {
            var response = await _orchestrator.ChatAsync(conversationRequest, ct);

            return Ok(new ChatResponseDto
            {
                Content = response.Content,
                Usage = response.Usage != null ? new UsageDto
                {
                    PromptTokens = response.Usage.PromptTokens,
                    CompletionTokens = response.Usage.CompletionTokens,
                    TotalTokens = response.Usage.TotalTokens
                } : null,
                MemoriesInjected = response.MemoriesInjected
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing simple chat request");
            return StatusCode(500, new { error = "An error occurred processing your request" });
        }
    }
}

#region DTOs

public class ChatRequestDto
{
    public List<MessageDto>? Messages { get; set; }
    public string? ConversationId { get; set; }
    public string? UserId { get; set; }
    public bool? IncludeMemory { get; set; }
    public bool? ExtractMemories { get; set; }
    public string? AdditionalContext { get; set; }
    public LlmParametersDto? Parameters { get; set; }
}

public class MessageDto
{
    public required string Role { get; set; }
    public required string Content { get; set; }
}

public class LlmParametersDto
{
    public string? Model { get; set; }
    public int? MaxTokens { get; set; }
    public float? Temperature { get; set; }
}

public class ChatResponseDto
{
    public required string Content { get; set; }
    /// <summary>Conversation ID to send on subsequent requests for context continuity.</summary>
    public string? ConversationId { get; set; }
    public List<ToolCallDto>? ToolCalls { get; set; }
    public string? FinishReason { get; set; }
    public UsageDto? Usage { get; set; }
    public bool MemoryExtractionTriggered { get; set; }
    public int MemoriesInjected { get; set; }
}

public class ToolCallDto
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public string? Arguments { get; set; }
}

public class UsageDto
{
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
}

public class SimpleMessageDto
{
    public required string Message { get; set; }
    public bool? IncludeMemory { get; set; }
}

public class AssistantDeltaDto
{
    public Guid ConversationId { get; set; }
    public Guid MessageId { get; set; }
    public required string Delta { get; set; }
}

public class AssistantDoneDto
{
    public Guid ConversationId { get; set; }
    public Guid MessageId { get; set; }
    public required string Content { get; set; }
    public UsageDto? Usage { get; set; }
    public string? FinishReason { get; set; }
}

#endregion