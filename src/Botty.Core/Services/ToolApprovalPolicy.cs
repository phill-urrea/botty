using Botty.Core.Enums;

namespace Botty.Core.Services;

/// <summary>
/// Classifies tool calls that must go through explicit approval.
/// </summary>
public static class ToolApprovalPolicy
{
    private static readonly HashSet<string> SendMessageTools = new(StringComparer.Ordinal)
    {
        "gmail_send_message",
        "send_whatsapp_message",
        "channel_send_message"
    };

    private static readonly HashSet<string> ShellTools = new(StringComparer.Ordinal)
    {
        "shell_execute",
        "shell_script",
        "shell_write_file"
    };

    private static readonly HashSet<string> CalendarTools = new(StringComparer.Ordinal)
    {
        "calendar_create_event",
        "calendar_update_event",
        "calendar_delete_event"
    };

    private static readonly HashSet<string> SystemChangeTools = new(StringComparer.Ordinal)
    {
        "schedule_cron_job",
        "cancel_scheduled_task",
        "script_create",
        "script_edit",
        "script_delete"
    };

    /// <summary>
    /// Returns true when the tool must be approved, and emits task type for approval workflow.
    /// </summary>
    public static bool TryGetApprovalTaskType(string toolName, out TaskType taskType)
    {
        taskType = TaskType.General;
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return false;
        }

        if (SendMessageTools.Contains(toolName))
        {
            taskType = TaskType.SendMessage;
            return true;
        }

        if (ShellTools.Contains(toolName))
        {
            taskType = TaskType.ShellCommand;
            return true;
        }

        if (CalendarTools.Contains(toolName))
        {
            taskType = TaskType.CalendarChange;
            return true;
        }

        if (SystemChangeTools.Contains(toolName))
        {
            taskType = TaskType.SystemChange;
            return true;
        }

        return false;
    }
}
