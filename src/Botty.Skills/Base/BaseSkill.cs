using Botty.Core.Interfaces;
using Botty.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Botty.Skills.Base;

/// <summary>
/// Base class for skills providing common functionality.
/// </summary>
public abstract class BaseSkill : ISkill
{
    protected readonly ILogger Logger;
    protected SkillConfiguration? Configuration;
    protected bool IsInitialized;

    protected BaseSkill(ILogger logger)
    {
        Logger = logger;
    }

    /// <inheritdoc />
    public abstract string Id { get; }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract string Description { get; }

    /// <inheritdoc />
    public abstract SkillConfigSchema ConfigSchema { get; }

    /// <inheritdoc />
    public virtual async Task InitializeAsync(SkillConfiguration config, CancellationToken ct = default)
    {
        Configuration = config;
        await OnInitializeAsync(ct);
        IsInitialized = true;
        Logger.LogInformation("Skill {SkillId} initialized", Id);
    }

    /// <summary>
    /// Override to perform skill-specific initialization.
    /// </summary>
    protected virtual Task OnInitializeAsync(CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public async Task<SkillResult> ExecuteAsync(SkillContext context, CancellationToken ct = default)
    {
        if (!IsInitialized)
        {
            return SkillResult.Fail($"Skill {Id} is not initialized");
        }

        try
        {
            Logger.LogDebug("Executing skill {SkillId} tool {ToolName}", Id, context.ToolName);
            var result = await OnExecuteAsync(context, ct);
            Logger.LogDebug("Skill {SkillId} tool {ToolName} completed: {Success}", 
                Id, context.ToolName, result.Success);
            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error executing skill {SkillId} tool {ToolName}", Id, context.ToolName);
            return SkillResult.Fail($"Error executing {context.ToolName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Override to implement skill execution logic.
    /// </summary>
    protected abstract Task<SkillResult> OnExecuteAsync(SkillContext context, CancellationToken ct);

    /// <inheritdoc />
    public abstract IEnumerable<LlmTool> GetTools();

    /// <summary>
    /// Helper to parse JSON arguments.
    /// </summary>
    protected T? ParseArguments<T>(string arguments) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(arguments, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            Logger.LogWarning(ex, "Failed to parse arguments for skill {SkillId}", Id);
            return null;
        }
    }

    /// <summary>
    /// Helper to serialize result to JSON.
    /// </summary>
    protected static string ToJson(object obj)
    {
        return JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });
    }

    /// <summary>
    /// Gets a required configuration value.
    /// </summary>
    protected string GetRequiredConfig(string key)
    {
        if (Configuration == null)
            throw new InvalidOperationException("Skill not initialized");
        return Configuration.GetRequiredValue(key);
    }

    /// <summary>
    /// Gets an optional configuration value.
    /// </summary>
    protected string? GetConfig(string key)
    {
        return Configuration?.GetValue(key);
    }
}
