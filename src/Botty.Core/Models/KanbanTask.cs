using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Botty.Core.Enums;

namespace Botty.Core.Models;

/// <summary>
/// Represents a task in the Kanban workflow system.
/// </summary>
public class KanbanTask
{
    /// <summary>
    /// Unique identifier for the task.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Title of the task.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Detailed description of the task.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Current lane in the Kanban board.
    /// </summary>
    public KanbanLane Lane { get; set; } = KanbanLane.ToDo;

    /// <summary>
    /// Who the task is assigned to (User or Assistant).
    /// </summary>
    public TaskAssignee Assignee { get; set; } = TaskAssignee.Assistant;

    /// <summary>
    /// Type of task.
    /// </summary>
    public TaskType Type { get; set; } = TaskType.General;

    /// <summary>
    /// Priority level.
    /// </summary>
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;

    /// <summary>
    /// Conversation id this task originated from, when applicable.
    /// </summary>
    public Guid? ConversationId { get; set; }

    /// <summary>
    /// User id associated with this task context.
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Source channel/system for this task context (admin, whatsapp, etc).
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// External channel id/chat id for this task context.
    /// </summary>
    public string? ExternalId { get; set; }

    /// <summary>
    /// Pending action to execute upon approval (null if no approval required).
    /// Alias for PendingActionData for EF mapping compatibility.
    /// </summary>
    public PendingAction? PendingActionData { get; set; }

    /// <summary>
    /// When the task was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the task was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// When the task was approved (if applicable).
    /// </summary>
    public DateTime? ApprovedAt { get; set; }

    /// <summary>
    /// When the task was completed.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Reason for rejection (if task was rejected).
    /// </summary>
    public string? RejectionReason { get; set; }

    /// <summary>
    /// Result of task execution (stored after completion).
    /// </summary>
    public string? ExecutionResult { get; set; }
}

/// <summary>
/// Represents a pending action that requires approval before execution.
/// </summary>
public class PendingAction
{
    /// <summary>
    /// Type of action (e.g., "SendMessage", "ShellCommand", "SystemChange").
    /// </summary>
    public required string ActionType { get; set; }

    /// <summary>
    /// Description of what this action will do.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Action-specific payload data as JSON (stored in DB; EF does not support Dictionary in JSON owned types).
    /// </summary>
    public string? PayloadJson { get; set; }

    /// <summary>
    /// Action-specific payload data as a dictionary (parsed from PayloadJson; not mapped to DB).
    /// </summary>
    [NotMapped]
    public Dictionary<string, string>? Payload
    {
        get => string.IsNullOrEmpty(PayloadJson) ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(PayloadJson);
        set => PayloadJson = value == null || value.Count == 0 ? null : JsonSerializer.Serialize(value);
    }

    /// <summary>
    /// Human-readable preview of what this action will do.
    /// </summary>
    public string? Preview { get; set; }

    /// <summary>
    /// Whether this action requires approval (always true for side effects).
    /// </summary>
    public bool RequiresApproval { get; set; } = true;
}
