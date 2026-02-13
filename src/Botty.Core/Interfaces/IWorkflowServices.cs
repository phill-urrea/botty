using Botty.Core.Enums;
using Botty.Core.Models;

namespace Botty.Core.Interfaces;

/// <summary>
/// Service for managing Kanban tasks.
/// </summary>
public interface IKanbanService
{
    /// <summary>
    /// Creates a new task.
    /// </summary>
    Task<KanbanTask> CreateTaskAsync(CreateTaskRequest request, CancellationToken ct = default);

    /// <summary>
    /// Gets a task by ID.
    /// </summary>
    Task<KanbanTask?> GetTaskAsync(Guid taskId, CancellationToken ct = default);

    /// <summary>
    /// Gets all active tasks.
    /// </summary>
    Task<IEnumerable<KanbanTask>> GetActiveTasksAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets tasks by lane.
    /// </summary>
    Task<IEnumerable<KanbanTask>> GetTasksByLaneAsync(KanbanLane lane, CancellationToken ct = default);

    /// <summary>
    /// Gets tasks by assignee.
    /// </summary>
    Task<IEnumerable<KanbanTask>> GetTasksByAssigneeAsync(TaskAssignee assignee, CancellationToken ct = default);

    /// <summary>
    /// Moves a task to a different lane.
    /// </summary>
    Task<KanbanTask> MoveTaskAsync(Guid taskId, KanbanLane lane, CancellationToken ct = default);

    /// <summary>
    /// Updates a task.
    /// </summary>
    Task<KanbanTask> UpdateTaskAsync(Guid taskId, UpdateTaskRequest request, CancellationToken ct = default);

    /// <summary>
    /// Deletes a task.
    /// </summary>
    Task DeleteTaskAsync(Guid taskId, CancellationToken ct = default);

    /// <summary>
    /// Gets the Kanban board state (all lanes with tasks).
    /// </summary>
    Task<KanbanBoard> GetBoardAsync(CancellationToken ct = default);
}

/// <summary>
/// Request to create a new task.
/// </summary>
public class CreateTaskRequest
{
    public required string Title { get; set; }
    public string? Description { get; set; }
    public KanbanLane Lane { get; set; } = KanbanLane.ToDo;
    public TaskAssignee Assignee { get; set; } = TaskAssignee.User;
    public TaskType Type { get; set; } = TaskType.General;
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;
    public Guid? ConversationId { get; set; }
    public Guid? UserId { get; set; }
    public string? Source { get; set; }
    public string? ExternalId { get; set; }
    public PendingAction? PendingAction { get; set; }
}

/// <summary>
/// Request to update a task.
/// </summary>
public class UpdateTaskRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public TaskAssignee? Assignee { get; set; }
    public TaskPriority? Priority { get; set; }
    public string? ExecutionResult { get; set; }
}

/// <summary>
/// Represents the full Kanban board state.
/// </summary>
public class KanbanBoard
{
    public IList<KanbanTask> ToDo { get; set; } = [];
    public IList<KanbanTask> InProgress { get; set; } = [];
    public IList<KanbanTask> NeedsApproval { get; set; } = [];
    public IList<KanbanTask> Done { get; set; } = [];
    public IList<KanbanTask> Cancelled { get; set; } = [];
}

/// <summary>
/// Service for managing approvals.
/// </summary>
public interface IApprovalService
{
    /// <summary>
    /// Creates a task requiring approval before execution.
    /// </summary>
    Task<KanbanTask> RequestApprovalAsync(ApprovalRequest request, CancellationToken ct = default);

    /// <summary>
    /// Approves a pending task.
    /// </summary>
    Task<KanbanTask> ApproveAsync(Guid taskId, string? comment = null, CancellationToken ct = default);

    /// <summary>
    /// Rejects a pending task.
    /// </summary>
    Task<KanbanTask> RejectAsync(Guid taskId, string reason, CancellationToken ct = default);

    /// <summary>
    /// Gets all tasks pending approval.
    /// </summary>
    Task<IEnumerable<KanbanTask>> GetPendingApprovalsAsync(CancellationToken ct = default);

    /// <summary>
    /// Checks if a task type requires approval.
    /// </summary>
    bool RequiresApproval(TaskType taskType);
}

/// <summary>
/// Request for approval.
/// </summary>
public class ApprovalRequest
{
    public required string Title { get; set; }
    public string? Description { get; set; }
    public required TaskType Type { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;
    public TaskAssignee Assignee { get; set; } = TaskAssignee.Assistant;
    public Guid? ConversationId { get; set; }
    public Guid? UserId { get; set; }
    public string? Source { get; set; }
    public string? ExternalId { get; set; }

    /// <summary>
    /// The action to execute when approved.
    /// </summary>
    public required PendingAction Action { get; set; }
}

/// <summary>
/// Event raised when a task changes state.
/// </summary>
public class TaskStateChangedEvent
{
    public required KanbanTask Task { get; set; }
    public required KanbanLane PreviousLane { get; set; }
    public required KanbanLane NewLane { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Interface for handling task state changes.
/// </summary>
public interface ITaskStateHandler
{
    /// <summary>
    /// Handles a task state change event.
    /// </summary>
    Task HandleStateChangeAsync(TaskStateChangedEvent stateChange, CancellationToken ct = default);
}
