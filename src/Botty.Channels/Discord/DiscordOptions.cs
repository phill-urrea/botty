namespace Botty.Channels.Discord;

/// <summary>
/// Configuration options for Discord channel
/// </summary>
public class DiscordOptions
{
    /// <summary>
    /// Whether the Discord channel is enabled
    /// </summary>
    public bool Enabled { get; set; } = false;
    
    /// <summary>
    /// Gateway intents to request
    /// </summary>
    public string[] GatewayIntents { get; set; } = ["Guilds", "GuildMessages", "DirectMessages", "MessageContent"];
    
    /// <summary>
    /// Timeout for API calls in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}
