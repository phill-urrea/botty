namespace Botty.Hooks.Models;

/// <summary>
/// Result of a hook execution.
/// </summary>
public class HookResult
{
    public bool Success { get; init; }
    public string? Output { get; init; }
    public string? Error { get; init; }
    public TimeSpan Duration { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}
