using Botty.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Botty.Infrastructure.Repositories;

/// <summary>
/// Entity for the channel_pairing_requests table.
/// </summary>
public class ChannelPairingRequest
{
    public Guid Id { get; set; }
    public required string Channel { get; set; }
    public required string SenderId { get; set; }
    public required string Code { get; set; }
    public string? Meta { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// Entity for the channel_allow_list table.
/// </summary>
public class ChannelAllowListEntry
{
    public Guid Id { get; set; }
    public required string Channel { get; set; }
    public required string Entry { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Repository for DM pairing requests and channel allow lists.
/// </summary>
public class PairingRepository
{
    private readonly BottyDbContext _context;
    private readonly ILogger<PairingRepository> _logger;

    public PairingRepository(BottyDbContext context, ILogger<PairingRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ChannelPairingRequest> CreateRequestAsync(
        string channel, string senderId, string code, int ttlMinutes = 60, CancellationToken ct = default)
    {
        var request = new ChannelPairingRequest
        {
            Channel = channel,
            SenderId = senderId,
            Code = code,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(ttlMinutes)
        };

        _context.ChannelPairingRequests.Add(request);
        await _context.SaveChangesAsync(ct);
        return request;
    }

    public async Task<ChannelPairingRequest?> FindByCodeAsync(
        string channel, string code, CancellationToken ct = default)
    {
        return await _context.ChannelPairingRequests
            .FirstOrDefaultAsync(r =>
                r.Channel == channel &&
                r.Code == code &&
                r.ExpiresAt > DateTime.UtcNow, ct);
    }

    public async Task<IEnumerable<ChannelPairingRequest>> GetPendingAsync(
        string channel, CancellationToken ct = default)
    {
        return await _context.ChannelPairingRequests
            .Where(r => r.Channel == channel && r.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<int> CountPendingForChannelAsync(
        string channel, CancellationToken ct = default)
    {
        return await _context.ChannelPairingRequests
            .CountAsync(r => r.Channel == channel && r.ExpiresAt > DateTime.UtcNow, ct);
    }

    public async Task DeleteRequestAsync(Guid id, CancellationToken ct = default)
    {
        var request = await _context.ChannelPairingRequests.FindAsync([id], ct);
        if (request != null)
        {
            _context.ChannelPairingRequests.Remove(request);
            await _context.SaveChangesAsync(ct);
        }
    }

    public async Task AddToAllowListAsync(
        string channel, string entry, CancellationToken ct = default)
    {
        var existing = await _context.ChannelAllowList
            .FirstOrDefaultAsync(a => a.Channel == channel && a.Entry == entry, ct);

        if (existing == null)
        {
            _context.ChannelAllowList.Add(new ChannelAllowListEntry
            {
                Channel = channel,
                Entry = entry,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync(ct);
        }
    }

    public async Task<bool> IsAllowedAsync(
        string channel, string entry, CancellationToken ct = default)
    {
        return await _context.ChannelAllowList
            .AnyAsync(a => a.Channel == channel && (a.Entry == entry || a.Entry == "*"), ct);
    }

    public async Task<IEnumerable<ChannelAllowListEntry>> GetAllowListAsync(
        string channel, CancellationToken ct = default)
    {
        return await _context.ChannelAllowList
            .Where(a => a.Channel == channel)
            .OrderBy(a => a.Entry)
            .ToListAsync(ct);
    }

    public async Task RemoveFromAllowListAsync(
        string channel, string entry, CancellationToken ct = default)
    {
        var existing = await _context.ChannelAllowList
            .FirstOrDefaultAsync(a => a.Channel == channel && a.Entry == entry, ct);

        if (existing != null)
        {
            _context.ChannelAllowList.Remove(existing);
            await _context.SaveChangesAsync(ct);
        }
    }
}
