namespace Botty.Cli.Models;

// === Channels ===

public class ChannelDto
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Description { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsConnected { get; set; }
    public string? AccountId { get; set; }
    public string? AccountName { get; set; }
    public DateTime? ConnectedSince { get; set; }
    public string? LastError { get; set; }
}

public class ChannelStatusDto
{
    public string ChannelId { get; set; } = "";
    public bool IsConnected { get; set; }
    public string? AccountId { get; set; }
    public string? AccountName { get; set; }
    public DateTime? ConnectedSince { get; set; }
    public string? Error { get; set; }
}

public class SendResultDto
{
    public bool Success { get; set; }
    public string? MessageId { get; set; }
    public string? Error { get; set; }
}

// === Kanban ===

public class KanbanBoardDto
{
    public List<KanbanTaskDto> ToDo { get; set; } = [];
    public List<KanbanTaskDto> InProgress { get; set; } = [];
    public List<KanbanTaskDto> NeedsApproval { get; set; } = [];
    public List<KanbanTaskDto> Done { get; set; } = [];
    public List<KanbanTaskDto> Cancelled { get; set; } = [];
}

public class KanbanTaskDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Lane { get; set; } = "";
    public string Assignee { get; set; } = "";
    public string Type { get; set; } = "";
    public string Priority { get; set; } = "";
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

// === Memory ===

public class MemorySearchResponse
{
    public List<MemoryDto> Memories { get; set; } = [];
}

public class MemoryDto
{
    public Guid Id { get; set; }
    public string Content { get; set; } = "";
    public string Type { get; set; } = "";
    public string Sensitivity { get; set; } = "";
    public decimal Confidence { get; set; }
    public string? Source { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class MemoryStatsResponse
{
    public int TotalCount { get; set; }
    public Dictionary<string, int> ByType { get; set; } = [];
}

// === Hooks ===

public class HookListDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string Trigger { get; set; } = "";
    public string ActionType { get; set; } = "";
    public bool IsEnabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class HookDetailDto : HookListDto
{
    public string? ConditionJson { get; set; }
    public string? ActionConfigJson { get; set; }
    public string? CreatedBy { get; set; }
}

public class HookExecutionDto
{
    public Guid Id { get; set; }
    public Guid HookId { get; set; }
    public string Trigger { get; set; } = "";
    public bool Success { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
    public long? DurationMs { get; set; }
    public DateTime ExecutedAt { get; set; }
}

public class HookResultDto
{
    public bool Success { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
}

// === Scheduler ===

public class ScheduledTaskDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string CronExpression { get; set; } = "";
    public DateTime NextRunAt { get; set; }
    public DateTime? LastRunAt { get; set; }
    public bool IsRecurring { get; set; }
    public int? MaxOccurrences { get; set; }
    public int OccurrenceCount { get; set; }
    public bool IsActive { get; set; }
    public string? Prompt { get; set; }
    public string? Timezone { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public TaskTemplateDto? TaskTemplate { get; set; }
}

public class TaskTemplateDto
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Type { get; set; } = "";
    public string Priority { get; set; } = "";
    public string Assignee { get; set; } = "";
    public bool RequiresApproval { get; set; }
}

// === Skills ===

public class SkillsListResponse
{
    public List<SkillDto> Skills { get; set; } = [];
    public int Count { get; set; }
}

public class SkillDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int ToolCount { get; set; }
    public bool IsConfigured { get; set; }
}

public class SkillDetailResponse
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsConfigured { get; set; }
    public List<string>? ValidationErrors { get; set; }
    public List<ToolDto> Tools { get; set; } = [];
}

public class ToolDto
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
}

public class SkillConfigResponse
{
    public string SkillId { get; set; } = "";
    public Dictionary<string, object?> Values { get; set; } = [];
}

public class ExecuteToolResponse
{
    public bool Success { get; set; }
    public string? Result { get; set; }
    public string? Error { get; set; }
}

// === Chat ===

public class ChatResponseDto
{
    public string Content { get; set; } = "";
    public Guid? ConversationId { get; set; }
    public string? FinishReason { get; set; }
    public UsageDto? Usage { get; set; }
    public int MemoriesInjected { get; set; }
}

public class UsageDto
{
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
}

// === WhatsApp ===

public class WhatsAppStatusDto
{
    public bool Connected { get; set; }
    public string? PhoneNumber { get; set; }
    public DateTime? LastSeen { get; set; }
}
