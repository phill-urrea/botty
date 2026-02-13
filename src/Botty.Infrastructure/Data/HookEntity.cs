using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace Botty.Infrastructure.Data;

/// <summary>
/// Persisted hook definition (maps to hooks table).
/// </summary>
public class HookEntity
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string Trigger { get; set; }
    public string? ConditionJson { get; set; }
    public required string ActionType { get; set; }
    public required string ActionConfigJson { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Log of a hook execution (maps to hook_executions table).
/// </summary>
public class HookExecutionEntity
{
    public Guid Id { get; set; }
    public Guid HookId { get; set; }
    public required string Trigger { get; set; }
    public string? PayloadJson { get; set; }
    public bool Success { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
    public int DurationMs { get; set; }
    public DateTime ExecutedAt { get; set; }
}
