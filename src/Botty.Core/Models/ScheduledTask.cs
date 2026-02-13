using Botty.Core.Enums;

namespace Botty.Core.Models;

/// <summary>
/// Represents a scheduled task that runs on a cron schedule.
/// </summary>
public class ScheduledTask
{
    /// <summary>
    /// Unique identifier for the scheduled task.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Name of the scheduled task.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Description of what this scheduled task does.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Cron expression defining when the task runs (e.g., "0 9 * * *" for daily at 9am).
    /// </summary>
    public required string CronExpression { get; set; }

    /// <summary>
    /// When the task is next scheduled to run.
    /// </summary>
    public DateTime NextRunAt { get; set; }

    /// <summary>
    /// When the task last ran (null if never).
    /// </summary>
    public DateTime? LastRunAt { get; set; }

    /// <summary>
    /// Template for creating the Kanban task when triggered.
    /// </summary>
    public required KanbanTaskTemplate TaskTemplate { get; set; }

    /// <summary>
    /// Who the created task should be assigned to.
    /// </summary>
    public TaskAssignee Assignee { get; set; } = TaskAssignee.Assistant;

    /// <summary>
    /// Whether this task recurs or is one-time.
    /// </summary>
    public bool IsRecurring { get; set; } = true;

    /// <summary>
    /// Maximum number of times this task should run (null for infinite).
    /// </summary>
    public int? MaxOccurrences { get; set; }

    /// <summary>
    /// Number of times this task has run.
    /// </summary>
    public int OccurrenceCount { get; set; }

    /// <summary>
    /// Whether the scheduled task is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Who created this scheduled task ("user" or "assistant").
    /// </summary>
    public required string CreatedBy { get; set; }

    /// <summary>
    /// When this scheduled task was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Template for creating a Kanban task from a scheduled task.
/// </summary>
public class KanbanTaskTemplate
{
    /// <summary>
    /// Title template for the created task.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Description template for the created task.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Type of task to create.
    /// </summary>
    public TaskType Type { get; set; } = TaskType.General;

    /// <summary>
    /// Priority of the created task.
    /// </summary>
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;

    /// <summary>
    /// Who the task should be assigned to.
    /// </summary>
    public TaskAssignee Assignee { get; set; } = TaskAssignee.Assistant;

    /// <summary>
    /// Whether this task requires approval before execution.
    /// </summary>
    public bool RequiresApproval { get; set; }

    /// <summary>
    /// Optional pre-filled action for the task.
    /// </summary>
    public PendingAction? PendingAction { get; set; }
}
