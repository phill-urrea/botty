using Botty.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Botty.Tools.Registry;

/// <summary>
/// Registry for managing tools.
/// </summary>
public class ToolRegistry : IToolRegistry
{
    private readonly ConcurrentDictionary<string, ITool> _tools = new();
    private readonly IToolConfigService _configService;
    private readonly ILogger<ToolRegistry> _logger;

    public ToolRegistry(
        IToolConfigService configService,
        ILogger<ToolRegistry> logger)
    {
        _configService = configService;
        _logger = logger;
    }

    /// <inheritdoc />
    public void Register(ITool tool)
    {
        if (_tools.TryAdd(tool.Id, tool))
        {
            _logger.LogInformation("Registered tool: {ToolId} ({ToolName})", tool.Id, tool.Name);
        }
        else
        {
            _logger.LogWarning("Tool {ToolId} is already registered", tool.Id);
        }
    }

    /// <inheritdoc />
    public ITool? Get(string id)
    {
        return _tools.TryGetValue(id, out var tool) ? tool : null;
    }

    /// <inheritdoc />
    public IEnumerable<ITool> GetAll()
    {
        return _tools.Values;
    }

    /// <inheritdoc />
    public async Task<bool> IsConfiguredAsync(string toolId, CancellationToken ct = default)
    {
        var tool = Get(toolId);
        if (tool == null)
        {
            return false;
        }

        var validation = await _configService.ValidateConfigAsync(toolId, ct);
        return validation.IsValid;
    }

    /// <summary>
    /// Initializes all registered tools with their configurations.
    /// </summary>
    public async Task InitializeAllAsync(CancellationToken ct = default)
    {
        foreach (var tool in _tools.Values)
        {
            try
            {
                var config = await _configService.GetConfigAsync(tool.Id, ct);
                var validation = await _configService.ValidateConfigAsync(tool.Id, ct);

                if (validation.IsValid)
                {
                    await tool.InitializeAsync(config, ct);
                    _logger.LogInformation("Initialized tool: {ToolId}", tool.Id);
                }
                else
                {
                    _logger.LogWarning("Tool {ToolId} not fully configured: {Errors}",
                        tool.Id,
                        string.Join(", ", validation.Errors.Select(e => e.Message)));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize tool: {ToolId}", tool.Id);
            }
        }
    }

    /// <summary>
    /// Gets all tools from all initialized tools.
    /// </summary>
    public IEnumerable<LlmTool> GetAllTools()
    {
        return _tools.Values.SelectMany(s => s.GetTools());
    }

    /// <summary>
    /// Executes a tool call by finding the appropriate tool.
    /// </summary>
    public async Task<ToolResult> ExecuteToolAsync(string toolName, string arguments, CancellationToken ct = default)
    {
        return await ExecuteToolAsync(toolName, arguments, null, null, null, ct);
    }

    /// <summary>
    /// Executes a tool call by finding the appropriate tool with explicit context metadata.
    /// </summary>
    public async Task<ToolResult> ExecuteToolAsync(
        string toolName,
        string arguments,
        Guid? conversationId,
        Guid? taskId,
        Guid? userId,
        CancellationToken ct = default)
    {
        foreach (var registeredTool in _tools.Values)
        {
            var toolDefs = registeredTool.GetTools();
            if (toolDefs.Any(t => t.Name == toolName))
            {
                var context = new ToolContext
                {
                    ToolName = toolName,
                    Arguments = arguments,
                    ConversationId = conversationId,
                    TaskId = taskId,
                    UserId = userId
                };
                return await registeredTool.ExecuteAsync(context, ct);
            }
        }

        return ToolResult.Fail($"No tool found for tool name: {toolName}");
    }
}
