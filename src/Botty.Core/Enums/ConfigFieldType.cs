namespace Botty.Core.Enums;

/// <summary>
/// Represents the type of a configuration field for skills.
/// </summary>
public enum ConfigFieldType
{
    /// <summary>
    /// Plain string value.
    /// </summary>
    String,

    /// <summary>
    /// Numeric value.
    /// </summary>
    Number,

    /// <summary>
    /// Boolean true/false value.
    /// </summary>
    Boolean,

    /// <summary>
    /// Secret value (always sensitive, e.g., API keys, passwords).
    /// </summary>
    Secret,

    /// <summary>
    /// OAuth token (always sensitive, triggers OAuth flow in UI).
    /// </summary>
    OAuth,

    /// <summary>
    /// Single selection from a list of options.
    /// </summary>
    Selection,

    /// <summary>
    /// Multiple selections from a list of options.
    /// </summary>
    MultiSelect,

    /// <summary>
    /// JSON object or array value.
    /// </summary>
    Json
}
