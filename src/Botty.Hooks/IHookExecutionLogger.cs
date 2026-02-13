using System.Text.Json;
using Botty.Hooks.Models;

namespace Botty.Hooks;

/// <summary>
/// Logs hook executions for audit and debugging.
/// </summary>
public interface IHookExecutionLogger
{
    Task LogAsync(Guid hookId, HookTrigger trigger, JsonElement? payload, HookResult result, CancellationToken ct = default);
}
