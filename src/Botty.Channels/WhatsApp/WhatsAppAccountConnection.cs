namespace Botty.Channels.WhatsApp;

/// <summary>
/// Encapsulates per-account WhatsApp connection state.
/// </summary>
public class WhatsAppAccountConnection : IDisposable
{
    /// <summary>
    /// Unique account identifier.
    /// </summary>
    public required string AccountId { get; init; }

    /// <summary>
    /// Per-account options (bridge URL, timeouts, security, etc.).
    /// </summary>
    public required WhatsAppAccountOptions Options { get; init; }

    /// <summary>
    /// HTTP client configured for this account's bridge.
    /// </summary>
    public HttpClient? HttpClient { get; set; }

    /// <summary>
    /// Phone number associated with this account (once connected).
    /// </summary>
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// Display name for this account.
    /// </summary>
    public string? AccountName { get; set; }

    /// <summary>
    /// Whether this account is currently connected.
    /// </summary>
    public bool IsConnected { get; set; }

    /// <summary>
    /// When this account connected.
    /// </summary>
    public DateTime? ConnectedSince { get; set; }

    /// <summary>
    /// Last error for this account.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Health check timer for this account.
    /// </summary>
    public Timer? HealthCheckTimer { get; set; }

    public void Dispose()
    {
        HealthCheckTimer?.Dispose();
        HttpClient?.Dispose();
        GC.SuppressFinalize(this);
    }
}
