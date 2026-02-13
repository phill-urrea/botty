using System.Text.Json;
using Botty.Core.Interfaces;
using Botty.Hooks.Models;
using Microsoft.Extensions.Logging;

namespace Botty.Hooks.Actions;

/// <summary>
/// Executes a skill tool by name with arguments from config (supports template substitution).
/// </summary>
public class ExecuteSkillAction : IHookAction
{
    public string Type => "execute_skill";
    private readonly ISkillRegistry _skillRegistry;
    private readonly ILogger<ExecuteSkillAction> _logger;

    public ExecuteSkillAction(ISkillRegistry skillRegistry, ILogger<ExecuteSkillAction> logger)
    {
        _skillRegistry = skillRegistry;
        _logger = logger;
    }

    public async Task<ActionResult> ExecuteAsync(JsonDocument config, HookContext context, CancellationToken ct = default)
    {
        var root = config.RootElement;
        var toolName = root.GetProperty("toolName").GetString();
        var arguments = root.TryGetProperty("arguments", out var argsEl) ? argsEl.GetString() ?? "{}" : "{}";
        if (string.IsNullOrEmpty(toolName))
            return new ActionResult { Success = false, Error = "Missing toolName in action config" };

        arguments = ActionHelpers.SubstituteVariables(arguments, context);
        var result = await _skillRegistry.ExecuteToolAsync(toolName, arguments, ct);
        if (!result.Success)
        {
            _logger.LogWarning("ExecuteSkill {Tool} failed: {Error}", toolName, result.Error);
            return new ActionResult { Success = false, Error = result.Error };
        }
        return new ActionResult { Success = true, Output = result.Result };
    }
}
