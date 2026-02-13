using Botty.Core.Interfaces;
using Botty.Core.Models;
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
    private readonly ISkillRegistry _skillRegistry;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        IConversationOrchestrator orchestrator,
        IConversationRepository conversationRepository,
        IFeedBroadcastService feedBroadcast,
        ISkillRegistry skillRegistry,
        IConfiguration configuration,
        ILogger<ChatController> logger)
    {
        _orchestrator = orchestrator;
        _conversationRepository = conversationRepository;
        _feedBroadcast = feedBroadcast;
        _skillRegistry = skillRegistry;
        _configuration = configuration;
        _logger = logger;
    }

    private Guid GetDefaultAdminUserId()
    {
        var value = _configuration["Conversation:DefaultAdminUserId"];
        return Guid.TryParse(value, out var id) ? id : FallbackAdminUserId;
    }

    /// <summary>
    /// Calls the orchestrator and runs the tool execution loop until no tool calls or max iterations.
    /// </summary>
    private async Task<ConversationResponse> RunChatWithToolLoopAsync(
        ConversationRequest request, CancellationToken ct, Guid? conversationId = null, string? source = null)
    {
        var response = await _orchestrator.ChatAsync(request, ct);
        var iterations = 0;

        while (response.ToolCalls is { Count: > 0 } toolCalls && iterations < MaxToolLoopIterations)
        {
            iterations++;

            // Re-broadcast typing indicator so it stays active during multi-turn tool use
            if (conversationId.HasValue && source != null)
            {
                _feedBroadcast.BroadcastTypingIndicator(new TypingIndicatorDto
                {
                    ConversationId = conversationId.Value,
                    Source = source,
                    IsTyping = true,
                    Timestamp = DateTime.UtcNow
                });
            }

            var toolResults = new List<LlmToolResult>();
            foreach (var tc in toolCalls)
            {
                var skillResult = await _skillRegistry.ExecuteToolAsync(tc.Name, tc.Arguments ?? "{}", ct);
                if (!skillResult.Success)
                {
                    _logger.LogWarning("Tool {ToolName} failed. Arguments: {Arguments}. Error: {Error}",
                        tc.Name, tc.Arguments ?? "(null)", skillResult.Error ?? "(none)");
                }
                var content = skillResult.Success ? (skillResult.Result ?? string.Empty) : $"Error: {skillResult.Error}";
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

            response = await _orchestrator.ChatAsync(request, ct);
        }

        return response;
    }

    /// <summary>
    /// Sends a message and gets a response.
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
                AvailableTools = _skillRegistry.GetAll().SelectMany(s => s.GetTools()).ToList(),
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
                var persisted = await _conversationRepository.AppendMessageAsync(conversation.Id, role, m.Content, null, null, ct);
                _feedBroadcast.BroadcastNewMessage(new FeedMessageDto
                {
                    Id = persisted.Id,
                    ConversationId = conversation.Id,
                    Source = conversation.Source,
                    ExternalId = conversation.ExternalId,
                    Role = persisted.Role.ToString().ToLowerInvariant(),
                    Content = persisted.Content,
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

            ConversationResponse response;
            try
            {
                response = await RunChatWithToolLoopAsync(conversationRequest, ct, conversation.Id, conversation.Source);
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

            var assistantMessage = await _conversationRepository.AppendMessageAsync(
                conversation.Id, MessageRole.Assistant, response.Content, null, null, ct);
            _feedBroadcast.BroadcastNewMessage(new FeedMessageDto
            {
                Id = assistantMessage.Id,
                ConversationId = conversation.Id,
                Source = conversation.Source,
                ExternalId = conversation.ExternalId,
                Role = assistantMessage.Role.ToString().ToLowerInvariant(),
                Content = assistantMessage.Content,
                SenderName = assistantMessage.SenderName,
                CreatedAt = assistantMessage.CreatedAt
            });

            return Ok(new ChatResponseDto
            {
                Content = response.Content,
                ConversationId = conversation.Id.ToString(),
                ToolCalls = response.ToolCalls?.Select(tc => new ToolCallDto
                {
                    Id = tc.Id,
                    Name = tc.Name,
                    Arguments = tc.Arguments
                }).ToList(),
                FinishReason = response.FinishReason,
                Usage = response.Usage != null ? new UsageDto
                {
                    PromptTokens = response.Usage.PromptTokens,
                    CompletionTokens = response.Usage.CompletionTokens,
                    TotalTokens = response.Usage.TotalTokens
                } : null,
                MemoryExtractionTriggered = response.MemoryExtractionTriggered,
                MemoriesInjected = response.MemoriesInjected
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

            await foreach (var chunk in _orchestrator.StreamChatAsync(conversationRequest, ct))
            {
                await Response.WriteAsync($"data: {chunk}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }

            await Response.WriteAsync("data: [DONE]\n\n", ct);
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

#endregion