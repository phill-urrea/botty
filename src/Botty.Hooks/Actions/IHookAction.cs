using System.Text.Json;
using Botty.Hooks.Models;

namespace Botty.Hooks.Actions;

/// <summary>
/// Reusable action type that a declarative hook can invoke.
/// </summary>
public interface IHookAction
{
    string Type { get; }
    Task<ActionResult> ExecuteAsync(JsonDocument config, HookContext context, CancellationToken ct = default);
}

/// <summary>
/// Result of an action execution.
/// </summary>
public class ActionResult
{
    public bool Success { get; init; }
    public string? Output { get; init; }
    public string? Error { get; init; }
}
