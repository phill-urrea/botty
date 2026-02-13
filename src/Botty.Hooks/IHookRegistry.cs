using Botty.Hooks.Models;

namespace Botty.Hooks;

/// <summary>
/// Registry for hooks and event publishing.
/// </summary>
public interface IHookRegistry
{
    void Register(IHook hook);
    void Unregister(string hookId);

    IHook? GetHook(string hookId);
    IEnumerable<IHook> GetHooksForTrigger(HookTrigger trigger);
    IEnumerable<IHook> GetAllHooks();

    Task<IEnumerable<HookResult>> TriggerAsync(
        HookTrigger trigger,
        HookContext context,
        CancellationToken ct = default);

    Task PublishEventAsync(HookTrigger trigger, object payload, CancellationToken ct = default);
}
