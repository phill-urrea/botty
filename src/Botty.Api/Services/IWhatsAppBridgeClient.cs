namespace Botty.Api.Services;

/// <summary>
/// Client for the WhatsApp bridge HTTP API (status and QR).
/// </summary>
public interface IWhatsAppBridgeClient
{
    /// <summary>
    /// Gets connection status from the bridge. Returns disconnected when bridge is unreachable or not configured.
    /// </summary>
    Task<BridgeStatusResult> GetStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets QR code image (data URL) from the bridge. Returns empty when none available or bridge unreachable.
    /// </summary>
    Task<string> GetQrImageAsync(CancellationToken ct = default);
}

/// <summary>
/// Result of a bridge status call; used to map to WhatsAppStatusDto.
/// </summary>
public sealed class BridgeStatusResult
{
    public bool Connected { get; init; }
    public string? PhoneNumber { get; init; }
    public string? LastSeen { get; init; }
}
