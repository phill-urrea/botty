using System.Collections.Concurrent;
using System.Text.Json;
using Botty.Hooks.Executor;
using Botty.Hooks.Models;
using Microsoft.Extensions.Logging;

namespace Botty.Hooks.Registry;

/// <summary>
/// In-memory hook registry with event publishing.
/// </summary>
public class HookRegistry : IHookRegistry
{
    private readonly ConcurrentDictionary<string, IHook> _hooks = new();
    private readonly ConcurrentDictionary<HookTrigger, List<IHook>> _byTrigger = new();
    private readonly IHookExecutor _executor;
    private readonly ILogger<HookRegistry> _logger;
    private readonly IHookExecutionLogger? _executionLogger;

    public HookRegistry(IHookExecutor executor, ILogger<HookRegistry> logger, IHookExecutionLogger? executionLogger = null)
    {
        _executor = executor;
        _logger = logger;
        _executionLogger = executionLogger;
        foreach (HookTrigger t in Enum.GetValues(typeof(HookTrigger)))
            _byTrigger[t] = [];
    }

    public void Register(IHook hook)
    {
        _hooks[hook.Id] = hook;
        var list = _byTrigger.GetOrAdd(hook.Trigger, _ => []);
        lock (list)
        {
            if (!list.Contains(hook))
                list.Add(hook);
        }
        _logger.LogInformation("Registered hook {HookId} ({Name}) for trigger {Trigger}", hook.Id, hook.Name, hook.Trigger);
    }

    public void Unregister(string hookId)
    {
        if (_hooks.TryRemove(hookId, out var hook))
        {
            if (_byTrigger.TryGetValue(hook.Trigger, out var list))
            {
                lock (list)
                    list.RemoveAll(h => h.Id == hookId);
            }
            _logger.LogInformation("Unregistered hook {HookId}", hookId);
        }
    }

    public IHook? GetHook(string hookId) => _hooks.TryGetValue(hookId, out var h) ? h : null;

    public IEnumerable<IHook> GetHooksForTrigger(HookTrigger trigger) =>
        _byTrigger.TryGetValue(trigger, out var list) ? list.Where(h => h.IsEnabled).ToList() : [];

    public IEnumerable<IHook> GetAllHooks() => _hooks.Values.ToList();

    public async Task<IEnumerable<HookResult>> TriggerAsync(HookTrigger trigger, HookContext context, CancellationToken ct = default)
    {
        var hooks = GetHooksForTrigger(trigger).ToList();
        if (hooks.Count == 0)
            return [];

        var results = new List<HookResult>();
        foreach (var hook in hooks)
        {
            if (hook.Condition != null && !hook.Condition.Evaluate(context))
                continue;
            var result = await _executor.ExecuteAsync(hook, context, ct);
            results.Add(result);
            if (_executionLogger != null && Guid.TryParse(hook.Id, out var hookId))
                await _executionLogger.LogAsync(hookId, trigger, context.Payload.RootElement.Clone(), result, ct);
        }
        return results;
    }

    public async Task PublishEventAsync(HookTrigger trigger, object payload, CancellationToken ct = default)
    {
        var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var context = new HookContext
        {
            Trigger = trigger,
            Timestamp = DateTime.UtcNow,
            Payload = doc
        };
        var results = await TriggerAsync(trigger, context, ct);
        foreach (var r in results.Where(x => !x.Success))
            _logger.LogWarning("Hook failed: {Error}", r.Error);
    }
}
