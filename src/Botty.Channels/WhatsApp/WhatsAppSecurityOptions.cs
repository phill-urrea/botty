namespace Botty.Channels.WhatsApp;

/// <summary>
/// Security configuration for WhatsApp channel access control.
/// </summary>
public class WhatsAppSecurityOptions
{
    /// <summary>
    /// Policy for group messages: "open", "allowlist", or "disabled".
    /// </summary>
    public string GroupPolicy { get; set; } = "open";

    /// <summary>
    /// Policy for direct messages: "pairing", "allowlist", "open", or "disabled".
    /// </summary>
    public string DmPolicy { get; set; } = "open";

    /// <summary>
    /// Allowed phone numbers or IDs. Use "*" as a wildcard to allow all.
    /// </summary>
    public List<string> AllowFrom { get; set; } = [];

    /// <summary>
    /// Per-group security configuration, keyed by group ID.
    /// </summary>
    public Dictionary<string, GroupConfig> Groups { get; set; } = new();
}

/// <summary>
/// Security configuration for a specific WhatsApp group.
/// </summary>
public class GroupConfig
{
    /// <summary>
    /// Whether the bot requires an @mention to respond in this group.
    /// </summary>
    public bool RequireMention { get; set; } = false;

    /// <summary>
    /// Access policy for this group: "open", "allowlist", "disabled".
    /// </summary>
    public string Policy { get; set; } = "open";
}
