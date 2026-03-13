using Botty.Core.Models;
using Botty.Tools.Services;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Text.Json;

namespace Botty.Tools.Repositories;

/// <summary>
/// PostgreSQL repository for tool configuration storage.
/// </summary>
public class ToolConfigRepository : IToolConfigRepository
{
    private readonly string _connectionString;
    private readonly ILogger<ToolConfigRepository> _logger;

    public ToolConfigRepository(
        string connectionString,
        ILogger<ToolConfigRepository> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task<List<ToolConfigValue>> GetAllValuesAsync(string toolId, CancellationToken ct = default)
    {
        var values = new List<ToolConfigValue>();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = new NpgsqlCommand(
            "SELECT id, skill_id, key, value, is_sensitive, updated_at FROM skill_config WHERE skill_id = @toolId",
            connection);
        command.Parameters.AddWithValue("toolId", toolId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            values.Add(new ToolConfigValue
            {
                Id = reader.GetGuid(0),
                ToolId = reader.GetString(1),
                Key = reader.GetString(2),
                Value = reader.IsDBNull(3) ? null : reader.GetString(3),
                IsSensitive = reader.GetBoolean(4),
                UpdatedAt = reader.GetDateTime(5)
            });
        }

        return values;
    }

    public async Task SetValueAsync(ToolConfigValue value, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        // Upsert based on skill_id and key
        await using var command = new NpgsqlCommand(@"
            INSERT INTO skill_config (id, skill_id, key, value, is_sensitive, updated_at)
            VALUES (@id, @toolId, @key, @value, @isSensitive, @updatedAt)
            ON CONFLICT (skill_id, key)
            DO UPDATE SET value = @value, is_sensitive = @isSensitive, updated_at = @updatedAt",
            connection);

        command.Parameters.AddWithValue("id", value.Id);
        command.Parameters.AddWithValue("toolId", value.ToolId);
        command.Parameters.AddWithValue("key", value.Key);
        command.Parameters.AddWithValue("value", (object?)value.Value ?? DBNull.Value);
        command.Parameters.AddWithValue("isSensitive", value.IsSensitive);
        command.Parameters.AddWithValue("updatedAt", value.UpdatedAt);

        await command.ExecuteNonQueryAsync(ct);
        _logger.LogDebug("Set config value {Key} for tool {ToolId}", value.Key, value.ToolId);
    }

    public async Task DeleteValueAsync(string toolId, string key, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = new NpgsqlCommand(
            "DELETE FROM skill_config WHERE skill_id = @toolId AND key = @key",
            connection);
        command.Parameters.AddWithValue("toolId", toolId);
        command.Parameters.AddWithValue("key", key);

        await command.ExecuteNonQueryAsync(ct);
        _logger.LogDebug("Deleted config value {Key} for tool {ToolId}", key, toolId);
    }

    public async Task<bool> HasValueAsync(string toolId, string key, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = new NpgsqlCommand(
            "SELECT EXISTS(SELECT 1 FROM skill_config WHERE skill_id = @toolId AND key = @key)",
            connection);
        command.Parameters.AddWithValue("toolId", toolId);
        command.Parameters.AddWithValue("key", key);

        var result = await command.ExecuteScalarAsync(ct);
        return result is bool exists && exists;
    }

    /// <summary>
    /// Ensures the skill_config table exists.
    /// </summary>
    public async Task EnsureTableExistsAsync(CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = new NpgsqlCommand(@"
            CREATE TABLE IF NOT EXISTS skill_config (
                id UUID PRIMARY KEY,
                skill_id VARCHAR(100) NOT NULL,
                key VARCHAR(100) NOT NULL,
                value TEXT,
                is_sensitive BOOLEAN NOT NULL DEFAULT FALSE,
                updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
                UNIQUE(skill_id, key)
            );
            
            CREATE INDEX IF NOT EXISTS idx_skill_config_skill_id ON skill_config(skill_id);",
            connection);

        await command.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("Ensured skill_config table exists");
    }
}
