using Botty.Hooks.Models;

namespace Botty.Hooks.Executor;

/// <summary>
/// Executes a single hook and returns the result (used by registry).
/// </summary>
public interface IHookExecutor
{
    Task<HookResult> ExecuteAsync(IHook hook, HookContext context, CancellationToken ct = default);
}
