using Botty.Core.Models;

namespace Botty.Core.Interfaces;

/// <summary>
/// Interface for skills that extend the assistant's capabilities.
/// </summary>
public interface ISkill
{
    /// <summary>
    /// Unique identifier for the skill.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Display name of the skill.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Description of what the skill does.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Configuration schema for this skill.
    /// </summary>
    SkillConfigSchema ConfigSchema { get; }

    /// <summary>
    /// Initializes the skill with its configuration.
    /// </summary>
    Task InitializeAsync(SkillConfiguration config, CancellationToken ct = default);

    /// <summary>
    /// Executes a skill action.
    /// </summary>
    Task<SkillResult> ExecuteAsync(SkillContext context, CancellationToken ct = default);

    /// <summary>
    /// Gets the tools this skill provides to the LLM.
    /// </summary>
    IEnumerable<LlmTool> GetTools();
}

/// <summary>
/// Context for skill execution.
/// </summary>
public class SkillContext
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
    /// The conversation this skill is being used in.
    /// </summary>
    public Guid? ConversationId { get; set; }

    /// <summary>
    /// The task this skill execution is for (if any).
    /// </summary>
    public Guid? TaskId { get; set; }
}

/// <summary>
/// Result of a skill execution.
/// </summary>
public class SkillResult
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
    public static SkillResult Ok(string result) => new() { Success = true, Result = result };

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static SkillResult Fail(string error) => new() { Success = false, Error = error };
}

/// <summary>
/// Registry for managing skills.
/// </summary>
public interface ISkillRegistry
{
    /// <summary>
    /// Registers a skill.
    /// </summary>
    void Register(ISkill skill);

    /// <summary>
    /// Gets a skill by ID.
    /// </summary>
    ISkill? Get(string id);

    /// <summary>
    /// Gets all registered skills.
    /// </summary>
    IEnumerable<ISkill> GetAll();

    /// <summary>
    /// Checks if a skill is fully configured.
    /// </summary>
    Task<bool> IsConfiguredAsync(string skillId, CancellationToken ct = default);

    /// <summary>
    /// Executes a tool by name with the given arguments (used by hooks and assistants).
    /// </summary>
    Task<SkillResult> ExecuteToolAsync(string toolName, string arguments, CancellationToken ct = default);
}
