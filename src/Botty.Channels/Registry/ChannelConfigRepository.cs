using System.Text.Json;
using Npgsql;
using Microsoft.Extensions.Logging;

namespace Botty.Channels.Registry;

/// <summary>
/// PostgreSQL implementation of IChannelConfigRepository
/// </summary>
public class ChannelConfigRepository : IChannelConfigRepository
{
    private readonly string _connectionString;
    private readonly ILogger<ChannelConfigRepository> _logger;
    
    public ChannelConfigRepository(string connectionString, ILogger<ChannelConfigRepository> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }
    
    public async Task<ChannelConfigEntity?> GetByChannelIdAsync(string channelId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(@"
            SELECT id, channel_id, enabled, config, last_connected_at, last_error, created_at, updated_at
            FROM channel_configs
            WHERE channel_id = @channel_id", conn);
        
        cmd.Parameters.AddWithValue("channel_id", channelId);
        
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return MapEntity(reader);
        }
        
        return null;
    }
    
    public async Task<IEnumerable<ChannelConfigEntity>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(@"
            SELECT id, channel_id, enabled, config, last_connected_at, last_error, created_at, updated_at
            FROM channel_configs
            ORDER BY channel_id", conn);
        
        var entities = new List<ChannelConfigEntity>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            entities.Add(MapEntity(reader));
        }
        
        return entities;
    }
    
    public async Task<ChannelConfigEntity> UpsertAsync(ChannelConfigEntity config, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        var configJson = JsonSerializer.Serialize(config.Config);
        
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO channel_configs (id, channel_id, enabled, config, created_at, updated_at)
            VALUES (@id, @channel_id, @enabled, @config::jsonb, @created_at, @updated_at)
            ON CONFLICT (channel_id) DO UPDATE SET
                enabled = @enabled,
                config = @config::jsonb,
                updated_at = @updated_at
            RETURNING id, channel_id, enabled, config, last_connected_at, last_error, created_at, updated_at", conn);
        
        cmd.Parameters.AddWithValue("id", config.Id);
        cmd.Parameters.AddWithValue("channel_id", config.ChannelId);
        cmd.Parameters.AddWithValue("enabled", config.Enabled);
        cmd.Parameters.AddWithValue("config", configJson);
        cmd.Parameters.AddWithValue("created_at", config.CreatedAt);
        cmd.Parameters.AddWithValue("updated_at", DateTime.UtcNow);
        
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return MapEntity(reader);
        }
        
        throw new InvalidOperationException("Failed to upsert channel config");
    }
    
    public async Task UpdateLastConnectedAsync(string channelId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(@"
            UPDATE channel_configs
            SET last_connected_at = NOW(), last_error = NULL, updated_at = NOW()
            WHERE channel_id = @channel_id", conn);
        
        cmd.Parameters.AddWithValue("channel_id", channelId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
    
    public async Task UpdateLastErrorAsync(string channelId, string? error, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(@"
            UPDATE channel_configs
            SET last_error = @error, updated_at = NOW()
            WHERE channel_id = @channel_id", conn);
        
        cmd.Parameters.AddWithValue("channel_id", channelId);
        cmd.Parameters.AddWithValue("error", error ?? (object)DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }
    
    public async Task EnsureTableExistsAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        
        await using var cmd = new NpgsqlCommand(@"
            CREATE TABLE IF NOT EXISTS channel_configs (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                channel_id VARCHAR(50) NOT NULL UNIQUE,
                enabled BOOLEAN NOT NULL DEFAULT false,
                config JSONB NOT NULL DEFAULT '{}',
                last_connected_at TIMESTAMPTZ,
                last_error TEXT,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            
            CREATE INDEX IF NOT EXISTS idx_channel_configs_channel_id ON channel_configs(channel_id);", conn);
        
        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("Ensured channel_configs table exists");
    }
    
    private static ChannelConfigEntity MapEntity(NpgsqlDataReader reader)
    {
        var configJson = reader.GetString(reader.GetOrdinal("config"));
        var config = JsonSerializer.Deserialize<Dictionary<string, string>>(configJson) ?? new();
        
        return new ChannelConfigEntity
        {
            Id = reader.GetGuid(reader.GetOrdinal("id")),
            ChannelId = reader.GetString(reader.GetOrdinal("channel_id")),
            Enabled = reader.GetBoolean(reader.GetOrdinal("enabled")),
            Config = config,
            LastConnectedAt = reader.IsDBNull(reader.GetOrdinal("last_connected_at")) 
                ? null 
                : reader.GetDateTime(reader.GetOrdinal("last_connected_at")),
            LastError = reader.IsDBNull(reader.GetOrdinal("last_error")) 
                ? null 
                : reader.GetString(reader.GetOrdinal("last_error")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
            UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
        };
    }
}
