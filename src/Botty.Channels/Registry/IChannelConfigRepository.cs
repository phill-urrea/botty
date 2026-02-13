namespace Botty.Channels.Registry;

/// <summary>
/// Repository for channel configurations
/// </summary>
public interface IChannelConfigRepository
{
    Task<ChannelConfigEntity?> GetByChannelIdAsync(string channelId, CancellationToken ct = default);
    Task<IEnumerable<ChannelConfigEntity>> GetAllAsync(CancellationToken ct = default);
    Task<ChannelConfigEntity> UpsertAsync(ChannelConfigEntity config, CancellationToken ct = default);
    Task UpdateLastConnectedAsync(string channelId, CancellationToken ct = default);
    Task UpdateLastErrorAsync(string channelId, string? error, CancellationToken ct = default);
}

/// <summary>
/// Channel configuration entity
/// </summary>
public class ChannelConfigEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string ChannelId { get; set; }
    public bool Enabled { get; set; }
    public Dictionary<string, string> Config { get; set; } = new();
    public DateTime? LastConnectedAt { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
