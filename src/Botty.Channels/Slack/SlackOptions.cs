namespace Botty.Channels.Slack;

/// <summary>
/// Configuration options for Slack channel
/// </summary>
public class SlackOptions
{
    /// <summary>
    /// Whether the Slack channel is enabled
    /// </summary>
    public bool Enabled { get; set; } = false;
    
    /// <summary>
    /// Whether to use Socket Mode for real-time events
    /// </summary>
    public bool UseSocketMode { get; set; } = true;
    
    /// <summary>
    /// Timeout for API calls in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}
