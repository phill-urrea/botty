namespace Botty.Hooks.Models;

/// <summary>
/// Event types that can trigger hooks.
/// </summary>
public enum HookTrigger
{
    MessageReceived,
    MessageSent,
    MessageFailed,

    TaskCreated,
    TaskMoved,
    TaskApproved,
    TaskRejected,
    TaskCompleted,

    ScheduleTrigger,

    SkillExecuted,
    SkillFailed,

    MemoryCreated,
    MemoryUpdated,
    MemoryDeleted,

    SystemStartup,
    SystemShutdown,
    ChannelConnected,
    ChannelDisconnected,

    WebhookReceived,
    GmailNotification,

    Custom
}
