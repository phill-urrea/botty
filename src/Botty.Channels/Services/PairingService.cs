using System.Security.Cryptography;
using Botty.Infrastructure.Repositories;
using Microsoft.Extensions.Logging;

namespace Botty.Channels.Services;

/// <summary>
/// Manages DM pairing code generation and approval for channel access control.
/// </summary>
public class PairingService
{
    // No ambiguous characters: 0O1I removed
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 8;
    private const int MaxPendingPerChannel = 3;
    private const int TtlMinutes = 60;

    private readonly PairingRepository _repository;
    private readonly ILogger<PairingService> _logger;

    public PairingService(PairingRepository repository, ILogger<PairingService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Generates a pairing code for a sender on a channel.
    /// Returns null if max pending requests reached.
    /// </summary>
    public async Task<string?> GenerateCodeAsync(
        string channel, string senderId, CancellationToken ct = default)
    {
        var pendingCount = await _repository.CountPendingForChannelAsync(channel, ct);
        if (pendingCount >= MaxPendingPerChannel)
        {
            _logger.LogWarning("Max pending pairing requests ({Max}) reached for channel {Channel}",
                MaxPendingPerChannel, channel);
            return null;
        }

        var code = GenerateCode();
        await _repository.CreateRequestAsync(channel, senderId, code, TtlMinutes, ct);

        _logger.LogInformation("Generated pairing code {Code} for {SenderId} on {Channel}",
            code, senderId, channel);

        return code;
    }

    /// <summary>
    /// Approves a pairing code, adding the sender to the allow list.
    /// </summary>
    public async Task<bool> ApproveCodeAsync(
        string channel, string code, CancellationToken ct = default)
    {
        var request = await _repository.FindByCodeAsync(channel, code.ToUpperInvariant(), ct);
        if (request == null)
        {
            _logger.LogWarning("Pairing code {Code} not found or expired for channel {Channel}", code, channel);
            return false;
        }

        await _repository.AddToAllowListAsync(channel, request.SenderId, ct);
        await _repository.DeleteRequestAsync(request.Id, ct);

        _logger.LogInformation("Approved pairing for {SenderId} on {Channel} via code {Code}",
            request.SenderId, channel, code);

        return true;
    }

    /// <summary>
    /// Gets all pending pairing requests for a channel.
    /// </summary>
    public async Task<IEnumerable<ChannelPairingRequest>> GetPendingAsync(
        string channel, CancellationToken ct = default)
    {
        return await _repository.GetPendingAsync(channel, ct);
    }

    private static string GenerateCode()
    {
        Span<byte> bytes = stackalloc byte[CodeLength];
        RandomNumberGenerator.Fill(bytes);

        var code = new char[CodeLength];
        for (var i = 0; i < CodeLength; i++)
        {
            code[i] = CodeAlphabet[bytes[i] % CodeAlphabet.Length];
        }

        return new string(code);
    }
}
