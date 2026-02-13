using Botty.Core.Enums;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace Botty.Api.Controllers;

/// <summary>
/// API endpoints for Kanban board management.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class KanbanController : ControllerBase
{
    private readonly IKanbanService _kanbanService;
    private readonly IApprovalService _approvalService;
    private readonly ILogger<KanbanController> _logger;

    public KanbanController(
        IKanbanService kanbanService,
        IApprovalService approvalService,
        ILogger<KanbanController> logger)
    {
        _kanbanService = kanbanService;
        _approvalService = approvalService;
        _logger = logger;
    }

    /// <summary>
    /// Gets the full Kanban board state.
    /// </summary>
    [HttpGet("board")]
    public async Task<IActionResult> GetBoard(CancellationToken ct)
    {
        var board = await _kanbanService.GetBoardAsync(ct);
        return Ok(new KanbanBoardDto
        {
            ToDo = board.ToDo.Select(MapToDto).ToList(),
            InProgress = board.InProgress.Select(MapToDto).ToList(),
            NeedsApproval = board.NeedsApproval.Select(MapToDto).ToList(),
            Done = board.Done.Select(MapToDto).ToList(),
            Cancelled = board.Cancelled.Select(MapToDto).ToList()
        });
    }

    /// <summary>
    /// Gets a task by ID.
    /// </summary>
    [HttpGet("{taskId:guid}")]
    public async Task<IActionResult> GetTask(Guid taskId, CancellationToken ct)
    {
        var task = await _kanbanService.GetTaskAsync(taskId, ct);
        if (task == null)
        {
            return NotFound();
        }
        return Ok(MapToDto(task));
    }

    /// <summary>
    /// Gets tasks by lane.
    /// </summary>
    [HttpGet("lane/{lane}")]
    public async Task<IActionResult> GetTasksByLane(KanbanLane lane, CancellationToken ct)
    {
        var tasks = await _kanbanService.GetTasksByLaneAsync(lane, ct);
        return Ok(tasks.Select(MapToDto));
    }

    /// <summary>
    /// Gets tasks by assignee.
    /// </summary>
    [HttpGet("assignee/{assignee}")]
    public async Task<IActionResult> GetTasksByAssignee(TaskAssignee assignee, CancellationToken ct)
    {
        var tasks = await _kanbanService.GetTasksByAssigneeAsync(assignee, ct);
        return Ok(tasks.Select(MapToDto));
    }

    /// <summary>
    /// Creates a new task.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateTask([FromBody] CreateTaskDto request, CancellationToken ct)
    {
        var createRequest = new CreateTaskRequest
        {
            Title = request.Title,
            Description = request.Description,
            Lane = request.Lane ?? KanbanLane.ToDo,
            Assignee = request.Assignee ?? TaskAssignee.User,
            Type = request.Type ?? TaskType.General,
            Priority = request.Priority ?? TaskPriority.Normal
        };

        var task = await _kanbanService.CreateTaskAsync(createRequest, ct);
        return CreatedAtAction(nameof(GetTask), new { taskId = task.Id }, MapToDto(task));
    }

    /// <summary>
    /// Updates a task.
    /// </summary>
    [HttpPut("{taskId:guid}")]
    public async Task<IActionResult> UpdateTask(Guid taskId, [FromBody] UpdateTaskDto request, CancellationToken ct)
    {
        var updateRequest = new UpdateTaskRequest
        {
            Title = request.Title,
            Description = request.Description,
            Assignee = request.Assignee,
            Priority = request.Priority
        };

        try
        {
            var task = await _kanbanService.UpdateTaskAsync(taskId, updateRequest, ct);
            return Ok(MapToDto(task));
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Moves a task to a different lane.
    /// </summary>
    [HttpPost("{taskId:guid}/move")]
    public async Task<IActionResult> MoveTask(Guid taskId, [FromBody] MoveTaskDto request, CancellationToken ct)
    {
        try
        {
            var task = await _kanbanService.MoveTaskAsync(taskId, request.Lane, ct);
            return Ok(MapToDto(task));
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Deletes a task.
    /// </summary>
    [HttpDelete("{taskId:guid}")]
    public async Task<IActionResult> DeleteTask(Guid taskId, CancellationToken ct)
    {
        await _kanbanService.DeleteTaskAsync(taskId, ct);
        return NoContent();
    }

    /// <summary>
    /// Gets all tasks pending approval.
    /// </summary>
    [HttpGet("approvals")]
    public async Task<IActionResult> GetPendingApprovals(CancellationToken ct)
    {
        var tasks = await _approvalService.GetPendingApprovalsAsync(ct);
        return Ok(tasks.Select(MapToDto));
    }

    /// <summary>
    /// Approves a pending task.
    /// </summary>
    [HttpPost("{taskId:guid}/approve")]
    public async Task<IActionResult> ApproveTask(Guid taskId, [FromBody] ApproveTaskDto? request, CancellationToken ct)
    {
        try
        {
            var task = await _approvalService.ApproveAsync(taskId, request?.Comment, ct);
            return Ok(MapToDto(task));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Rejects a pending task.
    /// </summary>
    [HttpPost("{taskId:guid}/reject")]
    public async Task<IActionResult> RejectTask(Guid taskId, [FromBody] RejectTaskDto request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest("Rejection reason is required");
        }

        try
        {
            var task = await _approvalService.RejectAsync(taskId, request.Reason, ct);
            return Ok(MapToDto(task));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private static KanbanTaskDto MapToDto(KanbanTask task) => new()
    {
        Id = task.Id,
        Title = task.Title,
        Description = task.Description,
        Lane = task.Lane.ToString(),
        Assignee = task.Assignee.ToString(),
        Type = task.Type.ToString(),
        Priority = task.Priority.ToString(),
        ConversationId = task.ConversationId,
        UserId = task.UserId,
        Source = task.Source,
        ExternalId = task.ExternalId,
        HasPendingAction = task.PendingActionData != null,
        PendingActionType = task.PendingActionData?.ActionType,
        PendingActionDescription = task.PendingActionData?.Description,
        RejectionReason = task.RejectionReason,
        ExecutionResult = task.ExecutionResult,
        CreatedAt = task.CreatedAt,
        UpdatedAt = task.UpdatedAt,
        ApprovedAt = task.ApprovedAt,
        CompletedAt = task.CompletedAt
    };
}

#region DTOs

public class KanbanBoardDto
{
    public IList<KanbanTaskDto> ToDo { get; set; } = [];
    public IList<KanbanTaskDto> InProgress { get; set; } = [];
    public IList<KanbanTaskDto> NeedsApproval { get; set; } = [];
    public IList<KanbanTaskDto> Done { get; set; } = [];
    public IList<KanbanTaskDto> Cancelled { get; set; } = [];
}

public class KanbanTaskDto
{
    public Guid Id { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public required string Lane { get; set; }
    public required string Assignee { get; set; }
    public required string Type { get; set; }
    public required string Priority { get; set; }
    public Guid? ConversationId { get; set; }
    public Guid? UserId { get; set; }
    public string? Source { get; set; }
    public string? ExternalId { get; set; }
    public bool HasPendingAction { get; set; }
    public string? PendingActionType { get; set; }
    public string? PendingActionDescription { get; set; }
    public string? RejectionReason { get; set; }
    public string? ExecutionResult { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class CreateTaskDto
{
    public required string Title { get; set; }
    public string? Description { get; set; }
    public KanbanLane? Lane { get; set; }
    public TaskAssignee? Assignee { get; set; }
    public TaskType? Type { get; set; }
    public TaskPriority? Priority { get; set; }
}

public class UpdateTaskDto
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public TaskAssignee? Assignee { get; set; }
    public TaskPriority? Priority { get; set; }
}

public class MoveTaskDto
{
    public KanbanLane Lane { get; set; }
}

public class ApproveTaskDto
{
    public string? Comment { get; set; }
}

public class RejectTaskDto
{
    public required string Reason { get; set; }
}

#endregion
