using System.Text.Json;
using Botty.Hooks.Actions;
using Botty.Hooks.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Botty.Hooks;

/// <summary>
/// Hook defined by config (trigger, condition, action type + config) rather than code.
/// Creates a new scope per execution so scoped actions (e.g. IKanbanService) resolve correctly.
/// </summary>
public class DeclarativeHook : IHook
{
    public string Id { get; init; } = null!;
    public string Name { get; init; } = null!;
    public string? Description { get; init; }
    public HookTrigger Trigger { get; init; }
    public HookCondition? Condition { get; init; }
    public bool IsEnabled { get; set; } = true;
    public string? CreatedBy { get; init; }

    public required string ActionType { get; init; }
    public required JsonDocument ActionConfig { get; init; }

    private readonly IServiceScopeFactory _scopeFactory;

    public DeclarativeHook(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<HookResult> ExecuteAsync(HookContext context, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var action = scope.ServiceProvider.GetRequiredKeyedService<IHookAction>(ActionType);
        var result = await action.ExecuteAsync(ActionConfig, context, ct);
        return new HookResult
        {
            Success = result.Success,
            Output = result.Output,
            Error = result.Error
        };
    }
}
