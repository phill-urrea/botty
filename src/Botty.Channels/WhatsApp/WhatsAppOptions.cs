namespace Botty.Channels.WhatsApp;

/// <summary>
/// Configuration options for WhatsApp channel (supports multi-account).
/// </summary>
public class WhatsAppOptions
{
    /// <summary>
    /// Whether the WhatsApp channel is enabled
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// URL of the WhatsApp Node.js bridge (used for single-account / default account)
    /// </summary>
    public string BridgeUrl { get; set; } = "http://localhost:3001";

    /// <summary>
    /// Timeout for bridge API calls in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Whether to reconnect automatically on disconnection
    /// </summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>
    /// Interval for health checks in seconds
    /// </summary>
    public int HealthCheckIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Maximum characters per message chunk. 0 uses default (4000).
    /// </summary>
    public int ChunkLimit { get; set; } = 4000;

    /// <summary>
    /// Chunking mode: "length" or "newline".
    /// </summary>
    public string ChunkMode { get; set; } = "length";

    /// <summary>
    /// Security configuration for this WhatsApp channel (default account).
    /// </summary>
    public WhatsAppSecurityOptions Security { get; set; } = new();

    /// <summary>
    /// Default account ID to use when no account is specified. Default: "default".
    /// </summary>
    public string DefaultAccount { get; set; } = "default";

    /// <summary>
    /// Per-account configurations. Each key is an account ID.
    /// If empty, a single account is used with the top-level settings.
    /// </summary>
    public Dictionary<string, WhatsAppAccountOptions> Accounts { get; set; } = new();
}

/// <summary>
/// Per-account configuration for multi-account WhatsApp.
/// </summary>
public class WhatsAppAccountOptions
{
    /// <summary>
    /// URL of the WhatsApp bridge for this account.
    /// </summary>
    public string BridgeUrl { get; set; } = "http://localhost:3001";

    /// <summary>
    /// Timeout for bridge API calls in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Whether to reconnect automatically on disconnection.
    /// </summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>
    /// Maximum characters per message chunk.
    /// </summary>
    public int ChunkLimit { get; set; } = 4000;

    /// <summary>
    /// Chunking mode: "length" or "newline".
    /// </summary>
    public string ChunkMode { get; set; } = "length";

    /// <summary>
    /// Security configuration for this account.
    /// </summary>
    public WhatsAppSecurityOptions Security { get; set; } = new();

    /// <summary>
    /// Session path for wwebjs auth (used by bridge).
    /// </summary>
    public string SessionPath { get; set; } = "./.wwebjs_auth";
}
