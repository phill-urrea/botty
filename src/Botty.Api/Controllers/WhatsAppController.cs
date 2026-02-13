using Botty.Api.Services;
using Botty.Channels.Services;
using Botty.Channels.WhatsApp;
using Botty.Core.Enums;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Botty.Infrastructure.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Botty.Api.Controllers;

/// <summary>
/// API controller for WhatsApp bridge integration.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class WhatsAppController : ControllerBase
{
    private readonly IKanbanService _kanbanService;
    private readonly IWhatsAppBridgeClient _bridgeClient;
    private readonly WhatsAppChannelPlugin _whatsAppPlugin;
    private readonly PairingService _pairingService;
    private readonly PairingRepository _pairingRepo;
    private readonly ILogger<WhatsAppController> _logger;

    public WhatsAppController(
        IKanbanService kanbanService,
        IWhatsAppBridgeClient bridgeClient,
        WhatsAppChannelPlugin whatsAppPlugin,
        PairingService pairingService,
        PairingRepository pairingRepo,
        ILogger<WhatsAppController> logger)
    {
        _kanbanService = kanbanService;
        _bridgeClient = bridgeClient;
        _whatsAppPlugin = whatsAppPlugin;
        _pairingService = pairingService;
        _pairingRepo = pairingRepo;
        _logger = logger;
    }

    /// <summary>
    /// Gets WhatsApp bridge connection status (proxied from bridge when configured).
    /// Returns 200 with connected: false when bridge is unreachable or not configured.
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<WhatsAppStatusDto>> GetStatus(CancellationToken ct)
    {
        var result = await _bridgeClient.GetStatusAsync(ct);
        return Ok(new WhatsAppStatusDto
        {
            Connected = result.Connected,
            PhoneNumber = result.PhoneNumber,
            QrCode = null,
            LastSeen = result.LastSeen
        });
    }

    /// <summary>
    /// Gets status for all WhatsApp accounts (multi-account mode).
    /// </summary>
    [HttpGet("status/all")]
    public ActionResult<Dictionary<string, WhatsAppAccountStatus>> GetAllStatuses()
    {
        return Ok(_whatsAppPlugin.GetAllAccountStatuses());
    }

    /// <summary>
    /// Gets status for a specific WhatsApp account.
    /// </summary>
    [HttpGet("status/{accountId}")]
    public ActionResult<WhatsAppAccountStatus> GetAccountStatus(string accountId)
    {
        var statuses = _whatsAppPlugin.GetAllAccountStatuses();
        if (statuses.TryGetValue(accountId, out var status))
            return Ok(status);

        return NotFound(new { error = $"Account '{accountId}' not found" });
    }

    /// <summary>
    /// Gets QR code image (data URL) for WhatsApp linking (proxied from bridge when configured).
    /// Returns 200 with empty qrCode when none available or bridge unreachable.
    /// </summary>
    [HttpGet("qr")]
    public async Task<ActionResult<WhatsAppQrDto>> GetQr(CancellationToken ct)
    {
        var qrCode = await _bridgeClient.GetQrImageAsync(ct);
        return Ok(new WhatsAppQrDto { QrCode = qrCode ?? string.Empty });
    }

    /// <summary>
    /// Receives an incoming WhatsApp message from the bridge.
    /// </summary>
    [HttpPost("messages/incoming")]
    public async Task<ActionResult<IncomingMessageResponse>> ReceiveMessage(
        [FromBody] IncomingWhatsAppMessage request,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Received WhatsApp message from {From}: {Preview}",
            request.From,
            request.Body.Length > 50 ? request.Body[..50] + "..." : request.Body);

        // Store the message in conversation history
        // TODO: Implement conversation storage

        return Ok(new IncomingMessageResponse
        {
            MessageId = request.MessageId,
            Received = true,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Gets pending outbound messages that have been approved.
    /// </summary>
    [HttpGet("messages/pending")]
    public async Task<ActionResult<IEnumerable<PendingOutboundMessage>>> GetPendingMessages(
        CancellationToken ct)
    {
        // Get tasks in Done lane with send_whatsapp action type that haven't been executed yet
        var tasks = await _kanbanService.GetTasksByLaneAsync(KanbanLane.Done, ct);
        
        var pendingMessages = tasks
            .Where(t => t.Type == TaskType.SendMessage && 
                       t.PendingActionData?.ActionType == "send_whatsapp" &&
                       t.CompletedAt == null)
            .Select(t => new PendingOutboundMessage
            {
                TaskId = t.Id,
                To = t.PendingActionData?.Payload?.GetValueOrDefault("to") ?? string.Empty,
                Body = t.PendingActionData?.Payload?.GetValueOrDefault("body") ?? string.Empty,
                ReplyToMessageId = t.PendingActionData?.Payload?.GetValueOrDefault("replyToMessageId"),
                ApprovedAt = t.ApprovedAt ?? DateTime.UtcNow
            })
            .ToList();

        return Ok(pendingMessages);
    }

    /// <summary>
    /// Notifies that a message was sent successfully.
    /// </summary>
    [HttpPost("messages/sent")]
    public async Task<ActionResult> NotifyMessageSent(
        [FromBody] MessageSentNotification request,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "WhatsApp message {MessageId} sent for task {TaskId}",
            request.MessageId,
            request.TaskId);

        if (Guid.TryParse(request.TaskId, out var taskId))
        {
            // Mark the task as completed
            var task = await _kanbanService.GetTaskAsync(taskId, ct);
            if (task != null)
            {
                await _kanbanService.MoveTaskAsync(taskId, KanbanLane.Done, ct);
            }
        }

        return Ok(new { success = true });
    }

    /// <summary>
    /// Queues a message to be sent (requires approval).
    /// </summary>
    [HttpPost("messages/queue")]
    public async Task<ActionResult<QueueMessageResponse>> QueueMessage(
        [FromBody] QueueMessageRequest request,
        CancellationToken ct)
    {
        var task = await _kanbanService.CreateTaskAsync(new CreateTaskRequest
        {
            Title = $"Send WhatsApp to {request.RecipientName ?? request.To}",
            Description = $"Message to send:\n\n{request.Body}",
            Type = TaskType.SendMessage,
            Assignee = TaskAssignee.Assistant,
            Priority = request.Priority ?? TaskPriority.Normal,
            PendingAction = new PendingAction
            {
                ActionType = "send_whatsapp",
                Description = $"Send WhatsApp message to {request.RecipientName ?? request.To}",
                Payload = new Dictionary<string, string>
                {
                    ["to"] = request.To,
                    ["body"] = request.Body,
                    ["recipientName"] = request.RecipientName ?? request.To
                },
                Preview = request.Body.Length > 100 ? request.Body[..100] + "..." : request.Body,
                RequiresApproval = true
            }
        }, ct);

        // Move to Needs Approval
        await _kanbanService.MoveTaskAsync(task.Id, KanbanLane.NeedsApproval, ct);

        _logger.LogInformation("Queued WhatsApp message for approval: Task {TaskId}", task.Id);

        return Ok(new QueueMessageResponse
        {
            TaskId = task.Id,
            Status = "pending_approval"
        });
    }
    // =========================================================================
    // Pairing & Security Endpoints
    // =========================================================================

    /// <summary>
    /// Approves a pairing code, adding the sender to the allow list.
    /// </summary>
    [HttpPost("pairing/approve")]
    public async Task<ActionResult> ApprovePairing(
        [FromBody] ApprovePairingRequest request,
        CancellationToken ct)
    {
        var success = await _pairingService.ApproveCodeAsync("whatsapp", request.Code, ct);
        if (!success)
            return NotFound(new { error = "Pairing code not found or expired" });

        _logger.LogInformation("Approved WhatsApp pairing code: {Code}", request.Code);
        return Ok(new { success = true });
    }

    /// <summary>
    /// Gets pending pairing requests.
    /// </summary>
    [HttpGet("pairing/pending")]
    public async Task<ActionResult<IEnumerable<PairingRequestDto>>> GetPendingPairings(
        CancellationToken ct)
    {
        var pending = await _pairingService.GetPendingAsync("whatsapp", ct);
        var dtos = pending.Select(p => new PairingRequestDto
        {
            Id = p.Id,
            SenderId = p.SenderId,
            Code = p.Code,
            CreatedAt = p.CreatedAt,
            ExpiresAt = p.ExpiresAt
        });
        return Ok(dtos);
    }

    /// <summary>
    /// Gets the allow list for WhatsApp.
    /// </summary>
    [HttpGet("security/allowlist")]
    public async Task<ActionResult<IEnumerable<AllowListEntryDto>>> GetAllowList(
        CancellationToken ct)
    {
        var entries = await _pairingRepo.GetAllowListAsync("whatsapp", ct);
        var dtos = entries.Select(e => new AllowListEntryDto
        {
            Id = e.Id,
            Entry = e.Entry,
            CreatedAt = e.CreatedAt
        });
        return Ok(dtos);
    }

    /// <summary>
    /// Adds an entry to the allow list.
    /// </summary>
    [HttpPost("security/allowlist")]
    public async Task<ActionResult> AddToAllowList(
        [FromBody] AddAllowListRequest request,
        CancellationToken ct)
    {
        await _pairingRepo.AddToAllowListAsync("whatsapp", request.Entry, ct);
        return Ok(new { success = true });
    }

    /// <summary>
    /// Removes an entry from the allow list.
    /// </summary>
    [HttpDelete("security/allowlist/{entry}")]
    public async Task<ActionResult> RemoveFromAllowList(
        string entry,
        CancellationToken ct)
    {
        await _pairingRepo.RemoveFromAllowListAsync("whatsapp", entry, ct);
        return Ok(new { success = true });
    }
}

#region DTOs

public class ApprovePairingRequest
{
    public required string Code { get; set; }
}

public class PairingRequestDto
{
    public Guid Id { get; set; }
    public required string SenderId { get; set; }
    public required string Code { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public class AllowListEntryDto
{
    public Guid Id { get; set; }
    public required string Entry { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AddAllowListRequest
{
    public required string Entry { get; set; }
}

public class IncomingWhatsAppMessage
{
    public required string MessageId { get; set; }
    public required string From { get; set; }
    public required string FromName { get; set; }
    public required string Body { get; set; }
    public required DateTime Timestamp { get; set; }
    public bool IsGroup { get; set; }
    public string? GroupName { get; set; }
    public string? ConversationId { get; set; }
}

public class IncomingMessageResponse
{
    public required string MessageId { get; set; }
    public bool Received { get; set; }
    public DateTime Timestamp { get; set; }
}

public class PendingOutboundMessage
{
    public Guid TaskId { get; set; }
    public required string To { get; set; }
    public required string Body { get; set; }
    public string? ReplyToMessageId { get; set; }
    public DateTime ApprovedAt { get; set; }
}

public class MessageSentNotification
{
    public required string TaskId { get; set; }
    public required string MessageId { get; set; }
    public required string To { get; set; }
    public required string Body { get; set; }
    public required DateTime Timestamp { get; set; }
}

public class QueueMessageRequest
{
    public required string To { get; set; }
    public required string Body { get; set; }
    public string? RecipientName { get; set; }
    public string? ReplyToMessageId { get; set; }
    public TaskPriority? Priority { get; set; }
    /// <summary>
    /// Optional account ID for multi-account routing. Defaults to the configured default account.
    /// </summary>
    public string? AccountId { get; set; }
}

public class QueueMessageResponse
{
    public Guid TaskId { get; set; }
    public required string Status { get; set; }
}

public class WhatsAppStatusDto
{
    public bool Connected { get; set; }
    public string? PhoneNumber { get; set; }
    public string? QrCode { get; set; }
    public string? LastSeen { get; set; }
}

public class WhatsAppQrDto
{
    public string QrCode { get; set; } = string.Empty;
}

#endregion
