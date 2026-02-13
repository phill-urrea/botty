using Botty.Core.Interfaces;
using Botty.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Botty.Api.Services;

namespace Botty.Api.Controllers;

/// <summary>
/// API for the merged feed (admin + channel messages) and incoming channel events.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class FeedController : ControllerBase
{
    private readonly IConversationRepository _conversationRepository;
    private readonly IFeedBroadcastService _feedBroadcast;
    private readonly ILogger<FeedController> _logger;

    public FeedController(
        IConversationRepository conversationRepository,
        IFeedBroadcastService feedBroadcast,
        ILogger<FeedController> logger)
    {
        _conversationRepository = conversationRepository;
        _feedBroadcast = feedBroadcast;
        _logger = logger;
    }

    /// <summary>
    /// Returns a single merged, chronological list of messages across all conversations.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<FeedDto>> GetFeed(
        [FromQuery] DateTime? since,
        [FromQuery] int limit = 500,
        CancellationToken ct = default)
    {
        if (limit < 1 || limit > 1000)
        {
            limit = 500;
        }

        var messages = await _conversationRepository.GetMergedFeedAsync(since, limit, ct);

        var items = messages.Select(m => new FeedMessageDto
        {
            Id = m.Id,
            ConversationId = m.ConversationId,
            Source = m.Conversation?.Source ?? "unknown",
            ExternalId = m.Conversation?.ExternalId,
            Role = m.Role.ToString().ToLowerInvariant(),
            Content = m.Content,
            SenderId = m.SenderId,
            SenderName = m.SenderName,
            CreatedAt = m.CreatedAt
        }).ToList();

        return Ok(new FeedDto { Messages = items });
    }

    /// <summary>
    /// Accepts an incoming channel message (e.g. from WhatsApp bridge). Persists to the feed and optionally creates a Kanban task.
    /// </summary>
    [HttpPost("incoming")]
    public async Task<ActionResult<FeedMessageDto>> Incoming(
        [FromBody] IncomingFeedMessageDto request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ChannelId) || string.IsNullOrWhiteSpace(request.ChatId))
        {
            return BadRequest("ChannelId and ChatId are required");
        }

        var conversation = await _conversationRepository.GetOrCreateAsync(
            request.ChannelId,
            request.ChatId,
            userId: Guid.Empty,
            title: request.SenderName != null ? $"{request.ChannelId}: {request.SenderName}" : null,
            ct);

        var role = string.Equals(request.SenderId, request.ChatId, StringComparison.OrdinalIgnoreCase)
            ? MessageRole.User
            : MessageRole.ThirdParty;

        var message = await _conversationRepository.AppendMessageAsync(
            conversation.Id,
            role,
            request.Text ?? string.Empty,
            senderName: request.SenderName,
            externalId: request.MessageId,
            senderId: request.SenderId,
            ct: ct);

        _logger.LogInformation(
            "Feed incoming: channel={ChannelId} chatId={ChatId} from={Sender}",
            request.ChannelId, request.ChatId, request.SenderName ?? request.SenderId);

        var dto = new FeedMessageDto
        {
            Id = message.Id,
            ConversationId = message.ConversationId,
            Source = conversation.Source,
            ExternalId = conversation.ExternalId,
            Role = message.Role.ToString().ToLowerInvariant(),
            Content = message.Content,
            SenderId = message.SenderId,
            SenderName = message.SenderName,
            CreatedAt = message.CreatedAt
        };

        _feedBroadcast.BroadcastNewMessage(dto);

        return Ok(dto);
    }
}

#region DTOs

public class FeedDto
{
    public required List<FeedMessageDto> Messages { get; set; }
}

public class FeedMessageDto
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public required string Source { get; set; }
    public string? ExternalId { get; set; }
    public required string Role { get; set; }
    public required string Content { get; set; }
    public string? SenderId { get; set; }
    public string? SenderName { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class IncomingFeedMessageDto
{
    public required string ChannelId { get; set; }
    public required string ChatId { get; set; }
    public required string MessageId { get; set; }
    public required string SenderId { get; set; }
    public string? SenderName { get; set; }
    public required string Text { get; set; }
    public DateTime Timestamp { get; set; }
}

public class TypingIndicatorDto
{
    public Guid ConversationId { get; set; }
    public required string Source { get; set; }
    public bool IsTyping { get; set; }
    public DateTime Timestamp { get; set; }
}

#endregion
