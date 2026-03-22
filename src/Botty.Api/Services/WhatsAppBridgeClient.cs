using System.Net.Http.Json;
using System.Text.Json;

namespace Botty.Api.Services;

/// <summary>
/// HTTP client for the WhatsApp bridge; calls GET /status and GET /qr/image.
/// Returns disconnected/empty when bridge URL is not configured or requests fail.
/// </summary>
public sealed class WhatsAppBridgeClient : IWhatsAppBridgeClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WhatsAppBridgeClient> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public WhatsAppBridgeClient(HttpClient httpClient, ILogger<WhatsAppBridgeClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<BridgeStatusResult> GetStatusAsync(CancellationToken ct = default)
    {
        // #region agent log
        _logger.LogWarning("[DEBUG-6a3d7a] WhatsAppBridgeClient.GetStatusAsync BaseAddress={BaseAddress}", _httpClient.BaseAddress?.ToString() ?? "(null)");
        // #endregion
        if (_httpClient.BaseAddress is null)
            return Disconnected();

        try
        {
            var response = await _httpClient.GetAsync("status", ct);
            if (!response.IsSuccessStatusCode)
                return Disconnected();

            var dto = await response.Content.ReadFromJsonAsync<BridgeStatusResponse>(JsonOptions, ct);
            if (dto is null)
                return Disconnected();

            return new BridgeStatusResult
            {
                Connected = dto.IsReady,
                PhoneNumber = dto.PhoneNumber,
                LastSeen = dto.ConnectedAt,
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "WhatsApp bridge status request failed");
            return Disconnected();
        }
    }

    /// <inheritdoc />
    public async Task<string> GetQrImageAsync(CancellationToken ct = default)
    {
        if (_httpClient.BaseAddress is null)
            return string.Empty;

        try
        {
            var response = await _httpClient.GetAsync("qr/image", ct);
            if (!response.IsSuccessStatusCode)
                return string.Empty;

            var dto = await response.Content.ReadFromJsonAsync<BridgeQrImageResponse>(JsonOptions, ct);
            return string.IsNullOrEmpty(dto?.Image) ? string.Empty : dto.Image;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "WhatsApp bridge QR image request failed");
            return string.Empty;
        }
    }

    private static BridgeStatusResult Disconnected() => new()
    {
        Connected = false,
        PhoneNumber = null,
        LastSeen = null,
    };

    private sealed class BridgeStatusResponse
    {
        public string? State { get; set; }
        public bool IsReady { get; set; }
        public string? PhoneNumber { get; set; }
        public string? ConnectedAt { get; set; }
    }

    private sealed class BridgeQrImageResponse
    {
        public string? Image { get; set; }
        public string? State { get; set; }
    }
}
