using Botty.Infrastructure.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Botty.Channels.WhatsApp;

/// <summary>
/// Result of a security filter check.
/// </summary>
public enum SecurityDecision
{
    Allow,
    Deny,
    RequestPairing
}

/// <summary>
/// Filters incoming WhatsApp messages based on security policy.
/// </summary>
public class WhatsAppSecurityFilter
{
    private readonly WhatsAppSecurityOptions _options;
    private readonly PairingRepository _pairingRepo;
    private readonly ILogger<WhatsAppSecurityFilter> _logger;

    public WhatsAppSecurityFilter(
        IOptions<WhatsAppSecurityOptions> options,
        PairingRepository pairingRepo,
        ILogger<WhatsAppSecurityFilter> logger)
    {
        _options = options.Value;
        _pairingRepo = pairingRepo;
        _logger = logger;
    }

    /// <summary>
    /// Checks whether an incoming message should be allowed, denied, or requires pairing.
    /// </summary>
    public async Task<SecurityDecision> FilterIncomingAsync(
        IncomingMessage message, CancellationToken ct = default)
    {
        var isGroup = message.Metadata?.TryGetValue("isGroup", out var isGroupObj) == true
            && isGroupObj is true;

        if (isGroup)
        {
            return await CheckGroupPolicyAsync(message, ct);
        }

        return await CheckDmPolicyAsync(message, ct);
    }

    private async Task<SecurityDecision> CheckDmPolicyAsync(
        IncomingMessage message, CancellationToken ct)
    {
        var policy = _options.DmPolicy.ToLowerInvariant();

        return policy switch
        {
            "disabled" => SecurityDecision.Deny,
            "open" => SecurityDecision.Allow,
            "allowlist" => await CheckAllowListAsync(message.SenderId, ct),
            "pairing" => await CheckPairingAsync(message.SenderId, ct),
            _ => SecurityDecision.Allow
        };
    }

    private async Task<SecurityDecision> CheckGroupPolicyAsync(
        IncomingMessage message, CancellationToken ct)
    {
        var policy = _options.GroupPolicy.ToLowerInvariant();

        // Check per-group configuration
        var chatId = message.ChatId;
        if (_options.Groups.TryGetValue(chatId, out var groupConfig))
        {
            policy = groupConfig.Policy.ToLowerInvariant();
        }

        return policy switch
        {
            "disabled" => SecurityDecision.Deny,
            "open" => SecurityDecision.Allow,
            "allowlist" => await CheckAllowListAsync(message.SenderId, ct),
            _ => SecurityDecision.Allow
        };
    }

    private async Task<SecurityDecision> CheckAllowListAsync(string senderId, CancellationToken ct)
    {
        // Check global AllowFrom list
        if (_options.AllowFrom.Contains("*") || _options.AllowFrom.Contains(senderId))
            return SecurityDecision.Allow;

        // Check database allow list
        if (await _pairingRepo.IsAllowedAsync("whatsapp", senderId, ct))
            return SecurityDecision.Allow;

        _logger.LogInformation("Sender {SenderId} not in allow list, denying", senderId);
        return SecurityDecision.Deny;
    }

    private async Task<SecurityDecision> CheckPairingAsync(string senderId, CancellationToken ct)
    {
        // Check if already allowed
        if (_options.AllowFrom.Contains("*") || _options.AllowFrom.Contains(senderId))
            return SecurityDecision.Allow;

        if (await _pairingRepo.IsAllowedAsync("whatsapp", senderId, ct))
            return SecurityDecision.Allow;

        _logger.LogInformation("Sender {SenderId} requires pairing", senderId);
        return SecurityDecision.RequestPairing;
    }
}
