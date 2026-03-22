using Botty.Core.Enums;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Botty.Hooks;
using Botty.Hooks.Models;
using Microsoft.Extensions.Logging;

namespace Botty.Workflow.Services;

/// <summary>
/// Service for managing approvals in the Kanban workflow.
/// </summary>
public class ApprovalService : IApprovalService
{
    private readonly IKanbanRepository _repository;
    private readonly IHookRegistry? _hookRegistry;
    private readonly ILogger<ApprovalService> _logger;

    // Task types that always require approval
    private static readonly HashSet<TaskType> ApprovalRequiredTypes =
    [
        TaskType.SendMessage,
        TaskType.SystemChange,
        TaskType.NewSkillCreation,
        TaskType.ShellCommand,
        TaskType.MemoryModification,
        TaskType.CalendarChange,
        TaskType.BugReport
    ];

    public ApprovalService(
        IKanbanRepository repository,
        ILogger<ApprovalService> logger,
        IHookRegistry? hookRegistry = null)
    {
        _repository = repository;
        _logger = logger;
        _hookRegistry = hookRegistry;
    }

    public async Task<KanbanTask> RequestApprovalAsync(ApprovalRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating approval request: {Title} (type: {Type})", 
            request.Title, request.Type);

        var task = new KanbanTask
        {
            Title = request.Title,
            Description = request.Description,
            Lane = KanbanLane.NeedsApproval,
            Assignee = request.Assignee,
            Type = request.Type,
            Priority = request.Priority,
            ConversationId = request.ConversationId,
            UserId = request.UserId,
            Source = request.Source,
            ExternalId = request.ExternalId,
            PendingActionData = request.Action
        };

        return await _repository.CreateAsync(task, ct);
    }

    public async Task<KanbanTask> ApproveAsync(Guid taskId, string? comment = null, CancellationToken ct = default)
    {
        var task = await _repository.GetByIdAsync(taskId, ct)
            ?? throw new InvalidOperationException($"Task {taskId} not found");

        if (task.Lane != KanbanLane.NeedsApproval)
        {
            throw new InvalidOperationException($"Task {taskId} is not pending approval (current lane: {task.Lane})");
        }

        _logger.LogInformation("Approving task {Id}: {Title}", taskId, task.Title);

        // Move to InProgress for the assistant to execute, or ToDo if assigned to user
        var targetLane = task.Assignee == TaskAssignee.Assistant 
            ? KanbanLane.InProgress 
            : KanbanLane.ToDo;

        // If comment provided, append to description
        if (!string.IsNullOrWhiteSpace(comment))
        {
            task.Description = string.IsNullOrEmpty(task.Description)
                ? $"Approval comment: {comment}"
                : $"{task.Description}\n\nApproval comment: {comment}";
        }

        task.RejectionReason = null; // Clear any previous rejection
        await _repository.UpdateAsync(task, ct);

        var movedTask = await _repository.MoveToLaneAsync(taskId, targetLane, ct);
        if (_hookRegistry != null && movedTask.Assignee == TaskAssignee.Assistant)
        {
            await _hookRegistry.PublishEventAsync(HookTrigger.TaskApproved, new
            {
                TaskId = movedTask.Id,
                Task = movedTask
            }, ct);
        }

        return movedTask;
    }

    public async Task<KanbanTask> RejectAsync(Guid taskId, string reason, CancellationToken ct = default)
    {
        var task = await _repository.GetByIdAsync(taskId, ct)
            ?? throw new InvalidOperationException($"Task {taskId} not found");

        if (task.Lane != KanbanLane.NeedsApproval)
        {
            throw new InvalidOperationException($"Task {taskId} is not pending approval (current lane: {task.Lane})");
        }

        _logger.LogInformation("Rejecting task {Id}: {Title}. Reason: {Reason}", 
            taskId, task.Title, reason);

        task.RejectionReason = reason;
        await _repository.UpdateAsync(task, ct);

        // Move to Cancelled or back to ToDo for revision depending on use case
        // For now, move to Cancelled - the assistant can create a new task if needed
        return await _repository.MoveToLaneAsync(taskId, KanbanLane.Cancelled, ct);
    }

    public async Task<IEnumerable<KanbanTask>> GetPendingApprovalsAsync(CancellationToken ct = default)
    {
        return await _repository.GetByLaneAsync(KanbanLane.NeedsApproval, ct);
    }

    public bool RequiresApproval(TaskType taskType)
    {
        return ApprovalRequiredTypes.Contains(taskType);
    }
}
