namespace Botty.Channels.Telegram;

/// <summary>
/// Configuration options for Telegram channel
/// </summary>
public class TelegramOptions
{
    /// <summary>
    /// Whether the Telegram channel is enabled
    /// </summary>
    public bool Enabled { get; set; } = false;
    
    /// <summary>
    /// Polling timeout in seconds for receiving updates
    /// </summary>
    public int PollingTimeout { get; set; } = 30;
    
    /// <summary>
    /// Whether to drop pending updates on startup
    /// </summary>
    public bool DropPendingUpdates { get; set; } = false;
    
    /// <summary>
    /// Maximum number of updates to receive per poll
    /// </summary>
    public int UpdatesLimit { get; set; } = 100;
}
