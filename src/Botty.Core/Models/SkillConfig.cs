using Botty.Core.Enums;

namespace Botty.Core.Models;

/// <summary>
/// Represents a skill configuration schema.
/// </summary>
public class SkillConfigSchema
{
    /// <summary>
    /// The skill this schema belongs to.
    /// </summary>
    public required string SkillId { get; set; }

    /// <summary>
    /// Configuration fields for the skill.
    /// </summary>
    public List<ConfigField> Fields { get; set; } = [];
}

/// <summary>
/// Represents a single configuration field.
/// </summary>
public class ConfigField
{
    /// <summary>
    /// Unique key for this field.
    /// </summary>
    public required string Key { get; set; }

    /// <summary>
    /// Display label for the field.
    /// </summary>
    public required string Label { get; set; }

    /// <summary>
    /// Description of what this field is for.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Type of the field.
    /// </summary>
    public ConfigFieldType Type { get; set; } = ConfigFieldType.String;

    /// <summary>
    /// Whether this field contains sensitive data (stored in secret store).
    /// </summary>
    public bool IsSensitive { get; set; }

    /// <summary>
    /// Whether this field is required.
    /// </summary>
    public bool IsRequired { get; set; }

    /// <summary>
    /// Default value for the field.
    /// </summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// Regex pattern for validation.
    /// </summary>
    public string? ValidationRegex { get; set; }

    /// <summary>
    /// Options for Selection/MultiSelect fields.
    /// </summary>
    public List<SelectOption>? Options { get; set; }
}

/// <summary>
/// Represents an option for selection fields.
/// </summary>
public class SelectOption
{
    /// <summary>
    /// Value of the option.
    /// </summary>
    public required string Value { get; set; }

    /// <summary>
    /// Display label for the option.
    /// </summary>
    public required string Label { get; set; }
}

/// <summary>
/// Represents a stored skill configuration value.
/// </summary>
public class SkillConfigValue
{
    /// <summary>
    /// Unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The skill this config belongs to.
    /// </summary>
    public required string SkillId { get; set; }

    /// <summary>
    /// Configuration key.
    /// </summary>
    public required string Key { get; set; }

    /// <summary>
    /// Value (null if sensitive, actual value in secret store).
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// Whether this is a sensitive value (stored in secret store).
    /// </summary>
    public bool IsSensitive { get; set; }

    /// <summary>
    /// When this config was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Tracks references to secrets in the secret store.
/// </summary>
public class SecretReference
{
    /// <summary>
    /// Unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Category of secret ("skill", "system", "oauth").
    /// </summary>
    public required string Category { get; set; }

    /// <summary>
    /// Reference ID (skill_id for skills, "anthropic" for system, etc.).
    /// </summary>
    public required string ReferenceId { get; set; }

    /// <summary>
    /// Key within the reference.
    /// </summary>
    public required string Key { get; set; }

    /// <summary>
    /// Full path to the secret in the secret store.
    /// </summary>
    public required string SecretPath { get; set; }

    /// <summary>
    /// When this reference was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Represents a complete skill configuration with values.
/// </summary>
public class SkillConfiguration
{
    /// <summary>
    /// The skill ID.
    /// </summary>
    public required string SkillId { get; set; }

    /// <summary>
    /// Configuration values keyed by field key.
    /// </summary>
    public Dictionary<string, string?> Values { get; set; } = [];

    /// <summary>
    /// Gets a configuration value.
    /// </summary>
    public string? GetValue(string key) => Values.TryGetValue(key, out var value) ? value : null;

    /// <summary>
    /// Gets a required configuration value.
    /// </summary>
    public string GetRequiredValue(string key) =>
        Values.TryGetValue(key, out var value) && value is not null
            ? value
            : throw new InvalidOperationException($"Required configuration '{key}' not found for skill '{SkillId}'");
}
