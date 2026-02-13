using Botty.Hooks.Models;

namespace Botty.Hooks;

/// <summary>
/// A hook that runs when a matching event occurs.
/// </summary>
public interface IHook
{
    string Id { get; }
    string Name { get; }
    string? Description { get; }

    HookTrigger Trigger { get; }
    HookCondition? Condition { get; }

    Task<HookResult> ExecuteAsync(HookContext context, CancellationToken ct = default);

    bool IsEnabled { get; }
    string? CreatedBy { get; }
}
