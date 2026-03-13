using Botty.Core.Models;

namespace Botty.Core.Interfaces;

/// <summary>
/// Interface for tools that extend the assistant's capabilities.
/// </summary>
public interface ITool
{
    /// <summary>
    /// Unique identifier for the tool.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Display name of the tool.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Description of what the tool does.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Configuration schema for this tool.
    /// </summary>
    ToolConfigSchema ConfigSchema { get; }

    /// <summary>
    /// Initializes the tool with its configuration.
    /// </summary>
    Task InitializeAsync(ToolConfiguration config, CancellationToken ct = default);

    /// <summary>
    /// Executes a tool action.
    /// </summary>
    Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken ct = default);

    /// <summary>
    /// Gets the tools this tool provides to the LLM.
    /// </summary>
    IEnumerable<LlmTool> GetTools();
}

/// <summary>
/// Context for tool execution.
/// </summary>
public class ToolContext
{
    /// <summary>
    /// The tool being called.
    /// </summary>
    public required string ToolName { get; set; }

    /// <summary>
    /// Arguments for the tool call (JSON).
    /// </summary>
    public required string Arguments { get; set; }

    /// <summary>
    /// The conversation this tool is being used in.
    /// </summary>
    public Guid? ConversationId { get; set; }

    /// <summary>
    /// The task this tool execution is for (if any).
    /// </summary>
    public Guid? TaskId { get; set; }

    /// <summary>
    /// User id associated with the current execution context.
    /// </summary>
    public Guid? UserId { get; set; }
}

/// <summary>
/// Result of a tool execution.
/// </summary>
public class ToolResult
{
    /// <summary>
    /// Whether the execution was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Result data (to return to LLM).
    /// </summary>
    public string? Result { get; set; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static ToolResult Ok(string result) => new() { Success = true, Result = result };

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static ToolResult Fail(string error) => new() { Success = false, Error = error };
}

/// <summary>
/// Registry for managing tools.
/// </summary>
public interface IToolRegistry
{
    /// <summary>
    /// Registers a tool.
    /// </summary>
    void Register(ITool tool);

    /// <summary>
    /// Gets a tool by ID.
    /// </summary>
    ITool? Get(string id);

    /// <summary>
    /// Gets all registered tools.
    /// </summary>
    IEnumerable<ITool> GetAll();

    /// <summary>
    /// Checks if a tool is fully configured.
    /// </summary>
    Task<bool> IsConfiguredAsync(string toolId, CancellationToken ct = default);

    /// <summary>
    /// Executes a tool by name with the given arguments (used by hooks and assistants).
    /// </summary>
    Task<ToolResult> ExecuteToolAsync(string toolName, string arguments, CancellationToken ct = default);

    /// <summary>
    /// Executes a tool by name with explicit context metadata.
    /// </summary>
    Task<ToolResult> ExecuteToolAsync(
        string toolName,
        string arguments,
        Guid? conversationId,
        Guid? taskId,
        Guid? userId,
        CancellationToken ct = default);
}
