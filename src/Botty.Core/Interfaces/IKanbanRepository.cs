using Botty.Core.Enums;
using Botty.Core.Models;

namespace Botty.Core.Interfaces;

/// <summary>
/// Repository interface for Kanban task operations.
/// </summary>
public interface IKanbanRepository
{
    /// <summary>
    /// Gets a task by ID.
    /// </summary>
    Task<KanbanTask?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Gets all tasks, optionally filtered by lane.
    /// </summary>
    Task<IEnumerable<KanbanTask>> GetAllAsync(
        KanbanLane? lane = null,
        TaskAssignee? assignee = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets tasks in a specific lane.
    /// </summary>
    Task<IEnumerable<KanbanTask>> GetByLaneAsync(KanbanLane lane, CancellationToken ct = default);

    /// <summary>
    /// Gets tasks assigned to a specific assignee.
    /// </summary>
    Task<IEnumerable<KanbanTask>> GetByAssigneeAsync(TaskAssignee assignee, CancellationToken ct = default);

    /// <summary>
    /// Creates a new task.
    /// </summary>
    Task<KanbanTask> CreateAsync(KanbanTask task, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing task.
    /// </summary>
    Task<KanbanTask> UpdateAsync(KanbanTask task, CancellationToken ct = default);

    /// <summary>
    /// Moves a task to a different lane.
    /// </summary>
    Task<KanbanTask> MoveToLaneAsync(Guid id, KanbanLane lane, CancellationToken ct = default);

    /// <summary>
    /// Approves a task in the NeedsApproval lane.
    /// </summary>
    Task<KanbanTask> ApproveAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Rejects a task in the NeedsApproval lane.
    /// </summary>
    Task<KanbanTask> RejectAsync(Guid id, string reason, CancellationToken ct = default);

    /// <summary>
    /// Deletes a task.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
