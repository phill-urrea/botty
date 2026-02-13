namespace Botty.Core.Enums;

/// <summary>
/// Represents the type of task in the Kanban workflow.
/// </summary>
public enum TaskType
{
    /// <summary>
    /// General purpose task.
    /// </summary>
    General,

    /// <summary>
    /// Task to send a message (WhatsApp, email, etc.).
    /// </summary>
    SendMessage,

    /// <summary>
    /// Task involving system configuration changes.
    /// </summary>
    SystemChange,

    /// <summary>
    /// Task to execute a skill action.
    /// </summary>
    SkillExecution,

    /// <summary>
    /// Task to create a new skill.
    /// </summary>
    NewSkillCreation,

    /// <summary>
    /// Task to execute a shell command.
    /// </summary>
    ShellCommand,

    /// <summary>
    /// Task to modify memories.
    /// </summary>
    MemoryModification,

    /// <summary>
    /// Task to create or modify calendar events.
    /// </summary>
    CalendarChange
}
