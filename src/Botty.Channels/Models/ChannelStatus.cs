namespace Botty.Channels;

/// <summary>
/// Current status of a channel connection
/// </summary>
public record ChannelStatus(
    bool IsConnected,
    string? AccountId,
    string? AccountName,
    DateTime? ConnectedSince,
    string? Error
)
{
    /// <summary>
    /// Create a connected status
    /// </summary>
    public static ChannelStatus Connected(string? accountId = null, string? accountName = null) =>
        new(true, accountId, accountName, DateTime.UtcNow, null);
    
    /// <summary>
    /// Create a disconnected status
    /// </summary>
    public static ChannelStatus Disconnected(string? error = null) =>
        new(false, null, null, null, error);
    
    /// <summary>
    /// Create an error status
    /// </summary>
    public static ChannelStatus WithError(string error) =>
        new(false, null, null, null, error);
}
