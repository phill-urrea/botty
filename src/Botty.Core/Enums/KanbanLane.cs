namespace Botty.Core.Enums;

/// <summary>
/// Represents the lanes in the Kanban workflow board.
/// </summary>
public enum KanbanLane
{
    /// <summary>
    /// Tasks waiting to be picked up by user or assistant.
    /// </summary>
    ToDo,

    /// <summary>
    /// Tasks currently being worked on.
    /// </summary>
    InProgress,

    /// <summary>
    /// Completed work awaiting user approval before execution.
    /// </summary>
    NeedsApproval,

    /// <summary>
    /// Approved and executed tasks.
    /// </summary>
    Done,

    /// <summary>
    /// Cancelled or archived tasks.
    /// </summary>
    Cancelled
}
