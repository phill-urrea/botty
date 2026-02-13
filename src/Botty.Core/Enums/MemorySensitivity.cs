namespace Botty.Core.Enums;

/// <summary>
/// Represents the sensitivity level of a memory.
/// </summary>
public enum MemorySensitivity
{
    /// <summary>
    /// Public information, can be shared freely.
    /// </summary>
    Public,

    /// <summary>
    /// Private information, should not be shared externally.
    /// </summary>
    Private,

    /// <summary>
    /// Sensitive information requiring extra protection.
    /// </summary>
    Sensitive
}
