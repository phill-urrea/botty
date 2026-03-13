using Botty.Core.Interfaces;
using Botty.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Botty.Tools.Base;

/// <summary>
/// Base class for tools providing common functionality.
/// </summary>
public abstract class BaseTool : ITool
{
    protected readonly ILogger Logger;
    protected ToolConfiguration? Configuration;
    protected bool IsInitialized;

    protected BaseTool(ILogger logger)
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
    public abstract ToolConfigSchema ConfigSchema { get; }

    /// <inheritdoc />
    public virtual async Task InitializeAsync(ToolConfiguration config, CancellationToken ct = default)
    {
        Configuration = config;
        await OnInitializeAsync(ct);
        IsInitialized = true;
        Logger.LogInformation("Tool {ToolId} initialized", Id);
    }

    /// <summary>
    /// Override to perform tool-specific initialization.
    /// </summary>
    protected virtual Task OnInitializeAsync(CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken ct = default)
    {
        if (!IsInitialized)
        {
            return ToolResult.Fail($"Tool {Id} is not initialized");
        }

        try
        {
            Logger.LogDebug("Executing tool {ToolId} function {ToolName}", Id, context.ToolName);
            var result = await OnExecuteAsync(context, ct);
            Logger.LogDebug("Tool {ToolId} function {ToolName} completed: {Success}",
                Id, context.ToolName, result.Success);
            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error executing tool {ToolId} function {ToolName}", Id, context.ToolName);
            return ToolResult.Fail($"Error executing {context.ToolName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Override to implement tool execution logic.
    /// </summary>
    protected abstract Task<ToolResult> OnExecuteAsync(ToolContext context, CancellationToken ct);

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
            Logger.LogWarning(ex, "Failed to parse arguments for tool {ToolId}", Id);
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
            throw new InvalidOperationException("Tool not initialized");
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
