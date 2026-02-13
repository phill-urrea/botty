using Botty.Core.Models;

namespace Botty.Core.Interfaces;

/// <summary>
/// Service interface for managing skill configurations.
/// </summary>
public interface ISkillConfigService
{
    /// <summary>
    /// Gets the configuration schema for a skill.
    /// </summary>
    Task<SkillConfigSchema> GetSchemaAsync(string skillId, CancellationToken ct = default);

    /// <summary>
    /// Gets the complete configuration for a skill (merges DB + secrets).
    /// </summary>
    Task<SkillConfiguration> GetConfigAsync(string skillId, CancellationToken ct = default);

    /// <summary>
    /// Sets a single configuration value (routes to DB or secret store based on sensitivity).
    /// </summary>
    Task SetConfigValueAsync(string skillId, string key, string value, CancellationToken ct = default);

    /// <summary>
    /// Updates multiple configuration values at once.
    /// </summary>
    Task UpdateConfigAsync(string skillId, Dictionary<string, string> values, CancellationToken ct = default);

    /// <summary>
    /// Validates that all required configuration is set.
    /// </summary>
    Task<ConfigValidationResult> ValidateConfigAsync(string skillId, CancellationToken ct = default);

    /// <summary>
    /// Deletes a configuration value.
    /// </summary>
    Task DeleteConfigValueAsync(string skillId, string key, CancellationToken ct = default);

    /// <summary>
    /// Checks if a configuration value exists (without revealing the value).
    /// </summary>
    Task<bool> HasValueAsync(string skillId, string key, CancellationToken ct = default);
}

/// <summary>
/// Result of configuration validation.
/// </summary>
public class ConfigValidationResult
{
    /// <summary>
    /// Whether the configuration is valid.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// List of validation errors.
    /// </summary>
    public List<ConfigValidationError> Errors { get; set; } = [];
}

/// <summary>
/// A single validation error.
/// </summary>
public class ConfigValidationError
{
    /// <summary>
    /// The field that has an error.
    /// </summary>
    public required string Field { get; set; }

    /// <summary>
    /// Error message.
    /// </summary>
    public required string Message { get; set; }
}
