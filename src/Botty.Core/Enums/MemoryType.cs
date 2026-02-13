namespace Botty.Core.Enums;

/// <summary>
/// Represents the type of memory stored in the memory system.
/// </summary>
public enum MemoryType
{
    /// <summary>
    /// User preference (e.g., "prefers dark mode", "likes coffee").
    /// </summary>
    Preference,

    /// <summary>
    /// Project or ongoing work item.
    /// </summary>
    Project,

    /// <summary>
    /// Artifact or document reference.
    /// </summary>
    Artifact,

    /// <summary>
    /// Episodic memory of a specific event or conversation.
    /// </summary>
    Episode,

    /// <summary>
    /// Fact about the user or their life.
    /// </summary>
    Fact,

    /// <summary>
    /// Relationship information (people the user knows).
    /// </summary>
    Relationship
}
