namespace Botty.Core.Models;

/// <summary>
/// Represents the Soul.md configuration parsed into a structured format.
/// </summary>
public class SoulConfiguration
{
    /// <summary>
    /// Raw markdown content of Soul.md.
    /// </summary>
    public required string RawContent { get; set; }

    /// <summary>
    /// Assistant's name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Assistant's role description.
    /// </summary>
    public string? Role { get; set; }

    /// <summary>
    /// Primary directives for the assistant.
    /// </summary>
    public List<string> Directives { get; set; } = [];

    /// <summary>
    /// Tone and personality settings.
    /// </summary>
    public ToneSettings Tone { get; set; } = new();

    /// <summary>
    /// Boundaries and restrictions.
    /// </summary>
    public BoundarySettings Boundaries { get; set; } = new();

    /// <summary>
    /// Working hours configuration.
    /// </summary>
    public WorkingHoursSettings WorkingHours { get; set; } = new();

    /// <summary>
    /// Response templates.
    /// </summary>
    public Dictionary<string, string> ResponseTemplates { get; set; } = [];
}

/// <summary>
/// Tone and personality settings.
/// </summary>
public class ToneSettings
{
    /// <summary>
    /// Communication style (casual, professional, balanced).
    /// </summary>
    public string CommunicationStyle { get; set; } = "balanced";

    /// <summary>
    /// Humor level (none, subtle, playful).
    /// </summary>
    public string HumorLevel { get; set; } = "subtle";

    /// <summary>
    /// Verbosity level (concise, detailed, adaptive).
    /// </summary>
    public string Verbosity { get; set; } = "adaptive";

    /// <summary>
    /// Formality when interacting with others.
    /// </summary>
    public string FormalityWithOthers { get; set; } = "match their tone";
}

/// <summary>
/// Boundary settings for the assistant.
/// </summary>
public class BoundarySettings
{
    /// <summary>
    /// Topics the assistant should avoid.
    /// </summary>
    public List<string> TopicsToAvoid { get; set; } = [];

    /// <summary>
    /// Actions the assistant should never take autonomously.
    /// </summary>
    public List<string> NeverAutonomousActions { get; set; } = [];

    /// <summary>
    /// Information the assistant should never share.
    /// </summary>
    public List<string> NeverShareInfo { get; set; } = [];
}

/// <summary>
/// Working hours configuration.
/// </summary>
public class WorkingHoursSettings
{
    /// <summary>
    /// Active hours description (e.g., "8am-10pm").
    /// </summary>
    public string? ActiveHours { get; set; }

    /// <summary>
    /// Conditions when the assistant should be available regardless of hours.
    /// </summary>
    public string? UrgentOverride { get; set; }
}

/// <summary>
/// Represents a version of the Soul configuration.
/// </summary>
public class SoulVersion
{
    /// <summary>
    /// Unique identifier for this version.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The raw content of this version.
    /// </summary>
    public required string Content { get; set; }

    /// <summary>
    /// Who made this change ("user" or specific identifier).
    /// </summary>
    public string? ChangedBy { get; set; }

    /// <summary>
    /// When this version was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Whether this is the currently active version.
    /// </summary>
    public bool IsActive { get; set; }
}
